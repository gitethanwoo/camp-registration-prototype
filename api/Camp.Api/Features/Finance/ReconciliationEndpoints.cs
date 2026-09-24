using System.Text.Json;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Finance;

public sealed record ResolveLineRequest(string? Resolution, string? Code, string? Note);

/// <summary>
/// FN2 · Reconciliation (FR-71). Each Fiserv settlement batch against the platform's payments:
/// matched, unmatched and fee lines, Resolve for an unmatched line, and the batch's Fusion journal.
/// </summary>
public sealed class ReconciliationEndpoints : IEndpointModule
{
    public const int MaxNoteLength = 500;

    public void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin/finance").RequireAuthorization(Policies.Finance);

        g.MapGet("/settlements", async (CampDbContext db, CancellationToken ct) =>
        {
            var batches = await db.Set<SettlementBatch>().AsNoTracking().OrderByDescending(b => b.SettledOn).ToListAsync(ct);
            var lines = await db.Set<SettlementLine>().AsNoTracking()
                .Select(l => new { l.BatchId, l.Kind, l.AmountCents, l.Status }).ToListAsync(ct);
            var journals = await db.Set<JournalBatch>().AsNoTracking().ToDictionaryAsync(j => j.SettlementBatchId, ct);
            return Results.Ok(new
            {
                Batches = batches.Select(b =>
                {
                    var mine = lines.Where(l => l.BatchId == b.Id).ToList();
                    var tx = mine.Where(l => l.Kind != SettlementLineKind.Fee).ToList();
                    journals.TryGetValue(b.Id, out var j);
                    return new
                    {
                        b.Id,
                        b.Reference,
                        b.SettledOn,
                        Transactions = tx.Count,
                        GrossCents = tx.Sum(l => l.AmountCents),
                        FeeCents = -mine.Where(l => l.Kind == SettlementLineKind.Fee).Sum(l => l.AmountCents),
                        Unmatched = tx.Count(l => l.Status == SettlementLineStatus.Unmatched),
                        JournalStatus = j?.Status.ToString(),
                    };
                }),
                Unsettled = await Unsettled(db, ct),
            });
        });

        g.MapGet("/settlements/{id:int}", async (int id, CampDbContext db, CancellationToken ct) =>
        {
            var b = await db.Set<SettlementBatch>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (b is null) return Results.NotFound();
            var lines = await Ledger.BatchLines(db, id, ct);
            var t = Ledger.Sum(lines);
            var platform = await PlatformTotal(db, id, ct);
            var j = await db.Set<JournalBatch>().AsNoTracking().FirstOrDefaultAsync(x => x.SettlementBatchId == id, ct);
            return Results.Ok(new
            {
                b.Id,
                b.Reference,
                b.SettledOn,
                b.ReceivedAt,
                Processor = new { t.GrossCents, t.FeeCents, t.NetCents },
                Platform = platform,
                Transactions = new { Total = t.Lines, t.Matched, t.Unmatched, t.Resolved, t.UnmatchedCents },
                Journal = j is null
                    ? new { Id = (int?)null, Reference = (string?)null, Status = "Not created", Detail = t.Unmatched > 0 ? $"Waiting on {t.Unmatched} unmatched {(t.Unmatched == 1 ? "line" : "lines")}. The journal is created when every line is matched or resolved." : "Not created yet." }
                    : new { Id = (int?)j.Id, Reference = (string?)j.Reference, Status = j.Status.ToString(), Detail = j.Status switch { JournalStatus.Posted => "Posted to the general ledger.", JournalStatus.Failed => j.ErrorDetail ?? "Export failed.", _ => "Queued for Oracle Fusion; not yet posted." } },
                Lines = lines.Select(l => new
                {
                    l.Id,
                    Kind = l.Kind.ToString(),
                    l.ProcessorRef,
                    l.TransactedAt,
                    l.AmountCents,
                    l.Description,
                    l.CardholderName,
                    l.CardLast4,
                    Status = l.Status.ToString(),
                    l.UnmatchedReason,
                    Resolution = l.Resolution?.ToString(),
                    l.ResolutionNote,
                    l.ResolvedBy,
                    l.ResolvedAt,
                    l.ConfirmationCode,
                    l.Program,
                    l.Session,
                }),
            });
        });

        g.MapGet("/settlement-lines/{id:int}/candidates", async (int id, string? q, CampDbContext db, CancellationToken ct) =>
        {
            var line = await db.Set<SettlementLine>().AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
            if (line is null) return Results.NotFound();
            return Results.Ok(new { Candidates = await Candidates(db, line, q, ct) });
        });

