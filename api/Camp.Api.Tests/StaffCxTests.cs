using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features;
using Camp.Api.Features.Polish;
using Camp.Api.Features.StaffCx;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Camp.Api.Tests;

/// <summary>Slice 4: search, household 360, discount approvals, duplicate merge and session transfers.</summary>
public class StaffCxTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    readonly FakeFiservGateway _gateway = (FakeFiservGateway)factory.Services.GetRequiredService<IPaymentGateway>();

    // ── access ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/admin/search?q=Johnson")]
    [InlineData("/api/admin/discounts")]
    [InlineData("/api/admin/duplicates")]
    [InlineData("/api/admin/transfers")]
    public async Task Staff_routes_reject_anonymous_family_and_host(string url)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsFamily()).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsStaff("host", "Pastor Dave")).GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Finance_cannot_open_the_duplicate_or_transfer_queues()
    {
        var finance = await factory.SignInAsStaff("finance", "Marcus Lee");
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.GetAsync("/api/admin/duplicates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.GetAsync("/api/admin/transfers")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await finance.GetAsync("/api/admin/discounts")).StatusCode);
    }

    // ── C1 / C2 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_finds_the_Johnson_household_by_name_phone_and_child()
    {
        var staff = await factory.SignInAsStaff();
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.GetAsync("/api/admin/search?q=J")).StatusCode);

        var johnsonId = await factory.WithDb(db => db.Households.Where(h => h.Email == Seed.JohnsonEmail).Select(h => h.Id).SingleAsync());
        foreach (var q in new[] { "Johnson", "Avery Johnson", Seed.JohnsonEmail })
        {
            var body = await Json(await staff.GetAsync($"/api/admin/search?q={Uri.EscapeDataString(q)}"));
            Assert.Contains(body.GetProperty("rows").EnumerateArray(), r => r.GetProperty("id").GetInt32() == johnsonId);
        }
        var phone = await factory.WithDb(db => db.Households.Where(h => h.Id == johnsonId).Select(h => h.Phone).SingleAsync());
        var byPhone = await Json(await staff.GetAsync($"/api/admin/search?q={new string(phone.Where(char.IsDigit).ToArray())[^4..]}"));
        Assert.Contains(byPhone.GetProperty("rows").EnumerateArray(), r => r.GetProperty("id").GetInt32() == johnsonId);
    }

    [Fact]
    public async Task Household_360_shows_members_and_notes_and_never_health_details()
    {
        var staff = await factory.SignInAsStaff();
        var johnsonId = await factory.WithDb(db => db.Households.Where(h => h.Email == Seed.JohnsonEmail).Select(h => h.Id).SingleAsync());
        var res = await staff.GetAsync($"/api/admin/households/{johnsonId}");
        var raw = await res.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Contains(body.GetProperty("children").EnumerateArray(), c => c.GetProperty("name").GetString() == "Avery Johnson");
        Assert.True(body.GetProperty("notes").GetArrayLength() >= 2);
        Assert.DoesNotContain("healthJson", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"allergies\"", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"dietary\"", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync("/api/admin/households/999999")).StatusCode);
    }

    [Fact]
    public async Task A_note_records_its_author_and_is_audited()
    {
        var staff = await factory.SignInAsStaff();
        var johnsonId = await factory.WithDb(db => db.Households.Where(h => h.Email == Seed.JohnsonEmail).Select(h => h.Id).SingleAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync($"/api/admin/households/{johnsonId}/notes", new { body = "  " })).StatusCode);
        var ok = await staff.PostAsJsonAsync($"/api/admin/households/{johnsonId}/notes", new { body = "Called about the June week 2 move." });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var note = await factory.WithDb(db => db.Set<HouseholdNote>().OrderByDescending(n => n.Id).FirstAsync(n => n.HouseholdId == johnsonId));
        Assert.Equal("Diane Carter (CET)", note.Author);
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(a => a.Action == "household.note_added" && a.EntityId == johnsonId.ToString(CultureInfo.InvariantCulture) && a.Actor == "Diane Carter (CET)")));
    }

    // ── C8 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Over_threshold_code_needs_finance_and_then_prices_at_checkout()
    {
        var cet = await factory.SignInAsStaff();
        var finance = await factory.SignInAsStaff("finance", "Marcus Lee");
        var family = await factory.SignInAsFamily();
        var id = await RequestId("SUMMERFUN");
        var (sessionId, avery) = await WeekOneAndAvery();

        var before = await Json(await family.PostAsJsonAsync($"/api/sessions/{sessionId}/quote", new { personIds = new[] { avery }, paymentOption = "Full", discountCode = "SUMMERFUN" }));
        Assert.Equal(Pricing.InvalidCodeMessage, before.GetProperty("discountError").GetString());

        var denied = await cet.PostAsJsonAsync($"/api/admin/discounts/{id}/approve", new { note = "Looks fine" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("Finance", (await Json(denied)).GetProperty("error").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await finance.PostAsJsonAsync($"/api/admin/discounts/{id}/approve", new { note = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await finance.PostAsJsonAsync($"/api/admin/discounts/{id}/approve", new { note = "Grace Community partnership, approved by budget." })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await finance.PostAsJsonAsync($"/api/admin/discounts/{id}/approve", new { note = "again" })).StatusCode);

        var after = await Json(await family.PostAsJsonAsync($"/api/sessions/{sessionId}/quote", new { personIds = new[] { avery }, paymentOption = "Full", discountCode = "SUMMERFUN" }));
        Assert.Equal(5000, after.GetProperty("discountCents").GetInt32());
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(a => a.Action == "discount.approved" && a.Actor == "Marcus Lee (FINANCE)")));
    }

    [Fact]
    public async Task A_rejected_code_needs_a_note_and_stays_invalid_at_checkout()
    {
        var cet = await factory.SignInAsStaff();
        var family = await factory.SignInAsFamily();
        var id = await RequestId("RIVERKIDS");
        var (sessionId, avery) = await WeekOneAndAvery();

        Assert.Equal(HttpStatusCode.BadRequest, (await cet.PostAsJsonAsync($"/api/admin/discounts/{id}/reject", new { note = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cet.PostAsJsonAsync($"/api/admin/discounts/{id}/reject", new { note = "Riverwood isn't a registered partner yet." })).StatusCode);

        var quote = await Json(await family.PostAsJsonAsync($"/api/sessions/{sessionId}/quote", new { personIds = new[] { avery }, paymentOption = "Full", discountCode = "RIVERKIDS" }));
        Assert.Equal(Pricing.InvalidCodeMessage, quote.GetProperty("discountError").GetString());
        Assert.Equal(0, quote.GetProperty("discountCents").GetInt32());
    }

    [Fact]
    public async Task CET_can_approve_a_code_under_the_threshold()
    {
        var cet = await factory.SignInAsStaff();
        var id = await RequestId("CITYREACH"); // 10%: at the threshold, not over it
        Assert.Equal(HttpStatusCode.OK, (await cet.PostAsJsonAsync($"/api/admin/discounts/{id}/approve", new { note = (string?)null })).StatusCode);
        Assert.Equal(DiscountStatus.Approved, await factory.WithDb(db => db.DiscountCodes.Where(d => d.Code == "CITYREACH").Select(d => d.Status).SingleAsync()));
    }

    // ── C9 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Merge_requires_every_conflict_resolved_then_moves_everything_to_the_survivor()
    {
        var staff = await factory.SignInAsStaff();
        var queue = await Json(await staff.GetAsync("/api/admin/duplicates"));
        var pair = Assert.Single(queue.EnumerateArray());
        var a = pair.GetProperty("householdA").GetInt32();
        var b = pair.GetProperty("householdB").GetInt32();
        Assert.Contains("Jordan Lee", pair.GetProperty("reason").GetString());

        var detail = await Json(await staff.GetAsync($"/api/admin/duplicates/{a}/{b}"));
        var conflict = Assert.Single(detail.GetProperty("conflicts").EnumerateArray());
        Assert.Equal("keep-registration", conflict.GetProperty("options")[0].GetProperty("value").GetString());

        var fields = new Dictionary<string, string> { ["name"] = "a", ["email"] = "a", ["phone"] = "b", ["city"] = "a" };
        var unresolved = await staff.PostAsJsonAsync($"/api/admin/duplicates/{a}/{b}/merge", new { survivor = "a", fields });
        Assert.Equal(HttpStatusCode.BadRequest, unresolved.StatusCode);
        Assert.Equal(0, await factory.WithDb(db => db.Set<HouseholdMerge>().CountAsync()));

        var poolReserved = await factory.WithDb(db => db.CapacityPools.Where(p => p.Session.Name == StaffCxSeed.WeekTwoName && p.Name == "Grade 4").Select(p => p.Reserved).SingleAsync());
        var resolutions = new Dictionary<string, string> { [conflict.GetProperty("key").GetString()!] = "keep-registration" };
        var merged = await staff.PostAsJsonAsync($"/api/admin/duplicates/{a}/{b}/merge", new { survivor = "a", fields, resolutions });
        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);

        await factory.WithDb(async db =>
        {
            var survivor = await db.Households.Include(h => h.Members).SingleAsync(h => h.Id == a);
            var other = await db.Households.Include(h => h.Members).SingleAsync(h => h.Id == b);
            Assert.Equal("770-555-0103", survivor.Phone); // picked from B
            // The archived account keeps its own copy of each shared person; nothing points at it any more.
            Assert.All(other.Members, m => Assert.Contains(survivor.Members, s => s.FirstName == m.FirstName && s.DateOfBirth == m.DateOfBirth));
            Assert.False(await db.Registrations.AnyAsync(r => r.Person.HouseholdId == b));
            Assert.StartsWith($"merged-into-{a}:", other.Email);
            Assert.Single(survivor.Members, m => m.FirstName == "Jordan");
            Assert.True(await db.Registrations.AnyAsync(r => r.HouseholdId == a && r.Person.FirstName == "Jordan" && r.Status == RegistrationStatus.Confirmed));
            Assert.False(await db.WaitlistEntries.AnyAsync(w => (w.HouseholdId == a || w.HouseholdId == b) && w.Status == WaitlistStatus.Waiting));
            Assert.Equal(poolReserved, await db.CapacityPools.Where(p => p.Session.Name == StaffCxSeed.WeekTwoName && p.Name == "Grade 4").Select(p => p.Reserved).SingleAsync());
            Assert.True(await db.OutboxEvents.AnyAsync(e => e.Type == "ConstituentMerge" && e.Target == "Salesforce"));
            Assert.True(await db.AuditEvents.AnyAsync(e => e.Action == "household.merged" && e.EntityId == a.ToString(CultureInfo.InvariantCulture)));
            return 0;
        });
        Assert.Empty((await Json(await staff.GetAsync("/api/admin/duplicates"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/admin/duplicates/{a}/{b}/merge", new { survivor = "a", fields, resolutions })).StatusCode);
    }

    // ── F8 / C10 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Approving_a_transfer_moves_the_seat_exactly_once()
    {
        var staff = await factory.SignInAsStaff();
        var request = await SeededPending(blocked: false);
        var id = request.GetProperty("id").GetInt32();
        var regId = request.GetProperty("registrationId").GetInt32();
        var (fromPool, before) = await PoolOf(regId);
        var toBefore = await ToPoolReserved(id);

        var ok = await staff.PostAsJsonAsync($"/api/admin/transfers/{id}/approve", new { note = (string?)null });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/admin/transfers/{id}/approve", new { note = (string?)null })).StatusCode);

        var (newPool, _) = await PoolOf(regId);
        Assert.NotEqual(fromPool, newPool);
        Assert.Equal(before - 1, await factory.WithDb(db => db.CapacityPools.Where(p => p.Id == fromPool).Select(p => p.Reserved).SingleAsync()));
        Assert.Equal(toBefore + 1, await factory.WithDb(db => db.CapacityPools.Where(p => p.Id == newPool).Select(p => p.Reserved).SingleAsync()));
        Assert.True(await factory.WithDb(db => db.AuditEvents.AnyAsync(a => a.Action == "registration.transferred" && a.EntityId == regId.ToString(CultureInfo.InvariantCulture))));
    }

    [Fact]
    public async Task Approving_into_a_full_pool_is_refused_and_nothing_moves()
    {
        var staff = await factory.SignInAsStaff();
        var request = await SeededPending(blocked: true);
        var id = request.GetProperty("id").GetInt32();
        var regId = request.GetProperty("registrationId").GetInt32();
        var (fromPool, before) = await PoolOf(regId);
        var toBefore = await ToPoolReserved(id);

        var res = await staff.PostAsJsonAsync($"/api/admin/transfers/{id}/approve", new { note = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Contains("full", (await Json(res)).GetProperty("error").GetString());

        Assert.Equal((fromPool, before), await PoolOf(regId));
        Assert.Equal(toBefore, await ToPoolReserved(id));
        Assert.Equal(TransferStatus.Pending, await factory.WithDb(db => db.Set<TransferRequest>().Where(t => t.Id == id).Select(t => t.Status).SingleAsync()));
    }

    [Fact]
    public async Task A_family_requests_a_transfer_and_cannot_touch_another_households_registration()
    {
        var (client, householdId, regId) = await NewFamilyRegisteredInWeekOne("Mover", PaymentOption.Deposit);
        var weekTwo = await WeekTwoId();

        var options = await Json(await client.GetAsync($"/api/transfers/{regId}/options"));
        var option = options.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("sessionId").GetInt32() == weekTwo);
        Assert.True(option.GetProperty("canMove").GetBoolean());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/transfers", new { registrationId = regId, toSessionId = weekTwo, reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/transfers", new { registrationId = regId, toSessionId = weekTwo, reason = "Family vacation moved." })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/transfers", new { registrationId = regId, toSessionId = weekTwo, reason = "Again" })).StatusCode);

        // Nothing moves until staff approve.
        Assert.NotEqual(weekTwo, await factory.WithDb(db => db.Registrations.Where(r => r.Id == regId).Select(r => r.SessionId).SingleAsync()));

        // Someone else's registration reads as not found, and their requests stay invisible.
        var otherReg = await factory.WithDb(db => db.Registrations.Where(r => r.HouseholdId != householdId && r.Status == RegistrationStatus.Confirmed).Select(r => r.Id).FirstAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/transfers/{otherReg}/options")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/transfers", new { registrationId = otherReg, toSessionId = weekTwo, reason = "Mine now" })).StatusCode);
        var mine = await Json(await client.GetAsync("/api/transfers"));
        Assert.All(mine.GetProperty("requests").EnumerateArray(), r => Assert.Equal(regId, r.GetProperty("registrationId").GetInt32()));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/transfers")).StatusCode);
    }

    [Fact]
    public async Task Deny_needs_a_reason_and_leaves_the_registration_intact()
    {
        var staff = await factory.SignInAsStaff();
        var (client, _, regId) = await NewFamilyRegisteredInWeekOne("Denied", PaymentOption.Deposit);
        var request = await Json(await client.PostAsJsonAsync("/api/transfers", new { registrationId = regId, toSessionId = await WeekTwoId(), reason = "Swim meet" }));
        var id = request.GetProperty("id").GetInt32();
        var before = await PoolOf(regId);

        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync($"/api/admin/transfers/{id}/deny", new { note = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync($"/api/admin/transfers/{id}/deny", new { note = "Week 2 is for returning campers only." })).StatusCode);

        Assert.Equal(before, await PoolOf(regId));
        var mine = await Json(await client.GetAsync("/api/transfers"));
        var row = Assert.Single(mine.GetProperty("requests").EnumerateArray());
        Assert.Equal("Denied", row.GetProperty("status").GetString());
        Assert.Equal("Week 2 is for returning campers only.", row.GetProperty("decisionNote").GetString());
    }

    [Fact]
    public async Task A_cheaper_session_refunds_the_overpayment()
    {
        var staff = await factory.SignInAsStaff();
        var cheaper = await CheaperSession(25000);
        var (client, _, regId) = await NewFamilyRegisteredInWeekOne("Refund", PaymentOption.Full);
        var refundsBefore = await factory.WithDb(db => db.PaymentOperations.CountAsync(o => o.Kind == PaymentKind.Refund && o.AmountCents == 7500));

        var request = await Json(await client.PostAsJsonAsync("/api/transfers", new { registrationId = regId, toSessionId = cheaper, reason = "Cheaper week" }));
        var res = await Json(await staff.PostAsJsonAsync($"/api/admin/transfers/{request.GetProperty("id").GetInt32()}/approve", new { note = "OK" }));
        Assert.Equal(7500, res.GetProperty("refundCents").GetInt32());

        var reg = await factory.WithDb(db => db.Registrations.AsNoTracking().SingleAsync(r => r.Id == regId));
        Assert.Equal(25000, reg.PriceCents);
        Assert.Equal(25000, reg.PaidCents);
        Assert.Equal(0, reg.BalanceCents);
        Assert.Equal(refundsBefore + 1, await factory.WithDb(db => db.PaymentOperations.CountAsync(o => o.Kind == PaymentKind.Refund && o.AmountCents == 7500)));
    }

    [Fact]
    public async Task Concurrent_approvals_move_the_seat_and_refund_exactly_once()
    {
        var cheaper = await CheaperSession(25000);
        var (client, _, regId) = await NewFamilyRegisteredInWeekOne("Race", PaymentOption.Full);
        var request = await Json(await client.PostAsJsonAsync("/api/transfers", new { registrationId = regId, toSessionId = cheaper, reason = "Cheaper week" }));
        var id = request.GetProperty("id").GetInt32();
        var (fromPool, fromBefore) = await PoolOf(regId);
        var toBefore = await ToPoolReserved(id);
        var orderId = await factory.WithDb(db => db.Registrations.Where(r => r.Id == regId).Select(r => r.OrderId!.Value).SingleAsync());

        var staff = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => factory.SignInAsStaff("cet", $"Racer {i}")));
        var results = await Task.WhenAll(staff.Select(s => s.PostAsJsonAsync($"/api/admin/transfers/{id}/approve", new { note = (string?)null })));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var (toPool, _) = await PoolOf(regId);
        await factory.WithDb(async db =>
        {
            Assert.Equal(fromBefore - 1, await db.CapacityPools.Where(p => p.Id == fromPool).Select(p => p.Reserved).SingleAsync());
            Assert.Equal(toBefore + 1, await db.CapacityPools.Where(p => p.Id == toPool).Select(p => p.Reserved).SingleAsync());
            Assert.Equal(1, await db.PaymentOperations.CountAsync(o => o.OrderId == orderId && o.Kind == PaymentKind.Refund));
            var regKey = regId.ToString(CultureInfo.InvariantCulture);
            Assert.Equal(1, await db.AuditEvents.CountAsync(a => a.Action == "registration.transferred" && a.EntityId == regKey));
            Assert.Equal(1, await db.OutboxEvents.CountAsync(e => e.Type == "RegistrationTransferred" && e.AggregateId == "reg-" + regKey));
            return 0;
        });
    }

    [Fact]
    public async Task Racing_approve_and_deny_settle_on_one_decision()
    {
        var (client, _, regId) = await NewFamilyRegisteredInWeekOne("Split", PaymentOption.Deposit);
        var request = await Json(await client.PostAsJsonAsync("/api/transfers", new { registrationId = regId, toSessionId = await WeekTwoId(), reason = "Either way" }));
        var id = request.GetProperty("id").GetInt32();
        var staff = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => factory.SignInAsStaff("cet", $"Racer {i}")));

        var results = await Task.WhenAll(staff.Select((s, i) => i % 2 == 0
            ? s.PostAsJsonAsync($"/api/admin/transfers/{id}/approve", new { note = (string?)null })
            : s.PostAsJsonAsync($"/api/admin/transfers/{id}/deny", new { note = "Week 2 is closed." })));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await factory.WithDb(db => db.AuditEvents.CountAsync(a => (a.Action == "transfer.approved" || a.Action == "transfer.denied") && a.Detail.Contains("Kid Split"))));
        var status = await factory.WithDb(db => db.Set<TransferRequest>().Where(t => t.Id == id).Select(t => t.Status).SingleAsync());
        var moved = await factory.WithDb(db => db.Registrations.AnyAsync(r => r.Id == regId && r.Session.Name == StaffCxSeed.WeekTwoName));
        Assert.Equal(status == TransferStatus.Approved, moved);
    }

    [Fact]
    public async Task Racing_discount_decisions_write_one_audit_row_and_one_event()
    {
        var cet = await Task.WhenAll(factory.SignInAsStaff(), factory.SignInAsStaff("cet", "Brian Hughes"));
        var finance = await Task.WhenAll(factory.SignInAsStaff("finance", "Marcus Lee"), factory.SignInAsStaff("finance", "Ana Ruiz"));
        var id = await RequestId("CHURCH20");
        var codeId = await factory.WithDb(db => db.Set<DiscountRequest>().Where(r => r.Id == id).Select(r => r.DiscountCodeId).SingleAsync());

        var results = await Task.WhenAll(
            finance[0].PostAsJsonAsync($"/api/admin/discounts/{id}/approve", new { note = "Staff kids, approved." }),
            cet[0].PostAsJsonAsync($"/api/admin/discounts/{id}/reject", new { note = "Doesn't stack." }),
            finance[1].PostAsJsonAsync($"/api/admin/discounts/{id}/approve", new { note = "Approved by budget." }),
            cet[1].PostAsJsonAsync($"/api/admin/discounts/{id}/reject", new { note = "Too generous." }));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var key = codeId.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(1, await factory.WithDb(db => db.AuditEvents.CountAsync(a => a.EntityType == "DiscountCode" && a.EntityId == key)));
        Assert.Equal(1, await factory.WithDb(db => db.OutboxEvents.CountAsync(e => e.AggregateId == "discount-" + key)));
    }

    [Fact]
    public async Task A_cheaper_session_on_a_plan_rebalances_the_installments()
    {
        var staff = await factory.SignInAsStaff();
        var cheaper = await CheaperSession(25000);
        var (client, _, regId) = await NewFamilyRegisteredInWeekOne("Plan", PaymentOption.Plan);

        var request = await Json(await client.PostAsJsonAsync("/api/transfers", new { registrationId = regId, toSessionId = cheaper, reason = "Cheaper week" }));
        var res = await Json(await staff.PostAsJsonAsync($"/api/admin/transfers/{request.GetProperty("id").GetInt32()}/approve", new { note = (string?)null }));
        Assert.Equal(0, res.GetProperty("refundCents").GetInt32());

        await factory.WithDb(async db =>
        {
            var reg = await db.Registrations.AsNoTracking().Include(r => r.Order!).ThenInclude(o => o.Installments).SingleAsync(r => r.Id == regId);
            Assert.Equal(15000, reg.BalanceCents); // $250 − $100 deposit
            Assert.Equal(reg.BalanceCents, reg.Order!.Installments.Where(i => i.Status == InstallmentStatus.Scheduled).Sum(i => i.AmountCents));
            return 0;
        });
    }

    [Fact]
    public async Task A_percent_discount_is_worked_out_again_on_the_new_price()
    {
        var code = $"PCT{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        await factory.WithDb(async db =>
        {
            db.DiscountCodes.Add(new DiscountCode { Code = code, Kind = DiscountKind.Percent, Value = 10, Status = DiscountStatus.Approved, CreatedBy = "test" });
            return await db.SaveChangesAsync();
        });
        var pricier = await CheaperSession(40000);
        var (client, _, regId) = await NewFamilyRegisteredInWeekOne("Percent", PaymentOption.Full, code);
        Assert.Equal(3250, await factory.WithDb(db => db.Registrations.Where(r => r.Id == regId).Select(r => r.DiscountCents).SingleAsync()));

        var request = await Json(await client.PostAsJsonAsync("/api/transfers", new { registrationId = regId, toSessionId = pricier, reason = "Later week" }));
        var staff = await factory.SignInAsStaff();
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync($"/api/admin/transfers/{request.GetProperty("id").GetInt32()}/approve", new { note = (string?)null })).StatusCode);

        await factory.WithDb(async db =>
        {
            var reg = await db.Registrations.AsNoTracking().Include(r => r.Order).SingleAsync(r => r.Id == regId);
            Assert.Equal(40000, reg.PriceCents);
            Assert.Equal(4000, reg.DiscountCents); // 10% of $400, not the $32.50 from the old price
            Assert.Equal(40000 - 4000 - 29250, reg.BalanceCents);
            Assert.Equal(4000, reg.Order!.DiscountCents);
            Assert.Equal(36000, reg.Order.TotalCents);
            return 0;
        });
    }

    [Fact]
    public async Task A_session_scoped_code_stays_behind_and_a_scholarship_moves_with_the_camper()
    {
        // Wave 2: K5 rules (setup) and O7 awards (finance) both feed Registration.DiscountCents.
        var code = $"WK1{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var (weekOne, _) = await WeekOneAndAvery();
        await factory.WithDb(async db =>
        {
            var d = new DiscountCode { Code = code, Kind = DiscountKind.Percent, Value = 10, Status = DiscountStatus.Approved, CreatedBy = "test" };
            db.DiscountCodes.Add(d);
            db.Set<Camp.Api.Features.Setup.DiscountRule>().Add(new Camp.Api.Features.Setup.DiscountRule
            {
                DiscountCode = d,
                Name = "June week only",
                SessionId = weekOne,
                CreatedBy = "test",
                CreatedAt = factory.Clock.UtcNow(),
                ValidFrom = factory.Clock.Today().AddDays(-1),
                ValidTo = factory.Clock.Today().AddDays(30),
            });
            return await db.SaveChangesAsync();
        });
        var pricier = await CheaperSession(40000);
        var (client, householdId, regId) = await NewFamilyRegisteredInWeekOne("Scoped", PaymentOption.Full, code);
        Assert.Equal(3250, await factory.WithDb(db => db.Registrations.Where(r => r.Id == regId).Select(r => r.DiscountCents).SingleAsync()));
        await factory.WithDb(async db =>
        {
            var reg = await db.Registrations.SingleAsync(r => r.Id == regId);
            var app = new Camp.Api.Features.Finance.ScholarshipApplication
            {
                HouseholdId = householdId,
                OrderId = reg.OrderId!.Value,
                SubmittedBy = "test",
                SubmittedAt = factory.Clock.UtcNow(),
                RequestedCents = 5000,
                IncomeBand = "test",
                Reason = "test",
                Status = Camp.Api.Features.Finance.ScholarshipStatus.Approved,
                AwardCents = 5000,
            };
            app.Lines.Add(new Camp.Api.Features.Finance.ScholarshipAwardLine { RegistrationId = reg.Id, AmountCents = 5000 });
            db.Add(app);
            reg.DiscountCents += 5000;
            return await db.SaveChangesAsync();
        });

        var request = await Json(await client.PostAsJsonAsync("/api/transfers", new { registrationId = regId, toSessionId = pricier, reason = "Later week" }));
        var staff = await factory.SignInAsStaff();
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync($"/api/admin/transfers/{request.GetProperty("id").GetInt32()}/approve", new { note = (string?)null })).StatusCode);

        var discount = await factory.WithDb(db => db.Registrations.Where(r => r.Id == regId).Select(r => r.DiscountCents).SingleAsync());
        Assert.Equal(5000, discount); // the June-week-only 10% is gone; the $50 award is kept
    }

    [Fact]
    public async Task Rebalancing_leaves_failed_installments_out_of_the_scheduled_ones()
    {
        var staff = await factory.SignInAsStaff();
        var cheaper = await CheaperSession(25000);
        var (client, _, regId) = await NewFamilyRegisteredInWeekOne("Failed", PaymentOption.Plan);
        var failed = await factory.WithDb(async db =>
        {
            var orderId = await db.Registrations.Where(r => r.Id == regId).Select(r => r.OrderId).SingleAsync();
            var first = await db.Installments.Where(i => i.OrderId == orderId && i.Status == InstallmentStatus.Scheduled).OrderBy(i => i.Sequence).FirstAsync();
            first.Status = InstallmentStatus.Failed;
            await db.SaveChangesAsync();
            return first.AmountCents;
        });

        var request = await Json(await client.PostAsJsonAsync("/api/transfers", new { registrationId = regId, toSessionId = cheaper, reason = "Cheaper week" }));
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync($"/api/admin/transfers/{request.GetProperty("id").GetInt32()}/approve", new { note = (string?)null })).StatusCode);

        await factory.WithDb(async db =>
        {
            var reg = await db.Registrations.AsNoTracking().Include(r => r.Order!).ThenInclude(o => o.Installments).SingleAsync(r => r.Id == regId);
            Assert.Equal(failed, reg.Order!.Installments.Where(i => i.Status == InstallmentStatus.Failed).Sum(i => i.AmountCents));
            Assert.Equal(reg.BalanceCents - failed, reg.Order.Installments.Where(i => i.Status == InstallmentStatus.Scheduled).Sum(i => i.AmountCents));
            return 0;
        });
    }

    [Fact]
    public async Task Two_identical_transfer_requests_at_once_give_one_request_and_a_409()
    {
        var (client, _, regId) = await NewFamilyRegisteredInWeekOne("Twice", PaymentOption.Deposit);
        var weekTwo = await WeekTwoId();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync("/api/transfers", new { registrationId = regId, toSessionId = weekTwo, reason = "Double click" })));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1, await factory.WithDb(db => db.Set<TransferRequest>().CountAsync(t => t.RegistrationId == regId)));
    }

    [Fact]
    public async Task Transfer_requests_from_two_devices_at_the_same_moment_give_one_request_and_409s()
    {
        var (client, householdId, regId) = await NewFamilyRegisteredInWeekOne("Devices", PaymentOption.Deposit);
        var email = await factory.WithDb(db => db.Households.Where(h => h.Id == householdId).Select(h => h.Email).SingleAsync());
        using var phone = await factory.SignInAsFamily(email, "Pat", "Devices");
        var weekTwo = await WeekTwoId();

        for (var round = 0; round < 3; round++)
        {
            await factory.WithDb(db => db.Set<TransferRequest>().Where(t => t.RegistrationId == regId).ExecuteDeleteAsync());
            var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => (i % 2 == 0 ? client : phone)
                .PostAsJsonAsync("/api/transfers", new { registrationId = regId, toSessionId = weekTwo, reason = $"Tab {i}" })));

            Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
            foreach (var r in results.Where(r => r.StatusCode != HttpStatusCode.OK))
            {
                Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
                Assert.Contains("already a transfer request waiting for review", await r.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task A_full_destination_marks_the_pool_requirement_blocked()
    {
        var staff = await factory.SignInAsStaff();
        var request = await SeededPending(blocked: true);
        var detail = await Json(await staff.GetAsync($"/api/admin/transfers/{request.GetProperty("id").GetInt32()}"));
        var pool = detail.GetProperty("check").GetProperty("requirements").EnumerateArray().Single(r => r.GetProperty("label").GetString() == "Grade and pool");
        Assert.Equal("Blocked", pool.GetProperty("state").GetString());
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        return JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text).RootElement;
    }

    Task<int> RequestId(string code) =>
        factory.WithDb(db => db.Set<DiscountRequest>().Where(r => r.DiscountCode.Code == code).Select(r => r.Id).SingleAsync());

    Task<(int SessionId, int Avery)> WeekOneAndAvery() => factory.WithDb(async db => (
        await db.Sessions.Where(s => s.Program.Slug == "day-camp-atlanta").OrderBy(s => s.Id).Select(s => s.Id).FirstAsync(),
        await db.People.Where(p => p.Household.Email == Seed.JohnsonEmail && p.FirstName == "Avery").Select(p => p.Id).SingleAsync()));

    Task<int> WeekTwoId() => factory.WithDb(db => db.Sessions.Where(s => s.Name == StaffCxSeed.WeekTwoName).Select(s => s.Id).SingleAsync());

    Task<(int PoolId, int Reserved)> PoolOf(int registrationId) => factory.WithDb(async db =>
    {
        var pool = await db.Registrations.Where(r => r.Id == registrationId).Select(r => new { r.PoolId, r.Pool.Reserved }).SingleAsync();
        return (pool.PoolId, pool.Reserved);
    });

    /// <summary>Reserved seats in the pool a request's camper would land in.</summary>
    Task<int> ToPoolReserved(int requestId) => factory.WithDb(async db =>
    {
        var t = await db.Set<TransferRequest>().Include(x => x.Registration).ThenInclude(r => r.Person).Include(x => x.ToSession).ThenInclude(s => s.Pools).SingleAsync(x => x.Id == requestId);
        return Eligibility.FindPool(t.Registration.Person, t.ToSession).Pool!.Reserved;
    });

    async Task<JsonElement> SeededPending(bool blocked)
    {
        var staff = await factory.SignInAsStaff();
        var queue = await Json(await staff.GetAsync("/api/admin/transfers?status=pending"));
        return queue.GetProperty("rows").EnumerateArray().First(r => r.GetProperty("blocked").GetBoolean() == blocked && !r.GetProperty("participant").GetString()!.StartsWith("Kid ", StringComparison.Ordinal));
    }

    Task<int> CheaperSession(int priceCents) => factory.WithDb(async db =>
    {
        var template = await db.Sessions.AsNoTracking().Where(s => s.Program.Slug == "day-camp-atlanta").OrderBy(s => s.Id).FirstAsync();
        var session = new Session
        {
            ProgramId = template.ProgramId,
            Name = $"Cheap {Guid.NewGuid():N}"[..20],
            StartDate = template.StartDate.AddDays(28),
            EndDate = template.EndDate.AddDays(28),
            PriceCents = priceCents,
            DepositCents = template.DepositCents,
            PlanInstallments = template.PlanInstallments,
            BalanceDueDate = template.BalanceDueDate,
        };
        db.CapacityPools.Add(new CapacityPool { Session = session, Name = "Grade 6", GradeMin = 6, GradeMax = 6, Capacity = 5 });
        await db.SaveChangesAsync();
        return session.Id;
    });

    /// <summary>A brand-new signed-in family whose Grade 6 camper is confirmed in Day Camp week 1.</summary>
    async Task<(HttpClient Client, int HouseholdId, int RegistrationId)> NewFamilyRegisteredInWeekOne(string name, PaymentOption option, string? discountCode = null)
    {
        var email = $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com";
        var client = await factory.SignInAsFamily(email, "Pat", name);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampDbContext>();
        var household = await db.Households.SingleAsync(h => h.Email == email);
        var kid = new Person { HouseholdId = household.Id, FirstName = "Kid", LastName = name, DateOfBirth = new(2017, 3, 4), Gender = Gender.Male };
        db.People.Add(kid);
        await db.SaveChangesAsync();

        var (sessionId, _) = await WeekOneAndAvery();
        var programId = await db.Sessions.Where(s => s.Id == sessionId).Select(s => s.ProgramId).SingleAsync();
        var waivers = await db.WaiverTemplates.Where(w => w.ProgramId == programId).ToListAsync();
        var req = new CheckoutRequest(
            $"staffcx-{Guid.NewGuid():N}", sessionId,
            [new CheckoutParticipant(kid.Id, new() { ["tshirt"] = "Youth M", ["swim"] = "Beginner" }, new HealthForm(null, null, null, null, "Dr. Test", "555-0100", null))],
            new() { ["church"] = "No" },
            waivers.Select(w => new WaiverSignature(w.Id, w.PerParticipant ? kid.Id : null, "Pat")).ToList(),
            option, discountCode, _gateway.Tokenize("4242424242424242"));
        var result = await scope.ServiceProvider.GetRequiredService<CheckoutService>().CheckoutAsync(household.Id, "test", req, default);
        Assert.Equal(OrderStatus.Paid, result.Status);
        var regId = await db.Registrations.Where(r => r.PersonId == kid.Id && r.SessionId == sessionId).Select(r => r.Id).SingleAsync();
        return (client, household.Id, regId);
    }
}
