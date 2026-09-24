using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Groups;
using Camp.Api.Features.Ops;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Activities;

/// <summary>
/// The activity catalog, a Juniors (G3–5) and a Seniors (G6–8) schedule for every Overnight Camp
/// session, and choices and places for ON Session 3's 186 campers. Adds no registrations, so
/// 186 / 9 / 159 / 27 stay as the core seed made them. In ON Session 3:
/// every 13th camper (by registration) hasn't chosen yet; every 11th has chosen but has no place
/// (work for "Assign from preferences"); Climbing in Period 2 for Juniors has one seat left; one
/// Senior is booked twice in Period 2, which puts Swimming at 25 / 24; and one grade 3 camper is in
/// Horseback (grades 5–8). One ready Girls G3–5 camper belongs to Pastor Dave Kim's household and
/// has no activities yet, so F1 shows "Choose activities".
/// </summary>
public sealed class ActivitiesSeed : ISeedModule
{
    public int Order => 175; // after OpsSeed (170): reads its placements to pick a camper who isn't checked in

    sealed record Def(string Name, string Category, int Min, int Max, int Ratio, string Instructor, string Space, string Bring, string Description, string[] Staff);

    static readonly Def[] Catalog =
    [
        new("Archery", "Outdoor skills", 3, 8, 8, "Certified archery instructor", "Archery range", "Closed-toe shoes.",
            "Learn range safety, how to stand and aim, and shoot at targets with a coach beside you. No experience needed.", ["Megan Carter", "Dylan Brooks", "Jordan Hayes"]),
        new("Swimming", "Aquatics", 3, 8, 6, "Certified lifeguard", "Pool", "Swimsuit, towel and sunscreen.",
            "Free swim and games in the pool, with a swim check on the first day so every camper swims where they're comfortable.", ["Chris Long", "Avery Kim", "Sam Ortiz"]),
        new("Climbing", "Outdoor skills", 4, 8, 6, "Certified climbing instructor", "Climbing wall", "Closed-toe shoes and clothes you can move in.",
            "Climb the 30-foot wall on a top rope with a belay instructor. Campers learn knots, belay commands and how to encourage each other.", ["Jordan Lee", "Taylor Morgan", "Riley Chen"]),
        new("Horseback", "Equestrian", 5, 8, 5, "Wrangler", "Stables", "Long pants and closed-toe shoes with a heel.",
            "Groom, tack and ride with our wranglers, starting in the ring and moving to the trail by the end of the week.", ["Emily Park", "Noah Wilson", "Grace Bell"]),
        new("Crafts", "Arts", 3, 8, 12, "", "Arts lodge", "Clothes that can get messy.",
            "Make something to take home each day: tie-dye, leather bracelets, painted rocks and a cabin banner.", ["Jasmine Patel", "Olivia Chen", "Maya Reed"]),
        new("Canoeing", "Aquatics", 4, 8, 8, "Certified lifeguard", "Lake", "Swimsuit, water shoes and a towel.",
            "Paddle the lake in two-person canoes. Campers learn strokes, steering and what to do if they tip, with a lifeguard on the water.", ["Daniel Ruiz", "Sophia Kim", "Ben Foster"]),
        new("Zipline", "Outdoor skills", 6, 8, 6, "Certified ropes instructor", "Ropes course", "Closed-toe shoes and long shorts or pants.",
            "Gear up, climb the tower and ride the 400-foot zipline over the meadow. Offered at some sessions.", ["Kyle Grant", "Hannah Moss", "Eli Price"]),
    ];

    const int Scheduled = 6; // the first six activities run every period; Zipline isn't on this summer's grid
    const int SoftCap = 20;  // seeded places stop here, leaving room for the demo's live choices

