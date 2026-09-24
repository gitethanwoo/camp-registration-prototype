using System.Text;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Finance;

/// <summary>
/// Demo rows for the finance screens, all in this slice's own Family Camp session: filler families
/// (some on payment plans whose first installment failed), Maria Johnson's Avery and Mia with a
/// balance due, the Mitchell family whose phone payment reaches Fiserv without a registration
/// reference, and scholarship applications. Then one Fiserv settlement batch per day of platform
/// payments so far, and the Oracle Fusion journal batch for each. Runs after the other slices'
/// seeds so their payments settle too. Nothing here touches another slice's program or session.
/// </summary>
public sealed class FinanceSeed : ISeedModule
{
    public const string ProgramSlug = "family-camp";
    public const string MitchellEmail = "dana.mitchell@example.com";
    public const int MitchellPhonePaymentCents = 25000;
    public const int UnknownPaymentCents = 12500;

    int ISeedModule.Order => 200;

    static readonly string[] Kids = ["Theo", "Ezra", "Silas", "Jude", "Nolan", "Beckett", "Rhett", "Arlo", "Hazel", "Iris", "Wren", "Juniper", "Margot", "Poppy", "Sadie", "Vivian"];
    static readonly string[] Parents = ["Laura", "Kevin", "Monica", "Derek", "Tasha", "Brandon", "Erin", "Marcus", "Heather", "Tyler", "Brooke", "Jared"];
    static readonly string[] Surnames = ["Garcia", "Okafor", "Brennan", "Holloway", "Castillo", "Pruitt", "Delgado", "Whitaker", "Sutton", "Ramsey", "Kimura", "Lindqvist", "Abernathy", "Fontaine", "Greer", "Hensley", "Ibarra", "Jennings", "Kessler", "Lowell", "Marsh", "Novak", "Osei", "Pryor", "Quintero", "Rowe", "Salazar", "Tran", "Underwood", "Vance", "Weller", "Yates"];
    static readonly string[] DeclineReasons = ["Insufficient funds", "Card expired", "Do not honor", "Insufficient funds", "Issuer unavailable", "Insufficient funds", "Card reported lost", "Do not honor"];

