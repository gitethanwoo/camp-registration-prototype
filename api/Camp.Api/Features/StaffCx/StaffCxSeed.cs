using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.StaffCx;

/// <summary>
/// Demo rows for the front-desk screens: a second Day Camp week to transfer into (with real filler
/// registrations so its availability is honest), the Jordan Lee duplicate pair, discount requests
/// from hosts and partners, household notes, and open transfer requests. Nothing here touches the
/// canonical ON Session 3 or Day Camp June 12–16 counts.
/// </summary>
public sealed class StaffCxSeed(TimeProvider clock) : ISeedModule
{
    public const string WeekTwoName = "June week 2";
    public const string JordanEmailA = "jlee@example.com";
    public const string JordanEmailB = "jordan.lee.family@example.com";

    public int Order => 100;

    public async Task RunAsync(CampDbContext db, CancellationToken ct)
    {
        // The core seed shares this context and fixes Registration.HouseholdId with raw SQL,
        // so anything it left tracked is stale. Start from the database.
        db.ChangeTracker.Clear();
        var day = await db.Programs.Include(p => p.Sessions).ThenInclude(s => s.Pools).Include(p => p.Waivers)
            .FirstOrDefaultAsync(p => p.Slug == "day-camp-atlanta", ct);
        if (day is null || day.Sessions.Any(s => s.Name == WeekTwoName)) return;
        var weekOne = day.Sessions.OrderBy(s => s.StartDate).First();
        var rng = new Random(4219);
        var now = clock.UtcNow();

        // ── Day Camp · Atlanta, second week ──
        var weekTwo = new Session
        {
            ProgramId = day.Id,
            Name = WeekTwoName,
            StartDate = new(2028, 6, 19),
            EndDate = new(2028, 6, 23),
            PriceCents = weekOne.PriceCents,
            DepositCents = weekOne.DepositCents,
            PlanInstallments = weekOne.PlanInstallments,
            BalanceDueDate = weekOne.BalanceDueDate,
        };
        var pools = Enumerable.Range(1, 6)
            .Select(g => new CapacityPool { Session = weekTwo, Name = $"Grade {g}", GradeMin = g, GradeMax = g, Capacity = 20, SortOrder = g })
            .ToArray();
        weekTwo.Pools.AddRange(pools);
        db.Sessions.Add(weekTwo);
        await db.SaveChangesAsync(ct);

        // Grade 6 has 5 left (the C10/F8 concepts); Grade 4 is full, and Jordan Lee (A) holds one of its seats.
        var targets = new[] { 9, 12, 10, 19, 13, 15 };
        for (var i = 0; i < pools.Length; i++)
            for (var n = 0; n < targets[i]; n++)
                AddFillerRegistration(db, rng, weekTwo, pools[i], day.Waivers, now);
        await db.SaveChangesAsync(ct);
        // Registration.HouseholdId has no navigation, so it's filled in once the households have ids.
        await db.Database.ExecuteSqlAsync($"UPDATE r SET HouseholdId = p.HouseholdId FROM Registrations r JOIN People p ON p.Id = r.PersonId WHERE r.SessionId = {weekTwo.Id}", ct);

        await SeedJordanLee(db, weekTwo, pools[3], day.Waivers, now, ct);
        await SeedDiscountRequests(db, day.Id, now, ct);
        await SeedNotes(db, now, ct);
        await SeedTransfers(db, weekOne, weekTwo, now, ct);
        await db.SaveChangesAsync(ct);
    }

