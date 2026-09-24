using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Features.Polish;
using Camp.Api.Features.Setup;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Activities;

public record ActivityInput(string Name, string Category, string Description, string WhatToBring, string ImageUrl,
    int GradeMin, int GradeMax, int DefaultCapacity, int StaffRatio, string Instructor, string Space, bool IsActive);

/// <summary>
/// K8 · Activity catalog (FR-23, 24, 81). Admins create and edit activities; every change is audited
/// with before and after values. A new default capacity also applies to upcoming schedules that still
/// use the old default, but never below the campers already placed.
/// </summary>
public sealed class ActivityCatalogEndpoints : IEndpointModule
{
    /// <summary>Images shipped in web/public/images/activities. No outside links.</summary>
    public static readonly string[] Images = [.. new[] { "archery", "swimming", "climbing", "horseback", "crafts", "canoeing", "zipline" }.Select(n => $"/images/activities/{n}.svg")];

    public void Map(IEndpointRouteBuilder app)
    {
        var setup = app.MapGroup("/api/admin/setup/activities").RequireAuthorization(Policies.Admin);

        setup.MapGet("", async (CampDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var list = await db.Set<Activity>().AsNoTracking().OrderBy(a => a.SortOrder).ToListAsync(ct);
            var today = clock.Today();
            var scheduled = await db.Set<ActivitySlot>().Join(db.Sessions.Where(s => s.EndDate >= today), x => x.SessionId, s => s.Id, (x, s) => x.ActivityId)
                .GroupBy(x => x).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            return Results.Ok(new
            {
                Activities = list.Select(a => View(a, scheduled.GetValueOrDefault(a.Id))),
                Categories = list.Select(a => a.Category).Distinct().Order(),
                Images,
                Periods = ActivityPeriods.All.Select(p => new { Period = p.Number, p.Time }),
            });
        });

        setup.MapPost("", async (ActivityInput input, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var errors = await CheckAsync(db, input, null, ct);
            if (errors.Count > 0) return SetupResults.Invalid(errors);
            var a = new Activity { SortOrder = (await db.Set<Activity>().MaxAsync(x => (int?)x.SortOrder, ct) ?? 0) + 1 };
            Copy(input, a);
            // The audit row needs the new id; one transaction keeps the activity and its audit row together.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.Set<Activity>().Add(a);
            await db.SaveChangesAsync(ct);
            audit.Record(db, "activity.created", "Activity", a.Id, $"Added {a.Name} to the activity catalog ({Grades(a)}, {a.DefaultCapacity} per period).");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(View(a, 0));
        });

        setup.MapPut("/{id:int}", async (int id, ActivityInput input, CampDbContext db, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            var a = await db.Set<Activity>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return Results.NotFound();
            var errors = await CheckAsync(db, input, id, ct);
            if (errors.Count > 0) return SetupResults.Invalid(errors);

            // Upcoming schedules still on the old default follow the new one.
            var today = clock.Today();
            var follow = await db.Set<ActivitySlot>().Where(s => s.ActivityId == id && s.Capacity == a.DefaultCapacity)
                .Join(db.Sessions.Where(s => s.StartDate > today), x => x.SessionId, s => s.Id, (x, s) => x).ToListAsync(ct);
            if (input.DefaultCapacity != a.DefaultCapacity && follow.FirstOrDefault(s => s.Assigned > input.DefaultCapacity) is { } tight)
                return SetupResults.Invalid("defaultCapacity",
                    $"{a.Name} already has {tight.Assigned} campers in Period {tight.Period} of an upcoming session. Capacity can't go below that.");

            // O4 and families only see active activities, so campers placed in one would drop out of the counts.
            if (a.IsActive && !input.IsActive)
            {
                var placed = await db.Set<ActivityAssignment>()
                    .Join(db.Set<ActivitySlot>().Where(s => s.ActivityId == id), x => x.SlotId, s => s.Id, (x, s) => x)
                    .Join(db.Sessions.Where(s => s.EndDate >= today), x => x.SessionId, s => s.Id, (x, s) => x).CountAsync(ct);
                if (placed > 0)
                    return SetupResults.Invalid("isActive",
                        $"{a.Name} has {placed} camper places in current or upcoming sessions. Move them to other activities on the activity schedule before making it inactive.");
            }

            var before = Snapshot(a);
            Copy(input, a);
            var after = Snapshot(a);
            if (input.DefaultCapacity != before.Capacity)
                foreach (var s in follow) s.Capacity = input.DefaultCapacity;
            audit.Record(db, "activity.updated", "Activity", a.Id, $"Edited {a.Name} in the activity catalog.",
                ("Name", before.Name, after.Name), ("Category", before.Category, after.Category), ("Grades", before.Grades, after.Grades),
                ("Default capacity", before.Capacity.ToString(CultureInfo.InvariantCulture), after.Capacity.ToString(CultureInfo.InvariantCulture)),
                ("Staff ratio", before.Ratio, after.Ratio), ("Instructor", before.Instructor, after.Instructor), ("Space", before.Space, after.Space),
                ("Description", before.Description, after.Description), ("What to bring", before.Bring, after.Bring),
                ("Photo", before.Image, after.Image), ("Status", before.Status, after.Status));
            await db.SaveChangesAsync(ct);
            return Results.Ok(View(a, follow.Count));
        });
    }

