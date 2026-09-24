using Camp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Activities;

/// <summary>What O1 and O5 show for a camper's activities. State is Assigned, Partial, Chosen or NotChosen.</summary>
public sealed record ActivitySummary(IReadOnlyList<string> Names, string State, string Label)
{
    public static readonly ActivitySummary NotChosen = new([], "NotChosen", "Not chosen");
}

public static class ActivityReadModel
{
    /// <summary>Activity names scheduled in a session, in catalog order.</summary>
    public static async Task<List<string>> ScheduledAsync(CampDbContext db, int sessionId, CancellationToken ct) =>
        await db.Set<ActivitySlot>().Where(s => s.SessionId == sessionId)
            .Join(db.Set<Activity>(), s => s.ActivityId, a => a.Id, (s, a) => a)
            .Distinct().OrderBy(a => a.SortOrder).Select(a => a.Name).ToListAsync(ct);

    /// <summary>Per registration in the session: the activities it's placed in, by period, and whether the family has chosen.</summary>
    public static async Task<Dictionary<int, ActivitySummary>> SummariesAsync(CampDbContext db, int sessionId, CancellationToken ct)
    {
        var placed = await db.Set<ActivityAssignment>().AsNoTracking().Where(a => a.SessionId == sessionId)
            .Join(db.Set<ActivitySlot>(), a => a.SlotId, s => s.Id, (a, s) => new { a.RegistrationId, a.Period, s.ActivityId })
            .Join(db.Set<Activity>(), x => x.ActivityId, a => a.Id, (x, a) => new { x.RegistrationId, x.Period, a.Name })
            .ToListAsync(ct);
        var chose = (await db.Set<ActivityPreference>()
            .Join(db.Registrations.Where(r => r.SessionId == sessionId), p => p.RegistrationId, r => r.Id, (p, r) => p.RegistrationId)
            .Distinct().ToListAsync(ct)).ToHashSet();
        var result = new Dictionary<int, ActivitySummary>();
        foreach (var g in placed.GroupBy(p => p.RegistrationId))
        {
            var names = g.OrderBy(x => x.Period).Select(x => x.Name).ToList();
            var periods = g.Select(x => x.Period).Distinct().Count();
            result[g.Key] = periods >= ActivityPeriods.All.Length
                ? new(names, "Assigned", string.Join(" · ", names))
                : new(names, "Partial", $"{periods} of {ActivityPeriods.All.Length} assigned");
        }
        foreach (var id in chose.Where(id => !result.ContainsKey(id)))
            result[id] = new([], "Chosen", "Chosen · not assigned");
        return result;
    }
}
