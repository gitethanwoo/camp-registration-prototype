using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Camp.Api.Auth;
using Camp.Api.Domain;
using Camp.Api.Features.Host;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Camp.Api.Tests;

/// <summary>Helpers shared by the host portal test classes.</summary>
internal static class HostTesting
{
    /// <summary>Signs in as a host-role staff member with a specific email (the membership is looked up by email).</summary>
    public static async Task<HttpClient> SignInAsHost(this ApiFactory factory, string email = HostSeed.GraceEmail, string name = "Grace Patel")
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });
        using var res = await client.PostAsJsonAsync("/api/auth/dev-login", new DevLoginRequest(email, name.Split(' ')[0], name.Split(' ')[^1], "host"));
        res.EnsureSuccessStatusCode();
        return client;
    }

    public static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public static async Task<string> Error(HttpResponseMessage res)
    {
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        if (body.TryGetProperty("error", out var e)) return e.GetString() ?? "";
        return string.Join(" ", body.GetProperty("errors").EnumerateObject().SelectMany(p => p.Value.EnumerateArray()).Select(v => v.GetString()));
    }

    /// <summary>The demo file shipped for the e2e spec: 40 rows, 37 valid, 3 with errors.</summary>
    public static string DemoCsv()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "e2e", "fixtures", "grace-volunteers.csv"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir.FullName, "e2e", "fixtures", "grace-volunteers.csv"));
    }

    public static async Task<string> Token(HttpClient client, string card)
    {
        var body = await Json(await client.PostAsJsonAsync("/api/fiserv-sandbox/tokenize", new { cardNumber = card }));
        return body.GetProperty("token").GetString() ?? "";
    }
}

