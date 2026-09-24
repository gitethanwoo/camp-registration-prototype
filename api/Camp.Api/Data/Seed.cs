using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Data;

/// <summary>
/// Seeds the canonical dataset from winshape-screen-specs.md Part 3. Filler registrations are
/// generated so every count on every screen is a real row count, not a hard-coded number.
/// </summary>
public static class Seed
{
    public const string JohnsonEmail = "maria.johnson@example.com";

    public static async Task RunAsync(CampDbContext db, TimeProvider clock)
    {
        if (await db.Ministries.AnyAsync()) return;
        var rng = new Random(2028);
        var now = clock.UtcNow();

        var wsc = new Ministry { Code = "WSC", Name = "WSC Camps" };
        var wsm = new Ministry { Code = "WSM", Name = "WSM Marriage" };
        var wsl = new Ministry { Code = "WSL", Name = "WSL Leadership" };
        db.Ministries.AddRange(wsc, wsm, wsl,
            new Ministry { Code = "WSCP", Name = "WSCP College Program" },
            new Ministry { Code = "WSH", Name = "WSH Homes" });

        // ── Overnight Camp (ON) ────────────────────────────────────────────────
        var on = new CampProgram
        {
            Ministry = wsc,
            Slug = "overnight-camp",
            Name = "Overnight Camp",
            Tagline = "Six-day camp for grades 3–8.",
            Description = "A week of cabins, lake days, and campfires on the mountain campus. Campers are grouped by grade in cabins of 12 with trained college-age counselors, and pick three activities to rotate through each day.",
            Type = ProgramType.Standard,
            HealthMechanism = HealthMechanism.CampDoc,
            Location = "WinShape Camps · Mount Berry, GA",
            ImageUrl = "/images/overnight.jpg",
            IsPublished = true,
        };
        var onS3 = new Session
        {
            Program = on,
            Name = "Session 3",
            StartDate = new(2028, 7, 10),
            EndDate = new(2028, 7, 15),
            PriceCents = 145000,
            DepositCents = 25000,
            PlanInstallments = 3,
            BalanceDueDate = new(2028, 6, 1),
        };
        var onPools = new[]
        {
            Pool(onS3, "Boys G3–5", Gender.Male, 3, 5, 50, 1),
            Pool(onS3, "Boys G6–8", Gender.Male, 6, 8, 50, 2),
            Pool(onS3, "Girls G3–5", Gender.Female, 3, 5, 50, 3),
            Pool(onS3, "Girls G6–8", Gender.Female, 6, 8, 50, 4),
        };
        onS3.Pools.AddRange(onPools);
        on.Sessions.Add(onS3);
        AddCommonQuestions(on);
        on.Waivers.AddRange(
            new WaiverTemplate { Title = "Participant Release and Waiver of Liability", Version = 3, EffectiveDate = new(2027, 11, 1), PerParticipant = true, Body = LiabilityText("Overnight Camp") },
            new WaiverTemplate { Title = "Program Policies", Version = 2, EffectiveDate = new(2027, 11, 1), PerParticipant = false, Body = PoliciesText });

        // ── Day Camp · Atlanta (WSCC, hosted by Grace Community Church) ────────
        var day = new CampProgram
        {
            Ministry = wsc,
            Slug = "day-camp-atlanta",
            Name = "Day Camp · Atlanta",
            Tagline = "A week of day camp for rising 1st–6th graders.",
            Description = "WinShape Day Camp comes to Grace Community Church for one week. Campers spend 9 AM–4 PM in grade-based groups with games, Bible time, swimming, and crafts, led by WinShape staff and church volunteers.",
            Type = ProgramType.Standard,
            HealthMechanism = HealthMechanism.Embedded,
            Location = "Grace Community Church · Atlanta, GA",
            HostOrganization = "Grace Community Church",
            ImageUrl = "/images/daycamp.jpg",
            IsPublished = true,
        };
        var dayS = new Session
        {
            Program = day,
            Name = "June week",
            StartDate = new(2028, 6, 12),
            EndDate = new(2028, 6, 16),
            PriceCents = 32500,
            DepositCents = 10000,
            PlanInstallments = 3,
            BalanceDueDate = new(2028, 5, 1),
        };
        var dayPools = Enumerable.Range(1, 6).Select(g => Pool(dayS, $"Grade {g}", null, g, g, 20, g)).ToArray();
        dayS.Pools.AddRange(dayPools);
        day.Sessions.Add(dayS);
        AddCommonQuestions(day);
        day.Waivers.AddRange(
            new WaiverTemplate { Title = "Participant Release and Waiver of Liability", Version = 3, EffectiveDate = new(2027, 11, 1), PerParticipant = true, Body = LiabilityText("Day Camp") },
            new WaiverTemplate { Title = "Photo and Media Release", Version = 2, EffectiveDate = new(2027, 11, 1), PerParticipant = true, Body = PhotoText },
            new WaiverTemplate { Title = "Program Policies", Version = 2, EffectiveDate = new(2027, 11, 1), PerParticipant = false, Body = PoliciesText });

        // ── Admittance / cohort programs: discoverable, registration out of prototype scope ──
        var retreat = new CampProgram
        {
            Ministry = wsm,
            Slug = "fall-marriage-retreat",
            Name = "Fall Marriage Retreat",
            Tagline = "A weekend away for married couples.",
            Description = "Three days at the retreat center with teaching sessions, time alone together, and small-group conversations. Couples apply first; cards are authorized, not charged, until the application is approved.",
            Type = ProgramType.Admittance,
            HealthMechanism = HealthMechanism.Embedded,
            Location = "WinShape Retreat · Rome, GA",
            ImageUrl = "/images/retreat.jpg",
            IsPublished = true,
        };
        var retreatS = new Session { Program = retreat, Name = "Fall 2028", StartDate = new(2028, 10, 6), EndDate = new(2028, 10, 8), PriceCents = 90000, DepositCents = 0, BalanceDueDate = new(2028, 9, 1) };
        retreatS.Pools.Add(new CapacityPool { Name = "Couples", GradeMin = 99, GradeMax = 99, Capacity = 40, Reserved = 31, SortOrder = 1 });
        retreat.Sessions.Add(retreatS);

        var cohort = new CampProgram
        {
            Ministry = wsl,
            Slug = "emerging-leaders-cohort",
            Name = "Emerging Leaders Cohort",
            Tagline = "A leadership cohort for teams, registered by a group leader.",
            Description = "A three-day leadership intensive. A group leader registers the cohort; each attendee then completes their own forms by secure link.",
            Type = ProgramType.Cohort,
            HealthMechanism = HealthMechanism.Embedded,
            Location = "WinShape Retreat · Rome, GA",
            ImageUrl = "/images/leaders.jpg",
            IsPublished = true,
        };
        var cohortS = new Session { Program = cohort, Name = "September 2028", StartDate = new(2028, 9, 13), EndDate = new(2028, 9, 15), PriceCents = 45000, DepositCents = 0, BalanceDueDate = new(2028, 8, 1) };
        cohortS.Pools.Add(new CapacityPool { Name = "Attendees", GradeMin = 99, GradeMax = 99, Capacity = 60, Reserved = 14, SortOrder = 1 });
        cohort.Sessions.Add(cohortS);

        db.Programs.AddRange(on, day, retreat, cohort);

        db.DiscountCodes.AddRange(
            new DiscountCode { Code = "EARLYBIRD", Kind = DiscountKind.Percent, Value = 10, Status = DiscountStatus.Approved, CreatedBy = "Diane Carter" },
            // Host-created and awaiting admin approval: inert, and looks invalid to guests (FR-62).
            new DiscountCode { Code = "SUMMERFUN", Kind = DiscountKind.Flat, Value = 5000, Status = DiscountStatus.PendingApproval, CreatedBy = "Renata Alvarez (Grace Community Church)" },
            new DiscountCode { Code = "WELCOME2028", Kind = DiscountKind.Percent, Value = 15, Status = DiscountStatus.PendingApproval, CreatedBy = "Renata Alvarez (Grace Community Church)" });

        // ── The Johnson family (returning) ─────────────────────────────────────
        var johnson = new Household { Name = "Johnson", Email = JohnsonEmail, Phone = "(404) 555-0142", City = "Atlanta, GA", SalesforceId = "001Hs00003JhnSN" };
        johnson.Members.AddRange(
            new Person { FirstName = "Maria", LastName = "Johnson", DateOfBirth = new(1986, 5, 2), Gender = Gender.Female, IsAdult = true, Role = "Primary", Email = JohnsonEmail },
            new Person { FirstName = "David", LastName = "Johnson", DateOfBirth = new(1984, 11, 20), Gender = Gender.Male, IsAdult = true, Role = "Co-owner", Email = "david.johnson@example.com" },
            new Person { FirstName = "Avery", LastName = "Johnson", DateOfBirth = new(2017, 3, 4), Gender = Gender.Male, Allergies = "Peanuts (mild)" },
            new Person { FirstName = "Mia", LastName = "Johnson", DateOfBirth = new(2019, 8, 19), Gender = Gender.Female, Dietary = "Vegetarian" });
        db.Households.Add(johnson);
        await db.SaveChangesAsync();

        // ── Filler registrations so the counts are real ────────────────────────
        var fillerTargets = new Dictionary<CapacityPool, int>
        {
            [onPools[0]] = 46,
            [onPools[1]] = 50,
            [onPools[2]] = 44,
            [onPools[3]] = 46,
            [dayPools[0]] = 15,
            [dayPools[1]] = 17,
            [dayPools[2]] = 14,
            [dayPools[3]] = 11,
            [dayPools[4]] = 16,
            [dayPools[5]] = 8,
        };
        var onIndex = 0;
        foreach (var (pool, count) in fillerTargets)
        {
            var session = pool.Session;
            var program = session.Program;
            for (var i = 0; i < count; i++)
            {
                var isOn = program == on;
                // ON Session 3 readiness (spec Part 3): 27 unique need attention —
                // 19 CampDoc incomplete (#0–18), 6 waivers missing (#16–21), 8 balance due (#19–26).
                var idx = isOn ? onIndex++ : -1;
                var campDocIncomplete = isOn && idx <= 18;
                var waiverMissing = isOn && idx is >= 16 and <= 21;
                var balanceDue = isOn ? idx is >= 19 and <= 26 : rng.Next(4) == 0;

                var gender = pool.Gender ?? (rng.Next(2) == 0 ? Gender.Male : Gender.Female);
                var grade = rng.Next(pool.GradeMin, pool.GradeMax + 1);
                var (household, child) = FillerFamily(rng, gender, grade, session.StartDate);
                db.Households.Add(household);

                var created = now.AddDays(-rng.Next(1, 60)).AddMinutes(-rng.Next(0, 1440));
                var option = balanceDue ? PaymentOption.Deposit : PaymentOption.Full;
                var paid = balanceDue ? session.DepositCents : session.PriceCents;
                var order = new PaymentOrder
                {
                    Household = household,
                    SessionId = session.Id,
                    IdempotencyKey = Guid.NewGuid().ToString(),
                    ConfirmationCode = $"WS-{rng.Next(0x100000, 0xFFFFFF):X6}",
                    PaymentOption = option,
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
                    HouseholdId = 0,
                    Status = RegistrationStatus.Confirmed,
                    Grade = grade,
                    PriceCents = session.PriceCents,
                    PaidCents = paid,
                    HealthStatus = program.HealthMechanism == HealthMechanism.CampDoc
                        ? (campDocIncomplete ? FormStatus.Incomplete : FormStatus.Complete)
                        : FormStatus.Complete,
                    AnswersJson = """{"tshirt":"Youth M","church":"No"}""",
                    CreatedAt = created,
                };
                if (!waiverMissing)
                    foreach (var w in program.Waivers)
                        reg.WaiverAcceptances.Add(new WaiverAcceptance { WaiverTemplateId = w.Id, Version = w.Version, SignerName = household.Members[0].FullName, AcceptedAt = created });
                order.Registrations.Add(reg);
                db.Orders.Add(order);
                pool.Reserved++;
            }
        }
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("UPDATE r SET HouseholdId = p.HouseholdId FROM Registrations r JOIN People p ON p.Id = r.PersonId");

        // Waitlist: 9, all Boys G6–8 (the only full pool).
        for (var i = 1; i <= 9; i++)
        {
            var (household, child) = FillerFamily(rng, Gender.Male, rng.Next(6, 9), onS3.StartDate);
            db.Households.Add(household);
            await db.SaveChangesAsync();
            db.WaitlistEntries.Add(new WaitlistEntry { PoolId = onPools[1].Id, PersonId = child.Id, HouseholdId = household.Id, Position = i, Status = WaitlistStatus.Waiting, CreatedAt = now.AddDays(-20 + i) });
        }

        db.AuditEvents.Add(new AuditEvent { Actor = "system", Action = "seed", EntityType = "Database", EntityId = "-", Detail = "Seeded canonical dataset.", CreatedAt = now });
        await db.SaveChangesAsync();
    }

