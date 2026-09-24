using System.Security.Cryptography;
using System.Text.Json;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Activities;
using Camp.Api.Features.Forms;
using Camp.Api.Features.Polish;
using Camp.Api.Features.Setup;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features;

public record HealthForm(string? Dietary, string? Allergies, string? AdaNeeds, string? Medications,
    string? PhysicianName, string? PhysicianPhone, string? InsuranceProvider);

// R4/R5: ranked activity choices per period and cabinmate requests, for sessions with an activity schedule.
public record CheckoutParticipant(int PersonId, Dictionary<string, string>? Answers, HealthForm? Health,
    List<ActivityChoice>? Activities = null, List<CabinmateInput>? Cabinmates = null);
public record WaiverSignature(int WaiverId, int? PersonId, string SignerName);

public record CheckoutRequest(
    string IdempotencyKey,
    int SessionId,
    List<CheckoutParticipant> Participants,
    Dictionary<string, string>? HouseholdAnswers,
    List<WaiverSignature> Waivers,
    PaymentOption PaymentOption,
    string? DiscountCode,
    string CardToken,
    int? FormVersionId = null); // K6: the form version the wizard showed; a stale one is refused

public record CheckoutResult(string ConfirmationCode, OrderStatus Status, string? Message);

public class CheckoutValidationException(Dictionary<string, string[]> errors) : Exception("Checkout is invalid.")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
}

public class CheckoutService(CampDbContext db, IPaymentGateway gateway, ILogger<CheckoutService> log, TimeProvider clock)
{
    public async Task<CheckoutResult> CheckoutAsync(int householdId, string actor, CheckoutRequest req, CancellationToken ct)
    {
        // FR-45: a repeated submit (double-click, retry after timeout) returns the original outcome.
        var existing = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.IdempotencyKey == req.IdempotencyKey, ct);
        if (existing is not null) return new(existing.ConfirmationCode, existing.Status, existing.DeclineReason);

        var session = await db.Sessions.Include(s => s.Program).ThenInclude(p => p.Questions)
            .Include(s => s.Program).ThenInclude(p => p.Waivers)
            .Include(s => s.Pools)
            .FirstOrDefaultAsync(s => s.Id == req.SessionId, ct)
            ?? throw Invalid("sessionId", "Session not found.");
        // K2: only a program that has passed its approval chain takes registrations.
        if (!session.Program.IsPublished)
            throw Invalid("sessionId", "This program isn't open for registration.");

        if (session.Program.Type != ProgramType.Standard)
            throw Invalid("sessionId", "Admittance and cohort programs are not part of this prototype.");

        var personIds = req.Participants.Select(p => p.PersonId).Distinct().ToList();
        if (personIds.Count == 0) throw Invalid("participants", "Choose at least one participant.");
        var people = await db.People.Where(p => personIds.Contains(p.Id) && p.HouseholdId == householdId).ToListAsync(ct);
        if (people.Count != personIds.Count) throw Invalid("participants", "Participants must belong to your household.");

