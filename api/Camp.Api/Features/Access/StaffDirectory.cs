using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Camp.Api.Auth;

namespace Camp.Api.Features.Access;

/// <summary>One active member of the WorkOS staff organization.</summary>
public sealed record DirectoryMember(string UserId, string Email, string FirstName, string LastName, string Role);

/// <summary>The directory could not be read. Nothing was changed.</summary>
public sealed class DirectoryUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Reads the staff organization's memberships from WorkOS (the local emulator stands in for Entra).
/// Read-only: people are added, removed and given roles in the identity provider, never from here.
/// </summary>
public sealed class StaffDirectory(HttpClient http, WorkOsOptions options)
{
    /// <summary>Named client, so tests can swap the handler.</summary>
    public const string ClientName = "workos-directory";

    /// <summary>Every active membership of the staff organization, following pagination.</summary>
    public async Task<List<DirectoryMember>> ActiveMembersAsync(CancellationToken ct)
    {
        var members = new List<DirectoryMember>();
        string? after = null;
        for (var page = 0; page < 50; page++)
        {
            var url = $"{options.ApiBaseUrl}/user_management/organization_memberships?organization_id={Uri.EscapeDataString(options.StaffOrganizationId)}&limit=100"
                + (after is null ? "" : $"&after={Uri.EscapeDataString(after)}");
            var body = await GetAsync<MembershipList>(url, ct);
            if (body is null) throw new DirectoryUnavailableException("WorkOS sent an empty staff list.");

            foreach (var m in body.Data)
            {
                if (!string.Equals(m.Status, "active", StringComparison.OrdinalIgnoreCase)) continue;
                // The emulator embeds the user; WorkOS itself returns only the id, so look it up.
                var user = m.User ?? await GetAsync<MemberUser>($"{options.ApiBaseUrl}/user_management/users/{Uri.EscapeDataString(m.UserId)}", ct)
                    ?? throw new DirectoryUnavailableException($"WorkOS has no user {m.UserId}.");
                members.Add(new DirectoryMember(m.UserId, user.Email, user.FirstName ?? "", user.LastName ?? "", m.Role?.Slug ?? ""));
            }
            after = body.ListMetadata?.After;
            if (string.IsNullOrEmpty(after)) return members;
        }
        throw new DirectoryUnavailableException("WorkOS kept returning more pages of staff than this app reads.");
    }

    async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        try
        {
            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
                throw new DirectoryUnavailableException($"WorkOS answered {(int)res.StatusCode} when listing staff.");
            return await res.Content.ReadFromJsonAsync<T>(ct);
        }
        catch (HttpRequestException e)
        {
            throw new DirectoryUnavailableException("Couldn't reach WorkOS.", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new DirectoryUnavailableException("WorkOS didn't answer in time.", e);
        }
        catch (System.Text.Json.JsonException e)
        {
            throw new DirectoryUnavailableException("WorkOS sent a staff list this app couldn't read.", e);
        }
    }

    sealed record MembershipList(
        [property: JsonPropertyName("data")] List<Membership> Data,
        [property: JsonPropertyName("list_metadata")] ListMetadata? ListMetadata);

    sealed record ListMetadata([property: JsonPropertyName("after")] string? After);

    sealed record Membership(
        [property: JsonPropertyName("user_id")] string UserId,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("role")] RoleRef? Role,
        [property: JsonPropertyName("user")] MemberUser? User);

    sealed record RoleRef([property: JsonPropertyName("slug")] string Slug);

    sealed record MemberUser(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("first_name")] string? FirstName,
        [property: JsonPropertyName("last_name")] string? LastName);
}
