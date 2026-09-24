using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Camp.Api.Domain;
using Camp.Api.Features.Ops;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Tests;

/// <summary>Slice 7: session readiness (O1), group board (O2), rooming (O3), check-in and check-out (O5).</summary>
public class OpsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    Task<int> SessionId() => OpsTestData.SessionId(factory);

    // ── access ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("readiness")]
    [InlineData("groups")]
    [InlineData("rooming")]
    [InlineData("check-in")]
    public async Task Ops_pages_reject_anonymous_family_and_host(string page)
    {
        var url = $"/api/admin/ops/sessions/{await SessionId()}/{page}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsFamily()).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsStaff("host", "Grace Patel")).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await factory.SignInAsStaff("admin", "Alex Morgan")).GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Finance_can_read_readiness_but_cannot_move_remind_or_check_in()
    {
        var id = await SessionId();
        var finance = await factory.SignInAsStaff("finance", "Marcus Lee");
        var roster = await Json(await finance.GetAsync($"/api/admin/ops/sessions/{id}/readiness"));
        var reg = roster.GetProperty("roster")[0].GetProperty("registrationId").GetInt32();

        Assert.Equal(HttpStatusCode.Forbidden, (await finance.PutAsJsonAsync($"/api/admin/ops/groups/placements/{reg}", new { groupId = (int?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.PutAsJsonAsync($"/api/admin/ops/rooming/placements/{reg}", new { cabinId = (int?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.PostAsJsonAsync($"/api/admin/ops/sessions/{id}/reminders", new { registrationIds = new[] { reg } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.PostAsJsonAsync($"/api/admin/ops/check-in/{reg}", new { overrideReason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsFamily()).PostAsJsonAsync($"/api/admin/ops/check-in/{reg}", new { overrideReason = "x" })).StatusCode);
    }

    // ── O1 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Readiness_shows_the_canonical_session_3_numbers_and_every_count_ties_to_the_roster()
    {
        var staff = await factory.SignInAsStaff();
        var body = await Json(await staff.GetAsync($"/api/admin/ops/sessions/{await SessionId()}/readiness"));

        Assert.Equal(200, body.GetProperty("capacity").GetInt32());
        Assert.Equal(186, body.GetProperty("registered").GetInt32());
        Assert.Equal(9, body.GetProperty("waitlisted").GetInt32());
        Assert.Equal(159, body.GetProperty("ready").GetInt32());
        Assert.Equal(27, body.GetProperty("needsAttention").GetInt32());
        var breakdown = body.GetProperty("breakdown");
        Assert.Equal(19, breakdown.GetProperty("health").GetInt32());
        Assert.Equal(6, breakdown.GetProperty("waivers").GetInt32());
        Assert.Equal(8, breakdown.GetProperty("balance").GetInt32());
        // 19 + 6 + 8 = 33 reasons across 27 people: 6 campers have two.
        Assert.Equal(33, body.GetProperty("overlap").GetProperty("totalReasons").GetInt32());
        Assert.Equal(6, body.GetProperty("overlap").GetProperty("twoReasons").GetInt32());
        Assert.True(body.GetProperty("usesCampDoc").GetBoolean());

        var roster = body.GetProperty("roster").EnumerateArray().ToList();
        Assert.Equal(186, roster.Count);
        Assert.Equal(27, roster.Count(r => r.GetProperty("reasons").GetArrayLength() > 0));
        Assert.Equal(19, roster.Count(r => r.GetProperty("health").GetString() == "Incomplete"));
        Assert.Equal(6, roster.Count(r => r.GetProperty("waiver").GetString() != "Complete"));
        Assert.Equal(8, roster.Count(r => r.GetProperty("payment").GetString() == "Balance due"));
        // A row that says "Balance due" carries the balance reason, and the reverse.
        Assert.All(roster, r => Assert.Equal(
            r.GetProperty("payment").GetString() == "Balance due",
            r.GetProperty("reasons").EnumerateArray().Any(x => x.GetString() == "Balance")));
        // Campers without a bed read "Unassigned", which is also the value of the page's cabin filter.
        Assert.Equal(14, roster.Count(r => r.GetProperty("cabin").GetString() == "Unassigned"));

        var pools = body.GetProperty("pools").EnumerateArray().ToList();
        Assert.Equal([46, 50, 44, 46], pools.Select(p => p.GetProperty("reserved").GetInt32()));
        var boys68 = pools.Single(p => p.GetProperty("name").GetString() == "Boys G6–8");
        Assert.Equal("full", boys68.GetProperty("state").GetString());
        Assert.Equal(9, boys68.GetProperty("waitlisted").GetInt32());
    }

    [Fact]
    public async Task Reminders_email_each_family_with_open_items_once_and_skip_ready_campers()
    {
        var id = await SessionId();
        var staff = await factory.SignInAsStaff();
        var roster = (await Json(await staff.GetAsync($"/api/admin/ops/sessions/{id}/readiness"))).GetProperty("roster").EnumerateArray().ToList();
        var ready = roster.Where(r => r.GetProperty("reasons").GetArrayLength() == 0).Select(RegId).ToList();
        var open = roster.Where(r => r.GetProperty("reasons").GetArrayLength() > 0).Select(RegId).ToList();

        var nothing = await staff.PostAsJsonAsync($"/api/admin/ops/sessions/{id}/reminders", new { registrationIds = ready.Take(3) });
        Assert.Equal(HttpStatusCode.Conflict, nothing.StatusCode);

        var otherSession = await factory.WithDb(db => db.Registrations.Where(r => r.SessionId != id && r.Status == RegistrationStatus.Confirmed).Select(r => r.Id).FirstAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync($"/api/admin/ops/sessions/{id}/reminders", new { registrationIds = new[] { otherSession } })).StatusCode);

        var before = await factory.WithDb(db => db.OutboxEvents.CountAsync(e => e.Type == "ReadinessReminder"));
        var families = await factory.WithDb(db => db.Registrations.Where(r => open.Contains(r.Id)).Select(r => r.HouseholdId).Distinct().CountAsync());
        var res = await staff.PostAsJsonAsync($"/api/admin/ops/sessions/{id}/reminders", new { registrationIds = roster.Select(RegId) });
        var sent = await Json(res);
        Assert.Equal(27, sent.GetProperty("campers").GetInt32());
        Assert.Equal(families, sent.GetProperty("families").GetInt32());
        Assert.Equal(159, sent.GetProperty("skipped").GetInt32());
        Assert.Equal(before + families, await factory.WithDb(db => db.OutboxEvents.CountAsync(e => e.Type == "ReadinessReminder" && e.Target == "HubSpot")));
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(a => a.Action == "ops.reminders_sent" && a.Actor == "Diane Carter (CET)")));

        // Sending the same list again the same day queues nothing new.
        var again = await staff.PostAsJsonAsync($"/api/admin/ops/sessions/{id}/reminders", new { registrationIds = roster.Select(RegId) });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(before + families, await factory.WithDb(db => db.OutboxEvents.CountAsync(e => e.Type == "ReadinessReminder" && e.Target == "HubSpot")));

        var after = (await Json(await staff.GetAsync($"/api/admin/ops/sessions/{id}/readiness"))).GetProperty("roster").EnumerateArray().ToList();
        Assert.All(after.Where(r => open.Contains(RegId(r))), r => Assert.NotEqual(JsonValueKind.Null, r.GetProperty("remindedAt").ValueKind));
        Assert.All(after.Where(r => ready.Contains(RegId(r))), r => Assert.Equal(JsonValueKind.Null, r.GetProperty("remindedAt").ValueKind));
    }

    // ── O2 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Boys_6_to_8_has_50_campers_in_five_groups_of_ten_with_one_separated_pair()
    {
        var staff = await factory.SignInAsStaff();
        var board = await Board(staff, "Boys G6–8");
        Assert.Equal("Boys G6–8", board.GetProperty("poolName").GetString());
        Assert.Equal(50, board.GetProperty("totals").GetProperty("total").GetInt32());
        var groups = board.GetProperty("groups").EnumerateArray().ToList();
        Assert.Equal(5, groups.Count);
        Assert.Equal(board.GetProperty("totals").GetProperty("assigned").GetInt32(), groups.Sum(g => g.GetProperty("count").GetInt32()));
        Assert.Equal(50, groups.Sum(g => g.GetProperty("count").GetInt32()) + board.GetProperty("totals").GetProperty("unassigned").GetInt32());
        Assert.Equal(4, board.GetProperty("pools").GetArrayLength());

        // The core seed repeats names; no cabinmate request pairs two campers with the same name.
        var pairs = await factory.WithDb(async db =>
        {
            var requests = await db.Set<OpsBuddyRequest>().ToListAsync();
            var ids = requests.SelectMany(r => new[] { r.RegistrationId, r.RequestedRegistrationId }).ToList();
            var names = (await db.Registrations.Include(r => r.Person).Where(r => ids.Contains(r.Id)).ToListAsync()).ToDictionary(r => r.Id, r => r.Person.FullName);
            return requests.Select(r => (names[r.RegistrationId], names[r.RequestedRegistrationId])).ToList();
        });
        Assert.NotEmpty(pairs);
        Assert.All(pairs, p => Assert.NotEqual(p.Item1, p.Item2));
    }

    [Fact]
    public async Task Moving_a_camper_autosaves_respects_capacity_and_pool_and_can_be_undone()
    {
        var staff = await factory.SignInAsStaff();
        var board = await Board(staff, "Boys G3–5");
        var groups = board.GetProperty("groups").EnumerateArray().ToList();
        var full = groups.First(g => g.GetProperty("count").GetInt32() == g.GetProperty("capacity").GetInt32());
        var fullId = full.GetProperty("id").GetInt32();
        var camper = board.GetProperty("campers").EnumerateArray().First(c => c.GetProperty("groupId").ValueKind == JsonValueKind.Number && c.GetProperty("groupId").GetInt32() != fullId);
        var reg = RegId(camper);
        var home = camper.GetProperty("groupId").GetInt32();

        var intoFull = await staff.PutAsJsonAsync($"/api/admin/ops/groups/placements/{reg}", new { groupId = fullId });
        Assert.Equal(HttpStatusCode.Conflict, intoFull.StatusCode);
        Assert.Contains("is full", await intoFull.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var girlsGroup = (await Board(staff, "Girls G3–5")).GetProperty("groups")[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync($"/api/admin/ops/groups/placements/{reg}", new { groupId = girlsGroup })).StatusCode);

        var moved = await Json(await staff.PutAsJsonAsync($"/api/admin/ops/groups/placements/{reg}", new { groupId = (int?)null }));
        Assert.Equal(home, moved.GetProperty("previousGroupId").GetInt32());
        var afterMove = await Board(staff, "Boys G3–5");
        Assert.Equal(1, afterMove.GetProperty("totals").GetProperty("unassigned").GetInt32());
        Assert.Equal("Not in a group yet.", afterMove.GetProperty("campers").EnumerateArray().Single(c => RegId(c) == reg).GetProperty("reviewReason").GetString());

        // Undo is the same call with the previous group.
        Assert.Equal(HttpStatusCode.OK, (await staff.PutAsJsonAsync($"/api/admin/ops/groups/placements/{reg}", new { groupId = home })).StatusCode);
        Assert.Equal(0, (await Board(staff, "Boys G3–5")).GetProperty("totals").GetProperty("unassigned").GetInt32());
        Assert.Equal(2, await factory.WithDb(db => db.AuditEvents.CountAsync(a => a.Action == "ops.group_moved" && a.EntityId == reg.ToString(CultureInfo.InvariantCulture))));
    }

    [Fact]
    public async Task Auto_suggest_changes_nothing_until_a_person_approves_then_reunites_the_pair()
    {
        var id = await SessionId();
        var staff = await factory.SignInAsStaff();
        var board = await Board(staff, "Boys G6–8");
        var poolId = board.GetProperty("poolId").GetInt32();
        Assert.Equal(1, board.GetProperty("separated").GetArrayLength());
        var before = board.GetProperty("campers").EnumerateArray().ToDictionary(RegId, c => c.GetProperty("groupId").GetInt32());

        var suggested = await Json(await staff.PostAsJsonAsync($"/api/admin/ops/sessions/{id}/groups/suggest", new { poolId }));
        Assert.Equal(2, suggested.GetProperty("suggested").GetInt32()); // every group is full, so it's a swap
        var pending = await Board(staff, "Boys G6–8");
        Assert.Equal(2, pending.GetProperty("suggestions").GetInt32());
        Assert.Equal(before, pending.GetProperty("campers").EnumerateArray().ToDictionary(RegId, c => c.GetProperty("groupId").GetInt32()));
        Assert.All(pending.GetProperty("campers").EnumerateArray().Where(c => c.GetProperty("suggestedGroupId").ValueKind == JsonValueKind.Number),
            c => Assert.False(string.IsNullOrEmpty(c.GetProperty("suggestionReason").GetString())));

        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync($"/api/admin/ops/sessions/{id}/groups/suggestions/approve", new { poolId })).StatusCode);
        var after = await Board(staff, "Boys G6–8");
        Assert.Equal(0, after.GetProperty("separated").GetArrayLength());
        Assert.Equal(0, after.GetProperty("suggestions").GetInt32());
        Assert.All(after.GetProperty("groups").EnumerateArray(), g => Assert.Equal(10, g.GetProperty("count").GetInt32()));
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/admin/ops/sessions/{id}/groups/suggestions/approve", new { poolId })).StatusCode);
    }

    // ── O3 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rooming_has_16_single_gender_cabins_and_flags_changes_since_the_last_review()
    {
        var staff = await factory.SignInAsStaff();
        var body = await Json(await staff.GetAsync($"/api/admin/ops/sessions/{await SessionId()}/rooming"));
        var s = body.GetProperty("summary");
        Assert.Equal(16, s.GetProperty("cabins").GetInt32());
        Assert.Equal(192, s.GetProperty("beds").GetInt32());
        Assert.Equal(186, s.GetProperty("registered").GetInt32());
        Assert.Equal(186, s.GetProperty("assigned").GetInt32() + s.GetProperty("unassigned").GetInt32());
        Assert.Equal(96, s.GetProperty("boys").GetProperty("registered").GetInt32());
        Assert.Equal(90, s.GetProperty("girls").GetProperty("registered").GetInt32());
        Assert.Equal(96, s.GetProperty("boys").GetProperty("beds").GetInt32());
        Assert.True(s.GetProperty("requestsNotMet").GetInt32() >= 1);
        // A girl's request to room with a boy is a conflict: cabins are single-gender.
        Assert.Equal(1, s.GetProperty("conflicts").GetInt32());

        var cabins = body.GetProperty("cabins").EnumerateArray().ToList();
        Assert.All(cabins, c =>
        {
            Assert.True(c.GetProperty("taken").GetInt32() <= c.GetProperty("beds").GetInt32());
            Assert.All(c.GetProperty("campers").EnumerateArray(), x => Assert.Equal(c.GetProperty("gender").GetString(), x.GetProperty("gender").GetString()));
        });
        Assert.Equal(s.GetProperty("assigned").GetInt32(), cabins.Sum(c => c.GetProperty("taken").GetInt32()));

        var items = body.GetProperty("review").GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("reason").GetString()!.StartsWith("Registered after the last rooming review", StringComparison.Ordinal));
        Assert.Contains(items, i => i.GetProperty("reason").GetString()!.StartsWith("Cabinmate request added", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_cabin_never_takes_the_other_gender_or_more_campers_than_beds()
    {
        var staff = await factory.SignInAsStaff();
        var body = await Json(await staff.GetAsync($"/api/admin/ops/sessions/{await SessionId()}/rooming"));
        var cabins = body.GetProperty("cabins").EnumerateArray().ToList();
        var boysCabin = cabins.First(c => c.GetProperty("gender").GetString() == "Male" && c.GetProperty("taken").GetInt32() == 11);
        var boysCabinId = boysCabin.GetProperty("id").GetInt32();
        var unassigned = body.GetProperty("unassigned").EnumerateArray().ToList();
        var girl = RegId(unassigned.First(c => c.GetProperty("gender").GetString() == "Female"));
        var boys = unassigned.Where(c => c.GetProperty("gender").GetString() == "Male").Select(RegId).Take(2).ToList();

        var mixed = await staff.PutAsJsonAsync($"/api/admin/ops/rooming/placements/{girl}", new { cabinId = boysCabinId });
        Assert.Equal(HttpStatusCode.Conflict, mixed.StatusCode);
        Assert.Contains("boys cabin", await mixed.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await staff.PutAsJsonAsync($"/api/admin/ops/rooming/placements/{boys[0]}", new { cabinId = boysCabinId })).StatusCode);
        var full = await staff.PutAsJsonAsync($"/api/admin/ops/rooming/placements/{boys[1]}", new { cabinId = boysCabinId });
        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        Assert.Contains("12 of 12 beds", await full.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var undo = await Json(await staff.PutAsJsonAsync($"/api/admin/ops/rooming/placements/{boys[0]}", new { cabinId = (int?)null }));
        Assert.Equal(boysCabinId, undo.GetProperty("previousCabinId").GetInt32());
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(a => a.Action == "ops.cabin_moved")));
    }

    // ── O5 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_blocked_camper_needs_an_audited_override_and_a_ready_camper_checks_in_once()
    {
        var staff = await factory.SignInAsStaff();
        var rows = await CheckInRows(staff);
        Assert.Equal(27, rows.Count(r => r.GetProperty("status").GetString() == "Blocked"));
        var blocked = rows.First(r => r.GetProperty("status").GetString() == "Blocked" && r.GetProperty("blockers").GetArrayLength() == 2);
        var ready = rows.First(r => r.GetProperty("status").GetString() == "Ready");

        var refused = await staff.PostAsJsonAsync($"/api/admin/ops/check-in/{RegId(blocked)}", new { overrideReason = "  " });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(2, (await Json(refused)).GetProperty("blockers").GetArrayLength());

        const string reason = "Mom paid the balance at the desk; waiver signed on paper.";
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync($"/api/admin/ops/check-in/{RegId(blocked)}", new { overrideReason = reason })).StatusCode);
        var audit = await factory.WithDb(db => db.AuditEvents.SingleAsync(a => a.Action == "ops.checked_in_override" && a.EntityId == RegId(blocked).ToString(CultureInfo.InvariantCulture)));
        Assert.Contains(reason, audit.Detail, StringComparison.Ordinal);
        Assert.Equal("Diane Carter (CET)", audit.Actor);

        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync($"/api/admin/ops/check-in/{RegId(ready)}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/admin/ops/check-in/{RegId(ready)}", new { })).StatusCode);
        var after = (await CheckInRows(staff)).Single(r => RegId(r) == RegId(ready));
        Assert.Equal("Checked in", after.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Check_out_releases_a_camper_only_to_their_own_authorized_adult_after_an_id_check()
    {
        var staff = await factory.SignInAsStaff();
        var rows = await CheckInRows(staff);
        var camper = rows.First(r => r.GetProperty("status").GetString() == "Checked in");
        var notIn = rows.First(r => r.GetProperty("status").GetString() == "Ready");
        var own = camper.GetProperty("pickupAdults")[0].GetProperty("id").GetInt32();
        var stranger = notIn.GetProperty("pickupAdults")[0].GetProperty("id").GetInt32();
        var url = $"/api/admin/ops/check-out/{RegId(camper)}";

        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/admin/ops/check-out/{RegId(notIn)}", new { pickupAdultId = stranger, idChecked = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync(url, new { pickupAdultId = stranger, idChecked = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync(url, new { pickupAdultId = own, idChecked = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync(url, new { pickupAdultId = own, idChecked = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(url, new { pickupAdultId = own, idChecked = true })).StatusCode);

        var after = (await CheckInRows(staff)).Single(r => RegId(r) == RegId(camper));
        Assert.Equal("Checked out", after.GetProperty("status").GetString());
        Assert.Contains(camper.GetProperty("pickupAdults")[0].GetProperty("name").GetString()!, after.GetProperty("pickedUpBy").GetString(), StringComparison.Ordinal);
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(a => a.Action == "ops.checked_out" && a.EntityId == RegId(camper).ToString(CultureInfo.InvariantCulture))));
    }

    [Fact]
    public async Task Two_tablets_checking_in_or_out_the_same_camper_at_once_record_it_once()
    {
        var staff = await factory.SignInAsStaff();
        var rows = await CheckInRows(staff);
        // The last rows, so the other O5 tests (which take the first) are unaffected.
        var ready = rows.Last(r => r.GetProperty("status").GetString() == "Ready");
        var inUrl = $"/api/admin/ops/check-in/{RegId(ready)}";
        var ins = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => staff.PostAsJsonAsync(inUrl, new { overrideReason = $"tablet {i}" })));
        Assert.Equal(1, ins.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(4, ins.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var regKey = RegId(ready).ToString(CultureInfo.InvariantCulture);
        Assert.Equal(1, await factory.WithDb(db => db.AuditEvents.CountAsync(a => a.Action.StartsWith("ops.checked_in") && a.EntityId == regKey)));

        var adults = ready.GetProperty("pickupAdults").EnumerateArray().Select(a => a.GetProperty("id").GetInt32()).ToList();
        var outUrl = $"/api/admin/ops/check-out/{RegId(ready)}";
        var outs = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => staff.PostAsJsonAsync(outUrl, new { pickupAdultId = adults[i % adults.Count], idChecked = true })));
        Assert.Equal(1, outs.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(3, outs.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await factory.WithDb(db => db.AuditEvents.CountAsync(a => a.Action == "ops.checked_out" && a.EntityId == regKey)));
    }

    [Fact]
    public async Task Sessions_without_cabins_or_groups_return_empty_boards()
    {
        var staff = await factory.SignInAsStaff();
        var dayCamp = await factory.WithDb(db => db.Sessions.Where(s => s.Program.Slug == "day-camp-atlanta").Select(s => s.Id).FirstAsync());
        var rooming = await Json(await staff.GetAsync($"/api/admin/ops/sessions/{dayCamp}/rooming"));
        Assert.Equal(0, rooming.GetProperty("summary").GetProperty("cabins").GetInt32());
        var groups = await Json(await staff.GetAsync($"/api/admin/ops/sessions/{dayCamp}/groups"));
        Assert.Equal(0, groups.GetProperty("groups").GetArrayLength());
        var readiness = await Json(await staff.GetAsync($"/api/admin/ops/sessions/{dayCamp}/readiness"));
        Assert.False(readiness.GetProperty("usesCampDoc").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync("/api/admin/ops/sessions/999999/readiness")).StatusCode);
    }

    async Task<JsonElement> Board(HttpClient staff, string pool)
    {
        var id = await SessionId();
        var first = await Json(await staff.GetAsync($"/api/admin/ops/sessions/{id}/groups"));
        var poolId = first.GetProperty("pools").EnumerateArray().Single(p => p.GetProperty("name").GetString() == pool).GetProperty("id").GetInt32();
        return await Json(await staff.GetAsync($"/api/admin/ops/sessions/{id}/groups?poolId={poolId}"));
    }

    async Task<List<JsonElement>> CheckInRows(HttpClient staff) =>
        (await Json(await staff.GetAsync($"/api/admin/ops/sessions/{await SessionId()}/check-in"))).GetProperty("rows").EnumerateArray().ToList();

    internal static int RegId(JsonElement e) => e.GetProperty("registrationId").GetInt32();

    internal static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode || (int)res.StatusCode is >= 400 and < 500, text);
        return JsonDocument.Parse(text).RootElement;
    }
}