        var errors = new Dictionary<string, string[]>();
        // K6: a program with a live form version is asked and checked by that version (required,
        // conditional and typed answers); others keep the original program questions.
        var form = await FormRules.LiveAsync(db, session.ProgramId, ct);
        var householdAnswers = new List<CleanAnswer>();
        var camperAnswers = new Dictionary<int, List<CleanAnswer>>();
        if (form is not null)
        {
            if (req.FormVersionId is { } shown && shown != form.Id)
                errors["formVersionId"] = ["The registration questions changed while you were registering. Go back to Questions and answer the new version."];
            var (clean, problems) = FormRules.Check(form.Questions.Where(q => q.Scope == QuestionScope.Household), req.HouseholdAnswers);
            householdAnswers = clean;
            if (problems.Count > 0) errors["householdAnswers"] = [.. problems];
        }
        var placements = new List<(Person Person, CapacityPool Pool, CheckoutParticipant Input)>();
        foreach (var input in req.Participants)
        {
            var person = people.Single(p => p.Id == input.PersonId);
            var (pool, reason) = Eligibility.FindPool(person, session);
            if (pool is null) { errors[$"participants.{person.Id}"] = [reason!]; continue; }
            if (form is not null)
            {
                var known = householdAnswers.ToDictionary(a => a.Question.Key, a => a.Value);
                var (clean, problems) = FormRules.Check(form.Questions.Where(q => q.Scope == QuestionScope.Participant), input.Answers, known);
                camperAnswers[person.Id] = clean;
                if (problems.Count > 0) errors[$"participants.{person.Id}.answers"] = [.. problems.Select(m => $"{person.FirstName}: {m}")];
            }
            else
            {
                var missing = MissingAnswers(session.Program, input.Answers, req.HouseholdAnswers);
                if (missing.Count > 0) errors[$"participants.{person.Id}.answers"] = [.. missing];
            }
            if (session.Program.HealthMechanism == HealthMechanism.Embedded && string.IsNullOrWhiteSpace(input.Health?.PhysicianName))
                errors[$"participants.{person.Id}.health"] = ["Health form is incomplete."];
            placements.Add((person, pool, input));
        }

        var already = await db.Registrations.Where(r => r.SessionId == session.Id && personIds.Contains(r.PersonId) && r.Status != RegistrationStatus.Cancelled)
            .Select(r => r.PersonId).ToListAsync(ct);
        var alreadyWaiting = await db.WaitlistEntries.Where(w => w.Pool.SessionId == session.Id && personIds.Contains(w.PersonId) && (w.Status == WaitlistStatus.Waiting || w.Status == WaitlistStatus.Offered))
            .Select(w => w.PersonId).ToListAsync(ct);
        foreach (var id in already.Concat(alreadyWaiting))
            errors[$"participants.{id}"] = [$"{people.Single(p => p.Id == id).FirstName} is already registered or waitlisted for this session."];

        var activityCampers = placements.Select(p => new ActivityCheckout.Camper(p.Person, p.Input.Activities, p.Input.Cabinmates)).ToList();
        foreach (var (key, messages) in await ActivityCheckout.CheckAsync(db, session, activityCampers, ct)) errors[key] = messages;

