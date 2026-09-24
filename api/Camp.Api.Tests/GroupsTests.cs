using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Groups;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Camp.Api.Tests;

public class GroupsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    readonly FakeFiservGateway _gateway = (FakeFiservGateway)factory.Services.GetRequiredService<IPaymentGateway>();

    [Fact]
    public async Task Seeded_group_matches_the_canonical_numbers()
    {
        using var dave = await Dave();
        var groups = await dave.GetFromJsonAsync<JsonElement>("/api/groups");
        var seeded = groups.EnumerateArray().Single(g => g.GetProperty("name").GetString() == GroupsSeed.GroupName);
        var counts = seeded.GetProperty("counts");
        Assert.Equal(14, counts.GetProperty("attendees").GetInt32());
        Assert.Equal(9, counts.GetProperty("complete").GetInt32());
        Assert.Equal(5, counts.GetProperty("incomplete").GetInt32());
        Assert.Equal(1, counts.GetProperty("withdrawalRequests").GetInt32());

        // The pool's reserved count is backed by rows, and the charge is 14 × $450.
        var (reserved, rows, charged) = await factory.WithDb(async db =>
        {
            var pool = await db.CapacityPools.OrderBy(p => p.Id).FirstAsync(p => p.Session.Program.Slug == "emerging-leaders-cohort");
            var active = await db.Set<GroupAttendee>().CountAsync(a => a.IsActive && a.Group.SessionId == pool.SessionId && a.Group.Status == GroupStatus.Confirmed);
            var order = await db.Set<GroupRegistration>().Where(g => g.Name == GroupsSeed.GroupName).Select(g => g.Order!.TotalCents).SingleAsync();
            return (pool.Reserved, active, order);
        });
        Assert.Equal(rows, reserved);
        Assert.Equal(14 * 45000, charged);
    }

    [Fact]
    public async Task Leader_registers_a_group_with_partial_rows_and_pays_for_every_attendee()
    {
        var sessionId = await IsolatedCohortSession(capacity: 10);
        using var dave = await Dave();
        var id = await CreateGroup(dave, sessionId, ("Ana Ruiz", "ana.ruiz@example.com"), ("Ben Ode", "ben.ode@example.com"), ("Cara Dunn", null), ("", ""));

        var res = await dave.PostAsJsonAsync($"/api/groups/{id}/checkout", new { IdempotencyKey = $"g-{Guid.NewGuid()}", CardToken = Card("4242424242424242") });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var detail = await dave.GetFromJsonAsync<JsonElement>($"/api/groups/{id}");
        Assert.Equal("Confirmed", detail.GetProperty("status").GetString());
        Assert.Equal(3, detail.GetProperty("counts").GetProperty("attendees").GetInt32()); // the blank row was dropped
        Assert.Equal(1, detail.GetProperty("counts").GetProperty("noEmail").GetInt32());
        Assert.Equal(3 * 45000, detail.GetProperty("payment").GetProperty("chargedCents").GetInt32());

        var (reserved, links, audited) = await factory.WithDb(async db => (
            await db.CapacityPools.Where(p => p.SessionId == sessionId).Select(p => p.Reserved).SingleAsync(),
            await db.OutboxEvents.CountAsync(e => e.Type == "GroupFormLink" && e.AggregateId == $"group-{id}"),
            await db.AuditEvents.AnyAsync(a => a.Action == "group.confirmed" && a.EntityId == id.ToString(CultureInfo.InvariantCulture))));
        Assert.Equal(3, reserved);
        Assert.Equal(2, links); // only attendees with an email get a link
        Assert.True(audited);

        // Once paid for, the roster is locked.
        using var locked = await dave.PutAsJsonAsync($"/api/groups/{id}/roster", new { Attendees = new[] { new { Name = "Late Add", Email = "late@example.com" } } });
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
    }

    [Fact]
    public async Task Declined_card_releases_every_seat_and_keeps_the_roster()
    {
        var sessionId = await IsolatedCohortSession(capacity: 5);
        using var dave = await Dave();
        var id = await CreateGroup(dave, sessionId, ("Ana Ruiz", "ana@example.com"), ("Ben Ode", "ben@example.com"), ("Cara Dunn", "cara@example.com"));

        var declined = await dave.PostAsJsonAsync($"/api/groups/{id}/checkout", new { IdempotencyKey = $"g-{Guid.NewGuid()}", CardToken = Card("4000000000000002") });
        Assert.Equal(HttpStatusCode.PaymentRequired, declined.StatusCode);
        Assert.Equal(0, await Reserved(sessionId));
        var draft = await dave.GetFromJsonAsync<JsonElement>($"/api/groups/{id}");
        Assert.Equal("Draft", draft.GetProperty("status").GetString());
        Assert.Equal(3, draft.GetProperty("attendees").GetArrayLength());

        // Retrying with another card works on the same roster.
        var paid = await dave.PostAsJsonAsync($"/api/groups/{id}/checkout", new { IdempotencyKey = $"g-{Guid.NewGuid()}", CardToken = Card("4242424242424242") });
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal(3, await Reserved(sessionId));
    }

    [Fact]
    public async Task A_group_larger_than_the_open_seats_is_refused_before_charging()
    {
        var sessionId = await IsolatedCohortSession(capacity: 2);
        using var dave = await Dave();
        var id = await CreateGroup(dave, sessionId, ("Ana Ruiz", "ana@example.com"), ("Ben Ode", "ben@example.com"), ("Cara Dunn", "cara@example.com"));
        var before = _gateway.ChargeCount;

        var res = await dave.PostAsJsonAsync($"/api/groups/{id}/checkout", new { IdempotencyKey = $"g-{Guid.NewGuid()}", CardToken = Card("4242424242424242") });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("Only 2 spots are left", await res.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, _gateway.ChargeCount);
        Assert.Equal(0, await Reserved(sessionId));
    }

    [Fact]
    public async Task Double_submit_with_the_same_key_charges_once()
    {
        var sessionId = await IsolatedCohortSession(capacity: 10);
        using var dave = await Dave();
        var id = await CreateGroup(dave, sessionId, ("Ana Ruiz", "ana@example.com"), ("Ben Ode", "ben@example.com"));
        var body = new { IdempotencyKey = $"g-{Guid.NewGuid()}", CardToken = Card("4242424242424242") };
        var before = _gateway.ChargeCount;

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => dave.PostAsJsonAsync($"/api/groups/{id}/checkout", body)));

        Assert.Contains(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(before + 1, _gateway.ChargeCount);
        Assert.Equal(2, await Reserved(sessionId));
    }

    [Fact]
    public async Task Roster_rows_need_a_name_and_a_valid_unique_email_when_one_is_given()
    {
        var sessionId = await IsolatedCohortSession(capacity: 10);
        using var dave = await Dave();
        using var res = await dave.PostAsJsonAsync("/api/groups", new
        {
            SessionId = sessionId,
            Attendees = new[]
            {
                new { Name = "Ana Ruiz", Email = "ana@example.com" },
                new { Name = "Ben Ode", Email = "ANA@example.com" },
                new { Name = "", Email = "nobody@example.com" },
                new { Name = "Cara Dunn", Email = "not-an-email" },
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("attendees[1].email", out _));
        Assert.True(errors.TryGetProperty("attendees[2].name", out _));
        Assert.True(errors.TryGetProperty("attendees[3].email", out _));

        // Standard programs can't be registered as a group.
        var dayCamp = await factory.WithDb(db => db.Sessions.Where(s => s.Program.Slug == "day-camp-atlanta").Select(s => s.Id).FirstAsync());
        using var wrong = await dave.PostAsJsonAsync("/api/groups", new { SessionId = dayCamp, Attendees = new[] { new { Name = "Ana", Email = "a@example.com" } } });
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
    }

    [Fact]
    public async Task Only_the_leader_can_see_or_act_on_their_group()
    {
        var seededId = await factory.WithDb(db => db.Set<GroupRegistration>().Where(g => g.Name == GroupsSeed.GroupName).Select(g => g.Id).SingleAsync());
        var mateo = await factory.WithDb(db => db.Set<GroupAttendee>().Where(a => a.Name == "Mateo Silva").Select(a => a.Id).SingleAsync());

        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/groups")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/groups/{seededId}")).StatusCode);

        using var staff = await factory.SignInAsStaff();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/groups/{seededId}")).StatusCode);

        using var maria = await factory.SignInAsFamily();
        var mine = await maria.GetFromJsonAsync<JsonElement>("/api/groups");
        Assert.Equal(0, mine.GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await maria.GetAsync($"/api/groups/{seededId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await maria.PostAsJsonAsync($"/api/groups/{seededId}/resend", new { AttendeeIds = new[] { mateo } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await maria.PostAsync($"/api/groups/{seededId}/attendees/{mateo}/withdrawal/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await maria.PutAsJsonAsync($"/api/groups/{seededId}/attendees/{mateo}/email", new { Email = "x@example.com" })).StatusCode);

        // Nothing changed for Mateo.
        var still = await factory.WithDb(db => db.Set<GroupAttendee>().Where(a => a.Id == mateo).Select(a => a.Withdrawal).SingleAsync());
        Assert.Equal(WithdrawalStatus.Requested, still);
    }

    [Fact]
    public async Task Secure_link_shows_one_attendee_and_completes_their_forms()
    {
        var (id, _, links) = await PaidGroup(("Ana Ruiz", "ana@example.com"), ("Ben Ode", "ben@example.com"));
        using var dave = await Dave();
        var anaId = await factory.WithDb(db => db.Set<GroupAttendee>().Where(a => a.GroupId == id && a.Name == "Ana Ruiz").Select(a => a.Id).SingleAsync());
        var sent = await (await dave.PostAsJsonAsync($"/api/groups/{id}/resend", new { AttendeeIds = new[] { anaId } })).Content.ReadFromJsonAsync<ResendResult>();
        var token = sent!.Sent.Single().Link["/g/".Length..];
        Assert.Equal(2, links);

        using var anonymous = factory.CreateClient();
        var view = await anonymous.GetStringAsync($"/api/group-links/{token}");
        Assert.Contains("Ana Ruiz", view, StringComparison.Ordinal);
        Assert.Contains("Dave Kim", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Ben Ode", view, StringComparison.Ordinal); // never other attendees

        var waivers = JsonDocument.Parse(view).RootElement.GetProperty("waivers").EnumerateArray().Select(w => w.GetProperty("id").GetInt32()).ToList();
        var answers = new Dictionary<string, string> { ["emergencyName"] = "Luis Ruiz", ["emergencyPhone"] = "(706) 555-0101", ["tshirt"] = "Adult M" };

        using var missingWaiver = await anonymous.PostAsJsonAsync($"/api/group-links/{token}/forms", new
        {
            Email = "ana@example.com",
            SignerName = "Ana Ruiz",
            Answers = answers,
            Waivers = waivers.Take(1).Select(w => new { WaiverId = w, Accepted = true }),
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingWaiver.StatusCode);

        using var ok = await anonymous.PostAsJsonAsync($"/api/group-links/{token}/forms", new
        {
            Email = "ana.ruiz@example.com",
            SignerName = "Ana Ruiz",
            Answers = answers,
            Waivers = waivers.Select(w => new { WaiverId = w, Accepted = true }),
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var detail = await dave.GetFromJsonAsync<JsonElement>($"/api/groups/{id}");
        Assert.Equal(1, detail.GetProperty("counts").GetProperty("complete").GetInt32());
        Assert.Equal(1, detail.GetProperty("counts").GetProperty("incomplete").GetInt32());
        Assert.Equal(waivers.Count, await factory.WithDb(db => db.Set<GroupWaiverAcceptance>().CountAsync(w => w.AttendeeId == anaId)));

        // Resending rotates the token: the old link stops working.
        await dave.PostAsJsonAsync($"/api/groups/{id}/resend", new { AttendeeIds = new[] { anaId } });
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/group-links/{token}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/group-links/not-a-real-token")).StatusCode);
    }

    [Fact]
    public async Task Approving_a_withdrawal_refunds_one_share_and_releases_one_seat()
    {
        var (id, sessionId, _) = await PaidGroup(("Ana Ruiz", "ana@example.com"), ("Ben Ode", "ben@example.com"));
        using var dave = await Dave();
        var ben = await factory.WithDb(db => db.Set<GroupAttendee>().Where(a => a.GroupId == id && a.Name == "Ben Ode").Select(a => a.Id).SingleAsync());
        var sent = await (await dave.PostAsJsonAsync($"/api/groups/{id}/resend", new { AttendeeIds = new[] { ben } })).Content.ReadFromJsonAsync<ResendResult>();
        var token = sent!.Sent.Single().Link["/g/".Length..];

        // Nothing to approve before the attendee asks.
        Assert.Equal(HttpStatusCode.BadRequest, (await dave.PostAsync($"/api/groups/{id}/attendees/{ben}/withdrawal/approve", null)).StatusCode);

        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync($"/api/group-links/{token}/withdrawal", new { Reason = "Schedule conflict" })).StatusCode);
        Assert.Equal(1, (await dave.GetFromJsonAsync<JsonElement>($"/api/groups/{id}")).GetProperty("counts").GetProperty("withdrawalRequests").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await dave.PostAsync($"/api/groups/{id}/attendees/{ben}/withdrawal/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await dave.PostAsync($"/api/groups/{id}/attendees/{ben}/withdrawal/approve", null)).StatusCode);

        var detail = await dave.GetFromJsonAsync<JsonElement>($"/api/groups/{id}");
        Assert.Equal(1, detail.GetProperty("counts").GetProperty("attendees").GetInt32());
        Assert.Equal(1, detail.GetProperty("counts").GetProperty("withdrawn").GetInt32());
        Assert.Equal(45000, detail.GetProperty("payment").GetProperty("refundedCents").GetInt32());
        Assert.Equal(1, await Reserved(sessionId));
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(a => a.Action == "group.withdrawal_approved" && a.Actor == "Dave Kim")));
    }

    [Fact]
    public async Task Declining_a_withdrawal_keeps_the_attendee_and_the_money()
    {
        var (id, sessionId, _) = await PaidGroup(("Ana Ruiz", "ana@example.com"));
        using var dave = await Dave();
        var ana = await factory.WithDb(db => db.Set<GroupAttendee>().Where(a => a.GroupId == id).Select(a => a.Id).SingleAsync());
        var sent = await (await dave.PostAsJsonAsync($"/api/groups/{id}/resend", new { AttendeeIds = new[] { ana } })).Content.ReadFromJsonAsync<ResendResult>();
        using var anonymous = factory.CreateClient();
        await anonymous.PostAsJsonAsync($"/api/group-links/{sent!.Sent.Single().Link["/g/".Length..]}/withdrawal", new { Reason = (string?)null });

        Assert.Equal(HttpStatusCode.NoContent, (await dave.PostAsync($"/api/groups/{id}/attendees/{ana}/withdrawal/decline", null)).StatusCode);

        var detail = await dave.GetFromJsonAsync<JsonElement>($"/api/groups/{id}");
        Assert.Equal(1, detail.GetProperty("counts").GetProperty("attendees").GetInt32());
        Assert.Equal(0, detail.GetProperty("payment").GetProperty("refundedCents").GetInt32());
        Assert.Equal(1, await Reserved(sessionId));
    }

    [Fact]
    public async Task Adding_a_missing_email_after_payment_sends_that_attendee_a_link()
    {
        var (id, _, links) = await PaidGroup(("Ana Ruiz", null));
        Assert.Equal(0, links);
        using var dave = await Dave();
        var ana = await factory.WithDb(db => db.Set<GroupAttendee>().Where(a => a.GroupId == id).Select(a => a.Id).SingleAsync());

        using var bad = await dave.PutAsJsonAsync($"/api/groups/{id}/attendees/{ana}/email", new { Email = "nope" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var res = await (await dave.PutAsJsonAsync($"/api/groups/{id}/attendees/{ana}/email", new { Email = "Ana.Ruiz@example.com" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ana.ruiz@example.com", res.GetProperty("email").GetString());
        Assert.StartsWith("/g/", res.GetProperty("link").GetProperty("link").GetString(), StringComparison.Ordinal);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    Task<HttpClient> Dave() => factory.SignInAsFamily(GroupsSeed.LeaderEmail, "Dave", "Kim");

    string Card(string number) => _gateway.Tokenize(number);

    Task<int> Reserved(int sessionId) => factory.WithDb(db => db.CapacityPools.Where(p => p.SessionId == sessionId).Select(p => p.Reserved).SingleAsync());

    static async Task<int> CreateGroup(HttpClient client, int sessionId, params (string Name, string? Email)[] rows)
    {
        using var res = await client.PostAsJsonAsync("/api/groups", new { SessionId = sessionId, Name = "Test group", Attendees = rows.Select(r => new { r.Name, r.Email }) });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    /// <summary>A paid group on its own cohort session. Returns the links queued at confirmation.</summary>
    async Task<(int GroupId, int SessionId, int Links)> PaidGroup(params (string Name, string? Email)[] rows)
    {
        var sessionId = await IsolatedCohortSession(capacity: 10);
        using var dave = await Dave();
        var id = await CreateGroup(dave, sessionId, rows);
        using var res = await dave.PostAsJsonAsync($"/api/groups/{id}/checkout", new { IdempotencyKey = $"g-{Guid.NewGuid()}", CardToken = Card("4242424242424242") });
        res.EnsureSuccessStatusCode();
        var links = await factory.WithDb(db => db.OutboxEvents.CountAsync(e => e.Type == "GroupFormLink" && e.AggregateId == $"group-{id}"));
        return (id, sessionId, links);
    }

    /// <summary>A fresh session of the cohort program with its own pool, so tests don't share seats.</summary>
    async Task<int> IsolatedCohortSession(int capacity)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampDbContext>();
        var template = await db.Sessions.AsNoTracking().FirstAsync(s => s.Program.Slug == "emerging-leaders-cohort");
        var session = new Session
        {
            ProgramId = template.ProgramId,
            Name = $"Test {Guid.NewGuid():N}"[..20],
            StartDate = template.StartDate,
            EndDate = template.EndDate,
            PriceCents = template.PriceCents,
            BalanceDueDate = template.BalanceDueDate,
        };
        session.Pools.Add(new CapacityPool { Name = "Attendees", GradeMin = 99, GradeMax = 99, Capacity = capacity, SortOrder = 1 });
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }
}
