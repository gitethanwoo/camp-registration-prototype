using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Camp.Api.Auth;

/// <summary>Claim types in the app's session cookie.</summary>
public static class CampClaims
{
    /// <summary>Household the signed-in guest acts for. Absent for staff.</summary>
    public const string HouseholdId = "camp:household_id";

    /// <summary>Staff role slug from WorkOS (cet, finance, host, admin). Absent for guests.</summary>
    public const string StaffRole = "camp:staff_role";
}

/// <summary>Authorization policies. Endpoints use these names, never raw role checks.</summary>
public static class Policies
{
    /// <summary>A signed-in guest with a household.</summary>
    public const string Family = "family";

    /// <summary>Any WinShape staff member who works in the admin console.</summary>
    public const string Staff = "staff";

    /// <summary>Customer Experience Team: registrations, families, discounts, transfers.</summary>
    public const string Cet = "cet";

    /// <summary>Finance: refunds over threshold, reconciliation, exports.</summary>
    public const string Finance = "finance";

    /// <summary>Host coordinators for partner-run events.</summary>
    public const string Host = "host";

    internal static void Configure(AuthorizationOptions o)
    {
        o.AddPolicy(Family, p => p.RequireClaim(CampClaims.HouseholdId));
        o.AddPolicy(Staff, p => p.RequireClaim(CampClaims.StaffRole, "cet", "finance", "admin"));
        o.AddPolicy(Cet, p => p.RequireClaim(CampClaims.StaffRole, "cet", "admin"));
        o.AddPolicy(Finance, p => p.RequireClaim(CampClaims.StaffRole, "finance", "admin"));
        o.AddPolicy(Host, p => p.RequireClaim(CampClaims.StaffRole, "host", "admin"));
    }
}

/// <summary>The signed-in guest. Only resolvable on endpoints that require <see cref="Policies.Family"/>.</summary>
public sealed record CurrentUser(int HouseholdId, string Name, string Email);

/// <summary>The signed-in staff member. Only resolvable on endpoints that require a staff policy.</summary>
public sealed record StaffUser(string UserId, string Name, string Email, string Role)
{
    /// <summary>How this person appears in audit history, e.g. "Diane Carter (CET)".</summary>
    public string Actor => $"{Name} ({Role.ToUpperInvariant()})";
}

/// <summary>Thrown when an endpoint resolves an identity the request doesn't carry. Mapped to 401.</summary>
public sealed class NotSignedInException() : Exception("Sign in to continue.");

internal static class IdentityFactory
{
    public static CurrentUser Guest(ClaimsPrincipal p) =>
        int.TryParse(p.FindFirstValue(CampClaims.HouseholdId), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? new CurrentUser(id, p.FindFirstValue(ClaimTypes.Name) ?? "", p.FindFirstValue(ClaimTypes.Email) ?? "")
            : throw new NotSignedInException();

    public static StaffUser Staff(ClaimsPrincipal p) =>
        p.FindFirstValue(CampClaims.StaffRole) is { } role
            ? new StaffUser(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", p.FindFirstValue(ClaimTypes.Name) ?? "", p.FindFirstValue(ClaimTypes.Email) ?? "", role)
            : throw new NotSignedInException();
}