/// <summary>Roster changes after the seed (a cancellation), in their own database so the canonical counts above hold.</summary>
public class OpsRosterChangeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_cancelled_camper_frees_their_bed_and_is_listed_until_the_review_is_marked_done()
    {
        var id = await OpsTestData.SessionId(factory);
        var staff = await factory.SignInAsStaff();
        var placed = await factory.WithDb(db => db.Set<OpsPlacement>().Where(p => p.SessionId == id && p.CabinId != null).OrderBy(p => p.Id).FirstAsync());
        await factory.WithDb(async db =>
        {
            var reg = await db.Registrations.SingleAsync(r => r.Id == placed.RegistrationId);
            reg.Status = RegistrationStatus.Cancelled;
            return await db.SaveChangesAsync();
        });

        var body = await OpsTests.Json(await staff.GetAsync($"/api/admin/ops/sessions/{id}/rooming"));
        Assert.Equal(185, body.GetProperty("summary").GetProperty("registered").GetInt32());
        Assert.Contains(body.GetProperty("review").GetProperty("items").EnumerateArray(),
            i => OpsTests.RegId(i) == placed.RegistrationId && i.GetProperty("reason").GetString()!.StartsWith("No longer registered", StringComparison.Ordinal));
        var cabin = body.GetProperty("cabins").EnumerateArray().Single(c => c.GetProperty("id").GetInt32() == placed.CabinId);
        Assert.DoesNotContain(cabin.GetProperty("campers").EnumerateArray(), c => OpsTests.RegId(c) == placed.RegistrationId);

        var reviewed = await OpsTests.Json(await staff.PostAsync($"/api/admin/ops/sessions/{id}/rooming/review", null));
        Assert.Equal(1, reviewed.GetProperty("released").GetInt32());
        var after = await OpsTests.Json(await staff.GetAsync($"/api/admin/ops/sessions/{id}/rooming"));
        Assert.Equal(0, after.GetProperty("review").GetProperty("items").GetArrayLength());
        Assert.Equal("Diane Carter (CET)", after.GetProperty("review").GetProperty("reviewedBy").GetString());
        Assert.Null(await factory.WithDb(db => db.Set<OpsPlacement>().Where(p => p.Id == placed.Id).Select(p => p.CabinId).SingleAsync()));
    }
}

internal static class OpsTestData
{
    public static Task<int> SessionId(ApiFactory factory) =>
        factory.WithDb(db => db.Sessions.Where(s => s.Program.Slug == "overnight-camp" && s.Name == "Session 3").Select(s => s.Id).SingleAsync());
}
