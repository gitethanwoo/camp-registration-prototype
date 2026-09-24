using System.Text.Json;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Family;
using Camp.Api.Infrastructure;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Finance;

public sealed record PlanRetryRequest(string? IdempotencyKey);

/// <summary>
/// FN3 · Payment plan exceptions (FR-48). Every failed installment, with its retry schedule, the end
/// of its grace period and the days left before policy action. Retry now charges the card on file
/// through the family slice's <see cref="BalancePaymentService"/>, the one path that charges installments.
/// </summary>
public sealed class PlanExceptionEndpoints : IEndpointModule
{
    static readonly TimeSpan ContactCooldown = TimeSpan.FromMinutes(10);

    public void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin/finance/plan-exceptions").RequireAuthorization(Policies.Finance);

        g.MapGet("", async (CampDbContext db, int? programId, CancellationToken ct) =>
        {
            var rows = await Load(db, programId, null, ct);
            var today = Fin.Today;
            var views = rows.Select(r => Row(r, today)).OrderBy(r => r.DaysToPolicyAction).ThenBy(r => r.Family).ToList();
            var programs = await db.Programs.AsNoTracking().Where(p => p.Sessions.Any()).OrderBy(p => p.Name).Select(p => new { p.Id, p.Name }).ToListAsync(ct);
            return Results.Ok(new
            {
                Counts = new
                {
                    Failed = views.Count,
                    OutstandingCents = views.Sum(v => v.AmountCents),
                    RetryScheduled = views.Count(v => v.NextRetryOn is not null),
                    NeedsAttention = views.Count(v => v.Stage == "Needs attention"),
                },
                Programs = programs,
                Rows = views,
            });
        });

