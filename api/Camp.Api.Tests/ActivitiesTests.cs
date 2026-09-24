using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Activities;
using Camp.Api.Features.Ops;
using Camp.Api.Features.Setup;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Camp.Api.Tests;

/// <summary>
/// Activities slice: K8 catalog, R4 choices at checkout (ranked fallback, just-filled 409, no oversell,
/// grade limits, one camper's choices never leak to another), R5 cabinmate requests. Checkout tests
/// run on their own copy of Overnight Camp with small slots so they never share seats.
/// </summary>
public class ActivitiesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    readonly FakeFiservGateway _gateway = (FakeFiservGateway)factory.Services.GetRequiredService<IPaymentGateway>();

    // ── K8 catalog ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Catalog_is_for_admins_only()
    {
        const string url = "/api/admin/setup/activities";
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsFamily()).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsStaff("cet")).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsStaff("cet")).PostAsJsonAsync(url, Input("Tennis"))).StatusCode);
        var body = await Json(await (await Admin()).GetAsync(url));
        Assert.Contains(body.GetProperty("activities").EnumerateArray(), a => a.GetProperty("name").GetString() == "Archery");
    }

    [Fact]
    public async Task Admin_creates_and_edits_an_activity_and_both_are_audited()
    {
        var admin = await Admin();
        var created = await Json(await admin.PostAsJsonAsync("/api/admin/setup/activities", Input("Fishing")));
        var id = created.GetProperty("id").GetInt32();

        var edited = await admin.PutAsJsonAsync($"/api/admin/setup/activities/{id}", Input("Fishing") with { GradeMin = 5, StaffRatio = 10, Space = "Dock" });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        await factory.WithDb(async db =>
        {
            var a = await db.Set<Activity>().SingleAsync(x => x.Id == id);
            Assert.Equal((5, 10, "Dock"), (a.GradeMin, a.StaffRatio, a.Space));
            var events = await db.AuditEvents.Where(e => e.EntityType == "Activity" && e.EntityId == id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToListAsync();
            Assert.Contains(events, e => e.Action == "activity.created");
            var update = Assert.Single(events, e => e.Action == "activity.updated");
            var changes = await db.Set<AuditChange>().Where(c => c.AuditEventId == update.Id).ToListAsync();
            Assert.Contains(changes, c => c.Field == "Grades" && c.Before == "Grades 3–8" && c.After == "Grades 5–8");
            Assert.Contains(changes, c => c.Field == "Space" && c.After == "Dock");
            return 0;
        });
    }

    [Fact]
    public async Task Catalog_refuses_outside_images_duplicate_names_and_bad_grades()
    {
        var admin = await Admin();
        var res = await admin.PostAsJsonAsync("/api/admin/setup/activities", Input("Kayaking") with { ImageUrl = "https://example.com/kayak.jpg" });
        Assert.Contains("imageUrl", await Errors(res));
        Assert.Contains("name", await Errors(await admin.PostAsJsonAsync("/api/admin/setup/activities", Input("Archery"))));
        Assert.Contains("gradeMin", await Errors(await admin.PostAsJsonAsync("/api/admin/setup/activities", Input("Kayaking") with { GradeMin = 8, GradeMax = 3 })));
    }

    [Fact]
    public async Task Default_capacity_cannot_drop_below_campers_already_placed()
    {
        var admin = await Admin();
        var climbing = await factory.WithDb(db => db.Set<Activity>().SingleAsync(a => a.Name == "Climbing"));
        // Seeded: Juniors' Climbing in Period 2 of ON Session 3 has 23 campers.
        var res = await admin.PutAsJsonAsync($"/api/admin/setup/activities/{climbing.Id}", FromActivity(climbing) with { DefaultCapacity = 20 });
        Assert.Contains("defaultCapacity", await Errors(res));
        Assert.Equal(24, await factory.WithDb(db => db.Set<Activity>().Where(a => a.Id == climbing.Id).Select(a => a.DefaultCapacity).SingleAsync()));
    }

    // ── R4 at checkout ───────────────────────────────────────────────────────

    [Fact]
    public async Task Checkout_places_each_period_at_the_highest_ranked_choice_with_room()
    {
        var s = await IsolatedOvernight(capacity: 1);
        var first = await NewFamily("Rank-A", 4);
        var second = await NewFamily("Rank-B", 4);
        var ids = await ActivityIds();

        var a = await CheckoutHttp(first, s, [new ActivityChoice(1, [ids["Climbing"], ids["Archery"]]), new ActivityChoice(2, [ids["Crafts"]])]);
        Assert.Equal(HttpStatusCode.OK, a.StatusCode);
        var b = await CheckoutHttp(second, s, [new ActivityChoice(1, [ids["Climbing"], ids["Archery"], ids["Crafts"]])]);
        Assert.Equal(HttpStatusCode.OK, b.StatusCode);

        var placed = await Placed(s);
        Assert.Equal("Climbing", placed[(first.PersonIds[0], 1)]);
        Assert.Equal("Crafts", placed[(first.PersonIds[0], 2)]);
        Assert.Equal("Archery", placed[(second.PersonIds[0], 1)]); // Climbing was taken, so the 2nd choice
        await factory.WithDb(async db =>
        {
            var prefs = await db.Set<ActivityPreference>().Join(db.Registrations.Where(r => r.PersonId == second.PersonIds[0]), p => p.RegistrationId, r => r.Id, (p, r) => p)
                .OrderBy(p => p.Rank).Select(p => p.ActivityId).ToListAsync();
            Assert.Equal([ids["Climbing"], ids["Archery"], ids["Crafts"]], prefs);
            return 0;
        });
        await AssertCountersMatchRows(s);
    }

    [Fact]
    public async Task When_every_ranked_choice_is_full_checkout_answers_409_with_alternatives_and_keeps_nothing()
    {
        var s = await IsolatedOvernight(capacity: 1);
        var ids = await ActivityIds();
        Assert.Equal(HttpStatusCode.OK, (await CheckoutHttp(await NewFamily("Full-A", 4), s, [new ActivityChoice(1, [ids["Climbing"]])])).StatusCode);

        var late = await NewFamily("Full-B", 5);
        var charges = _gateway.ChargeCount;
        var res = await CheckoutHttp(late, s, [new ActivityChoice(1, [ids["Climbing"]])]);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var conflict = (await Json(res)).GetProperty("conflicts")[0];
        Assert.Equal(late.PersonIds[0], conflict.GetProperty("personId").GetInt32());
        Assert.Equal(1, conflict.GetProperty("period").GetInt32());
        var alternatives = conflict.GetProperty("alternatives").EnumerateArray().Select(a => a.GetProperty("name").GetString()).ToList();
        Assert.Contains("Archery", alternatives);
        Assert.DoesNotContain("Climbing", alternatives);

        // The whole checkout rolled back: no order, no registration, the pool seat is free again.
        await factory.WithDb(async db =>
        {
            Assert.False(await db.Registrations.AnyAsync(r => r.PersonId == late.PersonIds[0]));
            Assert.False(await db.Orders.AnyAsync(o => o.HouseholdId == late.HouseholdId));
            Assert.Equal(1, await db.CapacityPools.Where(p => p.Id == s.PoolId).Select(p => p.Reserved).SingleAsync());
            return 0;
        });
        Assert.Equal(charges, _gateway.ChargeCount); // the card was never charged
        await AssertCountersMatchRows(s);
    }

    [Fact]
    public async Task Concurrent_checkouts_never_oversell_a_slot()
    {
        var s = await IsolatedOvernight(capacity: 3);
        var ids = await ActivityIds();
        var families = new List<Family>();
        for (var i = 0; i < 8; i++) families.Add(await NewFamily($"Race-{i}", 4));

        var results = await Task.WhenAll(families.Select(f => CheckoutHttp(f, s, [new ActivityChoice(1, [ids["Climbing"]])])));

        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        await factory.WithDb(async db =>
        {
            var slot = await db.Set<ActivitySlot>().SingleAsync(x => x.SessionId == s.SessionId && x.ActivityId == ids["Climbing"] && x.Period == 1 && x.BlockId == s.JuniorsId);
            Assert.Equal(3, slot.Assigned);
            Assert.Equal(3, await db.Set<ActivityAssignment>().CountAsync(a => a.SlotId == slot.Id));
            Assert.Equal(3, await db.Registrations.CountAsync(r => r.SessionId == s.SessionId));
            return 0;
        });
    }

    [Fact]
    public async Task Grade_limits_are_enforced_on_the_server()
    {
        var s = await IsolatedOvernight(capacity: 5);
        var ids = await ActivityIds();
        var third = await NewFamily("Grade-3", 3);
        // Horseback is grades 5–8 and Climbing 4–8: neither is offered to a 3rd grader.
        var res = await CheckoutHttp(third, s, [new ActivityChoice(1, [ids["Horseback"]]), new ActivityChoice(2, [ids["Climbing"]])]);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("isn't offered to grade 3", await res.Content.ReadAsStringAsync());
        // Zipline is in the catalog but not on this schedule.
        var seventh = await NewFamily("Grade-7", 7);
        Assert.Equal(HttpStatusCode.BadRequest, (await CheckoutHttp(seventh, s, [new ActivityChoice(1, [ids["Zipline"]])])).StatusCode);
        Assert.Equal(0, await factory.WithDb(db => db.Registrations.CountAsync(r => r.SessionId == s.SessionId)));
    }

    [Fact]
    public async Task Each_camper_gets_their_own_choices_in_their_own_block()
    {
        var s = await IsolatedOvernight(capacity: 5);
        var ids = await ActivityIds();
        var family = await NewFamily("Siblings", 4, 7);
        var (junior, senior) = (family.PersonIds[0], family.PersonIds[1]);
        var res = await CheckoutHttp(family, s, new Dictionary<int, List<ActivityChoice>>
        {
            [junior] = [new ActivityChoice(1, [ids["Crafts"]]), new ActivityChoice(2, [ids["Archery"]]), new ActivityChoice(3, [ids["Swimming"]])],
            [senior] = [new ActivityChoice(1, [ids["Horseback"]]), new ActivityChoice(2, [ids["Canoeing"]]), new ActivityChoice(3, [ids["Climbing"]])],
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var placed = await Placed(s);
        Assert.Equal(["Crafts", "Archery", "Swimming"], new[] { placed[(junior, 1)], placed[(junior, 2)], placed[(junior, 3)] });
        Assert.Equal(["Horseback", "Canoeing", "Climbing"], new[] { placed[(senior, 1)], placed[(senior, 2)], placed[(senior, 3)] });
        await factory.WithDb(async db =>
        {
            var blocks = await db.Set<ActivityAssignment>().Join(db.Set<ActivitySlot>(), a => a.SlotId, x => x.Id, (a, x) => new { a.RegistrationId, x.BlockId })
                .Join(db.Registrations, x => x.RegistrationId, r => r.Id, (x, r) => new { r.PersonId, x.BlockId })
                .Where(x => x.PersonId == junior || x.PersonId == senior).ToListAsync();
            Assert.All(blocks.Where(b => b.PersonId == junior), b => Assert.Equal(s.JuniorsId, b.BlockId));
            Assert.All(blocks.Where(b => b.PersonId == senior), b => Assert.Equal(s.SeniorsId, b.BlockId));
            return 0;
        });
    }

    [Fact]
    public async Task A_declined_card_gives_the_activity_seats_back()
    {
        var s = await IsolatedOvernight(capacity: 1);
        var ids = await ActivityIds();
        var family = await NewFamily("Declined", 4);
        var res = await CheckoutHttp(family, s, [new ActivityChoice(1, [ids["Climbing"]])], card: "4000000000000002");
        Assert.Equal(HttpStatusCode.PaymentRequired, res.StatusCode);
        await AssertCountersMatchRows(s);
        Assert.Empty(await Placed(s));
        // The seat is free for the next family.
        Assert.Equal(HttpStatusCode.OK, (await CheckoutHttp(await NewFamily("After-decline", 4), s, [new ActivityChoice(1, [ids["Climbing"]])])).StatusCode);
    }

    [Fact]
    public async Task Sessions_without_a_schedule_refuse_activity_choices()
    {
        var family = await NewFamily("DayCamp", 4);
        var dayCamp = await factory.WithDb(db => db.Sessions.Where(x => x.Program.Slug == "day-camp-atlanta").OrderBy(x => x.Id).Select(x => x.Id).FirstAsync());
        var ids = await ActivityIds();
        var res = await family.Client.PostAsJsonAsync("/api/checkout", Request(family, dayCamp, new() { [family.PersonIds[0]] = [new ActivityChoice(1, [ids["Archery"]])] }, [], await Waivers(dayCamp), health: true));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("doesn't take activity choices", await res.Content.ReadAsStringAsync());
        Assert.False((await Json(await family.Client.GetAsync($"/api/sessions/{dayCamp}/register-context"))).GetProperty("activities").GetBoolean());
    }

    // ── R5 cabinmates ────────────────────────────────────────────────────────

    [Fact]
    public async Task Cabinmate_requests_are_stored_matched_for_O3_and_limited_to_two()
    {
        var s = await IsolatedOvernight(capacity: 10);
        var ids = await ActivityIds();
        var choices = new List<ActivityChoice> { new(1, [ids["Archery"]]) };
        var friend = await NewFamily("Friend", 4);
        Assert.Equal(HttpStatusCode.OK, (await CheckoutHttp(friend, s, choices)).StatusCode);
        var friendCode = await factory.WithDb(db => db.Orders.Where(o => o.HouseholdId == friend.HouseholdId).Select(o => o.ConfirmationCode).SingleAsync());
        var friendReg = await factory.WithDb(db => db.Registrations.Where(r => r.PersonId == friend.PersonIds[0]).Select(r => r.Id).SingleAsync());

        var asker = await NewFamily("Asker", 4);
        // The check says yes or no and nothing else.
        var check = await Json(await asker.Client.PostAsJsonAsync($"/api/sessions/{s.SessionId}/cabinmates/check", new { name = "Kid0 Friend", contact = friend.Email }));
        Assert.True(check.GetProperty("matched").GetBoolean());
        Assert.False((await Json(await asker.Client.PostAsJsonAsync($"/api/sessions/{s.SessionId}/cabinmates/check", new { name = "Kid0 Friend", contact = "WS-000000" }))).GetProperty("matched").GetBoolean());

        var three = new List<CabinmateInput> { new("A B", "a@example.com"), new("C D", "c@example.com"), new("E F", "e@example.com") };
        var tooMany = await CheckoutHttp(asker, s, choices, cabinmates: three);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Contains("up to 2 friends", await tooMany.Content.ReadAsStringAsync());

        var two = new List<CabinmateInput> { new("Kid0", friendCode), new("Not Here Yet", "later@example.com") };
        Assert.Equal(HttpStatusCode.OK, (await CheckoutHttp(asker, s, choices, cabinmates: two)).StatusCode);
        var askerReg = await factory.WithDb(db => db.Registrations.Where(r => r.PersonId == asker.PersonIds[0]).Select(r => r.Id).SingleAsync());
        await factory.WithDb(async db =>
        {
            var rows = await db.Set<CabinmateRequest>().Where(c => c.RegistrationId == askerReg).OrderBy(c => c.Id).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.Equal(friendReg, rows[0].MatchedRegistrationId);
            Assert.Null(rows[1].MatchedRegistrationId);
            Assert.True(await db.Set<OpsBuddyRequest>().AnyAsync(b => b.RegistrationId == askerReg && b.RequestedRegistrationId == friendReg));
            return 0;
        });

        // The friend who wasn't registered yet signs up later: the waiting request finds her.
        var later = await NewFamily("Later", 5, email: "later@example.com", kidFirst: "Not Here", kidLast: "Yet");
        Assert.Equal(HttpStatusCode.OK, (await CheckoutHttp(later, s, choices)).StatusCode);
        await factory.WithDb(async db =>
        {
            var laterReg = await db.Registrations.Where(r => r.PersonId == later.PersonIds[0]).Select(r => r.Id).SingleAsync();
            Assert.True(await db.Set<CabinmateRequest>().AnyAsync(c => c.RegistrationId == askerReg && c.MatchedRegistrationId == laterReg));
            Assert.True(await db.Set<OpsBuddyRequest>().AnyAsync(b => b.RegistrationId == askerReg && b.RequestedRegistrationId == laterReg));
            return 0;
        });
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    internal sealed record Overnight(int SessionId, int PoolId, int JuniorsId, int SeniorsId);
    internal sealed record Family(HttpClient Client, int HouseholdId, string Email, List<int> PersonIds);

    Task<HttpClient> Admin() => factory.SignInAsStaff("admin", "Alex Morgan");

    static ActivityInput Input(string name) => new(name, "Outdoor skills", "Something to do at camp.", "Water bottle.", "/images/activities/archery.svg", 3, 8, 24, 8, "", "Field", true);

    static ActivityInput FromActivity(Activity a) => new(a.Name, a.Category, a.Description, a.WhatToBring, a.ImageUrl, a.GradeMin, a.GradeMax, a.DefaultCapacity, a.StaffRatio, a.Instructor, a.Space, a.IsActive);

    Task<Dictionary<string, int>> ActivityIds() => factory.WithDb(db => db.Set<Activity>().ToDictionaryAsync(a => a.Name, a => a.Id));

    /// <summary>A private copy of Overnight Camp: one pool for grades 3–8 and both blocks, every slot at <paramref name="capacity"/>.</summary>
    async Task<Overnight> IsolatedOvernight(int capacity)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampDbContext>();
        var template = await db.Sessions.AsNoTracking().SingleAsync(x => x.Program.Slug == "overnight-camp" && x.Name == "Session 3");
        var session = new Session
        {
            ProgramId = template.ProgramId,
            Name = $"Test {Guid.NewGuid():N}"[..20],
            StartDate = template.StartDate,
            EndDate = template.EndDate,
            PriceCents = template.PriceCents,
            DepositCents = template.DepositCents,
            PlanInstallments = template.PlanInstallments,
            BalanceDueDate = template.BalanceDueDate,
        };
        var pool = new CapacityPool { Session = session, Name = "Grades 3–8", GradeMin = 3, GradeMax = 8, Capacity = 50 };
        db.CapacityPools.Add(pool);
        await db.SaveChangesAsync();
        var juniors = new ActivityBlock { SessionId = session.Id, Name = "Juniors", GradeMin = 3, GradeMax = 5, SortOrder = 1 };
        var seniors = new ActivityBlock { SessionId = session.Id, Name = "Seniors", GradeMin = 6, GradeMax = 8, SortOrder = 2 };
        db.AddRange(juniors, seniors);
        await db.SaveChangesAsync();
        var scheduled = await db.Set<Activity>().Where(a => a.Name != "Zipline" && a.SortOrder <= 6).ToListAsync();
        foreach (var block in new[] { juniors, seniors })
            foreach (var a in scheduled)
                for (var p = 1; p <= 3; p++)
                    db.Add(new ActivitySlot { SessionId = session.Id, BlockId = block.Id, ActivityId = a.Id, Period = p, Capacity = capacity, Instructor = "Test", Space = a.Space });
        await db.SaveChangesAsync();
        return new(session.Id, pool.Id, juniors.Id, seniors.Id);
    }

    /// <summary>A signed-in household with one child per grade (for ON Session 3's July 2028 start).</summary>
    async Task<Family> NewFamily(string name, int grade, int? secondGrade = null, string? email = null, string? kidFirst = null, string? kidLast = null)
    {
        var mail = email ?? $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com";
        var client = await factory.SignInAsFamily(mail, "Parent", name);
        var (householdId, ids) = await factory.WithDb(async db =>
        {
            var h = await db.Households.SingleAsync(x => x.Email == mail);
            var kids = new List<Person>();
            foreach (var (g, i) in new[] { grade, secondGrade ?? 0 }.Where(g => g > 0).Select((g, i) => (g, i)))
                kids.Add(new Person { HouseholdId = h.Id, FirstName = kidFirst ?? $"Kid{i}", LastName = kidLast ?? name, DateOfBirth = new DateOnly(2028 - g - 6, 10, 1), Gender = Gender.Female });
            db.People.AddRange(kids);
            await db.SaveChangesAsync();
            return (h.Id, kids.Select(k => k.Id).ToList());
        });
        return new(client, householdId, mail, ids);
    }

    Task<HttpResponseMessage> CheckoutHttp(Family f, Overnight s, List<ActivityChoice> choices, List<CabinmateInput>? cabinmates = null, string card = "4242424242424242") =>
        CheckoutHttp(f, s, f.PersonIds.ToDictionary(id => id, _ => choices), cabinmates, card);

    async Task<HttpResponseMessage> CheckoutHttp(Family f, Overnight s, Dictionary<int, List<ActivityChoice>> choices, List<CabinmateInput>? cabinmates = null, string card = "4242424242424242") =>
        await f.Client.PostAsJsonAsync("/api/checkout", Request(f, s.SessionId, choices, cabinmates ?? [], await Waivers(s.SessionId), card: card));

    object Request(Family f, int sessionId, Dictionary<int, List<ActivityChoice>> choices, List<CabinmateInput> cabinmates, List<WaiverTemplate> w, bool health = false, string card = "4242424242424242")
    {
        return new
        {
            idempotencyKey = Guid.NewGuid().ToString(),
            sessionId,
            participants = f.PersonIds.Select(id => new
            {
                personId = id,
                answers = new Dictionary<string, string> { ["tshirt"] = "Youth M", ["swim"] = "Beginner" },
                health = health ? new { physicianName = "Dr. Test", physicianPhone = "555-0100" } : null,
                activities = choices.GetValueOrDefault(id),
                cabinmates = cabinmates.Count > 0 ? cabinmates : null,
            }),
            householdAnswers = new Dictionary<string, string> { ["church"] = "No" },
            waivers = w.SelectMany(x => x.PerParticipant ? f.PersonIds.Select(p => new { waiverId = x.Id, personId = (int?)p, signerName = "Parent" }) : [new { waiverId = x.Id, personId = (int?)null, signerName = "Parent" }]),
            paymentOption = "Deposit",
            discountCode = (string?)null,
            cardToken = _gateway.Tokenize(card),
        };
    }

    Task<List<WaiverTemplate>> Waivers(int sessionId) =>
        factory.WithDb(db => db.WaiverTemplates.Where(x => db.Sessions.Any(ss => ss.Id == sessionId && ss.ProgramId == x.ProgramId)).ToListAsync());

    /// <summary>(person, period) → activity name for every place in the session.</summary>
    Task<Dictionary<(int, int), string>> Placed(Overnight s) => factory.WithDb(async db =>
        (await db.Set<ActivityAssignment>().Where(a => a.SessionId == s.SessionId)
            .Join(db.Set<ActivitySlot>(), a => a.SlotId, x => x.Id, (a, x) => new { a.RegistrationId, a.Period, x.ActivityId })
            .Join(db.Set<Activity>(), x => x.ActivityId, a => a.Id, (x, a) => new { x.RegistrationId, x.Period, a.Name })
            .Join(db.Registrations, x => x.RegistrationId, r => r.Id, (x, r) => new { r.PersonId, x.Period, x.Name })
            .ToListAsync()).ToDictionary(x => (x.PersonId, x.Period), x => x.Name));

    async Task AssertCountersMatchRows(Overnight s) => await factory.WithDb(async db =>
    {
        var slots = await db.Set<ActivitySlot>().Where(x => x.SessionId == s.SessionId).ToListAsync();
        foreach (var slot in slots)
            Assert.Equal(await db.Set<ActivityAssignment>().CountAsync(a => a.SlotId == slot.Id), slot.Assigned);
        return 0;
    });

    static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.Conflict, $"{(int)res.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement;
    }

    static async Task<List<string>> Errors(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        return [.. JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name)];
    }
}

