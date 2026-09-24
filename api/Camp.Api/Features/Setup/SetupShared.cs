using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Setup;

/// <summary>Small helpers shared by the setup endpoints.</summary>
internal static class SetupResults
{
    public static IResult Invalid(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });

    public static IResult Invalid(Dictionary<string, string[]> errors) => Results.ValidationProblem(errors);

    public static IResult Conflict(string message) => Results.Conflict(new { error = message });

    public static IResult Forbidden(string message) => Results.Json(new { error = message }, statusCode: StatusCodes.Status403Forbidden);

    public static string Money(int cents) => (cents / 100m).ToString(cents % 100 == 0 ? "C0" : "C2", CultureInfo.GetCultureInfo("en-US"));

    public static string Date(DateOnly d) => d.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    public static string Date(DateTime? d) => d is null ? "Not set" : d.Value.ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}

/// <summary>Records an audit row plus the before and after values K12 shows.</summary>
internal static class SetupAudit
{
    public static void Record(this IAuditLog audit, CampDbContext db, string action, string entityType, object entityId, string detail,
        params (string Field, string? Before, string? After)[] changes)
    {
        audit.Record(action, entityType, entityId, detail);
        var id = Convert.ToString(entityId, CultureInfo.InvariantCulture);
        var entry = db.ChangeTracker.Entries<AuditEvent>()
            .LastOrDefault(e => e.State == EntityState.Added && e.Entity.Action == action && e.Entity.EntityId == id && e.Entity.Detail == detail);
        if (entry is null) return;
        foreach (var (field, before, after) in changes)
        {
            if (before == after) continue;
            db.Set<AuditChange>().Add(new AuditChange { AuditEvent = entry.Entity, Field = field, Before = Clip(before), After = Clip(after) });
        }
    }

    static string? Clip(string? s) => s is { Length: > 1000 } ? s[..997] + "..." : s;
}