    sealed record Snap(string Name, string Category, string Grades, int Capacity, string Ratio, string Instructor, string Space, string Description, string Bring, string Image, string Status);

    static Snap Snapshot(Activity a) => new(a.Name, a.Category, Grades(a), a.DefaultCapacity, $"1:{a.StaffRatio}", a.Instructor, a.Space, a.Description, a.WhatToBring, a.ImageUrl, a.IsActive ? "Active" : "Inactive");

    static void Copy(ActivityInput i, Activity a)
    {
        a.Name = i.Name.Trim();
        a.Category = i.Category.Trim();
        a.Description = i.Description.Trim();
        a.WhatToBring = (i.WhatToBring ?? "").Trim();
        a.ImageUrl = i.ImageUrl;
        a.GradeMin = i.GradeMin;
        a.GradeMax = i.GradeMax;
        a.DefaultCapacity = i.DefaultCapacity;
        a.StaffRatio = i.StaffRatio;
        a.Instructor = (i.Instructor ?? "").Trim();
        a.Space = i.Space.Trim();
        a.IsActive = i.IsActive;
    }

    static async Task<Dictionary<string, string[]>> CheckAsync(CampDbContext db, ActivityInput i, int? id, CancellationToken ct)
    {
        var e = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(i.Name) || i.Name.Trim().Length > 60) e["name"] = ["Enter a name of up to 60 characters."];
        else if (await db.Set<Activity>().AnyAsync(a => a.Name == i.Name.Trim() && a.Id != id, ct)) e["name"] = [$"There's already an activity called {i.Name.Trim()}."];
        if (string.IsNullOrWhiteSpace(i.Category) || i.Category.Trim().Length > 40) e["category"] = ["Enter a category of up to 40 characters."];
        if (string.IsNullOrWhiteSpace(i.Description) || i.Description.Trim().Length > 1000) e["description"] = ["Enter a description of up to 1,000 characters. Families read it."];
        if ((i.WhatToBring ?? "").Trim().Length > 300) e["whatToBring"] = ["Keep what to bring under 300 characters."];
        if (!Images.Contains(i.ImageUrl)) e["imageUrl"] = ["Choose one of the photos in the list."];
        if (i.GradeMin is < 1 or > 12 || i.GradeMax is < 1 or > 12 || i.GradeMin > i.GradeMax) e["gradeMin"] = ["Grades go from 1 to 12, lowest first."];
        if (i.DefaultCapacity is < 1 or > 100) e["defaultCapacity"] = ["Capacity per period is between 1 and 100 campers."];
        if (i.StaffRatio is < 1 or > 30) e["staffRatio"] = ["Staff ratio is between 1:1 and 1:30."];
        if ((i.Instructor ?? "").Trim().Length > 80) e["instructor"] = ["Keep the instructor requirement under 80 characters."];
        if (string.IsNullOrWhiteSpace(i.Space) || i.Space.Trim().Length > 60) e["space"] = ["Enter the space it needs, up to 60 characters."];
        return e;
    }

    public static string Grades(Activity a) => a.GradeMin == a.GradeMax ? $"Grade {a.GradeMin}" : $"Grades {a.GradeMin}–{a.GradeMax}";

    static object View(Activity a, int upcomingSlots) => new
    {
        a.Id,
        a.Name,
        a.Category,
        a.Description,
        a.WhatToBring,
        a.ImageUrl,
        a.GradeMin,
        a.GradeMax,
        GradeLabel = Grades(a),
        a.DefaultCapacity,
        a.StaffRatio,
        a.Instructor,
        a.Space,
        a.IsActive,
        UpcomingSlots = upcomingSlots,
    };
}
