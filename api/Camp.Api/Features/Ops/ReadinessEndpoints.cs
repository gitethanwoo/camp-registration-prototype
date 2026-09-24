using System.Text.Json;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Ops;

public record ReminderRequest(List<int> RegistrationIds);

/// <summary>
/// O1 · Session readiness (FR-27, FR-70, FR-80, FR-107). Every tile is computed from the same roster
/// the page lists, so a count can always be traced to rows. Needs attention counts unique campers.
/// </summary>
public sealed class ReadinessEndpoints : IEndpointModule
{
    static readonly TimeSpan RemindAgainAfter = TimeSpan.FromHours(24);

    public void Map(IEndpointRouteBuilder app)
    {
        var ops = app.MapGroup("/api/admin/ops");

        ops.MapGet("/sessions/{id:int}/readiness", async (int id, CampDbContext db, CancellationToken ct) =>
        {
            var roster = await OpsReadModel.LoadAsync(db, id, ct);
            if (roster is null) return Results.NotFound();
            var s = roster.Session;
            var campers = roster.Campers;
            var waitlist = await db.WaitlistEntries
                .Where(w => w.Pool.SessionId == id && (w.Status == WaitlistStatus.Waiting || w.Status == WaitlistStatus.Offered))
                .GroupBy(w => w.PoolId).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var names = await OpsNames.For(db, id, ct);
            var attention = campers.Where(c => c.Reasons.Count > 0).ToList();

            return Results.Ok(new
            {
                Session = new { s.Id, s.Name, s.StartDate, s.EndDate, Program = s.Program.Name, HealthMechanism = s.Program.HealthMechanism.ToString() },
                roster.UsesCampDoc,
                CampDocUrl = roster.UsesCampDoc ? OpsReadModel.CampDocUrl : null,
                Capacity = s.Pools.Sum(p => p.Capacity),
                Registered = campers.Count,
                Waitlisted = waitlist.Values.Sum(),
                Ready = campers.Count - attention.Count,
                NeedsAttention = attention.Count,
                Breakdown = new
                {
                    Health = campers.Count(c => c.HealthOpen),
                    Waivers = campers.Count(c => c.WaiverOpen),
                    Balance = campers.Count(c => c.BalanceOpen),
                },
                // The reasons overlap: this is how many campers carry more than one.
                Overlap = new
                {
                    TwoReasons = attention.Count(c => c.Reasons.Count == 2),
                    ThreeReasons = attention.Count(c => c.Reasons.Count == 3),
                    TotalReasons = attention.Sum(c => c.Reasons.Count),
                },
                Pools = s.Pools.OrderBy(p => p.SortOrder).Select(p => GuestEndpoints.ToAvailability(p, waitlist)),
                Cabins = names.Cabins.Values.Order(),
                Activities = OpsSeed.Activities,
                Roster = campers.Select(c => new
                {
                    c.RegistrationId,
                    c.HouseholdId,
                    c.Name,
                    c.Grade,
                    Gender = c.Gender.ToString(),
                    Pool = c.PoolName,
                    Payment = c.BalanceOpen ? "Balance due" : "Paid",
                    c.BalanceCents,
                    Waiver = c.WaiverState,
                    c.WaiversSigned,
                    c.WaiversRequired,
                    Health = c.HealthOpen ? "Incomplete" : "Complete",
                    Cabin = names.Cabin(c.Placement?.CabinId),
                    Group = names.Group(c.Placement?.GroupId),
                    c.Placement?.Activity,
                    Reasons = c.Reasons.Select(r => r.ToString()),
                    c.Placement?.RemindedAt,
                    CheckedIn = c.Placement?.CheckedInAt is not null,
                }),
            });
        }).RequireAuthorization(Policies.Staff);

        // Bulk remind: one HubSpot email per family listing each camper's open items. Ready campers are skipped.
        ops.MapPost("/sessions/{id:int}/reminders", async (int id, ReminderRequest req, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            if (req.RegistrationIds is not { Count: > 0 }) return OpsResults.Invalid("registrationIds", "Choose at least one camper to remind.");
            if (req.RegistrationIds.Count > 1000) return OpsResults.Invalid("registrationIds", "Send reminders to at most 1,000 campers at a time.");
            var roster = await OpsReadModel.LoadAsync(db, id, ct);
            if (roster is null) return Results.NotFound();
            var chosen = req.RegistrationIds.ToHashSet();
            var selected = roster.Campers.Where(c => chosen.Contains(c.RegistrationId)).ToList();
            if (selected.Count != chosen.Count) return OpsResults.Invalid("registrationIds", "Some of those campers aren't confirmed in this session. Refresh and try again.");
            var needsIt = selected.Where(c => c.Reasons.Count > 0).ToList();
            if (needsIt.Count == 0) return OpsResults.Conflict("Everyone you picked is ready for camp, so there's nothing to remind them about.");
            // A family reminded in the last day isn't emailed again, so a double click or a second
            // staff member working the same list doesn't send duplicate emails.
            var now = DateTime.UtcNow;
            var since = now - RemindAgainAfter;
            var open = needsIt.Where(c => c.Placement?.RemindedAt is not { } at || at < since).ToList();
            var recent = needsIt.Count - open.Count;
            if (open.Count == 0)
                return OpsResults.Conflict($"{(recent == 1 ? "That family was" : "Those families were")} reminded in the last 24 hours. Wait a day before reminding them again.");

            var households = open.Select(c => c.HouseholdId).Distinct().ToList();
            var emails = await db.Households.Where(h => households.Contains(h.Id)).ToDictionaryAsync(h => h.Id, h => h.Email, ct);
            var s = roster.Session;
            foreach (var family in open.GroupBy(c => c.HouseholdId))
            {
                db.OutboxEvents.Add(new OutboxEvent
                {
                    Type = "ReadinessReminder",
                    Target = "HubSpot",
                    AggregateId = "household-" + family.Key.ToString(CultureInfo.InvariantCulture),
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        email = emails.GetValueOrDefault(family.Key),
                        session = $"{s.Program.Name} · {s.Name}",
                        campers = family.Select(c => new { name = c.Name, items = c.Reasons.Select(r => OpsReadModel.Describe(r, c, roster.UsesCampDoc).Label) }),
                    }),
                    CreatedAt = now,
                });
            }
            var regIds = open.Select(c => c.RegistrationId).ToList();
            var regs = await db.Registrations.Where(r => regIds.Contains(r.Id)).ToListAsync(ct);
            foreach (var reg in regs) (await OpsReadModel.PlacementFor(db, reg, ct)).RemindedAt = now;

            var families = households.Count;
            audit.Record("ops.reminders_sent", "Session", id,
                $"Sent readiness reminders to {OpsResults.Plural(families, "family", "families")} for {OpsResults.Plural(open.Count, "camper", "campers")} in {s.Program.Name} · {s.Name}.");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { Families = families, Campers = open.Count, Skipped = selected.Count - needsIt.Count, RecentlyReminded = recent });
        }).RequireAuthorization(Policies.Cet);
    }
}

/// <summary>Cabin and group names for a session, for roster columns.</summary>
internal sealed record OpsNames(Dictionary<int, string> Cabins, Dictionary<int, string> Groups)
{
    public string Cabin(int? id) => id is { } x && Cabins.TryGetValue(x, out var n) ? n : "Unassigned";
    public string Group(int? id) => id is { } x && Groups.TryGetValue(x, out var n) ? n : "Unassigned";

    public static async Task<OpsNames> For(CampDbContext db, int sessionId, CancellationToken ct) => new(
        await db.Set<OpsCabin>().Where(c => c.SessionId == sessionId).ToDictionaryAsync(c => c.Id, c => c.Name, ct),
        await db.Set<OpsGroup>().Where(g => g.SessionId == sessionId).ToDictionaryAsync(g => g.Id, g => g.Name, ct));
}

internal static class OpsResults
{
    public static IResult Invalid(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });

    public static IResult Conflict(string message) => Results.Conflict(new { error = message });

    /// <summary>"1 camper", "3 campers".</summary>
    public static string Plural(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
}
