using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features;
using Camp.Api.Integrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Camp.Api.Tests;

/// <summary>
/// Boots the real app against a throwaway SQL Server database (migrated + seeded on startup).
/// Set TEST_SQL to point at a server; defaults to the docker-compose instance.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    static readonly string Server = Environment.GetEnvironmentVariable("TEST_SQL") ?? "localhost,14333";
    public static readonly string ConnectionString =
        $"Server={Server};Database=CampRegistration_Tests_{Guid.NewGuid():N};User Id=sa;Password=Camp_Dev_Passw0rd!;TrustServerCertificate=True";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
    }

    public override async ValueTask DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<CampDbContext>().Database.EnsureDeletedAsync();
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

public class RegistrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    readonly FakeFiservGateway _gateway = (FakeFiservGateway)factory.Services.GetRequiredService<IPaymentGateway>();

    [Fact]
    public async Task Two_families_racing_for_the_last_seat_one_confirms_and_one_is_waitlisted()
    {
        var (sessionId, poolId) = await IsolatedDayCampPool(capacity: 1);
        var a = await NewFamilyWithGrade6Child("Racer-A");
        var b = await NewFamilyWithGrade6Child("Racer-B");

        var results = await Task.WhenAll(
            Checkout(a, sessionId, "race-a", Card("4242424242424242")),
            Checkout(b, sessionId, "race-b", Card("4242424242424242")));

        Assert.Single(results, r => r.Status == OrderStatus.Paid);
        Assert.Single(results, r => r.Status == OrderStatus.Waitlisted);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampDbContext>();
        var pool = await db.CapacityPools.AsNoTracking().SingleAsync(p => p.Id == poolId);
        Assert.Equal(1, pool.Reserved); // never overbooked
        Assert.Equal(1, await db.WaitlistEntries.CountAsync(w => w.PoolId == poolId && w.Status == WaitlistStatus.Waiting));
    }

    [Fact]
    public async Task Double_submit_with_the_same_idempotency_key_charges_once()
    {
        var (sessionId, _) = await IsolatedDayCampPool(capacity: 5);
        var family = await NewFamilyWithGrade6Child("Double");
        var token = Card("4242424242424242");
        var before = _gateway.ChargeCount;

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Checkout(family, sessionId, "same-key", token)));

        Assert.Single(results.Select(r => r.ConfirmationCode).Distinct());
        Assert.Equal(before + 1, _gateway.ChargeCount);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampDbContext>();
        Assert.Equal(1, await db.Orders.CountAsync(o => o.IdempotencyKey == "same-key"));
    }

    [Fact]
    public async Task Declined_card_releases_the_seat()
    {
        var (sessionId, poolId) = await IsolatedDayCampPool(capacity: 1);
        var family = await NewFamilyWithGrade6Child("Declined");

        var result = await Checkout(family, sessionId, "decline-1", Card("4000000000000002"));

        Assert.Equal(OrderStatus.Declined, result.Status);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampDbContext>();
        Assert.Equal(0, (await db.CapacityPools.AsNoTracking().SingleAsync(p => p.Id == poolId)).Reserved);
        Assert.All(await db.Registrations.Where(r => r.Order!.IdempotencyKey == "decline-1").ToListAsync(),
            r => Assert.Equal(RegistrationStatus.Cancelled, r.Status));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    string Card(string number) => _gateway.Tokenize(number);

    /// <summary>A fresh Day Camp copy with only a Grade 6 pool, so tests don't share seats.</summary>
    async Task<(int SessionId, int PoolId)> IsolatedDayCampPool(int capacity)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampDbContext>();
        var template = await db.Sessions.AsNoTracking().OrderBy(s => s.Id).FirstAsync(s => s.Program.Slug == "day-camp-atlanta");
        var session = new Session
        {
            ProgramId = template.ProgramId, Name = $"Test {Guid.NewGuid():N}"[..20], StartDate = template.StartDate, EndDate = template.EndDate,
            PriceCents = template.PriceCents, DepositCents = template.DepositCents, PlanInstallments = template.PlanInstallments, BalanceDueDate = template.BalanceDueDate,
        };
        var pool = new CapacityPool { Session = session, Name = "Grade 6", GradeMin = 6, GradeMax = 6, Capacity = capacity };
        db.CapacityPools.Add(pool);
        await db.SaveChangesAsync();
        return (session.Id, pool.Id);
    }

    async Task<int> NewFamilyWithGrade6Child(string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampDbContext>();
        var h = new Household { Name = name, Email = $"{name.ToLower()}-{Guid.NewGuid():N}@example.com" };
        h.Members.Add(new Person { FirstName = "Parent", LastName = name, IsAdult = true, DateOfBirth = new(1985, 1, 1) });
        h.Members.Add(new Person { FirstName = "Kid", LastName = name, DateOfBirth = new(2017, 3, 4), Gender = Gender.Male });
        db.Households.Add(h);
        await db.SaveChangesAsync();
        return h.Id;
    }

    async Task<CheckoutResult> Checkout(int householdId, int sessionId, string key, string token)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampDbContext>();
        var kid = await db.People.SingleAsync(p => p.HouseholdId == householdId && !p.IsAdult);
        var programId = await db.Sessions.Where(s => s.Id == sessionId).Select(s => s.ProgramId).SingleAsync();
        var waivers = await db.WaiverTemplates.Where(w => w.ProgramId == programId).ToListAsync();
        var req = new CheckoutRequest(
            key, sessionId,
            [new CheckoutParticipant(kid.Id, new() { ["tshirt"] = "Youth M", ["swim"] = "Beginner" }, new HealthForm(null, null, null, null, "Dr. Test", "555-0100", null))],
            new() { ["church"] = "No" },
            waivers.Select(w => new WaiverSignature(w.Id, w.PerParticipant ? kid.Id : null, "Parent")).ToList(),
            PaymentOption.Deposit, null, token);
        return await scope.ServiceProvider.GetRequiredService<CheckoutService>().CheckoutAsync(householdId, "test", req, default);
    }
}
