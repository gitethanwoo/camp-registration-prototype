using System.Linq.Expressions;
using Camp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Finance;

/// <summary>A settlement line with the registration it belongs to, when it belongs to one.</summary>
public sealed record LineView(
    int Id, int BatchId, SettlementLineKind Kind, string ProcessorRef, DateTime TransactedAt, int AmountCents, string Description,
    string? CardholderName, string? CardLast4, SettlementLineStatus Status, string? UnmatchedReason, SettlementResolution? Resolution,
    string? ResolutionNote, string? ResolvedBy, DateTime? ResolvedAt, string? ConfirmationCode, int? MinistryId, string? MinistryCode,
    int? ProgramId, string? Program, int? SessionId, string? Session)
{
    /// <summary>Money from a family (payments, refunds), as opposed to Fiserv's fees.</summary>
    public bool IsRevenue => Kind != SettlementLineKind.Fee;

    public string? Department => MinistryCode is null || Program is null ? null : Fin.Department(MinistryCode, Program);
}

public sealed record JournalLine(string Account, string AccountName, string? Department, string Description, int DebitCents, int CreditCents);

/// <summary>Reads settlement lines and turns a batch into its Oracle Fusion journal.</summary>
internal static class Ledger
{
    public const string Cash = "1010";
    public const string Revenue = "4100";
    public const string Fees = "6120";
    public const string Unapplied = "2400";

    /// <summary>Settlement lines matching <paramref name="filter"/>, each with the registration order its payment belongs to.</summary>
    public static async Task<List<LineView>> Lines(CampDbContext db, Expression<Func<SettlementLine, bool>> filter, CancellationToken ct)
    {
        var lines = await db.Set<SettlementLine>().AsNoTracking().Where(filter)
            .OrderBy(l => l.Kind == SettlementLineKind.Fee).ThenBy(l => l.TransactedAt).ThenBy(l => l.Id)
            .Select(l => new { Line = l, OrderId = l.PaymentOperation == null ? (int?)null : l.PaymentOperation.OrderId })
            .ToListAsync(ct);
        var orderIds = lines.Where(x => x.OrderId is not null).Select(x => x.OrderId!.Value).Distinct().ToList();
        var orders = await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.ConfirmationCode, o.Session.Program.MinistryId, MinistryCode = o.Session.Program.Ministry.Code, o.Session.ProgramId, Program = o.Session.Program.Name, o.SessionId, Session = o.Session.Name })
            .ToDictionaryAsync(o => o.Id, ct);
        return lines.Select(x =>
        {
            var l = x.Line;
            var o = x.OrderId is { } id ? orders[id] : null;
            return new LineView(l.Id, l.BatchId, l.Kind, l.ProcessorRef, l.TransactedAt, l.AmountCents, l.Description, l.CardholderName, l.CardLast4, l.Status,
                l.UnmatchedReason, l.Resolution, l.ResolutionNote, l.ResolvedBy, l.ResolvedAt,
                o?.ConfirmationCode, o?.MinistryId, o?.MinistryCode, o?.ProgramId, o?.Program, o?.SessionId, o?.Session);
        }).ToList();
    }

    public static Task<List<LineView>> BatchLines(CampDbContext db, int batchId, CancellationToken ct) => Lines(db, l => l.BatchId == batchId, ct);

    public sealed record Totals(int GrossCents, int FeeCents, int NetCents, int Lines, int Matched, int Unmatched, int Resolved, int UnmatchedCents);

    public static Totals Sum(IReadOnlyCollection<LineView> lines)
    {
        var gross = lines.Where(l => l.IsRevenue).Sum(l => l.AmountCents);
        var fees = -lines.Where(l => !l.IsRevenue).Sum(l => l.AmountCents);
        var tx = lines.Where(l => l.IsRevenue).ToList();
        return new Totals(gross, fees, gross - fees, tx.Count,
            tx.Count(l => l.Status == SettlementLineStatus.Matched),
            tx.Count(l => l.Status == SettlementLineStatus.Unmatched),
            tx.Count(l => l.Status == SettlementLineStatus.Resolved),
            tx.Where(l => l.Status == SettlementLineStatus.Unmatched).Sum(l => l.AmountCents));
    }

    /// <summary>
    /// The journal for one settlement: debit cash for what reached the bank and merchant fees for
    /// what Fiserv kept; credit program revenue per department, and unapplied receipts for money no
    /// registration claims yet. Debits equal credits equal the batch's gross.
    /// </summary>
    public static List<JournalLine> Journal(IReadOnlyCollection<LineView> lines)
    {
        var t = Sum(lines);
        var result = new List<JournalLine>
        {
            new(Cash, "Cash – operating account", null, "Fiserv deposit", t.NetCents, 0),
        };
        if (t.FeeCents != 0) result.Add(new(Fees, "Merchant processing fees", null, "Fiserv interchange and processing fees", t.FeeCents, 0));
        foreach (var g in lines.Where(l => l.IsRevenue && l.Department is not null).GroupBy(l => (l.Department, l.Program)).OrderBy(g => g.Key.Department, StringComparer.Ordinal))
            result.Add(new(Revenue, "Program revenue", g.Key.Department, $"{g.Key.Program} registrations", 0, g.Sum(l => l.AmountCents)));
        var unapplied = lines.Where(l => l.IsRevenue && l.Department is null).Sum(l => l.AmountCents);
        if (unapplied != 0) result.Add(new(Unapplied, "Unapplied receipts", null, "Payments not tied to a registration", 0, unapplied));
        return result;
    }
}