    public async Task RunAsync(CampDbContext db, CancellationToken ct)
    {
        // Earlier seeds share this context and fix up rows with raw SQL; start from the database.
        db.ChangeTracker.Clear();
        if (await db.Programs.AnyAsync(p => p.Slug == ProgramSlug, ct)) return;
        var wsc = await db.Ministries.FirstOrDefaultAsync(m => m.Code == "WSC", ct);
        var johnson = await db.Households.Include(h => h.Members).FirstOrDefaultAsync(h => h.Email == Seed.JohnsonEmail, ct);
        if (wsc is null || johnson is null) return;

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var rng = new Random(6006);

        var program = new CampProgram
        {
            MinistryId = wsc.Id,
            Slug = ProgramSlug,
            Name = "Family Camp",
            Tagline = "A long weekend at camp for the whole family.",
            Description = "Three days on the mountain campus for families: lake time, family worship, and camp activities for kids in grades 1–8 while parents join sessions of their own.",
            Type = ProgramType.Standard,
            HealthMechanism = HealthMechanism.Embedded,
            Location = "WinShape Camps · Mount Berry, GA",
            ImageUrl = "/images/overnight.jpg",
            IsPublished = false, // registration is outside this slice; its rows exist for the finance screens
        };
        var session = new Session
        {
            Program = program,
            Name = "Summer 2028",
            StartDate = new(2028, 7, 28),
            EndDate = new(2028, 7, 30),
            PriceCents = 47500,
            DepositCents = 10000,
            PlanInstallments = 3,
            BalanceDueDate = new(2028, 6, 1),
        };
        var pool = new CapacityPool { Session = session, Name = "Campers G1–8", GradeMin = 1, GradeMax = 8, Capacity = 120, SortOrder = 1 };
        session.Pools.Add(pool);
        program.Sessions.Add(session);
        db.Programs.Add(program);
        await db.SaveChangesAsync(ct);

        var earlyBird = await db.DiscountCodes.AsNoTracking().FirstOrDefaultAsync(d => d.Code == "EARLYBIRD", ct);
        var fillers = new List<(Household H, PaymentOrder O)>();
        for (var i = 0; i < 52; i++)
        {
            var kind = i switch { < 8 => "failed", < 16 => "plan", < 36 => "full", _ => "deposit" };
            // Failed plans signed up about a month before the installment that failed.
            int[] failedDaysAgo = [0, 1, 2, 3, 4, 5, 6, 9];
            var created = kind == "failed"
                ? now.AddDays(-(failedDaysAgo[i] + 30)).Date.AddHours(14 + rng.Next(0, 6)).AddMinutes(rng.Next(60))
                : now.AddDays(-rng.Next(1, 58)).Date.AddHours(9 + rng.Next(0, 11)).AddMinutes(rng.Next(60));
            var discounted = i == 36 && earlyBird is not null;
            var (h, o) = Filler(rng, i, session, pool, created, kind, discounted ? earlyBird : null);
            db.Households.Add(h);
            db.Orders.Add(o);
            fillers.Add((h, o));
        }

        // Mitchell: deposit paid; the $250 phone payment in the latest settlement batch belongs here.
        var mitchell = new Household { Name = "Mitchell", Email = MitchellEmail, Phone = "(706) 555-0188", City = "Rome, GA" };
        mitchell.Members.AddRange(
            new Person { FirstName = "Dana", LastName = "Mitchell", DateOfBirth = new(1983, 6, 9), Gender = Gender.Female, IsAdult = true, Role = "Primary", Email = MitchellEmail },
            new Person { FirstName = "Owen", LastName = "Mitchell", DateOfBirth = new(2017, 1, 22), Gender = Gender.Male });
        db.Households.Add(mitchell);
        var mitchellOrder = Order(session, pool, mitchell, [mitchell.Members[1]], now.AddDays(-33).Date.AddHours(16), PaymentOption.Deposit, "WS-FC3M17", null, rng);
        db.Orders.Add(mitchellOrder);

        // Maria: Avery and Mia, deposit paid, $750 balance due. Uses the "Balance due" wording on F1.
        var kids = johnson.Members.Where(m => !m.IsAdult).OrderBy(m => m.DateOfBirth).ToList();
        var mariaOrder = Order(session, pool, johnson, kids, now.AddDays(-21).Date.AddHours(19).AddMinutes(12), PaymentOption.Deposit, "WS-FC2J08", null, rng);
        mariaOrder.Operations[0].CardLast4 = "4242";
        db.Orders.Add(mariaOrder);
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlAsync($"UPDATE r SET HouseholdId = p.HouseholdId FROM Registrations r JOIN People p ON p.Id = r.PersonId WHERE r.SessionId = {session.Id}", ct);

        SeedPlanFailures(db, fillers, now, rng);
        await db.SaveChangesAsync(ct);
        await SeedScholarships(db, fillers, now, ct);
        await SeedSettlements(db, today, rng, ct);
    }

    static (Household, PaymentOrder) Filler(Random rng, int i, Session session, CapacityPool pool, DateTime created, string kind, DiscountCode? discount)
    {
        var last = Surnames[i % Surnames.Length];
        var parent = Parents[rng.Next(Parents.Length)];
        var email = $"{parent.ToLowerInvariant()}.{last.ToLowerInvariant()}{i}@example.com";
        var h = new Household { Name = last, Email = email, Phone = $"(706) 555-{rng.Next(1000, 9999)}", City = i % 3 == 0 ? "Rome, GA" : "Atlanta, GA" };
        var grade = rng.Next(1, 9);
        var child = new Person
        {
            FirstName = Kids[rng.Next(Kids.Length)],
            LastName = last,
            DateOfBirth = new DateOnly(session.StartDate.Year - grade - 6, 9, 2).AddDays(rng.Next(0, 360)),
            Gender = rng.Next(2) == 0 ? Gender.Male : Gender.Female,
        };
        h.Members.AddRange(
            new Person { FirstName = parent, LastName = last, DateOfBirth = new(1984, 4, 1), Gender = Gender.Female, IsAdult = true, Role = "Primary", Email = email },
            child);
        var option = kind switch { "full" => PaymentOption.Full, "deposit" => PaymentOption.Deposit, _ => PaymentOption.Plan };
        return (h, Order(session, pool, h, [child], created, option, $"WS-FC{i:D2}{rng.Next(0x100, 0xFFF):X3}", discount, rng));
    }