    static CapacityPool Pool(Session s, string name, Gender? g, int min, int max, int cap, int order) =>
        new() { Session = s, Name = name, Gender = g, GradeMin = min, GradeMax = max, Capacity = cap, SortOrder = order };

    static void AddCommonQuestions(CampProgram p)
    {
        p.Questions.AddRange(
            new Question { Key = "tshirt", Label = "T-shirt size", Type = QuestionType.Select, Scope = QuestionScope.Participant, Required = true, Options = "Youth S|Youth M|Youth L|Adult S|Adult M", SortOrder = 1 },
            new Question { Key = "swim", Label = "Swimming ability", Type = QuestionType.Select, Scope = QuestionScope.Participant, Required = true, Options = "Non-swimmer|Beginner|Confident swimmer", SortOrder = 2 },
            new Question { Key = "church", Label = "Does your family attend a church?", Type = QuestionType.YesNo, Scope = QuestionScope.Household, Required = true, SortOrder = 3 },
            new Question { Key = "churchName", Label = "Church name", Type = QuestionType.Text, Scope = QuestionScope.Household, Required = true, ShowWhenKey = "church", ShowWhenValue = "Yes", SortOrder = 4 });
    }

    static readonly string[] First = ["Liam", "Noah", "Oliver", "Elijah", "James", "Lucas", "Henry", "Mason", "Ethan", "Caleb", "Owen", "Wyatt", "Isaac", "Levi", "Micah", "Jonah", "Eli", "Asher", "Gabriel", "Jack"];
    static readonly string[] FirstF = ["Olivia", "Emma", "Ava", "Sophia", "Isabella", "Charlotte", "Amelia", "Harper", "Evelyn", "Abigail", "Ella", "Grace", "Lily", "Chloe", "Hannah", "Nora", "Zoe", "Leah", "Ruby", "Claire"];
    static readonly string[] Last = ["Miller", "Smith", "Davis", "Brown", "Wilson", "Moore", "Taylor", "Anderson", "Thomas", "Jackson", "White", "Harris", "Martin", "Thompson", "Garcia", "Martinez", "Robinson", "Clark", "Lewis", "Walker", "Hall", "Allen", "Young", "King", "Wright", "Scott", "Green", "Baker", "Adams", "Nelson", "Hill", "Campbell", "Mitchell", "Roberts", "Carter", "Phillips", "Evans", "Turner", "Parker", "Collins"];
    static readonly string[] Parents = ["Sarah", "Jennifer", "Michael", "Jessica", "Chris", "Ashley", "Daniel", "Amanda", "Matthew", "Rachel", "Andrew", "Lauren", "Josh", "Megan", "Ryan", "Katie"];

