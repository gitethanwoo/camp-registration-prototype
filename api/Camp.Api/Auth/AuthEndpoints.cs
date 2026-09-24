using System.Security.Claims;
using System.Security.Cryptography;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Access;
using Camp.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Camp.Api.Auth;

/// <summary>Request body for the test-only sign-in.</summary>
public sealed record DevLoginRequest(string Email, string FirstName, string LastName, string? StaffRole);

/// <summary>Sign-in through WorkOS AuthKit, then an app-issued cookie session.</summary>
public sealed class AuthEndpoints : IEndpointModule
{
    const string StateCookie = "camp.auth_state";

    /// <summary>A duplicate merge renames the archived household's email to "merged-into-{id}:…".</summary>
    const string ArchivedPrefix = "merged-into-";

    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");

        // Starts sign-in: remember where to come back to, then hand the browser to AuthKit.
        auth.MapGet("/login", (HttpContext http, WorkOsClient workos, string? returnTo) =>
        {
            var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            http.Response.Cookies.Append(StateCookie, $"{state}|{SafeReturnTo(returnTo)}", new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromMinutes(10),
            });
            return Results.Redirect(workos.AuthorizationUrl(state).ToString());
        });

        auth.MapGet("/callback", async (HttpContext http, WorkOsClient workos, CampDbContext db, IOptions<WorkOsOptions> options, TimeProvider time, IAuditLog audit,
            string code, string state, CancellationToken ct) =>
        {
            var saved = http.Request.Cookies[StateCookie]?.Split('|', 2);
            http.Response.Cookies.Delete(StateCookie);
            if (saved is not [var expected, var returnTo] || !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(state)))
                return Results.Problem("That sign-in link expired. Start again from the sign-in button.", statusCode: 400);

            var id = await workos.AuthenticateAsync(code, ct);
            var staffRole = id.OrganizationId == options.Value.StaffOrganizationId ? id.Role : null;
            await SignInAsync(http, db, time, audit, id, staffRole, ct);
            // Host coordinators have their own portal; other staff land in the console.
            var landing = staffRole == "host" ? "/host" : "/admin";
            return Results.Redirect(returnTo == "/" && staffRole is not null ? landing : returnTo);
        });

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        auth.MapGet("/me", (ClaimsPrincipal user) =>
        {
            if (user.Identity?.IsAuthenticated != true) return Results.Ok(new { SignedIn = false });
            var role = user.FindFirstValue(CampClaims.StaffRole);
            return Results.Ok(new
            {
                SignedIn = true,
                Name = user.FindFirstValue(ClaimTypes.Name),
                Email = user.FindFirstValue(ClaimTypes.Email),
                Kind = role is null ? "family" : "staff",
                Role = role,
                // Staff only: display hints. Health reads check the database, not these claims.
                MinistryId = user.FindFirstValue(AccessClaims.MinistryId),
                HealthAccess = role is null ? (bool?)null : user.FindFirstValue(AccessClaims.HealthAccess) == "true",
            });
        });

        // Tests sign in without a browser. Never mapped outside the Testing environment.
        if (app.ServiceProvider.GetRequiredService<IHostEnvironment>().IsEnvironment("Testing"))
        {
            auth.MapPost("/dev-login", async (HttpContext http, CampDbContext db, TimeProvider time, IAuditLog audit, DevLoginRequest req, CancellationToken ct) =>
            {
                var id = new WorkOsIdentity($"user_test_{req.Email}", req.Email, req.FirstName, req.LastName, null, req.StaffRole);
                await SignInAsync(http, db, time, audit, id, req.StaffRole, ct);
                return Results.NoContent();
            });
        }
    }

    static async Task SignInAsync(HttpContext http, CampDbContext db, TimeProvider time, IAuditLog audit, WorkOsIdentity id, string? staffRole, CancellationToken ct)
    {
        var name = $"{id.FirstName} {id.LastName}".Trim();
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, id.UserId),
            new(ClaimTypes.Name, name.Length > 0 ? name : id.Email),
            new(ClaimTypes.Email, id.Email),
        ];
        if (staffRole is not null)
        {
            claims.Add(new Claim(CampClaims.StaffRole, staffRole));
            // Last sign-in, and the platform's own access attributes (K11), on the staff roster.
            var member = await StaffSync.RecordSignInAsync(db, audit, id.UserId, id.Email, id.FirstName, id.LastName, staffRole, time.GetUtcNow().UtcDateTime, ct);
            if (member.MinistryId is { } ministry) claims.Add(new Claim(AccessClaims.MinistryId, ministry.ToString(CultureInfo.InvariantCulture)));
            claims.Add(new Claim(AccessClaims.HealthAccess, member.HealthAccess ? "true" : "false"));
        }
        else
            claims.Add(new Claim(CampClaims.HouseholdId, (await HouseholdFor(db, id, ct)).ToString(CultureInfo.InvariantCulture)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    }

    // A guest's household is found through an adult member who has account access (the primary
    // owner or a co-owner; revoking access clears the role, so a revoked adult no longer matches),
    // then through the household's own email. Households archived by a duplicate merge are skipped.
    // First sign-in with neither creates an empty household with the guest as its primary adult.
    static async Task<int> HouseholdFor(CampDbContext db, WorkOsIdentity id, CancellationToken ct)
    {
        var member = await db.People
            .Where(p => p.IsAdult && p.Role != null && p.Email == id.Email && !p.Household.Email.StartsWith(ArchivedPrefix))
            .OrderBy(p => p.Role == "Primary" ? 0 : 1).ThenBy(p => p.Id)
            .Select(p => (int?)p.HouseholdId).FirstOrDefaultAsync(ct);
        if (member is { } memberHousehold) return memberHousehold;

        var existing = await db.Households.Where(h => h.Email == id.Email).Select(h => (int?)h.Id).FirstOrDefaultAsync(ct);
        if (existing is { } householdId) return householdId;

        var household = new Household { Name = id.LastName.Length > 0 ? id.LastName : id.Email, Email = id.Email };
        household.Members.Add(new Person { FirstName = id.FirstName, LastName = id.LastName, IsAdult = true, Role = "Primary", Email = id.Email });
        db.Households.Add(household);
        await db.SaveChangesAsync(ct);
        return household.Id;
    }

    static string SafeReturnTo(string? returnTo) =>
        returnTo is { Length: > 0 } r && r.StartsWith('/') && !r.StartsWith("//", StringComparison.Ordinal) && !r.Contains('\\', StringComparison.Ordinal) ? r : "/";
}
