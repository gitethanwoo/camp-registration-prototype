using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Camp.Api.Domain;
using Camp.Api.Features.Access;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Camp.Api.Tests;

/// <summary>Slice 10: staff access (K11) and health collection settings (K9), and who may read health details.</summary>
public class AccessTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    const string Access = "/api/access";

    // ── access to the setup screens ──────────────────────────────────────────

    [Theory]
    [InlineData(Access + "/staff")]
    [InlineData(Access + "/health")]
    public async Task Staff_and_health_settings_are_admin_only(string url)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsFamily()).GetAsync(url)).StatusCode);
        foreach (var (role, name) in new[] { ("host", "Grace Patel"), ("cet", "Diane Carter"), ("finance", "Marcus Lee") })
            Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsStaff(role, name)).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await Alex()).GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Staff_mutations_are_admin_only()
    {
        var cet = await factory.SignInAsStaff("cet", "Diane Carter");
        var id = await StaffId("diane.carter@winshape.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await cet.PutAsJsonAsync($"{Access}/staff/{id}", new { ministryId = (int?)null, healthAccess = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cet.PostAsync($"{Access}/staff/sync", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cet.PutAsJsonAsync($"{Access}/health/{await ProgramId("day-camp-atlanta")}", new { mechanism = "Embedded", viewerRoles = Roles.Cet })).StatusCode);
        Assert.False(await factory.WithDb(db => db.Set<StaffMember>().Where(m => m.Id == id).Select(m => m.HealthAccess).SingleAsync()));
    }

    // ── K11 · staff access ───────────────────────────────────────────────────

    [Fact]
    public async Task The_roster_lists_seeded_staff_with_role_scope_health_access_and_status()
    {
        var body = await (await Alex()).GetFromJsonAsync<JsonElement>($"{Access}/staff");
        var rows = body.GetProperty("rows").EnumerateArray().ToList();
        var morgan = rows.Single(r => r.GetProperty("email").GetString() == AccessSeed.FormerStaffEmail);
        Assert.Equal("Active", morgan.GetProperty("status").GetString()); // until the first sync says otherwise
        Assert.Equal("WSC Camps", morgan.GetProperty("ministry").GetProperty("name").GetString());
        var grace = rows.Single(r => r.GetProperty("email").GetString() == "grace.patel@winshape.example");
        Assert.Equal("Host coordinator", grace.GetProperty("roleLabel").GetString());
        Assert.False(grace.GetProperty("canViewHealth").GetBoolean());
        Assert.True(body.GetProperty("syncDue").GetBoolean());
        // No password field anywhere: people are added and removed in the identity provider.
        Assert.DoesNotContain("password", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Signing_in_records_the_last_sign_in_and_puts_access_on_the_session()
    {
        var marcus = await factory.SignInAsStaff("finance", "Marcus Lee");
        var row = await factory.WithDb(db => db.Set<StaffMember>().SingleAsync(m => m.Email == "finance@winshape.example"));
        Assert.NotNull(row.LastSignInAt);
        Assert.Equal("user_test_finance@winshape.example", row.WorkOsUserId);
        Assert.Equal("finance", row.Role);
        var me = await marcus.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.False(me.GetProperty("healthAccess").GetBoolean());

        // A family's session carries no staff access at all.
        var maria = await (await factory.SignInAsFamily()).GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(JsonValueKind.Null, maria.GetProperty("healthAccess").ValueKind);
    }

    [Fact]
    public async Task Scope_and_health_access_changes_are_audited_with_before_and_after()
    {
        var alex = await Alex();
        var id = await StaffId("marcus.lee@winshape.example");
        var wsc = await factory.WithDb(db => db.Ministries.Where(m => m.Code == "WSC").Select(m => m.Id).SingleAsync());

        var res = await alex.PutAsJsonAsync($"{Access}/staff/{id}", new { ministryId = wsc, healthAccess = true });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var key = id.ToString(CultureInfo.InvariantCulture);
        var events = await factory.WithDb(db => db.AuditEvents.Where(a => a.EntityType == "StaffMember" && a.EntityId == key).ToListAsync());
        var scope = Assert.Single(events, e => e.Action == "staff.scope_changed");
        Assert.Equal("Alex Morgan (ADMIN)", scope.Actor);
        Assert.Contains("from All ministries to WSC Camps", scope.Detail, StringComparison.Ordinal);
        Assert.Single(events, e => e.Action == "staff.health_access_changed");
        var changes = await factory.WithDb(db => db.Set<Camp.Api.Features.Setup.AuditChange>()
            .Where(c => c.AuditEvent.EntityType == "StaffMember" && c.AuditEvent.EntityId == key)
            .Select(c => new { c.Field, c.Before, c.After }).ToListAsync());
        Assert.Contains(changes, c => c.Field == "Ministry scope" && c.Before == "All ministries" && c.After == "WSC Camps");
        Assert.Contains(changes, c => c.Field == "Health-data access" && c.Before == "Completion status only" && c.After == "Health details");

        // Saving the same values again records nothing new.
        await alex.PutAsJsonAsync($"{Access}/staff/{id}", new { ministryId = wsc, healthAccess = true });
        Assert.Equal(events.Count, await factory.WithDb(db => db.AuditEvents.CountAsync(a => a.EntityType == "StaffMember" && a.EntityId == key)));
    }

    [Fact]
    public async Task Staff_edits_are_validated()
    {
        var alex = await Alex();
        var grace = await StaffId("grace.patel@winshape.example");
        var host = await alex.PutAsJsonAsync($"{Access}/staff/{grace}", new { ministryId = (int?)null, healthAccess = true });
        Assert.Equal(HttpStatusCode.BadRequest, host.StatusCode);
        Assert.Contains("never see camper health", await host.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var diane = await StaffId("diane.carter@winshape.example");
        Assert.Equal(HttpStatusCode.BadRequest, (await alex.PutAsJsonAsync($"{Access}/staff/{diane}", new { ministryId = 99999, healthAccess = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alex.PutAsJsonAsync($"{Access}/staff/99999", new { ministryId = (int?)null, healthAccess = false })).StatusCode);
    }

    // ── K9 · health collection settings ──────────────────────────────────────

    [Fact]
    public async Task CampDoc_outside_Overnight_Camp_needs_an_explicit_confirmation()
    {
        var alex = await Alex();
        var family = await ProgramId("family-weekend");
        var refused = await alex.PutAsJsonAsync($"{Access}/health/{family}", new { mechanism = "CampDoc", viewerRoles = Roles.Admin, confirmMismatch = false });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("CampDoc is used by Overnight Camp only", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HealthMechanism.Embedded, await Mechanism(family));

        // Overnight Camp moving off CampDoc needs the same confirmation.
        var on = await ProgramId("overnight-camp");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await alex.PutAsJsonAsync($"{Access}/health/{on}", new { mechanism = "Embedded", viewerRoles = Roles.None, confirmMismatch = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await alex.PutAsJsonAsync($"{Access}/health/{on}", new { mechanism = "CampDoc", viewerRoles = Roles.None, confirmMismatch = false })).StatusCode);

        var confirmed = await alex.PutAsJsonAsync($"{Access}/health/{family}", new { mechanism = "CampDoc", viewerRoles = Roles.Admin, confirmMismatch = true });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(HealthMechanism.CampDoc, await Mechanism(family));
        var key = family.ToString(CultureInfo.InvariantCulture);
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(a => a.Action == "health.settings_changed" && a.EntityId == key)));

        // Put it back for the other tests.
        await alex.PutAsJsonAsync($"{Access}/health/{family}", new { mechanism = "Embedded", viewerRoles = Roles.Admin, confirmMismatch = false });
    }

    [Fact]
    public async Task Health_settings_are_validated()
    {
        var alex = await Alex();
        var day = await ProgramId("day-camp-atlanta");
        var host = await alex.PutAsJsonAsync($"{Access}/health/{day}", new { mechanism = "Embedded", viewerRoles = Roles.Host });
        Assert.Equal(HttpStatusCode.BadRequest, host.StatusCode);
        var link = await alex.PutAsJsonAsync($"{Access}/health/{day}", new { mechanism = "ThirdParty", viewerRoles = Roles.Admin, thirdPartyFormUrl = "http://forms.example.com/x" });
        Assert.Equal(HttpStatusCode.BadRequest, link.StatusCode);
        Assert.Contains("https://", await link.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HealthMechanism.Embedded, await Mechanism(day));
    }

    [Fact]
    public async Task Changing_how_health_is_collected_never_touches_registrations_or_money()
    {
        var alex = await Alex();
        var day = await ProgramId("day-camp-atlanta");
        var before = await Snapshot(day);
        Assert.Equal(HttpStatusCode.OK, (await alex.PutAsJsonAsync($"{Access}/health/{day}",
            new { mechanism = "ThirdParty", viewerRoles = Roles.Admin, thirdPartyFormUrl = "https://forms.example.com/day-camp-health" })).StatusCode);
        Assert.Equal(before, await Snapshot(day));
        await alex.PutAsJsonAsync($"{Access}/health/{day}", new { mechanism = "Embedded", viewerRoles = Roles.Admin });
        Assert.Equal(before, await Snapshot(day));
    }

    // ── health details, enforced ─────────────────────────────────────────────

    [Fact]
    public async Task Health_details_need_the_flag_an_allowed_role_and_the_right_ministry_and_every_attempt_is_audited()
    {
        var alex = await Alex();
        var reg = await DayCampRegistrationWithForm();
        var url = $"{Access}/registrations/{reg}/health";
        var key = reg.ToString(CultureInfo.InvariantCulture);
        var day = await ProgramId("day-camp-atlanta");

        // Diane (CET) has no health-data access yet.
        var cet = await factory.SignInAsStaff("cet", "Diane Carter");
        var noFlag = await cet.GetAsync(url);
        Assert.Equal(HttpStatusCode.Forbidden, noFlag.StatusCode);
        Assert.Contains("completion status only", await noFlag.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("Dr. ", await noFlag.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(a => a.Action == "health.view_denied" && a.EntityId == key && a.Actor == "Diane Carter (CET)")));

        // The flag alone isn't enough: Day Camp opens health details to administrators only.
        var cetId = await StaffId("cet@winshape.example");
        await alex.PutAsJsonAsync($"{Access}/staff/{cetId}", new { ministryId = (int?)null, healthAccess = true });
        var wrongRole = await cet.GetAsync(url);
        Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);
        Assert.Contains("open to Administrator only", await wrongRole.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Alex opens Day Camp to CET: now Diane can read the form, and the read is audited.
        Assert.Equal(HttpStatusCode.OK, (await alex.PutAsJsonAsync($"{Access}/health/{day}", new { mechanism = "Embedded", viewerRoles = Roles.AdminCet })).StatusCode);
        var ok = await cet.GetFromJsonAsync<JsonElement>(url);
        Assert.StartsWith("Dr. ", ok.GetProperty("details").GetProperty("physicianName").GetString(), StringComparison.Ordinal);
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(a => a.Action == "health.viewed" && a.EntityId == key && a.Actor == "Diane Carter (CET)")));

        // The K9 page lists her as a viewer.
        var settings = await alex.GetFromJsonAsync<JsonElement>($"{Access}/health");
        var dayRow = settings.GetProperty("programs").EnumerateArray().Single(p => p.GetProperty("id").GetInt32() == day);
        Assert.Contains(dayRow.GetProperty("viewers").EnumerateArray(), v => v.GetProperty("id").GetInt32() == cetId);

        // Scoped to WSM Marriage, she can't read a WSC Camps form.
        var wsm = await factory.WithDb(db => db.Ministries.Where(m => m.Code == "WSM").Select(m => m.Id).SingleAsync());
        await alex.PutAsJsonAsync($"{Access}/staff/{cetId}", new { ministryId = wsm, healthAccess = true });
        var scoped = await cet.GetAsync(url);
        Assert.Equal(HttpStatusCode.Forbidden, scoped.StatusCode);
        Assert.Contains("WSM Marriage only", await scoped.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Families and hosts never reach it.
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsFamily()).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsStaff("host", "Grace Patel")).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);

        // Reset for the other tests.
        await alex.PutAsJsonAsync($"{Access}/staff/{cetId}", new { ministryId = (int?)null, healthAccess = false });
        await alex.PutAsJsonAsync($"{Access}/health/{day}", new { mechanism = "Embedded", viewerRoles = Roles.Admin });
    }

    [Fact]
    public async Task CampDoc_programs_return_status_and_a_link_but_no_details()
    {
        var alex = await Alex();
        var reg = await factory.WithDb(db => db.Registrations.Where(r => r.Session.Program.Slug == "overnight-camp").Select(r => r.Id).FirstAsync());
        var body = await alex.GetFromJsonAsync<JsonElement>($"{Access}/registrations/{reg}/health");
        Assert.Equal("CampDoc", body.GetProperty("mechanism").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("details").ValueKind);
        Assert.Equal(AccessEndpoints.CampDocUrl, body.GetProperty("link").GetString());
    }

    [Fact]
    public async Task The_registration_detail_still_never_carries_health_form_contents()
    {
        var reg = await DayCampRegistrationWithForm();
        var cet = await factory.SignInAsStaff("cet", "Diane Carter");
        var text = await cet.GetStringAsync($"/api/admin/registrations/{reg}");
        Assert.Contains("\"healthOnFile\":true", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Dr. ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_seed_fills_only_empty_Day_Camp_forms_and_changes_no_status()
    {
        var counts = await factory.WithDb(async db => new
        {
            Empty = await db.Registrations.CountAsync(r => r.Session.Program.Slug == "day-camp-atlanta" && r.HealthStatus == FormStatus.Complete && r.HealthJson == null),
            Filled = await db.Registrations.CountAsync(r => r.Session.Program.Slug == "day-camp-atlanta" && r.HealthJson != null),
            OnForms = await db.Registrations.CountAsync(r => r.Session.Program.Slug == "overnight-camp" && r.HealthJson != null),
        });
        Assert.Equal(0, counts.Empty);
        Assert.True(counts.Filled > 0);
        Assert.Equal(0, counts.OnForms);
    }

    Task<HttpClient> Alex() => factory.SignInAsStaff("admin", "Alex Morgan");

    Task<int> StaffId(string email) => factory.WithDb(db => db.Set<StaffMember>().Where(m => m.Email == email).Select(m => m.Id).SingleAsync());

    Task<int> ProgramId(string slug) => factory.WithDb(db => db.Programs.Where(p => p.Slug == slug).Select(p => p.Id).SingleAsync());

    Task<HealthMechanism> Mechanism(int programId) => factory.WithDb(db => db.Programs.Where(p => p.Id == programId).Select(p => p.HealthMechanism).SingleAsync());

    Task<int> DayCampRegistrationWithForm() => factory.WithDb(db => db.Registrations
        .Where(r => r.Session.Program.Slug == "day-camp-atlanta" && r.HealthJson != null).OrderBy(r => r.Id).Select(r => r.Id).FirstAsync());

    Task<string> Snapshot(int programId) => factory.WithDb(async db =>
    {
        var regs = await db.Registrations.Where(r => r.Session.ProgramId == programId)
            .Select(r => new { r.Id, r.Status, r.HealthStatus, r.PriceCents, r.PaidCents, r.DiscountCents }).OrderBy(r => r.Id).ToListAsync();
        var reserved = await db.CapacityPools.Where(p => p.Session.ProgramId == programId).SumAsync(p => p.Reserved);
        return $"{JsonSerializer.Serialize(regs)}|{reserved}";
    });
}

/// <summary>Sync against a fake WorkOS directory. Its own database: syncing revokes everyone the fake doesn't list.</summary>
public class AccessSyncTests(AccessSyncTests.Factory factory) : IClassFixture<AccessSyncTests.Factory>
{
    /// <summary>The app with the WorkOS directory client answered by <see cref="Directory"/>.</summary>
    public sealed class Factory : ApiFactory
    {
        public FakeDirectory Directory { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(s => s.AddHttpClient(StaffDirectory.ClientName).ConfigurePrimaryHttpMessageHandler(() => Directory));
        }
    }

    /// <summary>Answers the WorkOS memberships call from a list the test controls, two per page.</summary>
    public sealed class FakeDirectory : HttpMessageHandler
    {
        public List<(string UserId, string Email, string First, string Last, string Role)> Members { get; } = [];
        public bool Down { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Down) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            if (request.Headers.Authorization?.Parameter is null) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri?.Query ?? "");
            var start = int.TryParse(query["after"], out var a) ? a : 0;
            var page = Members.Skip(start).Take(2).Select(m => new
            {
                user_id = m.UserId,
                status = "active",
                role = new { slug = m.Role },
                user = new { email = m.Email, first_name = m.First, last_name = m.Last },
            }).ToList();
            var after = start + 2 < Members.Count ? (start + 2).ToString(CultureInfo.InvariantCulture) : null;
            var json = JsonSerializer.Serialize(new { data = page, list_metadata = new { after } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task Sync_adds_updates_revokes_restores_and_is_idempotent()
    {
        var alex = await factory.SignInAsStaff("admin", "Alex Morgan");
        await factory.SignInAsStaff("finance", "Marcus Lee"); // on the roster as finance
        factory.Directory.Down = false;
        factory.Directory.Members.Clear();
        factory.Directory.Members.AddRange([
            ("user_test_admin@winshape.example", "admin@winshape.example", "Alex", "Morgan", "admin"),
            ("user_test_finance@winshape.example", "finance@winshape.example", "Marcus", "Lee", "cet"), // role changed in WorkOS
            ("user_priya", "priya.shah@winshape.example", "Priya", "Shah", "finance"), // new
        ]);

        var first = await alex.PostAsync("/api/access/staff/sync", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var run = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, run.GetProperty("members").GetInt32());
        Assert.Equal(1, run.GetProperty("added").GetInt32());

        var staff = await factory.WithDb(db => db.Set<StaffMember>().AsNoTracking().ToListAsync());
        Assert.Equal(StaffStatus.Revoked, staff.Single(m => m.Email == AccessSeed.FormerStaffEmail).Status);
        Assert.NotNull(staff.Single(m => m.Email == AccessSeed.FormerStaffEmail).RevokedAt);
        Assert.Equal("cet", staff.Single(m => m.Email == "finance@winshape.example").Role);
        Assert.Equal(StaffStatus.Active, staff.Single(m => m.Email == "priya.shah@winshape.example").Status);
        var events = await factory.WithDb(db => db.AuditEvents.Where(a => a.EntityType == "StaffMember").ToListAsync());
        Assert.Contains(events, e => e.Action == "staff.revoked" && e.Detail.StartsWith("Morgan Ellis", StringComparison.Ordinal));
        Assert.Contains(events, e => e.Action == "staff.role_synced" && e.Detail.Contains("from Finance to Customer Experience", StringComparison.Ordinal));
        Assert.Contains(events, e => e.Action == "staff.added" && e.Detail.Contains("Priya Shah", StringComparison.Ordinal));

        // A revoked person can't be edited.
        var morgan = staff.Single(m => m.Email == AccessSeed.FormerStaffEmail).Id;
        Assert.Equal(HttpStatusCode.Conflict, (await alex.PutAsJsonAsync($"/api/access/staff/{morgan}", new { ministryId = (int?)null, healthAccess = false })).StatusCode);

        // Nothing changed in WorkOS: nothing changes here.
        var second = await (await alex.PostAsync("/api/access/staff/sync", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, second.GetProperty("added").GetInt32() + second.GetProperty("updated").GetInt32() + second.GetProperty("revoked").GetInt32());

        // Back in the organization: restored.
        factory.Directory.Members.Add(("user_morgan", AccessSeed.FormerStaffEmail, "Morgan", "Ellis", "cet"));
        var third = await (await alex.PostAsync("/api/access/staff/sync", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, third.GetProperty("updated").GetInt32());
        Assert.Equal(StaffStatus.Active, await factory.WithDb(db => db.Set<StaffMember>().Where(m => m.Id == morgan).Select(m => m.Status).SingleAsync()));

        var list = await alex.GetFromJsonAsync<JsonElement>("/api/access/staff");
        Assert.False(list.GetProperty("syncDue").GetBoolean());
    }

    [Fact]
    public async Task Syncs_started_together_revoke_and_audit_once()
    {
        var alex = await factory.SignInAsStaff("admin", "Alex Morgan");
        factory.Directory.Down = false;
        factory.Directory.Members.Clear();
        factory.Directory.Members.AddRange([
            ("user_test_admin@winshape.example", "admin@winshape.example", "Alex", "Morgan", "admin"),
            ("user_temp", "temp.helper@winshape.example", "Terry", "Helper", "cet"),
        ]);
        Assert.Equal(HttpStatusCode.OK, (await alex.PostAsync("/api/access/staff/sync", null)).StatusCode);
        var terry = await factory.WithDb(db => db.Set<StaffMember>().Where(m => m.Email == "temp.helper@winshape.example").Select(m => m.Id).SingleAsync());

        // Two tabs open K11 at once after Terry leaves the organization.
        factory.Directory.Members.RemoveAt(1);
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => alex.PostAsync("/api/access/staff/sync", null)));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var revocations = await factory.WithDb(db => db.AuditEvents.CountAsync(a => a.Action == "staff.revoked" && a.EntityType == "StaffMember" && a.EntityId == terry.ToString(CultureInfo.InvariantCulture)));
        Assert.Equal(1, revocations);
    }

    [Fact]
    public async Task A_failed_sync_changes_no_one()
    {
        var alex = await factory.SignInAsStaff("admin", "Alex Morgan");
        var before = await factory.WithDb(db => db.Set<StaffMember>().OrderBy(m => m.Id).Select(m => m.Status).ToListAsync());
        var runs = await factory.WithDb(db => db.Set<StaffSyncRun>().CountAsync());
        factory.Directory.Down = true;
        try
        {
            var res = await alex.PostAsync("/api/access/staff/sync", null);
            Assert.Equal(HttpStatusCode.BadGateway, res.StatusCode);
            Assert.Contains("No one's access changed", await res.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        finally
        {
            factory.Directory.Down = false;
        }
        Assert.Equal(before, await factory.WithDb(db => db.Set<StaffMember>().OrderBy(m => m.Id).Select(m => m.Status).ToListAsync()));
        Assert.Equal(runs, await factory.WithDb(db => db.Set<StaffSyncRun>().CountAsync()));
    }

    [Fact]
    public async Task A_revoked_staff_member_cannot_read_health_details_even_with_a_live_session()
    {
        var alex = await factory.SignInAsStaff("admin", "Alex Morgan");
        var cet = await factory.SignInAsStaff("cet", "Diane Carter");
        var day = await factory.WithDb(db => db.Programs.Where(p => p.Slug == "day-camp-atlanta").Select(p => p.Id).SingleAsync());
        var cetId = await factory.WithDb(db => db.Set<StaffMember>().Where(m => m.Email == "cet@winshape.example").Select(m => m.Id).SingleAsync());
        await alex.PutAsJsonAsync($"/api/access/staff/{cetId}", new { ministryId = (int?)null, healthAccess = true });
        await alex.PutAsJsonAsync($"/api/access/health/{day}", new { mechanism = "Embedded", viewerRoles = Roles.AdminCet });
        var reg = await factory.WithDb(db => db.Registrations.Where(r => r.Session.ProgramId == day && r.HealthJson != null).Select(r => r.Id).FirstAsync());
        Assert.Equal(HttpStatusCode.OK, (await cet.GetAsync($"/api/access/registrations/{reg}/health")).StatusCode);

        // Removed from the organization: the next sync revokes, and the same cookie is refused.
        factory.Directory.Down = false;
        factory.Directory.Members.Clear();
        factory.Directory.Members.Add(("user_test_admin@winshape.example", "admin@winshape.example", "Alex", "Morgan", "admin"));
        Assert.Equal(HttpStatusCode.OK, (await alex.PostAsync("/api/access/staff/sync", null)).StatusCode);
        var refused = await cet.GetAsync($"/api/access/registrations/{reg}/health");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("revoked", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_directory_client_reads_every_page()
    {
        var alex = await factory.SignInAsStaff("admin", "Alex Morgan");
        factory.Directory.Down = false;
        factory.Directory.Members.Clear();
        for (var i = 0; i < 5; i++) factory.Directory.Members.Add(($"user_page_{i}", $"page{i}@winshape.example", "Page", $"Person{i}", "cet"));
        factory.Directory.Members.Add(("user_test_admin@winshape.example", "admin@winshape.example", "Alex", "Morgan", "admin"));
        var run = await (await alex.PostAsync("/api/access/staff/sync", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(6, run.GetProperty("members").GetInt32());
        Assert.Equal(5, await factory.WithDb(db => db.Set<StaffMember>().CountAsync(m => m.Email.StartsWith("page") && m.Status == StaffStatus.Active)));
    }
}

/// <summary>Viewer role lists sent to the health settings endpoint.</summary>
static class Roles
{
    public static readonly string[] Admin = ["admin"];
    public static readonly string[] AdminCet = ["admin", "cet"];
    public static readonly string[] Cet = ["cet"];
    public static readonly string[] Host = ["host"];
    public static readonly string[] None = [];
}
