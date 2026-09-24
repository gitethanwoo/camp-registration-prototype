using System.Text.Json;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Finance;

/// <summary>
/// FN4 · Fusion journal export (FR-111). One journal batch per reconciled Fiserv settlement, with its
/// entries, totals, status (Posted, Pending, Failed) and, on failure, what Oracle Fusion said.
/// </summary>
public sealed class JournalEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin/finance/journals").RequireAuthorization(Policies.Finance);

        g.MapGet("", async (CampDbContext db, CancellationToken ct) =>
        {
            var journals = await db.Set<JournalBatch>().AsNoTracking().Include(j => j.SettlementBatch).OrderByDescending(j => j.SettlementBatch.SettledOn).ToListAsync(ct);
            var lines = (await Ledger.Lines(db, _ => true, ct)).ToLookup(l => l.BatchId);
            var waiting = await db.Set<SettlementBatch>().AsNoTracking()
                .Where(b => !db.Set<JournalBatch>().Any(j => j.SettlementBatchId == b.Id))
                .OrderByDescending(b => b.SettledOn)
                .Select(b => new { b.Id, b.Reference, b.SettledOn, Unmatched = b.Lines.Count(l => l.Status == SettlementLineStatus.Unmatched) })
                .ToListAsync(ct);
            return Results.Ok(new
            {
                Counts = new
                {
                    Total = journals.Count,
                    Posted = journals.Count(j => j.Status == JournalStatus.Posted),
                    Pending = journals.Count(j => j.Status == JournalStatus.Pending),
                    Failed = journals.Count(j => j.Status == JournalStatus.Failed),
                },
                Rows = journals.Select(j =>
                {
                    var mine = lines[j.SettlementBatchId].ToList();
                    var entries = Ledger.Journal(mine);
                    return new
                    {
                        j.Id,
                        j.Reference,
                        j.SettlementBatch.SettledOn,
                        Source = "Fiserv settlement",
                        SettlementReference = j.SettlementBatch.Reference,
                        Entries = mine.Count,
                        DebitCents = entries.Sum(e => e.DebitCents),
                        CreditCents = entries.Sum(e => e.CreditCents),
                        Status = j.Status.ToString(),
                        j.ErrorDetail,
                    };
                }),
                Waiting = waiting,
            });
        });

        g.MapGet("/{id:int}", async (int id, CampDbContext db, CancellationToken ct) =>
        {
            var j = await db.Set<JournalBatch>().AsNoTracking().Include(x => x.SettlementBatch).Include(x => x.Events).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (j is null) return Results.NotFound();
            var lines = await Ledger.BatchLines(db, j.SettlementBatchId, ct);
            var t = Ledger.Sum(lines);
            var entries = Ledger.Journal(lines);
            int debit = entries.Sum(e => e.DebitCents), credit = entries.Sum(e => e.CreditCents);
            return Results.Ok(new
            {
                j.Id,
                j.Reference,
                j.CreatedAt,
                Status = j.Status.ToString(),
                j.ErrorDetail,
                Settlement = new { j.SettlementBatch.Id, j.SettlementBatch.Reference, j.SettlementBatch.SettledOn, t.Lines, t.Matched, t.Resolved },
                Entries = lines.Count,
                t.GrossCents,
                t.FeeCents,
                t.NetCents,
                DebitCents = debit,
                CreditCents = credit,
                Balanced = debit == credit,
                JournalLines = entries,
                Events = j.Events.OrderBy(e => e.At).ThenBy(e => e.Id).Select(e => new { Status = e.Status.ToString(), e.Detail, e.Actor, e.At }),
            });
        });

        g.MapPost("/{id:int}/retry", async (int id, CampDbContext db, StaffUser staff, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var j = await db.Set<JournalBatch>().Include(x => x.SettlementBatch).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (j is null) return Results.NotFound();
            if (j.Status != JournalStatus.Failed) return Fin.Conflict($"{j.Reference} is {j.Status.ToString().ToLowerInvariant()}, so there's nothing to retry.");
            var error = j.ErrorDetail;
            var claimed = await db.Set<JournalBatch>().Where(x => x.Id == id && x.Status == JournalStatus.Failed)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, JournalStatus.Pending).SetProperty(x => x.ErrorDetail, (string?)null), ct);
            if (claimed == 0) return Fin.Conflict($"{j.Reference} was just retried by someone else.");
            var now = clock.UtcNow();
            db.Add(new JournalEvent { JournalBatchId = j.Id, Status = JournalStatus.Pending, Detail = "Export resent to Oracle Fusion.", Actor = staff.Actor, At = now });
            var entries = Ledger.Journal(await Ledger.BatchLines(db, j.SettlementBatchId, ct));
            db.OutboxEvents.Add(new OutboxEvent { Type = "JournalExport", Target = "OracleFusion", AggregateId = j.Reference, PayloadJson = JsonSerializer.Serialize(new { j.Reference, batch = j.SettlementBatch.Reference, lines = entries }), CreatedAt = now });
            audit.Record("journal.retried", "JournalBatch", j.Reference, $"Resent {j.Reference} to Oracle Fusion after: {error}");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { Status = JournalStatus.Pending.ToString() });
        });
    }
}