    public async Task RunAsync(CampDbContext db, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        if (await db.Set<Activity>().AnyAsync(ct)) return;

        var activities = Catalog.Select((d, i) => new Activity
        {
            Name = d.Name,
            Category = d.Category,
            Description = d.Description,
            WhatToBring = d.Bring,
            ImageUrl = $"/images/activities/{d.Name.ToLowerInvariant()}.svg",
            GradeMin = d.Min,
            GradeMax = d.Max,
            DefaultCapacity = 24,
            StaffRatio = d.Ratio,
            Instructor = d.Instructor,
            Space = d.Space,
            SortOrder = i + 1,
        }).ToList();
        db.Set<Activity>().AddRange(activities);
        await db.SaveChangesAsync(ct);

        var sessions = await db.Sessions.AsNoTracking().Where(s => s.Program.Slug == "overnight-camp").ToListAsync(ct);
        foreach (var session in sessions)
        {
            var blocks = new[]
            {
                new ActivityBlock { SessionId = session.Id, Name = "Juniors", GradeMin = 3, GradeMax = 5, SortOrder = 1 },
                new ActivityBlock { SessionId = session.Id, Name = "Seniors", GradeMin = 6, GradeMax = 8, SortOrder = 2 },
            };
            db.Set<ActivityBlock>().AddRange(blocks);
            await db.SaveChangesAsync(ct);
            foreach (var (block, b) in blocks.Select((x, i) => (x, i)))
                for (var a = 0; a < Scheduled; a++)
                    for (var period = 1; period <= 3; period++)
                        db.Set<ActivitySlot>().Add(new ActivitySlot
                        {
                            SessionId = session.Id,
                            BlockId = block.Id,
                            ActivityId = activities[a].Id,
                            Period = period,
                            Capacity = activities[a].DefaultCapacity,
                            Instructor = Catalog[a].Staff[(period - 1 + b) % 3],
                            Space = activities[a].Space,
                        });
            await db.SaveChangesAsync(ct);
        }

        var s3 = sessions.FirstOrDefault(s => s.Name == "Session 3");
        if (s3 is not null) await SeedSessionThreeAsync(db, s3, activities, ct);
    }

