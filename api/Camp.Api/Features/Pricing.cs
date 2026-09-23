using Camp.Api.Domain;

namespace Camp.Api.Features;

public record LineItem(int PersonId, string Name, int PriceCents, int DiscountCents);
public record ScheduleItem(DateOnly? DueDate, int AmountCents, string Label);

public record Quote(
    List<LineItem> Lines,
    int SubtotalCents,
    int DiscountCents,
    int TotalCents,
    int DueTodayCents,
    int RemainingCents,
    PaymentOption PaymentOption,
    List<ScheduleItem> Schedule,
    string? AppliedDiscountCode,
    string? DiscountError);

public static class Pricing
{
    // FR-62: a code pending admin approval is indistinguishable from an invalid one.
    public const string InvalidCodeMessage = "This code is not available.";

    public static Quote Build(Session session, IReadOnlyList<Person> participants, PaymentOption option, DiscountCode? discount, string? enteredCode)
    {
        var usable = discount is { Status: DiscountStatus.Approved } ? discount : null;
        string? discountError = !string.IsNullOrWhiteSpace(enteredCode) && usable is null ? InvalidCodeMessage : null;

        var lines = participants.Select(p =>
        {
            var off = usable switch
            {
                { Kind: DiscountKind.Percent } d => (int)Math.Round(session.PriceCents * Math.Min(d.Value, 100) / 100m),
                { Kind: DiscountKind.Flat } d => d.Value,
                _ => 0,
            };
            // Guard: a discount can never take a line below zero (never more than 100% of cost).
            return new LineItem(p.Id, p.FullName, session.PriceCents, Math.Min(off, session.PriceCents));
        }).ToList();

        var subtotal = lines.Sum(l => l.PriceCents);
        var discountTotal = lines.Sum(l => l.DiscountCents);
        var total = subtotal - discountTotal;
        var deposit = Math.Min(session.DepositCents * lines.Count, total);

        if (option == PaymentOption.Plan && session.PlanInstallments == 0) option = PaymentOption.Deposit;

        var schedule = new List<ScheduleItem>();
        int dueToday;
        switch (option)
        {
            case PaymentOption.Full:
                dueToday = total;
                schedule.Add(new(null, total, "Paid in full today"));
                break;
            case PaymentOption.Plan:
                dueToday = deposit;
                schedule.Add(new(null, deposit, "Deposit today"));
                schedule.AddRange(PlanSchedule(session, total - deposit));
                break;
            default:
                dueToday = deposit;
                schedule.Add(new(null, deposit, "Deposit today"));
                if (total > deposit) schedule.Add(new(session.BalanceDueDate, total - deposit, "Balance due"));
                break;
        }

        return new Quote(lines, subtotal, discountTotal, total, dueToday, total - dueToday, option, schedule,
            usable?.Code, discountError);
    }

    /// <summary>Equal monthly installments, the last one landing on the session's balance due date.</summary>
    public static IEnumerable<ScheduleItem> PlanSchedule(Session session, int remaining)
    {
        var n = session.PlanInstallments;
        if (n == 0 || remaining <= 0) yield break;
        var each = remaining / n;
        for (var i = 0; i < n; i++)
        {
            var amount = i == n - 1 ? remaining - each * (n - 1) : each;
            yield return new(session.BalanceDueDate.AddMonths(i - (n - 1)), amount, $"Installment {i + 1} of {n}");
        }
    }
}
