using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Camp.Api.Domain;
using Camp.Api.Features.Finance;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Tests;

/// <summary>Slice 6: reports, reconciliation, plan exceptions, Fusion journals and scholarships.</summary>
public class FinanceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    static readonly string TinyPdf = Convert.ToBase64String("%PDF-1.4\n1 0 obj << >> endobj\n%%EOF\n"u8.ToArray());

    // ── access ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/admin/finance/reports")]
    [InlineData("/api/admin/finance/reports/export")]
    [InlineData("/api/admin/finance/settlements")]
    [InlineData("/api/admin/finance/plan-exceptions")]
    [InlineData("/api/admin/finance/journals")]
    public async Task Finance_routes_need_finance_or_admin(string url)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsFamily()).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsStaff("cet")).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsStaff("host", "Pastor Dave")).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await factory.SignInAsStaff("finance", "Marcus Reed")).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await factory.SignInAsStaff("admin", "Priya Shah")).GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Finance_mutations_reject_cet_and_family()
    {
        var cet = await factory.SignInAsStaff("cet");
        var family = await factory.SignInAsFamily();
        foreach (var url in new[] { "/api/admin/finance/settlement-lines/1/resolve", "/api/admin/finance/plan-exceptions/1/retry", "/api/admin/finance/plan-exceptions/1/contact", "/api/admin/finance/journals/1/retry" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await cet.PostAsJsonAsync(url, new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await family.PostAsJsonAsync(url, new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync(url, new { })).StatusCode);
        }
    }

    [Fact]
    public async Task Scholarship_review_is_staff_only_and_the_family_side_is_family_only()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/admin/scholarships")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsFamily()).GetAsync("/api/admin/scholarships")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsStaff("host", "Pastor Dave")).GetAsync("/api/admin/scholarships")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await factory.SignInAsStaff("cet")).GetAsync("/api/admin/scholarships")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await factory.SignInAsStaff("finance", "Marcus Reed")).GetAsync("/api/admin/scholarships")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/family/scholarships")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsStaff("cet")).GetAsync("/api/family/scholarships")).StatusCode);
    }

    // ── FN1 / FN2: revenue ties to reconciliation ────────────────────────────

    [Fact]
    public async Task Report_settled_revenue_equals_the_reconciliation_batches_to_the_cent()
    {
        var marcus = await Finance();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-30);
        var report = await Json(await marcus.GetAsync($"/api/admin/finance/reports?from={from:yyyy-MM-dd}&to={today:yyyy-MM-dd}"));
        var settlements = await Json(await marcus.GetAsync("/api/admin/finance/settlements"));
        var fn2 = settlements.GetProperty("batches").EnumerateArray()
            .Where(b => DateOnly.Parse(b.GetProperty("settledOn").GetString()!, System.Globalization.CultureInfo.InvariantCulture) >= from)
            .Sum(b => b.GetProperty("grossCents").GetInt32());
        var revenue = report.GetProperty("settledRevenue");
        Assert.True(fn2 > 0);
        Assert.Equal(fn2, revenue.GetProperty("amountCents").GetInt32());
        Assert.Equal(fn2, revenue.GetProperty("batchGrossCents").GetInt32());
        Assert.True(revenue.GetProperty("certified").GetBoolean());
        // The table's rows add up to the same figure, the unattributed row included.
        Assert.Equal(fn2, report.GetProperty("rows").EnumerateArray().Sum(r => r.GetProperty("settledRevenueCents").GetInt32()));

        // Scoped to one program, only that program's lines count, and they're part of the whole.
        var programId = await factory.WithDb(db => db.Programs.Where(p => p.Slug == FinanceSeed.ProgramSlug).Select(p => p.Id).SingleAsync());
        var scoped = await Json(await marcus.GetAsync($"/api/admin/finance/reports?programId={programId}&from={from:yyyy-MM-dd}&to={today:yyyy-MM-dd}"));
        var familyCamp = scoped.GetProperty("settledRevenue").GetProperty("amountCents").GetInt32();
        Assert.InRange(familyCamp, 1, fn2);
        Assert.All(scoped.GetProperty("rows").EnumerateArray(), r => Assert.Equal("Family Camp", r.GetProperty("program").GetString()));

        Assert.Equal(HttpStatusCode.BadRequest, (await marcus.GetAsync($"/api/admin/finance/reports?from={today:yyyy-MM-dd}&to={from:yyyy-MM-dd}")).StatusCode);
    }

    [Fact]
    public async Task Report_export_is_a_csv_and_is_audited()
    {
        var res = await (await Finance()).GetAsync("/api/admin/finance/reports/export");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/csv", res.Content.Headers.ContentType?.MediaType);
        var csv = await res.Content.ReadAsStringAsync();
        Assert.StartsWith("Program,Session,Registrations,Attended,Settled revenue,Contracted tuition", csv, StringComparison.Ordinal);
        Assert.Contains("Family Camp,Summer 2028,", csv, StringComparison.Ordinal);
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(e => e.Action == "report.exported" && e.Actor == "Marcus Reed (FINANCE)")));
    }

    [Fact]
    public async Task Latest_batch_has_two_unmatched_lines_and_resolving_them_creates_the_journal()
    {
        var marcus = await Finance();
        var batch = await LatestBatch(marcus);
        Assert.Equal(2, batch.GetProperty("transactions").GetProperty("unmatched").GetInt32());
        Assert.Equal("Not created", batch.GetProperty("journal").GetProperty("status").GetString());
        var processor = batch.GetProperty("processor");
        Assert.Equal(processor.GetProperty("grossCents").GetInt32() - processor.GetProperty("feeCents").GetInt32(), processor.GetProperty("netCents").GetInt32());
        // Platform side is the processor gross less what nobody on the platform recorded.
        Assert.Equal(processor.GetProperty("grossCents").GetInt32() - FinanceSeed.MitchellPhonePaymentCents - FinanceSeed.UnknownPaymentCents,
            batch.GetProperty("platform").GetProperty("grossCents").GetInt32());
        Assert.Contains(batch.GetProperty("lines").EnumerateArray(), l => l.GetProperty("kind").GetString() == "Fee");
        var lines = batch.GetProperty("lines").EnumerateArray().Where(l => l.GetProperty("status").GetString() == "Unmatched").ToList();
        var mitchell = lines.Single(l => l.GetProperty("cardholderName").GetString() == "Dana Mitchell").GetProperty("id").GetInt32();
        var unknown = lines.Single(l => l.GetProperty("cardholderName").GetString() == "K. Nguyen").GetProperty("id").GetInt32();

        var candidates = (await Json(await marcus.GetAsync($"/api/admin/finance/settlement-lines/{mitchell}/candidates"))).GetProperty("candidates");
        var candidate = Assert.Single(candidates.EnumerateArray());
        var code = candidate.GetProperty("confirmationCode").GetString()!;
        Assert.True(candidate.GetProperty("canTake").GetBoolean());
        Assert.Empty((await Json(await marcus.GetAsync($"/api/admin/finance/settlement-lines/{unknown}/candidates"))).GetProperty("candidates").EnumerateArray());
        var before = await Balance(code);

        // A note is required, and the overpayment guard refuses a registration that owes less.
        Assert.Equal(HttpStatusCode.BadRequest, (await marcus.PostAsJsonAsync($"/api/admin/finance/settlement-lines/{mitchell}/resolve", new { resolution = "MatchedToRegistration", code })).StatusCode);
        var paidUp = await factory.WithDb(db => db.Orders.Where(o => o.Session.Program.Slug == FinanceSeed.ProgramSlug && o.PaymentOption == PaymentOption.Full).Select(o => o.ConfirmationCode).FirstAsync());
        var over = await marcus.PostAsJsonAsync($"/api/admin/finance/settlement-lines/{mitchell}/resolve", new { resolution = "MatchedToRegistration", code = paidUp, note = "Wrong family" });
        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
        Assert.Contains("overpay", await over.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var ok = await marcus.PostAsJsonAsync($"/api/admin/finance/settlement-lines/{mitchell}/resolve", new { resolution = "MatchedToRegistration", code, note = "Dana called the front desk and paid by phone." });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Json(ok)).GetProperty("journal").ValueKind);
        Assert.Equal(before - FinanceSeed.MitchellPhonePaymentCents, await Balance(code));
        Assert.Equal(HttpStatusCode.Conflict, (await marcus.PostAsJsonAsync($"/api/admin/finance/settlement-lines/{mitchell}/resolve", new { resolution = "Adjustment", note = "Again" })).StatusCode);

        var last = await Json(await marcus.PostAsJsonAsync($"/api/admin/finance/settlement-lines/{unknown}/resolve", new { resolution = "Adjustment", note = "Posted to unapplied receipts pending a call back." }));
        Assert.StartsWith("JRN-", last.GetProperty("journal").GetString(), StringComparison.Ordinal);

        var after = await LatestBatch(marcus);
        Assert.Equal(0, after.GetProperty("transactions").GetProperty("unmatched").GetInt32());
        Assert.Equal("Pending", after.GetProperty("journal").GetProperty("status").GetString());
        // The matched phone payment now counts on the platform side; the adjustment doesn't.
        Assert.Equal(after.GetProperty("processor").GetProperty("grossCents").GetInt32() - FinanceSeed.UnknownPaymentCents, after.GetProperty("platform").GetProperty("grossCents").GetInt32());
        var journalId = after.GetProperty("journal").GetProperty("id").GetInt32();
        var journal = await Json(await marcus.GetAsync($"/api/admin/finance/journals/{journalId}"));
        Assert.True(journal.GetProperty("balanced").GetBoolean());
        Assert.Contains(journal.GetProperty("journalLines").EnumerateArray(), l => l.GetProperty("account").GetString() == "2400" && l.GetProperty("creditCents").GetInt32() == FinanceSeed.UnknownPaymentCents);
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(e => e.Action == "settlement.matched" && e.Actor == "Marcus Reed (FINANCE)")));
        Assert.True(await factory.WithDb(db => db.OutboxEvents.AnyAsync(e => e.Target == "OracleFusion" && e.AggregateId == journal.GetProperty("reference").GetString())));
    }

    // ── FN3 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Plan_exceptions_list_failed_installments_with_grace_and_days_left()
    {
        var body = await Json(await (await Finance()).GetAsync("/api/admin/finance/plan-exceptions"));
        var rows = body.GetProperty("rows").EnumerateArray().Where(r => r.GetProperty("program").GetString() == "Family Camp").ToList();
        Assert.Equal(8, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal("Installment failed", r.GetProperty("status").GetString());
            var failed = DateOnly.Parse(r.GetProperty("failedOn").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(failed.AddDays(7).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), r.GetProperty("graceEndsOn").GetString());
        });
        Assert.Contains(rows, r => r.GetProperty("stage").GetString() == "Needs attention");
        Assert.Contains(rows, r => r.GetProperty("stage").GetString() == "Retry scheduled");
        Assert.Contains(rows, r => r.GetProperty("stage").GetString() == "In grace period");
    }

    [Fact]
    public async Task Retry_now_charges_the_card_on_file_and_a_declining_card_counts_an_attempt()
    {
        var marcus = await Finance();
        var (good, bad) = await factory.WithDb(async db =>
        {
            var cards = await db.Set<FinanceCardOnFile>().ToListAsync();
            var failed = await db.Installments.Where(i => i.Status == InstallmentStatus.Failed).ToListAsync();
            var withCard = failed.Where(i => cards.Any(c => c.OrderId == i.OrderId)).ToList();
            return (withCard.First(i => cards.Single(c => c.OrderId == i.OrderId).Last4 == "4242"), withCard.First(i => cards.Single(c => c.OrderId == i.OrderId).Last4 == "0002"));
        });
        var balanceBefore = await factory.WithDb(db => db.Registrations.Where(r => r.OrderId == good.OrderId).SumAsync(r => r.PriceCents - r.DiscountCents - r.PaidCents));

        Assert.Equal(HttpStatusCode.BadRequest, (await marcus.PostAsJsonAsync($"/api/admin/finance/plan-exceptions/{good.Id}/retry", new { })).StatusCode);
        var ok = await Json(await marcus.PostAsJsonAsync($"/api/admin/finance/plan-exceptions/{good.Id}/retry", new { idempotencyKey = Guid.NewGuid().ToString() }));
        Assert.Equal("Succeeded", ok.GetProperty("outcome").GetString());
        Assert.Equal(InstallmentStatus.Paid, await factory.WithDb(db => db.Installments.Where(i => i.Id == good.Id).Select(i => i.Status).SingleAsync()));
        Assert.Equal(balanceBefore - good.AmountCents, await factory.WithDb(db => db.Registrations.Where(r => r.OrderId == good.OrderId).SumAsync(r => r.PriceCents - r.DiscountCents - r.PaidCents)));
        Assert.Equal(HttpStatusCode.Conflict, (await marcus.PostAsJsonAsync($"/api/admin/finance/plan-exceptions/{good.Id}/retry", new { idempotencyKey = Guid.NewGuid().ToString() })).StatusCode);

        var attempts = await factory.WithDb(db => db.Set<InstallmentFailure>().Where(f => f.InstallmentId == bad.Id).Select(f => f.Attempts).SingleAsync());
        var declined = await Json(await marcus.PostAsJsonAsync($"/api/admin/finance/plan-exceptions/{bad.Id}/retry", new { idempotencyKey = Guid.NewGuid().ToString() }));
        Assert.Equal("Declined", declined.GetProperty("outcome").GetString());
        Assert.Equal(attempts + 1, await factory.WithDb(db => db.Set<InstallmentFailure>().Where(f => f.InstallmentId == bad.Id).Select(f => f.Attempts).SingleAsync()));
        Assert.Equal(InstallmentStatus.Failed, await factory.WithDb(db => db.Installments.Where(i => i.Id == bad.Id).Select(i => i.Status).SingleAsync()));

        var detail = await Json(await marcus.GetAsync($"/api/admin/finance/plan-exceptions/{bad.Id}"));
        Assert.Contains(detail.GetProperty("timeline").EnumerateArray(), e => e.GetProperty("actor").GetString() == "Marcus Reed (FINANCE)");
    }

    [Fact]
    public async Task Contact_family_queues_one_email_and_refuses_a_second_right_away()
    {
        var marcus = await Finance();
        var id = await factory.WithDb(db => db.Set<InstallmentFailure>().Where(f => f.ResolvedAt == null && f.Installment.Status == InstallmentStatus.Failed).OrderByDescending(f => f.FailedOn).Select(f => f.InstallmentId).FirstAsync());
        Assert.Equal(HttpStatusCode.OK, (await marcus.PostAsync($"/api/admin/finance/plan-exceptions/{id}/contact", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await marcus.PostAsync($"/api/admin/finance/plan-exceptions/{id}/contact", null)).StatusCode);
        Assert.Equal(1, await factory.WithDb(db => db.OutboxEvents.CountAsync(e => e.Type == "InstallmentFailedReminder" && e.Target == "HubSpot")));
    }

    // ── FN4 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Journals_balance_and_a_failed_export_can_be_retried_once()
    {
        var marcus = await Finance();
        var list = await Json(await marcus.GetAsync("/api/admin/finance/journals"));
        Assert.All(list.GetProperty("rows").EnumerateArray(), r => Assert.Equal(r.GetProperty("debitCents").GetInt32(), r.GetProperty("creditCents").GetInt32()));
        var failed = list.GetProperty("rows").EnumerateArray().First(r => r.GetProperty("status").GetString() == "Failed");
        Assert.False(string.IsNullOrEmpty(failed.GetProperty("errorDetail").GetString()));
        var id = failed.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await marcus.PostAsync($"/api/admin/finance/journals/{id}/retry", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await marcus.PostAsync($"/api/admin/finance/journals/{id}/retry", null)).StatusCode);
        var detail = await Json(await marcus.GetAsync($"/api/admin/finance/journals/{id}"));
        Assert.Equal("Pending", detail.GetProperty("status").GetString());
        Assert.Equal("Marcus Reed (FINANCE)", detail.GetProperty("events").EnumerateArray().Last().GetProperty("actor").GetString());
        var posted = list.GetProperty("rows").EnumerateArray().First(r => r.GetProperty("status").GetString() == "Posted").GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await marcus.PostAsync($"/api/admin/finance/journals/{posted}/retry", null)).StatusCode);
    }

    // ── O6 / O7 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Maria_applies_for_Avery_and_Mia_and_the_review_caps_the_award()
    {
        var maria = await factory.SignInAsFamily();
        var overview = await Json(await maria.GetAsync("/api/family/scholarships"));
        Assert.Equal("Maria Johnson", overview.GetProperty("household").GetProperty("contact").GetString());
        var order = overview.GetProperty("orders").EnumerateArray().Single(o => o.GetProperty("program").GetString() == "Family Camp");
        Assert.True(order.GetProperty("canApply").GetBoolean());
        var code = order.GetProperty("confirmationCode").GetString()!;
        var ids = order.GetProperty("campers").EnumerateArray().Select(c => c.GetProperty("registrationId").GetInt32()).ToArray();
        Assert.Equal(2, ids.Length);

        var noDoc = await maria.PostAsJsonAsync("/api/family/scholarships", Apply(code, ids, 30000, null));
        Assert.Equal(HttpStatusCode.BadRequest, noDoc.StatusCode);
        Assert.Contains("supporting document is required", await noDoc.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var notPdf = new { fileName = "x.pdf", contentType = "application/pdf", base64 = Convert.ToBase64String("hello"u8.ToArray()) };
        Assert.Equal(HttpStatusCode.BadRequest, (await maria.PostAsJsonAsync("/api/family/scholarships", Apply(code, ids, 30000, notPdf))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await maria.PostAsJsonAsync("/api/family/scholarships", Apply(code, ids, 95001, Pdf()))).StatusCode);
        var created = await maria.PostAsJsonAsync("/api/family/scholarships", Apply(code, ids, 30000, Pdf()));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await Json(created)).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await maria.PostAsJsonAsync("/api/family/scholarships", Apply(code, ids, 30000, Pdf()))).StatusCode);

        var diane = await factory.SignInAsStaff();
        var detail = await Json(await diane.GetAsync($"/api/admin/scholarships/{id}"));
        Assert.Equal(95000, detail.GetProperty("eligibleCents").GetInt32());
        Assert.Equal(75000, detail.GetProperty("owedCents").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await diane.GetAsync($"/api/admin/scholarships/{id}/document")).StatusCode);

        var cap = await diane.PostAsJsonAsync($"/api/admin/scholarships/{id}/approve", new { awardCents = 95100 });
        Assert.Equal(HttpStatusCode.BadRequest, cap.StatusCode);
        Assert.Contains("scholarships plus discounts cannot exceed 100%", await cap.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await diane.PostAsJsonAsync($"/api/admin/scholarships/{id}/approve", new { awardCents = 80000 })).StatusCode);

        var ok = await Json(await diane.PostAsJsonAsync($"/api/admin/scholarships/{id}/approve", new { awardCents = 25000, note = "Meets the fund guidelines." }));
        Assert.Equal(50000, ok.GetProperty("balanceCents").GetInt32());
        Assert.Equal(50000, await Balance(code));
        Assert.Equal(HttpStatusCode.Conflict, (await diane.PostAsJsonAsync($"/api/admin/scholarships/{id}/approve", new { awardCents = 100 })).StatusCode);
        Assert.Equal(25000, await factory.WithDb(db => db.Set<ScholarshipAwardLine>().Where(l => l.ApplicationId == id).SumAsync(l => l.AmountCents)));
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(e => e.Action == "scholarship.approved" && e.Actor == "Diane Carter (CET)")));

        var after = await Json(await maria.GetAsync("/api/family/scholarships"));
        var app = after.GetProperty("applications").EnumerateArray().Single(a => a.GetProperty("id").GetInt32() == id);
        Assert.Equal("Approved", app.GetProperty("status").GetString());
        Assert.Equal(25000, app.GetProperty("awardCents").GetInt32());
        Assert.False(app.TryGetProperty("decisionNote", out _));
    }

    [Fact]
    public async Task A_family_cannot_apply_against_or_see_another_households_registration()
    {
        var other = await factory.SignInAsFamily("someone.new@example.com", "Sam", "Newman");
        var body = await Json(await other.GetAsync("/api/family/scholarships"));
        Assert.Empty(body.GetProperty("orders").EnumerateArray());
        Assert.Empty(body.GetProperty("applications").EnumerateArray());

        var (code, regId) = await factory.WithDb(async db =>
        {
            var o = await db.Orders.Include(x => x.Registrations).FirstAsync(x => x.ConfirmationCode == "WS-FC2J08");
            return (o.ConfirmationCode, o.Registrations[0].Id);
        });
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/api/family/scholarships", Apply(code, [regId], 10000, Pdf()))).StatusCode);
    }

    [Fact]
    public async Task Denying_needs_a_note_and_changes_no_balance()
    {
        var diane = await factory.SignInAsStaff();
        var queue = await Json(await diane.GetAsync("/api/admin/scholarships"));
        Assert.True(queue.GetProperty("counts").GetProperty("submitted").GetInt32() >= 3);
        var row = queue.GetProperty("rows").EnumerateArray().Last(r => r.GetProperty("status").GetString() == "Submitted");
        var id = row.GetProperty("id").GetInt32();
        var code = row.GetProperty("confirmationCode").GetString()!;
        var before = await Balance(code);
        Assert.Equal(HttpStatusCode.BadRequest, (await diane.PostAsJsonAsync($"/api/admin/scholarships/{id}/deny", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await diane.PostAsJsonAsync($"/api/admin/scholarships/{id}/deny", new { note = "Income is above the guidelines." })).StatusCode);
        Assert.Equal(before, await Balance(code));
        Assert.Equal(HttpStatusCode.Conflict, (await diane.PostAsJsonAsync($"/api/admin/scholarships/{id}/approve", new { awardCents = 100 })).StatusCode);
    }

    [Fact]
    public async Task An_award_shrinks_the_unpaid_installments_so_the_plan_still_adds_up()
    {
        // A plan family applies, then gets an award: the plan's open installments drop by the award.
        var (email, code, ids) = await factory.WithDb(async db =>
        {
            var o = await db.Orders.Include(x => x.Household).Include(x => x.Registrations).Include(x => x.Installments)
                .Where(x => x.Session.Program.Slug == FinanceSeed.ProgramSlug && x.PaymentOption == PaymentOption.Plan && x.Installments.All(i => i.Status != InstallmentStatus.Failed))
                .OrderByDescending(x => x.Id).FirstAsync();
            return (o.Household.Email, o.ConfirmationCode, o.Registrations.Select(r => r.Id).ToArray());
        });
        var family = await factory.SignInAsFamily(email, "Plan", "Family");
        var created = await family.PostAsJsonAsync("/api/family/scholarships", Apply(code, ids, 10000, Pdf()));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await Json(created)).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await (await Finance()).PostAsJsonAsync($"/api/admin/scholarships/{id}/approve", new { awardCents = 10000 })).StatusCode);
        var orderId = await factory.WithDb(db => db.Orders.Where(o => o.ConfirmationCode == code).Select(o => o.Id).SingleAsync());
        var open = await factory.WithDb(db => db.Installments.Where(i => i.OrderId == orderId && i.Status != InstallmentStatus.Paid).SumAsync(i => i.AmountCents));
        var balance = await Balance(code);
        Assert.Equal(balance, open);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    Task<HttpClient> Finance() => factory.SignInAsStaff("finance", "Marcus Reed");

    static object Pdf() => new { fileName = "2025-tax-return.pdf", contentType = "application/pdf", base64 = TinyPdf };

    static object Apply(string code, int[] ids, int cents, object? doc) => new
    {
        code,
        registrationIds = ids,
        requestedCents = cents,
        incomeBand = "$35,000–$50,000",
        reason = "Our hours were cut this spring.",
        document = doc,
    };

    Task<int> Balance(string code) =>
        factory.WithDb(db => db.Registrations.Where(r => r.Order!.ConfirmationCode == code && r.Status != RegistrationStatus.Cancelled).SumAsync(r => r.PriceCents - r.DiscountCents - r.PaidCents));

    static async Task<JsonElement> LatestBatch(HttpClient client)
    {
        var list = await Json(await client.GetAsync("/api/admin/finance/settlements"));
        var id = list.GetProperty("batches")[0].GetProperty("id").GetInt32();
        return await Json(await client.GetAsync($"/api/admin/finance/settlements/{id}"));
    }

    static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {text}");
        return JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text).RootElement;
    }
}
