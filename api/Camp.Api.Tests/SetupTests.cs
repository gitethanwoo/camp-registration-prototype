using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features;
using Camp.Api.Features.Polish;
using Camp.Api.Features.Setup;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Camp.Api.Tests;

/// <summary>Slice 5: program setup (K2), sessions and pools (K3), pricing and policies (K4), discount rules (K5), waivers (K7) and the audit log (K12).</summary>
public class SetupTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    const string Setup = "/api/admin/setup";
    readonly FakeFiservGateway _gateway = (FakeFiservGateway)factory.Services.GetRequiredService<IPaymentGateway>();

    // ── access ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Setup + "/programs")]
    [InlineData(Setup + "/discount-rules")]
    [InlineData(Setup + "/waivers")]
    public async Task Setup_is_admin_only(string url)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsFamily()).GetAsync(url)).StatusCode);
        foreach (var (role, name) in new[] { ("host", "Pastor Dave"), ("cet", "Diane Carter"), ("finance", "Marcus Lee") })
            Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsStaff(role, name)).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await Alex()).GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Setup_mutations_are_admin_only()
    {
        var cet = await factory.SignInAsStaff("cet", "Diane Carter");
        var session = await FamilyWeekendSummer2028();
        Assert.Equal(HttpStatusCode.Forbidden, (await cet.PutAsJsonAsync($"{Setup}/sessions/{session}/pricing", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cet.PostAsync($"{Setup}/programs/{await FamilyWeekendId()}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync($"{Setup}/discount-rules", new { })).StatusCode);
    }

    [Fact]
    public async Task Audit_log_is_readable_by_any_staff_but_not_hosts_or_families()
    {
        const string url = "/api/admin/audit-log";
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsFamily()).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsStaff("host", "Pastor Dave")).GetAsync(url)).StatusCode);
        foreach (var (role, name) in new[] { ("cet", "Diane Carter"), ("finance", "Marcus Lee"), ("admin", "Alex Morgan") })
            Assert.Equal(HttpStatusCode.OK, (await (await factory.SignInAsStaff(role, name)).GetAsync(url)).StatusCode);
    }

    // ── K2 · programs ────────────────────────────────────────────────────────

    [Fact]
    public async Task Family_Camp_publishes_after_the_second_approval_and_is_audited()
    {
        var alex = await Alex();
        var id = await FamilyWeekendId();
        var row = (await Json(await alex.GetAsync($"{Setup}/programs"))).GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("id").GetInt32() == id);
        Assert.Equal("PendingApproval", row.GetProperty("state").GetString());
        Assert.True(row.GetProperty("canApprove").GetBoolean());

        // Guests don't see it until it's published.
        Assert.DoesNotContain("family-weekend", await factory.CreateClient().GetStringAsync("/api/programs"));

        Assert.Equal(HttpStatusCode.OK, (await alex.PostAsync($"{Setup}/programs/{id}/approve", null)).StatusCode);
        Assert.True(await factory.WithDb(db => db.Programs.Where(p => p.Id == id).Select(p => p.IsPublished).SingleAsync()));
        Assert.Equal(PublishState.Published, await factory.WithDb(db => db.Set<ProgramSetup>().Where(s => s.ProgramId == id).Select(s => s.State).SingleAsync()));
        Assert.Contains("family-weekend", await factory.CreateClient().GetStringAsync("/api/programs"));
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(e => e.Action == "program.published" && e.EntityId == $"{id}" && e.Actor == "Alex Morgan (ADMIN)")));

        // A second approval of the same program is refused, and a published program can't be edited in place.
        Assert.Equal(HttpStatusCode.Conflict, (await alex.PostAsync($"{Setup}/programs/{id}/approve", null)).StatusCode);
        var edit = await alex.PutAsJsonAsync($"{Setup}/programs/{id}", NewProgram("Family Weekend 2"));
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
    }

    [Fact]
    public async Task The_submitter_cannot_approve_their_own_program_and_a_return_needs_a_note()
    {
        var alex = await Alex();
        var created = await alex.PostAsJsonAsync($"{Setup}/programs", NewProgram("Test Retreat " + Guid.NewGuid().ToString("N")[..6]));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await Json(created)).GetProperty("id").GetInt32();

        // Nothing to register for yet.
        Assert.Equal(HttpStatusCode.BadRequest, (await alex.PostAsync($"{Setup}/programs/{id}/submit", null)).StatusCode);
        var session = await alex.PostAsJsonAsync($"{Setup}/programs/{id}/sessions",
            new NewSessionInput("Fall 2028", new(2028, 10, 6), new(2028, 10, 8), 20000, 5000, "Everyone", null, 0, 12, 30));
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        // No waiver yet: the publish guard refuses it (polish).
        Assert.Equal(HttpStatusCode.BadRequest, (await alex.PostAsync($"{Setup}/programs/{id}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alex.PostAsync($"{Setup}/programs/{id}/waivers", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alex.PostAsync($"{Setup}/programs/{id}/submit", null)).StatusCode);

        var self = await alex.PostAsync($"{Setup}/programs/{id}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
        Assert.Contains("someone else", (await Json(self)).GetProperty("error").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await alex.PostAsJsonAsync($"{Setup}/programs/{id}/return", new ReturnToDraftRequest(""))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alex.PostAsJsonAsync($"{Setup}/programs/{id}/return", new ReturnToDraftRequest("Add a tagline."))).StatusCode);
        Assert.Equal(PublishState.Draft, await factory.WithDb(db => db.Set<ProgramSetup>().Where(s => s.ProgramId == id).Select(s => s.State).SingleAsync()));
    }

    [Fact]
    public async Task A_program_takes_no_registrations_until_it_is_published()
    {
        var alex = await Alex();
        var created = await alex.PostAsJsonAsync($"{Setup}/programs", NewProgram("Secret Draft Camp " + Guid.NewGuid().ToString("N")[..6]));
        var programId = (await Json(created)).GetProperty("id").GetInt32();
        var session = await alex.PostAsJsonAsync($"{Setup}/programs/{programId}/sessions",
            new NewSessionInput("Fall 2028", new(2028, 10, 6), new(2028, 10, 8), 20000, 5000, "Everyone", null, 0, 12, 30));
        var sessionId = (await Json(session)).GetProperty("id").GetInt32();

        // A family holding the (sequential) session id gets nothing from a Draft program.
        var (family, kid, household) = await NewFamily();
        Assert.Equal(HttpStatusCode.NotFound, (await family.GetAsync($"/api/sessions/{sessionId}/register-context")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await family.PostAsJsonAsync($"/api/sessions/{sessionId}/quote", new QuoteRequest([kid], PaymentOption.Full, null))).StatusCode);
        var refused = await Assert.ThrowsAsync<CheckoutValidationException>(() => Checkout(household, kid, sessionId, null));
        Assert.Contains("isn't open for registration", refused.Errors["sessionId"].Single());
        Assert.False(await factory.WithDb(db => db.Orders.AnyAsync(o => o.SessionId == sessionId)));

        // Pending approval is still not published.
        Assert.Equal(HttpStatusCode.OK, (await alex.PostAsync($"{Setup}/programs/{programId}/waivers", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alex.PostAsync($"{Setup}/programs/{programId}/submit", null)).StatusCode);
        await Assert.ThrowsAsync<CheckoutValidationException>(() => Checkout(household, kid, sessionId, null));

        // Once the approval chain publishes it (dev sign-in makes every admin one person, so set the flag the last approval sets).
        await factory.WithDb(db => db.Programs.Where(p => p.Id == programId).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPublished, true)));
        Assert.Equal(HttpStatusCode.OK, (await family.GetAsync($"/api/sessions/{sessionId}/register-context")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await family.PostAsJsonAsync($"/api/sessions/{sessionId}/quote", new QuoteRequest([kid], PaymentOption.Full, null))).StatusCode);
        await Checkout(household, kid, sessionId, null);
    }

    // ── K3 · sessions and pools ──────────────────────────────────────────────

    [Fact]
    public async Task Capacity_cannot_drop_below_seats_taken_and_a_pool_with_campers_cannot_be_removed()
    {
        var alex = await Alex();
        var pool = await factory.WithDb(db => db.CapacityPools.Where(p => p.Session.Program.Slug == SetupSeed.FamilyWeekendSlug && p.Session.Name == "Summer 2026").SingleAsync());
        Assert.Equal(14, pool.Reserved);

        var tooLow = await alex.PutAsJsonAsync($"{Setup}/pools/{pool.Id}", new PoolInput(pool.Name, null, pool.GradeMin, pool.GradeMax, 10));
        Assert.Equal(HttpStatusCode.BadRequest, tooLow.StatusCode);
        Assert.Contains("14 seats taken", await tooLow.Content.ReadAsStringAsync());
        Assert.Equal(80, await factory.WithDb(db => db.CapacityPools.Where(p => p.Id == pool.Id).Select(p => p.Capacity).SingleAsync()));

        Assert.Equal(HttpStatusCode.OK, (await alex.PutAsJsonAsync($"{Setup}/pools/{pool.Id}", new PoolInput(pool.Name, null, pool.GradeMin, pool.GradeMax, 14))).StatusCode);
        Assert.Equal(14, await factory.WithDb(db => db.CapacityPools.Where(p => p.Id == pool.Id).Select(p => p.Capacity).SingleAsync()));
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(e => e.Action == "capacity.changed" && e.EntityId == $"{pool.Id}")));

        // Who a pool is for is fixed once campers were placed by it.
        Assert.Equal(HttpStatusCode.BadRequest, (await alex.PutAsJsonAsync($"{Setup}/pools/{pool.Id}", new PoolInput(pool.Name, Gender.Female, pool.GradeMin, pool.GradeMax, 14))).StatusCode);
        // The session's only pool, with campers in it, stays.
        Assert.Equal(HttpStatusCode.Conflict, (await alex.DeleteAsync($"{Setup}/pools/{pool.Id}")).StatusCode);
    }

    [Fact]
    public async Task Pools_in_a_session_cannot_overlap_and_registration_dates_must_make_sense()
    {
        var alex = await Alex();
        var sessionId = await FamilyWeekendSummer2028();
        var overlap = await alex.PostAsJsonAsync($"{Setup}/sessions/{sessionId}/pools", new PoolInput("Grades 4–6", null, 4, 6, 10));
        Assert.Equal(HttpStatusCode.BadRequest, overlap.StatusCode);

        var detail = await Json(await alex.GetAsync($"{Setup}/sessions/{sessionId}"));
        Assert.Equal(2, detail.GetProperty("pools").GetArrayLength());
        var late = await alex.PutAsJsonAsync($"{Setup}/sessions/{sessionId}",
            new SessionInput("Summer 2028", new(2028, 8, 4), new(2028, 8, 6), SessionSetupEndpoints.MountBerry, new DateTime(2028, 9, 1, 14, 0, 0, DateTimeKind.Utc), null));
        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
    }

    // ── K4 · pricing and policies ────────────────────────────────────────────

    [Fact]
    public async Task Pricing_changes_are_validated_audited_and_leave_existing_registrations_alone()
    {
        var alex = await Alex();
        var past = await factory.WithDb(db => db.Sessions.Where(s => s.Program.Slug == SetupSeed.FamilyWeekendSlug && s.Name == "Summer 2026").Select(s => s.Id).SingleAsync());
        var sessionId = await FamilyWeekendSummer2028();
        Tier[] tiers = [new(30, 100, RefundBasis.AmountPaid, 2500), new(0, 0, RefundBasis.AmountPaid, 0)];

        Assert.Equal(HttpStatusCode.BadRequest, (await alex.PutAsJsonAsync($"{Setup}/sessions/{sessionId}/pricing", Pricing(40000, 50000, tiers))).StatusCode);
        Tier[] backwards = [new(30, 50, RefundBasis.AmountPaid, 0), new(0, 80, RefundBasis.AmountPaid, 0)];
        var rising = await alex.PutAsJsonAsync($"{Setup}/sessions/{sessionId}/pricing", Pricing(40000, 10000, backwards));
        Assert.Equal(HttpStatusCode.BadRequest, rising.StatusCode);
        Assert.Contains("can't refund more", await rising.Content.ReadAsStringAsync());

        // A plan can't put an installment in the past.
        var pastPlan = await alex.PutAsJsonAsync($"{Setup}/sessions/{sessionId}/pricing", Pricing(40000, 10000, tiers) with { PlanInstallments = 3, BalanceDueDate = factory.Clock.Today().AddMonths(1) });
        Assert.Equal(HttpStatusCode.BadRequest, pastPlan.StatusCode);
        Assert.Contains("which has passed", await pastPlan.Content.ReadAsStringAsync());

        var preview = await Json(await alex.PostAsJsonAsync($"{Setup}/sessions/{sessionId}/pricing/preview", Pricing(42000, 12000, tiers)));
        Assert.Equal(42000, preview.GetProperty("scheduleTotalCents").GetInt32());
        Assert.Equal(42000 - 2500, preview.GetProperty("tiers")[0].GetProperty("exampleRefundCents").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await alex.PutAsJsonAsync($"{Setup}/sessions/{sessionId}/pricing", Pricing(42000, 12000, tiers))).StatusCode);
        var saved = await factory.WithDb(db => db.Sessions.SingleAsync(s => s.Id == sessionId));
        Assert.Equal((42000, 12000), (saved.PriceCents, saved.DepositCents));
        Assert.Equal(2, await factory.WithDb(db => db.Set<RefundTier>().CountAsync(t => t.SessionId == sessionId)));
        var changes = await factory.WithDb(db => db.Set<AuditChange>().Where(c => c.AuditEvent.Action == "pricing.changed" && c.AuditEvent.EntityId == $"{sessionId}").ToListAsync());
        Assert.Contains(changes, c => c.Field == "Price" && c.Before == "$395" && c.After == "$420");

        // Repricing one session never touches what was sold for another.
        Assert.Equal(HttpStatusCode.OK, (await alex.PutAsJsonAsync($"{Setup}/sessions/{past}/pricing", Pricing(50000, 10000, tiers) with { BalanceDueDate = new(2026, 7, 1) })).StatusCode);
        Assert.All(await factory.WithDb(db => db.Registrations.Where(r => r.SessionId == past).Select(r => r.PriceCents).ToListAsync()), p => Assert.Equal(37500, p));
    }

    [Fact]
    public async Task The_cancellation_quote_uses_the_sessions_refund_table()
    {
        var alex = await Alex();
        var reg = await factory.WithDb(db => db.Registrations.Include(r => r.Session)
            .FirstAsync(r => r.Session.Program.Slug == "day-camp-atlanta" && r.Status == RegistrationStatus.Confirmed && r.PaidCents == r.PriceCents));
        Tier[] generous = [new(0, 100, RefundBasis.AmountPaid, 1000)];
        var put = await alex.PutAsJsonAsync($"{Setup}/sessions/{reg.SessionId}/pricing",
            new PricingInput(reg.Session.PriceCents, reg.Session.DepositCents, reg.Session.PlanInstallments, reg.Session.BalanceDueDate, [.. generous.Select(t => t.Input)]));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var quote = await Json(await (await factory.SignInAsStaff()).GetAsync($"/api/admin/registrations/{reg.Id}"));
        Assert.Equal(reg.PaidCents - 1000, quote.GetProperty("cancellation").GetProperty("suggestedRefundCents").GetInt32());
    }

    // ── K5 · discount rules ──────────────────────────────────────────────────

    [Fact]
    public async Task A_rule_is_live_only_for_its_session_and_until_its_cap()
    {
        var alex = await Alex();
        var (dayProgram, target, other) = await TwoDayCampSessions();
        var code = "CAP" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var created = await alex.PostAsJsonAsync($"{Setup}/discount-rules",
            new DiscountRuleInput(code, "Test cap", DiscountKind.Flat, 2000, dayProgram, target, new(2026, 1, 1), new(2030, 1, 1), 1, false));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(e => e.Action == "discount.rule_created" && e.Detail.Contains(code))));

        var (family, kid, household) = await NewFamily();
        var inScope = await Json(await family.PostAsJsonAsync($"/api/sessions/{target}/quote", new QuoteRequest([kid], PaymentOption.Full, code)));
        Assert.Equal(2000, inScope.GetProperty("discountCents").GetInt32());
        var outOfScope = await Json(await family.PostAsJsonAsync($"/api/sessions/{other}/quote", new QuoteRequest([kid], PaymentOption.Full, code)));
        Assert.Equal(Features.Pricing.InvalidCodeMessage, outOfScope.GetProperty("discountError").GetString());

        // The first order takes the only use; the second is refused at checkout, not just in the quote.
        await Checkout(household, kid, target, code);
        Assert.Equal(1, await factory.WithDb(db => db.Set<DiscountRule>().Where(r => r.DiscountCode.Code == code).Select(r => r.Uses).SingleAsync()));
        var (_, kid2, household2) = await NewFamily();
        var refused = await Assert.ThrowsAsync<CheckoutValidationException>(() => Checkout(household2, kid2, target, code));
        Assert.Contains(Features.Pricing.InvalidCodeMessage, refused.Errors["discountCode"]);

        // The cap can't be set below what's been used.
        var lower = await alex.PutAsJsonAsync($"{Setup}/discount-rules/{(await Json(created)).GetProperty("id").GetInt32()}",
            new DiscountRuleInput(code, "Test cap", DiscountKind.Flat, 2000, dayProgram, target, new(2026, 1, 1), new(2030, 1, 1), 0, false));
        Assert.Equal(HttpStatusCode.BadRequest, lower.StatusCode);
    }

    [Fact]
    public async Task A_used_codes_type_and_amount_are_fixed_but_its_other_terms_stay_editable()
    {
        var alex = await Alex();
        var (dayProgram, target, _) = await TwoDayCampSessions();
        var code = "FIX" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        DiscountRuleInput Input(DiscountKind kind, int value, string name = "Fixed test", int? cap = 10, bool wholeProgram = false) =>
            new(code, name, kind, value, dayProgram, wholeProgram ? null : target, new(2026, 1, 1), new(2030, 1, 1), cap, false);
        var created = await alex.PostAsJsonAsync($"{Setup}/discount-rules", Input(DiscountKind.Percent, 10));
        var id = (await Json(created)).GetProperty("id").GetInt32();

        // Unused, the amount can still change.
        Assert.Equal(HttpStatusCode.OK, (await alex.PutAsJsonAsync($"{Setup}/discount-rules/{id}", Input(DiscountKind.Percent, 15))).StatusCode);

        var (_, kid, household) = await NewFamily();
        await Checkout(household, kid, target, code);

        foreach (var (kind, value) in new[] { (DiscountKind.Percent, 50), (DiscountKind.Flat, 1500) })
        {
            var res = await alex.PutAsJsonAsync($"{Setup}/discount-rules/{id}", Input(kind, value));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Contains($"{code} has been used 1 time. Create a new code to change the amount.", await res.Content.ReadAsStringAsync());
        }
        Assert.Equal((DiscountKind.Percent, 15), await factory.WithDb(db => db.DiscountCodes.Where(d => d.Id == id).Select(d => ValueTuple.Create(d.Kind, d.Value)).SingleAsync()));

        // Name, cap, scope and the rest stay editable, and the audit row names the scope in words.
        Assert.Equal(HttpStatusCode.OK, (await alex.PutAsJsonAsync($"{Setup}/discount-rules/{id}", Input(DiscountKind.Percent, 15, "Renamed", 20, wholeProgram: true))).StatusCode);
        var scope = await factory.WithDb(db => db.Set<AuditChange>().SingleAsync(c => c.AuditEvent.Action == "discount.rule_changed" && c.AuditEvent.EntityId == $"{id}" && c.Field == "Scope"));
        Assert.Matches(@"^Day Camp.* · Setup test week", scope.Before);
        Assert.Matches(@"^Day Camp.*, all sessions$", scope.After);
    }

    [Fact]
    public async Task Stacked_discounts_cannot_exceed_the_price()
    {
        var alex = await Alex();
        var (dayProgram, june, _) = await TwoDayCampSessions();
        // EARLY50 ($50, stackable) covers all of Day Camp. 90% more on a $325 week is over 100%.
        var stacked = await alex.PostAsJsonAsync($"{Setup}/discount-rules",
            new DiscountRuleInput("STACK90", "Too much", DiscountKind.Percent, 90, dayProgram, june, new(2026, 9, 1), new(2028, 6, 1), null, true));
        Assert.Equal(HttpStatusCode.BadRequest, stacked.StatusCode);
        Assert.Contains("EARLY50", await stacked.Content.ReadAsStringAsync());

        var preview = await Json(await alex.PostAsJsonAsync($"{Setup}/discount-rules/preview",
            new DiscountPreviewInput(DiscountKind.Percent, 90, dayProgram, june, new(2026, 9, 1), new(2028, 6, 1), true, null, null)));
        Assert.True(preview.GetProperty("blocked").GetBoolean());

        // The same rule without stacking is fine, and turning it off makes it an invalid code.
        var alone = await alex.PostAsJsonAsync($"{Setup}/discount-rules",
            new DiscountRuleInput("ALONE90", "Scholarship", DiscountKind.Percent, 90, dayProgram, june, new(2026, 9, 1), new(2028, 6, 1), null, false));
        Assert.Equal(HttpStatusCode.OK, alone.StatusCode);
        var id = (await Json(alone)).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await alex.PostAsync($"{Setup}/discount-rules/{id}/deactivate", null)).StatusCode);
        var (family, kid, _) = await NewFamily();
        var quote = await Json(await family.PostAsJsonAsync($"/api/sessions/{june}/quote", new QuoteRequest([kid], PaymentOption.Full, "ALONE90")));
        Assert.Equal(0, quote.GetProperty("discountCents").GetInt32());

        var list = await Json(await alex.GetAsync($"{Setup}/discount-rules"));
        var summerfun = list.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("code").GetString() == "SUMMERFUN");
        Assert.Equal("Pending approval", summerfun.GetProperty("status").GetString());
        Assert.False(summerfun.GetProperty("editable").GetBoolean());
    }

    [Fact]
    public async Task Discount_codes_must_be_unique_and_well_formed()
    {
        var alex = await Alex();
        Assert.Equal(HttpStatusCode.BadRequest, (await alex.PostAsJsonAsync($"{Setup}/discount-rules",
            new DiscountRuleInput("EARLYBIRD", "Dup", DiscountKind.Percent, 5, null, null, new(2026, 9, 1), new(2027, 1, 1), null, false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alex.PostAsJsonAsync($"{Setup}/discount-rules",
            new DiscountRuleInput("no spaces!", "Bad", DiscountKind.Percent, 5, null, null, new(2026, 9, 1), new(2027, 1, 1), null, false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alex.PostAsJsonAsync($"{Setup}/discount-rules",
            new DiscountRuleInput("BACKWARDS", "Dates", DiscountKind.Percent, 5, null, null, new(2027, 1, 1), new(2026, 9, 1), null, false))).StatusCode);
    }

    // ── K7 · waivers ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Approving_a_waiver_version_makes_it_live_and_keeps_earlier_signatures()
    {
        var alex = await Alex();
        var template = await factory.WithDb(db => db.WaiverTemplates.SingleAsync(w => w.Title == SetupSeed.FamilyWaiverTitle));
        var detail = await Json(await alex.GetAsync($"{Setup}/waivers/{template.Id}"));
        var versions = detail.GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal([3, 2, 1], versions.Select(v => v.GetProperty("version").GetInt32()));
        Assert.Equal(10, versions[1].GetProperty("signatures").GetInt32());
        Assert.Equal(4, versions[2].GetProperty("signatures").GetInt32());

        // A live version is never edited in place.
        var live = versions[1].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await alex.PutAsJsonAsync($"{Setup}/waiver-versions/{live}", new WaiverDraftInput("new text", "x"))).StatusCode);

        var v3 = versions[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await alex.PostAsync($"{Setup}/waiver-versions/{v3}/approve", null)).StatusCode);
        var after = await factory.WithDb(db => db.WaiverTemplates.SingleAsync(w => w.Id == template.Id));
        Assert.Equal(3, after.Version);
        Assert.Contains("Lake and waterfront", after.Body);
        Assert.Equal(WaiverVersionStatus.Archived, await factory.WithDb(db => db.Set<WaiverVersion>().Where(v => v.Id == live).Select(v => v.Status).SingleAsync()));
        Assert.Equal(10, await factory.WithDb(db => db.WaiverAcceptances.CountAsync(a => a.WaiverTemplateId == template.Id && a.Version == 2)));
        Assert.True(await factory.WithDb(db => db.AuditChanges().AnyAsync(c => c.AuditEvent.Action == "waiver.published" && c.Before == "v2" && c.After == "v3")));
    }

    [Fact]
    public async Task The_author_of_a_waiver_version_cannot_approve_it()
    {
        var alex = await Alex();
        var template = await factory.WithDb(db => db.WaiverTemplates.FirstAsync(w => w.Title == "Photo and Media Release"));
        var draft = await alex.PostAsync($"{Setup}/waivers/{template.Id}/drafts", null);
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        var vid = (await Json(draft)).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await alex.PostAsync($"{Setup}/waivers/{template.Id}/drafts", null)).StatusCode);

        // Unchanged text and a missing note are refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await alex.PostAsync($"{Setup}/waiver-versions/{vid}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alex.PutAsJsonAsync($"{Setup}/waiver-versions/{vid}", new WaiverDraftInput(template.Body + " Photos are kept for three years.", "Adds how long photos are kept."))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alex.PostAsync($"{Setup}/waiver-versions/{vid}/submit", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await alex.PostAsync($"{Setup}/waiver-versions/{vid}/approve", null)).StatusCode);
        Assert.Equal(template.Version, await factory.WithDb(db => db.WaiverTemplates.Where(w => w.Id == template.Id).Select(w => w.Version).SingleAsync()));
    }

    // ── K12 · audit log ──────────────────────────────────────────────────────

    [Fact]
    public async Task Audit_rows_cannot_be_changed_or_deleted()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => factory.WithDb(db => db.AuditEvents.ExecuteUpdateAsync(s => s.SetProperty(e => e.Detail, "edited"))));
        await Assert.ThrowsAnyAsync<Exception>(() => factory.WithDb(db => db.AuditEvents.ExecuteDeleteAsync()));
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync()));
        Assert.False(await factory.WithDb(db => db.AuditEvents.AnyAsync(e => e.Detail == "edited")));
    }

    [Fact]
    public async Task Audit_log_filters_shows_before_and_after_and_exports_safe_csv()
    {
        var cet = await factory.SignInAsStaff("cet", "Diane Carter");
        var list = await Json(await cet.GetAsync("/api/admin/audit-log?category=discount&q=SIBLING10"));
        var row = list.GetProperty("rows").EnumerateArray().First(r => r.GetProperty("action").GetString() == "discount.rule_created");
        Assert.All(list.GetProperty("rows").EnumerateArray(), r => Assert.StartsWith("discount.", r.GetProperty("action").GetString()));
        var entry = await Json(await cet.GetAsync($"/api/admin/audit-log/{row.GetProperty("id").GetInt64()}"));
        Assert.Contains(entry.GetProperty("changes").EnumerateArray(), c => c.GetProperty("field").GetString() == "Code" && c.GetProperty("after").GetString() == "SIBLING10");

        var csv = await cet.GetAsync("/api/admin/audit-log/export?category=discount");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("When (UTC),Who,Action", await csv.Content.ReadAsStringAsync());
        Assert.Equal("'=HYPERLINK(1)", AuditLogEndpoints.Cell("=HYPERLINK(1)"));
        Assert.Equal("\"a,\"\"b\"\"\"", AuditLogEndpoints.Cell("a,\"b\""));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    sealed record Tier(int DaysBefore, int Percent, RefundBasis Basis, int Fee)
    {
        public TierInput Input => new(DaysBefore, Percent, Basis, Fee);
    }

    static PricingInput Pricing(int price, int deposit, Tier[] tiers) => new(price, deposit, 2, new(2028, 7, 1), [.. tiers.Select(t => t.Input)]);

    static ProgramInput NewProgram(string name) =>
        new(1, name, ProgramType.Standard, HealthMechanism.Embedded, SessionSetupEndpoints.MountBerry, "A tagline.", "A description.");

    Task<HttpClient> Alex() => factory.SignInAsStaff("admin", "Alex Morgan");

    Task<int> FamilyWeekendId() => factory.WithDb(db => db.Programs.Where(p => p.Slug == SetupSeed.FamilyWeekendSlug).Select(p => p.Id).SingleAsync());

    Task<int> FamilyWeekendSummer2028() =>
        factory.WithDb(db => db.Sessions.Where(s => s.Program.Slug == SetupSeed.FamilyWeekendSlug && s.Name == "Summer 2028").Select(s => s.Id).SingleAsync());

    /// <summary>Day Camp's program id, its June week, and a second Day Camp session with open seats for any grade.</summary>
    Task<(int Program, int June, int Other)> TwoDayCampSessions() => factory.WithDb(async db =>
    {
        var day = await db.Programs.Include(p => p.Sessions).SingleAsync(p => p.Slug == "day-camp-atlanta");
        var june = day.Sessions.Single(s => s.Name == "June week");
        var other = new Session
        {
            ProgramId = day.Id,
            Name = "Setup test week " + Guid.NewGuid().ToString("N")[..4],
            StartDate = new(2028, 7, 10),
            EndDate = new(2028, 7, 14),
            PriceCents = 32500,
            DepositCents = 10000,
            BalanceDueDate = new(2028, 6, 1),
        };
        other.Pools.Add(new CapacityPool { Name = "Everyone", GradeMin = 0, GradeMax = 12, Capacity = 50 });
        db.Sessions.Add(other);
        await db.SaveChangesAsync();
        // The cap test checks out into this session; the rule scope targets it and the June week is "elsewhere".
        return (day.Id, other.Id, june.Id);
    });

    async Task<(HttpClient Client, int KidId, int HouseholdId)> NewFamily()
    {
        var email = $"setup-{Guid.NewGuid():N}@example.com";
        var client = await factory.SignInAsFamily(email, "Pat", "Setup");
        var (kid, household) = await factory.WithDb(async db =>
        {
            var h = await db.Households.SingleAsync(x => x.Email == email);
            var p = new Person { HouseholdId = h.Id, FirstName = "Kid", LastName = "Setup", DateOfBirth = new(2017, 3, 4), Gender = Gender.Male };
            db.People.Add(p);
            await db.SaveChangesAsync();
            return (p.Id, h.Id);
        });
        return (client, kid, household);
    }

    async Task Checkout(int householdId, int kidId, int sessionId, string? code)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampDbContext>();
        var programId = await db.Sessions.Where(s => s.Id == sessionId).Select(s => s.ProgramId).SingleAsync();
        var waivers = await db.WaiverTemplates.Where(w => w.ProgramId == programId).ToListAsync();
        var req = new CheckoutRequest(
            $"setup-{Guid.NewGuid():N}", sessionId,
            [new CheckoutParticipant(kidId, new() { ["tshirt"] = "Youth M", ["swim"] = "Beginner" }, new HealthForm(null, null, null, null, "Dr. Test", "555-0100", null))],
            new() { ["church"] = "No" },
            [.. waivers.Select(w => new WaiverSignature(w.Id, w.PerParticipant ? kidId : null, "Pat"))],
            PaymentOption.Full, code, _gateway.Tokenize("4242424242424242"));
        var result = await scope.ServiceProvider.GetRequiredService<CheckoutService>().CheckoutAsync(householdId, "test", req, default);
        Assert.Equal(OrderStatus.Paid, result.Status);
    }

    static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        return JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text).RootElement;
    }
}

static class SetupDbExtensions
{
    public static IQueryable<AuditChange> AuditChanges(this CampDbContext db) => db.Set<AuditChange>();
}
