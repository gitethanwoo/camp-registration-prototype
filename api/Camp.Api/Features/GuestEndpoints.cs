using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Setup;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features;

public record PoolAvailability(int Id, string Name, int Capacity, int Reserved, int Remaining, int Waitlisted, string State);

public static class GuestEndpoints
{
    const int LowStockThreshold = 5;

    public static void MapGuestEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        // Anything that reads or writes a household needs a signed-in guest.
        var family = api.MapGroup("").RequireAuthorization(Policies.Family);

        // FR-15: every program from one entry point.
        api.MapGet("/programs", async (CampDbContext db) =>
        {
            var programs = await db.Programs.Where(p => p.IsPublished)
                .Include(p => p.Ministry).Include(p => p.Sessions).ThenInclude(s => s.Pools)
                .AsNoTracking().ToListAsync();
            return programs.Select(p => new
            {
                p.Slug,
                p.Name,
                p.Tagline,
                p.Location,
                p.ImageUrl,
                p.HostOrganization,
                Ministry = p.Ministry.Name,
                Type = p.Type.ToString(),
                Sessions = p.Sessions.OrderBy(s => s.StartDate).Select(s => new
                {
                    s.Id,
                    s.Name,
                    s.StartDate,
                    s.EndDate,
                    s.PriceCents,
                    s.DepositCents,
                    GradeMin = s.Pools.Min(x => x.GradeMin),
                    GradeMax = s.Pools.Max(x => x.GradeMax),
                    Capacity = s.Pools.Sum(x => x.Capacity),
                    Remaining = s.Pools.Sum(x => x.Capacity - x.Reserved),
                }),
            });
        });

