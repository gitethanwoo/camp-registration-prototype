using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Camp.Api.Auth;
using Camp.Api.Domain;
using Camp.Api.Features.Finance;
using Camp.Api.Features.Polish;
using Camp.Api.Features.Setup;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Camp.Api.Tests;

/// <summary>Polish: the demo clock, the Family Camp waiver and K2's publish guard, moved campers on F1/F4, and David's sign-in.</summary>
public class PolishTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    const string Setup = "/api/admin/setup";
    const string MariaFamilyCamp = "WS-FC2J08";

    // ── Demo clock ───────────────────────────────────────────────────────────

    [Fact]
    public async Task The_clock_endpoint_reports_demo_time_to_anyone()
    {
        var res = await factory.CreateClient().GetAsync("/api/clock");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await Json(res);
        var now = body.GetProperty("now").GetDateTimeOffset();
        Assert.InRange(now, ApiFactory.Now, ApiFactory.Now.AddHours(1));
        // Demo time minus real time, so the web app can shift its own clock.
        var expected = (ApiFactory.Now - DateTimeOffset.UtcNow).TotalMilliseconds;
        Assert.InRange(body.GetProperty("offsetMs").GetInt64(), expected - 3_600_000, expected + 3_600_000);
    }

    [Fact]
    public void The_demo_clock_starts_at_its_anchor_and_moves_forward()
    {
        var clock = new DemoClock(ApiFactory.Now);
        var first = clock.GetUtcNow();
        Assert.InRange(first, ApiFactory.Now, ApiFactory.Now.AddMinutes(1));
        Assert.True(clock.GetUtcNow() >= first);
        Assert.Equal(new DateOnly(2028, 3, 2), clock.Today());
        Assert.Equal(DemoClock.DefaultAnchor, DemoClock.FromConfiguration(new ConfigurationBuilder().Build()).Anchor);
    }

    [Fact]
    public async Task Staff_changes_are_stamped_with_demo_time()
    {
        var id = await DraftProgramWithSession(await Admin("alex.morgan"));
        Assert.Equal(HttpStatusCode.OK, (await (await Admin("alex.morgan")).PostAsync($"{Setup}/programs/{id}/waivers", null)).StatusCode);
        var stamped = await factory.WithDb(db => db.AuditEvents.Where(a => a.Action == "waiver.added" && a.EntityId == id.ToString(CultureInfo.InvariantCulture))
            .Select(a => a.CreatedAt).SingleAsync());
        Assert.InRange(stamped, ApiFactory.Now.UtcDateTime, ApiFactory.Now.UtcDateTime.AddHours(1));
        var version = await factory.WithDb(db => db.Set<WaiverVersion>().Where(v => db.WaiverTemplates.Any(t => t.Id == v.WaiverTemplateId && t.ProgramId == id))
            .Select(v => new { v.CreatedAt, v.EffectiveDate }).SingleAsync());
        Assert.Equal(new DateOnly(2028, 3, 2), version.EffectiveDate);
        Assert.InRange(version.CreatedAt, ApiFactory.Now.UtcDateTime, ApiFactory.Now.UtcDateTime.AddHours(1));
    }

    // ── Family Camp waiver and K2's publish guard ───────────────────────────

    [Fact]
    public async Task Every_published_program_has_a_waiver_and_Family_Camps_registrations_are_signed()
    {
        var (waivers, unsigned) = await factory.WithDb(async db =>
        {
            var program = await db.Programs.Include(p => p.Waivers).SingleAsync(p => p.Slug == FinanceSeed.ProgramSlug);
            var missing = await db.Registrations.CountAsync(r => r.Session.ProgramId == program.Id && r.Status != RegistrationStatus.Cancelled
                && !r.WaiverAcceptances.Any(a => a.WaiverTemplate.ProgramId == program.Id));
            return (program.Waivers.Select(w => w.Title).ToList(), missing);
        });
        Assert.Equal([PolishSeed.FamilyCampWaiverTitle], waivers);
        Assert.Equal(0, unsigned);
        // The invariant: no published program takes registrations without a waiver.
        Assert.Empty(await factory.WithDb(db => db.Programs.Where(p => p.IsPublished && !p.Waivers.Any()).Select(p => p.Name).ToListAsync()));

        // Maria's checklist shows the waiver as signed, and K7 lists it with its version history.
        var maria = await factory.SignInAsFamily();
        var detail = await Json(await maria.GetAsync($"/api/family/registrations/{MariaFamilyCamp}"));
        var rows = detail.GetProperty("checklist").EnumerateArray().Where(c => c.GetProperty("kind").GetString() == "waiver").ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.True(r.GetProperty("done").GetBoolean()));
        var templates = await Json(await (await Admin("alex.morgan")).GetAsync($"{Setup}/waivers"));
        Assert.Contains(templates.EnumerateArray(), t => t.GetProperty("title").GetString() == PolishSeed.FamilyCampWaiverTitle && t.GetProperty("program").GetString() == "Family Camp");
    }

    [Fact]
    public async Task A_program_without_a_waiver_cannot_be_submitted()
    {
        var alex = await Admin("alex.morgan");
        var id = await DraftProgramWithSession(alex);

        var refused = await alex.PostAsync($"{Setup}/programs/{id}/submit", null);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("can't be published without one", (await Json(refused)).GetProperty("errors").GetProperty("waivers")[0].GetString());

        Assert.Equal(HttpStatusCode.OK, (await alex.PostAsync($"{Setup}/programs/{id}/waivers", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await alex.PostAsync($"{Setup}/programs/{id}/waivers", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alex.PostAsync($"{Setup}/programs/{id}/submit", null)).StatusCode);
        // Pending approval: the text approvers read can't change under them.
        await RemoveWaivers(id);
        var locked = await alex.PostAsync($"{Setup}/programs/{id}/waivers", null);
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        Assert.Contains("Return it to draft", (await Json(locked)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task The_last_approval_refuses_to_publish_a_program_with_no_waiver()
    {
        var alex = await Admin("alex.morgan");
        var id = await DraftProgramWithSession(alex);
        Assert.Equal(HttpStatusCode.OK, (await alex.PostAsync($"{Setup}/programs/{id}/waivers", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alex.PostAsync($"{Setup}/programs/{id}/submit", null)).StatusCode);
        // A program submitted before the guard existed can reach approval with no waiver.
        await RemoveWaivers(id);

        Assert.Equal(HttpStatusCode.OK, (await (await Admin("director.one")).PostAsync($"{Setup}/programs/{id}/approve", null)).StatusCode);
        var last = await (await Admin("owner.two")).PostAsync($"{Setup}/programs/{id}/approve", null);
        Assert.Equal(HttpStatusCode.BadRequest, last.StatusCode);
        Assert.Equal(ProgramSetupEndpoints.NoWaiver, (await Json(last)).GetProperty("errors").GetProperty("waivers")[0].GetString());
        Assert.False(await factory.WithDb(db => db.Programs.Where(p => p.Id == id).Select(p => p.IsPublished).SingleAsync()));
        Assert.Equal(PublishState.PendingApproval, await factory.WithDb(db => db.Set<ProgramSetup>().Where(s => s.ProgramId == id).Select(s => s.State).SingleAsync()));
    }

    [Fact]
    public async Task Only_admins_can_add_a_waiver_to_a_program()
    {
        var id = await DraftProgramWithSession(await Admin("alex.morgan"));
        var url = $"{Setup}/programs/{id}/waivers";
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsync(url, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsFamily()).PostAsync(url, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsStaff("cet")).PostAsync(url, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsStaff("finance", "Marcus Lee")).PostAsync(url, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await Admin("alex.morgan")).PostAsync($"{Setup}/programs/999999/waivers", null)).StatusCode);
        Assert.False(await factory.WithDb(db => db.WaiverTemplates.AnyAsync(w => w.ProgramId == id)));
    }

    // ── F1 / F4: a moved camper ─────────────────────────────────────────────

    [Fact]
    public async Task A_moved_camper_shows_their_own_session_on_F1_and_F4_and_only_to_their_family()
    {
        // What an approved transfer (C10) does: one camper's registration moves; the order keeps its session.
        var moved = await factory.WithDb(async db =>
        {
            var order = await db.Orders.Include(o => o.Registrations).ThenInclude(r => r.Person).Include(o => o.Session)
                .SingleAsync(o => o.ConfirmationCode == MariaFamilyCamp);
            var fall = new Session { ProgramId = order.Session.ProgramId, Name = "Fall Family Weekend", StartDate = new(2028, 10, 6), EndDate = new(2028, 10, 8), PriceCents = 47500, DepositCents = 10000, BalanceDueDate = new(2028, 9, 1) };
            var pool = new CapacityPool { Session = fall, Name = "Campers G1–8", GradeMin = 1, GradeMax = 8, Capacity = 40, SortOrder = 1 };
            fall.Pools.Add(pool);
            db.Sessions.Add(fall);
            await db.SaveChangesAsync();
            var mia = order.Registrations.Single(r => r.Person.FirstName == "Mia");
            mia.SessionId = fall.Id;
            mia.PoolId = pool.Id;
            await db.SaveChangesAsync();
            return fall.Name;
        });

        var maria = await factory.SignInAsFamily();
        var overview = await Json(await maria.GetAsync("/api/family/overview"));
        var card = overview.GetProperty("registrations").EnumerateArray().Single(r => r.GetProperty("confirmationCode").GetString() == MariaFamilyCamp);
        Assert.Equal("Avery", card.GetProperty("participants").GetString());
        Assert.Equal(2, card.GetProperty("count").GetInt32());
        var movedRow = Assert.Single(card.GetProperty("moved").EnumerateArray());
        Assert.Equal("Mia", movedRow.GetProperty("firstName").GetString());
        Assert.Equal(moved, movedRow.GetProperty("session").GetString());
        Assert.Equal("2028-10-06", movedRow.GetProperty("startDate").GetString());
        // Mia's own checklist rows name the session she now attends.
        Assert.Contains(overview.GetProperty("checklist").EnumerateArray(),
            c => c.GetProperty("participant").GetString() == "Mia" && c.GetProperty("context").GetString()!.EndsWith(moved, StringComparison.Ordinal));

        var list = await Json(await maria.GetAsync("/api/family/registrations"));
        var f4 = list.GetProperty("upcoming").EnumerateArray().Single(c => c.GetProperty("confirmationCode").GetString() == MariaFamilyCamp);
        var people = f4.GetProperty("participants").EnumerateArray().ToList();
        Assert.Equal(JsonValueKind.Null, people.Single(p => p.GetProperty("name").GetString() == "Avery Johnson").GetProperty("movedTo").ValueKind);
        var miaTo = people.Single(p => p.GetProperty("name").GetString() == "Mia Johnson").GetProperty("movedTo");
        Assert.Equal(moved, miaTo.GetProperty("name").GetString());
        Assert.Equal("2028-10-08", miaTo.GetProperty("endDate").GetString());

        // Another household sees none of it.
        var sam = await factory.SignInAsFamily("sam.rivera@example.com", "Sam", "Rivera");
        Assert.DoesNotContain(MariaFamilyCamp, await (await sam.GetAsync("/api/family/overview")).Content.ReadAsStringAsync());
        Assert.DoesNotContain(MariaFamilyCamp, await (await sam.GetAsync("/api/family/registrations")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await sam.GetAsync($"/api/family/registrations/{MariaFamilyCamp}")).StatusCode);
    }

    // ── David Johnson persona ───────────────────────────────────────────────

    [Fact]
    public async Task David_signs_in_to_the_Johnson_household()
    {
        var david = await factory.SignInAsFamily("david.johnson@example.com", "David", "Johnson");
        var overview = await Json(await david.GetAsync("/api/family/overview"));
        Assert.Equal("Johnson", overview.GetProperty("name").GetString());
        var names = overview.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("firstName").GetString()).ToList();
        Assert.Contains("Maria", names);
        Assert.Contains("Avery", names);
        Assert.Contains(overview.GetProperty("registrations").EnumerateArray(), r => r.GetProperty("confirmationCode").GetString() == MariaFamilyCamp);
        // Signing in didn't create a second household for him.
        Assert.Equal(1, await factory.WithDb(db => db.People.CountAsync(p => p.Email == "david.johnson@example.com")));
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>An admin with their own email, so two approval steps can have two different approvers.</summary>
    async Task<HttpClient> Admin(string who)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var res = await client.PostAsJsonAsync("/api/auth/dev-login", new DevLoginRequest($"{who}@winshape.example", who, "Admin", "admin"));
        res.EnsureSuccessStatusCode();
        return client;
    }

    Task<int> RemoveWaivers(int programId) => factory.WithDb(async db =>
    {
        var templates = await db.WaiverTemplates.Where(w => w.ProgramId == programId).Select(w => w.Id).ToListAsync();
        await db.Set<WaiverVersion>().Where(v => templates.Contains(v.WaiverTemplateId)).ExecuteDeleteAsync();
        return await db.WaiverTemplates.Where(w => w.ProgramId == programId).ExecuteDeleteAsync();
    });

    static async Task<int> DraftProgramWithSession(HttpClient admin)
    {
        var created = await admin.PostAsJsonAsync($"{Setup}/programs",
            new ProgramInput(1, "Polish Retreat " + Guid.NewGuid().ToString("N")[..6], ProgramType.Standard, HealthMechanism.Embedded, SessionSetupEndpoints.MountBerry, "A tagline.", "A description."));
        created.EnsureSuccessStatusCode();
        var id = (await Json(created)).GetProperty("id").GetInt32();
        var session = await admin.PostAsJsonAsync($"{Setup}/programs/{id}/sessions",
            new NewSessionInput("Fall 2028", new(2028, 10, 6), new(2028, 10, 8), 20000, 5000, "Everyone", null, 0, 12, 30));
        session.EnsureSuccessStatusCode();
        return id;
    }

    static async Task<JsonElement> Json(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
}
