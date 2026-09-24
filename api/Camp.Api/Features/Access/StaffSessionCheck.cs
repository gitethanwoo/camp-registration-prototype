using System.Security.Claims;
using Camp.Api.Auth;
using Camp.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Access;

/// <summary>
/// Revoked means access removed: a staff session whose platform row is Revoked (by a WorkOS sync) is
/// rejected on its next request, whatever the cookie's own expiry says.
/// </summary>
public static class StaffSessionCheck
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        var principal = ctx.Principal;
        if (principal?.FindFirstValue(CampClaims.StaffRole) is null) return;
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = principal.FindFirstValue(ClaimTypes.Email);
        var db = ctx.HttpContext.RequestServices.GetRequiredService<CampDbContext>();
        var status = await db.Set<StaffMember>().AsNoTracking()
            .Where(m => (userId != null && m.WorkOsUserId == userId) || (email != null && m.Email == email))
            .OrderByDescending(m => m.WorkOsUserId == userId)
            .Select(m => (StaffStatus?)m.Status)
            .FirstOrDefaultAsync(ctx.HttpContext.RequestAborted);
        if (status != StaffStatus.Revoked) return;
        ctx.RejectPrincipal();
        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