        // P1 · Program detail (FR-13, FR-14)
        api.MapGet("/programs/{slug}", async (string slug, CampDbContext db) =>
        {
            var p = await db.Programs.Include(x => x.Ministry).Include(x => x.Waivers)
                .Include(x => x.Sessions).ThenInclude(s => s.Pools)
                .AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug && x.IsPublished);
            if (p is null) return Results.NotFound();
            var waitlist = await WaitlistCounts(db, p.Sessions.Select(s => s.Id));
            return Results.Ok(new
            {
                p.Slug,
                p.Name,
                p.Tagline,
                p.Description,
                p.Location,
                p.ImageUrl,
                p.HostOrganization,
                Ministry = p.Ministry.Name,
                Type = p.Type.ToString(),
                HealthMechanism = p.HealthMechanism.ToString(),
                Requirements = p.Waivers.Select(w => w.Title).Append(p.HealthMechanism switch
                {
                    HealthMechanism.CampDoc => "Health forms in CampDoc",
                    HealthMechanism.ThirdParty => "Health form (external)",
                    _ => "Health form (completed during registration)",
                }),
                Sessions = p.Sessions.OrderBy(s => s.StartDate).Select(s => new
                {
                    s.Id,
                    s.Name,
                    s.StartDate,
                    s.EndDate,
                    s.PriceCents,
                    s.DepositCents,
                    s.PlanInstallments,
                    Pools = s.Pools.OrderBy(x => x.SortOrder).Select(x => ToAvailability(x, waitlist)),
                }),
                AsOf = DateTime.UtcNow,
            });
        });

        // P2 · Live availability, polled by the UI (NFR-1: seconds, not a daily snapshot).
        api.MapGet("/sessions/{id:int}/availability", async (int id, CampDbContext db) =>
        {
            var pools = await db.CapacityPools.Where(x => x.SessionId == id).OrderBy(x => x.SortOrder).AsNoTracking().ToListAsync();
            var waitlist = await WaitlistCounts(db, [id]);
            return new { AsOf = DateTime.UtcNow, Pools = pools.Select(x => ToAvailability(x, waitlist)) };
        });

        family.MapGet("/me", async (CampDbContext db, CurrentUser me) =>
        {
            var h = await db.Households.Include(x => x.Members).AsNoTracking().SingleAsync(x => x.Id == me.HouseholdId);
            return new { h.Id, h.Name, h.Email, h.Phone, h.City, Signer = me.Name, Members = h.Members.Select(m => new { m.Id, m.FirstName, m.LastName, m.IsAdult, m.Role }) };
        });

        // Everything the wizard needs for one session: members with eligibility, questions, waivers, pricing.
        family.MapGet("/sessions/{id:int}/register-context", async (int id, CampDbContext db, CurrentUser me) =>
        {
            var s = await db.Sessions.Include(x => x.Program).ThenInclude(p => p.Questions)
                .Include(x => x.Program).ThenInclude(p => p.Waivers)
                .Include(x => x.Pools).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return Results.NotFound();
            var h = await db.Households.Include(x => x.Members).AsNoTracking().SingleAsync(x => x.Id == me.HouseholdId);
            var active = await db.Registrations.Where(r => r.SessionId == id && r.HouseholdId == h.Id && r.Status != RegistrationStatus.Cancelled).Select(r => r.PersonId).ToListAsync();
            var waiting = await db.WaitlistEntries.Where(w => w.Pool.SessionId == id && w.HouseholdId == h.Id && (w.Status == WaitlistStatus.Waiting || w.Status == WaitlistStatus.Offered)).Select(w => w.PersonId).ToListAsync();
            var waitlist = await WaitlistCounts(db, [id]);

            return Results.Ok(new
            {
                Session = new { s.Id, s.Name, s.StartDate, s.EndDate, s.PriceCents, s.DepositCents, s.PlanInstallments, s.BalanceDueDate },
                Program = new { s.Program.Slug, s.Program.Name, s.Program.Location, s.Program.ImageUrl, Type = s.Program.Type.ToString(), HealthMechanism = s.Program.HealthMechanism.ToString() },
                Household = new { h.Name, h.Email, h.Phone, h.City, Signer = me.Name },
                Participants = h.Members.Where(m => !m.IsAdult).Select(m =>
                {
                    var (pool, reason) = Eligibility.FindPool(m, s);
                    var grade = Eligibility.GradeFor(m.DateOfBirth, s.StartDate);
                    var status = active.Contains(m.Id) ? "registered" : waiting.Contains(m.Id) ? "waitlisted" : pool is null ? "ineligible" : "eligible";
                    return new
                    {
                        m.Id,
                        m.FirstName,
                        m.LastName,
                        m.DateOfBirth,
                        Gender = m.Gender.ToString(),
                        Grade = grade,
                        GradeLabel = Eligibility.GradeLabel(grade),
                        Status = status,
                        Reason = reason,
                        Pool = pool is null ? null : ToAvailability(pool, waitlist),
                        BasicHealth = new { m.Dietary, m.Allergies, m.AdaNeeds },
                    };
                }),
                Questions = s.Program.Questions.OrderBy(q => q.SortOrder).Select(q => new
                {
                    q.Key,
                    q.Label,
                    Type = q.Type.ToString(),
                    Scope = q.Scope.ToString(),
                    q.Required,
                    Options = q.Options?.Split('|') ?? [],
                    q.ShowWhenKey,
                    q.ShowWhenValue,
                }),
                Waivers = s.Program.Waivers.Select(w => new { w.Id, w.Title, w.Version, w.EffectiveDate, w.Body, w.PerParticipant }),
            });
        });

        // R9 · price the cart server-side; the UI never does money math on its own.
        family.MapPost("/sessions/{id:int}/quote", async (int id, QuoteRequest req, CampDbContext db, CurrentUser me) =>
        {
            var s = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return Results.NotFound();
            var people = await db.People.Where(p => req.PersonIds.Contains(p.Id) && p.HouseholdId == me.HouseholdId).AsNoTracking().ToListAsync();
            var code = req.DiscountCode?.Trim().ToUpper();
            var discount = string.IsNullOrEmpty(code) ? null : await db.DiscountCodes.AsNoTracking().FirstOrDefaultAsync(d => d.Code == code);
            discount = await DiscountRuleGate.UsableAsync(db, discount, s); // K5 scope, dates and cap
            return Results.Ok(Pricing.Build(s, people.OrderBy(p => req.PersonIds.IndexOf(p.Id)).ToList(), req.PaymentOption, discount, code));
        });

        // Stand-in for Fiserv hosted fields: in production the card number is typed into Fiserv's
        // iframe and never reaches this API. Only the token below is sent to /checkout.
        api.MapPost("/fiserv-sandbox/tokenize", (TokenizeRequest req, IPaymentGateway gateway) =>
        {
            if (gateway is not FakeFiservGateway fake) return Results.NotFound();
            try { return Results.Ok(new { Token = fake.Tokenize(req.CardNumber) }); }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        });

        family.MapPost("/checkout", async (CheckoutRequest req, CheckoutService checkout, CurrentUser me, CancellationToken ct) =>
        {
            try
            {
                var result = await checkout.CheckoutAsync(me.HouseholdId, $"{me.Name} (guest)", req, ct);
                return result.Status == OrderStatus.Declined
                    ? Results.Json(result, statusCode: StatusCodes.Status402PaymentRequired)
                    : Results.Ok(result);
            }
            catch (CheckoutValidationException e) { return Results.ValidationProblem(e.Errors); }
        });

        // R11 / R12 · confirmation, including anyone who landed on the waitlist.
        family.MapGet("/orders/{code}", async (string code, CampDbContext db, CurrentUser me) =>
        {
            var o = await db.Orders.Include(x => x.Session).ThenInclude(s => s.Program)
                .Include(x => x.Registrations).ThenInclude(r => r.Person)
                .Include(x => x.Registrations).ThenInclude(r => r.Pool)
                .Include(x => x.Operations).Include(x => x.Installments).Include(x => x.Household)
                .AsNoTracking().FirstOrDefaultAsync(x => x.ConfirmationCode == code && x.HouseholdId == me.HouseholdId);
            if (o is null) return Results.NotFound();
            var waitlisted = await db.WaitlistEntries.Where(w => w.OrderId == o.Id).Include(w => w.Person).Include(w => w.Pool).AsNoTracking().ToListAsync();
            var emailSent = await db.OutboxEvents.AnyAsync(e => e.AggregateId == o.ConfirmationCode && e.Target == "HubSpot" && e.ProcessedAt != null);
            var charge = o.Operations.Where(x => x.Kind == PaymentKind.Charge && x.Succeeded).Sum(x => x.AmountCents);
            return Results.Ok(new
            {
                o.ConfirmationCode,
                Status = o.Status.ToString(),
                o.DeclineReason,
                o.Household.Email,
                EmailSent = emailSent,
                Program = new { o.Session.Program.Name, o.Session.Program.Slug, o.Session.Program.Location, HealthMechanism = o.Session.Program.HealthMechanism.ToString() },
                Session = new { o.Session.Id, o.Session.Name, o.Session.StartDate, o.Session.EndDate },
                Registrations = o.Registrations.Where(r => r.Status != RegistrationStatus.Cancelled).Select(r => new { r.Id, r.Person.FirstName, r.Person.LastName, Pool = r.Pool.Name, Status = r.Status.ToString() }),
                Waitlisted = waitlisted.Select(w => new { w.Id, w.Person.FirstName, Pool = w.Pool.Name, w.Position, Status = w.Status.ToString() }),
                Payment = new
                {
                    Option = o.PaymentOption.ToString(),
                    o.TotalCents,
                    o.DiscountCents,
                    ChargedCents = charge,
                    CardLast4 = o.Operations.FirstOrDefault(x => x.Succeeded)?.CardLast4,
                    BalanceCents = o.TotalCents - charge,
                    BalanceDueDate = o.Session.BalanceDueDate,
                    Installments = o.Installments.OrderBy(i => i.Sequence).Select(i => new { i.DueDate, i.AmountCents, Status = i.Status.ToString() }),
                },
            });
        });

        // F1 · one combined checklist across all kids (FR-27) + F4 registrations.
        family.MapGet("/family", async (CampDbContext db, CurrentUser me) =>
        {
            var regs = await db.Registrations.Where(r => r.HouseholdId == me.HouseholdId && r.Status != RegistrationStatus.Cancelled)
                .Include(r => r.Person).Include(r => r.Pool).Include(r => r.Session).ThenInclude(s => s.Program).ThenInclude(p => p.Waivers)
                .Include(r => r.WaiverAcceptances).Include(r => r.Order).ThenInclude(o => o!.Installments)
                .AsNoTracking().ToListAsync();
            var waitlist = await db.WaitlistEntries.Where(w => w.HouseholdId == me.HouseholdId && (w.Status == WaitlistStatus.Waiting || w.Status == WaitlistStatus.Offered))
                .Include(w => w.Person).Include(w => w.Pool).ThenInclude(p => p.Session).ThenInclude(s => s.Program).AsNoTracking().ToListAsync();

            return new
            {
                Registrations = regs.OrderBy(r => r.Session.StartDate).Select(r => new
                {
                    r.Id,
                    Participant = r.Person.FullName,
                    Program = r.Session.Program.Name,
                    Session = r.Session.Name,
                    r.Session.StartDate,
                    r.Session.EndDate,
                    Pool = r.Pool.Name,
                    Status = r.Status.ToString(),
                    r.BalanceCents,
                    r.Order!.ConfirmationCode,
                }),
                Waitlist = waitlist.Select(w => new { w.Id, Participant = w.Person.FullName, Program = w.Pool.Session.Program.Name, Session = w.Pool.Session.Name, Pool = w.Pool.Name, w.Position, Status = w.Status.ToString(), w.OfferExpiresAt }),
                Checklist = regs.Where(r => r.Status == RegistrationStatus.Confirmed).SelectMany(ChecklistFor)
                    .Concat(regs.Where(r => r.Status == RegistrationStatus.Confirmed && r.Order is not null).GroupBy(r => r.OrderId).SelectMany(BalanceFor))
                    .ToList(),
            };
        });
    }

    static IEnumerable<object> ChecklistFor(Registration r)
    {
        var who = r.Person.FirstName;
        var program = r.Session.Program;
        var ctx = $"{program.Name} · {r.Session.Name}";

        var signed = r.WaiverAcceptances.Select(a => a.WaiverTemplateId).ToHashSet();
        var missing = program.Waivers.Count(w => !signed.Contains(w.Id));
        yield return new { Key = $"waiver-{r.Id}", Participant = who, Context = ctx, Title = missing == 0 ? "Waivers signed" : $"{missing} waiver(s) to sign", Done = missing == 0, Action = "Sign waivers", Kind = "waiver" };

        yield return program.HealthMechanism switch
        {
            HealthMechanism.CampDoc => new { Key = $"health-{r.Id}", Participant = who, Context = ctx, Title = r.HealthStatus == FormStatus.Complete ? "CampDoc health forms complete" : "Complete health forms in CampDoc", Done = r.HealthStatus == FormStatus.Complete, Action = "Open CampDoc", Kind = "campdoc" },
            _ => new { Key = $"health-{r.Id}", Participant = who, Context = ctx, Title = r.HealthStatus == FormStatus.Complete ? "Health form complete" : "Complete health form", Done = r.HealthStatus == FormStatus.Complete, Action = "Complete form", Kind = "health" },
        };

    }

    // Balance is owed per order (one card, one plan), so it's one checklist item, not one per child.
    static IEnumerable<object> BalanceFor(IGrouping<int?, Registration> order)
    {
        var regs = order.ToList();
        var balance = regs.Sum(r => r.BalanceCents);
        if (balance <= 0) yield break;
        var first = regs[0];
        var who = string.Join(" & ", regs.Select(r => r.Person.FirstName));
        var ctx = $"{first.Session.Program.Name} · {first.Session.Name}";
        var next = first.Order!.Installments.Where(i => i.Status == InstallmentStatus.Scheduled).OrderBy(i => i.DueDate).FirstOrDefault();
        var title = next is not null
            ? $"Payment plan active · next {CheckoutService.Money(next.AmountCents)} on {next.DueDate:MMM d, yyyy} ({CheckoutService.Money(balance)} remaining)"
            : $"Balance of {CheckoutService.Money(balance)} due by {first.Session.BalanceDueDate:MMM d, yyyy}";
        yield return new { Key = $"balance-{order.Key}", Participant = who, Context = ctx, Title = title, Done = false, Action = next is not null ? "View schedule" : "Pay balance", Kind = "balance" };
    }

    static async Task<Dictionary<int, int>> WaitlistCounts(CampDbContext db, IEnumerable<int> sessionIds)
    {
        var ids = sessionIds.ToList();
        return await db.WaitlistEntries
            .Where(w => ids.Contains(w.Pool.SessionId) && (w.Status == WaitlistStatus.Waiting || w.Status == WaitlistStatus.Offered))
            .GroupBy(w => w.PoolId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
    }

    public static PoolAvailability ToAvailability(CapacityPool p, IReadOnlyDictionary<int, int> waitlist)
    {
        var remaining = p.Capacity - p.Reserved;
        var state = remaining <= 0 ? "full" : remaining <= LowStockThreshold ? "low" : "open";
        return new(p.Id, p.Name, p.Capacity, p.Reserved, remaining, waitlist.GetValueOrDefault(p.Id), state);
    }
}

public record QuoteRequest(List<int> PersonIds, PaymentOption PaymentOption, string? DiscountCode);
public record TokenizeRequest(string CardNumber);
