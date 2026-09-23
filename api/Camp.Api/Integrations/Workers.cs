using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Integrations;

/// <summary>
/// Delivers outbox events. Production: HubSpot transactional email, Salesforce upsert by external ID,
/// with retry/backoff and a repair queue. Here delivery is logged and marked processed.
/// </summary>
public class OutboxDispatcher(IServiceScopeFactory scopes, ILogger<OutboxDispatcher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CampDbContext>();
                var batch = await db.OutboxEvents.Where(e => e.ProcessedAt == null).OrderBy(e => e.Id).Take(50).ToListAsync(ct);
                foreach (var e in batch)
                {
                    log.LogInformation("Outbox → {Target}: {Type} {Aggregate} {Payload}", e.Target, e.Type, e.AggregateId, e.PayloadJson);
                    e.ProcessedAt = DateTime.UtcNow;
                }
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Outbox dispatch failed; will retry"); }
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }
    }
}

/// <summary>
/// FR-45: detects orders left in Pending (process died between charging and recording the result)
/// and repairs them from the processor's record, keyed by the idempotency key.
/// </summary>
public class PendingPaymentReconciler(IServiceScopeFactory scopes, ILogger<PendingPaymentReconciler> log) : BackgroundService
{
    static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CampDbContext>();
                var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGateway>();
                var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();
                var cutoff = DateTime.UtcNow - StaleAfter;
                var stale = await db.Orders.Where(o => o.Status == OrderStatus.Pending && o.CreatedAt < cutoff)
                    .Select(o => new { o.Id, o.IdempotencyKey }).ToListAsync(ct);
                foreach (var o in stale)
                {
                    var result = await gateway.LookupAsync(o.IdempotencyKey, ct)
                        ?? new GatewayResult(false, "", "", "Payment was not completed.");
                    log.LogWarning("Reconciling stale order {Id}: processor says {Succeeded}", o.Id, result.Succeeded);
                    await checkout.FinalizeAsync(o.Id, result, "system (reconciler)", ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Reconciliation pass failed"); }
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }
}

/// <summary>
/// FR-36: an offered spot that isn't accepted by the deadline is released back to the pool
/// (the admin then offers it to the next position; there is no auto-promotion in v1).
/// </summary>
public class WaitlistOfferExpiry(IServiceScopeFactory scopes, ILogger<WaitlistOfferExpiry> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CampDbContext>();
                var expired = await db.WaitlistEntries.Include(w => w.Person)
                    .Where(w => w.Status == WaitlistStatus.Offered && w.OfferExpiresAt < DateTime.UtcNow).ToListAsync(ct);
                foreach (var w in expired)
                {
                    await using var tx = await db.Database.BeginTransactionAsync(ct);
                    w.Status = WaitlistStatus.Expired;
                    await db.CapacityPools.Where(p => p.Id == w.PoolId && p.Reserved > 0)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved - 1), ct);
                    db.AuditEvents.Add(new AuditEvent { Actor = "system", Action = "waitlist.offer_expired", EntityType = "WaitlistEntry", EntityId = w.Id.ToString(), Detail = $"Offer to {w.Person.FullName} expired; seat released for the next position.", CreatedAt = DateTime.UtcNow });
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    log.LogInformation("Waitlist offer {Id} expired", w.Id);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Offer expiry pass failed"); }
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }
}
