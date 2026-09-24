using Camp.Api.Data;
using Camp.Api.Domain;

namespace Camp.Api.Features.Activities;

/// <summary>
/// The activity and cabinmate part of checkout (R4, R5). Checks run before seats are claimed;
/// placement runs inside the seat transaction, so a camper either gets a seat, a place in every
/// period they chose and their requests stored, or none of it.
/// </summary>
public static class ActivityCheckout
{
    public sealed record Camper(Person Person, List<ActivityChoice>? Activities, List<CabinmateInput>? Cabinmates);

    public static async Task<Dictionary<string, string[]>> CheckAsync(CampDbContext db, Session session, List<Camper> campers, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var asking = campers.Where(c => c.Activities is { Count: > 0 } || c.Cabinmates is { Count: > 0 }).ToList();
        if (asking.Count == 0) return errors;
        if (!await ActivityRules.OffersAsync(db, session.Id, ct))
        {
            errors["activities"] = ["This session doesn't take activity choices or cabinmate requests."];
            return errors;
        }
        foreach (var c in asking)
        {
            var grade = Eligibility.GradeFor(c.Person.DateOfBirth, session.StartDate);
            var block = await ActivityRules.BlockForAsync(db, session.Id, grade, ct);
            if (block is null)
            {
                errors[$"participants.{c.Person.Id}.activities"] = [$"{c.Person.FirstName}: no activities are scheduled for grade {grade}."];
                continue;
            }
            var offered = await ActivityRules.SlotsAsync(db, block.Id, grade, ct);
            foreach (var group in ActivityRules.Check(c.Person.Id, c.Person.FirstName, grade, offered, c.Activities, c.Cabinmates).GroupBy(p => p.Key))
                errors[group.Key] = [.. group.Select(p => p.Message).Distinct()];
        }
        return errors;
    }

    /// <summary>Places each seated camper and stores their requests. Throws <see cref="ActivityFullException"/> so the caller's transaction rolls back.</summary>
    public static async Task ApplyAsync(CampDbContext db, Session session, List<Registration> seated, List<Camper> campers, DateTime now, CancellationToken ct)
    {
        if (seated.Count == 0 || !await ActivityRules.OffersAsync(db, session.Id, ct)) return;
        await ActivityRules.LockSessionAsync(db, session.Id, ct);
        var conflicts = new List<ActivityConflict>();
        foreach (var reg in seated)
        {
            var c = campers.Single(x => x.Person.Id == reg.PersonId);
            if (c.Activities is { Count: > 0 })
            {
                var block = await ActivityRules.BlockForAsync(db, session.Id, reg.Grade, ct);
                if (block is null) continue;
                var offered = await ActivityRules.SlotsAsync(db, block.Id, reg.Grade, ct);
                conflicts.AddRange(await ActivityRules.ApplyAsync(db, reg.Id, session.Id, reg.PersonId, c.Person.FirstName, offered, c.Activities, "Checkout", now, ct));
            }
            if (c.Cabinmates is { Count: > 0 })
                await Cabinmates.SaveAsync(db, session.Id, reg.Id, reg.HouseholdId, c.Cabinmates, now, ct);
        }
        if (conflicts.Count > 0) throw new ActivityFullException(conflicts);
        await Cabinmates.MatchWaitingAsync(db, session.Id, [.. seated.Select(r => r.Id)], now, ct);
        await db.SaveChangesAsync(ct);
    }
}
