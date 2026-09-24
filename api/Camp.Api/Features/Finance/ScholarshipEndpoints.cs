using System.Text.Json;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Finance;

public sealed record ScholarshipDocumentUpload(string? FileName, string? ContentType, string? Base64);
public sealed record ScholarshipApplyRequest(string? Code, int[]? RegistrationIds, int RequestedCents, string? IncomeBand, string? Reason, ScholarshipDocumentUpload? Document);
public sealed record ScholarshipApproveRequest(int AwardCents, string? Note);
public sealed record ScholarshipDenyRequest(string? Note);

/// <summary>
/// O6 · Scholarship application (family, FR-38) and O7 · Scholarship review (staff, FR-53). An
/// approved award lowers each covered registration's balance on the server; scholarships plus
/// discounts never exceed the price.
/// </summary>
public sealed class ScholarshipEndpoints : IEndpointModule
{
    public const int MaxDocumentBytes = 5 * 1024 * 1024;
    public const int MaxReasonLength = 1000;
    public static readonly string[] IncomeBands = ["Under $35,000", "$35,000–$50,000", "$50,000–$75,000", "$75,000–$100,000", "Over $100,000"];

    public void Map(IEndpointRouteBuilder app)
    {
        var family = app.MapGroup("/api/family/scholarships").RequireAuthorization(Policies.Family);
        family.MapGet("", FamilyOverview);
        family.MapPost("", Apply);

        var staff = app.MapGroup("/api/admin/scholarships").RequireAuthorization(Policies.Staff);
        staff.MapGet("", Queue);
        staff.MapGet("/{id:int}", Detail);
        staff.MapGet("/{id:int}/document", async (int id, CampDbContext db, CancellationToken ct) =>
        {
            var doc = await db.Set<ScholarshipDocument>().AsNoTracking().FirstOrDefaultAsync(d => d.ApplicationId == id, ct);
            return doc is null ? Results.NotFound() : Results.File(doc.Content, doc.ContentType, doc.FileName);
        });
        staff.MapPost("/{id:int}/approve", Approve);
        staff.MapPost("/{id:int}/deny", Deny);
    }

    // ---- O6: family ----

    static async Task<IResult> FamilyOverview(CampDbContext db, CurrentUser user, TimeProvider clock, CancellationToken ct)
    {
        var household = await db.Households.AsNoTracking().Include(h => h.Members).SingleAsync(h => h.Id == user.HouseholdId, ct);
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.HouseholdId == user.HouseholdId && o.Status == OrderStatus.Paid)
            .Include(o => o.Session).ThenInclude(s => s.Program)
            .Include(o => o.Registrations).ThenInclude(r => r.Person)
            .OrderBy(o => o.Session.StartDate).ToListAsync(ct);
        var apps = await db.Set<ScholarshipApplication>().AsNoTracking()
            .Where(a => a.HouseholdId == user.HouseholdId)
            .Include(a => a.Lines).Include(a => a.Document)
            .OrderByDescending(a => a.SubmittedAt).ToListAsync(ct);
        var today = clock.Today();
        var primary = household.Members.Where(m => m.IsAdult).OrderBy(m => m.Role != "Primary").ThenBy(m => m.Id).FirstOrDefault();

