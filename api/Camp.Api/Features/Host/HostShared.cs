using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Host;

/// <summary>Who the signed-in host acts for, and small helpers shared by the host endpoints.</summary>
internal static class HostScope
{
    public const string NotLinked =
        "Your account isn't linked to a host church yet. Ask your WinShape contact to add you as a host coordinator.";

    /// <summary>The signed-in host's membership, found by their session email. Null when they have none.</summary>
    public static Task<HostMember?> MemberAsync(CampDbContext db, StaffUser staff, CancellationToken ct) =>
        db.Set<HostMember>().AsNoTracking().Include(m => m.HostOrganization)
            .FirstOrDefaultAsync(m => m.Email == staff.Email, ct);

    /// <summary>The organization's first event (by start date), with its session and program.</summary>
    public static Task<HostEvent?> EventAsync(CampDbContext db, int organizationId, CancellationToken ct) =>
        db.Set<HostEvent>().AsNoTracking()
            .Include(e => e.Session).ThenInclude(s => s.Program)
            .Include(e => e.Session).ThenInclude(s => s.Pools)
            .Where(e => e.HostOrganizationId == organizationId)
            .OrderBy(e => e.Session.StartDate)
            .FirstOrDefaultAsync(ct);

    public static IResult NotLinkedResult() => Forbidden(NotLinked);

    public static IResult Forbidden(string message) => Results.Json(new { error = message }, statusCode: StatusCodes.Status403Forbidden);

    public static IResult Conflict(string message) => Results.Conflict(new { error = message });

    public static IResult Invalid(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });

    public static string Money(int cents) => CheckoutService.Money(cents);

    public static string Day(DateOnly d) => d.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

    /// <summary>Registrations that hold a seat: everything except drafts, waitlist entries and cancellations.</summary>
    public static readonly RegistrationStatus[] Counted =
    [
        RegistrationStatus.ApplicationPending, RegistrationStatus.Approved, RegistrationStatus.PaymentPending,
        RegistrationStatus.Confirmed, RegistrationStatus.OfferedSpot,
    ];

    public static int Total(HostInvoice i) => i.Lines.Sum(l => l.AmountCents);

    public static int Paid(HostInvoice i) => i.Payments.Where(p => p.Status == HostPaymentStatus.Succeeded).Sum(p => p.AmountCents);

    public static int Balance(HostInvoice i) => Math.Max(0, Total(i) - Paid(i));
}
