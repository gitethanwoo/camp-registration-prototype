using System.Text;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Setup;

/// <summary>
/// K12 · Audit log (FR-76). Read-only for any staff member. Rows can't be edited or deleted: the
/// Setup migration adds a trigger that refuses UPDATE and DELETE on AuditEvents.
/// </summary>
public sealed class AuditLogEndpoints : IEndpointModule
{
    public const int MaxExportRows = 10_000;

    public void Map(IEndpointRouteBuilder app)
    {
        var audit = app.MapGroup("/api/admin/audit-log").RequireAuthorization(Policies.Staff);

        audit.MapGet("", async (string? category, string? actor, DateOnly? from, DateOnly? to, string? q, int? page, int? pageSize,
            CampDbContext db, CancellationToken ct) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 5, 100);
            var query = Filter(db, category, actor, from, to, q);
            var total = await query.CountAsync(ct);
            var pages = Math.Max(1, (int)Math.Ceiling(total / (double)size));
            var current = Math.Clamp(page ?? 1, 1, pages);
            var rows = await query.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
                .Skip((current - 1) * size).Take(size)
                .Select(e => new
                {
                    e.Id,
                    e.CreatedAt,
                    e.Actor,
                    e.Action,
                    e.EntityType,
                    e.EntityId,
                    e.Detail,
                    Changes = db.Set<AuditChange>().Count(c => c.AuditEventId == e.Id),
                }).ToListAsync(ct);
            var actions = await db.AuditEvents.Select(e => e.Action).Distinct().ToListAsync(ct);
            var actors = await db.AuditEvents.Select(e => e.Actor).Distinct().OrderBy(a => a).ToListAsync(ct);
            return Results.Ok(new
            {
                Rows = rows.Select(r => new { r.Id, r.CreatedAt, r.Actor, r.Action, Category = CategoryOf(r.Action), What = ActionLabel(r.Action), r.EntityType, r.EntityId, r.Detail, r.Changes }),
                Total = total,
                Page = current,
                Pages = pages,
                PageSize = size,
                Categories = actions.Select(CategoryOf).Distinct().Order().Select(c => new { Value = c, Label = CategoryLabel(c) }),
                Actors = actors,
            });
        });

        audit.MapGet("/{id:long}", async (long id, CampDbContext db, CancellationToken ct) =>
        {
            var e = await db.AuditEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return Results.NotFound();
            var changes = await db.Set<AuditChange>().AsNoTracking().Where(c => c.AuditEventId == id).OrderBy(c => c.Id)
                .Select(c => new { c.Field, c.Before, c.After }).ToListAsync(ct);
            return Results.Ok(new { e.Id, e.CreatedAt, e.Actor, e.Action, Category = CategoryOf(e.Action), What = ActionLabel(e.Action), e.EntityType, e.EntityId, e.Detail, Changes = changes });
        });

        audit.MapGet("/export", async (string? category, string? actor, DateOnly? from, DateOnly? to, string? q, CampDbContext db, IAuditLog log, TimeProvider clock, CancellationToken ct) =>
        {
            var rows = await Filter(db, category, actor, from, to, q).OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
                .Take(MaxExportRows).AsNoTracking().ToListAsync(ct);
            var csv = new StringBuilder("When (UTC),Who,Action,Record,Record ID,Detail\r\n");
            foreach (var e in rows)
                csv.AppendJoin(',', Cell(e.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)), Cell(e.Actor), Cell(e.Action), Cell(e.EntityType), Cell(e.EntityId), Cell(e.Detail))
                    .Append("\r\n");
            log.Record("audit.exported", "AuditLog", "export", $"Exported {rows.Count} audit rows.");
            await db.SaveChangesAsync(ct);
            return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"audit-log-{clock.Today():yyyy-MM-dd}.csv");
        });
    }

    static IQueryable<AuditEvent> Filter(CampDbContext db, string? category, string? actor, DateOnly? from, DateOnly? to, string? q)
    {
        var query = db.AuditEvents.AsQueryable();
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(e => e.Action.StartsWith(category + "."));
        if (!string.IsNullOrWhiteSpace(actor)) query = query.Where(e => e.Actor == actor);
        if (from is { } f) { var start = f.ToDateTime(TimeOnly.MinValue); query = query.Where(e => e.CreatedAt >= start); }
        if (to is { } t) { var end = t.AddDays(1).ToDateTime(TimeOnly.MinValue); query = query.Where(e => e.CreatedAt < end); }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(e => e.Detail.Contains(term) || e.Actor.Contains(term) || e.EntityId == term || e.Action.Contains(term));
        }
        return query;
    }

    static string CategoryOf(string action) => action.Split('.')[0];

    static string CategoryLabel(string c) => c switch
    {
        "program" => "Programs",
        "session" => "Sessions",
        "capacity" => "Capacity",
        "pricing" => "Pricing and policies",
        "discount" => "Discounts",
        "waiver" => "Waivers",
        "audit" => "Audit exports",
        _ => c.Length == 0 ? c : char.ToUpperInvariant(c[0]) + c[1..].Replace('_', ' '),
    };

    /// <summary>"waiver.published" reads "Waiver published"; "discount.rule_created" reads "Discount rule created".</summary>
    static string ActionLabel(string action)
    {
        var text = action.Replace('.', ' ').Replace('_', ' ').Trim();
        return text.Length == 0 ? action : char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>A CSV cell a spreadsheet won't run as a formula.</summary>
    public static string Cell(string? value)
    {
        var v = value ?? "";
        if (v.Length > 0 && "=+-@\t\r".Contains(v[0])) v = "'" + v;
        return v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}