    static async Task SeedSessionThreeAsync(CampDbContext db, Session session, List<Activity> activities, CancellationToken ct)
    {
        var kimRegistration = await MoveCamperToKimAsync(db, session, ct);
        var roster = await OpsReadModel.LoadAsync(db, session.Id, ct);
        if (roster is null || roster.Campers.Count == 0) return;
        var blocks = await db.Set<ActivityBlock>().AsNoTracking().Where(b => b.SessionId == session.Id).ToListAsync(ct);
        var slots = await db.Set<ActivitySlot>().Where(s => s.SessionId == session.Id).ToListAsync(ct);
        var byId = activities.ToDictionary(a => a.Id);
        var count = slots.ToDictionary(s => s.Id, _ => 0);
        var assignments = new List<ActivityAssignment>();
        var now = session.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(-30);

        ActivitySlot SlotOf(ActivityBlock block, string name, int period) =>
            slots.Single(s => s.BlockId == block.Id && s.Period == period && byId[s.ActivityId].Name == name);
        void Place(Camper c, ActivitySlot slot, string source)
        {
            assignments.Add(new ActivityAssignment { RegistrationId = c.RegistrationId, SessionId = session.Id, SlotId = slot.Id, Period = slot.Period, Source = source, AssignedAt = now });
            count[slot.Id]++;
        }

        var campers = roster.Campers.OrderBy(c => c.RegistrationId).ToList();
        for (var i = 0; i < campers.Count; i++)
        {
            var c = campers[i];
            if (i % 13 == 12 || c.RegistrationId == kimRegistration) continue; // hasn't chosen yet
            var block = blocks.Single(b => b.GradeMin <= c.Grade && b.GradeMax >= c.Grade);
            for (var period = 1; period <= 3; period++)
            {
                var offered = slots.Where(s => s.BlockId == block.Id && s.Period == period && byId[s.ActivityId].GradeMin <= c.Grade && byId[s.ActivityId].GradeMax >= c.Grade)
                    .OrderBy(s => byId[s.ActivityId].SortOrder).ToList();
                var ranked = Enumerable.Range(0, Math.Min(ActivityPeriods.MaxRanks, offered.Count)).Select(k => offered[(i + period * 2 + k) % offered.Count]).ToList();
                db.Set<ActivityPreference>().AddRange(ranked.Select((s, k) => new ActivityPreference { RegistrationId = c.RegistrationId, Period = period, Rank = k + 1, ActivityId = s.ActivityId }));
                if (i % 11 == 5) continue; // chosen, not placed yet
                var slot = ranked.FirstOrDefault(s => count[s.Id] < SoftCap);
                if (slot is not null) Place(c, slot, "Checkout");
            }
        }

        var juniors = blocks.Single(b => b.Name == "Juniors");
        var seniors = blocks.Single(b => b.Name == "Seniors");
        Camper Who(ActivityAssignment a) => campers.Single(c => c.RegistrationId == a.RegistrationId);

        // Juniors' Climbing in Period 2 has one seat left: staff moved campers in from other activities.
        var climbing = SlotOf(juniors, "Climbing", 2);
        foreach (var a in assignments.Where(a => a.Period == 2 && a.SlotId != climbing.Id && slots.Single(s => s.Id == a.SlotId).BlockId == juniors.Id && Who(a).Grade >= 4).ToList())
        {
            if (count[climbing.Id] >= climbing.Capacity - 1) break;
            count[a.SlotId]--;
            a.SlotId = climbing.Id;
            a.Source = "Staff";
            count[climbing.Id]++;
        }

        // Seniors' Swimming in Period 2 is at 25 / 24 (staff moved one camper too many in) for O4 to resolve.
        var swimming = SlotOf(seniors, "Swimming", 2);
        foreach (var a in assignments.Where(a => a.Period == 2 && a.SlotId != swimming.Id && slots.Single(s => s.Id == a.SlotId).BlockId == seniors.Id).ToList())
        {
            if (count[swimming.Id] >= swimming.Capacity + 1) break;
            count[a.SlotId]--;
            a.SlotId = swimming.Id;
            a.Source = "Staff";
            count[swimming.Id]++;
        }

        // A grade 3 camper in Horseback (grades 5–8) for Period 3.
        var young = assignments.First(a => a.Period == 3 && Who(a).Grade == 3);
        count[young.SlotId]--;
        young.SlotId = SlotOf(juniors, "Horseback", 3).Id;
        young.Source = "Staff";
        count[young.SlotId]++;

        db.Set<ActivityAssignment>().AddRange(assignments);
        foreach (var s in slots) s.Assigned = count[s.Id];
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Moves one ready, paid, not-yet-arrived Girls G3–5 camper (grade 4 or 5, so Climbing is open to
    /// her) with her order into Pastor Dave Kim's household. Returns her registration id.
    /// </summary>
    static async Task<int?> MoveCamperToKimAsync(CampDbContext db, Session session, CancellationToken ct)
    {
        var kim = await db.Households.Include(h => h.Members).FirstOrDefaultAsync(h => h.Email == GroupsSeed.LeaderEmail, ct);
        if (kim is null) return null;
        var roster = await OpsReadModel.LoadAsync(db, session.Id, ct);
        var pick = roster?.Campers
            .Where(c => c.Gender == Gender.Female && c.Grade is 4 or 5 && c.Reasons.Count == 0 && c.Placement?.CheckedInAt is null && c.ConfirmationCode is not null)
            .OrderBy(c => c.RegistrationId).FirstOrDefault();
        if (pick is null) return null;

        var reg = await db.Registrations.Include(r => r.Order).Include(r => r.Person).Include(r => r.WaiverAcceptances).SingleAsync(r => r.Id == pick.RegistrationId, ct);
        var dave = kim.Members.Where(m => m.IsAdult).OrderBy(m => m.Id).First();
        reg.HouseholdId = kim.Id;
        reg.Person.HouseholdId = kim.Id;
        reg.Person.LastName = kim.Name;
        if (reg.Order is not null) reg.Order.HouseholdId = kim.Id;
        foreach (var w in reg.WaiverAcceptances) w.SignerName = dave.FullName;
        if (!await db.Set<OpsPickupAdult>().AnyAsync(p => p.HouseholdId == kim.Id, ct))
            db.Set<OpsPickupAdult>().Add(new OpsPickupAdult { HouseholdId = kim.Id, Name = dave.FullName, Relationship = "Parent", Phone = kim.Phone });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return reg.Id;
    }
}
