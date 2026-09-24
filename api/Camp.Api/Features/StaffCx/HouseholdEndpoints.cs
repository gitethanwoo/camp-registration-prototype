using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Admittance;
using Camp.Api.Features.Finance;
using Camp.Api.Features.Polish;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.StaffCx;

public record NoteRequest(string Body);
public record VerificationRequest(bool Checked);

/// <summary>C1 global search and C2 household 360.</summary>
public sealed class HouseholdEndpoints : IEndpointModule
{
    /// <summary>The C2 verification checklist, in display order.</summary>
    public static readonly (string Key, string Label)[] VerificationItems =
    [
        ("guardians", "Parent/guardian names confirmed with the caller"),
        ("contact", "Email and phone confirmed"),
        ("birthdates", "Children's dates of birth and grades checked"),
        ("duplicate", "Possible duplicate accounts reviewed"),
    ];

    public void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(Policies.Staff);

        // C1 · One search across every ministry: name, member name, email, phone, or confirmation code (FR-61).
        admin.MapGet("/search", async (CampDbContext db, string? q, CancellationToken ct) =>
        {
            var term = q?.Trim() ?? "";
            if (term.Length < 2) return StaffCx.Invalid("q", "Type at least 2 characters: a name, email, phone number, or confirmation code.");
            var digits = new string(term.Where(char.IsDigit).ToArray());
            var merged = db.Set<HouseholdMerge>().Select(m => m.MergedHouseholdId);

            var query = db.Households.AsNoTracking().Where(h => !merged.Contains(h.Id) && (
                h.Name.Contains(term) || h.Email.Contains(term) ||
                (digits.Length >= 4 && h.Phone.Replace("(", "").Replace(")", "").Replace("-", "").Replace(" ", "").Replace(".", "").Contains(digits)) ||
                db.People.Any(p => p.HouseholdId == h.Id && ((p.FirstName + " " + p.LastName).Contains(term) || (p.Email != null && p.Email.Contains(term)))) ||
                db.Orders.Any(o => o.HouseholdId == h.Id && o.ConfirmationCode == term)));

            var total = await query.CountAsync(ct);
            var households = await query.OrderBy(h => h.Name).ThenBy(h => h.Id).Take(25)
                .Select(h => new { h.Id, h.Name, h.Email, h.Phone, h.City })
                .ToListAsync(ct);
            var ids = households.Select(h => h.Id).ToList();

            var members = await db.People.AsNoTracking().Where(p => ids.Contains(p.HouseholdId))
                .OrderByDescending(p => p.IsAdult).ThenBy(p => p.Id)
                .Select(p => new { p.HouseholdId, p.FirstName, p.LastName, p.IsAdult }).ToListAsync(ct);
            var regs = await db.Registrations.AsNoTracking()
                .Where(r => ids.Contains(r.HouseholdId) && r.Order!.Status != OrderStatus.Declined)
                .Select(r => new
                {
                    r.HouseholdId,
                    Participant = r.Person.FirstName,
                    Program = r.Session.Program.Name,
                    Ministry = r.Session.Program.Ministry.Code,
                    Year = r.Session.StartDate.Year,
                    Status = r.Status,
                    r.CreatedAt,
                }).ToListAsync(ct);
            var waits = await db.WaitlistEntries.AsNoTracking()
                .Where(w => ids.Contains(w.HouseholdId) && w.Status != WaitlistStatus.Removed)
                .Select(w => new { w.HouseholdId, Participant = w.Person.FirstName, Program = w.Pool.Session.Program.Name, Ministry = w.Pool.Session.Program.Ministry.Code, Year = w.Pool.Session.StartDate.Year, w.Position, w.CreatedAt })
                .ToListAsync(ct);
            var notes = await db.Set<HouseholdNote>().AsNoTracking().Where(n => ids.Contains(n.HouseholdId))
                .GroupBy(n => n.HouseholdId).Select(g => g.OrderByDescending(n => n.CreatedAt).First()).ToListAsync(ct);

            return Results.Ok(new
            {
                Total = total,
                Rows = households.Select(h =>
                {
                    var activity = regs.Where(r => r.HouseholdId == h.Id).Select(r => (Label: $"{r.Ministry} {r.Program} {r.Year}", r.CreatedAt, Text: $"{r.Participant} registered for {r.Program}"))
                        .Concat(waits.Where(w => w.HouseholdId == h.Id).Select(w => (Label: $"{w.Ministry} {w.Program} {w.Year}", w.CreatedAt, Text: $"{w.Participant} joined the {w.Program} waitlist (#{w.Position})")))
                        .Concat(notes.Where(n => n.HouseholdId == h.Id).Select(n => (Label: "", n.CreatedAt, Text: $"Note by {n.Author}")))
                        .OrderByDescending(a => a.CreatedAt).ToList();
                    var latest = activity.FirstOrDefault();
                    return new
                    {
                        h.Id,
                        h.Name,
                        h.Email,
                        h.Phone,
                        h.City,
                        Members = members.Where(m => m.HouseholdId == h.Id).Select(m => new { Name = m.FirstName + " " + m.LastName, m.IsAdult }),
                        Ministries = activity.Where(a => a.Label != "").Select(a => a.Label).Distinct(),
                        RecentActivity = latest.Text,
                        RecentActivityAt = latest.Text is null ? (DateTime?)null : latest.CreatedAt,
                    };
                }),
            });
        });

