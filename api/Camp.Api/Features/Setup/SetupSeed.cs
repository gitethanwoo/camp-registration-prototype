using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Setup;

/// <summary>
/// Setup rows. First it backfills what every existing program and session needs (publish state,
/// location and dates, the refund table C3 already used, the live waiver version), so nothing about
/// the other screens changes. Then it adds the demo program the setup screens are shown with:
/// Family Weekend, a returning Mount Berry program waiting on its second approval, with a new waiver
/// version waiting too. Runs after every other slice's seed.
/// </summary>
public sealed class SetupSeed(TimeProvider clock) : ISeedModule
{
    public const string FamilyWeekendSlug = "family-weekend";
    public const string FamilyWaiverTitle = "Family Weekend Release and Waiver";
    public const string Submitter = "Brian Hughes (Operations)";
    public const string SubmitterEmail = "brian.hughes@winshape.example";

    public int Order => 900;

    public async Task RunAsync(CampDbContext db, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await SeedFamilyWeekend(db, ct);
        await Backfill(db, ct);
        await SeedDiscountRules(db, ct);
        await SeedHistory(db, ct);
    }

    /// <summary>Setup rows for anything that doesn't have them yet. Safe to run on every start.</summary>
    async Task Backfill(CampDbContext db, CancellationToken ct)
    {
        var programs = await db.Programs.AsNoTracking().Include(p => p.Sessions).Include(p => p.Waivers).ToListAsync(ct);
        var withSetup = await db.Set<ProgramSetup>().Select(s => s.ProgramId).ToHashSetAsync(ct);
        var sessionsWithSetup = await db.Set<SessionSetup>().Select(s => s.SessionId).ToHashSetAsync(ct);
        var sessionsWithTiers = await db.Set<RefundTier>().Select(t => t.SessionId).Distinct().ToHashSetAsync(ct);
        var templatesWithVersions = await db.Set<WaiverVersion>().Select(v => v.WaiverTemplateId).Distinct().ToHashSetAsync(ct);
        var migrated = new DateTime(2027, 8, 1, 14, 0, 0, DateTimeKind.Utc);

        foreach (var p in programs)
        {
            if (!withSetup.Contains(p.Id))
            {
                var setup = new ProgramSetup { ProgramId = p.Id, State = p.IsPublished ? PublishState.Published : PublishState.Draft };
                var seq = 1;
                foreach (var (role, description) in ProgramSetupEndpoints.Chain)
                    setup.Steps.Add(new ProgramApprovalStep
                    {
                        Sequence = seq++,
                        Role = role,
                        Description = description,
                        ApprovedBy = p.IsPublished ? "WIN (before migration)" : null,
                        ApprovedAt = p.IsPublished ? migrated : null,
                    });
                db.Set<ProgramSetup>().Add(setup);
            }
            foreach (var s in p.Sessions)
            {
                if (!sessionsWithSetup.Contains(s.Id))
                {
                    // ON Session 3 opened to returning families first (K3's priority date). Both dates are in
                    // the past because the guest site sells it today; checkout doesn't read these dates yet.
                    var priority = p.Slug == "overnight-camp" && s.Name == "Session 3";
                    db.Set<SessionSetup>().Add(new SessionSetup
                    {
                        SessionId = s.Id,
                        Location = p.Location,
                        RegistrationOpensAt = priority ? new DateTime(2027, 9, 1, 14, 0, 0, DateTimeKind.Utc) : null,
                        PriorityOpensAt = priority ? new DateTime(2027, 8, 15, 14, 0, 0, DateTimeKind.Utc) : null,
                    });
                }
                if (!sessionsWithTiers.Contains(s.Id))
                    db.Set<RefundTier>().AddRange(RefundPolicy.Defaults.Select(t => new RefundTier { SessionId = s.Id, DaysBefore = t.DaysBefore, RefundPercent = t.RefundPercent, Basis = t.Basis, AdminFeeCents = t.AdminFeeCents }));
            }
            foreach (var w in p.Waivers.Where(w => !templatesWithVersions.Contains(w.Id)))
                db.Set<WaiverVersion>().Add(new WaiverVersion
                {
                    WaiverTemplateId = w.Id,
                    Version = w.Version,
                    Body = w.Body,
                    ChangeNote = "Imported from WIN.",
                    Status = WaiverVersionStatus.Published,
                    EffectiveDate = LiveSince(w.EffectiveDate),
                    CreatedBy = "Imported from WIN",
                    CreatedAt = migrated,
                    ApprovedBy = "WIN (before migration)",
                    ApprovedAt = migrated,
                });
        }
        await db.SaveChangesAsync(ct);

        // The core seed dates live waivers Nov 2027, but families sign them on the app clock's today.
        // A live version can't be effective in the future, so it reads as live since the WIN import.
        var today = clock.Today();
        await db.WaiverTemplates.Where(w => w.EffectiveDate > today)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.EffectiveDate, ImportedOn), ct);
    }

    static readonly DateOnly ImportedOn = new(2027, 8, 1);

    /// <summary>A live version's effective date: a future date becomes the WIN import date.</summary>
    DateOnly LiveSince(DateOnly effective) => effective > clock.Today() ? ImportedOn : effective;

    static async Task SeedFamilyWeekend(CampDbContext db, CancellationToken ct)
    {
        if (await db.Programs.AnyAsync(p => p.Slug == FamilyWeekendSlug, ct)) return;
        var wsc = await db.Ministries.FirstAsync(m => m.Code == "WSC", ct);
        var program = new CampProgram
        {
            MinistryId = wsc.Id,
            Slug = FamilyWeekendSlug,
            Name = "Family Weekend",
            Tagline = "A weekend at Mount Berry for the whole family.",
            Description = "Families stay together in the lodges, eat together in the dining hall, and split up by age for the day's activities: the lake, the ropes course, and worship on the hill.",
            Type = ProgramType.Standard,
            HealthMechanism = HealthMechanism.Embedded,
            Location = SessionSetupEndpoints.MountBerry,
            ImageUrl = "/images/overnight.jpg",
            IsPublished = false,
        };
        var v1Body = FamilyWaiver(includeLake: false, revised: false);
        var v2Body = FamilyWaiver(includeLake: false, revised: true);
        var waiver = new WaiverTemplate { Title = FamilyWaiverTitle, Version = 2, EffectiveDate = new(2026, 3, 1), PerParticipant = true, Body = v2Body };
        program.Waivers.Add(waiver);

        var past = new Session
        {
            Name = "Summer 2026",
            StartDate = new(2026, 7, 10),
            EndDate = new(2026, 7, 12),
            PriceCents = 37500,
            DepositCents = 10000,
            PlanInstallments = 0,
            BalanceDueDate = new(2026, 6, 1),
        };
        past.Pools.Add(new CapacityPool { Name = "All ages", GradeMin = 0, GradeMax = 12, Capacity = 80, SortOrder = 1 });
        var next = new Session
        {
            Name = "Summer 2028",
            StartDate = new(2028, 8, 4),
            EndDate = new(2028, 8, 6),
            PriceCents = 39500,
            DepositCents = 10000,
            PlanInstallments = 2,
            BalanceDueDate = new(2028, 7, 1),
        };
        next.Pools.AddRange(
            new CapacityPool { Name = "Grades K–5", GradeMin = 0, GradeMax = 5, Capacity = 60, SortOrder = 1 },
            new CapacityPool { Name = "Grades 6–12", GradeMin = 6, GradeMax = 12, Capacity = 40, SortOrder = 2 });
        program.Sessions.AddRange(past, next);
        db.Programs.Add(program);
        await db.SaveChangesAsync(ct);

        // Last summer's families, so the waiver history has real signatures on both versions.
        var rng = new Random(5150);
        var pool = past.Pools[0];
        for (var i = 0; i < 14; i++)
        {
            var signed = i < 4 ? new DateTime(2026, 2, 3 + i * 5, 15, 0, 0, DateTimeKind.Utc) : new DateTime(2026, 3, 4, 15, 0, 0, DateTimeKind.Utc).AddDays(i * 3);
            AddPastFamily(db, rng, past, pool, waiver, version: i < 4 ? 1 : 2, signed);
        }
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlAsync($"UPDATE r SET HouseholdId = p.HouseholdId FROM Registrations r JOIN People p ON p.Id = r.PersonId WHERE r.SessionId = {past.Id}", ct);

        var submitted = new DateTime(2028, 2, 24, 15, 12, 0, DateTimeKind.Utc);
        var setup = new ProgramSetup { ProgramId = program.Id, State = PublishState.PendingApproval, SubmittedBy = Submitter, SubmittedByEmail = SubmitterEmail, SubmittedAt = submitted };
        var seq = 1;
        foreach (var (role, description) in ProgramSetupEndpoints.Chain)
            setup.Steps.Add(new ProgramApprovalStep { Sequence = seq++, Role = role, Description = description });
        setup.Steps[0].ApprovedBy = "Jamie Dalton (Camp director)";
        setup.Steps[0].ApprovedByEmail = "jamie.dalton@winshape.example";
        setup.Steps[0].ApprovedAt = submitted.AddDays(2).AddHours(3);
        db.Set<ProgramSetup>().Add(setup);
        foreach (var s in program.Sessions)
            db.Set<SessionSetup>().Add(new SessionSetup
            {
                SessionId = s.Id,
                Location = SessionSetupEndpoints.MountBerry,
                RegistrationOpensAt = s == next ? new DateTime(2027, 9, 1, 14, 0, 0, DateTimeKind.Utc) : new DateTime(2026, 1, 15, 14, 0, 0, DateTimeKind.Utc),
            });

        db.Set<WaiverVersion>().AddRange(
            new WaiverVersion
            {
                WaiverTemplateId = waiver.Id,
                Version = 1,
                Body = v1Body,
                ChangeNote = "First version.",
                Status = WaiverVersionStatus.Archived,
                EffectiveDate = new(2025, 11, 1),
                RetiredDate = new(2026, 3, 1),
                CreatedBy = "Imported from WIN",
                CreatedAt = new DateTime(2025, 10, 20, 15, 0, 0, DateTimeKind.Utc),
                ApprovedBy = "WIN (before migration)",
                ApprovedAt = new DateTime(2025, 10, 28, 15, 0, 0, DateTimeKind.Utc),
            },
            new WaiverVersion
            {
                WaiverTemplateId = waiver.Id,
                Version = 2,
                Body = v2Body,
                ChangeNote = "Clarifies who signs for campers under 18.",
                Status = WaiverVersionStatus.Published,
                EffectiveDate = new(2026, 3, 1),
                CreatedBy = "Imported from WIN",
                CreatedAt = new DateTime(2026, 2, 10, 15, 0, 0, DateTimeKind.Utc),
                ApprovedBy = "WIN (before migration)",
                ApprovedAt = new DateTime(2026, 2, 24, 15, 0, 0, DateTimeKind.Utc),
            },
            new WaiverVersion
            {
                WaiverTemplateId = waiver.Id,
                Version = 3,
                Body = FamilyWaiver(includeLake: true, revised: true),
                ChangeNote = "Adds the lake and waterfront section.",
                Status = WaiverVersionStatus.PendingApproval,
                CreatedBy = Submitter,
                CreatedAt = submitted.AddDays(-1),
                SubmittedBy = Submitter,
                SubmittedByEmail = SubmitterEmail,
                SubmittedAt = submitted,
            });
        foreach (var s in program.Sessions)
            db.Set<RefundTier>().AddRange(RefundPolicy.Defaults.Select(t => new RefundTier { SessionId = s.Id, DaysBefore = t.DaysBefore, RefundPercent = t.RefundPercent, Basis = t.Basis, AdminFeeCents = t.AdminFeeCents }));
        await db.SaveChangesAsync(ct);
    }

    static readonly string[] Parents = ["Rachel", "Megan", "Tyler", "Josh", "Kendra", "Nate", "Laura", "Chris", "Dana", "Evan"];
    static readonly string[] Kids = ["Eli", "Nora", "Caleb", "Ruby", "Owen", "Hazel", "Levi", "Ivy", "Silas", "June", "Micah", "Clara"];
    static readonly string[] Last = ["Whitfield", "Okafor", "Brennan", "Castillo", "Duvall", "Hargrove", "Pruitt", "Sandoval", "Tillman", "Vaughn", "Ashby", "Kimura", "Lockhart", "Nwosu"];

    static void AddPastFamily(CampDbContext db, Random rng, Session session, CapacityPool pool, WaiverTemplate waiver, int version, DateTime signed)
    {
        var last = Last[rng.Next(Last.Length)];
        var parent = Parents[rng.Next(Parents.Length)];
        var email = $"{parent.ToLowerInvariant()}.{last.ToLowerInvariant()}{rng.Next(100, 999)}@example.com";
        var h = new Household { Name = last, Email = email, Phone = $"(706) 555-{rng.Next(1000, 9999)}", City = "Rome, GA" };
        h.Members.Add(new Person { FirstName = parent, LastName = last, DateOfBirth = new(1986, 4, 1), Gender = Gender.Female, IsAdult = true, Role = "Primary", Email = email });
        var grade = rng.Next(0, 9);
        var child = new Person { FirstName = Kids[rng.Next(Kids.Length)], LastName = last, DateOfBirth = new DateOnly(2026 - grade - 6, 9, 2).AddDays(rng.Next(0, 360)), Gender = rng.Next(2) == 0 ? Gender.Male : Gender.Female };
        h.Members.Add(child);
        db.Households.Add(h);
        var order = new PaymentOrder
        {
            Household = h,
            SessionId = session.Id,
            IdempotencyKey = Guid.NewGuid().ToString(),
            ConfirmationCode = $"WS-{rng.Next(0x100000, 0xFFFFFF):X6}",
            PaymentOption = PaymentOption.Full,
            SubtotalCents = session.PriceCents,
            TotalCents = session.PriceCents,
            DueTodayCents = session.PriceCents,
            Status = OrderStatus.Paid,
            CreatedAt = signed,
        };
        order.Operations.Add(new PaymentOperation { Kind = PaymentKind.Charge, AmountCents = session.PriceCents, Succeeded = true, ProcessorRef = $"fsv_{rng.Next():x8}", CardLast4 = $"{rng.Next(1000, 9999)}", CreatedAt = signed });
        var reg = new Registration
        {
            Order = order,
            SessionId = session.Id,
            PoolId = pool.Id,
            Person = child,
            Status = RegistrationStatus.Confirmed,
            Grade = grade,
            PriceCents = session.PriceCents,
            PaidCents = session.PriceCents,
            HealthStatus = FormStatus.Complete,
            CreatedAt = signed,
        };
        reg.WaiverAcceptances.Add(new WaiverAcceptance { WaiverTemplateId = waiver.Id, Version = version, SignerName = h.Members[0].FullName, AcceptedAt = signed });
        order.Registrations.Add(reg);
        db.Orders.Add(order);
        pool.Reserved++;
    }

    /// <summary>K5's demo rules. EARLYBIRD keeps working exactly as before, with no rule.</summary>
    static async Task SeedDiscountRules(CampDbContext db, CancellationToken ct)
    {
        if (await db.DiscountCodes.AnyAsync(d => d.Code == "SIBLING10", ct)) return;
        var day = await db.Programs.AsNoTracking().Include(p => p.Sessions).FirstOrDefaultAsync(p => p.Slug == "day-camp-atlanta", ct);
        if (day is null) return;
        var june = day.Sessions.OrderBy(s => s.StartDate).First();
        var created = new DateTime(2027, 8, 20, 16, 0, 0, DateTimeKind.Utc);
        // Uses tie to real orders: K5's "used N of cap" is a count of orders carrying the code, never a made-up number.
        var ordersByCode = await db.Orders.Where(o => o.DiscountCode != null && (o.Status == OrderStatus.Paid || o.Status == OrderStatus.Pending))
            .GroupBy(o => o.DiscountCode!).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        DiscountRule Rule(string code, DiscountKind kind, int value, string name, int? programId, int? sessionId, DateOnly from, DateOnly to, int? cap, bool stack, bool active = true) => new()
        {
            DiscountCode = new DiscountCode { Code = code, Kind = kind, Value = value, Status = DiscountStatus.Approved, CreatedBy = "Alex Morgan" },
            Name = name,
            ProgramId = programId,
            SessionId = sessionId,
            ValidFrom = from,
            ValidTo = to,
            MaxUses = cap,
            Uses = ordersByCode.GetValueOrDefault(code),
            Stackable = stack,
            Active = active,
            CreatedBy = "Alex Morgan (ADMIN)",
            CreatedAt = created,
        };
        db.Set<DiscountRule>().AddRange(
            Rule("SIBLING10", DiscountKind.Percent, 10, "Sibling discount", day.Id, june.Id, new(2027, 9, 1), new(2028, 6, 12), 200, stack: false),
            Rule("EARLY50", DiscountKind.Flat, 5000, "Early registration", day.Id, null, new(2027, 9, 1), new(2028, 3, 31), 500, stack: true),
            Rule("SPRING25", DiscountKind.Percent, 25, "Spring promotion", day.Id, null, new(2026, 3, 1), new(2026, 5, 31), 100, stack: false, active: false));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A few earlier setup changes, so K12 has history on a fresh database.</summary>
    static async Task SeedHistory(CampDbContext db, CancellationToken ct)
    {
        if (await db.AuditEvents.AnyAsync(e => e.Action.StartsWith("program.") || e.Action.StartsWith("discount.rule"), ct)) return;
        var family = await db.Programs.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == FamilyWeekendSlug, ct);
        var waiver = await db.WaiverTemplates.AsNoTracking().FirstOrDefaultAsync(w => w.Title == FamilyWaiverTitle, ct);
        var sibling = await db.DiscountCodes.AsNoTracking().FirstOrDefaultAsync(d => d.Code == "SIBLING10", ct);
        if (family is null || waiver is null || sibling is null) return;
        var t = new DateTime(2027, 8, 20, 16, 0, 0, DateTimeKind.Utc);
        var created = new AuditEvent { Actor = "Alex Morgan (ADMIN)", Action = "discount.rule_created", EntityType = "DiscountCode", EntityId = $"{sibling.Id}", Detail = "Created discount rule SIBLING10 (10% off). Live at checkout from Sep 1, 2027.", CreatedAt = t };
        var events = new[]
        {
            created,
            new AuditEvent { Actor = Submitter, Action = "waiver.submitted", EntityType = "WaiverTemplate", EntityId = $"{waiver.Id}", Detail = $"Sent version 3 of {FamilyWaiverTitle} for approval: Adds the lake and waterfront section.", CreatedAt = new(2028, 2, 24, 15, 10, 0, DateTimeKind.Utc) },
            new AuditEvent { Actor = Submitter, Action = "program.submitted", EntityType = "Program", EntityId = $"{family.Id}", Detail = "Submitted Family Weekend for approval.", CreatedAt = new(2028, 2, 24, 15, 12, 0, DateTimeKind.Utc) },
            new AuditEvent { Actor = "Jamie Dalton (Camp director)", Action = "program.step_approved", EntityType = "Program", EntityId = $"{family.Id}", Detail = "Approved the camp director step for Family Weekend.", CreatedAt = new(2028, 2, 26, 18, 12, 0, DateTimeKind.Utc) },
        };
        db.AuditEvents.AddRange(events);
        db.Set<AuditChange>().AddRange(
            new AuditChange { AuditEvent = created, Field = "Code", After = "SIBLING10" },
            new AuditChange { AuditEvent = created, Field = "Discount", After = "10% off" },
            new AuditChange { AuditEvent = created, Field = "Usage cap", After = "200" });
        await db.SaveChangesAsync(ct);
    }

    static string FamilyWaiver(bool includeLake, bool revised)
    {
        var signer = revised
            ? "A parent or legal guardian signs for each camper under 18. Adults 18 and older sign for themselves."
            : "A parent or guardian signs for each camper.";
        var lake = includeLake
            ? "\n\nLake and waterfront. Swimming, canoeing and the lake slide happen only when a certified lifeguard is on duty. Campers wear a life jacket in any boat and take a swim check before swimming in deep water."
            : "";
        return "Family Weekend at Mount Berry includes hiking, the ropes course, field games and other outdoor activities. These carry a risk of injury.\n\n"
            + "I agree that my family members attend voluntarily and accept those risks. I release WinShape Foundation, Berry College and their staff and volunteers from claims arising from ordinary negligence.\n\n"
            + "If someone in my family needs medical care and I can't be reached, I authorize staff to get treatment and agree to pay for it."
            + lake + "\n\n" + signer;
    }
}