        g.MapGet("/{installmentId:int}", async (int installmentId, CampDbContext db, CancellationToken ct) =>
        {
            var r = (await Load(db, null, installmentId, ct)).FirstOrDefault();
            if (r is null) return Results.NotFound();
            var today = Fin.Today;
            var order = await db.Orders.AsNoTracking().Include(o => o.Installments).Include(o => o.Household).ThenInclude(h => h.Members)
                .SingleAsync(o => o.Id == r.Installment.OrderId, ct);
            var card = await db.Set<FinanceCardOnFile>().AsNoTracking().FirstOrDefaultAsync(c => c.OrderId == order.Id, ct);
            var id = installmentId.ToString(CultureInfo.InvariantCulture);
            var timeline = await db.AuditEvents.AsNoTracking().Where(e => e.EntityType == "Installment" && e.EntityId == id)
                .OrderBy(e => e.CreatedAt).Select(e => new { e.CreatedAt, e.Action, e.Actor, e.Detail }).ToListAsync(ct);
            var primary = order.Household.Members.Where(m => m.IsAdult).OrderBy(m => m.Role != "Primary").ThenBy(m => m.Id).FirstOrDefault();
            return Results.Ok(new
            {
                Row = Row(r, today),
                Contact = new { Name = primary?.FullName ?? order.Household.Name, order.Household.Email, order.Household.Phone },
                Card = card is null ? null : new { card.Brand, card.Last4 },
                Plan = new
                {
                    DepositCents = order.DueTodayCents,
                    order.TotalCents,
                    Installments = order.Installments.OrderBy(i => i.Sequence).Select(i => new { i.Id, i.Sequence, i.DueDate, i.AmountCents, Status = i.Status.ToString() }),
                    Count = order.Installments.Count,
                },
                Timeline = timeline.Select(e => new { At = e.CreatedAt, Title = Title(e.Action), e.Actor, e.Detail })
                    .Concat(r.NextRetryOn is { } next ? [new { At = next.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc), Title = "Retry scheduled", Actor = "Fiserv (automatic retry)", Detail = "Next automatic attempt." }] : []),
            });
        });

        g.MapPost("/{installmentId:int}/retry", async (int installmentId, PlanRetryRequest req, CampDbContext db, IPaymentGateway gateway, IAuditLog audit, CancellationToken ct) =>
        {
            var key = req.IdempotencyKey?.Trim();
            if (string.IsNullOrEmpty(key) || key.Length > 80) return Fin.Invalid("idempotencyKey", "Missing request key. Reload the page and try again.");
            var installment = await db.Installments.AsNoTracking().FirstOrDefaultAsync(i => i.Id == installmentId, ct);
            if (installment is null) return Results.NotFound();
            if (installment.Status != InstallmentStatus.Failed) return Fin.Conflict($"Installment {installment.Sequence} isn't failed any more. Refresh to see its status.");
            var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == installment.OrderId, ct);
            var card = await db.Set<FinanceCardOnFile>().AsNoTracking().FirstOrDefaultAsync(c => c.OrderId == order.Id, ct);
            if (card is null) return Fin.Conflict("There's no card on file for this plan. Contact the family so they can pay from their account.");

            var service = new BalancePaymentService(db, gateway, audit);
            var result = await service.PayAsync(order.HouseholdId, order.ConfirmationCode, new PayRequest($"fn3-{key}", Fin.ChargeToken(gateway, card), installment.Sequence), ct);
            db.ChangeTracker.Clear();
            if (result.Outcome is PayOutcome.AlreadyProcessing or PayOutcome.Invalid or PayOutcome.NotFound)
                return Fin.Conflict(result.Message ?? "This installment can't be retried right now. Refresh to see its status.");

            var failure = await FailureFor(db, installmentId, ct);
            var id = installmentId.ToString(CultureInfo.InvariantCulture);
            var money = Fin.Money(result.AmountCents);
            if (result.Outcome is PayOutcome.Succeeded or PayOutcome.NothingOwed)
            {
                failure.ResolvedAt = DateTime.UtcNow;
                failure.NextRetryOn = null;
                audit.Record("installment.retry_succeeded", "Installment", id, result.Outcome == PayOutcome.Succeeded
                    ? $"Retried now: {money} charged to {card.Brand} ending {result.CardLast4 ?? card.Last4}."
                    : "Nothing was owed any more, so the installment was closed.");
            }
            else
            {
                failure.Attempts++;
                failure.DeclineReason = result.Message ?? failure.DeclineReason;
                audit.Record("installment.retry_failed", "Installment", id, $"Retried now: {money} declined ({failure.DeclineReason}).");
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new
            {
                Outcome = result.Outcome.ToString(),
                result.AmountCents,
                Message = result.Outcome switch
                {
                    PayOutcome.Succeeded => $"{money} charged to {card.Brand} ending {result.CardLast4 ?? card.Last4}. The plan is back on track.",
                    PayOutcome.NothingOwed => "Nothing is owed on this registration any more. The installment is closed.",
                    _ => $"The card was declined again: {failure.DeclineReason}. Contact the family for another card.",
                },
            });
        });

        g.MapPost("/{installmentId:int}/contact", async (int installmentId, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var r = (await Load(db, null, installmentId, ct)).FirstOrDefault();
            if (r is null) return Results.NotFound();
            var failure = await FailureFor(db, installmentId, ct);
            if (failure.LastContactedAt is { } last && DateTime.UtcNow - last < ContactCooldown)
                return Fin.Conflict($"This family was emailed at {last:h:mm tt} UTC. Wait a few minutes before sending another.");
            var graceEnd = r.FailedOn.AddDays(Fin.GraceDays);
            failure.LastContactedAt = DateTime.UtcNow;
            db.OutboxEvents.Add(new OutboxEvent
            {
                Type = "InstallmentFailedReminder",
                Target = "HubSpot",
                AggregateId = r.ConfirmationCode,
                PayloadJson = JsonSerializer.Serialize(new { r.ConfirmationCode, r.Email, r.Installment.Sequence, amountCents = r.Installment.AmountCents, graceEnds = graceEnd }),
                CreatedAt = DateTime.UtcNow,
            });
            audit.Record("installment.family_contacted", "Installment", installmentId,
                $"HubSpot emailed {r.Email}: installment {r.Installment.Sequence} ({Fin.Money(r.Installment.AmountCents)}) needs a new card by {Fin.Day(graceEnd)}.");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { Message = $"Email queued to {r.Email}." });
        });
    }

    sealed record Failed(Installment Installment, string Family, string Email, string ConfirmationCode, string Program, string Session, DateOnly FailedOn, int Attempts, string DeclineReason, DateOnly? NextRetryOn, DateTime? LastContactedAt);

    /// <summary>Failed installments. One with no failure record (another slice marked it failed) is dated by its due date.</summary>
    static async Task<List<Failed>> Load(CampDbContext db, int? programId, int? installmentId, CancellationToken ct)
    {
        var q = from i in db.Installments.AsNoTracking()
                join o in db.Orders on i.OrderId equals o.Id
                join f in db.Set<InstallmentFailure>() on i.Id equals f.InstallmentId into fs
                from f in fs.DefaultIfEmpty()
                where i.Status == InstallmentStatus.Failed
                select new { i, Family = o.Household.Name, o.Household.Email, o.ConfirmationCode, Program = o.Session.Program.Name, ProgramId = o.Session.ProgramId, Session = o.Session.Name, f };
        if (programId is { } p) q = q.Where(x => x.ProgramId == p);
        if (installmentId is { } id) q = q.Where(x => x.i.Id == id);
        var rows = await q.ToListAsync(ct);
        return rows.Select(x => new Failed(x.i, x.Family, x.Email, x.ConfirmationCode, x.Program, x.Session,
            x.f?.FailedOn ?? x.i.DueDate, x.f?.Attempts ?? 1, x.f?.DeclineReason ?? "Card declined",
            x.f is null ? x.i.DueDate.AddDays(Fin.FirstRetryDays) : x.f.NextRetryOn, x.f?.LastContactedAt)).ToList();
    }

    static async Task<InstallmentFailure> FailureFor(CampDbContext db, int installmentId, CancellationToken ct)
    {
        var failure = await db.Set<InstallmentFailure>().FirstOrDefaultAsync(f => f.InstallmentId == installmentId, ct);
        if (failure is not null) return failure;
        var i = await db.Installments.AsNoTracking().SingleAsync(x => x.Id == installmentId, ct);
        failure = new InstallmentFailure { InstallmentId = installmentId, FailedOn = i.DueDate, Attempts = 1, DeclineReason = "Card declined", NextRetryOn = i.DueDate.AddDays(Fin.FirstRetryDays) };
        db.Add(failure);
        return failure;
    }

    static PlanExceptionRow Row(Failed r, DateOnly today)
    {
        var graceEnd = r.FailedOn.AddDays(Fin.GraceDays);
        var daysLeft = Math.Max(0, graceEnd.DayNumber - today.DayNumber);
        var next = r.NextRetryOn is { } n && n >= today ? n : (DateOnly?)null;
        var stage = daysLeft == 0 || next is null ? "Needs attention" : r.Attempts <= 1 ? "Retry scheduled" : "In grace period";
        return new PlanExceptionRow(r.Installment.Id, r.Family, r.Email, r.ConfirmationCode, r.Program, r.Session, r.Installment.Sequence,
            r.Installment.AmountCents, r.FailedOn, r.Attempts, r.DeclineReason, next, graceEnd, daysLeft, "Installment failed", stage, r.LastContactedAt);
    }

    static string Title(string action) => action switch
    {
        "installment.failed" => "Payment attempt failed",
        "installment.family_notified" => "HubSpot family notified",
        "installment.retry_failed" => "Retry failed",
        "installment.retry_succeeded" => "Retry succeeded",
        "installment.family_contacted" => "Family contacted",
        _ => action,
    };
}

public sealed record PlanExceptionRow(
    int InstallmentId, string Family, string Email, string ConfirmationCode, string Program, string Session, int Sequence,
    int AmountCents, DateOnly FailedOn, int Attempts, string DeclineReason, DateOnly? NextRetryOn, DateOnly GraceEndsOn,
    int DaysToPolicyAction, string Status, string Stage, DateTime? LastContactedAt);
