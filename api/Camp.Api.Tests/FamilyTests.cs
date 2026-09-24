using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features;
using Camp.Api.Features.Family;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Camp.Api.Tests;

public class FamilyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    readonly FakeFiservGateway _gateway = (FakeFiservGateway)factory.Services.GetRequiredService<IPaymentGateway>();

    // ── Authorization ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/family/overview")]
    [InlineData("/api/family/access")]
    [InlineData("/api/family/registrations")]
    public async Task Family_pages_need_a_signed_in_family_not_staff(string url)
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);

        using var staff = await factory.SignInAsStaff("finance", "Marcus Lee");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Another_households_people_and_registrations_are_invisible()
    {
        var (maria, _) = await NewFamily("Owner");
        var kid = await AddChild(maria, "Pat", new DateOnly(2018, 2, 1), Gender.Female);
        var code = await RegisterOnPlan(await HouseholdIdOf(maria), [kid]);

        var (stranger, _) = await NewFamily("Stranger");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/family/members/{kid}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync($"/api/family/members/{kid}", Child("Hacked", new DateOnly(2018, 2, 1), Gender.Female))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/family/registrations/{code}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/family/registrations/{code}/payments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/family/registrations/{code}/pay", Pay("4242424242424242"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/family/members/{kid}/revoke-access", null)).StatusCode);

        var list = await stranger.GetFromJsonAsync<JsonElement>("/api/family/registrations");
        Assert.Equal(0, list.GetProperty("upcoming").GetArrayLength());
        Assert.Equal("Pat", (await factory.WithDb(db => db.People.SingleAsync(p => p.Id == kid))).FirstName);
    }

    // ── F2 member profile ──────────────────────────────────────────────────

    [Fact]
    public async Task A_new_family_adds_a_child_and_the_grade_follows_the_Sept_1_rule()
    {
        var (sam, _) = await NewFamily("Rivera");
        using var res = await sam.PostAsJsonAsync("/api/family/members", Child("Leo", new DateOnly(2017, 3, 4), Gender.Male) with { Allergies = "Bee stings" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var profile = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Grade 6", profile.GetProperty("gradeLabel").GetString());
        Assert.Equal(2028, profile.GetProperty("seasonYear").GetInt32());

        var overview = await sam.GetFromJsonAsync<JsonElement>("/api/family/overview");
        var members = overview.GetProperty("members").EnumerateArray().ToList();
        Assert.Equal(2, members.Count); // Sam (created at sign-in) and Leo
        Assert.Contains(members, m => m.GetProperty("firstName").GetString() == "Leo" && m.GetProperty("gradeLabel").GetString() == "Grade 6");

        // Adding the same child again would create a duplicate person; it's refused instead.
        using var again = await sam.PostAsJsonAsync("/api/family/members", Child("Leo", new DateOnly(2017, 3, 4), Gender.Male));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task A_child_needs_a_date_of_birth_and_gender_and_a_future_birthday_is_refused()
    {
        var (family, _) = await NewFamily("Checks");
        using var missing = await family.PostAsJsonAsync("/api/family/members", new MemberRequest("Kit", "Checks", null, null, false, null, null, null, null));
        var problem = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.True(problem.GetProperty("errors").TryGetProperty("dateOfBirth", out _));
        Assert.True(problem.GetProperty("errors").TryGetProperty("gender", out _));

        using var future = await family.PostAsJsonAsync("/api/family/members", Child("Kit", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3), Gender.Male));
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
    }

    [Fact]
    public async Task Date_of_birth_is_locked_once_a_child_is_registered_but_health_notes_still_save()
    {
        var (family, _) = await NewFamily("Locked");
        var kid = await AddChild(family, "Sky", new DateOnly(2018, 5, 5), Gender.Female);
        await RegisterOnPlan(await HouseholdIdOf(family), [kid]);

        using var moved = await family.PutAsJsonAsync($"/api/family/members/{kid}", Child("Sky", new DateOnly(2016, 5, 5), Gender.Female));
        Assert.Equal(HttpStatusCode.BadRequest, moved.StatusCode);

        using var notes = await family.PutAsJsonAsync($"/api/family/members/{kid}", Child("Sky", new DateOnly(2018, 5, 5), Gender.Female) with { Dietary = "Vegetarian" });
        notes.EnsureSuccessStatusCode();
        var person = await factory.WithDb(db => db.People.AsNoTracking().SingleAsync(p => p.Id == kid));
        Assert.Equal(new DateOnly(2018, 5, 5), person.DateOfBirth);
        Assert.Equal("Vegetarian", person.Dietary);
    }

    [Fact]
    public async Task An_adult_cant_take_the_primary_owners_email_and_the_primary_email_is_not_edited_here()
    {
        var (family, email) = await NewFamily("Emails");
        using var clash = await family.PostAsJsonAsync("/api/family/members", new MemberRequest("Other", "Emails", null, null, true, email.ToUpperInvariant(), null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, clash.StatusCode);

        var ownerId = await factory.WithDb(db => db.People.Where(p => p.Email == email).Select(p => p.Id).SingleAsync());
        using var change = await family.PutAsJsonAsync($"/api/family/members/{ownerId}", new MemberRequest("Parent", "Emails", null, null, true, "new@example.com", null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
        using var keep = await family.PutAsJsonAsync($"/api/family/members/{ownerId}", new MemberRequest("Parent", "Emails", null, null, true, email, "Vegetarian", null, null));
        keep.EnsureSuccessStatusCode();
        Assert.True((await family.GetFromJsonAsync<JsonElement>("/api/family/access")).GetProperty("canManage").GetBoolean());
    }

    // ── F3 household access ────────────────────────────────────────────────

    [Fact]
    public async Task The_primary_owner_invites_an_adult_and_revoking_a_co_owner_keeps_them_as_a_guardian()
    {
        var (owner, email) = await NewFamily("Access");
        var householdId = await HouseholdIdOf(owner);
        var coOwnerId = await factory.WithDb(async db =>
        {
            var co = new Person { HouseholdId = householdId, FirstName = "Dan", LastName = "Access", IsAdult = true, Role = "Co-owner", Email = $"dan-{email}" };
            db.People.Add(co);
            await db.SaveChangesAsync();
            return co.Id;
        });

        using var invite = await owner.PostAsJsonAsync("/api/family/invitations", new InviteRequest("Rosa", "Alvarez", "Rosa.Alvarez@example.com"));
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        using var twice = await owner.PostAsJsonAsync("/api/family/invitations", new InviteRequest("Rosa", "Alvarez", "rosa.alvarez@example.com"));
        Assert.Equal(HttpStatusCode.BadRequest, twice.StatusCode);
        using var existing = await owner.PostAsJsonAsync("/api/family/invitations", new InviteRequest("Dan", "Access", $"dan-{email}"));
        Assert.Equal(HttpStatusCode.BadRequest, existing.StatusCode);
        Assert.True(await factory.WithDb(db => db.OutboxEvents.AnyAsync(e => e.Type == "HouseholdInvitation" && e.AggregateId == $"household-{householdId}")));

        var access = await owner.GetFromJsonAsync<JsonElement>("/api/family/access");
        Assert.True(access.GetProperty("canManage").GetBoolean());
        Assert.Contains(access.GetProperty("invitations").EnumerateArray(), i => i.GetProperty("email").GetString() == "rosa.alvarez@example.com");

        using var revoke = await owner.PostAsync($"/api/family/members/{coOwnerId}/revoke-access", null);
        revoke.EnsureSuccessStatusCode();
        var after = await factory.WithDb(db => db.People.AsNoTracking().SingleAsync(p => p.Id == coOwnerId));
        Assert.Null(after.Role);
        Assert.Equal(householdId, after.HouseholdId); // still in the household, so guardian links stay
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(a => a.Action == "household.access_revoked" && a.EntityId == coOwnerId.ToString(CultureInfo.InvariantCulture))));

        var ownerId = await factory.WithDb(db => db.People.Where(p => p.Email == email).Select(p => p.Id).SingleAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync($"/api/family/members/{ownerId}/revoke-access", null)).StatusCode);
    }

    [Fact]
    public async Task A_co_owner_can_see_access_but_not_change_it()
    {
        var email = $"coowner-{Guid.NewGuid():N}@example.com";
        var otherId = await factory.WithDb(async db =>
        {
            var h = new Household { Name = "Shared", Email = email };
            h.Members.Add(new Person { FirstName = "Pri", LastName = "Shared", IsAdult = true, Role = "Primary", Email = $"pri-{email}" });
            h.Members.Add(new Person { FirstName = "Co", LastName = "Shared", IsAdult = true, Role = "Co-owner", Email = email });
            db.Households.Add(h);
            await db.SaveChangesAsync();
            return h.Members[0].Id;
        });

        using var co = await factory.SignInAsFamily(email, "Co", "Shared");
        var access = await co.GetFromJsonAsync<JsonElement>("/api/family/access");
        Assert.False(access.GetProperty("canManage").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await co.PostAsJsonAsync("/api/family/invitations", new InviteRequest("A", "B", "a.b@example.com"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await co.PostAsync($"/api/family/members/{otherId}/revoke-access", null)).StatusCode);
    }

    // ── F4 / F5 registrations ──────────────────────────────────────────────

    [Fact]
    public async Task Maria_sees_the_2026_retreat_under_past()
    {
        using var maria = await factory.SignInAsFamily();
        var list = await maria.GetFromJsonAsync<JsonElement>("/api/family/registrations");
        var past = Assert.Single(list.GetProperty("past").EnumerateArray());
        Assert.Equal("Spring Marriage Retreat", past.GetProperty("program").GetString());
        Assert.Equal("WSM Marriage", past.GetProperty("ministry").GetString());
        Assert.Equal(["Maria Johnson", "David Johnson"], past.GetProperty("participants").EnumerateArray().Select(p => p.GetProperty("name").GetString()).Order().Reverse());
        Assert.Equal(90000, past.GetProperty("totalCents").GetInt32());
        Assert.Equal(0, past.GetProperty("balanceCents").GetInt32());
        Assert.Equal("Paid", past.GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public async Task Registration_detail_and_home_checklist_link_to_what_resolves_each_item()
    {
        var (family, _) = await NewFamily("Checklist");
        var a = await AddChild(family, "Ada", new DateOnly(2017, 3, 4), Gender.Female);
        var b = await AddChild(family, "Bo", new DateOnly(2019, 8, 19), Gender.Male);
        var code = await RegisterOnPlan(await HouseholdIdOf(family), [a, b]);

        var overview = await family.GetFromJsonAsync<JsonElement>("/api/family/overview");
        var items = overview.GetProperty("checklist").EnumerateArray().ToList();
        var balance = Assert.Single(items, i => i.GetProperty("kind").GetString() == "balance");
        Assert.False(balance.GetProperty("done").GetBoolean());
        Assert.Equal($"/family/registrations/{code}/payments", balance.GetProperty("href").GetString());
        Assert.Equal("Ada and Bo", balance.GetProperty("participant").GetString());
        Assert.All(items.Where(i => i.GetProperty("kind").GetString() == "waiver"), i => Assert.True(i.GetProperty("done").GetBoolean()));

        var plan = Assert.Single(overview.GetProperty("registrations").EnumerateArray());
        Assert.Equal(65000, plan.GetProperty("totalCents").GetInt32());
        Assert.Equal(20000, plan.GetProperty("paidCents").GetInt32());
        Assert.Equal(45000, plan.GetProperty("balanceCents").GetInt32());
        Assert.Equal("Plan active", plan.GetProperty("paymentStatus").GetString());

        var detail = await family.GetFromJsonAsync<JsonElement>($"/api/family/registrations/{code}");
        Assert.Equal(2, detail.GetProperty("participants").GetArrayLength());
        Assert.Equal(45000, detail.GetProperty("payment").GetProperty("balanceCents").GetInt32());
    }

    [Fact]
    public async Task A_missing_waiver_shows_on_the_checklist_and_can_be_signed_from_the_detail_page()
    {
        var (family, _) = await NewFamily("Waiver");
        var kid = await AddChild(family, "Wren", new DateOnly(2018, 1, 9), Gender.Female);
        var code = await RegisterOnPlan(await HouseholdIdOf(family), [kid]);
        var photo = await factory.WithDb(async db =>
        {
            var acceptance = await db.WaiverAcceptances.Include(w => w.WaiverTemplate)
                .FirstAsync(w => w.WaiverTemplate.Title == "Photo and Media Release" && db.Registrations.Any(r => r.Id == w.RegistrationId && r.PersonId == kid));
            db.WaiverAcceptances.Remove(acceptance); // e.g. a new version was published
            await db.SaveChangesAsync();
            return acceptance.WaiverTemplateId;
        });

        var overview = await family.GetFromJsonAsync<JsonElement>("/api/family/overview");
        var waiver = Assert.Single(overview.GetProperty("checklist").EnumerateArray(), i => i.GetProperty("kind").GetString() == "waiver");
        Assert.False(waiver.GetProperty("done").GetBoolean());

        using var sign = await family.PostAsJsonAsync($"/api/family/registrations/{code}/waivers", new SignWaiverRequest(photo, kid, "Parent Waiver"));
        sign.EnsureSuccessStatusCode();
        using var again = await family.PostAsJsonAsync($"/api/family/registrations/{code}/waivers", new SignWaiverRequest(photo, kid, "Parent Waiver"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        overview = await family.GetFromJsonAsync<JsonElement>("/api/family/overview");
        Assert.True(overview.GetProperty("checklist").EnumerateArray().Single(i => i.GetProperty("kind").GetString() == "waiver").GetProperty("done").GetBoolean());
    }

    // ── F6 payments ────────────────────────────────────────────────────────

    [Fact]
    public async Task Paying_the_balance_charges_exactly_what_is_owed_and_settles_the_plan()
    {
        var (family, _) = await NewFamily("Payer");
        var a = await AddChild(family, "Avi", new DateOnly(2017, 3, 4), Gender.Male);
        var b = await AddChild(family, "Mae", new DateOnly(2019, 8, 19), Gender.Female);
        var code = await RegisterOnPlan(await HouseholdIdOf(family), [a, b]);

        using var res = await family.PostAsJsonAsync($"/api/family/registrations/{code}/pay", Pay("4242424242424242"));
        res.EnsureSuccessStatusCode();
        Assert.Equal(45000, (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("amountCents").GetInt32());

        var payments = await family.GetFromJsonAsync<JsonElement>($"/api/family/registrations/{code}/payments");
        Assert.Equal(0, payments.GetProperty("balanceCents").GetInt32());
        Assert.Equal(65000, payments.GetProperty("paidCents").GetInt32());
        Assert.Equal("Paid", payments.GetProperty("paymentStatus").GetString());
        Assert.Equal(["Deposit", "Balance payment"], payments.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("label").GetString()));
        Assert.All(payments.GetProperty("installments").EnumerateArray(), i => Assert.Equal("Paid", i.GetProperty("status").GetString()));

        await AssertPaidMatchesCharges(code);
        using var nothing = await family.PostAsJsonAsync($"/api/family/registrations/{code}/pay", Pay("4242424242424242"));
        Assert.Equal(HttpStatusCode.Conflict, nothing.StatusCode);
    }

    [Fact]
    public async Task Double_submitting_a_balance_payment_charges_once()
    {
        var (family, _) = await NewFamily("Double");
        var kid = await AddChild(family, "Dot", new DateOnly(2018, 4, 4), Gender.Female);
        var code = await RegisterOnPlan(await HouseholdIdOf(family), [kid]);
        var before = _gateway.ChargeCount;
        var request = Pay("4242424242424242");

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => family.PostAsJsonAsync($"/api/family/registrations/{code}/pay", request)));

        Assert.Contains(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(before + 1, _gateway.ChargeCount);
        await AssertPaidMatchesCharges(code);
        Assert.Equal(0, await Balance(code));
    }

    [Fact]
    public async Task Two_different_payment_attempts_at_once_never_pay_more_than_the_balance()
    {
        var (family, _) = await NewFamily("Racer");
        var kid = await AddChild(family, "Ray", new DateOnly(2018, 4, 4), Gender.Male);
        var code = await RegisterOnPlan(await HouseholdIdOf(family), [kid]);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => family.PostAsJsonAsync($"/api/family/registrations/{code}/pay", Pay("4242424242424242"))));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        await AssertPaidMatchesCharges(code);
        Assert.Equal(0, await Balance(code));
    }

    [Fact]
    public async Task A_declined_card_leaves_the_balance_and_a_new_card_can_pay_it()
    {
        var (family, _) = await NewFamily("Decline");
        var kid = await AddChild(family, "Dee", new DateOnly(2018, 4, 4), Gender.Female);
        var code = await RegisterOnPlan(await HouseholdIdOf(family), [kid]);
        var owed = await Balance(code);

        using var declined = await family.PostAsJsonAsync($"/api/family/registrations/{code}/pay", Pay("4000000000000002"));
        Assert.Equal(HttpStatusCode.PaymentRequired, declined.StatusCode);
        Assert.Contains("declined", (await declined.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(owed, await Balance(code));

        using var ok = await family.PostAsJsonAsync($"/api/family/registrations/{code}/pay", Pay("4242424242424242"));
        ok.EnsureSuccessStatusCode();
        Assert.Equal(0, await Balance(code));
        await AssertPaidMatchesCharges(code);
    }

    [Fact]
    public async Task Retrying_a_failed_installment_charges_only_that_installment()
    {
        var (family, _) = await NewFamily("Retry");
        var kid = await AddChild(family, "Rae", new DateOnly(2018, 4, 4), Gender.Female);
        var code = await RegisterOnPlan(await HouseholdIdOf(family), [kid]);
        await factory.WithDb(db => db.Installments.Where(i => i.OrderId == db.Orders.Single(o => o.ConfirmationCode == code).Id && i.Sequence == 1)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InstallmentStatus.Failed)));

        var payments = await family.GetFromJsonAsync<JsonElement>($"/api/family/registrations/{code}/payments");
        var failed = payments.GetProperty("failedInstallment");
        Assert.Equal("Installment failed", payments.GetProperty("paymentStatus").GetString());
        var due = DateOnly.Parse(failed.GetProperty("dueDate").GetString()!, CultureInfo.InvariantCulture);
        Assert.Equal(due.AddDays(7), DateOnly.Parse(failed.GetProperty("graceUntil").GetString()!, CultureInfo.InvariantCulture));

        using var scheduled = await family.PostAsJsonAsync($"/api/family/registrations/{code}/pay", Pay("4242424242424242") with { InstallmentSequence = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, scheduled.StatusCode);

        var owed = await Balance(code);
        using var retry = await family.PostAsJsonAsync($"/api/family/registrations/{code}/pay", Pay("4242424242424242") with { InstallmentSequence = 1 });
        retry.EnsureSuccessStatusCode();
        var charged = (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("amountCents").GetInt32();
        Assert.Equal(7500, charged); // ($325 − $100 deposit) / 3
        Assert.Equal(owed - charged, await Balance(code));

        var statuses = await factory.WithDb(db => db.Installments.Where(i => i.OrderId == db.Orders.Single(o => o.ConfirmationCode == code).Id).OrderBy(i => i.Sequence).Select(i => i.Status).ToListAsync());
        Assert.Equal([InstallmentStatus.Paid, InstallmentStatus.Scheduled, InstallmentStatus.Scheduled], statuses);
        await AssertPaidMatchesCharges(code);
    }

    [Fact]
    public async Task A_stale_pending_payment_is_recorded_once_when_requests_race_to_reconcile_it()
    {
        var (family, _) = await NewFamily("Stale");
        var kid = await AddChild(family, "Sol", new DateOnly(2018, 4, 4), Gender.Male);
        var code = await RegisterOnPlan(await HouseholdIdOf(family), [kid]);
        var owed = await Balance(code);

        // The process died after the processor charged the card but before the charge was recorded.
        var key = Guid.NewGuid().ToString();
        var charged = await _gateway.ChargeAsync(_gateway.Tokenize("4242424242424242"), owed, $"balance-{key}");
        Assert.True(charged.Succeeded);
        await factory.WithDb(async db =>
        {
            var orderId = await db.Orders.Where(o => o.ConfirmationCode == code).Select(o => o.Id).SingleAsync();
            db.Set<BalancePayment>().Add(new BalancePayment
            {
                OrderId = orderId,
                IdempotencyKey = key,
                AmountCents = owed,
                Kind = BalancePaymentKind.Balance,
                Status = BalancePaymentStatus.Pending,
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            });
            return await db.SaveChangesAsync();
        });

        // Every new attempt reconciles the stale payment first; they must not all record it.
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => family.PostAsJsonAsync($"/api/family/registrations/{code}/pay", Pay("4242424242424242"))));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode)); // nothing left to pay
        var recorded = await factory.WithDb(db => db.PaymentOperations.CountAsync(o => o.ProcessorRef == charged.ProcessorRef));
        Assert.Equal(1, recorded);
        Assert.Equal(0, await Balance(code));
        await AssertPaidMatchesCharges(code);
    }

    [Fact]
    public async Task A_payment_key_only_replays_for_the_order_it_paid()
    {
        var (family, _) = await NewFamily("Keys");
        var a = await AddChild(family, "Kai", new DateOnly(2018, 4, 4), Gender.Male);
        var b = await AddChild(family, "Kit", new DateOnly(2019, 8, 19), Gender.Female);
        var householdId = await HouseholdIdOf(family);
        var first = await RegisterOnPlan(householdId, [a]);
        var second = await RegisterOnPlan(householdId, [b]);
        var owedOnSecond = await Balance(second);
        var request = Pay("4242424242424242");

        using var paid = await family.PostAsJsonAsync($"/api/family/registrations/{first}/pay", request);
        paid.EnsureSuccessStatusCode();
        using var replayed = await family.PostAsJsonAsync($"/api/family/registrations/{first}/pay", request);
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);

        // The same key against another order must not report that order as paid.
        using var misused = await family.PostAsJsonAsync($"/api/family/registrations/{second}/pay", request);
        Assert.Equal(HttpStatusCode.BadRequest, misused.StatusCode);
        Assert.Equal(owedOnSecond, await Balance(second));
    }

    [Fact]
    public async Task Installments_settled_by_a_balance_payment_say_what_covered_them()
    {
        var (family, _) = await NewFamily("Covered");
        var kid = await AddChild(family, "Cy", new DateOnly(2018, 4, 4), Gender.Male);
        var code = await RegisterOnPlan(await HouseholdIdOf(family), [kid]);
        await factory.WithDb(db => db.Installments.Where(i => i.OrderId == db.Orders.Single(o => o.ConfirmationCode == code).Id && i.Sequence == 1)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InstallmentStatus.Failed)));
        (await family.PostAsJsonAsync($"/api/family/registrations/{code}/pay", Pay("4242424242424242") with { InstallmentSequence = 1 })).EnsureSuccessStatusCode();
        (await family.PostAsJsonAsync($"/api/family/registrations/{code}/pay", Pay("4242424242424242"))).EnsureSuccessStatusCode();

        var payments = await family.GetFromJsonAsync<JsonElement>($"/api/family/registrations/{code}/payments");
        var installments = payments.GetProperty("installments").EnumerateArray().ToList();
        Assert.All(installments, i => Assert.Equal("Paid", i.GetProperty("status").GetString()));
        // Installment 1 was charged on its own; 2 and 3 were covered by the balance payment.
        Assert.Equal(JsonValueKind.Null, installments[0].GetProperty("coveredOn").ValueKind);
        Assert.All(installments.Skip(1), i => Assert.Equal(DateTime.UtcNow.Date, i.GetProperty("coveredOn").GetDateTime().Date));
    }

    [Fact]
    public async Task A_camper_moved_by_a_transfer_shows_their_new_session_on_the_registration()
    {
        var (family, _) = await NewFamily("Moved");
        var first = await AddChild(family, "Mo", new DateOnly(2017, 4, 4), Gender.Male);
        var second = await AddChild(family, "Mae", new DateOnly(2019, 4, 4), Gender.Female);
        var code = await RegisterOnPlan(await HouseholdIdOf(family), [first, second]);
        // F8 approval moves one registration and leaves the order on its session until every camper has moved.
        var weekTwo = await factory.WithDb(async db =>
        {
            var order = await db.Orders.Include(o => o.Session).SingleAsync(o => o.ConfirmationCode == code);
            var to = await db.Sessions.Where(s => s.ProgramId == order.Session.ProgramId && s.Id != order.SessionId).FirstAsync();
            await db.Registrations.Where(r => r.OrderId == order.Id && r.PersonId == first).ExecuteUpdateAsync(u => u.SetProperty(r => r.SessionId, to.Id));
            return to.Name;
        });

        var detail = await family.GetFromJsonAsync<JsonElement>($"/api/family/registrations/{code}");
        var people = detail.GetProperty("participants").EnumerateArray().ToDictionary(p => p.GetProperty("personId").GetInt32());
        Assert.Equal(weekTwo, people[first].GetProperty("movedTo").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, people[second].GetProperty("movedTo").ValueKind);
    }

    [Fact]
    public async Task The_home_page_shows_a_held_waitlist_offer_and_no_dead_health_action()
    {
        var (family, _) = await NewFamily("Offer");
        var kid = await AddChild(family, "Ola", new DateOnly(2018, 4, 4), Gender.Female);
        var householdId = await HouseholdIdOf(family);
        var code = await RegisterOnPlan(householdId, [kid]);
        var expires = DateTime.UtcNow.AddHours(20);
        await factory.WithDb(async db =>
        {
            var pool = await db.CapacityPools.FirstAsync(p => p.Session.Program.Slug == "overnight-camp");
            db.WaitlistEntries.Add(new WaitlistEntry { PoolId = pool.Id, PersonId = kid, HouseholdId = householdId, Position = 1, Status = WaitlistStatus.Offered, OfferExpiresAt = expires, CreatedAt = DateTime.UtcNow });
            // An embedded health form can't be finished after registration, so it must not offer a button.
            await db.Registrations.Where(r => r.Order!.ConfirmationCode == code).ExecuteUpdateAsync(s => s.SetProperty(r => r.HealthStatus, FormStatus.Incomplete));
            return await db.SaveChangesAsync();
        });

        var overview = await family.GetFromJsonAsync<JsonElement>("/api/family/overview");
        var offer = Assert.Single(overview.GetProperty("waitlist").EnumerateArray());
        Assert.Equal("Offered", offer.GetProperty("status").GetString());
        Assert.Equal(expires, offer.GetProperty("offerExpiresAt").GetDateTime(), TimeSpan.FromSeconds(1));

        var health = Assert.Single(overview.GetProperty("checklist").EnumerateArray(), i => i.GetProperty("kind").GetString() == "health");
        Assert.False(health.GetProperty("done").GetBoolean());
        Assert.Equal("", health.GetProperty("action").GetString());
        Assert.Equal("", health.GetProperty("href").GetString());
    }

    // ── helpers ────────────────────────────────────────────────────────────

    static MemberRequest Child(string first, DateOnly dob, Gender gender) => new(first, "Test", dob, gender, false, null, null, null, null);

    PayRequest Pay(string card) => new(Guid.NewGuid().ToString(), _gateway.Tokenize(card), null);

    async Task<(HttpClient Client, string Email)> NewFamily(string name)
    {
        var email = $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com";
        return (await factory.SignInAsFamily(email, "Parent", name), email);
    }

    static async Task<int> AddChild(HttpClient family, string first, DateOnly dob, Gender gender)
    {
        using var res = await family.PostAsJsonAsync("/api/family/members", Child(first, dob, gender));
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    static async Task<int> HouseholdIdOf(HttpClient family) =>
        (await family.GetFromJsonAsync<JsonElement>("/api/me")).GetProperty("id").GetInt32();

    /// <summary>Checks out through the real service on a private copy of Day Camp, on the payment plan.</summary>
    async Task<string> RegisterOnPlan(int householdId, int[] kids)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampDbContext>();
        var template = await db.Sessions.AsNoTracking().OrderBy(s => s.Id).FirstAsync(s => s.Program.Slug == "day-camp-atlanta");
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
        db.CapacityPools.Add(new CapacityPool { Session = session, Name = "Grades 1–6", GradeMin = 1, GradeMax = 6, Capacity = 50 });
        await db.SaveChangesAsync();
        var waivers = await db.WaiverTemplates.Where(w => w.ProgramId == template.ProgramId).ToListAsync();
        var req = new CheckoutRequest(
            Guid.NewGuid().ToString(), session.Id,
            [.. kids.Select(k => new CheckoutParticipant(k, new() { ["tshirt"] = "Youth M", ["swim"] = "Beginner" }, new HealthForm(null, null, null, null, "Dr. Test", "555-0100", null)))],
            new() { ["church"] = "No" },
            [.. waivers.SelectMany(w => w.PerParticipant ? kids.Select(k => new WaiverSignature(w.Id, k, "Parent")) : Enumerable.Repeat(new WaiverSignature(w.Id, null, "Parent"), 1))],
            PaymentOption.Plan, null, _gateway.Tokenize("4242424242424242"));
        var result = await scope.ServiceProvider.GetRequiredService<CheckoutService>().CheckoutAsync(householdId, "test", req, default);
        Assert.Equal(OrderStatus.Paid, result.Status);
        return result.ConfirmationCode;
    }

    Task<int> Balance(string code) => factory.WithDb(async db =>
        (await db.Registrations.Where(r => r.Order!.ConfirmationCode == code && r.Status != RegistrationStatus.Cancelled).ToListAsync()).Sum(r => r.BalanceCents));

    /// <summary>Money invariant: what the registrations say was paid equals what the processor charged.</summary>
    async Task AssertPaidMatchesCharges(string code)
    {
        var (paid, charged, total) = await factory.WithDb(async db =>
        {
            var order = await db.Orders.Include(o => o.Registrations).Include(o => o.Operations).SingleAsync(o => o.ConfirmationCode == code);
            return (order.Registrations.Sum(r => r.PaidCents),
                order.Operations.Where(o => o.Kind == PaymentKind.Charge && o.Succeeded).Sum(o => o.AmountCents),
                order.Registrations.Sum(r => r.PriceCents - r.DiscountCents));
        });
        Assert.Equal(charged, paid);
        Assert.True(paid <= total, $"Paid {paid} exceeds total {total}.");
    }
}