/// <summary>
/// Activities slice on the seeded ON Session 3: the per-block schedule ties out to the roster, O1
/// reads the new places, O4's assign-from-preferences, move and keep are audited, and a family
/// chooses after registering from the F1 checklist.
/// </summary>
public class ActivitiesScheduleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    Task<int> SessionId() => OpsTestData.SessionId(factory);

    [Fact]
    public async Task Each_block_ties_out_to_the_roster_and_the_seeded_conflicts_show()
    {
        var id = await SessionId();
        var staff = await factory.SignInAsStaff("finance", "Marcus Lee"); // any staff can read
        var juniors = await Json(await staff.GetAsync($"/api/admin/ops/sessions/{id}/activities"));
        var blocks = juniors.GetProperty("blocks").EnumerateArray().ToList();
        Assert.Equal(["Juniors", "Seniors"], blocks.Select(b => b.GetProperty("name").GetString()));
        Assert.Equal(186, blocks.Sum(b => b.GetProperty("campers").GetInt32()));
        Assert.Equal(90, blocks[0].GetProperty("campers").GetInt32()); // Boys G3–5 46 + Girls G3–5 44
        Assert.Equal(96, blocks[1].GetProperty("campers").GetInt32()); // Boys G6–8 50 + Girls G6–8 46

        var seniors = await Json(await staff.GetAsync($"/api/admin/ops/sessions/{id}/activities?blockId={blocks[1].GetProperty("id").GetInt32()}"));
        foreach (var grid in new[] { juniors, seniors })
        {
            var campers = grid.GetProperty("block").GetProperty("campers").GetInt32();
            Assert.Equal(6, grid.GetProperty("rows").GetArrayLength());
            foreach (var p in grid.GetProperty("periods").EnumerateArray())
            {
                Assert.Equal(144, p.GetProperty("capacity").GetInt32());
                Assert.Equal(campers, p.GetProperty("placed").GetInt32() + p.GetProperty("waiting").GetInt32() + p.GetProperty("notChosen").GetInt32());
            }
        }
        Assert.Equal(23, Cell(juniors, "Climbing", 2).GetProperty("assigned").GetInt32());
        Assert.Equal("Over capacity", Cell(seniors, "Swimming", 2).GetProperty("status").GetString());
        var kinds = seniors.GetProperty("conflicts").EnumerateArray().Select(c => c.GetProperty("kind").GetString()).ToList();
        Assert.Contains("DoubleBooked", kinds);
        Assert.Contains("OverCapacity", kinds);
        Assert.Contains("OutsideGrades", juniors.GetProperty("conflicts").EnumerateArray().Select(c => c.GetProperty("kind").GetString()));

        // Seeding activities left the canonical readiness numbers alone.
        var readiness = await Json(await staff.GetAsync($"/api/admin/ops/sessions/{id}/readiness"));
        Assert.Equal((186, 9, 159, 27), (readiness.GetProperty("registered").GetInt32(), readiness.GetProperty("waitlisted").GetInt32(),
            readiness.GetProperty("ready").GetInt32(), readiness.GetProperty("needsAttention").GetInt32()));
    }

    [Fact]
    public async Task O1_reads_activities_from_the_new_places()
    {
        var id = await SessionId();
        var body = await Json(await (await factory.SignInAsStaff()).GetAsync($"/api/admin/ops/sessions/{id}/readiness"));
        Assert.Equal(["Archery", "Swimming", "Climbing", "Horseback", "Crafts", "Canoeing"], body.GetProperty("activities").EnumerateArray().Select(a => a.GetString()));
        var roster = body.GetProperty("roster").EnumerateArray().ToList();
        var (names, chose) = await factory.WithDb(async db =>
        {
            var rows = await db.Set<ActivityAssignment>().Where(a => a.SessionId == id)
                .Join(db.Set<ActivitySlot>(), a => a.SlotId, s => s.Id, (a, s) => new { a.RegistrationId, a.Period, s.ActivityId })
                .Join(db.Set<Activity>(), x => x.ActivityId, a => a.Id, (x, a) => new { x.RegistrationId, x.Period, a.Name }).ToListAsync();
            var prefs = await db.Set<ActivityPreference>().Select(p => p.RegistrationId).Distinct().ToListAsync();
            return (rows.GroupBy(r => r.RegistrationId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Period).Select(x => x.Name).ToList()), prefs.ToHashSet());
        });
        foreach (var r in roster)
        {
            var reg = r.GetProperty("registrationId").GetInt32();
            var activity = r.GetProperty("activity");
            var expected = names.GetValueOrDefault(reg) ?? [];
            Assert.Equal(expected, activity.GetProperty("names").EnumerateArray().Select(n => n.GetString() ?? ""));
            if (expected.Count == 0)
                Assert.Equal(chose.Contains(reg) ? "Chosen" : "NotChosen", activity.GetProperty("state").GetString());
        }
        // Every 13th camper (and the Kim camper) hasn't chosen yet.
        Assert.InRange(roster.Count(r => r.GetProperty("activity").GetProperty("state").GetString() == "NotChosen"), 14, 15);
        // O5 shows the same summary.
        var checkIn = await Json(await (await factory.SignInAsStaff()).GetAsync($"/api/admin/ops/sessions/{id}/check-in"));
        var first = roster.First(r => r.GetProperty("activity").GetProperty("state").GetString() == "Assigned");
        var row = checkIn.GetProperty("rows").EnumerateArray().Single(x => x.GetProperty("registrationId").GetInt32() == first.GetProperty("registrationId").GetInt32());
        Assert.Equal(first.GetProperty("activity").GetProperty("label").GetString(), row.GetProperty("activity").GetString());
    }

    [Fact]
    public async Task Assign_from_preferences_previews_then_applies_and_is_audited()
    {
        var id = await SessionId();
        var diane = await factory.SignInAsStaff();
        var grid = await Json(await diane.GetAsync($"/api/admin/ops/sessions/{id}/activities"));
        var blockId = grid.GetProperty("block").GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsStaff("finance", "Marcus Lee")).PostAsJsonAsync($"/api/admin/ops/sessions/{id}/activities/assign", new { blockId })).StatusCode);

        var preview = await Json(await diane.PostAsJsonAsync($"/api/admin/ops/sessions/{id}/activities/assign/preview", new { blockId }));
        Assert.True(preview.GetProperty("places").GetInt32() > 0);
        var placedBefore = grid.GetProperty("periods").EnumerateArray().Sum(p => p.GetProperty("placed").GetInt32());
        // Previewing saves nothing.
        Assert.Equal(placedBefore, (await Json(await diane.GetAsync($"/api/admin/ops/sessions/{id}/activities"))).GetProperty("periods").EnumerateArray().Sum(p => p.GetProperty("placed").GetInt32()));

        var done = await Json(await diane.PostAsJsonAsync($"/api/admin/ops/sessions/{id}/activities/assign", new { blockId }));
        Assert.Equal(preview.GetProperty("places").GetInt32(), done.GetProperty("places").GetInt32());
        var after = await Json(await diane.GetAsync($"/api/admin/ops/sessions/{id}/activities"));
        Assert.Equal(placedBefore + done.GetProperty("places").GetInt32(), after.GetProperty("periods").EnumerateArray().Sum(p => p.GetProperty("placed").GetInt32()));
        Assert.Equal(0, after.GetProperty("pending").GetProperty("places").GetInt32());
        // Nobody went over capacity, and campers who never chose stay unplaced for their family.
        foreach (var row in after.GetProperty("rows").EnumerateArray())
            foreach (var cell in row.GetProperty("cells").EnumerateArray())
                Assert.True(cell.GetProperty("assigned").GetInt32() <= cell.GetProperty("capacity").GetInt32() || cell.GetProperty("status").GetString() == "Over capacity");
        Assert.Equal(grid.GetProperty("periods")[0].GetProperty("notChosen").GetInt32(), after.GetProperty("periods")[0].GetProperty("notChosen").GetInt32());
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(e => e.Action == "activities.assigned_from_preferences" && e.Actor.Contains("Diane"))));
        await AssertCountersMatchRows(id);
    }

    [Fact]
    public async Task Staff_move_a_camper_within_a_period_only_where_there_is_room_and_it_fits_their_grade()
    {
        var id = await SessionId();
        var diane = await factory.SignInAsStaff();
        var grid = await Json(await diane.GetAsync($"/api/admin/ops/sessions/{id}/activities"));
        var crafts = Cell(grid, "Crafts", 1);
        var camper = crafts.GetProperty("campers").EnumerateArray().First(c => c.GetProperty("grade").GetInt32() == 3 && !c.GetProperty("doubleBooked").GetBoolean());
        var reg = camper.GetProperty("registrationId").GetInt32();
        var from = crafts.GetProperty("slotId").GetInt32();
        var url = $"/api/admin/ops/sessions/{id}/activities/move";

        // Grade 3 can't do Climbing (4–8); another period is refused; a full slot is a 409.
        Assert.Equal(HttpStatusCode.BadRequest, (await diane.PostAsJsonAsync(url, new { registrationId = reg, fromSlotId = from, toSlotId = Cell(grid, "Climbing", 1).GetProperty("slotId").GetInt32() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await diane.PostAsJsonAsync(url, new { registrationId = reg, fromSlotId = from, toSlotId = Cell(grid, "Archery", 2).GetProperty("slotId").GetInt32() })).StatusCode);
        var archery = Cell(grid, "Archery", 1).GetProperty("slotId").GetInt32();
        await factory.WithDb(db => db.Set<ActivitySlot>().Where(s => s.Id == archery).ExecuteUpdateAsync(s => s.SetProperty(x => x.Capacity, x => x.Assigned)));
        Assert.Equal(HttpStatusCode.Conflict, (await diane.PostAsJsonAsync(url, new { registrationId = reg, fromSlotId = from, toSlotId = archery })).StatusCode);
        await factory.WithDb(db => db.Set<ActivitySlot>().Where(s => s.Id == archery).ExecuteUpdateAsync(s => s.SetProperty(x => x.Capacity, 24)));

        var ok = await diane.PostAsJsonAsync(url, new { registrationId = reg, fromSlotId = from, toSlotId = archery });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        await factory.WithDb(async db =>
        {
            Assert.True(await db.Set<ActivityAssignment>().AnyAsync(a => a.RegistrationId == reg && a.SlotId == archery && a.Source == "Staff"));
            Assert.False(await db.Set<ActivityAssignment>().AnyAsync(a => a.RegistrationId == reg && a.SlotId == from));
            Assert.True(await db.AuditEvents.AnyAsync(e => e.Action == "activities.camper_moved" && e.EntityId == reg.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return 0;
        });
        await AssertCountersMatchRows(id);
    }

    [Fact]
    public async Task Keeping_one_activity_resolves_a_double_booking_and_is_audited()
    {
        var id = await SessionId();
        var diane = await factory.SignInAsStaff();
        var juniors = await Json(await diane.GetAsync($"/api/admin/ops/sessions/{id}/activities"));
        var seniorsId = juniors.GetProperty("blocks")[1].GetProperty("id").GetInt32();
        var grid = await Json(await diane.GetAsync($"/api/admin/ops/sessions/{id}/activities?blockId={seniorsId}"));
        var conflict = grid.GetProperty("conflicts").EnumerateArray().First(c => c.GetProperty("kind").GetString() == "DoubleBooked");
        var reg = conflict.GetProperty("registrationId").GetInt32();
        var keep = Cell(grid, "Climbing", 2).GetProperty("slotId").GetInt32();

        var res = await diane.PostAsJsonAsync($"/api/admin/ops/sessions/{id}/activities/keep", new { registrationId = reg, period = 2, keepSlotId = keep });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var after = await Json(await diane.GetAsync($"/api/admin/ops/sessions/{id}/activities?blockId={seniorsId}"));
        Assert.DoesNotContain(after.GetProperty("conflicts").EnumerateArray(), c => c.GetProperty("kind").GetString() == "DoubleBooked" && c.GetProperty("registrationId").GetInt32() == reg);
        Assert.Equal(24, Cell(after, "Swimming", 2).GetProperty("assigned").GetInt32()); // back to capacity
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(e => e.Action == "activities.double_booking_resolved")));
        await AssertCountersMatchRows(id);
    }

    [Fact]
    public async Task A_family_chooses_activities_after_registering_from_the_F1_checklist()
    {
        var dave = await factory.SignInAsFamily(Features.Groups.GroupsSeed.LeaderEmail, "Dave", "Kim");
        var overview = await Json(await dave.GetAsync("/api/family/overview"));
        var item = overview.GetProperty("checklist").EnumerateArray().Single(c => c.GetProperty("kind").GetString() == "activities");
        Assert.False(item.GetProperty("done").GetBoolean());
        Assert.Equal("Choose activities", item.GetProperty("action").GetString());
        var href = item.GetProperty("href").GetString() ?? "";
        var reg = int.Parse(href.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);

        var context = await Json(await dave.GetAsync($"/api/family/activities/{reg}"));
        Assert.Equal("Juniors", context.GetProperty("block").GetString());
        var choices = context.GetProperty("periods").EnumerateArray().Select(p => new
        {
            period = p.GetProperty("period").GetInt32(),
            ranked = p.GetProperty("options").EnumerateArray().Where(o => o.GetProperty("remaining").GetInt32() > 0).Take(2).Select(o => o.GetProperty("activityId").GetInt32()).ToList(),
        }).ToList();

        // Another family can't see or change it.
        var maria = await factory.SignInAsFamily();
        Assert.Equal(HttpStatusCode.NotFound, (await maria.GetAsync($"/api/family/activities/{reg}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await maria.PutAsJsonAsync($"/api/family/activities/{reg}", new { choices })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await dave.PutAsJsonAsync($"/api/family/activities/{reg}", new { choices })).StatusCode);
        var done = (await Json(await dave.GetAsync("/api/family/overview"))).GetProperty("checklist").EnumerateArray().Single(c => c.GetProperty("kind").GetString() == "activities");
        Assert.True(done.GetProperty("done").GetBoolean());
        Assert.True(await factory.WithDb(db => db.Set<ActivityAssignment>().CountAsync(a => a.RegistrationId == reg && a.Source == "Family")) == 3);
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(e => e.Action == "activities.chosen" && e.EntityId == reg.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        await AssertCountersMatchRows(await SessionId());
    }

    static JsonElement Cell(JsonElement grid, string activity, int period) =>
        grid.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("name").GetString() == activity)
            .GetProperty("cells").EnumerateArray().Single(c => c.GetProperty("period").GetInt32() == period);

    async Task AssertCountersMatchRows(int sessionId) => await factory.WithDb(async db =>
    {
        foreach (var slot in await db.Set<ActivitySlot>().Where(x => x.SessionId == sessionId).ToListAsync())
            Assert.Equal(await db.Set<ActivityAssignment>().CountAsync(a => a.SlotId == slot.Id), slot.Assigned);
        return 0;
    });

    static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement;
    }
}
