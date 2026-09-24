using Camp.Api.Data;
using Camp.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Setup;

/// <summary>
/// The time-based cancellation and refund table (K4, FR-49), read by the staff cancel screen (C3).
/// A session with no rows uses <see cref="Defaults"/>, which are the rules C3 used before this table existed.
/// </summary>
public static class RefundPolicy
{
    /// <summary>60+ days: everything beyond the deposit. 14–59 days: half of it. Under 14 days: nothing.</summary>
    public static IReadOnlyList<RefundTier> Defaults =>
    [
        new() { DaysBefore = 60, RefundPercent = 100, Basis = RefundBasis.BeyondDeposit },
        new() { DaysBefore = 14, RefundPercent = 50, Basis = RefundBasis.BeyondDeposit },
        new() { DaysBefore = 0, RefundPercent = 0, Basis = RefundBasis.BeyondDeposit },
    ];

    /// <summary>The session's tiers, longest notice first.</summary>
    public static async Task<List<RefundTier>> TiersForAsync(CampDbContext db, int sessionId, CancellationToken ct = default)
    {
        var tiers = await db.Set<RefundTier>().AsNoTracking().Where(t => t.SessionId == sessionId).ToListAsync(ct);
        return [.. (tiers.Count > 0 ? tiers : Defaults).OrderByDescending(t => t.DaysBefore)];
    }

    /// <summary>The tier a cancellation made <paramref name="daysUntilStart"/> days before the start falls in.</summary>
    public static RefundTier TierFor(IReadOnlyList<RefundTier> tiers, int daysUntilStart) =>
        tiers.Where(t => daysUntilStart >= t.DaysBefore).MaxBy(t => t.DaysBefore) ?? tiers.MinBy(t => t.DaysBefore)!;

    /// <summary>Refund in cents for a registration that has paid <paramref name="paidCents"/>.</summary>
    public static int Refund(RefundTier tier, int paidCents, int depositCents)
    {
        var basis = tier.Basis == RefundBasis.AmountPaid ? paidCents : paidCents - Math.Min(depositCents, paidCents);
        var gross = (int)((long)basis * tier.RefundPercent / 100);
        return Math.Max(0, gross - tier.AdminFeeCents);
    }

    /// <summary>"14–59 days before start: 50% of amounts paid beyond the deposit, less a $75 admin fee".</summary>
    public static string Describe(IReadOnlyList<RefundTier> tiers, RefundTier tier)
    {
        var longer = tiers.Where(t => t.DaysBefore > tier.DaysBefore).MinBy(t => t.DaysBefore);
        var window = (tier.DaysBefore, longer) switch
        {
            (_, null) => tier.DaysBefore == 0 ? "Any time before start" : $"{tier.DaysBefore}+ days before start",
            (0, { } l) => $"Within {l.DaysBefore} days of start",
            (var d, { } l) => $"{d}–{l.DaysBefore - 1} days before start",
        };
        return $"{window}: {Terms(tier)}";
    }

    /// <summary>"50% of amounts paid beyond the deposit, less a $75 admin fee".</summary>
    public static string Terms(RefundTier tier)
    {
        var what = tier.Basis == RefundBasis.AmountPaid ? "amounts paid" : "amounts paid beyond the deposit";
        var share = tier.RefundPercent switch
        {
            0 => "no refund",
            100 => $"full refund of {what}",
            var p => $"{p}% of {what}",
        };
        return tier.AdminFeeCents > 0 && tier.RefundPercent > 0 ? $"{share}, less a {SetupResults.Money(tier.AdminFeeCents)} admin fee" : share;
    }

    /// <summary>The cancel quote the C3 registration detail shows.</summary>
    public static object Quote(Registration r, IReadOnlyList<RefundTier> tiers, DateOnly today)
    {
        var days = r.Session.StartDate.DayNumber - today.DayNumber;
        var tier = TierFor(tiers, days);
        var refund = Math.Min(Refund(tier, r.PaidCents, r.Session.DepositCents), r.PaidCents);
        return new { DaysUntilStart = days, Rule = Describe(tiers, tier), SuggestedRefundCents = refund, MaxRefundCents = r.PaidCents };
    }
}
