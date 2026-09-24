using System.Reflection;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;

namespace Camp.Api.Infrastructure;

/// <summary>
/// A feature slice's HTTP surface. Implementations are discovered by assembly scan, so adding a
/// slice never edits Program.cs. Put one in <c>Features/&lt;Slice&gt;/&lt;Slice&gt;Endpoints.cs</c>.
/// </summary>
public interface IEndpointModule
{
    /// <summary>Maps this slice's routes.</summary>
    void Map(IEndpointRouteBuilder app);
}

/// <summary>
/// Demo data for a slice, run after the core seed on every startup. Implementations must be
/// idempotent: check for their own rows before inserting.
/// </summary>
public interface ISeedModule
{
    /// <summary>Lower runs first. Core seed is 0; slices use 100+.</summary>
    int Order { get; }

    /// <summary>Inserts this slice's demo rows if they're missing.</summary>
    Task RunAsync(CampDbContext db, CancellationToken ct);
}

/// <summary>Records who did what. Every staff mutation calls this before SaveChanges.</summary>
public interface IAuditLog
{
    /// <summary>Stages an audit row in the current unit of work; the caller's SaveChanges commits it.</summary>
    void Record(string action, string entityType, object entityId, string detail);
}

internal sealed class AuditLog(CampDbContext db, IHttpContextAccessor http) : IAuditLog
{
    public void Record(string action, string entityType, object entityId, string detail)
    {
        var user = http.HttpContext?.User;
        var actor = user?.FindFirst(CampClaims.StaffRole) is not null ? IdentityFactory.Staff(user).Actor
            : user?.Identity?.Name ?? "System";
        db.AuditEvents.Add(new AuditEvent
        {
            Actor = actor,
            Action = action,
            EntityType = entityType,
            EntityId = Convert.ToString(entityId, CultureInfo.InvariantCulture) ?? "",
            Detail = detail,
            CreatedAt = DateTime.UtcNow,
        });
    }
}

/// <summary>Discovery and registration for slice modules.</summary>
public static class ModuleRegistry
{
    static readonly Assembly Assembly = typeof(ModuleRegistry).Assembly;

    /// <summary>Registers every endpoint and seed module in the assembly, plus the audit log.</summary>
    public static IServiceCollection AddCampModules(this IServiceCollection services)
    {
        foreach (var type in Concrete<IEndpointModule>()) services.AddSingleton(typeof(IEndpointModule), type);
        foreach (var type in Concrete<ISeedModule>()) services.AddScoped(typeof(ISeedModule), type);
        services.AddScoped<IAuditLog, AuditLog>();
        return services;
    }

    /// <summary>Maps every registered endpoint module.</summary>
    public static void MapCampModules(this WebApplication app)
    {
        foreach (var module in app.Services.GetServices<IEndpointModule>()) module.Map(app);
    }

    /// <summary>Runs every seed module in order.</summary>
    public static async Task RunSeedModulesAsync(this IServiceProvider scoped, CampDbContext db, CancellationToken ct)
    {
        foreach (var seed in scoped.GetServices<ISeedModule>().OrderBy(s => s.Order)) await seed.RunAsync(db, ct);
    }

    static IEnumerable<Type> Concrete<T>() =>
        Assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(T).IsAssignableFrom(t));
}
