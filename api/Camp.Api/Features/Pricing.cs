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

    public static Quote Build(Session session, IReadOnlyList<Person> participants, PaymentOption option, DiscountCode? discount, string? enteredCode, DateOnly today)
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
                schedule.AddRange(PlanSchedule(session, total - deposit, today));
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

    /// <summary>
    /// Equal monthly installments, the last one landing on the session's balance due date. When <paramref name="today"/>
    /// is past the first of those dates (the family registered late), the whole schedule moves forward by whole months
    /// until the first installment is on or after today, keeping the count; no installment lands on or after the day
    /// the session starts.
    /// </summary>
    public static IEnumerable<ScheduleItem> PlanSchedule(Session session, int remaining, DateOnly today)
    {
        var n = session.PlanInstallments;
        if (n == 0 || remaining <= 0) yield break;
        var each = remaining / n;
        var dates = PlanDates(session, today);
        for (var i = 0; i < n; i++)
        {
            var amount = i == n - 1 ? remaining - each * (n - 1) : each;
            yield return new(dates[i], amount, $"Installment {i + 1} of {n}");
        }
    }

    /// <summary>The installment dates <see cref="PlanSchedule"/> uses: never before today, never on or after the start date.</summary>
    public static List<DateOnly> PlanDates(Session session, DateOnly today)
    {
        var n = session.PlanInstallments;
        var shift = 0;
        while (session.BalanceDueDate.AddMonths(shift - (n - 1)) < today) shift++;
        var latest = session.StartDate > today ? session.StartDate.AddDays(-1) : today;
        return Enumerable.Range(0, n)
            .Select(i => session.BalanceDueDate.AddMonths(shift + i - (n - 1)))
            .Select(d => d > latest ? latest : d)
            .ToList();
    }
}
