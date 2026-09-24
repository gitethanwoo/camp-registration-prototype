using System.Text.Json;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Admittance;

/// <summary>
/// Demo applications for the WSM Fall Marriage Retreat. The core seed reserves 31 of 40 couple
/// seats with no rows behind them; this adds those 31 as approved, paid couples so the count is
/// real, plus a review queue: 11 submitted, 7 under review (one waiting on the couple's answer and
/// one whose card hold lapsed), and 3 declined. The Johnsons have no application, so the demo
/// starts clean.
/// </summary>
public sealed class AdmittanceSeed : ISeedModule
{
    public int Order => 100;

    static readonly string[] Husbands = ["James", "Michael", "Chris", "Daniel", "Robert", "Brian", "Steven", "Tyler", "Matt", "Andrew", "Kevin", "Jason", "Mark", "Paul", "Eric", "Nathan", "Aaron", "Luke"];
    static readonly string[] Wives = ["Emily", "Sarah", "Amanda", "Grace", "Melissa", "Rachel", "Laura", "Hannah", "Jessica", "Megan", "Anna", "Lauren", "Kristen", "Julie", "Beth", "Natalie", "Erin", "Kate"];
    static readonly string[] Surnames = ["Carter", "Lee", "Wright", "Kim", "Nguyen", "Mitchell", "Brooks", "Turner", "Smith", "Brown", "Davis", "Miller", "Wilson", "Taylor", "Thomas", "Moore", "Hayes", "Price", "Bennett", "Reed", "Foster", "Graham", "Ward", "Cole", "Perry", "Long", "Bryant", "Russell", "Griffin", "Hughes", "Myers", "Ford", "Hamilton", "Wells", "Porter", "Hunter", "Hicks", "Crawford", "Boyd", "Mason", "Warren", "Dixon", "Ramos", "Burns", "Gordon", "Shaw", "Holmes", "Rice", "Robertson", "Black", "Daniels", "Palmer"];

    static readonly string[] Why =
    [
        "A couple at our church came back from this retreat talking about it for months. We want time away from the kids to actually talk.",
        "We've been married twelve years and realized we mostly talk about schedules. We want to reconnect.",
        "Our pastor recommended it after a hard year with a job change and a move. We want tools, not just a getaway.",
        "We loved the Spring Retreat and want to keep building on what we learned there.",
    ];
    static readonly string[] Goals =
    [
        "Time to slow down, reconnect, and grow in our marriage with practical tools and biblical teaching.",
        "Better ways to handle conflict, and a plan for a weekly date night we actually keep.",
        "Learning to pray together again and to listen before we fix.",
    ];
    static readonly string[] Heard = ["A friend or family member", "Our church", "A past WinShape event", "Social media"];

