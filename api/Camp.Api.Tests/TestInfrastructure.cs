using System.Net.Http.Json;
using Camp.Api.Auth;
using Camp.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Camp.Api.Tests;

/// <summary>
/// Boots the real app against a throwaway SQL Server database (migrated + seeded on startup).
/// Set TEST_SQL to point at a server; defaults to the docker-compose instance.
/// Share one per test class with <c>IClassFixture&lt;ApiFactory&gt;</c>.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    static readonly string SqlServer = Environment.GetEnvironmentVariable("TEST_SQL") ?? "localhost,14333";
    public static readonly string ConnectionString =
        $"Server={SqlServer};Database=CampRegistration_Tests_{Guid.NewGuid():N};User Id=sa;Password=Camp_Dev_Passw0rd!;TrustServerCertificate=True";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
    }

    /// <summary>An HTTP client signed in as a guest. The household is matched by email, or created.</summary>
    public Task<HttpClient> SignInAsFamily(string email = Seed.JohnsonEmail, string firstName = "Maria", string lastName = "Johnson") =>
        SignIn(new DevLoginRequest(email, firstName, lastName, null));

    /// <summary>An HTTP client signed in as staff with a WorkOS role: cet, finance, host, or admin.</summary>
    public Task<HttpClient> SignInAsStaff(string role = "cet", string name = "Diane Carter") =>
        SignIn(new DevLoginRequest($"{role}@winshape.example", name.Split(' ')[0], name.Split(' ')[^1], role));

    /// <summary>Runs <paramref name="work"/> against the test database in its own scope.</summary>
    public async Task<T> WithDb<T>(Func<CampDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<CampDbContext>());
    }

    async Task<HttpClient> SignIn(DevLoginRequest req)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var res = await client.PostAsJsonAsync("/api/auth/dev-login", req);
        res.EnsureSuccessStatusCode();
        return client;
    }

    public override async ValueTask DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<CampDbContext>().Database.EnsureDeletedAsync();
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