    static async Task SeedJordanLee(CampDbContext db, Session weekTwo, CapacityPool grade4, List<WaiverTemplate> waivers, DateTime now, CancellationToken ct)
    {
        // Two accounts for one family: same child name + DOB + phone digits (FR-7).
        var a = new Household { Name = "Lee", Email = JordanEmailA, Phone = "(770) 555-0103", City = "Marietta, GA", SalesforceId = "001Hs00004JLeeA" };
        a.Members.AddRange(
            new Person { FirstName = "Chris", LastName = "Lee", DateOfBirth = new(1984, 2, 14), Gender = Gender.Male, IsAdult = true, Role = "Primary", Email = JordanEmailA },
            new Person { FirstName = "Jordan", LastName = "Lee", DateOfBirth = new(2018, 10, 3), Gender = Gender.Male, Allergies = "Bee stings" });
        var b = new Household { Name = "Lee", Email = JordanEmailB, Phone = "770-555-0103", City = "Marietta, GA", SalesforceId = "001Hs00004JLeeB" };
        b.Members.AddRange(
            new Person { FirstName = "Chris", LastName = "Lee", DateOfBirth = new(1984, 2, 14), Gender = Gender.Male, IsAdult = true, Role = "Primary", Email = JordanEmailB },
            new Person { FirstName = "Jordan", LastName = "Lee", DateOfBirth = new(2018, 10, 3), Gender = Gender.Male });
        db.Households.AddRange(a, b);
        await db.SaveChangesAsync(ct);

        // Account A: confirmed on an active 3-installment plan, deposit paid.
        var created = now.AddDays(-41);
        var order = new PaymentOrder
        {
            HouseholdId = a.Id,
            SessionId = weekTwo.Id,
            IdempotencyKey = Guid.NewGuid().ToString(),
            ConfirmationCode = "WS-7A3L0E",
            PaymentOption = PaymentOption.Plan,
            SubtotalCents = weekTwo.PriceCents,
            TotalCents = weekTwo.PriceCents,
            DueTodayCents = weekTwo.DepositCents,
            Status = OrderStatus.Paid,
            CreatedAt = created,
        };
        order.Operations.Add(new PaymentOperation { Kind = PaymentKind.Charge, AmountCents = weekTwo.DepositCents, Succeeded = true, ProcessorRef = "fsv_jlee0001", CardLast4 = "4417", CreatedAt = created });
        var seq = 1;
        foreach (var item in Pricing.PlanSchedule(weekTwo, weekTwo.PriceCents - weekTwo.DepositCents, DateOnly.FromDateTime(created)))
            order.Installments.Add(new Installment { Sequence = seq++, DueDate = item.DueDate!.Value, AmountCents = item.AmountCents, Status = InstallmentStatus.Scheduled });
        var jordanA = a.Members[1];
        var reg = new Registration
        {
            Order = order,
            SessionId = weekTwo.Id,
            PoolId = grade4.Id,
            PersonId = jordanA.Id,
            HouseholdId = a.Id,
            Status = RegistrationStatus.Confirmed,
            Grade = Eligibility.GradeFor(jordanA.DateOfBirth, weekTwo.StartDate),
            PriceCents = weekTwo.PriceCents,
            PaidCents = weekTwo.DepositCents,
            HealthStatus = FormStatus.Complete,
            AnswersJson = """{"tshirt":"Youth M","swim":"Beginner","church":"Yes","churchName":"Grace Community Church"}""",
            CreatedAt = created,
        };
        foreach (var w in waivers)
            reg.WaiverAcceptances.Add(new WaiverAcceptance { WaiverTemplateId = w.Id, Version = w.Version, SignerName = "Chris Lee", AcceptedAt = created });
        order.Registrations.Add(reg);
        db.Orders.Add(order);
        grade4.Reserved++;

        // Account B: first in line for the same (full) Grade 4 pool.
        db.WaitlistEntries.Add(new WaitlistEntry { PoolId = grade4.Id, PersonId = b.Members[1].Id, HouseholdId = b.Id, Position = 1, Status = WaitlistStatus.Waiting, CreatedAt = now.AddDays(-12) });
        await db.SaveChangesAsync(ct);
    }

