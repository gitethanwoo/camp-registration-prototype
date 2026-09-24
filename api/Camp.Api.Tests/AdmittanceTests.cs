using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Camp.Api.Domain;
using Camp.Api.Features.Admittance;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Camp.Api.Tests;

/// <summary>
/// Admittance (R2/F7/C6): the card is authorized at submit and captured only on approval; a seat
/// is claimed at approval and never past capacity; one household can't see another's application.
/// </summary>
public class AdmittanceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    readonly FakeFiservGateway _gateway = (FakeFiservGateway)factory.Services.GetRequiredService<IPaymentGateway>();

    static readonly Dictionary<string, string> GoodAnswers = new()
    {
        ["yearsMarried"] = "6–15 years",
        ["why"] = "We want time away to focus on each other.",
        ["goals"] = "Better communication.",
        ["heardFrom"] = "Our church",
        ["attendedBefore"] = "No",
    };

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Submitting_authorizes_the_card_without_charging_or_taking_a_seat()
    {
        var sessionId = await IsolatedRetreat(capacity: 5);
        var (family, _) = await NewCouple("Submit");
        var charges = _gateway.ChargeCount;
        var captures = _gateway.CaptureCount;

        var id = await Apply(family, sessionId);

        var status = await family.GetFromJsonAsync<JsonElement>($"/api/admittance/applications/{id}");
        Assert.Equal("Submitted", status.GetProperty("stage").GetString());
        Assert.Equal("ApplicationPending", status.GetProperty("status").GetString());
        Assert.Equal("Authorized", status.GetProperty("payment").GetProperty("state").GetString());
        Assert.Equal(90000, status.GetProperty("payment").GetProperty("amountCents").GetInt32());
        Assert.Equal(charges, _gateway.ChargeCount);
        Assert.Equal(captures, _gateway.CaptureCount);
        Assert.Equal(0, await Reserved(sessionId));
        Assert.Equal(0, await factory.WithDb(db => db.Orders.CountAsync(o => o.SessionId == sessionId)));
    }

    [Fact]
    public async Task Approving_captures_once_claims_one_seat_and_confirms_a_paid_registration()
    {
        var sessionId = await IsolatedRetreat(capacity: 5);
        var (family, householdId) = await NewCouple("Approve");
        var id = await Apply(family, sessionId);
        var staff = await factory.SignInAsStaff();
        var captures = _gateway.CaptureCount;

        var res = await staff.PostAsync($"/api/admin/admittance/applications/{id}/approve", null);

        res.EnsureSuccessStatusCode();
        Assert.True((await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("captured").GetBoolean());
        Assert.Equal(captures + 1, _gateway.CaptureCount);
        Assert.Equal(1, await Reserved(sessionId));
        var reg = await factory.WithDb(db => db.Registrations.Include(r => r.Order).AsNoTracking().SingleAsync(r => r.SessionId == sessionId));
        Assert.Equal(RegistrationStatus.Confirmed, reg.Status);
        Assert.Equal(householdId, reg.HouseholdId);
        Assert.Equal(90000, reg.PaidCents);
        Assert.Equal(reg.PriceCents, reg.PaidCents); // nothing left owing
        Assert.Equal(OrderStatus.Paid, reg.Order!.Status);

        var status = await family.GetFromJsonAsync<JsonElement>($"/api/admittance/applications/{id}");
        Assert.Equal("Confirmed", status.GetProperty("status").GetString());
        Assert.Equal("Paid", status.GetProperty("payment").GetProperty("state").GetString());
        Assert.StartsWith("WS-", status.GetProperty("confirmationCode").GetString());
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(e => e.Action == "application.approved" && e.EntityId == $"{id}" && e.Actor.Contains("Diane Carter"))));
    }

    [Fact]
    public async Task Two_staff_approving_at_once_capture_once_and_claim_one_seat()
    {
        var sessionId = await IsolatedRetreat(capacity: 5);
        var (family, _) = await NewCouple("Race");
        var id = await Apply(family, sessionId);
        var a = await factory.SignInAsStaff();
        var b = await factory.SignInAsStaff("admin", "Grace Lee");
        var captures = _gateway.CaptureCount;

        var results = await Task.WhenAll(
            a.PostAsync($"/api/admin/admittance/applications/{id}/approve", null),
            b.PostAsync($"/api/admin/admittance/applications/{id}/approve", null));

        Assert.Contains(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(results, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"got {r.StatusCode}"));
        Assert.Equal(captures + 1, _gateway.CaptureCount);
        Assert.Equal(1, await Reserved(sessionId));
        Assert.Equal(1, await factory.WithDb(db => db.Registrations.CountAsync(r => r.SessionId == sessionId)));
    }

    [Fact]
    public async Task Declining_voids_the_hold_and_takes_no_seat()
    {
        var sessionId = await IsolatedRetreat(capacity: 5);
        var (family, _) = await NewCouple("Decline");
        var id = await Apply(family, sessionId);
        var staff = await factory.SignInAsStaff();
        var voids = _gateway.VoidCount;

        var noNote = await staff.PostAsJsonAsync($"/api/admin/admittance/applications/{id}/decline", new { message = " " });
        Assert.Equal(HttpStatusCode.BadRequest, noNote.StatusCode);

        var res = await staff.PostAsJsonAsync($"/api/admin/admittance/applications/{id}/decline", new { message = "This retreat is for couples married five years or more." });

        res.EnsureSuccessStatusCode();
        Assert.Equal(voids + 1, _gateway.VoidCount);
        Assert.Equal(0, await Reserved(sessionId));
        var status = await family.GetFromJsonAsync<JsonElement>($"/api/admittance/applications/{id}");
        Assert.Equal("Declined", status.GetProperty("status").GetString());
        Assert.Equal("Voided", status.GetProperty("payment").GetProperty("state").GetString());
        Assert.Contains("five years", status.GetProperty("decisionNote").GetString());
        Assert.Equal(0, await factory.WithDb(db => db.Orders.CountAsync(o => o.SessionId == sessionId)));
    }

    [Fact]
    public async Task Request_info_then_reply_returns_the_application_to_review()
    {
        var sessionId = await IsolatedRetreat(capacity: 5);
        var (family, _) = await NewCouple("Info");
        var id = await Apply(family, sessionId);
        var staff = await factory.SignInAsStaff();

        (await staff.PostAsJsonAsync($"/api/admin/admittance/applications/{id}/request-info", new { message = "Which church do you attend?" })).EnsureSuccessStatusCode();
        var asked = await family.GetFromJsonAsync<JsonElement>($"/api/admittance/applications/{id}");
        Assert.Equal("InfoRequested", asked.GetProperty("stage").GetString());
        Assert.Equal("Which church do you attend?", asked.GetProperty("infoRequest").GetString());

        (await family.PostAsJsonAsync($"/api/admittance/applications/{id}/reply", new { message = "North Point." })).EnsureSuccessStatusCode();
        var detail = await staff.GetFromJsonAsync<JsonElement>($"/api/admin/admittance/applications/{id}");
        Assert.Equal("UnderReview", detail.GetProperty("stage").GetString());
        Assert.Equal("North Point.", detail.GetProperty("infoResponse").GetString());
    }

    // ── Capacity ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Approving_when_full_is_refused_and_waitlisting_is_allowed_only_then()
    {
        var sessionId = await IsolatedRetreat(capacity: 1);
        var (first, _) = await NewCouple("Full-A");
        var (second, _) = await NewCouple("Full-B");
        var a = await Apply(first, sessionId);
        var b = await Apply(second, sessionId);
        var staff = await factory.SignInAsStaff();

        var early = await staff.PostAsync($"/api/admin/admittance/applications/{b}/waitlist", null);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode); // seats are open: approve or decline

        (await staff.PostAsync($"/api/admin/admittance/applications/{a}/approve", null)).EnsureSuccessStatusCode();
        var full = await staff.PostAsync($"/api/admin/admittance/applications/{b}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        Assert.Contains("is full", (await full.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(1, await Reserved(sessionId));

        var voids = _gateway.VoidCount;
        (await staff.PostAsync($"/api/admin/admittance/applications/{b}/waitlist", null)).EnsureSuccessStatusCode();
        Assert.Equal(voids + 1, _gateway.VoidCount);
        var status = await second.GetFromJsonAsync<JsonElement>($"/api/admittance/applications/{b}");
        Assert.Equal("Waitlisted", status.GetProperty("status").GetString());
        Assert.Equal(1, await Reserved(sessionId));
    }

    // ── Card holds ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_lapsed_hold_is_not_captured_on_approval_and_a_new_card_confirms_the_held_seat()
    {
        var sessionId = await IsolatedRetreat(capacity: 5);
        var (family, _) = await NewCouple("Lapsed");
        var id = await Apply(family, sessionId);
        await factory.WithDb(db => db.Set<AdmittanceApplication>().Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.AuthorizationExpiresAt, DateTime.UtcNow.AddDays(-1))));
        var staff = await factory.SignInAsStaff();
        var captures = _gateway.CaptureCount;

        var res = await staff.PostAsync($"/api/admin/admittance/applications/{id}/approve", null);

        res.EnsureSuccessStatusCode();
        Assert.False((await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("captured").GetBoolean());
        Assert.Equal(captures, _gateway.CaptureCount);
        Assert.Equal(1, await Reserved(sessionId)); // the seat is held while the couple updates their card
        var waiting = await family.GetFromJsonAsync<JsonElement>($"/api/admittance/applications/{id}");
        Assert.Equal("PaymentPending", waiting.GetProperty("status").GetString());
        Assert.True(waiting.GetProperty("payment").GetProperty("canUpdateCard").GetBoolean());

        var reauth = await family.PostAsJsonAsync($"/api/admittance/applications/{id}/reauthorize", new { cardToken = Card("4242424242424242"), idempotencyKey = "re-1" });

        reauth.EnsureSuccessStatusCode();
        Assert.Equal(captures + 1, _gateway.CaptureCount);
        Assert.Equal(1, await Reserved(sessionId));
        var done = await family.GetFromJsonAsync<JsonElement>($"/api/admittance/applications/{id}");
        Assert.Equal("Confirmed", done.GetProperty("status").GetString());
        Assert.Equal("Paid", done.GetProperty("payment").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Declining_after_approval_releases_the_seat_in_the_pool_approval_claimed()
    {
        var sessionId = await IsolatedRetreat(capacity: 5);
        var other = await factory.WithDb(async db =>
        {
            // A second pool in the same session with seats taken; a decline must not touch it.
            var pool = new CapacityPool { SessionId = sessionId, Name = "Staff couples", GradeMin = 99, GradeMax = 99, Capacity = 10, Reserved = 4, SortOrder = 1 };
            db.CapacityPools.Add(pool);
            await db.SaveChangesAsync();
            return pool.Id;
        });
        var (family, _) = await NewCouple("Release");
        var id = await Apply(family, sessionId);
        await factory.WithDb(db => db.Set<AdmittanceApplication>().Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.AuthorizationExpiresAt, DateTime.UtcNow.AddDays(-1))));
        var staff = await factory.SignInAsStaff();
        (await staff.PostAsync($"/api/admin/admittance/applications/{id}/approve", null)).EnsureSuccessStatusCode();
        var claimed = await factory.WithDb(db => db.Set<AdmittanceApplication>().Where(a => a.Id == id).Select(a => a.PoolId).SingleAsync());
        Assert.NotNull(claimed);
        Assert.NotEqual(other, claimed);
        Assert.Equal(5, await Reserved(sessionId));

        var declined = await staff.PostAsJsonAsync($"/api/admin/admittance/applications/{id}/decline", new { message = "The couple never updated their card." });

        declined.EnsureSuccessStatusCode();
        Assert.Equal(4, await Reserved(sessionId));
        Assert.Equal(4, await factory.WithDb(db => db.CapacityPools.Where(p => p.Id == other).Select(p => p.Reserved).SingleAsync()));
    }

    [Fact]
    public async Task Reauthorizing_twice_at_once_leaves_exactly_one_open_hold()
    {
        var sessionId = await IsolatedRetreat(capacity: 5);
        var (family, _) = await NewCouple("Reauth");
        var id = await Apply(family, sessionId);
        await factory.WithDb(db => db.Set<AdmittanceApplication>().Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.AuthorizationExpiresAt, DateTime.UtcNow.AddDays(-1))));
        var authorized = _gateway.AuthorizeCount;
        var voided = _gateway.VoidCount;

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
            family.PostAsJsonAsync($"/api/admittance/applications/{id}/reauthorize", new { cardToken = Card("4242424242424242"), idempotencyKey = $"race-{i}" })));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        // Every new hold but the saved one was voided, and so was the lapsed one it replaced.
        Assert.Equal(_gateway.AuthorizeCount - authorized, _gateway.VoidCount - voided);
        var app = await factory.WithDb(db => db.Set<AdmittanceApplication>().AsNoTracking().SingleAsync(a => a.Id == id));
        Assert.Equal(HoldStatus.Authorized, app.Hold);
        Assert.True(app.AuthorizationExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task A_declined_card_leaves_the_application_as_a_draft()
    {
        var sessionId = await IsolatedRetreat(capacity: 5);
        var (family, _) = await NewCouple("Card");
        var id = await SaveDraft(family, sessionId, GoodAnswers);

        var res = await family.PostAsJsonAsync($"/api/admittance/applications/{id}/submit", new { cardToken = Card("4000000000000002"), idempotencyKey = "k1" });

        Assert.Equal(HttpStatusCode.PaymentRequired, res.StatusCode);
        Assert.Contains("declined", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(ApplicationStage.Draft, await Stage(id));
    }

    [Fact]
    public async Task Missing_answers_are_refused_and_submitted_applications_are_locked()
    {
        var sessionId = await IsolatedRetreat(capacity: 5);
        var (family, _) = await NewCouple("Lock");
        var partial = new Dictionary<string, string>(GoodAnswers) { ["attendedBefore"] = "Yes" }; // now "which event" is required
        var id = await SaveDraft(family, sessionId, partial);

        var missing = await family.PostAsJsonAsync($"/api/admittance/applications/{id}/submit", new { cardToken = Card("4242424242424242"), idempotencyKey = "k1" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.True((await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("answers.attendedWhich", out _));

        await SaveDraft(family, sessionId, GoodAnswers);
        (await family.PostAsJsonAsync($"/api/admittance/applications/{id}/submit", new { cardToken = Card("4242424242424242"), idempotencyKey = "k2" })).EnsureSuccessStatusCode();
        var edit = await family.PutAsJsonAsync($"/api/admittance/sessions/{sessionId}/draft", Draft(GoodAnswers));
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
    }

    // ── Authorization ────────────────────────────────────────────────────────

    [Fact]
    public async Task Anonymous_callers_get_401_and_guests_cannot_reach_the_review_queue()
    {
        var sessionId = await IsolatedRetreat(capacity: 5);
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/admittance/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/admin/admittance/sessions/{sessionId}/applications")).StatusCode);

        var (family, _) = await NewCouple("Guest");
        var id = await Apply(family, sessionId);
        Assert.Equal(HttpStatusCode.Forbidden, (await family.GetAsync($"/api/admin/admittance/sessions/{sessionId}/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await family.PostAsync($"/api/admin/admittance/applications/{id}/approve", null)).StatusCode);

        // Finance can read the queue but decisions are CET work.
        var finance = await factory.SignInAsStaff("finance", "Marcus Webb");
        Assert.Equal(HttpStatusCode.OK, (await finance.GetAsync($"/api/admin/admittance/sessions/{sessionId}/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.PostAsync($"/api/admin/admittance/applications/{id}/approve", null)).StatusCode);
        Assert.Equal(ApplicationStage.Submitted, await Stage(id));

        // Staff can't use the family endpoints.
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.GetAsync("/api/admittance/applications")).StatusCode);
    }

    [Fact]
    public async Task Another_household_cannot_see_or_act_on_an_application()
    {
        var sessionId = await IsolatedRetreat(capacity: 5);
        var (owner, _) = await NewCouple("Owner");
        var id = await Apply(owner, sessionId);
        var (other, _) = await NewCouple("Other");

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/admittance/applications/{id}")).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<JsonElement>("/api/admittance/applications")).EnumerateArray());
        var reauth = await other.PostAsJsonAsync($"/api/admittance/applications/{id}/reauthorize", new { cardToken = Card("4242424242424242"), idempotencyKey = "x" });
        Assert.Equal(HttpStatusCode.NotFound, reauth.StatusCode);
        var submit = await other.PostAsJsonAsync($"/api/admittance/applications/{id}/submit", new { cardToken = Card("4242424242424242"), idempotencyKey = "x" });
        Assert.Equal(HttpStatusCode.NotFound, submit.StatusCode);
        // Their own apply page starts empty rather than showing the owner's draft.
        var ctx = await other.GetFromJsonAsync<JsonElement>($"/api/admittance/sessions/{sessionId}/apply");
        Assert.Equal(JsonValueKind.Null, ctx.GetProperty("application").ValueKind);
    }

    [Fact]
    public async Task The_seeded_retreat_queue_reconciles_with_the_pool()
    {
        var staff = await factory.SignInAsStaff();
        var sessions = await staff.GetFromJsonAsync<JsonElement>("/api/admin/admittance/sessions");
        var retreat = sessions.EnumerateArray().First(s => s.GetProperty("session").GetProperty("program").GetProperty("slug").GetString() == "fall-marriage-retreat");
        var sessionId = retreat.GetProperty("session").GetProperty("id").GetInt32();

        var queue = await staff.GetFromJsonAsync<JsonElement>($"/api/admin/admittance/sessions/{sessionId}/applications");

        var counts = queue.GetProperty("counts");
        Assert.Equal(queue.GetProperty("reserved").GetInt32(), counts.GetProperty("approved").GetInt32());
        Assert.Equal(40, queue.GetProperty("capacity").GetInt32());
        Assert.True(counts.GetProperty("submitted").GetInt32() > 0);
        Assert.DoesNotContain(queue.GetProperty("rows").EnumerateArray(), r => r.GetProperty("stage").GetString() == "Draft");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    string Card(string number) => _gateway.Tokenize(number);

    /// <summary>A fresh retreat session with its own couples pool, so tests don't share seats.</summary>
    async Task<int> IsolatedRetreat(int capacity) => await factory.WithDb(async db =>
    {
        var template = await db.Sessions.AsNoTracking().FirstAsync(s => s.Program.Slug == "fall-marriage-retreat");
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
        db.CapacityPools.Add(new CapacityPool { Session = session, Name = "Couples", GradeMin = 99, GradeMax = 99, Capacity = capacity });
        await db.SaveChangesAsync();
        return session.Id;
    });

    [Fact]
    public async Task An_unpublished_program_takes_no_drafts_and_no_submits()
    {
        var sessionId = await IsolatedRetreat(capacity: 5);
        var programId = await MoveToOwnProgram(sessionId);
        var (family, _) = await NewCouple("Unpublished");
        var id = await SaveDraft(family, sessionId, GoodAnswers);
        var authorizations = _gateway.AuthorizeCount;

        // K2 "Return to draft" after the family saved a draft: submitting authorizes nothing.
        await factory.WithDb(db => db.Programs.Where(p => p.Id == programId).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPublished, false)));
        var submit = await family.PostAsJsonAsync($"/api/admittance/applications/{id}/submit", new { cardToken = Card("4242424242424242"), idempotencyKey = Guid.NewGuid().ToString() });
        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);
        Assert.Equal(authorizations, _gateway.AuthorizeCount);

        var (other, _) = await NewCouple("UnpublishedDraft");
        var draft = await other.PutAsJsonAsync($"/api/admittance/sessions/{sessionId}/draft", Draft(GoodAnswers));
        Assert.Equal(HttpStatusCode.NotFound, draft.StatusCode);
    }

    /// <summary>Gives an isolated session a program of its own, so a test can unpublish it alone.</summary>
    async Task<int> MoveToOwnProgram(int sessionId) => await factory.WithDb(async db =>
    {
        var session = await db.Sessions.Include(s => s.Program).SingleAsync(s => s.Id == sessionId);
        var copy = new CampProgram
        {
            MinistryId = session.Program.MinistryId,
            Slug = $"test-{Guid.NewGuid():N}"[..20],
            Name = "Test retreat",
            Type = session.Program.Type,
            HealthMechanism = session.Program.HealthMechanism,
            Location = session.Program.Location,
            IsPublished = true,
        };
        db.Programs.Add(copy);
        session.Program = copy;
        await db.SaveChangesAsync();
        return copy.Id;
    });

    async Task<(HttpClient Client, int HouseholdId)> NewCouple(string name)
    {
        var email = $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com";
        var client = await factory.SignInAsFamily(email, "Pat", name);
        var householdId = await factory.WithDb(db => db.Households.Where(h => h.Email == email).Select(h => h.Id).SingleAsync());
        return (client, householdId);
    }

    static object Draft(Dictionary<string, string> answers) =>
        new { spousePersonId = (int?)null, spouseFirstName = "Sam", spouseLastName = "Rivera", spouseEmail = (string?)null, answers, step = 3 };

    static async Task<int> SaveDraft(HttpClient family, int sessionId, Dictionary<string, string> answers)
    {
        var res = await family.PutAsJsonAsync($"/api/admittance/sessions/{sessionId}/draft", Draft(answers));
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    async Task<int> Apply(HttpClient family, int sessionId)
    {
        var id = await SaveDraft(family, sessionId, GoodAnswers);
        var res = await family.PostAsJsonAsync($"/api/admittance/applications/{id}/submit", new { cardToken = Card("4242424242424242"), idempotencyKey = Guid.NewGuid().ToString() });
        res.EnsureSuccessStatusCode();
        return id;
    }

    Task<int> Reserved(int sessionId) => factory.WithDb(db => db.CapacityPools.Where(p => p.SessionId == sessionId).SumAsync(p => p.Reserved));

    Task<ApplicationStage> Stage(int id) => factory.WithDb(db => db.Set<AdmittanceApplication>().Where(a => a.Id == id).Select(a => a.Stage).SingleAsync());
}
