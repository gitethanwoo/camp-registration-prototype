using System.Text.Json.Serialization;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Features;
using Camp.Api.Infrastructure;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// No EF retrying strategy: checkout uses explicit transactions around conditional seat updates.
builder.Services.AddDbContext<CampDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();

// The one clock. Inject TimeProvider instead of reading DateTime.UtcNow (wave 3 adds a demo clock here).
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPaymentGateway, FakeFiservGateway>();
builder.Services.AddScoped<CheckoutService>();
builder.Services.AddCampAuth(builder.Configuration);
builder.Services.AddCampModules();
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<OutboxDispatcher>();
    builder.Services.AddHostedService<PendingPaymentReconciler>();
    builder.Services.AddHostedService<WaitlistOfferExpiry>();
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CampDbContext>();
    for (var attempt = 1; ; attempt++)
    {
        try { await db.Database.MigrateAsync(); break; }
        catch (Exception) when (attempt < 20) { await Task.Delay(3000); } // SQL Server still starting
    }
    await Seed.RunAsync(db);
    await scope.ServiceProvider.RunSeedModulesAsync(db, CancellationToken.None);
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/api/health", () => Results.Ok(new { ok = true }));
app.MapGuestEndpoints();
app.MapAdminEndpoints();
app.MapCampModules();

app.Run();

public partial class Program;
