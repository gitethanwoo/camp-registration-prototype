using Camp.Api.Data;
using Camp.Api.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Activities;

/// <summary>A camper's ranked choices for one period. The first id is the first choice.</summary>
public record ActivityChoice(int Period, List<int> Ranked);

/// <summary>A friend the camper would like to share a cabin with: a name plus a parent's email or the friend's confirmation code.</summary>
public record CabinmateInput(string Name, string Contact);

public record ActivityAlternative(int ActivityId, string Name, int Remaining);

/// <summary>Every ranked choice for this camper and period was full.</summary>
public record ActivityConflict(int PersonId, string FirstName, int Period, List<ActivityAlternative> Alternatives);

/// <summary>Thrown inside a transaction when a camper can't be placed; the caller rolls back and answers 409.</summary>
public class ActivityFullException(List<ActivityConflict> conflicts) : Exception("An activity filled up.")
{
    public List<ActivityConflict> Conflicts { get; } = conflicts;

    public IResult ToResult() => Results.Conflict(new
    {
        Title = Conflicts.Count == 1
            ? $"{Conflicts[0].FirstName}'s choices for Period {Conflicts[0].Period} just filled. Pick another activity."
            : "Some activity choices just filled. Pick another activity where it's marked.",
        Conflicts,
    });
}

/// <summary>One offered activity in one period for one block, with its live count.</summary>
public sealed record SlotView(int SlotId, int Period, int ActivityId, string Name, int GradeMin, int GradeMax, int Capacity, int Assigned, string Instructor, string Space)
{
    public int Remaining => Math.Max(0, Capacity - Assigned);
}

public static class ActivityPeriods
{
    public static readonly (int Number, string Time)[] All = [(1, "9:00–10:15 AM"), (2, "10:45 AM–12:00 PM"), (3, "2:00–3:15 PM")];
    public const int MaxRanks = 3;
    public const int CabinmateLimit = 2;

    public static string Time(int period) => All.Where(p => p.Number == period).Select(p => p.Time).FirstOrDefault() ?? "";
}

/// <summary>Capacity and grade rules shared by checkout, the family page and O4.</summary>
public static class ActivityRules
{
    /// <summary>Registrations that hold activity seats: confirmed, or checked out and waiting on the charge.</summary>
    public static bool Holds(RegistrationStatus s) => s is RegistrationStatus.Confirmed or RegistrationStatus.PaymentPending;

    public static Task<bool> OffersAsync(CampDbContext db, int sessionId, CancellationToken ct = default) =>
        db.Set<ActivityBlock>().AnyAsync(b => b.SessionId == sessionId, ct);

    public static Task<ActivityBlock?> BlockForAsync(CampDbContext db, int sessionId, int grade, CancellationToken ct) =>
        db.Set<ActivityBlock>().AsNoTracking().FirstOrDefaultAsync(b => b.SessionId == sessionId && b.GradeMin <= grade && b.GradeMax >= grade, ct);

    /// <summary>Slots in a block, with fresh counts. With a grade, only activities that grade may take.</summary>
    public static async Task<List<SlotView>> SlotsAsync(CampDbContext db, int blockId, int? grade, CancellationToken ct)
    {
        var rows = await db.Set<ActivitySlot>().AsNoTracking().Where(s => s.BlockId == blockId)
            .Join(db.Set<Activity>(), s => s.ActivityId, a => a.Id, (s, a) => new { s, a })
            .Where(x => x.a.IsActive)
            .OrderBy(x => x.a.SortOrder).ThenBy(x => x.s.Period)
            .Select(x => new SlotView(x.s.Id, x.s.Period, x.a.Id, x.a.Name, x.a.GradeMin, x.a.GradeMax, x.s.Capacity, x.s.Assigned, x.s.Instructor, x.s.Space))
            .ToListAsync(ct);
        return grade is { } g ? rows.Where(r => r.GradeMin <= g && r.GradeMax >= g).ToList() : rows;
    }

