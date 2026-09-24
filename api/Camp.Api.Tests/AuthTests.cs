using System.Net;
using System.Net.Http.Json;
using Camp.Api.Data;
using Camp.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Tests;

public class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Family_endpoints_require_sign_in()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/family")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/programs")).StatusCode);
    }

    [Fact]
    public async Task Admin_endpoints_require_staff()
    {
        using var guest = await factory.SignInAsFamily();
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.GetAsync("/api/admin/scope")).StatusCode);

        using var host = await factory.SignInAsStaff("host", "Grace Patel");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.GetAsync("/api/admin/scope")).StatusCode);

        using var cet = await factory.SignInAsStaff();
        Assert.Equal(HttpStatusCode.OK, (await cet.GetAsync("/api/admin/scope")).StatusCode);
    }

    [Fact]
    public async Task Returning_guest_acts_for_their_own_household()
    {
        using var maria = await factory.SignInAsFamily();
        var me = await maria.GetFromJsonAsync<MeResponse>("/api/me");
        Assert.Equal(Seed.JohnsonEmail, me!.Email);
        Assert.Contains(me.Members, m => m.FirstName == "Avery");
    }

    [Fact]
    public async Task First_sign_in_creates_an_empty_household()
    {
        using var sam = await factory.SignInAsFamily("sam.rivera@example.com", "Sam", "Rivera");
        var me = await sam.GetFromJsonAsync<MeResponse>("/api/me");
        Assert.Equal("Rivera", me!.Name);
        Assert.Single(me.Members);

        // Signing in again finds the same household instead of creating another.
        using var again = await factory.SignInAsFamily("sam.rivera@example.com", "Sam", "Rivera");
        Assert.Equal(1, await factory.WithDb(db => db.Households.CountAsync(h => h.Email == "sam.rivera@example.com")));
    }

    [Fact]
    public async Task Staff_changes_are_audited_under_the_signed_in_name()
    {
        using var cet = await factory.SignInAsStaff();
        var poolId = await factory.WithDb(db => db.CapacityPools.Select(p => p.Id).FirstAsync());
        var capacity = await factory.WithDb(db => db.CapacityPools.Where(p => p.Id == poolId).Select(p => p.Capacity).SingleAsync());

        using var res = await cet.PutAsJsonAsync($"/api/admin/pools/{poolId}", new { Capacity = capacity + 1 });
        res.EnsureSuccessStatusCode();

        var actor = await factory.WithDb(db => db.AuditEvents.OrderByDescending(a => a.Id).Select(a => a.Actor).FirstAsync());
        Assert.Equal("Diane Carter (CET)", actor);
    }

    [Fact]
    public async Task A_co_owner_signs_in_to_the_household_they_share()
    {
        var johnson = await factory.WithDb(db => db.Households.Where(h => h.Email == Seed.JohnsonEmail).Select(h => h.Id).SingleAsync());
        using var david = await factory.SignInAsFamily("david.johnson@example.com", "David", "Johnson");
        var me = await david.GetFromJsonAsync<MeResponse>("/api/me");
        Assert.Equal(johnson, me!.Id);
        Assert.Contains(me.Members, m => m.FirstName == "Avery");
        Assert.False(await factory.WithDb(db => db.Households.AnyAsync(h => h.Email == "david.johnson@example.com")));
    }

    [Fact]
    public async Task A_revoked_adult_signs_in_to_a_new_empty_household()
    {
        var email = $"revoked-{Guid.NewGuid():N}@example.com";
        var shared = await factory.WithDb(async db =>
        {
            var h = new Household { Name = "Shared", Email = $"owner-{email}" };
            h.Members.Add(new Person { FirstName = "Owner", LastName = "Shared", IsAdult = true, Role = "Primary", Email = $"owner-{email}" });
            h.Members.Add(new Person { FirstName = "Ex", LastName = "Shared", IsAdult = true, Role = "Co-owner", Email = email });
            db.Households.Add(h);
            await db.SaveChangesAsync();
            return h;
        });
        using var owner = await factory.SignInAsFamily($"owner-{email}", "Owner", "Shared");
        var exId = shared.Members.Single(m => m.Email == email).Id;
        (await owner.PostAsync($"/api/family/members/{exId}/revoke-access", null)).EnsureSuccessStatusCode();

        using var ex = await factory.SignInAsFamily(email, "Ex", "Shared");
        var me = await ex.GetFromJsonAsync<MeResponse>("/api/me");
        Assert.NotEqual(shared.Id, me!.Id);
        Assert.Single(me.Members);
    }

    [Fact]
    public async Task An_email_that_signs_in_elsewhere_cant_be_given_to_another_households_adult()
    {
        using var sam = await factory.SignInAsFamily($"sam-{Guid.NewGuid():N}@example.com", "Sam", "Rivera");
        using var maria = await sam.PostAsJsonAsync("/api/family/members", new { firstName = "Maria", lastName = "Johnson", isAdult = true, email = Seed.JohnsonEmail });
        Assert.Equal(HttpStatusCode.BadRequest, maria.StatusCode);
        Assert.Contains("another family account", await maria.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var david = await sam.PostAsJsonAsync("/api/family/members", new { firstName = "David", lastName = "Johnson", isAdult = true, email = "David.Johnson@example.com" });
        Assert.Equal(HttpStatusCode.BadRequest, david.StatusCode);
        using var invite = await sam.PostAsJsonAsync("/api/family/invitations", new { firstName = "David", lastName = "Johnson", email = "david.johnson@example.com" });
        Assert.Equal(HttpStatusCode.BadRequest, invite.StatusCode);

        // An adult without account access elsewhere doesn't sign in anywhere, so their email is free.
        using var fresh = await sam.PostAsJsonAsync("/api/family/members", new { firstName = "Rosa", lastName = "Rivera", isAdult = true, email = $"rosa-{Guid.NewGuid():N}@example.com" });
        Assert.Equal(HttpStatusCode.Created, fresh.StatusCode);
    }

    sealed record MeResponse(int Id, string Name, string Email, List<MemberResponse> Members);
    sealed record MemberResponse(string FirstName);
}