    static (Household, Person) FillerFamily(Random rng, Gender gender, int grade, DateOnly sessionStart)
    {
        var last = Last[rng.Next(Last.Length)];
        var parent = Parents[rng.Next(Parents.Length)];
        var email = $"{parent.ToLower()}.{last.ToLower()}{rng.Next(10, 99)}@example.com";
        var h = new Household { Name = last, Email = email, Phone = $"(770) 555-{rng.Next(1000, 9999)}", City = "Atlanta, GA" };
        h.Members.Add(new Person { FirstName = parent, LastName = last, DateOfBirth = new(1985, 1, 1), Gender = Gender.Female, IsAdult = true, Role = "Primary", Email = email });
        // Birthday that lands in the requested grade for the session year (Sept 1 cutoff).
        var dob = new DateOnly(sessionStart.Year - grade - 6, 9, 2).AddDays(rng.Next(0, 360));
        var child = new Person { FirstName = gender == Gender.Male ? First[rng.Next(First.Length)] : FirstF[rng.Next(FirstF.Length)], LastName = last, DateOfBirth = dob, Gender = gender };
        h.Members.Add(child);
        return (h, child);
    }

    static string LiabilityText(string program) => $"""
        In consideration of my child's participation in WinShape {program}, I acknowledge that camp activities — including swimming, climbing, archery, field games, and travel between activity areas — carry inherent risks of injury.

        I understand that WinShape Foundation, its staff, and volunteers take reasonable precautions but cannot eliminate all risk. On behalf of my child and myself, I release WinShape Foundation and its host partners from claims arising from ordinary negligence related to participation, to the extent permitted by Georgia law.

        I authorize camp staff to obtain emergency medical treatment for my child if I cannot be reached, and I accept responsibility for costs of such care.

        I confirm the information I have provided about my child's health and needs is accurate and complete, and I will notify WinShape of any changes before the session begins.
        """;

    const string PhotoText = """
        WinShape may photograph or record campers during program activities. I grant WinShape Foundation permission to use these images in printed materials, on its websites, and on its social media accounts, without compensation.

        Images will not be captioned with a camper's full name. I may withdraw this permission at any time by contacting WinShape; withdrawal applies to future use.
        """;

    const string PoliciesText = """
        Cancellation and refunds: Cancellations made 60 or more days before the session start receive a full refund less the deposit. Cancellations 14–59 days before receive a 50% refund of amounts paid beyond the deposit. No refunds are available within 14 days of the session.

        Payment plans: Scheduled installments are charged automatically to the card on file. If an installment is declined, we retry after 3 days and again after 7 days, and notify you each time. Registrations with a balance outstanding after the grace period may be cancelled under the policy above.

        Conduct: Campers are expected to follow staff instructions and the camp code of conduct. Serious or repeated violations may result in dismissal without refund.
        """;
}
