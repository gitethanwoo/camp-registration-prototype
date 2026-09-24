using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Camp.Api.Auth;

/// <summary>WorkOS AuthKit settings. Locally these point at the WorkOS emulator (infra/workos).</summary>
public sealed class WorkOsOptions
{
    /// <summary>Server-to-server base URL (inside docker: http://workos:4100).</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:4100";

    /// <summary>Browser-facing base URL for the hosted sign-in page.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:4100";

    /// <summary>API key; sent as client_secret when redeeming a code.</summary>
    public string ApiKey { get; set; } = "sk_test_default";

    /// <summary>AuthKit client id.</summary>
    public string ClientId { get; set; } = "client_camp";

    /// <summary>Where AuthKit sends the browser back with a code. Proxied to this API by Vite/nginx.</summary>
    public string RedirectUri { get; set; } = "http://localhost:5173/api/auth/callback";

    /// <summary>Organization whose members are WinShape staff.</summary>
    public string StaffOrganizationId { get; set; } = "org_winshape_staff";
}

/// <summary>The parts of an AuthKit sign-in this app uses.</summary>
public sealed record WorkOsIdentity(string UserId, string Email, string FirstName, string LastName, string? OrganizationId, string? Role);

/// <summary>Thin client for the two AuthKit calls we need. No SDK: the surface is two HTTP requests.</summary>
public sealed class WorkOsClient(HttpClient http, Microsoft.Extensions.Options.IOptions<WorkOsOptions> options)
{
    readonly WorkOsOptions _o = options.Value;

    /// <summary>The hosted sign-in page URL the browser is redirected to.</summary>
    public Uri AuthorizationUrl(string state) =>
        new($"{_o.PublicBaseUrl}/user_management/authorize?client_id={Uri.EscapeDataString(_o.ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(_o.RedirectUri)}&response_type=code&provider=authkit&state={Uri.EscapeDataString(state)}");

    /// <summary>Redeems a one-time code for the signed-in user, their organization, and their role.</summary>
    public async Task<WorkOsIdentity> AuthenticateAsync(string code, CancellationToken ct)
    {
        using var res = await http.PostAsJsonAsync(new Uri($"{_o.ApiBaseUrl}/user_management/authenticate"), new
        {
            client_id = _o.ClientId,
            client_secret = _o.ApiKey,
            grant_type = "authorization_code",
            code,
        }, ct);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<AuthenticateResponse>(ct)
            ?? throw new InvalidOperationException("WorkOS returned an empty authentication response.");
        // The token came straight from WorkOS over the back channel, so its claims are trusted without
        // a signature check. We only read the role; the app issues its own session cookie.
        var role = ReadClaim(body.AccessToken, "role");
        return new WorkOsIdentity(body.User.Id, body.User.Email, body.User.FirstName ?? "", body.User.LastName ?? "", body.OrganizationId, role);
    }

    static string? ReadClaim(string jwt, string claim)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) return null;
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        return doc.RootElement.TryGetProperty(claim, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    sealed record AuthenticateResponse(
        [property: JsonPropertyName("user")] WorkOsUser User,
        [property: JsonPropertyName("organization_id")] string? OrganizationId,
        [property: JsonPropertyName("access_token")] string AccessToken);

    sealed record WorkOsUser(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("first_name")] string? FirstName,
        [property: JsonPropertyName("last_name")] string? LastName);
}
