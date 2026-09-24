using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Ops;

/// <summary>
/// Operations rows for Overnight Camp Session 3, on top of the core seed's 186 confirmed campers
/// (spec Part 3). Adds no registrations, so every canonical count stays as the core seed made it:
/// 16 cabins of 12 beds (86 boys and 86 girls placed, 14 unassigned), five groups of 10 per pool
/// (Boys G6–8 fully grouped with one separated pair), cabinmate requests, activity choices, pickup
/// adults, a rooming review just before the newest registration, and 12 campers already checked in.
/// </summary>
public sealed class OpsSeed : ISeedModule
{
    public static readonly string[] Activities = ["Archery", "Swimming", "Climbing", "Horseback", "Crafts", "Canoeing"];
    const int CabinsPerGender = 8;
    const int BedsPerCabin = 12;
    static readonly int[] CabinFill = [11, 11, 11, 11, 11, 11, 10, 10]; // 86 per gender

    public int Order => 170;

    public async Task RunAsync(CampDbContext db, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var session = await db.Sessions.Include(s => s.Pools).AsNoTracking()
            .FirstOrDefaultAsync(s => s.Program.Slug == "overnight-camp" && s.Name == "Session 3", ct);
        if (session is null || await db.Set<OpsCabin>().AnyAsync(c => c.SessionId == session.Id, ct)) return;
        var roster = await OpsReadModel.LoadAsync(db, session.Id, ct);
        if (roster is null || roster.Campers.Count == 0) return;
        var campers = roster.Campers;

        // ── Cabins: 8 boys, 8 girls ──
        var cabins = new List<OpsCabin>();
        foreach (var (gender, label, offset) in new[] { (Gender.Male, "Boys", 0), (Gender.Female, "Girls", CabinsPerGender) })
            for (var i = 1; i <= CabinsPerGender; i++)
                cabins.Add(new OpsCabin { SessionId = session.Id, Name = $"{label} Cabin {i}", Gender = gender, Beds = BedsPerCabin, SortOrder = offset + i });
        db.Set<OpsCabin>().AddRange(cabins);

        // ── Groups: five of 10 per pool ──
        var groups = new List<OpsGroup>();
        foreach (var pool in session.Pools.OrderBy(p => p.SortOrder))
            for (var i = 1; i <= 5; i++)
                groups.Add(new OpsGroup { SessionId = session.Id, PoolId = pool.Id, Name = $"Group {i}", Capacity = 10, SortOrder = pool.SortOrder * 10 + i });
        db.Set<OpsGroup>().AddRange(groups);
        await db.SaveChangesAsync(ct);

        var placements = campers.ToDictionary(c => c.RegistrationId, c => new OpsPlacement { RegistrationId = c.RegistrationId, SessionId = session.Id });

        // The newest registration arrived after the last rooming review: no cabin yet, flagged on O3.
        var newest = campers.MaxBy(c => c.CreatedAt)!;
        var reviewedAt = newest.CreatedAt.AddSeconds(-1);

        // ── Cabins: sorted by grade so cabins hold neighbouring grades; 10 boys and 4 girls unassigned ──
        foreach (var (gender, unassigned) in new[] { (Gender.Male, 10), (Gender.Female, 4) })
        {
            var list = campers.Where(c => c.Gender == gender).OrderBy(c => c.Grade).ThenBy(c => c.LastName).ThenBy(c => c.RegistrationId).ToList();
            var left = new HashSet<int>();
            if (newest.Gender == gender) left.Add(newest.RegistrationId);
            for (var i = 4; left.Count < unassigned && i < list.Count; i += 9) left.Add(list[i].RegistrationId);
            var placed = list.Where(c => !left.Contains(c.RegistrationId)).ToList();
            var genderCabins = cabins.Where(c => c.Gender == gender).OrderBy(c => c.SortOrder).ToList();
            var at = 0;
            for (var k = 0; k < genderCabins.Count; k++)
                for (var n = 0; n < CabinFill[k] && at < placed.Count; n++)
                    placements[placed[at++].RegistrationId].CabinId = genderCabins[k].Id;
        }

        // ── Groups: consecutive chunks of 10 by grade; Girls G3–5 keeps 3 campers ungrouped ──
        var poolLists = new Dictionary<int, List<Camper>>();
        foreach (var pool in session.Pools.OrderBy(p => p.SortOrder))
        {
            var list = campers.Where(c => c.PoolId == pool.Id).OrderBy(c => c.Grade).ThenBy(c => c.LastName).ThenBy(c => c.RegistrationId).ToList();
            poolLists[pool.Id] = list;
            var poolGroups = groups.Where(g => g.PoolId == pool.Id).OrderBy(g => g.SortOrder).ToList();
            var keepOut = pool.Gender == Gender.Female && pool.GradeMax <= 5 ? 3 : 0;
            for (var i = 0; i < list.Count - keepOut; i++)
                placements[list[i].RegistrationId].GroupId = poolGroups[Math.Min(i / 10, poolGroups.Count - 1)].Id;
        }

        // ── Activity choices: every 13th camper hasn't chosen yet ──
        for (var i = 0; i < campers.Count; i++)
            placements[campers[i].RegistrationId].Activity = i % 13 == 12 ? null : Activities[i % Activities.Length];

        // ── Cabinmate requests ──
        var requests = new List<OpsBuddyRequest>();
        void Ask(Camper a, Camper b, DateTime at) =>
            requests.Add(new OpsBuddyRequest { SessionId = session.Id, RegistrationId = a.RegistrationId, RequestedRegistrationId = b.RegistrationId, CreatedAt = at });
        var older = reviewedAt.AddDays(-10);
        var boysG68 = session.Pools.First(p => p.Gender == Gender.Male && p.GradeMin >= 6);
        var boys = poolLists[boysG68.Id];
        // Three pairs already together, and one pair split across Group 1 and Group 2 (O2's review case).
        foreach (var (a, b) in new[] { (1, 2), (21, 22), (33, 34), (8, 14) }) Ask(boys[a], boys[b], older);
        // Girls: pairs sharing a cabin, plus one new request for a camper in another cabin (O3's "not met").
        var girlsInCabin = campers.Where(c => c.Gender == Gender.Female && placements[c.RegistrationId].CabinId is not null)
            .GroupBy(c => placements[c.RegistrationId].CabinId!.Value).OrderBy(g => g.Key).Select(g => g.OrderBy(c => c.RegistrationId).ToList()).ToList();
        Ask(girlsInCabin[0][0], girlsInCabin[0][1], older);
        Ask(girlsInCabin[1][2], girlsInCabin[1][3], older);
        Ask(girlsInCabin[1][3], girlsInCabin[1][2], older);
        Ask(girlsInCabin[3][0], girlsInCabin[2][0], newest.CreatedAt);
        db.Set<OpsBuddyRequest>().AddRange(requests);

        db.Set<OpsRoomingReview>().Add(new OpsRoomingReview { SessionId = session.Id, ReviewedAt = reviewedAt, ReviewedBy = "Jamie Dalton (Camp director)" });

        // ── Pickup adults: the primary guardian for every family, a grandparent for every third ──
        var householdIds = campers.Select(c => c.HouseholdId).Distinct().OrderBy(x => x).ToList();
        var households = await db.Households.Include(h => h.Members).AsNoTracking().Where(h => householdIds.Contains(h.Id)).ToListAsync(ct);
        var grandparents = new[] { "Linda", "Carol", "Richard", "Barbara", "Thomas", "Patricia" };
        foreach (var (h, i) in households.OrderBy(h => h.Id).Select((h, i) => (h, i)))
        {
            var guardian = h.Members.Where(m => m.IsAdult).OrderBy(m => m.Id).FirstOrDefault();
            if (guardian is not null)
                db.Set<OpsPickupAdult>().Add(new OpsPickupAdult { HouseholdId = h.Id, Name = guardian.FullName, Relationship = "Parent", Phone = h.Phone });
            if (i % 3 == 0)
            {
                var first = grandparents[i / 3 % grandparents.Length];
                db.Set<OpsPickupAdult>().Add(new OpsPickupAdult { HouseholdId = h.Id, Name = $"{first} {h.Name}", Relationship = first is "Richard" or "Thomas" ? "Grandfather" : "Grandmother", Phone = h.Phone });
            }
        }

        // ── Arrival day has started: 12 ready campers are already checked in ──
        var arrival = session.StartDate.ToDateTime(new TimeOnly(14, 0), DateTimeKind.Utc);
        foreach (var (c, i) in campers.Where(c => c.Reasons.Count == 0 && placements[c.RegistrationId].CabinId is not null)
                     .OrderBy(c => c.RegistrationId).Take(12).Select((c, i) => (c, i)))
        {
            placements[c.RegistrationId].CheckedInAt = arrival.AddMinutes(3 * i);
            placements[c.RegistrationId].CheckedInBy = "Jamie Dalton (Camp director)";
        }

        db.Set<OpsPlacement>().AddRange(placements.Values);
        await db.SaveChangesAsync(ct);
    }
}
