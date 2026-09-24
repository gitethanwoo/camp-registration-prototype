using System.Text.Json;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Host;

public sealed record InvoicePayRequest(string? IdempotencyKey, string? CardToken);

public enum InvoicePayOutcome { Succeeded, Declined, NotFound, NothingOwed, AlreadyProcessing, Invalid }

public sealed record InvoicePayResult(InvoicePayOutcome Outcome, int AmountCents, string? Message, string? CardLast4 = null);

/// <summary>
/// H3 · Pay an invoice's remaining balance through Fiserv (FR-89). The processor and SQL can't share
/// a transaction, so: insert a Pending payment (one per invoice) and read the balance in one
/// transaction; charge the card with the payment's key; then claim the Pending row with a
/// conditional update and record the result. A repeated key returns the first result.
/// </summary>
public sealed class InvoicePaymentService(CampDbContext db, IPaymentGateway gateway, IAuditLog audit)
{
    static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    public async Task<InvoicePayResult> PayAsync(int organizationId, int invoiceId, InvoicePayRequest req, string paidBy, CancellationToken ct)
    {
        var key = req.IdempotencyKey?.Trim() ?? "";
        if (key.Length is 0 or > 100) return new(InvoicePayOutcome.Invalid, 0, "Missing payment key. Reload the page and try again.");
        if (string.IsNullOrWhiteSpace(req.CardToken)) return new(InvoicePayOutcome.Invalid, 0, "Enter your card details first.");

        var repeat = await Existing(organizationId, invoiceId, key, ct);
        if (repeat is not null) return repeat;

        var invoice = await db.Set<HostInvoice>().AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.HostOrganizationId == organizationId, ct);
        if (invoice is null) return new(InvoicePayOutcome.NotFound, 0, null);

        await ReconcileStaleAsync(invoiceId, ct);

        var payment = new HostInvoicePayment
        {
            InvoiceId = invoiceId,
            IdempotencyKey = key,
            Status = HostPaymentStatus.Pending,
            PaidBy = paidBy,
            CreatedAt = DateTime.UtcNow,
        };
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            db.Add(payment);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException e) when (e.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
            {
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                return await Existing(organizationId, invoiceId, key, ct)
                    ?? new(InvoicePayOutcome.AlreadyProcessing, 0, "A payment for this invoice is already processing. Refresh in a moment to see it.");
            }
            // Read the balance only after our Pending row exists, so a payment that just finished is counted.
            var balance = await Balance(invoiceId, ct);
            if (balance <= 0)
            {
                await tx.RollbackAsync(ct);
                return new(InvoicePayOutcome.NothingOwed, 0, $"{invoice.Number} is already paid.");
            }
            payment.AmountCents = balance;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        var result = await gateway.ChargeAsync(req.CardToken, payment.AmountCents, GatewayKey(key), ct);
        await FinalizeAsync(payment.Id, result, ct);
        return result.Succeeded
            ? new(InvoicePayOutcome.Succeeded, payment.AmountCents, null, result.CardLast4)
            : new(InvoicePayOutcome.Declined, payment.AmountCents, result.DeclineReason);
    }

    async Task FinalizeAsync(int paymentId, GatewayResult result, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.ChangeTracker.Clear();
        var claimed = await db.Set<HostInvoicePayment>()
            .Where(p => p.Id == paymentId && p.Status == HostPaymentStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, result.Succeeded ? HostPaymentStatus.Succeeded : HostPaymentStatus.Declined)
                .SetProperty(p => p.ProcessorRef, result.ProcessorRef)
                .SetProperty(p => p.CardLast4, result.CardLast4)
                .SetProperty(p => p.DeclineReason, result.Succeeded ? null : result.DeclineReason), ct);
        if (claimed == 0) return;

        var payment = await db.Set<HostInvoicePayment>().AsNoTracking().SingleAsync(p => p.Id == paymentId, ct);
        var invoice = await db.Set<HostInvoice>().AsNoTracking().SingleAsync(i => i.Id == payment.InvoiceId, ct);
        var money = HostScope.Money(payment.AmountCents);
        if (result.Succeeded)
        {
            audit.Record("host.invoice_paid", "HostInvoice", invoice.Number, $"{money} charged to card ending {result.CardLast4} for {invoice.Number}.");
            db.OutboxEvents.Add(new OutboxEvent
            {
                Type = "HostInvoicePaid",
                Target = "HubSpot",
                AggregateId = invoice.Number,
                PayloadJson = JsonSerializer.Serialize(new { invoice.Number, amountCents = payment.AmountCents, payment.PaidBy }),
                CreatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            audit.Record("host.invoice_payment_declined", "HostInvoice", invoice.Number, $"{money} declined for {invoice.Number}. Invoice still due.");
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>A payment left Pending (the process died mid-charge) would block the invoice; ask the processor and finish it.</summary>
    async Task ReconcileStaleAsync(int invoiceId, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - StaleAfter;
        var stale = await db.Set<HostInvoicePayment>().AsNoTracking()
            .Where(p => p.InvoiceId == invoiceId && p.Status == HostPaymentStatus.Pending && p.CreatedAt < cutoff)
            .Select(p => new { p.Id, p.IdempotencyKey }).ToListAsync(ct);
        foreach (var p in stale)
        {
            var result = await gateway.LookupAsync(GatewayKey(p.IdempotencyKey), ct)
                ?? new GatewayResult(false, "", "", "Payment was not completed.");
            await FinalizeAsync(p.Id, result, ct);
        }
    }

    async Task<int> Balance(int invoiceId, CancellationToken ct)
    {
        var total = await db.Set<HostInvoiceLine>().Where(l => l.InvoiceId == invoiceId).SumAsync(l => l.AmountCents, ct);
        var paid = await db.Set<HostInvoicePayment>()
            .Where(p => p.InvoiceId == invoiceId && p.Status == HostPaymentStatus.Succeeded).SumAsync(p => p.AmountCents, ct);
        return total - paid;
    }

    async Task<InvoicePayResult?> Existing(int organizationId, int invoiceId, string key, CancellationToken ct)
    {
        var p = await db.Set<HostInvoicePayment>().AsNoTracking().FirstOrDefaultAsync(x => x.IdempotencyKey == key, ct);
        if (p is null) return null;
        var sameInvoice = p.InvoiceId == invoiceId
            && await db.Set<HostInvoice>().AnyAsync(i => i.Id == invoiceId && i.HostOrganizationId == organizationId, ct);
        if (!sameInvoice) return new(InvoicePayOutcome.Invalid, 0, "That payment key was already used. Reload the page and try again.");
        return p.Status switch
        {
            HostPaymentStatus.Succeeded => new(InvoicePayOutcome.Succeeded, p.AmountCents, null, p.CardLast4),
            HostPaymentStatus.Declined => new(InvoicePayOutcome.Declined, p.AmountCents, p.DeclineReason),
            _ => new(InvoicePayOutcome.AlreadyProcessing, p.AmountCents, "This payment is still processing. Refresh in a moment to see it."),
        };
    }

    static string GatewayKey(string key) => $"host-invoice-{key}";
}
