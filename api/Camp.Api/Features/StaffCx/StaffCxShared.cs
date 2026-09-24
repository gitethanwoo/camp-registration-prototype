using Camp.Api.Domain;

namespace Camp.Api.Features.StaffCx;

/// <summary>Small helpers shared by the front-desk endpoints.</summary>
internal static class StaffCx
{
    /// <summary>Payment status in the global vocabulary: Paid, Plan active, Installment failed, Balance due.</summary>
    public static string PaymentState(RegistrationStatus status, int balanceCents, int paidCents, IEnumerable<InstallmentStatus> installments)
    {
        if (status == RegistrationStatus.Cancelled) return paidCents > 0 ? "Paid" : "Voided";
        if (balanceCents <= 0) return "Paid";
        var list = installments.ToList();
        if (list.Contains(InstallmentStatus.Failed)) return "Installment failed";
        if (list.Contains(InstallmentStatus.Scheduled)) return "Plan active";
        return "Balance due";
    }

    public static IResult Invalid(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });

    public static IResult Conflict(string message) => Results.Conflict(new { error = message });

    public static IResult Forbidden(string message) => Results.Json(new { error = message }, statusCode: StatusCodes.Status403Forbidden);

    public static string Money(int cents) => CheckoutService.Money(cents);

    /// <summary>"June 12–16, 2028".</summary>
    public static string Dates(DateOnly start, DateOnly end) =>
        start.Month == end.Month
            ? $"{start.ToString("MMMM d", CultureInfo.InvariantCulture)}–{end.Day}, {end.Year}"
            : $"{start.ToString("MMMM d", CultureInfo.InvariantCulture)} – {end.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)}";

}