    static async Task SeedDiscountRequests(CampDbContext db, int dayProgramId, DateTime now, CancellationToken ct)
    {
        var codes = await db.DiscountCodes.ToDictionaryAsync(d => d.Code, ct);
        DiscountCode Code(string code, DiscountKind kind, int value, string by)
        {
            if (codes.TryGetValue(code, out var existing)) return existing;
            var d = new DiscountCode { Code = code, Kind = kind, Value = value, Status = DiscountStatus.PendingApproval, CreatedBy = by };
            db.DiscountCodes.Add(d);
            return d;
        }
        DiscountRequest Req(DiscountCode code, RequesterType type, string by, string org, int? maxUses, bool stack, bool overrides, string note, int daysAgo) => new()
        {
            DiscountCode = code,
            RequesterType = type,
            RequestedBy = by,
            Organization = org,
            ProgramId = dayProgramId,
            ValidFrom = new(2027, 12, 1),
            ValidTo = new(2028, 6, 23),
            MaxUses = maxUses,
            Stackable = stack,
            OverridesOtherCodes = overrides,
            RequesterNote = note,
            RequestedAt = now.AddDays(-daysAgo),
            Decision = ReviewDecision.Pending,
        };

        const string renata = "Renata Alvarez";
        const string grace = "Grace Community Church";
        db.Set<DiscountRequest>().AddRange(
            Req(Code("SUMMERFUN", DiscountKind.Flat, 5000, $"{renata} ({grace})"), RequesterType.Host, renata, grace, 60, false, false,
                "For families from our Wednesday night program. Most of them are first-time campers.", 6),
            Req(Code("WELCOME2028", DiscountKind.Percent, 15, $"{renata} ({grace})"), RequesterType.Host, renata, grace, 40, false, false,
                "New-member families who joined the church this year.", 5),
            Req(Code("CHURCH20", DiscountKind.Percent, 20, $"{renata} ({grace})"), RequesterType.Host, renata, grace, 25, true, false,
                "Church staff kids. We'd like this to stack with EARLYBIRD.", 3),
            Req(Code("RIVERKIDS", DiscountKind.Flat, 2500, "Tom Becker (Riverwood Church)"), RequesterType.Partner, "Tom Becker", "Riverwood Church", 30, false, false,
                "Riverwood is sending a van of kids for the second week.", 2),
            Req(Code("CITYREACH", DiscountKind.Percent, 10, "Alicia Grant (City Reach Ministries)"), RequesterType.Partner, "Alicia Grant", "City Reach Ministries", null, false, false,
                "Neighborhood outreach families; we cover the deposit separately.", 1));

        var rejected = Req(Code("FREEWEEK", DiscountKind.Percent, 100, "Tom Becker (Riverwood Church)"), RequesterType.Partner, "Tom Becker", "Riverwood Church", 5, false, true,
            "Five full scholarships for the second week.", 20);
        rejected.Decision = ReviewDecision.Rejected;
        rejected.ReviewedBy = "Diane Carter (CET)";
        rejected.ReviewedAt = now.AddDays(-18);
        rejected.ReviewNote = "A 100% code has to go through the scholarship fund, not a discount. Sent Tom the scholarship form.";
        db.Set<DiscountRequest>().Add(rejected);
        await db.SaveChangesAsync(ct);
    }

    static async Task SeedNotes(CampDbContext db, DateTime now, CancellationToken ct)
    {
        var johnson = await db.Households.FirstOrDefaultAsync(h => h.Email == Seed.JohnsonEmail, ct);
        if (johnson is null) return;
        db.Set<HouseholdNote>().AddRange(
            new HouseholdNote { HouseholdId = johnson.Id, Author = "Brian Hughes (Operations)", Body = "David asked whether Day Camp has an early drop-off. Told him 8:30 AM at the gym entrance.", CreatedAt = now.AddDays(-9) },
            new HouseholdNote { HouseholdId = johnson.Id, Author = "Diane Carter (CET)", Body = "Maria called to ask whether Avery can ride home with the Hendersons on Fridays. Told her to add them as an authorized pickup on the registration.", CreatedAt = now.AddDays(-2) });
        db.Set<HouseholdVerification>().Add(new HouseholdVerification { HouseholdId = johnson.Id, ItemKey = "guardians", CheckedBy = "Diane Carter (CET)", CheckedAt = now.AddDays(-2) });
        await db.SaveChangesAsync(ct);
    }

    static async Task SeedTransfers(CampDbContext db, Session weekOne, Session weekTwo, DateTime now, CancellationToken ct)
    {
        async Task Add(int grade, string reason, int hoursAgo, TransferStatus status = TransferStatus.Pending, string? note = null)
        {
            var reg = await db.Registrations.AsNoTracking()
                .Where(r => r.SessionId == weekOne.Id && r.Grade == grade && r.Status == RegistrationStatus.Confirmed)
                .OrderBy(r => r.Id).FirstAsync(ct);
            var guardian = await db.People.Where(p => p.HouseholdId == reg.HouseholdId && p.IsAdult).OrderBy(p => p.Id).Select(p => p.FirstName + " " + p.LastName).FirstAsync(ct);
            db.Set<TransferRequest>().Add(new TransferRequest
            {
                RegistrationId = reg.Id,
                HouseholdId = reg.HouseholdId,
                FromSessionId = weekOne.Id,
                FromPoolId = reg.PoolId,
                ToSessionId = weekTwo.Id,
                Reason = reason,
                RequestedBy = guardian,
                CreatedAt = now.AddHours(-hoursAgo),
                Status = status,
                DecidedBy = status == TransferStatus.Pending ? null : "Diane Carter (CET)",
                DecidedAt = status == TransferStatus.Pending ? null : now.AddHours(-hoursAgo + 20),
                DecisionNote = note,
            });
        }

        await Add(6, "We'll be at a family reunion in Savannah the first week of June.", 26);
        // Grade 4 in week 2 filled after this was submitted, so staff can't approve it (C10 trap).
        await Add(4, "Swim team meet on June 14; the second week works for us.", 50);
        await Add(2, "Wanted to go with a cousin in the second week.", 400, TransferStatus.Denied,
            "Family called back and decided to keep the first week. Closed at their request.");
    }

