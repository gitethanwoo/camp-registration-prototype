using System.Text.Json;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Camp.Api.Features.Setup;
using Camp.Api.Infrastructure;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features;

public record CancelRequest(string Reason, int RefundCents);
public record CapacityRequest(int Capacity);
public record OfferRequest(DateTime Deadline);
public record RemoveRequest(string Reason);

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(Policies.Staff);

        // Scope switcher: Ministry ▸ Program ▸ Session
        admin.MapGet("/scope", async (CampDbContext db) =>
        {
            var ministries = await db.Ministries.Include(m => m.Programs).ThenInclude(p => p.Sessions).AsNoTracking().ToListAsync();
            return ministries.Where(m => m.Programs.Count > 0).Select(m => new
            {
                m.Id,
                m.Code,
                m.Name,
                Programs = m.Programs.Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Slug,
                    Type = p.Type.ToString(),
                    Sessions = p.Sessions.OrderBy(s => s.StartDate).Select(s => new { s.Id, s.Name, s.StartDate, s.EndDate }),
                }),
            });
        });

        // O1-lite · session KPIs + needs-attention (unique people, reasons overlap)
        admin.MapGet("/sessions/{id:int}/overview", async (int id, CampDbContext db, TimeProvider clock) =>
        {
            var s = await db.Sessions.Include(x => x.Program).ThenInclude(p => p.Waivers).Include(x => x.Pools).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return Results.NotFound();
            var regs = await db.Registrations.Where(r => r.SessionId == id && r.Status == RegistrationStatus.Confirmed)
                .Include(r => r.WaiverAcceptances).AsNoTracking().ToListAsync();
            var waitlist = await db.WaitlistEntries.Where(w => w.Pool.SessionId == id && (w.Status == WaitlistStatus.Waiting || w.Status == WaitlistStatus.Offered))
                .GroupBy(w => w.PoolId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);

            var waiverCount = s.Program.Waivers.Count;
            var healthIncomplete = regs.Where(r => r.HealthStatus != FormStatus.Complete && r.HealthStatus != FormStatus.NotRequired).Select(r => r.Id).ToHashSet();
            var waiverMissing = regs.Where(r => r.WaiverAcceptances.Count < waiverCount).Select(r => r.Id).ToHashSet();
            var balanceDue = regs.Where(r => r.BalanceCents > 0).Select(r => r.Id).ToHashSet();
            var attention = healthIncomplete.Union(waiverMissing).Union(balanceDue).ToHashSet();
            var today = clock.UtcNow().Date;
            var collected = await db.PaymentOperations.Where(o => o.Succeeded && db.Orders.Any(x => x.Id == o.OrderId && x.SessionId == id))
                .SumAsync(o => o.Kind == PaymentKind.Charge ? o.AmountCents : o.Kind == PaymentKind.Refund ? -o.AmountCents : 0);

            return Results.Ok(new
            {
                Session = new { s.Id, s.Name, s.StartDate, s.EndDate, Program = s.Program.Name, HealthMechanism = s.Program.HealthMechanism.ToString() },
                Capacity = s.Pools.Sum(p => p.Capacity),
                Registered = regs.Count,
                Held = s.Pools.Sum(p => p.Reserved) - regs.Count, // pending payments + outstanding waitlist offers
                Waitlisted = waitlist.Values.Sum(),
                Ready = regs.Count - attention.Count,
                NeedsAttention = attention.Count,
                Breakdown = new { Health = healthIncomplete.Count, Waivers = waiverMissing.Count, Balance = balanceDue.Count },
                NewToday = regs.Count(r => r.CreatedAt >= today),
                CollectedCents = collected,
                OutstandingCents = regs.Sum(r => r.BalanceCents),
                Pools = s.Pools.OrderBy(p => p.SortOrder).Select(p => GuestEndpoints.ToAvailability(p, waitlist)),
            });
        });

        // C4 · Registration dashboard. With no session, searches every ministry (FR-61).
        admin.MapGet("/registrations", async (CampDbContext db, int? sessionId, string? status, string? q, string? attention,
            int? poolId, string? sort, string? dir, int page = 1, int pageSize = 25) =>
        {
            var query = db.Registrations.AsNoTracking().AsQueryable();
            if (sessionId is not null) query = query.Where(r => r.SessionId == sessionId);
            if (poolId is not null) query = query.Where(r => r.PoolId == poolId);
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<RegistrationStatus>(status, out var st)) query = query.Where(r => r.Status == st);
            else query = query.Where(r => r.Status != RegistrationStatus.Cancelled || status == "all");
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                var digits = new string(term.Where(char.IsDigit).ToArray());
                query = query.Where(r =>
                    (r.Person.FirstName + " " + r.Person.LastName).Contains(term) ||
                    db.Households.Any(h => h.Id == r.HouseholdId && (h.Email.Contains(term) || h.Name.Contains(term) ||
                        (digits.Length >= 4 && h.Phone.Replace("(", "").Replace(")", "").Replace("-", "").Replace(" ", "").Contains(digits)))) ||
                    r.Order!.ConfirmationCode == term);
            }
            query = attention switch
            {
                "balance" => query.Where(r => r.PriceCents - r.DiscountCents - r.PaidCents > 0),
                "health" => query.Where(r => r.HealthStatus == FormStatus.Incomplete || r.HealthStatus == FormStatus.Missing),
                "waivers" => query.Where(r => r.WaiverAcceptances.Count < db.WaiverTemplates.Count(w => w.ProgramId == r.Session.ProgramId)),
                _ => query,
            };

            var desc = dir == "desc";
            query = sort switch
            {
                "participant" => desc ? query.OrderByDescending(r => r.Person.LastName).ThenByDescending(r => r.Person.FirstName) : query.OrderBy(r => r.Person.LastName).ThenBy(r => r.Person.FirstName),
                "grade" => desc ? query.OrderByDescending(r => r.Grade) : query.OrderBy(r => r.Grade),
                "balance" => desc ? query.OrderByDescending(r => r.PriceCents - r.DiscountCents - r.PaidCents) : query.OrderBy(r => r.PriceCents - r.DiscountCents - r.PaidCents),
                "status" => desc ? query.OrderByDescending(r => r.Status) : query.OrderBy(r => r.Status),
                _ => desc || sort is null ? query.OrderByDescending(r => r.CreatedAt) : query.OrderBy(r => r.CreatedAt),
            };

            var total = await query.CountAsync();
            var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).Select(r => new
            {
                r.Id,
                Participant = r.Person.FirstName + " " + r.Person.LastName,
                r.Grade,
                Guardian = db.People.Where(p => p.HouseholdId == r.HouseholdId && p.IsAdult).OrderBy(p => p.Id).Select(p => p.FirstName + " " + p.LastName).FirstOrDefault(),
                Email = db.Households.Where(h => h.Id == r.HouseholdId).Select(h => h.Email).FirstOrDefault(),
                Program = r.Session.Program.Name,
                Session = r.Session.Name,
                Pool = r.Pool.Name,
                Status = r.Status.ToString(),
                Balance = r.Status == RegistrationStatus.Cancelled ? 0 : r.PriceCents - r.DiscountCents - r.PaidCents,
                Health = r.HealthStatus.ToString(),
                HealthMechanism = r.Session.Program.HealthMechanism.ToString(),
                WaiversSigned = r.WaiverAcceptances.Count,
                WaiversRequired = db.WaiverTemplates.Count(w => w.ProgramId == r.Session.ProgramId),
                r.CreatedAt,
                ConfirmationCode = r.Order!.ConfirmationCode,
            }).ToListAsync();
            return new { Total = total, Page = page, PageSize = pageSize, Rows = rows };
        });

        // C3 · Registration detail: timeline, documents, payments with refund on the same screen, audit.
        admin.MapGet("/registrations/{id:int}", async (int id, CampDbContext db, TimeProvider clock) =>
        {
            var r = await db.Registrations.Include(x => x.Person).Include(x => x.Pool)
                .Include(x => x.Session).ThenInclude(s => s.Program).ThenInclude(p => p.Waivers)
                .Include(x => x.WaiverAcceptances).ThenInclude(w => w.WaiverTemplate)
                .Include(x => x.Order).ThenInclude(o => o!.Operations)
                .Include(x => x.Order).ThenInclude(o => o!.Installments)
                .AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (r is null) return Results.NotFound();
            var h = await db.Households.Include(x => x.Members).AsNoTracking().SingleAsync(x => x.Id == r.HouseholdId);
            // Registrations compensated after a declined card never existed as far as staff are concerned.
            var siblings = await db.Registrations.Where(x => x.HouseholdId == h.Id && x.Id != id && x.Order!.Status != OrderStatus.Declined).Include(x => x.Person).Include(x => x.Session).ThenInclude(s => s.Program).AsNoTracking()
                .Select(x => new { x.Id, Participant = x.Person.FirstName, Program = x.Session.Program.Name, Session = x.Session.Name, Status = x.Status.ToString() }).ToListAsync();
            var code = r.Order?.ConfirmationCode ?? "";
            var regKey = id.ToString(CultureInfo.InvariantCulture);
            var orderKey = r.OrderId?.ToString(CultureInfo.InvariantCulture);
            var audit = await db.AuditEvents.Where(a => (a.EntityType == "Registration" && a.EntityId == regKey) || (a.EntityType == "PaymentOrder" && a.EntityId == orderKey))
                .OrderByDescending(a => a.Id).AsNoTracking().ToListAsync();
            var messages = await db.OutboxEvents.Where(e => e.AggregateId == code || e.AggregateId == "reg-" + id).OrderBy(e => e.Id).AsNoTracking().ToListAsync();

            return Results.Ok(new
            {
                r.Id,
                Status = r.Status.ToString(),
                r.Grade,
                GradeLabel = Eligibility.GradeLabel(r.Grade),
                r.CreatedAt,
                Participant = new { r.Person.Id, r.Person.FirstName, r.Person.LastName, r.Person.DateOfBirth, Gender = r.Person.Gender.ToString() },
                // No allergies, dietary or ADA needs here: they are health details, read only through
                // GET /api/access/registrations/{id}/health, which enforces K9/K11 access and audits the view.
                Household = new
                {
                    h.Id,
                    h.Name,
                    h.Email,
                    h.Phone,
                    h.City,
                    h.SalesforceId,
                    Adults = h.Members.Where(m => m.IsAdult).Select(m => new { m.FirstName, m.LastName, m.Role, m.Email }),
                    OtherRegistrations = siblings,
                },
                Program = new { r.Session.Program.Name, r.Session.Program.Slug, HealthMechanism = r.Session.Program.HealthMechanism.ToString() },
                Session = new { r.Session.Id, r.Session.Name, r.Session.StartDate, r.Session.EndDate },
                Pool = r.Pool.Name,
                // Registration answers come from GET /api/admin/forms/registrations/{id}/answers, which withholds
                // K6 health questions (medication) from staff without health access and audits the view.
                // Embedded health data is intentionally not returned to CET by default (FR-112 access scope).
                HealthStatus = r.HealthStatus.ToString(),
                HealthOnFile = r.HealthJson is not null,
                Waivers = r.Session.Program.Waivers.Select(w =>
                {
                    var a = r.WaiverAcceptances.FirstOrDefault(x => x.WaiverTemplateId == w.Id);
                    return new { w.Title, CurrentVersion = w.Version, Status = a is null ? "Missing" : "Complete", AcceptedVersion = a?.Version, a?.SignerName, a?.AcceptedAt };
                }),
                Money = new
                {
                    r.PriceCents,
                    r.DiscountCents,
                    r.PaidCents,
                    r.BalanceCents,
                    ConfirmationCode = code,
                    Option = r.Order?.PaymentOption.ToString(),
                    BalanceDueDate = r.Session.BalanceDueDate,
                    OrderParticipants = r.Order is null ? 0 : await db.Registrations.CountAsync(x => x.OrderId == r.OrderId),
                    Operations = r.Order?.Operations.OrderBy(o => o.CreatedAt).Select(o => new { o.Id, Kind = o.Kind.ToString(), o.AmountCents, o.Succeeded, o.ProcessorRef, o.CardLast4, o.Reason, o.CreatedAt }),
                    Installments = r.Order?.Installments.OrderBy(i => i.Sequence).Select(i => new { i.Sequence, i.DueDate, i.AmountCents, Status = i.Status.ToString() }),
                },
                // K4: the session's cancellation and refund table.
                Cancellation = r.Status == RegistrationStatus.Confirmed
                    ? RefundPolicy.Quote(r, await RefundPolicy.TiersForAsync(db, r.SessionId), clock.Today())
                    : null,
                Audit = audit.Select(a => new { a.Actor, a.Action, a.Detail, a.CreatedAt }),
                Messages = messages.Select(m => new { m.Type, m.Target, m.CreatedAt, m.ProcessedAt }),
            });
        });

        // Cancel under the time-based policy, refund on the same screen (FR-40, FR-49), release the seat.
        // The waitlist is not auto-promoted: the freed seat shows up for an admin to offer (FR-69).
        admin.MapPost("/registrations/{id:int}/cancel", async (int id, CancelRequest req, CampDbContext db, IPaymentGateway gateway, IAuditLog audit, TimeProvider clock) =>
        {
            if (string.IsNullOrWhiteSpace(req.Reason)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["A reason is required."] });
            var r = await db.Registrations.Include(x => x.Person).Include(x => x.Session).Include(x => x.Order).ThenInclude(o => o!.Operations).FirstOrDefaultAsync(x => x.Id == id);
            if (r is null) return Results.NotFound();
            if (r.Status != RegistrationStatus.Confirmed) return Results.Conflict(new { error = "Only confirmed registrations can be cancelled." });
            if (req.RefundCents < 0 || req.RefundCents > r.PaidCents) return Results.ValidationProblem(new Dictionary<string, string[]> { ["refundCents"] = [$"Refund must be between $0 and {CheckoutService.Money(r.PaidCents)}."] });

            await using var tx = await db.Database.BeginTransactionAsync();
            if (req.RefundCents > 0)
            {
                var charge = r.Order!.Operations.Where(o => o.Kind == PaymentKind.Charge && o.Succeeded).OrderBy(o => o.CreatedAt).Last();
                var result = await gateway.RefundAsync(charge.ProcessorRef, req.RefundCents);
                db.PaymentOperations.Add(new PaymentOperation { OrderId = r.Order.Id, Kind = PaymentKind.Refund, AmountCents = req.RefundCents, Succeeded = result.Succeeded, ProcessorRef = result.ProcessorRef, CardLast4 = charge.CardLast4, Reason = req.Reason, CreatedAt = clock.UtcNow() });
                r.PaidCents -= req.RefundCents;
            }
            r.Status = RegistrationStatus.Cancelled;
            await db.CapacityPools.Where(p => p.Id == r.PoolId && p.Reserved > 0).ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved - 1));
            audit.Record("registration.cancelled", "Registration", id, $"Cancelled {r.Person.FullName}. Refund {CheckoutService.Money(req.RefundCents)}. Reason: {req.Reason}");
            db.OutboxEvents.Add(new OutboxEvent { Type = "RegistrationCancelled", Target = "HubSpot", AggregateId = "reg-" + id, PayloadJson = JsonSerializer.Serialize(new { id, req.RefundCents }), CreatedAt = clock.UtcNow() });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok();
        });

        // K3 · Session editor + capacity pools
        admin.MapGet("/sessions/{id:int}", async (int id, CampDbContext db) =>
        {
            var s = await db.Sessions.Include(x => x.Program).ThenInclude(p => p.Ministry).Include(x => x.Pools).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return Results.NotFound();
            var waitlist = await db.WaitlistEntries.Where(w => w.Pool.SessionId == id && (w.Status == WaitlistStatus.Waiting || w.Status == WaitlistStatus.Offered))
                .GroupBy(w => w.PoolId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
            return Results.Ok(new
            {
                s.Id,
                s.Name,
                s.StartDate,
                s.EndDate,
                s.PriceCents,
                s.DepositCents,
                s.PlanInstallments,
                s.BalanceDueDate,
                s.WaitlistMode,
                Program = new { s.Program.Name, Ministry = s.Program.Ministry.Name, s.Program.Location, HealthMechanism = s.Program.HealthMechanism.ToString(), Type = s.Program.Type.ToString() },
                Pools = s.Pools.OrderBy(p => p.SortOrder).Select(p => new
                {
                    p.Id,
                    p.Name,
                    Gender = p.Gender?.ToString(),
                    p.GradeMin,
                    p.GradeMax,
                    p.Capacity,
                    p.Reserved,
                    Remaining = p.Capacity - p.Reserved,
                    Waitlisted = waitlist.GetValueOrDefault(p.Id),
                }),
            });
        });

        admin.MapPut("/pools/{id:int}", async (int id, CapacityRequest req, CampDbContext db, IAuditLog audit) =>
        {
            var pool = await db.CapacityPools.FirstOrDefaultAsync(p => p.Id == id);
            if (pool is null) return Results.NotFound();
            // Conditional update so capacity can't drop below seats taken by a registration in flight.
            var updated = await db.CapacityPools.Where(p => p.Id == id && p.Reserved <= req.Capacity)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Capacity, req.Capacity));
            if (updated == 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["capacity"] = [$"{pool.Name} already has {pool.Reserved} seats taken; capacity can't go below that."] });
            audit.Record("capacity.changed", "CapacityPool", id, $"{pool.Name}: {pool.Capacity} → {req.Capacity}");
            await db.SaveChangesAsync();
            return Results.Ok();
        }).RequireAuthorization(Policies.Admin); // K3: capacity is an admin change, like session setup's pool editor

        // C7 · Waitlist management
        admin.MapGet("/sessions/{id:int}/waitlist", async (int id, CampDbContext db) =>
        {
            var pools = await db.CapacityPools.Where(p => p.SessionId == id).OrderBy(p => p.SortOrder).AsNoTracking().ToListAsync();
            var entries = await db.WaitlistEntries.Where(w => w.Pool.SessionId == id && w.Status != WaitlistStatus.Removed)
                .Include(w => w.Person).OrderBy(w => w.PoolId).ThenBy(w => w.Position).AsNoTracking().ToListAsync();
            var households = await db.Households.Where(h => entries.Select(e => e.HouseholdId).Contains(h.Id)).Include(h => h.Members).AsNoTracking().ToDictionaryAsync(h => h.Id);
            var session = await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == id);
            return new
            {
                Pools = pools.Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Capacity,
                    p.Reserved,
                    Remaining = p.Capacity - p.Reserved,
                    Entries = entries.Where(e => e.PoolId == p.Id).Select(e => new
                    {
                        e.Id,
                        e.Position,
                        Participant = e.Person.FullName,
                        Grade = Eligibility.GradeFor(e.Person.DateOfBirth, session.StartDate),
                        Guardian = households[e.HouseholdId].Members.Where(m => m.IsAdult).OrderBy(m => m.Id).Select(m => m.FullName).FirstOrDefault(),
                        Status = e.Status.ToString(),
                        e.OfferExpiresAt,
                        e.CreatedAt,
                    }),
                }).Where(p => p.Entries.Any() || p.Remaining == 0),
            };
        });

        admin.MapPost("/waitlist/{id:int}/offer", async (int id, OfferRequest req, CampDbContext db, IAuditLog audit, TimeProvider clock) =>
        {
            var entry = await db.WaitlistEntries.Include(w => w.Person).Include(w => w.Pool).FirstOrDefaultAsync(w => w.Id == id);
            if (entry is null) return Results.NotFound();
            if (entry.Status != WaitlistStatus.Waiting) return Results.Conflict(new { error = "Only waiting entries can be offered a spot." });
            if (req.Deadline <= clock.UtcNow()) return Results.ValidationProblem(new Dictionary<string, string[]> { ["deadline"] = ["Deadline must be in the future."] });

            await using var tx = await db.Database.BeginTransactionAsync();
            // An offer holds a real seat, so it can't be offered twice or into a full pool.
            var claimed = await db.CapacityPools.Where(p => p.Id == entry.PoolId && p.Reserved < p.Capacity)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved + 1));
            if (claimed == 0) return Results.Conflict(new { error = $"{entry.Pool.Name} has no open spots. Raise capacity or wait for a cancellation." });
            entry.Status = WaitlistStatus.Offered;
            entry.OfferExpiresAt = req.Deadline;
            audit.Record("waitlist.offered", "WaitlistEntry", id, $"Offered {entry.Pool.Name} spot to {entry.Person.FullName} (#{entry.Position}); respond by {req.Deadline:MMM d, h:mm tt} UTC.");
            db.OutboxEvents.Add(new OutboxEvent { Type = "WaitlistOfferSent", Target = "HubSpot", AggregateId = "wl-" + id, PayloadJson = JsonSerializer.Serialize(new { id, req.Deadline }), CreatedAt = clock.UtcNow() });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok();
        });

        admin.MapPost("/waitlist/{id:int}/remove", async (int id, RemoveRequest req, CampDbContext db, IAuditLog audit) =>
        {
            var entry = await db.WaitlistEntries.Include(w => w.Person).FirstOrDefaultAsync(w => w.Id == id);
            if (entry is null) return Results.NotFound();
            await using var tx = await db.Database.BeginTransactionAsync();
            if (entry.Status == WaitlistStatus.Offered)
                await db.CapacityPools.Where(p => p.Id == entry.PoolId && p.Reserved > 0).ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved - 1));
            entry.Status = WaitlistStatus.Removed;
            audit.Record("waitlist.removed", "WaitlistEntry", id, $"Removed {entry.Person.FullName}: {req.Reason}");
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok();
        });

        admin.MapGet("/audit", async (CampDbContext db, int take = 50) =>
            await db.AuditEvents.OrderByDescending(a => a.Id).Take(take).AsNoTracking().ToListAsync());
    }
}
