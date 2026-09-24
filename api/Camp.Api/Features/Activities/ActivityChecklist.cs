using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Family;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Activities;

/// <summary>F1 checklist rows: one "Activities" item per confirmed camper in a session with an activity schedule.</summary>
public static class ActivityChecklist
{
    public static async Task<List<ChecklistEntry>> ForAsync(CampDbContext db, IReadOnlyList<PaymentOrder> orders, CancellationToken ct = default)
    {
        // A moved camper's row names the session they now attend; the program is the order's.
        var regs = orders.SelectMany(o => o.Registrations.Select(r => (Reg: r, Program: o.Session.Program.Name)))
            .Where(x => x.Reg.Status == RegistrationStatus.Confirmed).Select(x => (x.Reg, x.Program)).ToList();
        if (regs.Count == 0) return [];
        var sessionIds = regs.Select(x => x.Reg.SessionId).Distinct().ToList();
        var scheduled = (await db.Set<ActivityBlock>().Where(b => sessionIds.Contains(b.SessionId)).Select(b => b.SessionId).Distinct().ToListAsync(ct)).ToHashSet();
        var entries = new List<ChecklistEntry>();
        foreach (var sessionId in scheduled)
        {
            var summaries = await ActivityReadModel.SummariesAsync(db, sessionId, ct);
            foreach (var (r, program) in regs.Where(x => x.Reg.SessionId == sessionId))
            {
                var s = summaries.GetValueOrDefault(r.Id, ActivitySummary.NotChosen);
                var (detail, done) = s.State switch
                {
                    "Assigned" => (s.Label, true),
                    "Partial" => ($"{s.Label} · choose again for the rest", false),
                    "Chosen" => ("Chosen · the camp office is placing campers", false),
                    _ => ("Not chosen yet", false),
                };
                entries.Add(new ChecklistEntry($"activities-{r.Id}", r.Person.FirstName, $"{program} · {r.Session.Name}",
                    "Activities", detail, done, done ? "Change activities" : "Choose activities", "activities", $"/family/activities/{r.Id}"));
            }
        }
        return entries;
    }
}
