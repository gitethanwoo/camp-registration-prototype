using System.Security.Cryptography;
using System.Text.Json;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Admittance;

/// <summary>An expected refusal: a conflict (409), invalid input (400), or a declined card (402).</summary>
public sealed class AdmittanceException(int status, string message, Dictionary<string, string[]>? errors = null) : Exception(message)
{
    public int Status { get; } = status;
    public Dictionary<string, string[]>? Errors { get; } = errors;
}

public sealed record DraftRequest(int? SpousePersonId, string? SpouseFirstName, string? SpouseLastName, string? SpouseEmail, Dictionary<string, string>? Answers, int Step);
public sealed record CardRequest(string CardToken, string IdempotencyKey);
public sealed record MessageRequest(string Message);

/// <summary>
/// Admittance lifecycle: draft → submitted (card authorized) → review → approved (seat claimed,
/// card captured) or declined (hold voided) or waitlisted (hold voided, only when full).
/// </summary>
public sealed class AdmittanceService(CampDbContext db, IPaymentGateway gateway, IAuditLog audit)
{
    /// <summary>How long a card network honors a hold before it lapses.</summary>
    public static readonly TimeSpan HoldLifetime = TimeSpan.FromDays(7);

    /// <summary>A hold with this little time left shows as expiring to staff.</summary>
    public static readonly TimeSpan ExpiringWithin = TimeSpan.FromDays(3);

    const string Entity = "AdmittanceApplication";

    // ── Family ──────────────────────────────────────────────────────────────

    public async Task<AdmittanceApplication> SaveDraftAsync(int householdId, string guestEmail, int sessionId, DraftRequest req, CancellationToken ct)
    {
        var session = await AdmittanceSession(sessionId, ct);
        var app = await db.Set<AdmittanceApplication>().Include(a => a.Applicant)
            .FirstOrDefaultAsync(a => a.HouseholdId == householdId && a.SessionId == session.Id, ct);
        var adults = await db.People.Where(p => p.HouseholdId == householdId && p.IsAdult).ToListAsync(ct);
        var now = DateTime.UtcNow;

        if (app is null)
        {
            var applicant = adults.FirstOrDefault(p => string.Equals(p.Email, guestEmail, StringComparison.OrdinalIgnoreCase))
                ?? adults.OrderBy(p => p.Id).FirstOrDefault()
                ?? throw new AdmittanceException(409, "Add yourself to your family account before applying.");
            app = new AdmittanceApplication
            {
                HouseholdId = householdId,
                SessionId = session.Id,
                ApplicantPersonId = applicant.Id,
                Applicant = applicant,
                Stage = ApplicationStage.Draft,
                AmountCents = session.PriceCents,
                CreatedAt = now,
            };
            db.Add(app);
        }
        else if (app.Stage != ApplicationStage.Draft)
        {
            throw new AdmittanceException(409, "This application was already submitted, so it can't be edited.");
        }

        if (req.SpousePersonId is { } spouseId)
        {
            var spouse = adults.FirstOrDefault(p => p.Id == spouseId && p.Id != app.ApplicantPersonId)
                ?? throw new AdmittanceException(400, "Choose your spouse from the adults on your family account.");
            app.SpousePersonId = spouse.Id;
            app.SpouseFirstName = spouse.FirstName;
            app.SpouseLastName = spouse.LastName;
            app.SpouseEmail = spouse.Email;
        }
        else
        {
            app.SpousePersonId = null;
            app.SpouseFirstName = Cap(req.SpouseFirstName, 60);
            app.SpouseLastName = Cap(req.SpouseLastName, 60);
            app.SpouseEmail = string.IsNullOrWhiteSpace(req.SpouseEmail) ? null : Cap(req.SpouseEmail, 200);
        }
        app.AnswersJson = JsonSerializer.Serialize(ApplicationForm.Clean(req.Answers));
        app.CurrentStep = Math.Clamp(req.Step, 0, ApplicationForm.Sections.Length);
        app.UpdatedAt = now;
        await SaveOrConflict(ct);
        return app;
    }