/// <summary>Slice 8: host portal access, the host home, isolation between churches, and invoice payment.</summary>
public class HostTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    readonly FakeFiservGateway _gateway = (FakeFiservGateway)factory.Services.GetRequiredService<IPaymentGateway>();

    [Theory]
    [InlineData("/api/host/me")]
    [InlineData("/api/host/overview")]
    [InlineData("/api/host/volunteers")]
    [InlineData("/api/host/uploads/latest")]
    [InlineData("/api/host/invoices")]
    public async Task Host_routes_reject_anonymous_family_and_other_staff(string url)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsFamily()).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsStaff("cet")).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.SignInAsStaff("finance", "Marcus Lee")).GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task A_host_with_no_church_is_refused_with_a_reason()
    {
        var stranger = await factory.SignInAsHost("new.host@winshape.example", "New Host");
        var res = await stranger.GetAsync("/api/host/overview");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Contains("isn't linked to a host church", await HostTesting.Error(res));
    }

    [Fact]
    public async Task A_host_is_kept_out_of_the_staff_console()
    {
        var grace = await factory.SignInAsHost();
        Assert.Equal(HttpStatusCode.Forbidden, (await grace.GetAsync("/api/admin/discounts")).StatusCode);
    }

    [Fact]
    public async Task Host_home_counts_real_registrations_volunteers_and_the_open_invoice()
    {
        var grace = await factory.SignInAsHost();
        var me = await HostTesting.Json(await grace.GetAsync("/api/host/me"));
        Assert.Equal(HostSeed.GraceChurch, me.GetProperty("organization").GetString());
        Assert.Equal("Operations Director", me.GetProperty("title").GetString());

        var body = await HostTesting.Json(await grace.GetAsync("/api/host/overview"));
        var ev = body.GetProperty("event");
        var expected = await factory.WithDb(async db =>
        {
            var sessionId = await db.Set<HostEvent>().Select(e => e.SessionId).SingleAsync();
            return (
                Registered: await db.Registrations.CountAsync(r => r.SessionId == sessionId && r.Status != RegistrationStatus.Cancelled && r.Status != RegistrationStatus.Waitlisted && r.Status != RegistrationStatus.Draft && r.Status != RegistrationStatus.Transferred),
                Capacity: await db.CapacityPools.Where(p => p.SessionId == sessionId).SumAsync(p => p.Capacity),
                Volunteers: await db.Set<HostVolunteer>().CountAsync(v => db.Set<HostOrganization>().Any(o => o.Id == v.HostOrganizationId && o.Name == HostSeed.GraceChurch)));
        });
        Assert.Equal(expected.Registered, ev.GetProperty("registrations").GetInt32());
        Assert.True(expected.Registered > 0);
        Assert.Equal(120, ev.GetProperty("capacity").GetInt32());
        Assert.Equal(expected.Capacity, ev.GetProperty("capacity").GetInt32());
        Assert.Equal(88, ev.GetProperty("lastYear").GetInt32());
        Assert.Equal("2028-06-12", ev.GetProperty("startDate").GetString());

        var v = body.GetProperty("volunteers");
        Assert.Equal(v.GetProperty("total").GetInt32(), v.GetProperty("approved").GetInt32() + v.GetProperty("inProgress").GetInt32() + v.GetProperty("notStarted").GetInt32());

        var deadlines = body.GetProperty("deadlines").EnumerateArray().ToList();
        Assert.Contains(deadlines, d => d.GetProperty("title").GetString() == "Vetting done by May 15, 2028");
        Assert.Contains(deadlines, d => d.GetProperty("title").GetString() == $"Pay {HostSeed.OpenInvoice} by May 20, 2028"
            && d.GetProperty("detail").GetString() == "$1,200 balance due for Day Camp.");
    }

    [Fact]
    public async Task One_church_never_sees_another_churchs_volunteers_or_invoices()
    {
        var grace = await factory.SignInAsHost();
        var riverside = await factory.SignInAsHost(HostSeed.RiversideEmail, "Riverside Host");

        var graceVolunteers = await HostTesting.Json(await grace.GetAsync("/api/host/volunteers"));
        var riversideVolunteers = await HostTesting.Json(await riverside.GetAsync("/api/host/volunteers"));
        Assert.DoesNotContain(graceVolunteers.GetProperty("rows").EnumerateArray(), r => r.GetProperty("email").GetString() == "elena.brooks@example.org");
        Assert.Contains(riversideVolunteers.GetProperty("rows").EnumerateArray(), r => r.GetProperty("email").GetString() == "elena.brooks@example.org");
        Assert.DoesNotContain(riversideVolunteers.GetProperty("rows").EnumerateArray(), r => r.GetProperty("email").GetString() == "morgan.lee@example.org");

        var riversideInvoices = (await HostTesting.Json(await riverside.GetAsync("/api/host/invoices"))).EnumerateArray().ToList();
        Assert.All(riversideInvoices, i => Assert.StartsWith("INV-2028-052", i.GetProperty("number").GetString()));
        var riversideOverview = await HostTesting.Json(await riverside.GetAsync("/api/host/overview"));
        Assert.Equal(JsonValueKind.Null, riversideOverview.GetProperty("event").ValueKind);

        var graceInvoiceId = await factory.WithDb(db => db.Set<HostInvoice>().Where(i => i.Number == HostSeed.OpenInvoice).Select(i => i.Id).SingleAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await riverside.GetAsync($"/api/host/invoices/{graceInvoiceId}")).StatusCode);
        var token = await HostTesting.Token(riverside, "4242 4242 4242 4242");
        var pay = await riverside.PostAsJsonAsync($"/api/host/invoices/{graceInvoiceId}/pay", new { idempotencyKey = Guid.NewGuid().ToString(), cardToken = token });
        Assert.Equal(HttpStatusCode.NotFound, pay.StatusCode);
    }

    [Fact]
    public async Task Invoice_detail_lines_add_up_to_the_total()
    {
        var grace = await factory.SignInAsHost();
        var list = (await HostTesting.Json(await grace.GetAsync("/api/host/invoices"))).EnumerateArray().ToList();
        Assert.Contains(list, i => i.GetProperty("number").GetString() == "INV-2027-039" && i.GetProperty("status").GetString() == "Paid");
        var open = list.Single(i => i.GetProperty("number").GetString() == HostSeed.OpenInvoice);
        var detail = await HostTesting.Json(await grace.GetAsync($"/api/host/invoices/{open.GetProperty("id").GetInt32()}"));
        var lines = detail.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("amountCents").GetInt32()).ToList();
        Assert.Equal([80000, 40000], lines);
        Assert.Equal(lines.Sum(), detail.GetProperty("invoice").GetProperty("totalCents").GetInt32());
        Assert.Equal("Day Camp · Atlanta", detail.GetProperty("event").GetProperty("program").GetString());
    }

    [Fact]
    public async Task A_declined_card_leaves_the_invoice_due_and_a_good_card_pays_it_once()
    {
        var (id, number) = await NewInvoice(HostSeed.GraceChurch, 50000, 25000);
        var grace = await factory.SignInAsHost();

        var declined = await grace.PostAsJsonAsync($"/api/host/invoices/{id}/pay", new { idempotencyKey = Guid.NewGuid().ToString(), cardToken = await HostTesting.Token(grace, "4000 0000 0000 0002") });
        Assert.Equal(HttpStatusCode.PaymentRequired, declined.StatusCode);
        var afterDecline = await HostTesting.Json(await grace.GetAsync($"/api/host/invoices/{id}"));
        Assert.Equal("Balance due", afterDecline.GetProperty("invoice").GetProperty("status").GetString());
        Assert.Equal(75000, afterDecline.GetProperty("invoice").GetProperty("balanceCents").GetInt32());
        Assert.Contains(afterDecline.GetProperty("payments").EnumerateArray(), p => p.GetProperty("status").GetString() == "Declined");

        var charges = _gateway.ChargeCount;
        var key = Guid.NewGuid().ToString();
        var token = await HostTesting.Token(grace, "4242 4242 4242 4242");
        var paid = await HostTesting.Json(await grace.PostAsJsonAsync($"/api/host/invoices/{id}/pay", new { idempotencyKey = key, cardToken = token }));
        Assert.Equal(75000, paid.GetProperty("amountCents").GetInt32());
        // The same key replays the first result instead of charging again.
        var replay = await HostTesting.Json(await grace.PostAsJsonAsync($"/api/host/invoices/{id}/pay", new { idempotencyKey = key, cardToken = token }));
        Assert.Equal("Succeeded", replay.GetProperty("outcome").GetString());
        Assert.Equal(charges + 1, _gateway.ChargeCount);

        var again = await grace.PostAsJsonAsync($"/api/host/invoices/{id}/pay", new { idempotencyKey = Guid.NewGuid().ToString(), cardToken = token });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already paid", await HostTesting.Error(again));

        var detail = await HostTesting.Json(await grace.GetAsync($"/api/host/invoices/{id}"));
        Assert.Equal("Paid", detail.GetProperty("invoice").GetProperty("status").GetString());
        Assert.Equal(0, detail.GetProperty("invoice").GetProperty("balanceCents").GetInt32());
        var audits = await factory.WithDb(db => db.AuditEvents.Where(a => a.EntityId == number).Select(a => a.Action).ToListAsync());
        Assert.Contains("host.invoice_paid", audits);
        Assert.Contains("host.invoice_payment_declined", audits);
        Assert.Equal(1, await factory.WithDb(db => db.OutboxEvents.CountAsync(o => o.Type == "HostInvoicePaid" && o.AggregateId == number)));
    }

    [Fact]
    public async Task Concurrent_payments_charge_the_invoice_exactly_once()
    {
        var (id, number) = await NewInvoice(HostSeed.GraceChurch, 30000);
        var grace = await factory.SignInAsHost();
        var tokens = new List<string>();
        for (var i = 0; i < 6; i++) tokens.Add(await HostTesting.Token(grace, "4242 4242 4242 4242"));
        var charges = _gateway.ChargeCount;

        var results = await Task.WhenAll(tokens.Select(t => grace.PostAsJsonAsync($"/api/host/invoices/{id}/pay", new { idempotencyKey = Guid.NewGuid().ToString(), cardToken = t })));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(charges + 1, _gateway.ChargeCount);
        var paid = await factory.WithDb(db => db.Set<HostInvoicePayment>()
            .Where(p => db.Set<HostInvoice>().Any(i => i.Id == p.InvoiceId && i.Number == number) && p.Status == HostPaymentStatus.Succeeded).SumAsync(p => p.AmountCents));
        Assert.Equal(30000, paid);
    }

    [Fact]
    public async Task Adding_one_volunteer_checks_the_same_rules_as_the_csv()
    {
        var grace = await factory.SignInAsHost();
        var bad = await grace.PostAsJsonAsync("/api/host/volunteers", new { firstName = "Tim", lastName = "Young", email = "tim.young@example.org", dateOfBirth = "03/14/2012", role = "Games" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("18 or older on June 12, 2028", await HostTesting.Error(bad));

        var ok = await grace.PostAsJsonAsync("/api/host/volunteers", new { firstName = "Tim", lastName = "Young", email = "tim.young@example.org", dateOfBirth = "1990-03-14", role = "games" });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var list = await HostTesting.Json(await grace.GetAsync("/api/host/volunteers"));
        var tim = list.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("email").GetString() == "tim.young@example.org");
        Assert.Equal("NotStarted", tim.GetProperty("vettingStatus").GetString());
        Assert.Equal("Games", tim.GetProperty("role").GetString());

        var dup = await grace.PostAsJsonAsync("/api/host/volunteers", new { firstName = "Timothy", lastName = "Young", email = "TIM.YOUNG@example.org", dateOfBirth = "1990-03-14" });
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        Assert.Contains("Duplicate email", await HostTesting.Error(dup));
    }

    async Task<(int Id, string Number)> NewInvoice(string church, params int[] lines)
    {
        var number = $"INV-T-{Guid.NewGuid():N}"[..16];
        var id = await factory.WithDb(async db =>
        {
            var org = await db.Set<HostOrganization>().SingleAsync(o => o.Name == church);
            var invoice = new HostInvoice { HostOrganizationId = org.Id, Number = number, Description = "Test", Period = "June 2028", IssuedOn = new(2028, 1, 1), DueDate = new(2028, 5, 1) };
            invoice.Lines.AddRange(lines.Select((c, i) => new HostInvoiceLine { Description = $"Line {i + 1}", AmountCents = c, SortOrder = i }));
            db.Add(invoice);
            await db.SaveChangesAsync();
            return invoice.Id;
        });
        return (id, number);
    }
}