    /// <summary>Checks ranked choices and cabinmate requests for one camper. Grade limits are enforced here.</summary>
    public static List<(string Key, string Message)> Check(int personId, string firstName, int grade, List<SlotView> offered,
        List<ActivityChoice>? choices, List<CabinmateInput>? mates)
    {
        var problems = new List<(string, string)>();
        var key = $"participants.{personId}.activities";
        foreach (var c in choices ?? [])
        {
            if (!ActivityPeriods.All.Any(p => p.Number == c.Period)) { problems.Add((key, $"{firstName}: Period {c.Period} doesn't exist.")); continue; }
            if ((choices ?? []).Count(x => x.Period == c.Period) > 1) { problems.Add((key, $"{firstName}: Period {c.Period} is listed twice.")); continue; }
            if (c.Ranked is not { Count: > 0 }) { problems.Add((key, $"{firstName}: choose at least one activity for Period {c.Period}.")); continue; }
            if (c.Ranked.Count > ActivityPeriods.MaxRanks) problems.Add((key, $"{firstName}: rank at most {ActivityPeriods.MaxRanks} activities for Period {c.Period}."));
            if (c.Ranked.Distinct().Count() != c.Ranked.Count) problems.Add((key, $"{firstName}: an activity is ranked twice in Period {c.Period}."));
            foreach (var id in c.Ranked.Where(id => !offered.Any(o => o.ActivityId == id && o.Period == c.Period)))
                problems.Add((key, $"{firstName}: that activity isn't offered to grade {grade} campers in Period {c.Period}."));
        }
        var mkey = $"participants.{personId}.cabinmates";
        if (mates is { Count: > ActivityPeriods.CabinmateLimit })
            problems.Add((mkey, $"{firstName}: request up to {ActivityPeriods.CabinmateLimit} friends."));
        foreach (var m in mates ?? [])
        {
            if (string.IsNullOrWhiteSpace(m.Name) || m.Name.Trim().Length > 100) problems.Add((mkey, $"{firstName}: enter each friend's full name."));
            if (string.IsNullOrWhiteSpace(m.Contact) || m.Contact.Trim().Length > 200) problems.Add((mkey, $"{firstName}: enter a parent's email or the friend's confirmation code."));
        }
        return problems;
    }

    /// <summary>Days before a session starts that families can still choose or change activities themselves.</summary>
    public const int ChangeCutoffDays = 7;

    /// <summary>The last day a family can change activities for a session starting on <paramref name="start"/>.</summary>
    public static DateOnly ChangeDeadline(DateOnly start) => start.AddDays(-ChangeCutoffDays);

