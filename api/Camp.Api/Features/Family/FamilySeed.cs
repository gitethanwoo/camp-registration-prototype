using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Family;

/// <summary>
/// The Johnsons attended the WSM marriage retreat in 2026 (spec Part 3), so it shows under Past on
/// F4. It's its own unpublished program: the published Fall Marriage Retreat belongs to the
/// admittance slice, and a past session there would show up on its program page.
/// </summary>
public sealed class FamilySeed : ISeedModule
{
    public const string PastRetreatSlug = "spring-marriage-retreat-2026";

    public int Order => 100;

    public async Task RunAsync(CampDbContext db, CancellationToken ct)
    {
        if (await db.Programs.AnyAsync(p => p.Slug == PastRetreatSlug, ct)) return;
        var wsm = await db.Ministries.FirstOrDefaultAsync(m => m.Code == "WSM", ct);
        var johnson = await db.Households.Include(h => h.Members).FirstOrDefaultAsync(h => h.Email == Seed.JohnsonEmail, ct);
        if (wsm is null || johnson is null) return;

        var program = new CampProgram
        {
            MinistryId = wsm.Id,
            Slug = PastRetreatSlug,
            Name = "Spring Marriage Retreat",
            Tagline = "A weekend away for married couples.",
            Description = "Three days at the retreat center with teaching sessions, time alone together, and small-group conversations.",
            Type = ProgramType.Admittance,
            HealthMechanism = HealthMechanism.Embedded,
            Location = "WinShape Retreat · Rome, GA",
            ImageUrl = "/images/retreat.jpg",
            IsPublished = false,
        };
        var session = new Session
        {
            Program = program,
            Name = "Spring 2026",
            StartDate = new(2026, 4, 17),
            EndDate = new(2026, 4, 19),
            PriceCents = 90000, // per couple
            DepositCents = 0,
            BalanceDueDate = new(2026, 3, 1),
        };
        var pool = new CapacityPool { Session = session, Name = "Attendees", GradeMin = 99, GradeMax = 99, Capacity = 80, SortOrder = 1 };
        session.Pools.Add(pool);
        program.Sessions.Add(session);
        db.Programs.Add(program);
        await db.SaveChangesAsync(ct);

        var paidAt = new DateTime(2026, 2, 9, 15, 12, 0, DateTimeKind.Utc);
        var order = new PaymentOrder
        {
            HouseholdId = johnson.Id,
            SessionId = session.Id,
            IdempotencyKey = $"seed-{PastRetreatSlug}",
            ConfirmationCode = "WS-2A6C41",
            PaymentOption = PaymentOption.Full,
            SubtotalCents = session.PriceCents,
            TotalCents = session.PriceCents,
            DueTodayCents = session.PriceCents,
            Status = OrderStatus.Paid,
            CreatedAt = paidAt,
        };
        order.Operations.Add(new PaymentOperation { Kind = PaymentKind.Charge, AmountCents = session.PriceCents, Succeeded = true, ProcessorRef = "fsv_seed2026wsm", CardLast4 = "4242", CreatedAt = paidAt });
        foreach (var adult in johnson.Members.Where(m => m.IsAdult).OrderBy(m => m.Id))
        {
            order.Registrations.Add(new Registration
            {
                SessionId = session.Id,
                PoolId = pool.Id,
                PersonId = adult.Id,
                HouseholdId = johnson.Id,
                Status = RegistrationStatus.Confirmed,
                Grade = 99,
                PriceCents = session.PriceCents / 2, // the couple price, split per person
                PaidCents = session.PriceCents / 2,
                HealthStatus = FormStatus.NotRequired,
                CreatedAt = paidAt,
            });
            pool.Reserved++;
        }
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
    }
}
