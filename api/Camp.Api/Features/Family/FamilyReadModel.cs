using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Family;

/// <summary>One open or done item on a family checklist, with the page that resolves it.</summary>
public sealed record ChecklistEntry(string Key, string Participant, string Context, string Title, string Detail, bool Done, string Action, string Kind, string Href);

/// <summary>Money for one order, from its active (not cancelled) registrations. Total = Paid + Balance.</summary>
public sealed record OrderMoney(int TotalCents, int PaidCents, int BalanceCents, string PaymentStatus);

/// <summary>Queries and rules shared by the family portal endpoints.</summary>
public static class FamilyReadModel
{
    /// <summary>Days a family has to retry a failed installment (Program Policies: retries after 3 and 7 days).</summary>
    public const int GraceDays = 7;

    /// <summary>
    /// The camp season grades are shown for: the year of the next published session. Grades change
    /// on Sept 1, so "Grade 6" only means something next to a year ("Grade 6 in fall 2028").
    /// </summary>
    public static async Task<int> SeasonYearAsync(CampDbContext db, TimeProvider clock)
    {
        var today = clock.Today();
        var next = await db.Sessions.Where(s => s.Program.IsPublished && s.StartDate >= today)
            .OrderBy(s => s.StartDate).Select(s => (DateOnly?)s.StartDate).FirstOrDefaultAsync();
        return (next ?? today).Year;
    }

    /// <summary>Grade in the fall of <paramref name="seasonYear"/>, using the same Sept 1 rule as placement.</summary>
    public static int GradeFor(Person p, int seasonYear) => Eligibility.GradeFor(p.DateOfBirth, new DateOnly(seasonYear, 6, 1));

    public static int AgeOn(DateOnly dob, DateOnly day) => day.Year - dob.Year - (day < dob.AddYears(day.Year - dob.Year) ? 1 : 0);

    /// <summary>Orders for a household with everything the family pages show. Declined checkouts are left out: nothing happened.</summary>
    public static IQueryable<PaymentOrder> Orders(CampDbContext db, int householdId) =>
        db.Orders.Where(o => o.HouseholdId == householdId && o.Status != OrderStatus.Declined)
            .Include(o => o.Session).ThenInclude(s => s.Program).ThenInclude(p => p.Ministry)
            .Include(o => o.Session).ThenInclude(s => s.Program).ThenInclude(p => p.Waivers)
            .Include(o => o.Registrations).ThenInclude(r => r.Person)
            .Include(o => o.Registrations).ThenInclude(r => r.Pool)
            .Include(o => o.Registrations).ThenInclude(r => r.Session)
            .Include(o => o.Registrations).ThenInclude(r => r.WaiverAcceptances)
            .Include(o => o.Operations)
            .Include(o => o.Installments)
            .AsSplitQuery();

    /// <summary>A camper a staff-approved transfer (F8) moved to another session; the order keeps its session until every camper has moved.</summary>
    public static MovedSession? MovedTo(Registration r, PaymentOrder o) =>
        r.SessionId != o.SessionId ? new(r.Session.Name, r.Session.StartDate, r.Session.EndDate) : null;

    public static List<Registration> Active(PaymentOrder o) => o.Registrations.Where(r => r.Status != RegistrationStatus.Cancelled).ToList();

    public static OrderMoney Money(PaymentOrder o)
    {
        var active = Active(o);
        var total = active.Sum(r => r.PriceCents - r.DiscountCents);
        var paid = active.Sum(r => r.PaidCents);
        var balance = active.Sum(r => r.BalanceCents);
        return new(total, paid, balance, PaymentStatus(o, active, paid, balance));
    }

    /// <summary>Payment status in the global vocabulary: Paid, Deposit paid, Balance due, Plan active, Installment failed, Refunded.</summary>
    static string PaymentStatus(PaymentOrder o, List<Registration> active, int paid, int balance)
    {
        if (active.Count == 0)
            return o.Operations.Any(x => x.Kind == PaymentKind.Refund && x.Succeeded) ? "Refunded" : "";
        if (balance <= 0) return "Paid";
        if (o.Installments.Any(i => i.Status == InstallmentStatus.Failed)) return "Installment failed";
        if (o.Installments.Any(i => i.Status == InstallmentStatus.Scheduled)) return "Plan active";
        return paid > 0 && o.PaymentOption == PaymentOption.Deposit ? "Deposit paid" : "Balance due";
    }