    public async Task RunAsync(CampDbContext db, CancellationToken ct)
    {
        if (await db.Set<AdmittanceApplication>().AnyAsync(ct)) return;
        var session = await db.Sessions.Include(s => s.Pools).Include(s => s.Program)
            .FirstOrDefaultAsync(s => s.Program.Slug == "fall-marriage-retreat", ct);
        if (session is null) return;
        var pool = session.Pools.OrderBy(p => p.SortOrder).First();

        var rng = new Random(1006);
        var now = DateTime.UtcNow;
        var used = new HashSet<string>();
        var n = 0;

        // Approved and paid: exactly the seats the core seed already reserved.
        for (var i = 0; i < pool.Reserved; i++)
        {
            var submitted = now.AddDays(-rng.Next(20, 60)).AddMinutes(-rng.Next(0, 1440));
            var app = NewCouple(db, session, rng, used, ref n, submitted);
            app.Stage = ApplicationStage.Approved;
            app.ReviewStartedAt = submitted.AddDays(1);
            app.DecidedAt = submitted.AddDays(rng.Next(2, 5));
            app.ReviewedBy = "Diane Carter (CET)";
            app.UpdatedAt = app.DecidedAt.Value;
            app.Hold = HoldStatus.Captured;
            app.SeatHeld = true;
            var order = new PaymentOrder
            {
                Household = app.Household,
                SessionId = session.Id,
                IdempotencyKey = $"seed-capture-{n}",
                ConfirmationCode = $"WS-{rng.Next(0x100000, 0xFFFFFF):X6}",
                PaymentOption = PaymentOption.Full,
                SubtotalCents = session.PriceCents,
                TotalCents = session.PriceCents,
                DueTodayCents = session.PriceCents,
                Status = OrderStatus.Paid,
                CreatedAt = app.DecidedAt.Value,
            };
            order.Operations.Add(new PaymentOperation { Kind = PaymentKind.Authorize, AmountCents = session.PriceCents, Succeeded = true, ProcessorRef = app.AuthorizationRef!, CardLast4 = app.CardLast4!, Reason = "Admittance application hold", CreatedAt = submitted });
            order.Operations.Add(new PaymentOperation { Kind = PaymentKind.Charge, AmountCents = session.PriceCents, Succeeded = true, ProcessorRef = $"fsv_cap_{rng.Next():x8}", CardLast4 = app.CardLast4!, Reason = "Captured on approval", CreatedAt = app.DecidedAt.Value });
            order.Registrations.Add(new Registration
            {
                SessionId = session.Id,
                PoolId = pool.Id,
                Person = app.Applicant,
                Status = RegistrationStatus.Confirmed,
                Grade = pool.GradeMin,
                PriceCents = session.PriceCents,
                PaidCents = session.PriceCents,
                HealthStatus = FormStatus.Complete,
                AnswersJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["spouse"] = $"{app.SpouseFirstName} {app.SpouseLastName}" }),
                CreatedAt = app.DecidedAt.Value,
            });
            db.Orders.Add(order);
        }

        // Submitted, waiting for someone to pick them up. Holds are 6 hours to 4 days old, so the
        // oldest shows as expiring (3 days or less left on a 7-day hold).
        for (var i = 0; i < 11; i++)
            NewCouple(db, session, rng, used, ref n, now.AddHours(-6 - (i * 9) - rng.Next(0, 3)));

        // Under review.
        for (var i = 0; i < 7; i++)
        {
            var submitted = now.AddDays(-2 - (i % 3)).AddHours(-rng.Next(0, 12));
            var app = NewCouple(db, session, rng, used, ref n, submitted);
            app.Stage = ApplicationStage.UnderReview;
            app.ReviewStartedAt = submitted.AddHours(20);
            app.ReviewedBy = "Diane Carter (CET)";
            app.UpdatedAt = app.ReviewStartedAt.Value;
            if (i == 0)
            {
                app.Stage = ApplicationStage.InfoRequested;
                app.InfoRequest = "Could you tell us a little more about what you're hoping to work on together? It helps us place you in a small group.";
                app.InfoRequestedAt = app.ReviewStartedAt.Value.AddHours(1);
                app.UpdatedAt = app.InfoRequestedAt.Value;
            }
            if (i == 1)
            {
                // Applied 9 days ago; the 7-day hold has lapsed.
                app.SubmittedAt = now.AddDays(-9);
                app.CreatedAt = app.SubmittedAt.Value.AddHours(-1);
                app.AuthorizedAt = app.SubmittedAt;
                app.AuthorizationExpiresAt = app.SubmittedAt + AdmittanceService.HoldLifetime;
                app.ReviewStartedAt = app.SubmittedAt.Value.AddDays(1);
                app.UpdatedAt = app.ReviewStartedAt.Value;
            }
        }

        // Declined, holds voided.
        for (var i = 0; i < 3; i++)
        {
            var submitted = now.AddDays(-rng.Next(10, 30));
            var app = NewCouple(db, session, rng, used, ref n, submitted);
            app.Stage = ApplicationStage.Declined;
            app.ReviewStartedAt = submitted.AddDays(1);
            app.DecidedAt = submitted.AddDays(3);
            app.ReviewedBy = "Diane Carter (CET)";
            app.DecisionNote = "Thank you for applying. This retreat is designed for couples married at least a year; we'd love to see you at a future event.";
            app.UpdatedAt = app.DecidedAt.Value;
            app.Hold = HoldStatus.Voided;
        }

        await db.SaveChangesAsync(ct);
        // Registrations were created through the order graph; point them at their households.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE r SET HouseholdId = p.HouseholdId FROM Registrations r JOIN People p ON p.Id = r.PersonId WHERE r.HouseholdId = 0", ct);
        var approved = await db.Set<AdmittanceApplication>().Where(a => a.Stage == ApplicationStage.Approved && a.RegistrationId == null).ToListAsync(ct);
        var regs = await db.Registrations.Where(r => r.SessionId == session.Id).Select(r => new { r.Id, r.PersonId, r.OrderId }).ToListAsync(ct);
        foreach (var a in approved)
        {
            var reg = regs.Single(r => r.PersonId == a.ApplicantPersonId);
            a.RegistrationId = reg.Id;
            a.OrderId = reg.OrderId;
        }
        db.AuditEvents.Add(new AuditEvent { Actor = "system", Action = "seed", EntityType = "Database", EntityId = "-", Detail = "Seeded WSM retreat applications.", CreatedAt = now });
        await db.SaveChangesAsync(ct);
    }

    static AdmittanceApplication NewCouple(CampDbContext db, Session session, Random rng, HashSet<string> used, ref int n, DateTime submitted)
    {
        string him, her, last;
        do
        {
            him = Husbands[rng.Next(Husbands.Length)];
            her = Wives[rng.Next(Wives.Length)];
            last = Surnames[rng.Next(Surnames.Length)];
        } while (!used.Add(last) && used.Count < Surnames.Length);
        n++;
        var email = $"{her.ToLowerInvariant()}.{last.ToLowerInvariant()}{n}@example.com";
        var household = new Household { Name = last, Email = email, Phone = $"(678) 555-{rng.Next(1000, 9999)}", City = rng.Next(3) == 0 ? "Rome, GA" : "Atlanta, GA" };
        var wife = new Person { FirstName = her, LastName = last, DateOfBirth = new(1980 + rng.Next(0, 15), rng.Next(1, 13), rng.Next(1, 28)), Gender = Gender.Female, IsAdult = true, Role = "Primary", Email = email };
        var husband = new Person { FirstName = him, LastName = last, DateOfBirth = new(1978 + rng.Next(0, 15), rng.Next(1, 13), rng.Next(1, 28)), Gender = Gender.Male, IsAdult = true, Role = "Co-owner", Email = $"{him.ToLowerInvariant()}.{last.ToLowerInvariant()}{n}@example.com" };
        household.Members.AddRange(wife, husband);
        db.Households.Add(household);

        var answers = new Dictionary<string, string>
        {
            ["yearsMarried"] = ApplicationForm.Questions[0].Options[1 + rng.Next(4)],
            ["why"] = Why[rng.Next(Why.Length)],
            ["goals"] = Goals[rng.Next(Goals.Length)],
            ["heardFrom"] = Heard[rng.Next(Heard.Length)],
            ["attendedBefore"] = rng.Next(3) == 0 ? "Yes" : "No",
        };
        if (answers["attendedBefore"] == "Yes") answers["attendedWhich"] = "Spring Marriage Retreat, 2026";
        if (rng.Next(4) == 0) answers["needs"] = "Gluten-free meals for " + him;

        var app = new AdmittanceApplication
        {
            Household = household,
            SessionId = session.Id,
            Applicant = wife,
            Spouse = husband,
            SpouseFirstName = husband.FirstName,
            SpouseLastName = husband.LastName,
            SpouseEmail = husband.Email,
            Stage = ApplicationStage.Submitted,
            CurrentStep = ApplicationForm.Sections.Length,
            AnswersJson = JsonSerializer.Serialize(answers),
            CreatedAt = submitted.AddMinutes(-35),
            UpdatedAt = submitted,
            SubmittedAt = submitted,
            Hold = HoldStatus.Authorized,
            AmountCents = session.PriceCents,
            AuthorizationRef = $"fsv_auth_seed{n:D4}",
            CardLast4 = $"{rng.Next(1000, 9999)}",
            AuthorizedAt = submitted,
            AuthorizationExpiresAt = submitted + AdmittanceService.HoldLifetime,
        };
        db.Add(app);
        return app;
    }
}
