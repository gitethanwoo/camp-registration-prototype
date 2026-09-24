using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Setup;

/// <summary>
/// Applies a K5 discount rule (scope, dates, usage cap) wherever a code is priced: the guest quote and
/// checkout. A code without a rule behaves as it always has. A code outside its rule reads exactly
/// like an invalid code (FR-62).
/// </summary>
public static class DiscountRuleGate
{
    /// <summary>The code if it may be used for <paramref name="session"/> today, otherwise null.</summary>
    public static async Task<DiscountCode?> UsableAsync(CampDbContext db, DiscountCode? code, Session session, TimeProvider clock, CancellationToken ct = default)
    {
        if (code is null) return null;
        var rule = await db.Set<DiscountRule>().AsNoTracking().FirstOrDefaultAsync(r => r.DiscountCodeId == code.Id, ct);
        return rule is null || Applies(rule, session.Id, session.ProgramId, clock.Today()) ? code : null;
    }

    /// <summary>Whether a rule allows its code for a session on a date, ignoring nothing but the code's own approval.</summary>
    public static bool Applies(DiscountRule rule, int sessionId, int programId, DateOnly today) =>
        rule.Active
        && today >= rule.ValidFrom && today <= rule.ValidTo
        && (rule.SessionId is { } s ? s == sessionId : rule.ProgramId is null || rule.ProgramId == programId)
        && (rule.MaxUses is null || rule.Uses < rule.MaxUses);

    /// <summary>
    /// Takes one use of the code's cap with a conditional update, inside the caller's transaction.
    /// False when the cap was reached by someone else first. Codes without a rule always succeed.
    /// </summary>
    public static async Task<bool> ClaimAsync(CampDbContext db, int discountCodeId, CancellationToken ct = default)
    {
        var rules = db.Set<DiscountRule>().Where(r => r.DiscountCodeId == discountCodeId);
        if (!await rules.AnyAsync(ct)) return true;
        return await rules.Where(r => r.MaxUses == null || r.Uses < r.MaxUses)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Uses, r => r.Uses + 1), ct) == 1;
    }

    /// <summary>Gives a use back when the order that claimed it didn't go through.</summary>
    public static Task ReleaseAsync(CampDbContext db, string? code, CancellationToken ct = default) =>
        code is null ? Task.CompletedTask
            : db.Set<DiscountRule>().Where(r => r.DiscountCode.Code == code && r.Uses > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Uses, r => r.Uses - 1), ct);
}
