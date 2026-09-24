using System.Text.Json;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Camp.Api.Infrastructure;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Groups;

/// <summary>
/// R8 group roster entry, G2 leader tracker (signed-in leader only), and G1 secure-link forms
/// (anonymous, identified only by the attendee's token).
/// </summary>
public sealed class GroupsEndpoints : IEndpointModule
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder app)
    {
        var leader = app.MapGroup("/api/groups").RequireAuthorization(Policies.Family);

        leader.MapGet("", async (CampDbContext db, CurrentUser me) =>
        {
            var groups = await Groups(db).Where(g => g.LeaderHouseholdId == me.HouseholdId)
                .OrderByDescending(g => g.CreatedAt).AsNoTracking().ToListAsync();
            return groups.Select(g => new
            {
                g.Id,
                g.Name,
                Status = g.Status.ToString(),
                Program = g.Session.Program.Name,
                Session = g.Session.Name,
                g.Session.StartDate,
                g.Session.EndDate,
                Counts = Counts(g),
            });
        });

        // Everything R8 needs before a group exists: the cohort session, its price and open seats.
        leader.MapGet("/start/{sessionId:int}", async (int sessionId, CampDbContext db, CurrentUser me) =>
        {
            var s = await db.Sessions.Include(x => x.Program).ThenInclude(p => p.Ministry).Include(x => x.Program).ThenInclude(p => p.Waivers)
                .Include(x => x.Pools).AsNoTracking().FirstOrDefaultAsync(x => x.Id == sessionId && x.Program.Type == ProgramType.Cohort && x.Program.IsPublished);
            if (s is null) return Results.NotFound();
            var mine = await db.Set<GroupRegistration>().Where(g => g.SessionId == sessionId && g.LeaderHouseholdId == me.HouseholdId)
                .Select(g => new { g.Id, g.Name, Status = g.Status.ToString(), Attendees = g.Attendees.Count(a => a.IsActive) }).ToListAsync();
            return Results.Ok(new
            {
                Session = SessionDto(s),
                Program = new { s.Program.Name, s.Program.Slug, Ministry = s.Program.Ministry.Code },
                Remaining = s.Pools.Sum(p => p.Capacity - p.Reserved),
                Waivers = s.Program.Waivers.Select(w => w.Title),
                LeaderName = me.Name,
                ExistingGroups = mine,
            });
        });

        leader.MapPost("", async (CreateGroupRequest req, CampDbContext db, CurrentUser me, IAuditLog audit, TimeProvider clock) =>
        {
            var s = await db.Sessions.Include(x => x.Program).FirstOrDefaultAsync(x => x.Id == req.SessionId && x.Program.Type == ProgramType.Cohort && x.Program.IsPublished);
            if (s is null) return Results.NotFound();
            try
            {
                var roster = GroupService.CleanRoster(req.Attendees);
                var group = new GroupRegistration
                {
                    SessionId = s.Id,
                    LeaderHouseholdId = me.HouseholdId,
                    LeaderName = me.Name,
                    Name = GroupName(req.Name, me.Name),
                    Status = GroupStatus.Draft,
                    CreatedAt = clock.UtcNow(),
                };
                GroupService.ReplaceRoster(group, roster);
                db.Add(group);
                await db.SaveChangesAsync();
                audit.Record("group.draft_saved", "GroupRegistration", group.Id, $"{group.Name}: draft roster of {roster.Count} for {s.Program.Name}.");
                await db.SaveChangesAsync();
                return Results.Ok(new { group.Id });
            }
            catch (GroupValidationException e) { return Results.ValidationProblem(e.Errors); }
        });

        leader.MapGet("/{id:int}", async (int id, CampDbContext db, IPaymentGateway gateway, IAuditLog audit, CurrentUser me, CancellationToken ct, TimeProvider clock) =>
        {
            // Settles a payment that was interrupted and then finished by the reconciler.
            await new GroupService(db, gateway, audit, clock).ReconcileAsync(me.HouseholdId, id, ct);
            var g = await Groups(db).Include(x => x.Order).ThenInclude(o => o!.Operations)
                .AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.LeaderHouseholdId == me.HouseholdId, ct);
            return g is null ? Results.NotFound() : Results.Ok(Detail(g));
        });

        leader.MapPut("/{id:int}/roster", async (int id, RosterRequest req, CampDbContext db, CurrentUser me, IAuditLog audit) =>
        {
            var g = await db.Set<GroupRegistration>().Include(x => x.Attendees)
                .FirstOrDefaultAsync(x => x.Id == id && x.LeaderHouseholdId == me.HouseholdId);
            if (g is null) return Results.NotFound();
            if (g.Status != GroupStatus.Draft || g.OrderId is not null)
                return Results.Conflict(new { error = "This group is already paid for, so its roster is locked. Add a missing email from the tracker." });
            try
            {
                var roster = GroupService.CleanRoster(req.Attendees);
                g.Name = GroupName(req.Name, g.LeaderName);
                GroupService.ReplaceRoster(g, roster);
                audit.Record("group.draft_saved", "GroupRegistration", g.Id, $"{g.Name}: draft roster of {roster.Count}.");
                await db.SaveChangesAsync();
                return Results.Ok(new { g.Id });
            }
            catch (GroupValidationException e) { return Results.ValidationProblem(e.Errors); }
        });

        leader.MapPost("/{id:int}/checkout", async (int id, GroupCheckoutRequest req, CampDbContext db, IPaymentGateway gateway, IAuditLog audit, CurrentUser me, CancellationToken ct, TimeProvider clock) =>
        {
            try
            {
                var result = await new GroupService(db, gateway, audit, clock).CheckoutAsync(me.HouseholdId, id, req, ct);
                return result.Status == "Declined"
                    ? Results.Json(result, statusCode: StatusCodes.Status402PaymentRequired)
                    : Results.Ok(result);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (GroupValidationException e) { return Results.ValidationProblem(e.Errors); }
        });

        leader.MapPost("/{id:int}/resend", async (int id, ResendRequest req, CampDbContext db, IPaymentGateway gateway, IAuditLog audit, CurrentUser me, TimeProvider clock) =>
        {
            var g = await LeaderGroup(db, id, me);
            if (g is null) return Results.NotFound();
            if (g.Status != GroupStatus.Confirmed) return Results.Conflict(new { error = "Links go out once the group is paid for." });
            var service = new GroupService(db, gateway, audit, clock);
            var sent = new List<SentLink>();
            var skipped = new List<string>();
            foreach (var a in g.Attendees.Where(a => req.AttendeeIds.Contains(a.Id)).OrderBy(a => a.SortOrder))
            {
                if (!a.IsActive) skipped.Add($"{a.Name} has withdrawn.");
                else if (a.Email is null) skipped.Add($"{a.Name} has no email yet. Add one to send their link.");
                else sent.Add(service.IssueLink(g, a));
            }
            if (sent.Count > 0)
                audit.Record("group.links_resent", "GroupRegistration", g.Id, $"Secure links resent to {string.Join(", ", sent.Select(s => s.Name))}.");
            await db.SaveChangesAsync();
            return Results.Ok(new ResendResult(sent, skipped));
        });

        leader.MapPut("/{id:int}/attendees/{aid:int}/email", async (int id, int aid, EmailRequest req, CampDbContext db, IPaymentGateway gateway, IAuditLog audit, CurrentUser me, TimeProvider clock) =>
        {
            var g = await LeaderGroup(db, id, me);
            var a = g?.Attendees.FirstOrDefault(x => x.Id == aid);
            if (g is null || a is null) return Results.NotFound();
            if (!a.IsActive) return Results.Conflict(new { error = $"{a.Name} has withdrawn." });
            var email = req.Email?.Trim().ToLowerInvariant() ?? "";
            if (!GroupService.IsEmail(email)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = [$"{req.Email} doesn't look like an email address."] });
            if (g.Attendees.Any(x => x.Id != aid && x.Email == email)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = [$"{email} already belongs to another attendee in this group."] });
            a.Email = email;
            audit.Record("group.attendee_email_set", "GroupAttendee", a.Id, $"Email for {a.Name} set by {g.LeaderName}.");
            // Once the group is paid for, a new email means a new link right away.
            var link = g.Status == GroupStatus.Confirmed && a.FormStatus != FormStatus.Complete ? new GroupService(db, gateway, audit, clock).IssueLink(g, a) : null;
            await db.SaveChangesAsync();
            return Results.Ok(new { a.Id, a.Email, Link = link });
        });

        leader.MapPost("/{id:int}/attendees/{aid:int}/withdrawal/approve", async (int id, int aid, CampDbContext db, IPaymentGateway gateway, IAuditLog audit, CurrentUser me, CancellationToken ct, TimeProvider clock) =>
        {
            var g = await LeaderGroup(db, id, me);
            var a = g?.Attendees.FirstOrDefault(x => x.Id == aid);
            if (g is null || a is null) return Results.NotFound();
            try
            {
                await new GroupService(db, gateway, audit, clock).ApproveWithdrawalAsync(g, a, ct);
                return Results.NoContent();
            }
            catch (GroupValidationException e) { return Results.ValidationProblem(e.Errors); }
        });

        leader.MapPost("/{id:int}/attendees/{aid:int}/withdrawal/decline", async (int id, int aid, CampDbContext db, IPaymentGateway gateway, IAuditLog audit, CurrentUser me, CancellationToken ct, TimeProvider clock) =>
        {
            var g = await LeaderGroup(db, id, me);
            var a = g?.Attendees.FirstOrDefault(x => x.Id == aid);
            if (g is null || a is null) return Results.NotFound();
            try
            {
                await new GroupService(db, gateway, audit, clock).DeclineWithdrawalAsync(g, a, ct);
                return Results.NoContent();
            }
            catch (GroupValidationException e) { return Results.ValidationProblem(e.Errors); }
        });

        // ── G1 · secure link. No account; the token identifies one attendee and nothing else. ──
        var link = app.MapGroup("/api/group-links/{token}").AllowAnonymous();

        link.MapGet("", async (string token, CampDbContext db, IPaymentGateway gateway, IAuditLog audit, CancellationToken ct, TimeProvider clock) =>
        {
            var a = await new GroupService(db, gateway, audit, clock).FindByTokenAsync(token, ct);
            return a is null ? LinkNotFound() : Results.Ok(LinkDto(a));
        });

        link.MapPost("/forms", async (string token, FormsRequest req, CampDbContext db, IPaymentGateway gateway, IAuditLog audit, CancellationToken ct, TimeProvider clock) =>
        {
            var service = new GroupService(db, gateway, audit, clock);
            var a = await service.FindByTokenAsync(token, ct);
            if (a is null) return LinkNotFound();
            try
            {
                service.SubmitForms(a, req);
                await db.SaveChangesAsync(ct);
                return Results.Ok(LinkDto(a));
            }
            catch (GroupValidationException e) { return Results.ValidationProblem(e.Errors); }
        });

        link.MapPost("/withdrawal", async (string token, WithdrawalRequest req, CampDbContext db, IPaymentGateway gateway, IAuditLog audit, CancellationToken ct, TimeProvider clock) =>
        {
            var service = new GroupService(db, gateway, audit, clock);
            var a = await service.FindByTokenAsync(token, ct);
            if (a is null) return LinkNotFound();
            try
            {
                service.RequestWithdrawal(a, req);
                await db.SaveChangesAsync(ct);
                return Results.Ok(LinkDto(a));
            }
            catch (GroupValidationException e) { return Results.ValidationProblem(e.Errors); }
        });
    }

    static IQueryable<GroupRegistration> Groups(CampDbContext db) =>
        db.Set<GroupRegistration>().Include(g => g.Attendees).Include(g => g.Session).ThenInclude(s => s.Program);

    static Task<GroupRegistration?> LeaderGroup(CampDbContext db, int id, CurrentUser me) =>
        db.Set<GroupRegistration>().Include(g => g.Attendees).FirstOrDefaultAsync(g => g.Id == id && g.LeaderHouseholdId == me.HouseholdId);

    static string GroupName(string? requested, string leader)
    {
        var name = requested?.Trim() ?? "";
        if (name.Length > 120) name = name[..120];
        return name.Length > 0 ? name : $"{leader}'s group";
    }

    static IResult LinkNotFound() => Results.NotFound(new
    {
        error = "This link isn't active. If your group leader resent it, use the newest email, or ask them to send it again.",
    });

    static object SessionDto(Session s) => new { s.Id, s.Name, s.StartDate, s.EndDate, s.PriceCents, s.Program.Location };

    /// <summary>Active attendees only; every total is a sum of the rows the tracker lists.</summary>
    static object Counts(GroupRegistration g)
    {
        var active = g.Attendees.Where(a => a.IsActive).ToList();
        return new
        {
            Attendees = active.Count,
            Complete = active.Count(a => a.FormStatus == FormStatus.Complete),
            Incomplete = active.Count(a => a.FormStatus != FormStatus.Complete),
            NoEmail = active.Count(a => a.Email is null),
            WithdrawalRequests = active.Count(a => a.Withdrawal == WithdrawalStatus.Requested),
            Withdrawn = g.Attendees.Count - active.Count,
        };
    }

    static object Detail(GroupRegistration g)
    {
        var ops = g.Order?.Operations.Where(o => o.Succeeded).ToList() ?? [];
        var charge = ops.FirstOrDefault(o => o.Kind == PaymentKind.Charge);
        return new
        {
            g.Id,
            g.Name,
            Status = g.Status.ToString(),
            g.LeaderName,
            Program = new { g.Session.Program.Name, g.Session.Program.Slug },
            Session = SessionDto(g.Session),
            Counts = Counts(g),
            // Draft: what paying now would cost. Confirmed: what was charged, per attendee.
            PricePerAttendeeCents = g.Order is { Status: OrderStatus.Paid } paid ? paid.TotalCents / Math.Max(1, g.Attendees.Count) : g.Session.PriceCents,
            TotalCents = g.Order is { Status: OrderStatus.Paid } o ? o.TotalCents : g.Session.PriceCents * g.Attendees.Count,
            Payment = g.Status != GroupStatus.Confirmed || g.Order is null ? null : new
            {
                g.Order.ConfirmationCode,
                ChargedCents = charge?.AmountCents ?? 0,
                RefundedCents = ops.Where(o => o.Kind == PaymentKind.Refund).Sum(o => o.AmountCents),
                PricePerAttendeeCents = g.Order.TotalCents / Math.Max(1, g.Attendees.Count),
                CardLast4 = charge?.CardLast4,
                PaidAt = charge?.CreatedAt,
            },
            Attendees = g.Attendees.OrderBy(a => a.SortOrder).Select(a => new
            {
                a.Id,
                a.Name,
                a.Email,
                FormStatus = a.FormStatus.ToString(),
                a.LinkSentAt,
                a.SubmittedAt,
                a.IsActive,
                Withdrawal = a.Withdrawal.ToString(),
                a.WithdrawalReason,
                a.WithdrawalRequestedAt,
            }),
        };
    }

    // What an attendee sees: their own row and the program's forms. Never other attendees.
    static object LinkDto(GroupAttendee a)
    {
        var g = a.Group;
        var p = g.Session.Program;
        var answers = JsonSerializer.Deserialize<Dictionary<string, string>>(a.AnswersJson) ?? [];
        return new
        {
            Attendee = new { a.Name, a.Email, a.Phone, FormStatus = a.FormStatus.ToString(), a.SubmittedAt, a.IsActive, Withdrawal = a.Withdrawal.ToString(), Answers = answers },
            g.LeaderName,
            GroupName = g.Name,
            Program = new { p.Name, p.Location },
            Session = new { g.Session.Name, g.Session.StartDate, g.Session.EndDate },
            Waivers = p.Waivers.OrderBy(w => w.Id).Select(w => new
            {
                w.Id,
                w.Title,
                w.Version,
                w.EffectiveDate,
                w.Body,
                Accepted = a.WaiverAcceptances.Any(x => x.WaiverTemplateId == w.Id && x.Version == w.Version),
            }),
            Questions = p.Questions.OrderBy(q => q.SortOrder).Select(q => new
            {
                q.Key,
                q.Label,
                Type = q.Type.ToString(),
                q.Required,
                Options = q.Options?.Split('|') ?? [],
            }),
        };
    }
}
