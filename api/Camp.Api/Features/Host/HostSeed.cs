using Camp.Api.Data;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Host;

/// <summary>
/// Demo rows for the host portal: Grace Community Church (host of Day Camp · Atlanta, June 12–16),
/// its coordinator Grace Patel, 60 volunteers already on file, this year's open invoice and last
/// year's paid one. A second church, Riverside Fellowship, has its own volunteers and invoice so the
/// isolation between hosts is visible in tests. Nothing here adds sessions or registrations.
/// </summary>
public sealed class HostSeed : ISeedModule
{
    public const string GraceChurch = "Grace Community Church";
    public const string GraceEmail = "grace.patel@winshape.example";
    public const string RiversideChurch = "Riverside Fellowship";
    public const string RiversideEmail = "host.riverside@winshape.example";
    public const string OpenInvoice = "INV-2028-041";

    public int Order => 180;

    static readonly string[] First = ["Helen", "Jordan", "Taylor", "Casey", "Riley", "Jamie", "Reese", "Dakota", "Skyler", "Cameron", "Logan", "Harper", "Emerson", "Rowan", "Blake"];
    static readonly string[] Last = ["Parker", "Tucker", "Morgan", "Patel", "Chen", "Brooks", "Howard", "Kim", "Wells", "Adams", "Price", "Ellis", "Ford", "Garner", "Sutton"];

    public async Task RunAsync(CampDbContext db, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        if (await db.Set<HostOrganization>().AnyAsync(ct)) return;
        var session = await db.Sessions.Include(s => s.Program)
            .Where(s => s.Program.Slug == "day-camp-atlanta").OrderBy(s => s.StartDate).FirstOrDefaultAsync(ct);
        if (session is null) return;

        var grace = new HostOrganization { Name = GraceChurch, City = "Atlanta, GA" };
        var riverside = new HostOrganization { Name = RiversideChurch, City = "Rome, GA" };
        db.AddRange(grace, riverside);
        db.AddRange(
            new HostMember { HostOrganization = grace, Email = GraceEmail, Title = "Operations Director" },
            new HostMember { HostOrganization = riverside, Email = RiversideEmail, Title = "Children's Pastor" });
        var ev = new HostEvent { HostOrganization = grace, SessionId = session.Id, LastYearRegistrations = 88, VettingDeadline = new(2028, 5, 15) };
        db.Add(ev);
        await db.SaveChangesAsync(ct);

        // 60 volunteers on file: 59 generated, plus Morgan Lee, whom the demo CSV lists again (row 32).
        var created = new DateTime(2028, 1, 15, 15, 0, 0, DateTimeKind.Utc);
        var volunteers = Enumerable.Range(0, 59).Select(i =>
        {
            var first = First[i % First.Length];
            var last = Last[(i + (i / First.Length)) % Last.Length];
            return Volunteer(grace.Id, first, last, i, created.AddDays(i % 20));
        }).ToList();
        volunteers.Add(Volunteer(grace.Id, "Morgan", "Lee", 1, created));
        db.AddRange(volunteers);
        db.AddRange(
            Volunteer(riverside.Id, "Elena", "Brooks", 0, created),
            Volunteer(riverside.Id, "Marcus", "Webb", 1, created),
            Volunteer(riverside.Id, "Tessa", "Ruiz", 6, created));

        var lines = new[] { ("Host participation fee", 80000), ("Site services", 40000) };
        db.AddRange(
            Invoice(grace.Id, ev.Id, OpenInvoice, "Day Camp", "June 2028", new(2028, 2, 1), new(2028, 5, 20), lines),
            Invoice(grace.Id, ev.Id, "INV-2027-039", "Day Camp", "June 2027", new(2027, 2, 1), new(2027, 5, 20), lines,
                paidOn: new DateTime(2027, 5, 12, 14, 30, 0, DateTimeKind.Utc)),
            Invoice(riverside.Id, null, "INV-2028-052", "VBS partnership", "July 2028", new(2028, 3, 1), new(2028, 6, 1),
                [("Curriculum and training", 65000), ("Site services", 30000)]));
        await db.SaveChangesAsync(ct);
    }

    static HostVolunteer Volunteer(int orgId, string first, string last, int i, DateTime created) => new()
    {
        HostOrganizationId = orgId,
        FirstName = first,
        LastName = last,
        Email = $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}@example.org",
        Phone = $"(404) 555-{2000 + i:0000}",
        DateOfBirth = new(1970 + (i * 3 % 30), 1 + (i % 12), 1 + (i % 27)),
        Role = VolunteerCsv.Roles[i % VolunteerCsv.Roles.Length],
        // Six in ten approved, two in progress, two not started.
        VettingStatus = (i % 10) switch { < 6 => VettingStatus.Approved, < 8 => VettingStatus.InProgress, _ => VettingStatus.NotStarted },
        CreatedAt = created,
    };

    static HostInvoice Invoice(int orgId, int? eventId, string number, string description, string period, DateOnly issued, DateOnly due,
        (string Description, int Cents)[] lines, DateTime? paidOn = null)
    {
        var invoice = new HostInvoice
        {
            HostOrganizationId = orgId,
            HostEventId = eventId,
            Number = number,
            Description = description,
            Period = period,
            IssuedOn = issued,
            DueDate = due,
        };
        invoice.Lines.AddRange(lines.Select((l, i) => new HostInvoiceLine { Description = l.Description, AmountCents = l.Cents, SortOrder = i }));
        if (paidOn is { } at)
            invoice.Payments.Add(new HostInvoicePayment
            {
                IdempotencyKey = $"seed-{number}",
                AmountCents = lines.Sum(l => l.Cents),
                Status = HostPaymentStatus.Succeeded,
                ProcessorRef = $"fsv_seed_{number}",
                CardLast4 = "4242",
                PaidBy = "Grace Patel (HOST)",
                CreatedAt = at,
            });
        return invoice;
    }
}
