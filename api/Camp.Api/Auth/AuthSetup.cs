using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;

namespace Camp.Api.Auth;

/// <summary>Registers cookie sessions, WorkOS, policies, and the per-request identities.</summary>
public static class AuthSetup
{
    /// <summary>Wires authentication for the app.</summary>
    public static IServiceCollection AddCampAuth(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<WorkOsOptions>(config.GetSection("WorkOS"));
        services.AddHttpClient<WorkOsClient>();
        services.AddHttpContextAccessor();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "camp.session";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.ExpireTimeSpan = TimeSpan.FromHours(8);
                o.SlidingExpiration = true;
                // An API: answer with status codes, never redirect to a login page.
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
                // A staff member revoked by a WorkOS sync loses the session on the next request, not when the cookie expires.
                o.Events.OnValidatePrincipal = Features.Access.StaffSessionCheck.ValidateAsync;
            });
        services.AddAuthorization(Policies.Configure);

        services.AddScoped(sp => IdentityFactory.Guest(sp.GetRequiredService<IHttpContextAccessor>().HttpContext!.User));
        services.AddScoped(sp => IdentityFactory.Staff(sp.GetRequiredService<IHttpContextAccessor>().HttpContext!.User));
        services.AddExceptionHandler<NotSignedInHandler>();
        return services;
    }
}

/// <summary>Turns a missing identity into a 401 instead of a 500.</summary>
internal sealed class NotSignedInHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (exception is not NotSignedInException) return false;
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = { Status = 401, Title = exception.Message },
        });
    }
}