/// <summary>Slice 8: the H2 CSV batch upload. Nothing is dropped, and only valid rows reach vetting.</summary>
public class HostUploadTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task The_demo_file_previews_37_valid_and_3_errors_then_fixed_and_valid_rows_go_to_vetting()
    {
        var grace = await factory.SignInAsHost();
        var before = await factory.WithDb(db => db.Set<HostVolunteer>().CountAsync(v => db.Set<HostOrganization>().Any(o => o.Id == v.HostOrganizationId && o.Name == HostSeed.GraceChurch)));

        var preview = await HostTesting.Json(await grace.PostAsJsonAsync("/api/host/uploads", new { fileName = "grace-volunteers.csv", content = HostTesting.DemoCsv() }));
        Assert.Equal(40, preview.GetProperty("total").GetInt32());
        Assert.Equal(37, preview.GetProperty("valid").GetInt32());
        Assert.Equal(3, preview.GetProperty("errors").GetInt32());
        var errors = preview.GetProperty("rows").EnumerateArray().Where(r => r.GetProperty("status").GetString() == "Error")
            .ToDictionary(r => r.GetProperty("rowNumber").GetInt32(), r => r);
        Assert.Equal([8, 17, 32], errors.Keys.Order());
        Assert.Equal("Missing email", errors[8].GetProperty("issue").GetString());
        Assert.Equal("Invalid date of birth", errors[17].GetProperty("issue").GetString());
        Assert.Equal("Duplicate email", errors[32].GetProperty("issue").GetString());
        Assert.Equal("This email already exists in your volunteer list.", errors[32].GetProperty("detail").GetString());
        // Uploading is not submitting: no volunteers yet.
        Assert.Equal(before, await factory.WithDb(db => db.Set<HostVolunteer>().CountAsync(v => db.Set<HostOrganization>().Any(o => o.Id == v.HostOrganizationId && o.Name == HostSeed.GraceChurch))));

        // A second file can't hide these rows.
        var second = await grace.PostAsJsonAsync("/api/host/uploads", new { fileName = "more.csv", content = HostTesting.DemoCsv() });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("Finish your open upload first", await HostTesting.Error(second));

        var uploadId = preview.GetProperty("id").GetInt32();
        var row8 = errors[8];
        var fixedStill = await HostTesting.Json(await grace.PutAsJsonAsync($"/api/host/uploads/{uploadId}/rows/{row8.GetProperty("id").GetInt32()}",
            new { firstName = "Chris", lastName = "Walker", email = "not-an-email", phone = "", dateOfBirth = "09/09/1983", role = "Group leader" }));
        Assert.Equal("Invalid email", Row(fixedStill, 8).GetProperty("issue").GetString());
        var fixedRow = await HostTesting.Json(await grace.PutAsJsonAsync($"/api/host/uploads/{uploadId}/rows/{row8.GetProperty("id").GetInt32()}",
            new { firstName = "Chris", lastName = "Walker", email = "chris.walker@example.org", phone = "", dateOfBirth = "09/09/1983", role = "Group leader" }));
        Assert.Equal("Valid", Row(fixedRow, 8).GetProperty("status").GetString());
        Assert.Equal(38, fixedRow.GetProperty("valid").GetInt32());

        var skipped = await HostTesting.Json(await grace.PostAsync($"/api/host/uploads/{uploadId}/rows/{errors[17].GetProperty("id").GetInt32()}/skip", null));
        Assert.Equal("Skipped", Row(skipped, 17).GetProperty("status").GetString());
        Assert.Equal(40, skipped.GetProperty("total").GetInt32());

        var submit = await HostTesting.Json(await grace.PostAsync($"/api/host/uploads/{uploadId}/submit", null));
        Assert.Equal(38, submit.GetProperty("submitted").GetInt32());
        Assert.Equal(1, submit.GetProperty("heldBack").GetInt32());
        var upload = submit.GetProperty("upload");
        Assert.Equal(40, upload.GetProperty("submitted").GetInt32() + upload.GetProperty("errors").GetInt32() + upload.GetProperty("skipped").GetInt32());
        Assert.Equal("Error", Row(upload, 32).GetProperty("status").GetString());

        var (count, notStarted, outbox, audit) = await factory.WithDb(async db => (
            await db.Set<HostVolunteer>().CountAsync(v => db.Set<HostOrganization>().Any(o => o.Id == v.HostOrganizationId && o.Name == HostSeed.GraceChurch)),
            await db.Set<HostVolunteer>().CountAsync(v => v.UploadRowId != null && v.VettingStatus == VettingStatus.NotStarted),
            await db.OutboxEvents.CountAsync(o => o.Type == "VolunteerVettingRequested" && o.PayloadJson.Contains(HostSeed.GraceChurch)),
            await db.AuditEvents.Where(a => a.Action.StartsWith("host.")).Select(a => a.Action).ToListAsync()));
        Assert.Equal(before + 38, count);
        Assert.Equal(38, notStarted);
        Assert.Equal(38, outbox);
        Assert.Contains("host.volunteers_uploaded", audit);
        Assert.Contains("host.upload_row_fixed", audit);
        Assert.Contains("host.upload_row_skipped", audit);
        Assert.Contains("host.volunteers_submitted", audit);

        // Submitting again sends nothing twice; the held-back row is still there to fix.
        var again = await grace.PostAsync($"/api/host/uploads/{uploadId}/submit", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("Fix or skip the 1 row", await HostTesting.Error(again));
        var changeSubmitted = await grace.PostAsync($"/api/host/uploads/{uploadId}/rows/{row8.GetProperty("id").GetInt32()}/skip", null);
        Assert.Equal(HttpStatusCode.Conflict, changeSubmitted.StatusCode);

        var overview = await HostTesting.Json(await grace.GetAsync("/api/host/overview"));
        Assert.Contains(overview.GetProperty("deadlines").EnumerateArray(), d => d.GetProperty("title").GetString() == "Fix 1 volunteer CSV row");

        // Bringing back the skipped row puts it back under review, with its original problem.
        var restored = await HostTesting.Json(await grace.PostAsync($"/api/host/uploads/{uploadId}/rows/{errors[17].GetProperty("id").GetInt32()}/restore", null));
        Assert.Equal("Invalid date of birth", Row(restored, 17).GetProperty("issue").GetString());
        Assert.Equal(2, restored.GetProperty("errors").GetInt32());
    }

    [Fact]
    public async Task Duplicates_within_a_file_and_bad_files_are_reported_not_dropped()
    {
        var riverside = await factory.SignInAsHost(HostSeed.RiversideEmail, "Riverside Host");
        var bad = await riverside.PostAsJsonAsync("/api/host/uploads", new { fileName = "x.csv", content = "name,email\nA,a@example.org\n" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("Missing: first_name, last_name, date_of_birth", await HostTesting.Error(bad));

        const string csv = "First Name,Last Name,Email,Phone,Date of Birth,Role\r\n" +
            "Ana,Diaz,ana.diaz@example.org,,1988-02-11,Kitchen\r\n" +
            "\"Diaz, Jr.\",Luis,ANA.DIAZ@example.org,,1990-05-01,\r\n" +
            "\r\n" +
            "Kai,Nguyen,kai.nguyen@example.org,,2015-01-01,Games\r\n" +
            "Bo,Stone,bo.stone@example.org,,1979-07-07,Lifeguard\r\n";
        var preview = await HostTesting.Json(await riverside.PostAsJsonAsync("/api/host/uploads", new { fileName = "../../riverside.csv", content = csv }));
        Assert.Equal("riverside.csv", preview.GetProperty("fileName").GetString());
        Assert.Equal(4, preview.GetProperty("total").GetInt32());
        Assert.Equal("Diaz, Jr.", Row(preview, 2).GetProperty("firstName").GetString());
        Assert.Equal("Same email as row 1. Each volunteer needs their own email.", Row(preview, 2).GetProperty("detail").GetString());
        Assert.Equal("Under 18", Row(preview, 3).GetProperty("issue").GetString());
        Assert.Equal("Unknown role", Row(preview, 4).GetProperty("issue").GetString());

        // A volunteer added by hand after the preview is caught again at Submit.
        Assert.Equal(HttpStatusCode.Created, (await riverside.PostAsJsonAsync("/api/host/volunteers",
            new { firstName = "Ana", lastName = "Diaz", email = "ana.diaz@example.org", dateOfBirth = "1988-02-11" })).StatusCode);
        var submit = await riverside.PostAsync($"/api/host/uploads/{preview.GetProperty("id").GetInt32()}/submit", null);
        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);
        var latest = await HostTesting.Json(await riverside.GetAsync("/api/host/uploads/latest"));
        Assert.Equal("Duplicate email", Row(latest, 1).GetProperty("issue").GetString());
        Assert.Equal(1, await factory.WithDb(db => db.Set<HostVolunteer>().CountAsync(v => v.Email == "ana.diaz@example.org")));

        // Grace can't touch Riverside's upload.
        var grace = await factory.SignInAsHost();
        var rowId = Row(latest, 1).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NotFound, (await grace.PostAsync($"/api/host/uploads/{preview.GetProperty("id").GetInt32()}/rows/{rowId}/skip", null)).StatusCode);
    }

    [Fact]
    public void Csv_parsing_handles_quotes_blank_lines_and_a_byte_order_mark()
    {
        var (rows, error) = VolunteerCsv.Parse("﻿first_name,last_name,email,date_of_birth\n\"O\"\"Neil\",\"Smith\nJones\",x@example.org,01/02/1990\n\n");
        Assert.Null(error);
        var row = Assert.Single(rows);
        Assert.Equal("O\"Neil", row.FirstName);
        Assert.Equal("Smith\nJones", row.LastName);
        Assert.Equal("", row.Role);
        Assert.Equal("That file is empty. Download the template and add one volunteer per row.", VolunteerCsv.Parse("").Error);
        Assert.NotNull(VolunteerCsv.Parse("first_name,last_name,email,date_of_birth\n").Error);
        Assert.Null(VolunteerCsv.ParseDate("02/30/1991"));
        Assert.Equal(new DateOnly(1991, 2, 28), VolunteerCsv.ParseDate("2/28/1991"));
    }

    static JsonElement Row(JsonElement upload, int number) =>
        upload.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("rowNumber").GetInt32() == number);
}
