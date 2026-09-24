using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Camp.Api.Infrastructure;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.StaffCx;

public record NewTransferRequest(int RegistrationId, int ToSessionId, string? Reason);
public record TransferDecision(string? Note);

/// <summary>
/// F8 (family asks) and C10 (staff decides) for session transfers (FR-42). A request never moves
/// anything by itself: the registration stays where it is until staff approve.
/// </summary>
public sealed class TransferEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        MapFamily(app.MapGroup("/api/transfers").RequireAuthorization(Policies.Family));
        MapStaff(app.MapGroup("/api/admin/transfers").RequireAuthorization(Policies.Cet));
    }

    static void MapFamily(RouteGroupBuilder family)
    {
        // The household's confirmed registrations, whether each can move, and every request so far.
        family.MapGet("", async (CampDbContext db, CurrentUser me, CancellationToken ct) =>
        {
            var regs = await db.Registrations.AsNoTracking()
                .Where(r => r.HouseholdId == me.HouseholdId && r.Status == RegistrationStatus.Confirmed)
                .Include(r => r.Person).Include(r => r.Pool).Include(r => r.Session).ThenInclude(s => s.Program)
                .OrderBy(r => r.Session.StartDate).ThenBy(r => r.Person.FirstName)
                .ToListAsync(ct);
            var programIds = regs.Select(r => r.Session.ProgramId).Distinct().ToList();
            var sessionsPerProgram = await db.Sessions.Where(s => programIds.Contains(s.ProgramId))
                .GroupBy(s => s.ProgramId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var requests = await Requests(db).Where(t => t.HouseholdId == me.HouseholdId).OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
            var pending = requests.Where(t => t.Status == TransferStatus.Pending).Select(t => t.RegistrationId).ToHashSet();
            return Results.Ok(new
            {
                Registrations = regs.Select(r => new
                {
                    r.Id,
                    Participant = r.Person.FullName,
                    Program = r.Session.Program.Name,
                    Session = r.Session.Name,
                    Dates = StaffCx.Dates(r.Session.StartDate, r.Session.EndDate),
                    Pool = r.Pool.Name,
                    HasPendingRequest = pending.Contains(r.Id),
                    CanRequest = !pending.Contains(r.Id) && sessionsPerProgram.GetValueOrDefault(r.Session.ProgramId) > 1,
                }),
                Requests = requests.Select(ToFamilyRow),
            });
        });

        family.MapGet("/{registrationId:int}/options", async (int registrationId, CampDbContext db, CurrentUser me, CancellationToken ct) =>
        {
            var reg = await LoadOwned(db, registrationId, me, ct);
            if (reg is null) return Results.NotFound(new { error = "Registration not found." });
            var sessions = await db.Sessions.AsNoTracking().Include(s => s.Pools)
                .Where(s => s.ProgramId == reg.Session.ProgramId && s.Id != reg.SessionId)
                .OrderBy(s => s.StartDate).ToListAsync(ct);
            var options = new List<TransferCheck>();
            foreach (var s in sessions) options.Add(await TransferService.CheckAsync(db, reg, s, ct));
            var pending = await Requests(db).FirstOrDefaultAsync(t => t.RegistrationId == reg.Id && t.Status == TransferStatus.Pending, ct);
            return Results.Ok(new
            {
                Registration = new
                {
                    reg.Id,
                    Participant = reg.Person.FullName,
                    reg.Person.FirstName,
                    Program = reg.Session.Program.Name,
                    Session = reg.Session.Name,
                    Dates = StaffCx.Dates(reg.Session.StartDate, reg.Session.EndDate),
                    Pool = reg.Pool.Name,
                    Status = TransferService.Label(reg.Status),
                    reg.PriceCents,
                    reg.PaidCents,
                    reg.BalanceCents,
                },
                PendingRequest = pending is null ? null : ToFamilyRow(pending),
                Options = options,
            });
        });

        family.MapPost("", async (NewTransferRequest req, CampDbContext db, CurrentUser me, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            var reason = req.Reason?.Trim();
            if (string.IsNullOrEmpty(reason)) return StaffCx.Invalid("reason", "Tell us why you'd like to switch sessions.");
            if (reason.Length > 500) return StaffCx.Invalid("reason", "Keep the reason under 500 characters.");
            var reg = await LoadOwned(db, req.RegistrationId, me, ct);
            if (reg is null) return Results.NotFound(new { error = "Registration not found." });
            var to = await db.Sessions.AsNoTracking().Include(s => s.Pools).FirstOrDefaultAsync(s => s.Id == req.ToSessionId, ct);
            if (to is null) return StaffCx.Invalid("toSessionId", "Choose a session to move to.");
            if (await db.Set<TransferRequest>().AnyAsync(t => t.RegistrationId == reg.Id && t.Status == TransferStatus.Pending, ct))
                return StaffCx.Conflict($"There's already a transfer request waiting for review for {reg.Person.FirstName}.");
            var check = await TransferService.CheckAsync(db, reg, to, ct);
            if (!check.CanMove) return StaffCx.Conflict(string.Join(" ", check.Blockers));

            var t = new TransferRequest
            {
                RegistrationId = reg.Id,
                HouseholdId = me.HouseholdId,
                FromSessionId = reg.SessionId,
                FromPoolId = reg.PoolId,
                ToSessionId = to.Id,
                Reason = reason,
                RequestedBy = me.Name,
                CreatedAt = clock.UtcNow(),
                Status = TransferStatus.Pending,
                PriceDifferenceCents = check.PriceDifferenceCents,
            };
            db.Set<TransferRequest>().Add(t);
            audit.Record("transfer.requested", "Household", me.HouseholdId,
                $"{me.Name} asked to move {reg.Person.FullName} from {reg.Session.Name} to {check.Session}. Reason: {reason}");
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Two submits at once: the filtered unique index lets only one pending request per registration in.
                return StaffCx.Conflict($"There's already a transfer request waiting for review for {reg.Person.FirstName}.");
            }
            return Results.Ok(new { t.Id });
        });
    }

    static void MapStaff(RouteGroupBuilder admin)
    {
        admin.MapGet("", async (CampDbContext db, string? status, CancellationToken ct) =>
        {
            var all = (await Requests(db).ToListAsync(ct)).OrderBy(t => t.Status != TransferStatus.Pending).ThenByDescending(t => t.CreatedAt).ToList();
            var filter = status?.ToLowerInvariant() switch
            {
                "approved" => (TransferStatus?)TransferStatus.Approved,
                "denied" => TransferStatus.Denied,
                "all" => null,
                _ => TransferStatus.Pending,
            };
            var rows = new List<object>();
            foreach (var t in all.Where(t => filter is null || t.Status == filter))
            {
                var check = t.Status == TransferStatus.Pending ? await Check(db, t, ct) : null;
                rows.Add(ToStaffRow(t, check));
            }
            return Results.Ok(new
            {
                Counts = new
                {
                    Pending = all.Count(t => t.Status == TransferStatus.Pending),
                    Approved = all.Count(t => t.Status == TransferStatus.Approved),
                    Denied = all.Count(t => t.Status == TransferStatus.Denied),
                    All = all.Count,
                },
                Rows = rows,
            });
        });

        admin.MapGet("/{id:int}", async (int id, CampDbContext db, CancellationToken ct) =>
        {
            var t = await Requests(db).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Results.NotFound();
            var check = t.Status == TransferStatus.Pending ? await Check(db, t, ct) : null;
            var fromPool = await db.CapacityPools.AsNoTracking().Where(p => p.Id == t.FromPoolId).Select(p => new { p.Name, p.Capacity, p.Reserved }).SingleAsync(ct);
            var reg = t.Registration;
            return Results.Ok(new
            {
                Request = ToStaffRow(t, check),
                From = new { Pool = fromPool.Name, fromPool.Capacity, fromPool.Reserved },
                Registration = new
                {
                    reg.Id,
                    Status = TransferService.Label(reg.Status),
                    reg.PriceCents,
                    reg.DiscountCents,
                    reg.PaidCents,
                    reg.BalanceCents,
                    Payment = StaffCx.PaymentState(reg.Status, reg.BalanceCents, reg.PaidCents, reg.Order?.Installments.Select(i => i.Status) ?? []),
                },
                Check = check,
            });
        });

        admin.MapPost("/{id:int}/approve", async (int id, TransferDecision req, CampDbContext db, IPaymentGateway gateway, IAuditLog audit, StaffUser staff, CancellationToken ct, TimeProvider clock) =>
        {
            if (req.Note?.Length > 500) return StaffCx.Invalid("note", "Notes are limited to 500 characters.");
            var outcome = await new TransferService(db, gateway, audit, clock).ApproveAsync(id, staff.Actor, req.Note, ct);
            return outcome.StatusCode switch
            {
                200 => Results.Ok(new { outcome.RefundCents }),
                404 => Results.NotFound(new { error = outcome.Error }),
                409 => StaffCx.Conflict(outcome.Error!),
                _ => Results.Json(new { error = outcome.Error }, statusCode: outcome.StatusCode),
            };
        });

        admin.MapPost("/{id:int}/deny", async (int id, TransferDecision req, CampDbContext db, IAuditLog audit, StaffUser staff, TimeProvider clock, CancellationToken ct) =>
        {
            var note = req.Note?.Trim();
            if (string.IsNullOrEmpty(note)) return StaffCx.Invalid("note", "Give the family a reason. They'll see it on their request.");
            if (note.Length > 500) return StaffCx.Invalid("note", "Notes are limited to 500 characters.");
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // The decision is a conditional update: of two decisions racing, only one changes the row.
            var now = (DateTime?)clock.UtcNow();
            var won = await db.Set<TransferRequest>().Where(x => x.Id == id && x.Status == TransferStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, TransferStatus.Denied)
                    .SetProperty(x => x.DecidedBy, staff.Actor)
                    .SetProperty(x => x.DecidedAt, now)
                    .SetProperty(x => x.DecisionNote, note), ct);
            var t = await db.Set<TransferRequest>().AsNoTracking().Include(x => x.Registration).ThenInclude(r => r.Person).Include(x => x.ToSession).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Results.NotFound();
            if (won == 0) return StaffCx.Conflict(TransferService.AlreadyDecided(t));
            audit.Record("transfer.denied", "Household", t.HouseholdId,
                $"Denied moving {t.Registration.Person.FullName} to {t.ToSession.Name}. The registration stays as it was. Reason: {note}");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok();
        });
    }

    static IQueryable<TransferRequest> Requests(CampDbContext db) => db.Set<TransferRequest>().AsNoTracking()
        .Include(t => t.Registration).ThenInclude(r => r.Person)
        .Include(t => t.Registration).ThenInclude(r => r.Order!).ThenInclude(o => o.Installments)
        .Include(t => t.FromSession).ThenInclude(s => s.Program)
        .Include(t => t.ToSession).ThenInclude(s => s.Pools);

    /// <summary>Only the signed-in household's registrations are visible; anyone else's reads as not found.</summary>
    static Task<Registration?> LoadOwned(CampDbContext db, int registrationId, CurrentUser me, CancellationToken ct) =>
        db.Registrations.AsNoTracking()
            .Include(r => r.Person).Include(r => r.Pool).Include(r => r.Session).ThenInclude(s => s.Program)
            .FirstOrDefaultAsync(r => r.Id == registrationId && r.HouseholdId == me.HouseholdId, ct);

    static async Task<TransferCheck> Check(CampDbContext db, TransferRequest t, CancellationToken ct)
    {
        var reg = await db.Registrations.AsNoTracking().Include(r => r.Person).Include(r => r.Session).SingleAsync(r => r.Id == t.RegistrationId, ct);
        return await TransferService.CheckAsync(db, reg, t.ToSession, ct);
    }

    static object ToFamilyRow(TransferRequest t) => new
    {
        t.Id,
        t.RegistrationId,
        Participant = t.Registration.Person.FullName,
        Program = t.FromSession.Program.Name,
        From = StaffCx.Dates(t.FromSession.StartDate, t.FromSession.EndDate),
        To = StaffCx.Dates(t.ToSession.StartDate, t.ToSession.EndDate),
        t.Reason,
        t.CreatedAt,
        Status = t.Status.ToString(),
        t.DecisionNote,
        t.DecidedAt,
        t.PriceDifferenceCents,
        t.RefundCents,
    };

    static object ToStaffRow(TransferRequest t, TransferCheck? check) => new
    {
        t.Id,
        t.RegistrationId,
        t.HouseholdId,
        Participant = t.Registration.Person.FullName,
        Program = t.FromSession.Program.Name,
        FromSession = t.FromSession.Name,
        FromDates = StaffCx.Dates(t.FromSession.StartDate, t.FromSession.EndDate),
        ToSession = t.ToSession.Name,
        ToDates = StaffCx.Dates(t.ToSession.StartDate, t.ToSession.EndDate),
        t.Reason,
        t.RequestedBy,
        t.CreatedAt,
        Status = t.Status.ToString(),
        t.DecidedBy,
        t.DecidedAt,
        t.DecisionNote,
        PriceDifferenceCents = check?.PriceDifferenceCents ?? t.PriceDifferenceCents ?? 0,
        t.RefundCents,
        ToPool = check?.Pool,
        ToCapacity = check?.Capacity,
        ToReserved = check?.Reserved,
        Blocked = check is { CanMove: false },
        Blockers = check?.Blockers ?? [],
    };
}
