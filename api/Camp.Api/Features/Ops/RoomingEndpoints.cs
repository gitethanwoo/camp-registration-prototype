using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Ops;

public record CabinMoveRequest(int? CabinId);

/// <summary>
/// O3 · Rooming board (FR-82, FR-25, FR-106). Cabins are single-gender and the server refuses a
/// placement across genders or past a cabin's beds. Changes to the roster after the last rooming
/// review are listed so staff can re-check them.
/// </summary>
public sealed class RoomingEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        var ops = app.MapGroup("/api/admin/ops");

        ops.MapGet("/sessions/{id:int}/rooming", async (int id, CampDbContext db, CancellationToken ct) =>
        {
            var roster = await OpsReadModel.LoadAsync(db, id, ct);
            if (roster is null) return Results.NotFound();
            var s = roster.Session;
            var cabins = await db.Set<OpsCabin>().AsNoTracking().Where(c => c.SessionId == id).OrderBy(c => c.SortOrder).ToListAsync(ct);
            // The retreat center's rooms live in Oracle Opera (FR-106); this board is for camp cabins.
            var managedInOpera = cabins.Count == 0 && s.Program.Location.Contains("Retreat", StringComparison.OrdinalIgnoreCase);
            var requests = await db.Set<OpsBuddyRequest>().AsNoTracking().Where(r => r.SessionId == id).OrderBy(r => r.Id).ToListAsync(ct);
            var review = await db.Set<OpsRoomingReview>().AsNoTracking().FirstOrDefaultAsync(r => r.SessionId == id, ct);
            var stale = await StalePlacements(db, id).Select(p => new
            {
                p.RegistrationId,
                Name = db.Registrations.Where(r => r.Id == p.RegistrationId).Select(r => r.Person.FirstName + " " + r.Person.LastName).First(),
                Cabin = db.Set<OpsCabin>().Where(c => c.Id == p.CabinId).Select(c => c.Name).First(),
            }).ToListAsync(ct);

            var byId = roster.Campers.ToDictionary(c => c.RegistrationId);
            var cabinIds = cabins.ToDictionary(c => c.Id);
            int? CabinOf(Camper c) => c.Placement?.CabinId is { } x && cabinIds.ContainsKey(x) ? x : null;
            string CabinName(Camper c) => CabinOf(c) is { } x ? cabinIds[x].Name : "Unassigned";

            // A request is met when both campers share a cabin; it can't be met across genders or when
            // the other camper is no longer confirmed in this session.
            var pairs = requests.Select(r =>
            {
                byId.TryGetValue(r.RegistrationId, out var a);
                byId.TryGetValue(r.RequestedRegistrationId, out var b);
                var status = a is null || b is null ? "Cannot be met"
                    : a.Gender != b.Gender ? "Cannot be met"
                    : CabinOf(a) is { } ca && ca == CabinOf(b) ? "Met" : "Not met";
                var why = a is null || b is null ? "The other camper is no longer registered for this session."
                    : a.Gender != b.Gender ? "Cabins are for one gender, so these campers can't share one."
                    : status == "Met" ? $"Both in {CabinName(a)}."
                    : $"{a.Name} is in {CabinName(a)} and {b.Name} is in {CabinName(b)}.";
                return new { Request = r, A = a, B = b, Status = status, Why = why };
            }).Where(p => p.A is not null).ToList();

            var reviewedAt = review?.ReviewedAt;
            var reviewItems = roster.Campers
                .Where(c => reviewedAt is not null && c.CreatedAt > reviewedAt && CabinOf(c) is null)
                .Select(c => new { c.RegistrationId, c.Name, Reason = "Registered after the last rooming review and has no cabin yet." })
                .Concat(pairs.Where(p => reviewedAt is not null && p.Request.CreatedAt > reviewedAt)
                    .Select(p => new { p.A!.RegistrationId, p.A.Name, Reason = $"Cabinmate request added: wants to be with {p.B?.Name ?? "a camper who has left"} ({p.Status.ToLowerInvariant()})." }))
                .Concat(stale.Select(x => new { x.RegistrationId, x.Name, Reason = $"No longer registered for this session; still listed in {x.Cabin} until you mark the review done." }))
                .ToList();

            var campersView = roster.Campers.Select(c => new
            {
                c.RegistrationId,
                c.Name,
                c.Grade,
                Gender = c.Gender.ToString(),
                CabinId = CabinOf(c),
                Requests = pairs.Where(p => p.A == c || p.B == c).Select(p => new
                {
                    With = p.A == c ? p.B?.Name ?? "" : p.A!.Name,
                    WithRegistrationId = p.A == c ? p.Request.RequestedRegistrationId : p.Request.RegistrationId,
                    p.Status,
                    p.Why,
                }),
                NeedsReview = reviewItems.Any(i => i.RegistrationId == c.RegistrationId),
            }).ToList();

            object Side(Gender g)
            {
                var list = cabins.Where(c => c.Gender == g).ToList();
                return new
                {
                    Cabins = list.Count,
                    Beds = list.Sum(c => c.Beds),
                    Assigned = roster.Campers.Count(c => c.Gender == g && CabinOf(c) is not null),
                    Registered = roster.Campers.Count(c => c.Gender == g),
                };
            }

            return Results.Ok(new
            {
                Session = new { s.Id, s.Name, s.StartDate, s.EndDate, Program = s.Program.Name },
                ManagedInOpera = managedInOpera,
                Summary = new
                {
                    Cabins = cabins.Count,
                    Beds = cabins.Sum(c => c.Beds),
                    Capacity = s.Pools.Sum(p => p.Capacity),
                    Registered = roster.Campers.Count,
                    Assigned = roster.Campers.Count(c => CabinOf(c) is not null),
                    Unassigned = roster.Campers.Count(c => CabinOf(c) is null),
                    Boys = Side(Gender.Male),
                    Girls = Side(Gender.Female),
                    RequestsMet = pairs.Count(p => p.Status == "Met"),
                    RequestsNotMet = pairs.Count(p => p.Status == "Not met"),
                    Conflicts = pairs.Count(p => p.Status == "Cannot be met"),
                },
                Review = new { ReviewedAt = reviewedAt, review?.ReviewedBy, Items = reviewItems },
                Cabins = cabins.Select(c =>
                {
                    var inCabin = campersView.Where(x => x.CabinId == c.Id).ToList();
                    var cabinPairs = pairs.Where(p => (p.A is { } a && CabinOf(a) == c.Id) || (p.B is { } b && CabinOf(b) == c.Id)).ToList();
                    return new
                    {
                        c.Id,
                        c.Name,
                        Gender = c.Gender.ToString(),
                        c.Beds,
                        Taken = inCabin.Count,
                        RequestsMet = cabinPairs.Count(p => p.Status == "Met"),
                        RequestsNotMet = cabinPairs.Count(p => p.Status != "Met"),
                        Campers = inCabin,
                    };
                }),
                Unassigned = campersView.Where(x => x.CabinId is null),
            });
        }).RequireAuthorization(Policies.Staff);

        ops.MapPut("/rooming/placements/{registrationId:int}", async (int registrationId, CabinMoveRequest req, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var reg = await db.Registrations.Include(r => r.Person).FirstOrDefaultAsync(r => r.Id == registrationId, ct);
            if (reg is null || reg.Status != RegistrationStatus.Confirmed) return Results.NotFound();
            // The target cabin is row-locked so two people can't both take its last bed.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            OpsCabin? target = null;
            if (req.CabinId is { } cid)
            {
                target = await db.Set<OpsCabin>().FromSqlInterpolated($"SELECT * FROM OpsCabins WITH (UPDLOCK, HOLDLOCK) WHERE Id = {cid}").FirstOrDefaultAsync(ct);
                if (target is null || target.SessionId != reg.SessionId) return Results.NotFound();
                if (target.Gender != reg.Person.Gender)
                    return OpsResults.Conflict($"{target.Name} is a {(target.Gender == Gender.Male ? "boys" : "girls")} cabin. Choose a {(reg.Person.Gender == Gender.Male ? "boys" : "girls")} cabin for {reg.Person.FullName}.");
            }
            var placement = await OpsReadModel.PlacementFor(db, reg, ct);
            var previous = placement.CabinId;
            if (previous == req.CabinId) return Results.Ok(new { PreviousCabinId = previous });
            if (target is not null)
            {
                var taken = await db.Set<OpsPlacement>().CountAsync(p => p.CabinId == target.Id
                    && db.Registrations.Any(r => r.Id == p.RegistrationId && r.SessionId == p.SessionId && r.Status == RegistrationStatus.Confirmed), ct);
                if (taken >= target.Beds)
                    return OpsResults.Conflict($"{target.Name} is full ({taken} of {target.Beds} beds). Move someone out first.");
            }
            var from = previous is { } p ? (await db.Set<OpsCabin>().FindAsync([p], ct))?.Name : null;
            placement.CabinId = req.CabinId;
            audit.Record("ops.cabin_moved", "Registration", reg.Id, $"Moved {reg.Person.FullName} from {from ?? "Unassigned"} to {target?.Name ?? "Unassigned"}.");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { PreviousCabinId = previous });
        }).RequireAuthorization(Policies.Cet);

        // Marks the rooming reviewed now; campers who left the session give their beds back.
        ops.MapPost("/sessions/{id:int}/rooming/review", async (int id, CampDbContext db, StaffUser staff, IAuditLog audit, CancellationToken ct) =>
        {
            if (!await db.Set<OpsCabin>().AnyAsync(c => c.SessionId == id, ct)) return Results.NotFound();
            var review = await db.Set<OpsRoomingReview>().FirstOrDefaultAsync(r => r.SessionId == id, ct);
            if (review is null)
            {
                review = new OpsRoomingReview { SessionId = id };
                db.Set<OpsRoomingReview>().Add(review);
            }
            review.ReviewedAt = DateTime.UtcNow;
            review.ReviewedBy = staff.Actor;
            var stale = await StalePlacements(db, id).ToListAsync(ct);
            foreach (var p in stale)
            {
                p.CabinId = null;
                p.GroupId = null;
            }
            audit.Record("ops.rooming_reviewed", "Session", id,
                $"Marked rooming reviewed.{(stale.Count > 0 ? $" Released {OpsResults.Plural(stale.Count, "bed", "beds")} held by campers who left the session." : "")}");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { review.ReviewedAt, review.ReviewedBy, Released = stale.Count });
        }).RequireAuthorization(Policies.Cet);
    }

    /// <summary>Placements in this session still holding a cabin for a camper who is no longer confirmed here.</summary>
    static IQueryable<OpsPlacement> StalePlacements(CampDbContext db, int sessionId) =>
        db.Set<OpsPlacement>().Where(p => p.SessionId == sessionId && p.CabinId != null
            && !db.Registrations.Any(r => r.Id == p.RegistrationId && r.SessionId == sessionId && r.Status == RegistrationStatus.Confirmed));
}