    public static string Participants(IEnumerable<string> firstNames)
    {
        var names = firstNames.ToList();
        return names.Count switch
        {
            0 => "",
            1 => names[0],
            _ => $"{string.Join(", ", names[..^1])} and {names[^1]}",
        };
    }

    public static string? GradeLabel(Registration r) => r.Person.IsAdult ? null : Eligibility.GradeLabel(r.Grade);

    public static string Money(int cents) => CheckoutService.Money(cents);

    static string Day(DateOnly d) => d.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// FR-27: one combined list across every child and order, each item with the page that resolves it.
    /// Waivers and health are per child; the balance is per order (one card, one plan).
    /// </summary>
    public static IEnumerable<ChecklistEntry> Checklist(PaymentOrder o)
    {
        var program = o.Session.Program;
        var ctx = $"{program.Name} · {o.Session.Name}";
        var detail = $"/family/registrations/{o.ConfirmationCode}";
        var confirmed = o.Registrations.Where(r => r.Status == RegistrationStatus.Confirmed).ToList();
        foreach (var r in confirmed)
        {
            var who = r.Person.FirstName;
            // A moved camper's own rows name the session they now attend.
            var rctx = r.SessionId == o.SessionId ? ctx : $"{program.Name} · {r.Session.Name}";
            var signed = r.WaiverAcceptances.Select(a => a.WaiverTemplateId).ToHashSet();
            var missing = program.Waivers.Count(w => !signed.Contains(w.Id));
            if (program.Waivers.Count > 0)
                yield return new($"waiver-{r.Id}", who, rctx, "Waivers", missing == 0 ? "Complete" : $"Missing: {missing} to sign", missing == 0, "Sign waivers", "waiver", $"{detail}#checklist");

            if (r.HealthStatus is FormStatus.NotRequired) continue;
            var healthDone = r.HealthStatus == FormStatus.Complete;
            yield return program.HealthMechanism == HealthMechanism.CampDoc
                ? new($"health-{r.Id}", who, rctx, "Health forms in CampDoc", healthDone ? "Complete" : "Incomplete", healthDone, "Open CampDoc", "campdoc", "https://app.campdoc.com/")
                // The embedded health form is filled in during registration; there's no page to finish it
                // afterwards, so an incomplete one is shown without a button rather than a dead link.
                : new($"health-{r.Id}", who, rctx, "Health form", healthDone ? "Complete" : "Incomplete", healthDone, "", "health", "");
        }

        if (confirmed.Count == 0) yield break;
        var money = Money(o);
        var payments = $"{detail}/payments";
        var everyone = Participants(confirmed.Select(r => r.Person.FirstName));
        var failed = o.Installments.Where(i => i.Status == InstallmentStatus.Failed).OrderBy(i => i.DueDate).FirstOrDefault();
        var next = o.Installments.Where(i => i.Status == InstallmentStatus.Scheduled).OrderBy(i => i.DueDate).FirstOrDefault();
        if (money.BalanceCents <= 0)
            yield return new($"balance-{o.Id}", everyone, ctx, "Payment", $"Paid in full · {Money(money.PaidCents)}", true, "View payments", "balance", payments);
        else if (failed is not null)
            yield return new($"balance-{o.Id}", everyone, ctx, "Installment failed",
                $"{Money(failed.AmountCents)} due {Day(failed.DueDate)} didn't go through. Retry by {Day(failed.DueDate.AddDays(GraceDays))}.", false, "Retry payment", "balance", payments);
        else if (next is not null)
            yield return new($"balance-{o.Id}", everyone, ctx, "Payment plan",
                $"{Money(money.PaidCents)} paid · next {Money(next.AmountCents)} on {Day(next.DueDate)} · {Money(money.BalanceCents)} remaining", false, "Pay balance", "balance", payments);
        else
            yield return new($"balance-{o.Id}", everyone, ctx, "Balance due",
                $"{Money(money.PaidCents)} paid · {Money(money.BalanceCents)} due by {Day(o.Session.BalanceDueDate)}", false, "Pay balance", "balance", payments);
    }
}

/// <summary>Where a moved camper now goes (F1, F4, F5).</summary>
public sealed record MovedSession(string Name, DateOnly StartDate, DateOnly EndDate);