        // C2 · Household 360: everything about one household, across ministries. No health details (FR-112).
        admin.MapGet("/households/{id:int}", async (int id, CampDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var h = await db.Households.Include(x => x.Members).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (h is null) return Results.NotFound();

            var season = await db.Sessions.Where(s => s.StartDate >= clock.Today()).MinAsync(s => (DateOnly?)s.StartDate, ct)
                ?? clock.Today();
            var regs = await db.Registrations.AsNoTracking()
                .Where(r => r.HouseholdId == id && r.Order!.Status != OrderStatus.Declined)
                .Include(r => r.Person).Include(r => r.Pool).Include(r => r.Session).ThenInclude(s => s.Program).ThenInclude(p => p.Ministry)
                .Include(r => r.Order).ThenInclude(o => o!.Installments)
                .OrderByDescending(r => r.Session.StartDate).ThenBy(r => r.Person.FirstName)
                .ToListAsync(ct);
            var waits = await db.WaitlistEntries.AsNoTracking().Where(w => w.HouseholdId == id && w.Status != WaitlistStatus.Removed)
                .Include(w => w.Person).Include(w => w.Pool).ThenInclude(p => p.Session).ThenInclude(s => s.Program)
                .ToListAsync(ct);
            var transfers = await db.Set<TransferRequest>().AsNoTracking().Where(t => t.HouseholdId == id)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new { t.Id, Participant = t.Registration.Person.FirstName + " " + t.Registration.Person.LastName, From = t.FromSession.Name, To = t.ToSession.Name, Status = t.Status.ToString(), t.CreatedAt })
                .ToListAsync(ct);
            var notes = await db.Set<HouseholdNote>().AsNoTracking().Where(n => n.HouseholdId == id).OrderByDescending(n => n.CreatedAt).ToListAsync(ct);
            var checks = await db.Set<HouseholdVerification>().AsNoTracking().Where(v => v.HouseholdId == id).ToListAsync(ct);
            var duplicates = await DuplicateDetector.FindAsync(db, id, ct);
            var mergedInto = await db.Set<HouseholdMerge>().AsNoTracking().Where(m => m.MergedHouseholdId == id).Select(m => (int?)m.SurvivorHouseholdId).FirstOrDefaultAsync(ct);
            var mergedFrom = await db.Set<HouseholdMerge>().AsNoTracking().Where(m => m.SurvivorHouseholdId == id).Select(m => new { m.MergedHouseholdId, m.Actor, m.CreatedAt }).ToListAsync(ct);
            var key = id.ToString(CultureInfo.InvariantCulture);
            var sync = await db.OutboxEvents.AsNoTracking().Where(e => e.Target == "Salesforce" && e.AggregateId == key)
                .OrderByDescending(e => e.Id).Select(e => new { e.Type, e.CreatedAt, e.ProcessedAt }).FirstOrDefaultAsync(ct);
            var history = await HistoryAsync(db, id, h.Members.Select(m => m.Id), ct);

            return Results.Ok(new
            {
                h.Id,
                h.Name,
                h.Email,
                h.Phone,
                h.City,
                MergedIntoHouseholdId = mergedInto,
                MergedFrom = mergedFrom,
                Salesforce = new
                {
                    Id = h.SalesforceId,
                    Status = (h.SalesforceId, sync) switch
                    {
                        (_, { ProcessedAt: null }) => "Pending",
                        (null, null) => "Not linked",
                        _ => "Synced",
                    },
                    LastSyncAt = sync?.ProcessedAt,
                },
                Adults = h.Members.Where(m => m.IsAdult).OrderBy(m => m.Id).Select(m => new { m.Id, Name = m.FullName, Role = m.Role ?? "Adult", m.Email }),
                Children = h.Members.Where(m => !m.IsAdult).OrderBy(m => m.DateOfBirth).Select(m => new
                {
                    m.Id,
                    Name = m.FullName,
                    m.DateOfBirth,
                    Grade = Eligibility.GradeLabel(Eligibility.GradeFor(m.DateOfBirth, season)),
                    GradeYear = season.Year,
                }),
                Registrations = regs.Select(r => new
                {
                    r.Id,
                    Participant = r.Person.FullName,
                    Program = r.Session.Program.Name,
                    Ministry = r.Session.Program.Ministry.Code,
                    Session = r.Session.Name,
                    r.Session.StartDate,
                    r.Session.EndDate,
                    Pool = r.Pool.Name,
                    Status = r.Status.ToString(),
                    r.PriceCents,
                    r.DiscountCents,
                    r.PaidCents,
                    r.BalanceCents,
                    Payment = StaffCx.PaymentState(r.Status, r.BalanceCents, r.PaidCents, r.Order?.Installments.Select(i => i.Status) ?? []),
                    ConfirmationCode = r.Order?.ConfirmationCode,
                }),
                Totals = new
                {
                    PriceCents = regs.Where(r => r.Status != RegistrationStatus.Cancelled).Sum(r => r.PriceCents - r.DiscountCents),
                    PaidCents = regs.Where(r => r.Status != RegistrationStatus.Cancelled).Sum(r => r.PaidCents),
                    BalanceCents = regs.Sum(r => r.BalanceCents),
                },
                Waitlist = waits.Select(w => new
                {
                    w.Id,
                    Participant = w.Person.FullName,
                    Program = w.Pool.Session.Program.Name,
                    Session = w.Pool.Session.Name,
                    Pool = w.Pool.Name,
                    w.Position,
                    Status = w.Status.ToString(),
                    w.OfferExpiresAt,
                }),
                Transfers = transfers,
                Notes = notes.Select(n => new { n.Id, n.Body, n.Author, n.CreatedAt }),
                Verification = VerificationItems.Select(item =>
                {
                    var c = checks.FirstOrDefault(v => v.ItemKey == item.Key);
                    return new { item.Key, item.Label, Checked = c is not null, c?.CheckedBy, c?.CheckedAt };
                }),
                Duplicates = duplicates.Select(d => new { OtherHouseholdId = d.HouseholdA == id ? d.HouseholdB : d.HouseholdA, d.HouseholdA, d.HouseholdB, d.Reason }),
                History = history,
            });
        });

        admin.MapPost("/households/{id:int}/notes", async (int id, NoteRequest req, CampDbContext db, StaffUser staff, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await db.Households.AnyAsync(h => h.Id == id, ct)) return Results.NotFound();
            var body = req.Body?.Trim() ?? "";
            if (body.Length == 0) return StaffCx.Invalid("body", "Write the note first.");
            if (body.Length > 2000) return StaffCx.Invalid("body", "Notes are limited to 2,000 characters.");
            var note = new HouseholdNote { HouseholdId = id, Body = body, Author = staff.Actor, CreatedAt = clock.UtcNow() };
            db.Set<HouseholdNote>().Add(note);
            audit.Record("household.note_added", "Household", id, $"Note added: {(body.Length > 120 ? body[..120] + "…" : body)}");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { note.Id, note.Body, note.Author, note.CreatedAt });
        });

        admin.MapPut("/households/{id:int}/verification/{key}", async (int id, string key, VerificationRequest req, CampDbContext db, StaffUser staff, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await db.Households.AnyAsync(h => h.Id == id, ct)) return Results.NotFound();
            var item = VerificationItems.FirstOrDefault(i => i.Key == key);
            if (item.Key is null) return Results.NotFound();
            var existing = await db.Set<HouseholdVerification>().FirstOrDefaultAsync(v => v.HouseholdId == id && v.ItemKey == key, ct);
            if (req.Checked && existing is null)
                db.Set<HouseholdVerification>().Add(new HouseholdVerification { HouseholdId = id, ItemKey = key, CheckedBy = staff.Actor, CheckedAt = clock.UtcNow() });
            else if (!req.Checked && existing is not null)
                db.Set<HouseholdVerification>().Remove(existing);
            else
                return Results.Ok();
            audit.Record(req.Checked ? "household.verified" : "household.unverified", "Household", id, $"{(req.Checked ? "Checked" : "Unchecked")}: {item.Label}");
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        }).RequireAuthorization(Policies.Cet);
    }
    /// <summary>
    /// Audit events about the household and everything it owns: its members, orders and their installments,
    /// registrations, waitlist spots, admittance and scholarship applications. Newest first. Health-record views
    /// stay out (FR-112); they're in the audit log.
    /// </summary>
    static async Task<List<HistoryRow>> HistoryAsync(CampDbContext db, int id, IEnumerable<int> memberIds, CancellationToken ct)
    {
        static List<string> Keys(IEnumerable<int> ids) => [.. ids.Select(i => i.ToString(CultureInfo.InvariantCulture))];
        var household = id.ToString(CultureInfo.InvariantCulture);
        var people = Keys(memberIds);
        var orders = Keys(await db.Orders.Where(o => o.HouseholdId == id).Select(o => o.Id).ToListAsync(ct));
        var installments = Keys(await db.Installments.Where(i => db.Orders.Any(o => o.Id == i.OrderId && o.HouseholdId == id)).Select(i => i.Id).ToListAsync(ct));
        var registrations = Keys(await db.Registrations.Where(r => r.HouseholdId == id).Select(r => r.Id).ToListAsync(ct));
        var waitlist = Keys(await db.WaitlistEntries.Where(w => w.HouseholdId == id).Select(w => w.Id).ToListAsync(ct));
        var applications = Keys(await db.Set<AdmittanceApplication>().Where(a => a.HouseholdId == id).Select(a => a.Id).ToListAsync(ct));
        var scholarships = Keys(await db.Set<ScholarshipApplication>().Where(a => a.HouseholdId == id).Select(a => a.Id).ToListAsync(ct));

        return await db.AuditEvents.AsNoTracking()
            .Where(a => !a.Action.StartsWith("health."))
            .Where(a => (a.EntityType == "Household" && a.EntityId == household)
                || (a.EntityType == "Person" && people.Contains(a.EntityId))
                || (a.EntityType == "PaymentOrder" && orders.Contains(a.EntityId))
                || (a.EntityType == "Installment" && installments.Contains(a.EntityId))
                || (a.EntityType == "Registration" && registrations.Contains(a.EntityId))
                || (a.EntityType == "WaitlistEntry" && waitlist.Contains(a.EntityId))
                || (a.EntityType == "AdmittanceApplication" && applications.Contains(a.EntityId))
                || (a.EntityType == "ScholarshipApplication" && scholarships.Contains(a.EntityId)))
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Take(50)
            .Select(a => new HistoryRow(a.Actor, a.Action, a.Detail, a.CreatedAt))
            .ToListAsync(ct);
    }

    sealed record HistoryRow(string Actor, string Action, string Detail, DateTime CreatedAt);
}
