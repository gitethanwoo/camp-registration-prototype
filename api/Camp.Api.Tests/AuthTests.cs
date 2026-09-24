using System.Net;
using System.Net.Http.Json;
using Camp.Api.Data;
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

    sealed record MeResponse(string Name, string Email, List<MemberResponse> Members);
    sealed record MemberResponse(string FirstName);
}