    // ── filler ──
    static readonly string[] Boys = ["Liam", "Noah", "Oliver", "Elijah", "James", "Lucas", "Henry", "Mason", "Ethan", "Caleb", "Owen", "Wyatt", "Isaac", "Levi", "Micah"];
    static readonly string[] Girls = ["Olivia", "Emma", "Ava", "Sophia", "Isabella", "Charlotte", "Amelia", "Harper", "Evelyn", "Abigail", "Ella", "Grace", "Lily", "Chloe", "Hannah"];
    static readonly string[] Last = ["Bennett", "Coleman", "Foster", "Graham", "Hayes", "Jenkins", "Kelly", "Long", "Murphy", "Perry", "Reed", "Russell", "Sanders", "Stewart", "Watson", "Wood", "Brooks", "Cooper", "Price", "Ward"];
    static readonly string[] Parents = ["Beth", "Kara", "Nate", "Tessa", "Greg", "Holly", "Jon", "Paige", "Seth", "Dana"];

    static void AddFillerRegistration(CampDbContext db, Random rng, Session session, CapacityPool pool, List<WaiverTemplate> waivers, DateTime now)
    {
        var gender = rng.Next(2) == 0 ? Gender.Male : Gender.Female;
        var last = Last[rng.Next(Last.Length)];
        var parent = Parents[rng.Next(Parents.Length)];
        var email = $"{parent.ToLowerInvariant()}.{last.ToLowerInvariant()}{rng.Next(100, 999)}@example.com";
        var h = new Household { Name = last, Email = email, Phone = $"(678) 555-{rng.Next(1000, 9999)}", City = "Atlanta, GA" };
        h.Members.Add(new Person { FirstName = parent, LastName = last, DateOfBirth = new(1987, 6, 1), Gender = Gender.Female, IsAdult = true, Role = "Primary", Email = email });
        var grade = pool.GradeMin;
        var child = new Person
        {
            FirstName = gender == Gender.Male ? Boys[rng.Next(Boys.Length)] : Girls[rng.Next(Girls.Length)],
            LastName = last,
            DateOfBirth = new DateOnly(session.StartDate.Year - grade - 6, 9, 2).AddDays(rng.Next(0, 360)),
            Gender = gender,
        };
        h.Members.Add(child);
        db.Households.Add(h);

        var balanceDue = rng.Next(4) == 0;
        var paid = balanceDue ? session.DepositCents : session.PriceCents;
        var created = now.AddDays(-rng.Next(1, 60)).AddMinutes(-rng.Next(0, 1440));
        var order = new PaymentOrder
        {
            Household = h,
            SessionId = session.Id,
            IdempotencyKey = Guid.NewGuid().ToString(),
            ConfirmationCode = $"WS-{rng.Next(0x100000, 0xFFFFFF):X6}",
            PaymentOption = balanceDue ? PaymentOption.Deposit : PaymentOption.Full,
            SubtotalCents = session.PriceCents,
            TotalCents = session.PriceCents,
            DueTodayCents = paid,
            Status = OrderStatus.Paid,
            CreatedAt = created,
        };
        order.Operations.Add(new PaymentOperation { Kind = PaymentKind.Charge, AmountCents = paid, Succeeded = true, ProcessorRef = $"fsv_{rng.Next():x8}", CardLast4 = $"{rng.Next(1000, 9999)}", CreatedAt = created });
        var reg = new Registration
        {
            Order = order,
            SessionId = session.Id,
            PoolId = pool.Id,
            Person = child,
            Status = RegistrationStatus.Confirmed,
            Grade = grade,
            PriceCents = session.PriceCents,
            PaidCents = paid,
            HealthStatus = FormStatus.Complete,
            AnswersJson = """{"tshirt":"Youth M","church":"No"}""",
            CreatedAt = created,
        };
        foreach (var w in waivers)
            reg.WaiverAcceptances.Add(new WaiverAcceptance { WaiverTemplateId = w.Id, Version = w.Version, SignerName = h.Members[0].FullName, AcceptedAt = created });
        order.Registrations.Add(reg);
        db.Orders.Add(order);
        pool.Reserved++;
    }
}