    /// <summary>A confirmed order with its first charge (deposit or full), like checkout leaves it.</summary>
    static PaymentOrder Order(Session session, CapacityPool pool, Household h, List<Person> kids, DateTime created, PaymentOption option, string code, DiscountCode? discount, Random rng)
    {
        var off = discount is { Kind: DiscountKind.Percent } ? (int)Math.Round(session.PriceCents * discount.Value / 100m) : 0;
        var total = (session.PriceCents - off) * kids.Count;
        var paid = option == PaymentOption.Full ? total : session.DepositCents * kids.Count;
        var order = new PaymentOrder
        {
            Household = h,
            SessionId = session.Id,
            IdempotencyKey = $"seed-finance-{code}",
            ConfirmationCode = code,
            PaymentOption = option,
            DiscountCode = discount?.Code,
            SubtotalCents = session.PriceCents * kids.Count,
            DiscountCents = off * kids.Count,
            TotalCents = total,
            DueTodayCents = paid,
            Status = OrderStatus.Paid,
            CreatedAt = created,
        };
        order.Operations.Add(new PaymentOperation { Kind = PaymentKind.Charge, AmountCents = paid, Succeeded = true, ProcessorRef = $"fsv_fc{rng.Next():x8}", CardLast4 = $"{rng.Next(1000, 9999)}", CreatedAt = created });
        var perKid = paid / kids.Count;
        foreach (var kid in kids)
        {
            order.Registrations.Add(new Registration
            {
                SessionId = session.Id,
                PoolId = pool.Id,
                Person = kid,
                Status = RegistrationStatus.Confirmed,
                Grade = Eligibility.GradeFor(kid.DateOfBirth, session.StartDate),
                PriceCents = session.PriceCents,
                DiscountCents = off,
                PaidCents = perKid,
                HealthStatus = FormStatus.Complete,
                AnswersJson = "{}",
                CreatedAt = created,
            });
            pool.Reserved++;
        }
        return order;
    }

