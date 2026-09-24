using Camp.Api.Domain;
using Camp.Api.Integrations;

namespace Camp.Api.Features.Finance;

/// <summary>Rules and small helpers shared by the finance endpoints.</summary>
internal static class Fin
{
    /// <summary>Automatic retries 3 and 7 days after a failure; the grace period ends 7 days after it (Program Policies).</summary>
    public const int FirstRetryDays = 3;
    public const int GraceDays = 7;
    public const int MaxAttempts = 3;

    public static string Money(int cents) =>
        (cents / 100m).ToString(cents % 100 == 0 ? "C0" : "C2", CultureInfo.GetCultureInfo("en-US"));

    public static string Day(DateOnly d) => d.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    public static IResult Invalid(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });

    public static IResult Conflict(string message) => Results.Conflict(new { error = message });

    /// <summary>Department code for a program in Oracle Fusion, e.g. "WSC-FC" for Family Camp.</summary>
    public static string Department(string ministryCode, string programName)
    {
        var initials = string.Concat(programName.Split([' ', '·'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetter(w[0])).Select(w => char.ToUpperInvariant(w[0])));
        return $"{ministryCode}-{initials}";
    }

    /// <summary>
    /// A charge token for the card on file. Production passes the Fiserv vault token straight through;
    /// the sandbox gateway only knows tokens it issued this process, so it issues one for the test card.
    /// </summary>
    public static string ChargeToken(IPaymentGateway gateway, FinanceCardOnFile card) =>
        gateway is FakeFiservGateway fake && card.VaultRef.StartsWith("sandbox:", StringComparison.Ordinal)
            ? fake.Tokenize(card.VaultRef["sandbox:".Length..])
            : card.VaultRef;

    /// <summary>Spreads money received across registrations by what each still owes, never past zero.</summary>
    public static void Allocate(IReadOnlyList<Registration> regs, int amount)
    {
        var owing = regs.Where(r => r.BalanceCents > 0).ToList();
        var owed = owing.Sum(r => r.BalanceCents);
        if (owed <= 0) return;
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

    /// <summary>
    /// After the balance drops without a charge (a scholarship), shrink the unpaid installments from the
    /// last one back so the plan still adds up to what's owed. An installment reduced to zero is closed.
    /// </summary>
    public static void ShrinkPlan(IEnumerable<Installment> installments, int newBalanceCents)
    {
        var open = installments.Where(i => i.Status != InstallmentStatus.Paid).OrderByDescending(i => i.DueDate).ThenByDescending(i => i.Sequence).ToList();
        var excess = open.Sum(i => i.AmountCents) - Math.Max(0, newBalanceCents);
        foreach (var i in open)
        {
            if (excess <= 0) break;
            var cut = Math.Min(excess, i.AmountCents);
            i.AmountCents -= cut;
            excess -= cut;
            if (i.AmountCents == 0) i.Status = InstallmentStatus.Paid;
        }
    }
}