        g.MapPost("/settlement-lines/{id:int}/resolve", Resolve);
    }

    /// <summary>Platform payments captured after the last settlement: Fiserv hasn't paid them out yet.</summary>
    static async Task<object> Unsettled(CampDbContext db, CancellationToken ct)
    {
        var settled = db.Set<SettlementLine>().Where(l => l.PaymentOperationId != null).Select(l => l.PaymentOperationId);
        var ops = await db.PaymentOperations.AsNoTracking()
            .Where(o => o.Succeeded && (o.Kind == PaymentKind.Charge || o.Kind == PaymentKind.Refund) && !settled.Contains(o.Id))
            .Select(o => new { o.Kind, o.AmountCents }).ToListAsync(ct);
        return new { Count = ops.Count, AmountCents = ops.Sum(o => o.Kind == PaymentKind.Refund ? -o.AmountCents : o.AmountCents) };
    }

    /// <summary>What the platform recorded for the payments this batch settles (matched and resolved-as-match lines).</summary>
    static async Task<object> PlatformTotal(CampDbContext db, int batchId, CancellationToken ct)
    {
        var ops = await db.Set<SettlementLine>().AsNoTracking()
            .Where(l => l.BatchId == batchId && l.PaymentOperation != null)
            .Select(l => new { l.PaymentOperation!.Kind, l.PaymentOperation.AmountCents }).ToListAsync(ct);
        return new { GrossCents = ops.Sum(o => o.Kind == PaymentKind.Refund ? -o.AmountCents : o.AmountCents), Payments = ops.Count };
    }

    /// <summary>
    /// Registrations this money could belong to: an adult in the household has the cardholder's name
    /// and the order still owes at least this much. A search by confirmation code or family name widens it.
    /// </summary>
    static async Task<List<Candidate>> Candidates(CampDbContext db, SettlementLine line, string? q, CancellationToken ct)
    {
        if (line.Kind != SettlementLineKind.Payment) return [];
        var name = line.CardholderName?.Trim() ?? "";
        var term = q?.Trim() ?? "";
        var orders = db.Orders.AsNoTracking().Where(o => o.Status == OrderStatus.Paid);
        orders = term.Length >= 2
            ? orders.Where(o => o.ConfirmationCode == term || o.Household.Name.Contains(term) || o.Household.Members.Any(m => (m.FirstName + " " + m.LastName).Contains(term)))
            : orders.Where(o => o.Household.Members.Any(m => m.IsAdult && m.FirstName + " " + m.LastName == name));
        var found = await orders
            .Select(o => new
            {
                o.ConfirmationCode,
                Household = o.Household.Name,
                Payer = o.Household.Members.Where(m => m.IsAdult).OrderBy(m => m.Id).Select(m => m.FirstName + " " + m.LastName).FirstOrDefault(),
                Campers = o.Registrations.Where(r => r.Status != RegistrationStatus.Cancelled).Select(r => r.Person.FirstName).ToList(),
                Program = o.Session.Program.Name,
                Session = o.Session.Name,
                o.Session.StartDate,
                o.Session.EndDate,
                BalanceCents = o.Registrations.Where(r => r.Status != RegistrationStatus.Cancelled).Sum(r => r.PriceCents - r.DiscountCents - r.PaidCents),
            })
            .Take(25).ToListAsync(ct);
        return found
            .Select(o => new Candidate(o.ConfirmationCode, o.Household, o.Payer, o.Campers, o.Program, o.Session, o.StartDate, o.EndDate, o.BalanceCents,
                string.Equals(o.Payer, name, StringComparison.OrdinalIgnoreCase), o.BalanceCents >= line.AmountCents))
            .OrderByDescending(o => o.CanTake).ThenByDescending(o => o.NameMatches).Take(10).ToList();
    }

    public sealed record Candidate(string ConfirmationCode, string Household, string? Payer, List<string> Campers, string Program, string Session,
        DateOnly StartDate, DateOnly EndDate, int BalanceCents, bool NameMatches, bool CanTake);

    static async Task<IResult> Resolve(int id, ResolveLineRequest req, CampDbContext db, StaffUser staff, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var note = req.Note?.Trim();
        if (string.IsNullOrEmpty(note)) return Fin.Invalid("note", "Add an audit note saying how you know.");
        if (note.Length > MaxNoteLength) return Fin.Invalid("note", $"Notes are limited to {MaxNoteLength} characters.");
        var resolution = req.Resolution switch
        {
            "MatchedToRegistration" => SettlementResolution.MatchedToRegistration,
            "Adjustment" => (SettlementResolution?)SettlementResolution.Adjustment,
            _ => null,
        };
        if (resolution is null) return Fin.Invalid("resolution", "Choose a registration, or mark it as an adjustment.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var line = await db.Set<SettlementLine>().Include(l => l.Batch).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (line is null) return Results.NotFound();
        if (line.Status != SettlementLineStatus.Unmatched) return Fin.Conflict($"This line was already resolved by {line.ResolvedBy ?? "someone else"}.");

        PaymentOrder? order = null;
        if (resolution == SettlementResolution.MatchedToRegistration)
        {
            if (line.Kind != SettlementLineKind.Payment) return Fin.Invalid("resolution", "Only a payment can be matched to a registration.");
            var code = req.Code?.Trim() ?? "";
            // Lock the order row for this transaction before reading what it owes, so two different
            // unmatched lines matched to the same order at once can't both pass the overpayment check.
            await db.Database.ExecuteSqlAsync($"SELECT Id FROM PaymentOrders WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE ConfirmationCode = {code}", ct);
            order = await db.Orders.Include(o => o.Registrations).Include(o => o.Installments).Include(o => o.Household)
                .FirstOrDefaultAsync(o => o.ConfirmationCode == code && o.Status == OrderStatus.Paid, ct);
            if (order is null) return Fin.Invalid("code", "Choose a registration to match this payment to.");
            var owed = order.Registrations.Where(r => r.Status != RegistrationStatus.Cancelled).Sum(r => r.BalanceCents);
            if (owed < line.AmountCents)
                return Fin.Invalid("code", $"{order.ConfirmationCode} owes {Fin.Money(owed)}, less than this {Fin.Money(line.AmountCents)} payment. Matching it would overpay the registration; resolve it as an adjustment and refund the difference.");
        }

        // Claim the line; of two people resolving it at once, the second changes nothing.
        var now = clock.UtcNow();
        var claimed = await db.Set<SettlementLine>().Where(l => l.Id == id && l.Status == SettlementLineStatus.Unmatched)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.Status, SettlementLineStatus.Resolved)
                .SetProperty(l => l.Resolution, resolution)
                .SetProperty(l => l.ResolutionNote, note)
                .SetProperty(l => l.ResolvedBy, staff.Actor)
                .SetProperty(l => l.ResolvedAt, (DateTime?)now), ct);
        if (claimed == 0) return Fin.Conflict("This line was just resolved by someone else. Refresh to see how.");

        var money = Fin.Money(line.AmountCents);
        if (order is not null)
        {
            var op = new PaymentOperation
            {
                OrderId = order.Id,
                Kind = PaymentKind.Charge,
                AmountCents = line.AmountCents,
                Succeeded = true,
                ProcessorRef = line.ProcessorRef,
                CardLast4 = line.CardLast4 ?? "",
                Reason = "Phone payment (Fiserv virtual terminal), matched in reconciliation",
                CreatedAt = line.TransactedAt,
            };
            db.PaymentOperations.Add(op);
            line.PaymentOperation = op;
            var live = order.Registrations.Where(r => r.Status != RegistrationStatus.Cancelled).ToList();
            Fin.Allocate(live, line.AmountCents);
            var balance = live.Sum(r => r.BalanceCents);
            if (balance <= 0) foreach (var i in order.Installments) i.Status = InstallmentStatus.Paid;
            else Fin.ShrinkPlan(order.Installments, balance);
            audit.Record("settlement.matched", "SettlementLine", line.Id, $"Matched {money} ({line.ProcessorRef}, {line.CardholderName}) in {line.Batch.Reference} to {order.ConfirmationCode}. Balance now {Fin.Money(balance)}. Note: {note}");
            audit.Record("payment.recorded", "PaymentOrder", order.Id, $"{money} phone payment from Fiserv batch {line.Batch.Reference} applied. Balance now {Fin.Money(balance)}.");
            db.OutboxEvents.Add(new OutboxEvent { Type = "PaymentReceipt", Target = "HubSpot", AggregateId = order.ConfirmationCode, PayloadJson = JsonSerializer.Serialize(new { order.ConfirmationCode, amountCents = line.AmountCents, label = "Phone payment" }), CreatedAt = now });
        }
        else
        {
            audit.Record("settlement.adjusted", "SettlementLine", line.Id, $"Resolved {money} ({line.ProcessorRef}, {line.CardholderName ?? "no name"}) in {line.Batch.Reference} as an adjustment to unapplied receipts. Note: {note}");
        }
        await db.SaveChangesAsync(ct);

        var journal = await CreateJournalWhenReconciled(db, line.Batch, audit, clock, ct);
        await tx.CommitAsync(ct);
        return Results.Ok(new { Journal = journal });
    }

    /// <summary>Once no line in the batch is unmatched, queue its journal for Oracle Fusion.</summary>
    static async Task<string?> CreateJournalWhenReconciled(CampDbContext db, SettlementBatch batch, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        if (await db.Set<SettlementLine>().AnyAsync(l => l.BatchId == batch.Id && l.Status == SettlementLineStatus.Unmatched, ct)) return null;
        if (await db.Set<JournalBatch>().AnyAsync(j => j.SettlementBatchId == batch.Id, ct)) return null;
        var now = clock.UtcNow();
        var journal = new JournalBatch { Reference = $"JRN-{batch.SettledOn:yyyy-MM-dd}", SettlementBatchId = batch.Id, CreatedAt = now, Status = JournalStatus.Pending };
        journal.Events.Add(new JournalEvent { Status = JournalStatus.Pending, Detail = "Export created and queued for Oracle Fusion.", Actor = "System", At = now });
        db.Add(journal);
        var lines = Ledger.Journal(await Ledger.BatchLines(db, batch.Id, ct));
        db.OutboxEvents.Add(new OutboxEvent { Type = "JournalExport", Target = "OracleFusion", AggregateId = journal.Reference, PayloadJson = JsonSerializer.Serialize(new { journal.Reference, batch = batch.Reference, lines }), CreatedAt = now });
        audit.Record("journal.created", "JournalBatch", journal.Reference, $"{batch.Reference} fully reconciled; journal {journal.Reference} queued for Oracle Fusion.");
        await db.SaveChangesAsync(ct);
        return journal.Reference;
    }
}
