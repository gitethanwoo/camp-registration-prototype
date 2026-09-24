using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Ops;

public record CheckInRequest(string? OverrideReason);
public record CheckOutRequest(int PickupAdultId, bool IdChecked);

/// <summary>
/// O5 · Check-in and check-out (FR-84). A camper with an open readiness item is blocked: check-in needs
/// a written override, which is audited. Check-out releases a camper only to an adult the household
/// authorized, after staff confirm the adult's ID.
/// </summary>
public sealed class CheckInEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        var ops = app.MapGroup("/api/admin/ops");

        ops.MapGet("/sessions/{id:int}/check-in", async (int id, CampDbContext db, CancellationToken ct) =>
        {
            var roster = await OpsReadModel.LoadAsync(db, id, ct);
            if (roster is null) return Results.NotFound();
            var s = roster.Session;
            var households = roster.Campers.Select(c => c.HouseholdId).Distinct().ToList();
            var adults = await db.Set<OpsPickupAdult>().AsNoTracking().Where(a => households.Contains(a.HouseholdId)).OrderBy(a => a.Id).ToListAsync(ct);
            var phones = await db.Households.Where(h => households.Contains(h.Id)).ToDictionaryAsync(h => h.Id, h => h.Phone, ct);
            var names = await OpsNames.For(db, id, ct);
            var rows = roster.Campers.Select(c => new
            {
                c.RegistrationId,
                c.Name,
                c.Grade,
                Gender = c.Gender.ToString(),
                c.ConfirmationCode,
                Cabin = names.Cabin(c.Placement?.CabinId),
                c.Placement?.Activity,
                Status = Status(c),
                Blockers = c.Reasons.Select(r =>
                {
                    var (label, detail) = OpsReadModel.Describe(r, c, roster.UsesCampDoc);
                    return new { Kind = r.ToString(), Label = label, Detail = detail };
                }),
                c.Placement?.CheckedInAt,
                c.Placement?.CheckedInBy,
                c.Placement?.CheckInOverride,
                c.Placement?.CheckedOutAt,
                c.Placement?.CheckedOutBy,
                c.Placement?.PickedUpBy,
                GuardianPhone = phones.GetValueOrDefault(c.HouseholdId),
                PickupAdults = adults.Where(a => a.HouseholdId == c.HouseholdId).Select(a => new { a.Id, a.Name, a.Relationship }),
            }).ToList();
            return Results.Ok(new
            {
                Session = new { s.Id, s.Name, s.StartDate, s.EndDate, Program = s.Program.Name },
                Capacity = s.Pools.Sum(p => p.Capacity),
                Counts = new
                {
                    Registered = rows.Count,
                    Ready = rows.Count(r => r.Status == "Ready"),
                    Blocked = rows.Count(r => r.Status == "Blocked"),
                    CheckedIn = rows.Count(r => r.Status == "Checked in"),
                    CheckedOut = rows.Count(r => r.Status == "Checked out"),
                },
                Rows = rows,
            });
        }).RequireAuthorization(Policies.Staff);

        ops.MapPost("/check-in/{registrationId:int}", async (int registrationId, CheckInRequest req, CampDbContext db, StaffUser staff, IAuditLog audit, CancellationToken ct) =>
        {
            var (reg, camper, usesCampDoc) = await Load(db, registrationId, ct);
            if (reg is null || camper is null) return Results.NotFound();
            if (camper.Placement?.CheckedInAt is { } at)
                return OpsResults.Conflict($"{camper.Name} was already checked in by {camper.Placement.CheckedInBy} at {at:h:mm tt} UTC.");
            var reason = req.OverrideReason?.Trim();
            if (reason?.Length > 500) return OpsResults.Invalid("overrideReason", "Keep the override reason under 500 characters.");
            var blockers = camper.Reasons.Select(r => OpsReadModel.Describe(r, camper, usesCampDoc).Label).ToList();
            if (blockers.Count > 0 && string.IsNullOrEmpty(reason))
                return Results.Conflict(new
                {
                    error = $"{camper.Name} can't be checked in yet: {string.Join(" · ", blockers)}. Resolve these, or check in with an override and a reason.",
                    blockers,
                });

            var placement = await OpsReadModel.PlacementFor(db, reg, ct);
            placement.CheckedInAt = DateTime.UtcNow;
            placement.CheckedInBy = staff.Actor;
            placement.CheckInOverride = blockers.Count > 0 ? reason : null;
            if (blockers.Count > 0)
                audit.Record("ops.checked_in_override", "Registration", reg.Id, $"Checked in {camper.Name} with open items ({string.Join(", ", blockers)}). Reason: {reason}");
            else
                audit.Record("ops.checked_in", "Registration", reg.Id, $"Checked in {camper.Name}.");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { placement.CheckedInAt, placement.CheckedInBy, Override = placement.CheckInOverride });
        }).RequireAuthorization(Policies.Cet);

        ops.MapPost("/check-out/{registrationId:int}", async (int registrationId, CheckOutRequest req, CampDbContext db, StaffUser staff, IAuditLog audit, CancellationToken ct) =>
        {
            var (reg, camper, _) = await Load(db, registrationId, ct);
            if (reg is null || camper is null) return Results.NotFound();
            if (camper.Placement?.CheckedInAt is null) return OpsResults.Conflict($"{camper.Name} hasn't been checked in, so there's nothing to check out.");
            if (camper.Placement.CheckedOutAt is not null) return OpsResults.Conflict($"{camper.Name} was already picked up by {camper.Placement.PickedUpBy}.");
            var adult = await db.Set<OpsPickupAdult>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == req.PickupAdultId, ct);
            // Only the camper's own household can authorize a pickup; any other id is treated as unknown.
            if (adult is null || adult.HouseholdId != reg.HouseholdId)
                return OpsResults.Invalid("pickupAdultId", $"That adult isn't on {camper.FirstName}'s authorized pickup list. Don't release the camper; call the guardian.");
            if (!req.IdChecked) return OpsResults.Invalid("idChecked", $"Check {adult.Name}'s photo ID before releasing {camper.FirstName}.");

            var placement = await OpsReadModel.PlacementFor(db, reg, ct);
            placement.CheckedOutAt = DateTime.UtcNow;
            placement.CheckedOutBy = staff.Actor;
            placement.PickedUpBy = $"{adult.Name} ({adult.Relationship})";
            audit.Record("ops.checked_out", "Registration", reg.Id, $"Released {camper.Name} to {placement.PickedUpBy}; photo ID checked.");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { placement.CheckedOutAt, placement.PickedUpBy });
        }).RequireAuthorization(Policies.Cet);
    }

    static string Status(Camper c) =>
        c.Placement?.CheckedOutAt is not null ? "Checked out"
        : c.Placement?.CheckedInAt is not null ? "Checked in"
        : c.Reasons.Count > 0 ? "Blocked" : "Ready";

    static async Task<(Registration? Reg, Camper? Camper, bool UsesCampDoc)> Load(CampDbContext db, int registrationId, CancellationToken ct)
    {
        var reg = await db.Registrations.FirstOrDefaultAsync(r => r.Id == registrationId && r.Status == RegistrationStatus.Confirmed, ct);
        if (reg is null) return (null, null, false);
        var roster = await OpsReadModel.LoadAsync(db, reg.SessionId, ct);
        return (reg, roster?.Campers.FirstOrDefault(c => c.RegistrationId == registrationId), roster?.UsesCampDoc ?? false);
    }
}
