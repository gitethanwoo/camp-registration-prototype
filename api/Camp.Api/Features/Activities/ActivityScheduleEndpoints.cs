using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Features.Ops;
using Camp.Api.Features.Polish;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Activities;

public record AssignRequest(int BlockId);
public record MoveRequest(int RegistrationId, int FromSlotId, int ToSlotId);

/// <summary>
/// O4 · Activity schedule builder (FR-81, 85). One grid per age block (6 activities × 3 periods).
/// Counts come from the confirmed roster, so every period ties out: placed + chosen but not placed +
/// not chosen = campers in the block. Staff read; CET and admins change, and every change is audited.
/// Every change takes the session's activity lock first (see <see cref="ActivityRules.LockSessionAsync"/>).
/// </summary>
public sealed class ActivityScheduleEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        var ops = app.MapGroup("/api/admin/ops/sessions/{id:int}/activities");

        ops.MapGet("", async (int id, int? blockId, CampDbContext db, CancellationToken ct) =>
        {
            var grid = await ScheduleGrid.LoadAsync(db, id, blockId, ct);
            return grid is null ? Results.NotFound() : Results.Ok(grid.View());
        }).RequireAuthorization(Policies.Staff);

        // Dry run: what "Assign from preferences" would do. Nothing is saved.
        ops.MapPost("/assign/preview", async (int id, AssignRequest req, CampDbContext db, CancellationToken ct) =>
        {
            var grid = await ScheduleGrid.LoadAsync(db, id, req.BlockId, ct);
            if (grid is null || grid.Block.Id != req.BlockId) return Results.NotFound();
            var plan = grid.PlanFromPreferences();
            return Results.Ok(new { Block = grid.Block.Name, Campers = plan.Select(p => p.RegistrationId).Distinct().Count(), Places = plan.Count(p => p.Slot is not null), NoRoom = plan.Count(p => p.Slot is null) });
        }).RequireAuthorization(Policies.Cet);

        ops.MapPost("/assign", async (int id, AssignRequest req, CampDbContext db, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
            await ActivityRules.RetryWhenBusyAsync(db, async () =>
            {
                // Plan under the session lock: a second "Assign" (or a family saving) waits, then plans from what this one saved.
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await ActivityRules.LockSessionAsync(db, id, ct);
                var grid = await ScheduleGrid.LoadAsync(db, id, req.BlockId, ct);
                if (grid is null || grid.Block.Id != req.BlockId) return Results.NotFound();
                var plan = grid.PlanFromPreferences();
                if (plan.Count == 0) return OpsResults.Conflict($"Everyone in {grid.Block.Name} who chose activities already has a place.");

                var placed = 0;
                var noRoom = 0;
                foreach (var choice in plan)
                {
                    SlotView? got = null;
                    foreach (var slot in choice.Ranked)
                        if (await ActivityRules.ClaimAsync(db, slot.SlotId, ct)) { got = slot; break; }
                    if (got is null) { noRoom++; continue; }
                    db.Set<ActivityAssignment>().Add(new ActivityAssignment { RegistrationId = choice.RegistrationId, SessionId = id, SlotId = got.SlotId, Period = choice.Period, Source = "Preferences", AssignedAt = clock.UtcNow() });
                    placed++;
                }
                var campers = plan.Select(p => p.RegistrationId).Distinct().Count();
                audit.Record("activities.assigned_from_preferences", "Session", id,
                    $"Assigned {OpsResults.Plural(placed, "place", "places")} from preferences for {OpsResults.Plural(campers, "camper", "campers")} in {grid.Block.Name}"
                    + (noRoom > 0 ? $"; {noRoom} period(s) had no room in any choice." : "."));
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Results.Ok(new { Block = grid.Block.Name, Campers = campers, Places = placed, NoRoom = noRoom });
            })).RequireAuthorization(Policies.Cet);

        // Move a camper to another activity in the same period and block, if it has room and fits their grade.
        ops.MapPost("/move", async (int id, MoveRequest req, CampDbContext db, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
            await ActivityRules.RetryWhenBusyAsync(db, async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await ActivityRules.LockSessionAsync(db, id, ct);
                var from = await db.Set<ActivityAssignment>().AsNoTracking().FirstOrDefaultAsync(a => a.SessionId == id && a.RegistrationId == req.RegistrationId && a.SlotId == req.FromSlotId, ct);
                if (from is null) return OpsResults.Invalid("fromSlotId", "That camper isn't in that activity. Refresh and try again.");
                var slots = await db.Set<ActivitySlot>().AsNoTracking().Where(s => s.Id == req.FromSlotId || s.Id == req.ToSlotId).ToListAsync(ct);
                var source = slots.Single(s => s.Id == req.FromSlotId);
                var target = slots.FirstOrDefault(s => s.Id == req.ToSlotId);
                if (target is null || target.SessionId != id) return OpsResults.Invalid("toSlotId", "Choose an activity in this session.");
                if (target.Id == source.Id) return OpsResults.Invalid("toSlotId", "The camper is already in that activity.");
                if (target.BlockId != source.BlockId || target.Period != source.Period)
                    return OpsResults.Invalid("toSlotId", "Move campers within the same block and period.");
                var reg = await db.Registrations.Include(r => r.Person).AsNoTracking().SingleAsync(r => r.Id == req.RegistrationId, ct);
                var activities = await db.Set<Activity>().AsNoTracking().Where(a => a.Id == source.ActivityId || a.Id == target.ActivityId).ToDictionaryAsync(a => a.Id, ct);
                var to = activities[target.ActivityId];
                if (reg.Grade < to.GradeMin || reg.Grade > to.GradeMax)
                    return OpsResults.Invalid("toSlotId", $"{to.Name} is for {ActivityCatalogEndpoints.Grades(to).ToLowerInvariant()}; {reg.Person.FirstName} is in grade {reg.Grade}.");

                if (!await ActivityRules.ClaimAsync(db, target.Id, ct))
                    return OpsResults.Conflict($"{to.Name} is full in Period {target.Period}. Move someone out first or choose another activity.");
                // Only the place we read moves; if it changed underneath us, nothing is counted twice.
                var now = clock.UtcNow();
                var moved = await db.Set<ActivityAssignment>().Where(a => a.Id == from.Id && a.SlotId == source.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.SlotId, target.Id).SetProperty(a => a.Source, "Staff").SetProperty(a => a.AssignedAt, now), ct);
                if (moved != 1) return OpsResults.Conflict($"{reg.Person.FirstName} was just moved by someone else. Refresh and try again.");
                await ActivityRules.ReleaseSeatAsync(db, source.Id, ct);
                audit.Record("activities.camper_moved", "Registration", reg.Id,
                    $"Moved {reg.Person.FullName} from {activities[source.ActivityId].Name} to {to.Name} in Period {target.Period}.");
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Results.Ok(new { Moved = true });
            })).RequireAuthorization(Policies.Cet);
    }
}