    /// <summary>
    /// Serializes every change to one session's activity places (checkout, family save, assign, move,
    /// cancel, transfer) until the caller's transaction ends. Callers take it after any capacity-pool
    /// claims and before touching slots, so the lock order is always pool → session → slot. Reads made
    /// after it (the plan for "Assign from preferences", a camper's current place) can't go stale.
    /// </summary>
    public static Task LockSessionAsync(CampDbContext db, int sessionId, CancellationToken ct)
    {
        var resource = string.Create(CultureInfo.InvariantCulture, $"activities-session-{sessionId}");
        return db.Database.ExecuteSqlInterpolatedAsync($@"DECLARE @r int;
EXEC @r = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
IF @r < 0 THROW 51000, 'Activity places are busy.', 1;", ct);
    }

    /// <summary>A deadlock victim (1205) or a lock wait that timed out (51000): worth one retry, then a 409.</summary>
    public static bool IsBusy(Exception e) =>
        (e as SqlException ?? e.InnerException as SqlException) is { Number: 1205 or 51000 };

    /// <summary>Runs <paramref name="work"/> (which opens its own transaction); on a deadlock retries once, then answers 409.</summary>
    public static async Task<IResult> RetryWhenBusyAsync(CampDbContext db, Func<Task<IResult>> work)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return await work(); }
            catch (Exception e) when (IsBusy(e))
            {
                db.ChangeTracker.Clear();
                if (attempt == 2)
                    return Results.Conflict(new { Title = "Someone else was changing activities at the same moment. Try again." });
            }
        }
    }

    /// <summary>Takes one seat if the slot has room. Safe under concurrency: the check and the increment are one statement.</summary>
    public static async Task<bool> ClaimAsync(CampDbContext db, int slotId, CancellationToken ct) =>
        await db.Set<ActivitySlot>().Where(s => s.Id == slotId && s.Assigned < s.Capacity)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Assigned, x => x.Assigned + 1), ct) == 1;

    public static Task ReleaseSeatAsync(CampDbContext db, int slotId, CancellationToken ct) =>
        db.Set<ActivitySlot>().Where(s => s.Id == slotId && s.Assigned > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Assigned, x => x.Assigned - 1), ct);

    /// <summary>
    /// Saves the choices and places the camper in each period at the highest-ranked activity with
    /// room. A period the camper already has a place in is released first (the family changed its
    /// mind). Returns the periods where nothing had room; the caller's transaction must roll back then.
    /// </summary>
    public static async Task<List<ActivityConflict>> ApplyAsync(CampDbContext db, int registrationId, int sessionId, int personId, string firstName,
        List<SlotView> offered, List<ActivityChoice> choices, string source, DateTime now, CancellationToken ct)
    {
        var conflicts = new List<ActivityConflict>();
        var periods = choices.Select(c => c.Period).ToList();
        await db.Set<ActivityPreference>().Where(p => p.RegistrationId == registrationId && periods.Contains(p.Period)).ExecuteDeleteAsync(ct);
        var existing = await db.Set<ActivityAssignment>().Where(a => a.RegistrationId == registrationId && periods.Contains(a.Period)).ToListAsync(ct);
        foreach (var a in existing) await ReleaseSeatAsync(db, a.SlotId, ct);
        db.Set<ActivityAssignment>().RemoveRange(existing);

        foreach (var c in choices.OrderBy(c => c.Period))
        {
            db.Set<ActivityPreference>().AddRange(c.Ranked.Select((id, i) => new ActivityPreference { RegistrationId = registrationId, Period = c.Period, Rank = i + 1, ActivityId = id }));
            SlotView? placed = null;
            foreach (var id in c.Ranked)
            {
                var slot = offered.Single(o => o.ActivityId == id && o.Period == c.Period);
                if (await ClaimAsync(db, slot.SlotId, ct)) { placed = slot; break; }
            }
            if (placed is not null)
            {
                db.Set<ActivityAssignment>().Add(new ActivityAssignment { RegistrationId = registrationId, SessionId = sessionId, SlotId = placed.SlotId, Period = c.Period, Source = source, AssignedAt = now });
                continue;
            }
            var fresh = await db.Set<ActivitySlot>().AsNoTracking().Where(s => offered.Select(o => o.SlotId).Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Capacity - s.Assigned, ct);
            var alternatives = offered.Where(o => o.Period == c.Period && !c.Ranked.Contains(o.ActivityId) && fresh.GetValueOrDefault(o.SlotId) > 0)
                .Select(o => new ActivityAlternative(o.ActivityId, o.Name, fresh[o.SlotId])).ToList();
            conflicts.Add(new ActivityConflict(personId, firstName, c.Period, alternatives));
        }
        return conflicts;
    }

    /// <summary>
    /// Gives back every seat, choice and cabinmate request held by these registrations: a declined
    /// payment, a cancellation, or a transfer to another session. Runs inside the caller's transaction.
    /// </summary>
    public static async Task ReleaseAsync(CampDbContext db, List<int> registrationIds, CancellationToken ct)
    {
        if (registrationIds.Count == 0) return;
        var sessions = await db.Set<ActivityAssignment>().Where(a => registrationIds.Contains(a.RegistrationId)).Select(a => a.SessionId)
            .Union(db.Set<CabinmateRequest>().Where(c => registrationIds.Contains(c.RegistrationId) || (c.MatchedRegistrationId != null && registrationIds.Contains(c.MatchedRegistrationId.Value))).Select(c => c.SessionId))
            .Distinct().ToListAsync(ct);
        foreach (var sessionId in sessions.Order()) await LockSessionAsync(db, sessionId, ct);
        var held = await db.Set<ActivityAssignment>().Where(a => registrationIds.Contains(a.RegistrationId)).ToListAsync(ct);
        foreach (var a in held) await ReleaseSeatAsync(db, a.SlotId, ct);
        await db.Set<ActivityAssignment>().Where(a => registrationIds.Contains(a.RegistrationId)).ExecuteDeleteAsync(ct);
        await db.Set<ActivityPreference>().Where(a => registrationIds.Contains(a.RegistrationId)).ExecuteDeleteAsync(ct);
        await Cabinmates.ForgetAsync(db, registrationIds, ct);
    }
}