    /// <summary>Validates, authorizes the card for the session price, and submits. Charges nothing.</summary>
    public async Task SubmitAsync(int householdId, int id, CardRequest req, CancellationToken ct)
    {
        var app = await Owned(householdId, id, ct);
        if (app.Stage != ApplicationStage.Draft)
        {
            if (app.Stage == ApplicationStage.Submitted) return; // a repeated submit is the same submit
            throw new AdmittanceException(409, "This application was already submitted.");
        }
        var answers = Answers(app);
        var errors = ApplicationForm.Validate(answers, app.SpouseFirstName, app.SpouseLastName);
        if (errors.Count > 0) throw new AdmittanceException(400, "Some answers are missing.", errors);

        var hold = await gateway.AuthorizeAsync(req.CardToken, app.AmountCents, $"auth-{app.Id}-{req.IdempotencyKey}", ct);
        if (!hold.Succeeded) throw new AdmittanceException(402, hold.DeclineReason ?? "Your card was declined.");

        var now = DateTime.UtcNow;
        SetHold(app, hold, now);
        app.Stage = ApplicationStage.Submitted;
        app.SubmittedAt = now;
        app.UpdatedAt = now;
        audit.Record("application.submitted", Entity, app.Id, $"{app.CoupleName} applied; {Money(app.AmountCents)} authorized, not charged (card ending {hold.CardLast4}).");
        Outbox("ApplicationSubmitted", app, new { app.Id, to = app.Household.Email, amountCents = app.AmountCents });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent submit won. Release this second hold unless it's the same one.
            db.ChangeTracker.Clear();
            var winner = await db.Set<AdmittanceApplication>().AsNoTracking().SingleAsync(a => a.Id == id, ct);
            if (winner.AuthorizationRef != hold.ProcessorRef) await gateway.VoidAsync(hold.ProcessorRef, ct);
            // If what won was a draft save rather than another submit, nothing was submitted.
            if (winner.Stage == ApplicationStage.Draft)
                throw new AdmittanceException(409, "Your application changed while we were submitting it. Nothing was authorized; please submit again.");
        }
    }

    /// <summary>
    /// A new card when the hold lapsed. Before a decision it replaces the hold; after approval it
    /// is authorized and captured straight away, which confirms the seat already held.
    /// </summary>
    public async Task ReauthorizeAsync(int householdId, string actor, int id, CardRequest req, CancellationToken ct)
    {
        var app = await Owned(householdId, id, ct);
        var now = DateTime.UtcNow;
        var approvedAwaitingCard = app.Stage == ApplicationStage.Approved && app.SeatHeld && app.Hold != HoldStatus.Captured;
        var pendingLapsed = AdmittanceApplication.Pending.Contains(app.Stage) && !app.HoldUsable(now);
        if (!approvedAwaitingCard && !pendingLapsed)
            throw new AdmittanceException(409, "Your card authorization is still active; there's nothing to update.");

        var hold = await gateway.AuthorizeAsync(req.CardToken, app.AmountCents, $"reauth-{app.Id}-{req.IdempotencyKey}", ct);
        if (!hold.Succeeded) throw new AdmittanceException(402, hold.DeclineReason ?? "Your card was declined.");
        var previous = app.Hold == HoldStatus.Authorized ? app.AuthorizationRef : null;
        SetHold(app, hold, now);
        app.UpdatedAt = now;
        audit.Record("application.reauthorized", Entity, app.Id, $"New card ending {hold.CardLast4} authorized for {Money(app.AmountCents)}, not charged.");
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else changed the application first (another re-entry, or a staff decision).
            // This hold was never recorded, so release it unless it's the one that won.
            db.ChangeTracker.Clear();
            var winner = await db.Set<AdmittanceApplication>().AsNoTracking().SingleAsync(a => a.Id == id, ct);
            if (winner.AuthorizationRef == hold.ProcessorRef) return; // a repeated request with the same key already saved it
            await gateway.VoidAsync(hold.ProcessorRef, ct);
            throw new AdmittanceException(409, "Your application changed while we were updating your card. This card wasn't charged; reload to see where it stands.");
        }
        if (previous is not null) await gateway.VoidAsync(previous, ct);
        if (approvedAwaitingCard) await CaptureAndConfirmAsync(app.Id, actor, ct);
    }

    public async Task ReplyAsync(int householdId, int id, string message, CancellationToken ct)
    {
        var app = await Owned(householdId, id, ct);
        if (app.Stage != ApplicationStage.InfoRequested) throw new AdmittanceException(409, "There's no open question on this application.");
        var reply = Required(message, "message", "Write a reply before sending.");
        app.InfoResponse = Cap(reply, 2000);
        app.InfoRespondedAt = DateTime.UtcNow;
        app.Stage = ApplicationStage.UnderReview;
        app.UpdatedAt = DateTime.UtcNow;
        audit.Record("application.info_provided", Entity, app.Id, $"{app.CoupleName} answered the team's question.");
        await SaveOrConflict(ct);
    }

    // ── Staff ───────────────────────────────────────────────────────────────

    public async Task StartReviewAsync(int id, string actor, CancellationToken ct)
    {
        var app = await Tracked(id, ct);
        if (app.Stage != ApplicationStage.Submitted) throw new AdmittanceException(409, $"Only submitted applications can move to review; this one is {Label(app.Stage)}.");
        app.Stage = ApplicationStage.UnderReview;
        app.ReviewStartedAt = DateTime.UtcNow;
        app.ReviewedBy = actor;
        app.UpdatedAt = DateTime.UtcNow;
        audit.Record("application.review_started", Entity, id, $"Review started for {app.CoupleName}.");
        await SaveOrConflict(ct);
    }

    public async Task RequestInfoAsync(int id, string actor, string message, CancellationToken ct)
    {
        var app = await Tracked(id, ct);
        if (app.Stage is not (ApplicationStage.Submitted or ApplicationStage.UnderReview))
            throw new AdmittanceException(409, $"You can only ask a question while an application is in review; this one is {Label(app.Stage)}.");
        var question = Required(message, "message", "Write the question for the couple.");
        var now = DateTime.UtcNow;
        app.Stage = ApplicationStage.InfoRequested;
        app.ReviewStartedAt ??= now;
        app.ReviewedBy = actor;
        app.InfoRequest = Cap(question, 2000);
        app.InfoRequestedAt = now;
        app.InfoResponse = null;
        app.InfoRespondedAt = null;
        app.UpdatedAt = now;
        audit.Record("application.info_requested", Entity, id, $"Asked {app.CoupleName}: {app.InfoRequest}");
        Outbox("ApplicationInfoRequested", app, new { app.Id, to = app.Household.Email });
        await SaveOrConflict(ct);
    }

    /// <summary>Claims a seat (never past capacity), then captures the hold if it's still good.</summary>
    public async Task<bool> ApproveAsync(int id, string actor, CancellationToken ct)
    {
        var app = await Tracked(id, ct);
        var now = DateTime.UtcNow;
        if (app.Stage == ApplicationStage.Approved)
        {
            if (app.Hold == HoldStatus.Captured) throw new AdmittanceException(409, "This application is already approved and paid.");
            if (!app.HoldUsable(now)) throw new AdmittanceException(409, "Approved. We're waiting on the couple to re-enter their card.");
            return await CaptureAndConfirmAsync(id, actor, ct); // retry after a capture that didn't finish
        }
        if (!AdmittanceApplication.Pending.Contains(app.Stage) && app.Stage != ApplicationStage.Waitlisted)
            throw new AdmittanceException(409, $"This application is {Label(app.Stage)}; it can't be approved.");

        var capture = app.HoldUsable(now);
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var pool = await db.CapacityPools.AsNoTracking().Where(p => p.SessionId == app.SessionId).OrderBy(p => p.SortOrder).FirstAsync(ct);
            var claimed = await db.CapacityPools.Where(p => p.Id == pool.Id && p.Reserved < p.Capacity)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved + 1), ct);
            if (claimed == 0)
                throw new AdmittanceException(409, $"{app.Session.Name} is full ({pool.Capacity} of {pool.Capacity} couples). Waitlist this couple instead, or raise capacity.");

            app.Stage = ApplicationStage.Approved;
            app.SeatHeld = true;
            app.PoolId = pool.Id;
            app.ReviewStartedAt ??= now;
            app.DecidedAt = now;
            app.ReviewedBy = actor;
            app.UpdatedAt = now;
            audit.Record("application.approved", Entity, id, capture
                ? $"Approved {app.CoupleName}; seat claimed; capturing {Money(app.AmountCents)}."
                : $"Approved {app.CoupleName}; seat held. Card authorization had lapsed, so the couple was asked to re-enter a card.");
            Outbox("ApplicationApproved", app, new { app.Id, to = app.Household.Email });
            if (!capture) Outbox("PaymentReauthRequested", app, new { app.Id, to = app.Household.Email, amountCents = app.AmountCents });
            await SaveOrConflict(ct);
            await tx.CommitAsync(ct);
        }
        return capture && await CaptureAndConfirmAsync(id, actor, ct);
    }

    public async Task DeclineAsync(int id, string actor, string message, CancellationToken ct)
    {
        var app = await Tracked(id, ct);
        var note = Required(message, "message", "Write a short message to the couple explaining the decision.");
        var releasable = app.Stage == ApplicationStage.Approved && app.Hold != HoldStatus.Captured;
        if (!AdmittanceApplication.Pending.Contains(app.Stage) && app.Stage != ApplicationStage.Waitlisted && !releasable)
            throw new AdmittanceException(409, $"This application is {Label(app.Stage)}; it can't be declined here.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (app.SeatHeld)
        {
            // Release the one seat approval claimed, in the pool it claimed it from.
            var poolId = app.PoolId ?? await FirstPoolId(app.SessionId, ct);
            await db.CapacityPools.Where(p => p.Id == poolId && p.Reserved > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved - 1), ct);
            app.SeatHeld = false;
            app.PoolId = null;
        }
        var voided = await VoidHold(app, ct);
        app.Stage = ApplicationStage.Declined;
        app.DecidedAt = DateTime.UtcNow;
        app.ReviewedBy = actor;
        app.DecisionNote = Cap(note, 2000);
        app.UpdatedAt = DateTime.UtcNow;
        audit.Record("application.declined", Entity, id, $"Declined {app.CoupleName}.{(voided ? $" {Money(app.AmountCents)} hold voided." : "")} Note: {app.DecisionNote}");
        Outbox("ApplicationDeclined", app, new { app.Id, to = app.Household.Email });
        await SaveOrConflict(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Only when the pool is full (a waitlist beside open seats is a defect). Voids the hold.</summary>
    public async Task WaitlistAsync(int id, string actor, CancellationToken ct)
    {
        var app = await Tracked(id, ct);
        if (!AdmittanceApplication.Pending.Contains(app.Stage))
            throw new AdmittanceException(409, $"This application is {Label(app.Stage)}; it can't be waitlisted.");
        var remaining = await db.CapacityPools.Where(p => p.SessionId == app.SessionId).SumAsync(p => p.Capacity - p.Reserved, ct);
        if (remaining > 0)
            throw new AdmittanceException(409, $"{app.Session.Name} still has {remaining} open {(remaining == 1 ? "spot" : "spots")}. Approve or decline instead.");

        var voided = await VoidHold(app, ct);
        app.Stage = ApplicationStage.Waitlisted;
        app.DecidedAt = DateTime.UtcNow;
        app.ReviewedBy = actor;
        app.DecisionNote = "The retreat is full. If a spot opens, we'll ask you to re-enter your card to confirm.";
        app.UpdatedAt = DateTime.UtcNow;
        audit.Record("application.waitlisted", Entity, id, $"Waitlisted {app.CoupleName}; the session is full.{(voided ? $" {Money(app.AmountCents)} hold voided." : "")}");
        Outbox("ApplicationWaitlisted", app, new { app.Id, to = app.Household.Email });
        await SaveOrConflict(ct);
    }

    // ── Capture ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Captures the hold and creates the paid order and confirmed registration. The capture key is
    /// tied to the authorization, so a retry never charges twice. Returns false if the card was
    /// refused, in which case the family is asked for a new card and the seat stays held.
    /// </summary>
    async Task<bool> CaptureAndConfirmAsync(int id, string actor, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var app = await Tracked(id, ct);
        if (app.Hold == HoldStatus.Captured) return true;
        var key = $"capture-{app.Id}-{app.AuthorizationRef}";
        var result = await gateway.CaptureAsync(app.AuthorizationRef ?? "", app.AmountCents, key, ct);
        var now = DateTime.UtcNow;

        if (!result.Succeeded)
        {
            app.Hold = HoldStatus.None;
            app.UpdatedAt = now;
            audit.Record("payment.capture_failed", Entity, id, $"Capture of {Money(app.AmountCents)} failed: {result.DeclineReason} The couple was asked to re-enter a card.");
            Outbox("PaymentReauthRequested", app, new { app.Id, to = app.Household.Email, amountCents = app.AmountCents });
            await SaveOrConflict(ct);
            return false;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = new PaymentOrder
        {
            HouseholdId = app.HouseholdId,
            SessionId = app.SessionId,
            IdempotencyKey = key,
            ConfirmationCode = "WS-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(3)),
            PaymentOption = PaymentOption.Full,
            SubtotalCents = app.AmountCents,
            TotalCents = app.AmountCents,
            DueTodayCents = app.AmountCents,
            Status = OrderStatus.Paid,
            CreatedAt = now,
        };
        order.Operations.Add(new PaymentOperation { Kind = PaymentKind.Authorize, AmountCents = app.AmountCents, Succeeded = true, ProcessorRef = app.AuthorizationRef ?? "", CardLast4 = app.CardLast4 ?? "", Reason = "Admittance application hold", CreatedAt = app.AuthorizedAt ?? now });
        order.Operations.Add(new PaymentOperation { Kind = PaymentKind.Charge, AmountCents = app.AmountCents, Succeeded = true, ProcessorRef = result.ProcessorRef, CardLast4 = app.CardLast4 ?? "", Reason = "Captured on approval", CreatedAt = now });
        var poolId = app.PoolId ?? await FirstPoolId(app.SessionId, ct);
        var pool = await db.CapacityPools.AsNoTracking().SingleAsync(p => p.Id == poolId, ct);
        var registration = new Registration
        {
            Order = order,
            SessionId = app.SessionId,
            PoolId = pool.Id,
            PersonId = app.ApplicantPersonId,
            HouseholdId = app.HouseholdId,
            Status = RegistrationStatus.Confirmed,
            Grade = pool.GradeMin,
            PriceCents = app.AmountCents,
            PaidCents = app.AmountCents,
            // The application collected dietary and accessibility needs; there's no separate health form.
            HealthStatus = FormStatus.Complete,
            AnswersJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["spouse"] = $"{app.SpouseFirstName} {app.SpouseLastName}".Trim(), ["applicationId"] = app.Id.ToString(CultureInfo.InvariantCulture) }),
            CreatedAt = now,
        };
        db.Registrations.Add(registration);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Another approval of the same hold got here first; the capture key made it one charge.
            if (await OrderExists(key, ct)) return true;
            throw;
        }

        app.Hold = HoldStatus.Captured;
        app.OrderId = order.Id;
        app.RegistrationId = registration.Id;
        app.UpdatedAt = now;
        audit.Record("payment.captured", Entity, id, $"Captured {Money(app.AmountCents)} on card ending {app.CardLast4}; {app.CoupleName} confirmed ({order.ConfirmationCode}) by {actor}.");
        audit.Record("registration.confirmed", "Registration", registration.Id, $"{app.CoupleName} confirmed for {app.Session.Program.Name} · {app.Session.Name} after approval.");
        Outbox("RegistrationConfirmed", app, new { order.ConfirmationCode, to = app.Household.Email });
        await SaveOrConflict(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>The pool approval claims from: the session's first pool (a retreat has one, "Couples").</summary>
    Task<int> FirstPoolId(int sessionId, CancellationToken ct) =>
        db.CapacityPools.Where(p => p.SessionId == sessionId).OrderBy(p => p.SortOrder).Select(p => p.Id).FirstAsync(ct);

    async Task<bool> OrderExists(string key, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        return await db.Orders.AnyAsync(o => o.IdempotencyKey == key, ct);
    }

    async Task<Session> AdmittanceSession(int sessionId, CancellationToken ct) =>
        await db.Sessions.Include(s => s.Program).AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.Program.Type == ProgramType.Admittance && s.Program.IsPublished, ct)
        ?? throw new AdmittanceException(404, "That program doesn't take applications.");

    Task<AdmittanceApplication> Owned(int householdId, int id, CancellationToken ct) => Load(a => a.Id == id && a.HouseholdId == householdId, ct);

    Task<AdmittanceApplication> Tracked(int id, CancellationToken ct) => Load(a => a.Id == id && a.Stage != ApplicationStage.Draft, ct);

    async Task<AdmittanceApplication> Load(System.Linq.Expressions.Expression<Func<AdmittanceApplication, bool>> where, CancellationToken ct) =>
        await db.Set<AdmittanceApplication>().Include(a => a.Applicant).Include(a => a.Household)
            .Include(a => a.Session).ThenInclude(s => s.Program).FirstOrDefaultAsync(where, ct)
        ?? throw new AdmittanceException(404, "Application not found.");

    async Task<bool> VoidHold(AdmittanceApplication app, CancellationToken ct)
    {
        if (app.Hold != HoldStatus.Authorized || app.AuthorizationRef is null) return false;
        var result = await gateway.VoidAsync(app.AuthorizationRef, ct);
        if (!result.Succeeded) throw new AdmittanceException(409, result.DeclineReason ?? "The card hold couldn't be voided.");
        app.Hold = HoldStatus.Voided;
        return true;
    }

    static void SetHold(AdmittanceApplication app, GatewayResult hold, DateTime now)
    {
        app.Hold = HoldStatus.Authorized;
        app.AuthorizationRef = hold.ProcessorRef;
        app.CardLast4 = hold.CardLast4;
        app.AuthorizedAt = now;
        app.AuthorizationExpiresAt = now + HoldLifetime;
    }

    async Task SaveOrConflict(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new AdmittanceException(409, "Someone else just changed this application. Reload to see the latest."); }
    }

    void Outbox(string type, AdmittanceApplication app, object payload) =>
        db.OutboxEvents.Add(new OutboxEvent { Type = type, Target = "HubSpot", AggregateId = $"app-{app.Id}", PayloadJson = JsonSerializer.Serialize(payload), CreatedAt = DateTime.UtcNow });

    public static Dictionary<string, string> Answers(AdmittanceApplication app) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(app.AnswersJson) ?? [];

    static string Required(string? value, string key, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new AdmittanceException(400, message, new() { [key] = [message] }) : value.Trim();

    static string Cap(string? value, int max)
    {
        var v = (value ?? "").Trim();
        return v.Length > max ? v[..max] : v;
    }

    public static string Label(ApplicationStage stage) => stage switch
    {
        ApplicationStage.UnderReview => "under review",
        ApplicationStage.InfoRequested => "waiting on the couple's answer",
        _ => stage.ToString().ToLowerInvariant(),
    };

    public static string Money(int cents) => CheckoutService.Money(cents);
}