        foreach (var w in session.Program.Waivers)
        {
            var signedFor = req.Waivers.Where(s => s.WaiverId == w.Id && !string.IsNullOrWhiteSpace(s.SignerName)).ToList();
            var ok = w.PerParticipant ? personIds.All(id => signedFor.Any(s => s.PersonId == id)) : signedFor.Count > 0;
            if (!ok) errors[$"waivers.{w.Id}"] = [$"{w.Title} must be accepted."];
        }
        if (errors.Count > 0)
        {
            // A concurrent duplicate submit may have committed between the key check above and the
            // "already registered" check; that's the same checkout, not a conflict.
            var raced = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.IdempotencyKey == req.IdempotencyKey, ct);
            if (raced is not null) return new(raced.ConfirmationCode, raced.Status, raced.DeclineReason);
            throw new CheckoutValidationException(errors);
        }

        var discount = string.IsNullOrWhiteSpace(req.DiscountCode) ? null
            : await db.DiscountCodes.FirstOrDefaultAsync(d => d.Code == req.DiscountCode.Trim().ToUpper(), ct);
        // K5: a code outside its rule's scope, dates or cap reads as invalid.
        discount = await DiscountRuleGate.UsableAsync(db, discount, session, clock, ct);
        if (!string.IsNullOrWhiteSpace(req.DiscountCode) && discount is not { Status: DiscountStatus.Approved })
            throw Invalid("discountCode", Pricing.InvalidCodeMessage);

        // ── Step 1: claim seats. One SQL transaction; each seat is a conditional UPDATE, so the
        // last seat can only be taken once. Anyone who loses the race is waitlisted, never overbooked.
        var order = new PaymentOrder
        {
            HouseholdId = householdId,
            SessionId = session.Id,
            IdempotencyKey = req.IdempotencyKey,
            ConfirmationCode = NewCode(),
            PaymentOption = req.PaymentOption,
            DiscountCode = discount?.Code,
            Status = OrderStatus.Pending,
            CreatedAt = clock.UtcNow(),
        };
        var seated = new List<Registration>();
        var waitlisted = new List<WaitlistEntry>();

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            db.Orders.Add(order);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException e) when (e.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
            {
                // Lost a race with an identical submit: report the in-flight order instead of charging twice.
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                var winner = await db.Orders.AsNoTracking().SingleAsync(o => o.IdempotencyKey == req.IdempotencyKey, ct);
                return new(winner.ConfirmationCode, winner.Status, winner.DeclineReason);
            }
            // K5 usage cap: taken in this transaction, so two orders can't both take the last use.
            if (discount is not null && !await DiscountRuleGate.ClaimAsync(db, discount.Id, ct))
                throw Invalid("discountCode", Pricing.InvalidCodeMessage);

            foreach (var (person, pool, input) in placements)
            {
                var claimed = await db.CapacityPools
                    .Where(p => p.Id == pool.Id && p.Reserved < p.Capacity)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved + 1), ct);

                if (claimed == 1)
                {
                    var reg = new Registration
                    {
                        OrderId = order.Id,
                        SessionId = session.Id,
                        PoolId = pool.Id,
                        PersonId = person.Id,
                        HouseholdId = householdId,
                        Status = RegistrationStatus.PaymentPending,
                        Grade = Eligibility.GradeFor(person.DateOfBirth, session.StartDate),
                        PriceCents = session.PriceCents,
                        HealthStatus = session.Program.HealthMechanism switch
                        {
                            HealthMechanism.Embedded => FormStatus.Complete,
                            _ => FormStatus.Incomplete, // completed in CampDoc / third-party tool; status synced back
                        },
                        AnswersJson = form is null
                            ? JsonSerializer.Serialize(Merge(input.Answers, req.HouseholdAnswers))
                            : JsonSerializer.Serialize(householdAnswers.Concat(camperAnswers[person.Id]).ToDictionary(a => a.Question.Key, a => a.Value)),
                        HealthJson = session.Program.HealthMechanism == HealthMechanism.Embedded ? JsonSerializer.Serialize(input.Health) : null,
                        CreatedAt = clock.UtcNow(),
                    };
                    foreach (var w in session.Program.Waivers)
                    {
                        var sig = req.Waivers.First(s => s.WaiverId == w.Id && (!w.PerParticipant || s.PersonId == person.Id));
                        reg.WaiverAcceptances.Add(new WaiverAcceptance { WaiverTemplateId = w.Id, Version = w.Version, SignerName = sig.SignerName.Trim(), AcceptedAt = clock.UtcNow() });
                    }
                    db.Registrations.Add(reg);
                    seated.Add(reg);
                }
                else
                {
                    var position = await db.Database
                        .SqlQuery<int>($"SELECT ISNULL(MAX(Position), 0) + 1 AS Value FROM WaitlistEntries WITH (UPDLOCK, HOLDLOCK) WHERE PoolId = {pool.Id}")
                        .SingleAsync(ct);
                    var entry = new WaitlistEntry
                    {
                        PoolId = pool.Id,
                        PersonId = person.Id,
                        HouseholdId = householdId,
                        OrderId = order.Id,
                        Position = position,
                        Status = WaitlistStatus.Waiting,
                        CreatedAt = clock.UtcNow(),
                    };
                    db.WaitlistEntries.Add(entry);
                    await db.SaveChangesAsync(ct);
                    waitlisted.Add(entry);
                }
            }

            var quote = Pricing.Build(session, seated.Select(r => people.Single(p => p.Id == r.PersonId)).ToList(), req.PaymentOption, discount, discount?.Code, clock.Today());
            foreach (var reg in seated) reg.DiscountCents = quote.Lines.Single(l => l.PersonId == reg.PersonId).DiscountCents;
            order.SubtotalCents = quote.SubtotalCents;
            order.DiscountCents = quote.DiscountCents;
            order.TotalCents = quote.TotalCents;
            order.DueTodayCents = quote.DueTodayCents;
            order.PaymentOption = quote.PaymentOption;
            if (seated.Count == 0)
            {
                order.Status = OrderStatus.Waitlisted;
                if (discount is not null) await DiscountRuleGate.ReleaseAsync(db, discount.Code, ct);
            }

            foreach (var w in waitlisted)
            {
                var name = people.Single(p => p.Id == w.PersonId).FirstName;
                Audit(actor, "waitlist.joined", "WaitlistEntry", w.Id, $"{name} joined the waitlist at position {w.Position}; pool was full.");
                Outbox("WaitlistJoined", "HubSpot", order.ConfirmationCode, new { order.ConfirmationCode, person = name, w.Position });
            }
            await db.SaveChangesAsync(ct);
            if (form is not null)
            {
                // K6: each answer is kept with the version and question it answered. Household answers
                // belong to the order; camper answers to the camper's registration.
                db.AddRange(householdAnswers.Select(a => new FormAnswer { OrderId = order.Id, FormVersionId = form.Id, FormQuestionId = a.Question.Id, Value = a.Value }));
                foreach (var reg in seated)
                    db.AddRange(camperAnswers[reg.PersonId].Select(a => new FormAnswer { OrderId = order.Id, RegistrationId = reg.Id, FormVersionId = form.Id, FormQuestionId = a.Question.Id, Value = a.Value }));
                await db.SaveChangesAsync(ct);
            }
            // R4: each camper's highest-ranked activity with room, per period; all full → rolled back, 409.
            await ActivityCheckout.ApplyAsync(db, session, seated, activityCampers, clock.UtcNow(), ct);
            await tx.CommitAsync(ct);
        }

        if (seated.Count == 0) return new(order.ConfirmationCode, order.Status, null);

        // ── Step 2: charge. The processor and SQL can't share a transaction, so the order sits in
        // Pending with seats held; PendingPaymentReconciler repairs it if we crash right here.
        var result = await gateway.ChargeAsync(req.CardToken, order.DueTodayCents, req.IdempotencyKey, ct);

        // ── Step 3: finalize or compensate.
        await FinalizeAsync(order.Id, result, actor, ct);
        return new(order.ConfirmationCode, result.Succeeded ? OrderStatus.Paid : OrderStatus.Declined, result.DeclineReason);
    }

    public async Task FinalizeAsync(int orderId, GatewayResult result, string actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Orders.Include(o => o.Registrations).ThenInclude(r => r.Person)
            .Include(o => o.Household).Include(o => o.Session).ThenInclude(s => s.Program)
            .SingleAsync(o => o.Id == orderId, ct);
        if (order.Status != OrderStatus.Pending) return; // already finalized

        db.PaymentOperations.Add(new PaymentOperation
        {
            OrderId = order.Id,
            Kind = PaymentKind.Charge,
            AmountCents = order.DueTodayCents,
            Succeeded = result.Succeeded,
            ProcessorRef = result.ProcessorRef,
            CardLast4 = result.CardLast4,
            Reason = result.DeclineReason,
            CreatedAt = clock.UtcNow(),
        });

        if (result.Succeeded)
        {
            order.Status = OrderStatus.Paid;
            Allocate(order.Registrations, order.DueTodayCents);
            foreach (var reg in order.Registrations)
            {
                reg.Status = RegistrationStatus.Confirmed;
                Audit(actor, "registration.confirmed", "Registration", reg.Id, $"{reg.Person.FullName} confirmed for {order.Session.Program.Name} · {order.Session.Name}.");
            }
            if (order.PaymentOption == PaymentOption.Plan)
            {
                var seq = 1;
                foreach (var item in Pricing.PlanSchedule(order.Session, order.TotalCents - order.DueTodayCents, clock.Today()))
                    db.Installments.Add(new Installment { OrderId = order.Id, Sequence = seq++, DueDate = item.DueDate!.Value, AmountCents = item.AmountCents, Status = InstallmentStatus.Scheduled });
            }
            Outbox("RegistrationConfirmed", "HubSpot", order.ConfirmationCode, new { order.ConfirmationCode, to = order.Household.Email, participants = order.Registrations.Select(r => r.Person.FirstName) });
            Outbox("ConstituentUpsert", "Salesforce", order.Household.Id.ToString(CultureInfo.InvariantCulture), new { householdId = order.Household.Id, order.Household.Email });
        }
        else
        {
            // Compensate: release every seat this order claimed and clear its waitlist entries,
            // so a retry with a new card starts clean.
            order.Status = OrderStatus.Declined;
            order.DeclineReason = result.DeclineReason;
            foreach (var reg in order.Registrations)
            {
                reg.Status = RegistrationStatus.Cancelled;
                await db.CapacityPools.Where(p => p.Id == reg.PoolId && p.Reserved > 0)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved - 1), ct);
            }
            await db.WaitlistEntries.Where(w => w.OrderId == order.Id && w.Status == WaitlistStatus.Waiting)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WaitlistStatus.Removed), ct);
            await DiscountRuleGate.ReleaseAsync(db, order.DiscountCode, ct);
            await ActivityRules.ReleaseAsync(db, [.. order.Registrations.Select(r => r.Id)], ct);
            Audit(actor, "payment.declined", "PaymentOrder", order.Id, $"Charge of {Money(order.DueTodayCents)} declined; {order.Registrations.Count} seat(s) released.");
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        log.LogInformation("Order {Code} finalized as {Status}", order.ConfirmationCode, order.Status);
    }

    static void Allocate(List<Registration> regs, int paid)
    {
        var net = regs.Sum(r => r.PriceCents - r.DiscountCents);
        var left = paid;
        foreach (var r in regs)
        {
            var share = net == 0 ? 0 : (int)((long)paid * (r.PriceCents - r.DiscountCents) / net);
            r.PaidCents = share;
            left -= share;
        }
        if (regs.Count > 0) regs[0].PaidCents += left;
    }

    static List<string> MissingAnswers(CampProgram program, Dictionary<string, string>? participant, Dictionary<string, string>? household)
    {
        var all = Merge(participant, household);
        return program.Questions
            .Where(q => q.Required)
            .Where(q => q.ShowWhenKey is null || (all.TryGetValue(q.ShowWhenKey, out var v) && v == q.ShowWhenValue))
            .Where(q => !all.TryGetValue(q.Key, out var a) || string.IsNullOrWhiteSpace(a))
            .Select(q => $"{q.Label} is required.")
            .ToList();
    }

    static Dictionary<string, string> Merge(Dictionary<string, string>? a, Dictionary<string, string>? b)
    {
        var d = new Dictionary<string, string>(b ?? []);
        foreach (var (k, v) in a ?? []) d[k] = v;
        return d;
    }

    void Audit(string actor, string action, string type, object id, string detail) =>
        db.AuditEvents.Add(new AuditEvent { Actor = actor, Action = action, EntityType = type, EntityId = id.ToString()!, Detail = detail, CreatedAt = clock.UtcNow() });

    void Outbox(string type, string target, string aggregateId, object payload) =>
        db.OutboxEvents.Add(new OutboxEvent { Type = type, Target = target, AggregateId = aggregateId, PayloadJson = JsonSerializer.Serialize(payload), CreatedAt = clock.UtcNow() });

    static CheckoutValidationException Invalid(string key, string msg) => new(new() { [key] = [msg] });

    static string NewCode() => "WS-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(3));

    public static string Money(int cents) => (cents / 100m).ToString("C0", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
}
