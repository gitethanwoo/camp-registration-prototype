using System.Text.Json;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Camp.Api.Infrastructure;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Family;

/// <summary>Pay what's left on an order, or retry one failed installment (F6, FR-47, FR-48).</summary>
public sealed record PayRequest(string IdempotencyKey, string CardToken, int? InstallmentSequence);

public enum PayOutcome { Succeeded, Declined, NotFound, NothingOwed, AlreadyProcessing, Invalid }

public sealed record PayResult(PayOutcome Outcome, int AmountCents, string? Message, string? CardLast4 = null);

/// <summary>
/// Same shape as checkout, because the processor and SQL can't share a transaction:
/// 1. In one transaction, insert a Pending payment (only one may be pending per order) and only
///    then read the balance, so a payment that finished a moment ago is already counted.
/// 2. Charge the card with the payment's idempotency key.
/// 3. Record the charge, spread it across the order's registrations, and mark installments paid.
/// </summary>
public sealed class BalancePaymentService(CampDbContext db, IPaymentGateway gateway, IAuditLog audit, TimeProvider clock)
{
    static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    public async Task<PayResult> PayAsync(int householdId, string code, PayRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.IdempotencyKey) || req.IdempotencyKey.Length > 100)
            return new(PayOutcome.Invalid, 0, "Missing payment key. Reload the page and try again.");

        var repeat = await Existing(householdId, code, req.IdempotencyKey, ct);
        if (repeat is not null) return repeat;

        var order = await db.Orders.Include(o => o.Installments)
            .FirstOrDefaultAsync(o => o.ConfirmationCode == code && o.HouseholdId == householdId && o.Status == OrderStatus.Paid, ct);
        if (order is null) return new(PayOutcome.NotFound, 0, null);

        await ReconcileStaleAsync(order.Id, ct);

        Installment? installment = null;
        if (req.InstallmentSequence is { } seq)
        {
            installment = order.Installments.FirstOrDefault(i => i.Sequence == seq);
            if (installment is not { Status: InstallmentStatus.Failed })
                return new(PayOutcome.Invalid, 0, "Only a failed installment can be retried.");
        }

        var payment = new BalancePayment
        {
            OrderId = order.Id,
            IdempotencyKey = req.IdempotencyKey,
            Kind = installment is null ? BalancePaymentKind.Balance : BalancePaymentKind.Installment,
            InstallmentSequence = installment?.Sequence,
            Status = BalancePaymentStatus.Pending,
            CreatedAt = clock.UtcNow(),
        };

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            db.Add(payment);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException e) when (e.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
            {
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                return await Existing(householdId, code, req.IdempotencyKey, ct)
                    ?? new(PayOutcome.AlreadyProcessing, 0, "A payment for this registration is already processing. Refresh in a moment to see it.");
            }

            var balance = await Balance(order.Id, ct);
            payment.AmountCents = installment is null ? balance : Math.Min(installment.AmountCents, balance);
            if (payment.AmountCents <= 0)
            {
                await tx.RollbackAsync(ct);
                return new(PayOutcome.NothingOwed, 0, "Nothing is owed on this registration.");
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        var result = await gateway.ChargeAsync(req.CardToken, payment.AmountCents, $"balance-{req.IdempotencyKey}", ct);
        await FinalizeAsync(payment.Id, result, ct);
        return result.Succeeded
            ? new(PayOutcome.Succeeded, payment.AmountCents, null, result.CardLast4)
            : new(PayOutcome.Declined, payment.AmountCents, result.DeclineReason);
    }

    async Task FinalizeAsync(int paymentId, GatewayResult result, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.ChangeTracker.Clear();
        // Claim the pending row first. The original request and a reconcile pass can both get here;
        // the conditional update lets exactly one of them record the charge and allocate it.
        var claimed = await db.Set<BalancePayment>()
            .Where(p => p.Id == paymentId && p.Status == BalancePaymentStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, result.Succeeded ? BalancePaymentStatus.Succeeded : BalancePaymentStatus.Declined), ct);
        if (claimed == 0) return;
        var payment = await db.Set<BalancePayment>().SingleAsync(p => p.Id == paymentId, ct);
        var order = await db.Orders.Include(o => o.Registrations).ThenInclude(r => r.Person)
            .Include(o => o.Installments).Include(o => o.Session).ThenInclude(s => s.Program)
            .SingleAsync(o => o.Id == payment.OrderId, ct);

        var label = payment.Kind == BalancePaymentKind.Installment
            ? $"Installment {payment.InstallmentSequence} of {order.Installments.Count} (retry)"
            : "Balance payment";
        db.PaymentOperations.Add(new PaymentOperation
        {
            OrderId = order.Id,
            Kind = PaymentKind.Charge,
            AmountCents = payment.AmountCents,
            Succeeded = result.Succeeded,
            ProcessorRef = result.ProcessorRef,
            CardLast4 = result.CardLast4,
            Reason = result.Succeeded ? label : $"{label}: {result.DeclineReason}",
            CreatedAt = clock.UtcNow(),
        });
        payment.CardLast4 = result.CardLast4;

        var money = CheckoutService.Money(payment.AmountCents);
        if (result.Succeeded)
        {
            Allocate(order.Registrations.Where(r => r.Status != RegistrationStatus.Cancelled).ToList(), payment.AmountCents);
            var paidOff = order.Registrations.Where(r => r.Status != RegistrationStatus.Cancelled).Sum(r => r.BalanceCents) <= 0;
            foreach (var i in order.Installments.Where(i => i.Status != InstallmentStatus.Paid))
                if (paidOff || i.Sequence == payment.InstallmentSequence) i.Status = InstallmentStatus.Paid;
            audit.Record("payment.balance_paid", "PaymentOrder", order.Id, $"{label}: {money} charged to card ending {result.CardLast4}.");
            db.OutboxEvents.Add(new OutboxEvent
            {
                Type = "PaymentReceipt",
                Target = "HubSpot",
                AggregateId = order.ConfirmationCode,
                PayloadJson = JsonSerializer.Serialize(new { order.ConfirmationCode, amountCents = payment.AmountCents, label }),
                CreatedAt = clock.UtcNow(),
            });
        }
        else
        {
            payment.DeclineReason = result.DeclineReason;
            audit.Record("payment.balance_declined", "PaymentOrder", order.Id, $"{label}: {money} declined. Balance unchanged.");
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// A payment left Pending (the process died between charging and recording) would block the
    /// order forever. After two minutes, ask the processor what happened and finish it.
    /// </summary>
    async Task ReconcileStaleAsync(int orderId, CancellationToken ct)
    {
        var cutoff = clock.UtcNow() - StaleAfter;
        var stale = await db.Set<BalancePayment>().AsNoTracking()
            .Where(p => p.OrderId == orderId && p.Status == BalancePaymentStatus.Pending && p.CreatedAt < cutoff)
            .Select(p => new { p.Id, p.IdempotencyKey }).ToListAsync(ct);
        foreach (var p in stale)
        {
            var result = await gateway.LookupAsync($"balance-{p.IdempotencyKey}", ct)
                ?? new GatewayResult(false, "", "", "Payment was not completed.");
            await FinalizeAsync(p.Id, result, ct);
        }
    }

    /// <summary>Spreads a payment across registrations by what each still owes, never past zero.</summary>
    static void Allocate(List<Registration> regs, int amount)
    {
        var owing = regs.Where(r => r.BalanceCents > 0).ToList();
        var owed = owing.Sum(r => r.BalanceCents);
        var left = amount;
        foreach (var r in owing)
        {
            var share = (int)Math.Min((long)amount * r.BalanceCents / owed, r.BalanceCents);
            r.PaidCents += share;
            left -= share;
        }
        foreach (var r in owing)
        {
            if (left <= 0) break;
            var extra = Math.Min(left, r.BalanceCents);
            r.PaidCents += extra;
            left -= extra;
        }
    }

    async Task<int> Balance(int orderId, CancellationToken ct)
    {
        var regs = await db.Registrations.AsNoTracking()
            .Where(r => r.OrderId == orderId && r.Status != RegistrationStatus.Cancelled)
            .Select(r => new { r.PriceCents, r.DiscountCents, r.PaidCents }).ToListAsync(ct);
        return regs.Sum(r => r.PriceCents - r.DiscountCents - r.PaidCents);
    }

    /// <summary>A repeated key replays its result, but only for the same order it paid.</summary>
    async Task<PayResult?> Existing(int householdId, string code, string key, CancellationToken ct)
    {
        var p = await db.Set<BalancePayment>().AsNoTracking().Include(x => x.Order)
            .FirstOrDefaultAsync(x => x.IdempotencyKey == key, ct);
        if (p is null) return null;
        if (p.Order.HouseholdId != householdId || p.Order.ConfirmationCode != code) return new(PayOutcome.Invalid, 0, "That payment key was already used. Reload the page and try again.");
        return p.Status switch
        {
            BalancePaymentStatus.Succeeded => new(PayOutcome.Succeeded, p.AmountCents, null, p.CardLast4),
            BalancePaymentStatus.Declined => new(PayOutcome.Declined, p.AmountCents, p.DeclineReason),
            _ => new(PayOutcome.AlreadyProcessing, p.AmountCents, "This payment is still processing. Refresh in a moment to see it."),
        };
    }
}
