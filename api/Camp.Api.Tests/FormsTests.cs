using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features;
using Camp.Api.Features.Forms;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Camp.Api.Tests;

/// <summary>Slice 9: the registration form builder (K6), its versions and approval, and answers at checkout, C3 and F5.</summary>
public class FormsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    const string Forms = "/api/admin/forms";
    readonly FakeFiservGateway _gateway = (FakeFiservGateway)factory.Services.GetRequiredService<IPaymentGateway>();

    // ── access ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_builder_is_admin_only()
    {
        var rome = await ProgramId(FormsSeed.RomeSlug);
        foreach (var url in new[] { Forms, $"{Forms}/programs/{rome}" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsFamily()).GetAsync(url)).StatusCode);
            foreach (var (role, name) in new[] { ("host", "Pastor Dave"), ("cet", "Diane Carter"), ("finance", "Marcus Lee") })
                Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsStaff(role, name)).GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await (await Alex()).GetAsync(url)).StatusCode);
        }
        var cet = await factory.SignInAsStaff("cet", "Diane Carter");
        Assert.Equal(HttpStatusCode.Forbidden, (await cet.PostAsync($"{Forms}/programs/{rome}/drafts", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsync($"{Forms}/versions/1/approve", null)).StatusCode);
    }

    [Fact]
    public async Task Seeded_forms_are_live_for_Day_Camp_and_Overnight_with_the_medication_follow_up()
    {
        var list = await Json(await (await Alex()).GetAsync(Forms));
        foreach (var name in new[] { "Day Camp · Atlanta", "Overnight Camp", "Day Camp · Rome" })
            Assert.Equal(JsonValueKind.Object, list.EnumerateArray().Single(p => p.GetProperty("program").GetString() == name).GetProperty("live").ValueKind);

        var atlanta = await factory.WithDb(db => FormRules.LiveAsync(db, db.Programs.Single(p => p.Slug == "day-camp-atlanta").Id));
        var details = atlanta!.Questions.Single(q => q.Key == "medicationDetails");
        Assert.Equal(("medication", "Yes", true), (details.ShowWhenKey, details.ShowWhenValue, details.Required));

        // The wizard asks the live version.
        var family = await factory.SignInAsFamily();
        var session = await factory.WithDb(db => db.Sessions.Where(s => s.Program.Slug == "day-camp-atlanta").Select(s => s.Id).FirstAsync());
        var ctx = await Json(await family.GetAsync($"/api/sessions/{session}/register-context"));
        Assert.Equal(atlanta.Id, ctx.GetProperty("form").GetProperty("id").GetInt32());
        Assert.Contains(ctx.GetProperty("questions").EnumerateArray(), q => q.GetProperty("key").GetString() == "medicationDetails" && q.GetProperty("type").GetString() == "LongText");
    }

    // ── versions and approval ────────────────────────────────────────────────

    [Fact]
    public async Task A_second_admin_approves_Jamies_v2_which_retires_v1_and_is_audited()
    {
        var alex = await Alex();
        var rome = await ProgramId(FormsSeed.RomeSlug);
        var v2 = (await Versions(alex, rome)).Single(v => v.GetProperty("version").GetInt32() == 2);
        if (v2.GetProperty("status").GetString() == "PendingApproval")
        {
            Assert.True(v2.GetProperty("canApprove").GetBoolean());
            Assert.Equal(HttpStatusCode.OK, (await alex.PostAsync($"{Forms}/versions/{v2.GetProperty("id").GetInt32()}/approve", null)).StatusCode);
        }

        var versions = await factory.WithDb(db => db.Set<FormVersion>().Where(v => v.ProgramId == rome).OrderBy(v => v.Version).ToListAsync());
        Assert.Equal(FormVersionStatus.Retired, versions[0].Status);
        Assert.NotNull(versions[0].RetiredAt);
        Assert.Equal(FormVersionStatus.Published, versions[1].Status);
        Assert.Equal("Alex Morgan (ADMIN)", versions[1].ApprovedBy);
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(e => e.Action == "form.published" && e.EntityId == $"{rome}" && e.Actor == "Alex Morgan (ADMIN)")));

        // Approving twice is refused; published versions are never edited in place.
        Assert.Equal(HttpStatusCode.Conflict, (await alex.PostAsync($"{Forms}/versions/{versions[1].Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await alex.PutAsJsonAsync($"{Forms}/versions/{versions[1].Id}", new FormDraftInput("x", []))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await alex.DeleteAsync($"{Forms}/versions/{versions[0].Id}")).StatusCode);
    }

    [Fact]
    public async Task A_draft_is_validated_and_its_author_cannot_approve_it()
    {
        var alex = await Alex();
        var overnight = await ProgramId("overnight-camp");
        var start = await alex.PostAsync($"{Forms}/programs/{overnight}/drafts", null);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var id = (await Json(start)).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await alex.PostAsync($"{Forms}/programs/{overnight}/drafts", null)).StatusCode);

        // The draft starts as a copy of live v1.
        var draft = (await Versions(alex, overnight)).Single(v => v.GetProperty("id").GetInt32() == id);
        Assert.True(draft.GetProperty("editable").GetBoolean());
        var questions = draft.GetProperty("questions").EnumerateArray().Select(Input).ToList();
        Assert.Contains(questions, q => q.Key == "medicationDetails");

        // Sending an unchanged copy, or one without a change note, is refused.
        var unchanged = await alex.PostAsync($"{Forms}/versions/{id}/submit", null);
        Assert.Equal(HttpStatusCode.BadRequest, unchanged.StatusCode);
        var errors = (await Json(unchanged)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("changeNote", out _));
        Assert.Contains("matches live v1", errors.GetProperty("questions")[0].GetString());

        // Invalid questions: a duplicate key, a choice with one option, and a condition on a later question.
        var bad = await alex.PutAsJsonAsync($"{Forms}/versions/{id}", new FormDraftInput("Bad", [
            new("pickup", "Who picks up?", null, FormQuestionType.ShortText, QuestionScope.Participant, true, null, "bus", "Yes"),
            new("pickup", "Pickup again", null, FormQuestionType.ShortText, QuestionScope.Participant, false, null, null, null),
            new("bus", "Bus?", null, FormQuestionType.SingleChoice, QuestionScope.Participant, false, ["Yes"], null, null),
        ]));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var badErrors = (await Json(bad)).GetProperty("errors");
        // Field paths: question 0's condition, question 1's duplicate id, question 2's choices.
        foreach (var (index, field) in new[] { (0, "showWhenKey"), (1, "key"), (2, "options") })
            Assert.True(badErrors.TryGetProperty($"questions.{index}.{field}", out _), $"{index}.{field}");

        // A household question can't depend on a camper's answer.
        var scoped = await alex.PutAsJsonAsync($"{Forms}/versions/{id}", new FormDraftInput("Bad", [
            new("bus", "Riding the bus?", null, FormQuestionType.YesNo, QuestionScope.Participant, false, null, null, null),
            new("stop", "Bus stop", null, FormQuestionType.ShortText, QuestionScope.Household, false, null, "bus", "Yes"),
        ]));
        Assert.True((await Json(scoped)).GetProperty("errors").TryGetProperty("questions.1.showWhenKey", out _));

        questions.Add(new("pickupPerson", "Who will pick your camper up on Saturday?", null, FormQuestionType.ShortText, QuestionScope.Household, true, null, null, null));
        Assert.Equal(HttpStatusCode.OK, (await alex.PutAsJsonAsync($"{Forms}/versions/{id}", new FormDraftInput("Adds Saturday pickup.", questions))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alex.PostAsync($"{Forms}/versions/{id}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await alex.PutAsJsonAsync($"{Forms}/versions/{id}", new FormDraftInput("x", questions))).StatusCode);

        // Whoever sent it can't approve it.
        var pending = (await Versions(alex, overnight)).Single(v => v.GetProperty("id").GetInt32() == id);
        Assert.False(pending.GetProperty("canApprove").GetBoolean());
        Assert.Contains("different admin", pending.GetProperty("approvalBlock").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await alex.PostAsync($"{Forms}/versions/{id}/approve", null)).StatusCode);

        // Returning needs a note; then the draft can be discarded. Live v1 stays live throughout.
        Assert.Equal(HttpStatusCode.BadRequest, (await alex.PostAsJsonAsync($"{Forms}/versions/{id}/return", new FormReturnInput(" "))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alex.PostAsJsonAsync($"{Forms}/versions/{id}/return", new FormReturnInput("Ask this per camper."))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alex.DeleteAsync($"{Forms}/versions/{id}")).StatusCode);
        Assert.False(await factory.WithDb(db => db.Set<FormVersion>().AnyAsync(v => v.Id == id)));
        Assert.Equal(1, (await factory.WithDb(db => FormRules.LiveAsync(db, overnight)))!.Version);
        foreach (var action in new[] { "form.draft_started", "form.draft_saved", "form.submitted", "form.returned", "form.draft_discarded" })
            Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(e => e.Action == action && e.EntityId == $"{overnight}")), action);
    }

    // ── answers at checkout ──────────────────────────────────────────────────

    [Fact]
    public async Task Checkout_enforces_required_and_conditional_answers_and_keeps_the_version()
    {
        var (family, kid, household) = await NewFamily();
        var session = await RomeSession();
        var live = (await factory.WithDb(db => FormRules.LiveAsync(db, db.Programs.Single(p => p.Slug == FormsSeed.RomeSlug).Id)))!;
        var camper = new Dictionary<string, string> { ["tshirt"] = "Youth L", ["swim"] = "Beginner", ["medication"] = "Yes" };
        var home = new Dictionary<string, string> { ["church"] = "Yes" };

        // "Yes" reveals a required follow-up, for the camper and for the household.
        var refused = await Assert.ThrowsAsync<CheckoutValidationException>(() => Checkout(household, kid, session, camper, home, live.Id));
        Assert.Contains("Kid: Medication name and schedule is required.", refused.Errors[$"participants.{kid}.answers"]);
        Assert.Contains("Church name is required.", refused.Errors["householdAnswers"]);

        // A form version the wizard showed that is no longer live is refused.
        var other = await factory.WithDb(db => db.Set<FormVersion>().Where(v => v.ProgramId == live.ProgramId && v.Id != live.Id).Select(v => v.Id).FirstAsync());
        camper["medicationDetails"] = "Inhaler before swimming";
        home["churchName"] = "North Rome Church";
        var stale = await Assert.ThrowsAsync<CheckoutValidationException>(() => Checkout(household, kid, session, camper, home, other));
        Assert.True(stale.Errors.ContainsKey("formVersionId"));

        // Hidden answers are dropped: "No" hides the follow-up, so its answer isn't kept.
        camper["medication"] = "no";
        camper["unknown"] = "ignored";
        var code = await Checkout(household, kid, session, camper, home, live.Id);
        var orderId = await factory.WithDb(db => db.Orders.Where(o => o.ConfirmationCode == code).Select(o => o.Id).SingleAsync());
        var stored = await factory.WithDb(db => db.Set<FormAnswer>().Include(a => a.Question).Where(a => a.OrderId == orderId).ToListAsync());
        Assert.All(stored, a => Assert.Equal(live.Id, a.FormVersionId));
        Assert.DoesNotContain(stored, a => a.Question.Key is "medicationDetails" or "unknown");
        Assert.Equal("No", stored.Single(a => a.Question.Key == "medication").Value);
        Assert.Equal("North Rome Church", stored.Single(a => a.Question.Key == "churchName" && a.RegistrationId == null).Value);
        var json = await factory.WithDb(db => db.Registrations.Where(r => r.Order!.ConfirmationCode == code).Select(r => r.AnswersJson).SingleAsync());
        Assert.DoesNotContain("medicationDetails", json);

        // The family sees their answers on F5, with the version they answered; other households can't.
        var f5 = await Json(await family.GetAsync($"/api/family/forms/orders/{code}/answers"));
        Assert.Equal(live.Version, f5.GetProperty("formVersion").GetInt32());
        Assert.Contains(f5.GetProperty("household").EnumerateArray(), a => a.GetProperty("key").GetString() == "church");
        var kidAnswers = f5.GetProperty("participants")[0].GetProperty("answers").EnumerateArray().ToList();
        Assert.Equal("Youth L", kidAnswers.Single(a => a.GetProperty("key").GetString() == "tshirt").GetProperty("value").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await (await factory.SignInAsFamily()).GetAsync($"/api/family/forms/orders/{code}/answers")).StatusCode);

        // Staff see them on the registration.
        var regId = await factory.WithDb(db => db.Registrations.Where(r => r.Order!.ConfirmationCode == code).Select(r => r.Id).SingleAsync());
        var url = $"{Forms}/registrations/{regId}/answers";
        var c3 = await Json(await (await factory.SignInAsStaff("cet", "Diane Carter")).GetAsync(url));
        Assert.Equal(live.Version, c3.GetProperty("formVersion").GetInt32());
        Assert.Contains(c3.GetProperty("participant").EnumerateArray(), a => a.GetProperty("label").GetString() == "Does your camper need medication at camp?");
        Assert.Equal(HttpStatusCode.Forbidden, (await family.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsStaff("host", "Pastor Dave")).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Registrations_from_before_forms_show_their_stored_answers()
    {
        var reg = await factory.WithDb(db => db.Registrations.Where(r => r.AnswersJson.Contains("tshirt") && !db.Set<FormAnswer>().Any(a => a.RegistrationId == r.Id))
            .Select(r => new { r.Id, r.AnswersJson }).FirstAsync());
        var view = await Json(await (await factory.SignInAsStaff("cet", "Diane Carter")).GetAsync($"{Forms}/registrations/{reg.Id}/answers"));
        Assert.Equal(JsonValueKind.Null, view.GetProperty("formVersion").ValueKind);
        Assert.Contains(view.GetProperty("participant").EnumerateArray(), a => a.GetProperty("label").GetString() == "T-shirt size");
    }

    // ── answer rules ─────────────────────────────────────────────────────────

    [Fact]
    public void Answers_are_normalized_by_type()
    {
        FormQuestion Q(string key, FormQuestionType type, string? options = null, bool required = false) =>
            new() { Key = key, Label = key, Type = type, Options = options, Required = required, Scope = QuestionScope.Participant };
        var questions = new[]
        {
            Q("pick", FormQuestionType.MultipleChoice, "Crafts|Sports|Music"),
            Q("count", FormQuestionType.Number),
            Q("day", FormQuestionType.Date),
            Q("size", FormQuestionType.SingleChoice, "S|M", required: true),
        };
        var (clean, errors) = FormRules.Check(questions, new Dictionary<string, string> { ["pick"] = "Music|crafts", ["count"] = " 2 ", ["day"] = "2028-07-17", ["size"] = "m" });
        Assert.Empty(errors);
        Assert.Equal("Crafts|Music", clean.Single(a => a.Question.Key == "pick").Value);
        Assert.Equal("2", clean.Single(a => a.Question.Key == "count").Value);
        Assert.Equal("M", clean.Single(a => a.Question.Key == "size").Value);

        var (_, bad) = FormRules.Check(questions, new Dictionary<string, string> { ["pick"] = "Juggling", ["count"] = "two", ["day"] = "07/17/2028" });
        Assert.Equal(4, bad.Count);
        Assert.Contains("size is required.", bad);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    Task<HttpClient> Alex() => factory.SignInAsStaff("admin", "Alex Morgan");

    Task<int> ProgramId(string slug) => factory.WithDb(db => db.Programs.Where(p => p.Slug == slug).Select(p => p.Id).SingleAsync());

    Task<int> RomeSession() => factory.WithDb(db => db.Sessions.Where(s => s.Program.Slug == FormsSeed.RomeSlug).Select(s => s.Id).SingleAsync());

    static async Task<List<JsonElement>> Versions(HttpClient admin, int programId) =>
        [.. (await Json(await admin.GetAsync($"{Forms}/programs/{programId}"))).GetProperty("versions").EnumerateArray()];

    static FormQuestionInput Input(JsonElement q) => new(
        q.GetProperty("key").GetString(), q.GetProperty("label").GetString(), q.GetProperty("helpText").GetString(),
        Enum.Parse<FormQuestionType>(q.GetProperty("type").GetString()!), Enum.Parse<QuestionScope>(q.GetProperty("scope").GetString()!),
        q.GetProperty("required").GetBoolean(), [.. q.GetProperty("options").EnumerateArray().Select(o => o.GetString()!)],
        q.GetProperty("showWhenKey").GetString(), q.GetProperty("showWhenValue").GetString());

    async Task<(HttpClient Client, int KidId, int HouseholdId)> NewFamily()
    {
        var email = $"forms-{Guid.NewGuid():N}@example.com";
        var client = await factory.SignInAsFamily(email, "Pat", "Forms");
        var (kid, household) = await factory.WithDb(async db =>
        {
            var h = await db.Households.SingleAsync(x => x.Email == email);
            var p = new Person { HouseholdId = h.Id, FirstName = "Kid", LastName = "Forms", DateOfBirth = new(2018, 5, 1), Gender = Gender.Female };
            db.People.Add(p);
            await db.SaveChangesAsync();
            return (p.Id, h.Id);
        });
        return (client, kid, household);
    }

    async Task<string> Checkout(int householdId, int kidId, int sessionId, Dictionary<string, string> camper, Dictionary<string, string> home, int formVersionId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampDbContext>();
        var programId = await db.Sessions.Where(s => s.Id == sessionId).Select(s => s.ProgramId).SingleAsync();
        var waivers = await db.WaiverTemplates.Where(w => w.ProgramId == programId).ToListAsync();
        var req = new CheckoutRequest(
            $"forms-{Guid.NewGuid():N}", sessionId,
            [new CheckoutParticipant(kidId, new(camper), new HealthForm(null, null, null, null, "Dr. Test", "555-0100", null))],
            new(home),
            [.. waivers.Select(w => new WaiverSignature(w.Id, w.PerParticipant ? kidId : null, "Pat Forms"))],
            PaymentOption.Full, null, _gateway.Tokenize("4242424242424242"), formVersionId);
        var result = await scope.ServiceProvider.GetRequiredService<CheckoutService>().CheckoutAsync(householdId, "test", req, default);
        Assert.Equal(OrderStatus.Paid, result.Status);
        return result.ConfirmationCode;
    }

    static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        return JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text).RootElement;
    }
}