    /// <summary>
    /// Plans: installments monthly from sign-up. Fillers 0–7 had installment 1 fail 0–9 days ago
    /// (FN3); fillers 8–15 paid installment 1. Every plan keeps its card on file.
    /// </summary>
    static void SeedPlanFailures(CampDbContext db, List<(Household H, PaymentOrder O)> fillers, DateTime now, Random rng)
    {
        int[] failedDaysAgo = [0, 1, 2, 3, 4, 5, 6, 9];
        for (var i = 0; i < 16; i++)
        {
            var (h, o) = fillers[i];
            var remaining = o.TotalCents - o.DueTodayCents;
            var each = remaining / 3;
            var failed = i < 8;
            var first = failed ? DateOnly.FromDateTime(now).AddDays(-failedDaysAgo[i]) : DateOnly.FromDateTime(o.CreatedAt).AddDays(30);
            // Paid plans whose first installment would still be in the future just have it scheduled.
            var firstPaid = !failed && first < DateOnly.FromDateTime(now);
            for (var seq = 1; seq <= 3; seq++)
            {
                var status = seq > 1 ? InstallmentStatus.Scheduled : failed ? InstallmentStatus.Failed : firstPaid ? InstallmentStatus.Paid : InstallmentStatus.Scheduled;
                o.Installments.Add(new Installment { OrderId = o.Id, Sequence = seq, DueDate = first.AddMonths(seq - 1), AmountCents = seq == 3 ? remaining - each * 2 : each, Status = status });
            }
            if (firstPaid)
            {
                var at = first.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
                o.Operations.Add(new PaymentOperation { OrderId = o.Id, Kind = PaymentKind.Charge, AmountCents = each, Succeeded = true, ProcessorRef = $"fsv_fc{rng.Next():x8}", CardLast4 = o.Operations[0].CardLast4, Reason = "Installment 1 of 3", CreatedAt = at });
                o.Registrations[0].PaidCents += each;
            }
            // Fillers 2 and 5 have a card that keeps declining; the rest succeed when retried.
            var declines = i is 2 or 5;
            db.Add(new FinanceCardOnFile
            {
                OrderId = o.Id,
                Brand = "Visa",
                Last4 = declines ? "0002" : "4242",
                VaultRef = declines ? "sandbox:4000000000000002" : "sandbox:4242424242424242",
            });
            if (!failed) continue;

            var d = failedDaysAgo[i];
            var attempts = d < Fin.FirstRetryDays ? 1 : d < Fin.GraceDays ? 2 : Fin.MaxAttempts;
            var failedOn = first;
            var installment = o.Installments.Single(x => x.Sequence == 1);
            var reason = DeclineReasons[i];
            var failure = new InstallmentFailure
            {
                Installment = installment,
                FailedOn = failedOn,
                Attempts = attempts,
                DeclineReason = reason,
                NextRetryOn = attempts switch { 1 => failedOn.AddDays(Fin.FirstRetryDays), 2 => failedOn.AddDays(Fin.GraceDays), _ => null },
            };
            db.Add(failure);
            db.SaveChanges();
            var id = installment.Id.ToString(CultureInfo.InvariantCulture);
            var at0 = failedOn.ToDateTime(new TimeOnly(9, 14), DateTimeKind.Utc);
            db.AuditEvents.Add(new AuditEvent { Actor = "Fiserv (scheduled charge)", Action = "installment.failed", EntityType = "Installment", EntityId = id, Detail = $"Installment 1 of 3 ({Fin.Money(installment.AmountCents)}) declined: {reason}.", CreatedAt = at0 });
            db.AuditEvents.Add(new AuditEvent { Actor = "System", Action = "installment.family_notified", EntityType = "Installment", EntityId = id, Detail = $"HubSpot emailed {h.Email} that the payment didn't go through.", CreatedAt = at0.AddMinutes(2) });
            if (attempts >= 2)
                db.AuditEvents.Add(new AuditEvent { Actor = "Fiserv (automatic retry)", Action = "installment.retry_failed", EntityType = "Installment", EntityId = id, Detail = $"Retry 1 declined: {reason}.", CreatedAt = at0.AddDays(Fin.FirstRetryDays) });
            if (attempts >= 3)
                db.AuditEvents.Add(new AuditEvent { Actor = "Fiserv (automatic retry)", Action = "installment.retry_failed", EntityType = "Installment", EntityId = id, Detail = $"Retry 2 declined: {reason}. No retries left.", CreatedAt = at0.AddDays(Fin.GraceDays) });
        }
    }