        return Results.Ok(new
        {
            Household = new { household.Name, Contact = primary?.FullName ?? household.Name, household.Email, household.Phone, household.City },
            IncomeBands,
            MaxDocumentBytes,
            Orders = orders.Where(o => o.Session.EndDate >= today).Select(o =>
            {
                var regs = o.Registrations.Where(r => r.Status == RegistrationStatus.Confirmed).OrderBy(r => r.Person.DateOfBirth).ToList();
                var open = apps.FirstOrDefault(a => a.OrderId == o.Id && a.Status == ScholarshipStatus.Submitted);
                return new
                {
                    o.ConfirmationCode,
                    Program = o.Session.Program.Name,
                    Session = o.Session.Name,
                    o.Session.StartDate,
                    o.Session.EndDate,
                    o.Session.Program.Location,
                    BalanceCents = regs.Sum(r => r.BalanceCents),
                    CanApply = open is null && regs.Any(r => r.BalanceCents > 0),
                    Campers = regs.Select(r => new { RegistrationId = r.Id, r.Person.FirstName, Name = r.Person.FullName, r.Grade, r.PriceCents, r.DiscountCents, r.BalanceCents }),
                };
            }).Where(o => o.Campers.Any()),
            Applications = apps.Select(a =>
            {
                var o = orders.FirstOrDefault(x => x.Id == a.OrderId);
                var covered = a.Lines.Select(l => l.RegistrationId).ToHashSet();
                return new
                {
                    a.Id,
                    Status = a.Status.ToString(),
                    a.SubmittedAt,
                    a.RequestedCents,
                    a.AwardCents,
                    a.DecidedAt,
                    // Staff notes are internal; a family sees only the decision and the amount.
                    ConfirmationCode = o?.ConfirmationCode,
                    Program = o?.Session.Program.Name,
                    Session = o?.Session.Name,
                    Campers = o?.Registrations.Where(r => covered.Contains(r.Id)).Select(r => r.Person.FirstName) ?? [],
                    DocumentName = a.Document?.FileName,
                };
            }),
        });
    }

    static async Task<IResult> Apply(ScholarshipApplyRequest req, CampDbContext db, CurrentUser user, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var code = req.Code?.Trim() ?? "";
        var order = await db.Orders.Include(o => o.Registrations).ThenInclude(r => r.Person).Include(o => o.Session).ThenInclude(s => s.Program)
            .FirstOrDefaultAsync(o => o.ConfirmationCode == code && o.HouseholdId == user.HouseholdId && o.Status == OrderStatus.Paid, ct);
        if (order is null) return Results.NotFound();
        if (order.Session.EndDate < clock.Today()) return Fin.Invalid("code", "This camp has already ended.");

        var ids = (req.RegistrationIds ?? []).Distinct().ToList();
        var regs = order.Registrations.Where(r => ids.Contains(r.Id) && r.Status == RegistrationStatus.Confirmed).ToList();
        if (ids.Count == 0) return Fin.Invalid("registrationIds", "Choose at least one camper.");
        if (regs.Count != ids.Count) return Fin.Invalid("registrationIds", "One of those campers isn't on this registration.");
        if (regs.Sum(r => r.BalanceCents) <= 0) return Fin.Invalid("registrationIds", "Nothing is owed for these campers, so there's nothing to apply toward.");

        var eligible = regs.Sum(r => r.PriceCents - r.DiscountCents);
        if (req.RequestedCents <= 0) return Fin.Invalid("requestedCents", "Enter the amount you're asking for.");
        if (req.RequestedCents > eligible) return Fin.Invalid("requestedCents", $"You can ask for up to {Fin.Money(eligible)}, the cost for these campers after discounts.");
        var band = req.IncomeBand?.Trim();
        if (band is null || !IncomeBands.Contains(band)) return Fin.Invalid("incomeBand", "Choose your household income range.");
        var reason = req.Reason?.Trim();
        if (string.IsNullOrEmpty(reason)) return Fin.Invalid("reason", "Tell us a little about your situation.");
        if (reason.Length > MaxReasonLength) return Fin.Invalid("reason", $"Keep this to {MaxReasonLength:N0} characters.");
        var (doc, docError) = ReadDocument(req.Document, clock);
        if (doc is null) return Fin.Invalid("document", docError!);

        var submitter = order.Registrations.Select(r => r.Person).FirstOrDefault(p => p.IsAdult)?.FullName;
        var application = new ScholarshipApplication
        {
            HouseholdId = user.HouseholdId,
            OrderId = order.Id,
            SubmittedBy = string.IsNullOrWhiteSpace(user.Name) ? submitter ?? user.Email : user.Name,
            SubmittedAt = clock.UtcNow(),
            RequestedCents = req.RequestedCents,
            IncomeBand = band,
            Reason = reason,
            Status = ScholarshipStatus.Submitted,
            Document = doc,
        };
        application.Lines.AddRange(regs.Select(r => new ScholarshipAwardLine { RegistrationId = r.Id }));
        db.Add(application);
        audit.Record("scholarship.submitted", "PaymentOrder", order.Id, $"Applied for {Fin.Money(req.RequestedCents)} in financial assistance for {string.Join(" and ", regs.Select(r => r.Person.FirstName))} ({order.Session.Program.Name}).");
        db.OutboxEvents.Add(new OutboxEvent { Type = "ScholarshipSubmitted", Target = "HubSpot", AggregateId = order.ConfirmationCode, PayloadJson = JsonSerializer.Serialize(new { order.ConfirmationCode, req.RequestedCents }), CreatedAt = clock.UtcNow() });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            return Fin.Conflict("You already have an application in review for this registration.");
        }
        return Results.Ok(new { application.Id, Status = application.Status.ToString() });
    }

    /// <summary>Accepts a PDF, PNG or JPEG up to 5 MB, judged by its first bytes rather than its name.</summary>
    static (ScholarshipDocument?, string?) ReadDocument(ScholarshipDocumentUpload? upload, TimeProvider clock)
    {
        if (upload is null || string.IsNullOrEmpty(upload.Base64)) return (null, "A supporting document is required. Upload a tax return, pay stub or similar.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(upload.Base64); }
        catch (FormatException) { return (null, "That file didn't upload correctly. Try choosing it again."); }
        if (bytes.Length == 0) return (null, "That file is empty.");
        if (bytes.Length > MaxDocumentBytes) return (null, "Files can be up to 5 MB.");
        var type = bytes switch
        {
            [0x25, 0x50, 0x44, 0x46, ..] => "application/pdf",
            [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
            [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
            _ => null,
        };
        if (type is null) return (null, "Upload a PDF, PNG or JPEG.");
        var name = Path.GetFileName(upload.FileName ?? "").Trim();
        if (name.Length == 0) name = "document";
        if (name.Length > 200) name = name[..200];
        return (new ScholarshipDocument { FileName = name, ContentType = type, SizeBytes = bytes.Length, Content = bytes, UploadedAt = clock.UtcNow() }, null);
    }

    // ---- O7: staff ----

    static async Task<IResult> Queue(CampDbContext db, string? status, CancellationToken ct)
    {
        var all = await db.Set<ScholarshipApplication>().AsNoTracking()
            .Select(a => new
            {
                a.Id,
                a.Status,
                a.SubmittedAt,
                a.SubmittedBy,
                a.RequestedCents,
                a.AwardCents,
                Household = a.Household.Name,
                Program = a.Order.Session.Program.Name,
                Session = a.Order.Session.Name,
                a.Order.ConfirmationCode,
                Campers = a.Lines.Select(l => l.Registration.Person.FirstName).ToList(),
                HasDocument = a.Document != null,
            })
            .ToListAsync(ct);
        var filter = status?.ToLowerInvariant() switch
        {
            "approved" => (ScholarshipStatus?)ScholarshipStatus.Approved,
            "denied" => ScholarshipStatus.Denied,
            "all" => null,
            _ => ScholarshipStatus.Submitted,
        };
        return Results.Ok(new
        {
            Counts = new
            {
                All = all.Count,
                Submitted = all.Count(a => a.Status == ScholarshipStatus.Submitted),
                Approved = all.Count(a => a.Status == ScholarshipStatus.Approved),
                Denied = all.Count(a => a.Status == ScholarshipStatus.Denied),
                AwardedCents = all.Where(a => a.Status == ScholarshipStatus.Approved).Sum(a => a.AwardCents),
            },
            Rows = all.Where(a => filter is null || a.Status == filter)
                .OrderBy(a => a.Status != ScholarshipStatus.Submitted).ThenBy(a => a.Status == ScholarshipStatus.Submitted ? a.SubmittedAt : DateTime.MaxValue).ThenByDescending(a => a.SubmittedAt)
                .Select(a => new { a.Id, Status = a.Status.ToString(), a.SubmittedAt, a.SubmittedBy, a.RequestedCents, a.AwardCents, a.Household, a.Program, a.Session, a.ConfirmationCode, a.Campers, a.HasDocument }),
        });
    }

    static async Task<IResult> Detail(int id, CampDbContext db, CancellationToken ct)
    {
        var a = await LoadForDecision(db, id, tracking: false, ct);
        if (a is null) return Results.NotFound();
        var regs = a.Lines.Select(l => l.Registration).ToList();
        var (eligible, owed) = Limits(regs);
        var h = a.Household;
        var primary = h.Members.Where(m => m.IsAdult).OrderBy(m => m.Role != "Primary").ThenBy(m => m.Id).FirstOrDefault();
        return Results.Ok(new
        {
            a.Id,
            Status = a.Status.ToString(),
            a.SubmittedAt,
            a.SubmittedBy,
            a.RequestedCents,
            a.IncomeBand,
            a.Reason,
            a.AwardCents,
            a.DecidedBy,
            a.DecidedAt,
            a.DecisionNote,
            Household = new { h.Name, Contact = primary?.FullName ?? h.Name, h.Email, h.Phone, h.City },
            a.Order.ConfirmationCode,
            Program = a.Order.Session.Program.Name,
            Session = a.Order.Session.Name,
            a.Order.Session.StartDate,
            a.Order.Session.EndDate,
            Campers = a.Lines.Select(l => new
            {
                l.RegistrationId,
                Name = l.Registration.Person.FullName,
                l.Registration.Grade,
                l.Registration.PriceCents,
                // Discounts other than this application's award, so the cap check reads plainly.
                DiscountCents = l.Registration.DiscountCents - l.AmountCents,
                AwardCents = l.AmountCents,
                l.Registration.PaidCents,
                l.Registration.BalanceCents,
            }),
            EligibleCents = eligible,
            OwedCents = owed,
            MaxAwardCents = Math.Min(eligible, owed),
            Document = a.Document is null ? null : new { a.Document.FileName, a.Document.ContentType, a.Document.SizeBytes, a.Document.UploadedAt },
        });
    }

    static async Task<IResult> Approve(int id, ScholarshipApproveRequest req, CampDbContext db, StaffUser staff, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var note = req.Note?.Trim();
        if (note?.Length > MaxReasonLength) return Fin.Invalid("note", $"Notes are limited to {MaxReasonLength:N0} characters.");
        if (req.AwardCents <= 0) return Fin.Invalid("awardCents", "Enter an award amount. To turn the request down, use Deny.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var a = await LoadForDecision(db, id, tracking: true, ct);
        if (a is null) return Results.NotFound();
        if (a.Status != ScholarshipStatus.Submitted) return Fin.Conflict($"This application was already {a.Status.ToString().ToLowerInvariant()} by {a.DecidedBy}.");
        var regs = a.Lines.Select(l => l.Registration).ToList();
        var (eligible, owed) = Limits(regs);
        if (req.AwardCents > eligible)
            return Fin.Invalid("awardCents", $"Cannot approve: award {Fin.Money(req.AwardCents)} exceeds {Fin.Money(eligible)} eligible total; scholarships plus discounts cannot exceed 100%.");
        if (req.AwardCents > owed)
            return Fin.Invalid("awardCents", $"Cannot approve: award {Fin.Money(req.AwardCents)} is more than the {Fin.Money(owed)} still owed. The family has already paid the rest, so the difference would be a refund.");

        if (!await Decide(db, id, ScholarshipStatus.Approved, req.AwardCents, staff.Actor, note, clock, ct)) return await AlreadyDecided(db, id, ct);
        ApplyAward(a.Lines, req.AwardCents);
        var order = await db.Orders.Include(o => o.Installments).Include(o => o.Registrations).SingleAsync(o => o.Id == a.OrderId, ct);
        Fin.ShrinkPlan(order.Installments, order.Registrations.Where(r => r.Status != RegistrationStatus.Cancelled).Sum(r => r.BalanceCents));
        audit.Record("scholarship.approved", "ScholarshipApplication", a.Id,
            $"Awarded {Fin.Money(req.AwardCents)} to the {a.Household.Name} household ({a.Order.ConfirmationCode}, {string.Join(" and ", regs.Select(r => r.Person.FirstName))}). Balance now {Fin.Money(regs.Sum(r => r.BalanceCents))}.{(note is { Length: > 0 } ? $" Note: {note}" : "")}");
        db.OutboxEvents.Add(new OutboxEvent { Type = "ScholarshipAwarded", Target = "HubSpot", AggregateId = a.Order.ConfirmationCode, PayloadJson = JsonSerializer.Serialize(new { a.Order.ConfirmationCode, awardCents = req.AwardCents }), CreatedAt = clock.UtcNow() });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(new { AwardCents = req.AwardCents, BalanceCents = regs.Sum(r => r.BalanceCents) });
    }

    static async Task<IResult> Deny(int id, ScholarshipDenyRequest req, CampDbContext db, StaffUser staff, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var note = req.Note?.Trim();
        if (string.IsNullOrEmpty(note)) return Fin.Invalid("note", "Add a note saying why, for the file.");
        if (note.Length > MaxReasonLength) return Fin.Invalid("note", $"Notes are limited to {MaxReasonLength:N0} characters.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var a = await db.Set<ScholarshipApplication>().Include(x => x.Household).Include(x => x.Order).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Results.NotFound();
        if (!await Decide(db, id, ScholarshipStatus.Denied, 0, staff.Actor, note, clock, ct)) return await AlreadyDecided(db, id, ct);
        audit.Record("scholarship.denied", "ScholarshipApplication", a.Id, $"Denied the {a.Household.Name} household's request for {Fin.Money(a.RequestedCents)} ({a.Order.ConfirmationCode}). Note: {note}");
        db.OutboxEvents.Add(new OutboxEvent { Type = "ScholarshipDenied", Target = "HubSpot", AggregateId = a.Order.ConfirmationCode, PayloadJson = JsonSerializer.Serialize(new { a.Order.ConfirmationCode }), CreatedAt = clock.UtcNow() });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok();
    }

    static Task<ScholarshipApplication?> LoadForDecision(CampDbContext db, int id, bool tracking, CancellationToken ct)
    {
        var q = db.Set<ScholarshipApplication>()
            .Include(a => a.Household).ThenInclude(h => h.Members)
            .Include(a => a.Order).ThenInclude(o => o.Session).ThenInclude(s => s.Program)
            .Include(a => a.Lines).ThenInclude(l => l.Registration).ThenInclude(r => r.Person)
            .Include(a => a.Document)
            .AsSplitQuery();
        return (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    /// <summary>
    /// Eligible: the price after every discount and scholarship already applied (the 100% cap).
    /// Owed: what the family still has to pay; an award past it would be a refund.
    /// </summary>
    static (int Eligible, int Owed) Limits(List<Registration> regs)
    {
        var live = regs.Where(r => r.Status != RegistrationStatus.Cancelled).ToList();
        return (live.Sum(r => Math.Max(0, r.PriceCents - r.DiscountCents)), live.Sum(r => Math.Max(0, r.BalanceCents)));
    }

    /// <summary>Splits the award across the covered registrations by what each still owes, never past zero.</summary>
    static void ApplyAward(List<ScholarshipAwardLine> lines, int award)
    {
        var owing = lines.Where(l => l.Registration.Status != RegistrationStatus.Cancelled && l.Registration.BalanceCents > 0).ToList();
        var owed = owing.Sum(l => l.Registration.BalanceCents);
        var shares = owing.Select(l => (Line: l, Share: (int)((long)award * l.Registration.BalanceCents / owed))).ToList();
        var left = award - shares.Sum(s => s.Share);
        foreach (var (line, share) in shares)
        {
            var extra = Math.Min(left, line.Registration.BalanceCents - share);
            left -= extra;
            line.AmountCents += share + extra;
            line.Registration.DiscountCents += share + extra;
        }
    }

    static async Task<bool> Decide(CampDbContext db, int id, ScholarshipStatus status, int award, string actor, string? note, TimeProvider clock, CancellationToken ct)
    {
        var now = (DateTime?)clock.UtcNow();
        return await db.Set<ScholarshipApplication>().Where(a => a.Id == id && a.Status == ScholarshipStatus.Submitted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, status)
                .SetProperty(a => a.AwardCents, award)
                .SetProperty(a => a.DecidedBy, actor)
                .SetProperty(a => a.DecidedAt, now)
                .SetProperty(a => a.DecisionNote, note), ct) == 1;
    }

    static async Task<IResult> AlreadyDecided(CampDbContext db, int id, CancellationToken ct)
    {
        var a = await db.Set<ScholarshipApplication>().AsNoTracking().SingleAsync(x => x.Id == id, ct);
        return Fin.Conflict($"This application was already {a.Status.ToString().ToLowerInvariant()} by {a.DecidedBy}.");
    }
}