    /// <summary>Seven applications from Family Camp families: four waiting, one with EARLYBIRD applied, one approved, one denied.</summary>
    static async Task SeedScholarships(CampDbContext db, List<(Household H, PaymentOrder O)> fillers, DateTime now, CancellationToken ct)
    {
        (int Filler, int Requested, string Band, string Reason, ScholarshipStatus Status, int Award, string? Note)[] rows =
        [
            (36, 30000, "$35,000–$50,000", "My husband's hours were cut this spring and we're a single-income family for now. Family Camp is the one week we get together, and we'd be grateful for help with the balance.", ScholarshipStatus.Submitted, 0, null),
            (37, 47500, "Under $35,000", "I'm raising two kids on my own and our church recommended we apply. Any help toward the cost would make this possible.", ScholarshipStatus.Submitted, 0, null),
            (38, 20000, "$50,000–$75,000", "We had unexpected medical bills this year. We can pay part of the balance and are asking for help with the rest.", ScholarshipStatus.Submitted, 0, null),
            (39, 15000, "$35,000–$50,000", "We're foster parents and our new placement arrived in March. This would let him come to camp with our family.", ScholarshipStatus.Submitted, 0, null),
            (40, 20000, "Under $35,000", "Job loss in January; I start a new position in August.", ScholarshipStatus.Approved, 15000, "Approved $150 toward the balance per the assistance fund guidelines."),
            (41, 37500, "Over $100,000", "We'd like help with the balance.", ScholarshipStatus.Denied, 0, "Household income is above the fund's guidelines this year."),
        ];
        var day = 0;
        foreach (var (index, requested, band, reason, status, award, note) in rows)
        {
            var (h, o) = fillers[index];
            var reg = o.Registrations[0];
            var submitted = now.AddDays(-(12 - day++)).Date.AddHours(20).AddMinutes(index);
            var app = new ScholarshipApplication
            {
                HouseholdId = h.Id,
                OrderId = o.Id,
                SubmittedBy = h.Members[0].FullName,
                SubmittedAt = submitted,
                RequestedCents = requested,
                IncomeBand = band,
                Reason = reason,
                Status = status,
                AwardCents = award,
                DecidedBy = status == ScholarshipStatus.Submitted ? null : "Diane Carter (CET)",
                DecidedAt = status == ScholarshipStatus.Submitted ? null : submitted.AddDays(2),
                DecisionNote = note,
                Document = new ScholarshipDocument { FileName = $"{h.Name.ToLowerInvariant()}-2025-tax-return.pdf", ContentType = "application/pdf", Content = Pdf($"{h.Name} household 2025 income summary"), UploadedAt = submitted },
            };
            app.Document.SizeBytes = app.Document.Content.Length;
            app.Lines.Add(new ScholarshipAwardLine { RegistrationId = reg.Id, AmountCents = award });
            if (award > 0) reg.DiscountCents += award;
            db.Add(app);
            if (status != ScholarshipStatus.Submitted)
                db.AuditEvents.Add(new AuditEvent { Actor = "Diane Carter (CET)", Action = status == ScholarshipStatus.Approved ? "scholarship.approved" : "scholarship.denied", EntityType = "ScholarshipApplication", EntityId = "seed", Detail = $"{h.Name} household: {note}", CreatedAt = submitted.AddDays(2) });
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// One Fiserv batch per day with platform payments, built from the payments themselves (each
    /// line matched), plus the day's fees. Yesterday's batch also has two lines the platform has no
    /// payment for. Journals: yesterday's waits on reconciliation, the day before is Pending in
    /// Fusion, two older ones Failed, the rest Posted.
    /// </summary>
    static async Task SeedSettlements(CampDbContext db, DateOnly today, Random rng, CancellationToken ct)
    {
        var start = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var ops = await db.PaymentOperations.AsNoTracking()
            .Where(o => o.Succeeded && (o.Kind == PaymentKind.Charge || o.Kind == PaymentKind.Refund) && o.CreatedAt < start)
            .Join(db.Orders, o => o.OrderId, x => x.Id, (o, x) => new { Op = o, x.HouseholdId })
            .Join(db.Households, a => a.HouseholdId, h => h.Id, (a, h) => new { a.Op, Payer = h.Members.Where(m => m.IsAdult).OrderBy(m => m.Id).Select(m => m.FirstName + " " + m.LastName).FirstOrDefault() ?? h.Name })
            .ToListAsync(ct);
        var yesterday = today.AddDays(-1);
        var days = ops.Select(o => DateOnly.FromDateTime(o.Op.CreatedAt)).Append(yesterday).Distinct().OrderBy(d => d).ToList();

        var batches = new List<SettlementBatch>();
        foreach (var d in days)
        {
            var batch = new SettlementBatch { Reference = $"FS-{d:yyyy-MM-dd}", SettledOn = d, ReceivedAt = d.AddDays(1).ToDateTime(new TimeOnly(6, 0), DateTimeKind.Utc) };
            foreach (var o in ops.Where(o => DateOnly.FromDateTime(o.Op.CreatedAt) == d).OrderBy(o => o.Op.CreatedAt))
            {
                var refund = o.Op.Kind == PaymentKind.Refund;
                batch.Lines.Add(new SettlementLine
                {
                    Kind = refund ? SettlementLineKind.Refund : SettlementLineKind.Payment,
                    ProcessorRef = o.Op.ProcessorRef,
                    TransactedAt = o.Op.CreatedAt,
                    AmountCents = refund ? -o.Op.AmountCents : o.Op.AmountCents,
                    Description = refund ? "Card refund" : "Card payment",
                    CardholderName = o.Payer,
                    CardLast4 = o.Op.CardLast4,
                    PaymentOperationId = o.Op.Id,
                    Status = SettlementLineStatus.Matched,
                });
            }
            if (d == yesterday)
            {
                batch.Lines.Add(new SettlementLine
                {
                    Kind = SettlementLineKind.Payment,
                    ProcessorRef = $"fsv_vt{rng.Next():x8}",
                    TransactedAt = d.ToDateTime(new TimeOnly(11, 3), DateTimeKind.Utc),
                    AmountCents = MitchellPhonePaymentCents,
                    Description = "Virtual terminal payment",
                    CardholderName = "Dana Mitchell",
                    CardLast4 = "3318",
                    Status = SettlementLineStatus.Unmatched,
                    UnmatchedReason = "No registration reference. Keyed in Fiserv's virtual terminal, so it never passed through checkout.",
                });
                batch.Lines.Add(new SettlementLine
                {
                    Kind = SettlementLineKind.Payment,
                    ProcessorRef = $"fsv_vt{rng.Next():x8}",
                    TransactedAt = d.ToDateTime(new TimeOnly(13, 5), DateTimeKind.Utc),
                    AmountCents = UnknownPaymentCents,
                    Description = "Virtual terminal payment",
                    CardholderName = "K. Nguyen",
                    CardLast4 = "7710",
                    Status = SettlementLineStatus.Unmatched,
                    UnmatchedReason = "No registration reference, and no family with this cardholder's name owes this amount.",
                });
            }
            var charged = batch.Lines.Where(l => l.Kind == SettlementLineKind.Payment).Sum(l => (long)l.AmountCents);
            if (charged > 0)
            {
                var feeAt = d.ToDateTime(new TimeOnly(23, 59), DateTimeKind.Utc);
                batch.Lines.Add(new SettlementLine { Kind = SettlementLineKind.Fee, ProcessorRef = $"{batch.Reference}-IC", TransactedAt = feeAt, AmountCents = -(int)Math.Round(charged * 0.022m), Description = "Interchange and card-brand fees", Status = SettlementLineStatus.Matched });
                batch.Lines.Add(new SettlementLine { Kind = SettlementLineKind.Fee, ProcessorRef = $"{batch.Reference}-PF", TransactedAt = feeAt, AmountCents = -(int)Math.Round(charged * 0.003m), Description = "Fiserv processing fee", Status = SettlementLineStatus.Matched });
            }
            batches.Add(batch);
        }
        db.AddRange(batches);
        await db.SaveChangesAsync(ct);

        var newestFirst = batches.OrderByDescending(b => b.SettledOn).ToList();
        for (var i = 1; i < newestFirst.Count; i++)
        {
            var b = newestFirst[i];
            var created = b.ReceivedAt.AddHours(1);
            var journal = new JournalBatch { Reference = $"JRN-{b.SettledOn:yyyy-MM-dd}", SettlementBatchId = b.Id, CreatedAt = created };
            journal.Events.Add(new JournalEvent { Status = JournalStatus.Pending, Detail = "Export created and queued for Oracle Fusion.", Actor = "System", At = created });
            if (i == 1)
            {
                journal.Status = JournalStatus.Pending;
            }
            else if (i is 3 or 7)
            {
                journal.Status = JournalStatus.Failed;
                journal.ErrorDetail = i == 3
                    ? "Department code WSC-FC is not mapped to an Oracle Fusion department. Map it in Fusion, then retry the export."
                    : $"The accounting period for {b.SettledOn:MMMM yyyy} was closed in Oracle Fusion when this batch arrived. Reopen the period or ask Accounting to post it to the current one, then retry.";
                journal.Events.Add(new JournalEvent { Status = JournalStatus.Failed, Detail = journal.ErrorDetail, Actor = "Oracle Fusion", At = created.AddMinutes(13) });
            }
            else
            {
                journal.Status = JournalStatus.Posted;
                journal.Events.Add(new JournalEvent { Status = JournalStatus.Posted, Detail = $"Posted to the general ledger as journal {rng.Next(40000, 49999)}.", Actor = "Oracle Fusion", At = created.AddMinutes(21) });
            }
            db.Add(journal);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A one-page PDF with a line of text, standing in for an uploaded tax document.</summary>
    static byte[] Pdf(string text)
    {
        var content = $"BT /F1 14 Tf 72 720 Td ({text.Replace("(", "", StringComparison.Ordinal).Replace(")", "", StringComparison.Ordinal)}) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = sb.Length;
        sb.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append(CultureInfo.InvariantCulture, $"{o:D10} 00000 n \n");
        sb.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
