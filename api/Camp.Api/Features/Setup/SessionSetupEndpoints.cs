using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Setup;

public record SessionInput(string Name, DateOnly StartDate, DateOnly EndDate, string Location, DateTime? RegistrationOpensAt, DateTime? PriorityOpensAt);
public record PoolInput(string Name, Gender? Gender, int GradeMin, int GradeMax, int Capacity);
public record NewSessionInput(string Name, DateOnly StartDate, DateOnly EndDate, int PriceCents, int DepositCents, string PoolName, Gender? Gender, int GradeMin, int GradeMax, int Capacity);

/// <summary>
/// K3 · Session editor with capacity pools (FR-67, FR-68, FR-106). Capacity is always a set of pools,
/// never one number. Retreat-center sessions take room capacity from Oracle Opera, so it is read-only here.
/// </summary>
public sealed class SessionSetupEndpoints : IEndpointModule
{
    public const string MountBerry = "WinShape Camps · Mount Berry, GA";
    public const string RetreatCenter = "WinShape Retreat · Rome, GA";
    const string OperaMessage = "Room capacity at the retreat center comes from Oracle Opera. Change it in Opera; it syncs here.";

    public void Map(IEndpointRouteBuilder app)
    {
        var setup = app.MapGroup("/api/admin/setup").RequireAuthorization(Policies.Admin);

        setup.MapGet("/sessions/{id:int}", async (int id, CampDbContext db, CancellationToken ct, TimeProvider clock) =>
        {
            var s = await db.Sessions.AsNoTracking().Include(x => x.Program).ThenInclude(p => p.Ministry).Include(x => x.Pools).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Results.NotFound();
            var extra = await db.Set<SessionSetup>().AsNoTracking().FirstOrDefaultAsync(x => x.SessionId == id, ct);
            var waitlist = await db.WaitlistEntries.Where(w => w.Pool.SessionId == id && (w.Status == WaitlistStatus.Waiting || w.Status == WaitlistStatus.Offered))
                .GroupBy(w => w.PoolId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var usedPools = await db.Registrations.Where(r => r.SessionId == id).Select(r => r.PoolId)
                .Union(db.WaitlistEntries.Where(w => w.Pool.SessionId == id).Select(w => w.PoolId)).ToListAsync(ct);
            var registered = await ProgramSetupEndpoints.ActiveRegistrations(db).CountAsync(r => r.SessionId == id, ct);
            var location = extra?.Location is { Length: > 0 } l ? l : s.Program.Location;
            var programState = (await db.Set<ProgramSetup>().AsNoTracking().FirstOrDefaultAsync(x => x.ProgramId == s.ProgramId, ct))?.State
                ?? (s.Program.IsPublished ? PublishState.Published : PublishState.Draft);
            var pools = s.Pools.OrderBy(p => p.SortOrder).ToList();
            var locations = await db.Programs.Select(p => p.Location).Union(db.Set<SessionSetup>().Select(x => x.Location)).Distinct().ToListAsync(ct);
            return Results.Ok(new
            {
                s.Id,
                s.Name,
                s.StartDate,
                s.EndDate,
                Location = location,
                extra?.RegistrationOpensAt,
                extra?.PriorityOpensAt,
                s.WaitlistMode,
                WaitlistModeLabel = "Admin approval",
                CapacityFromOpera = location == RetreatCenter,
                Registered = registered,
                Program = new
                {
                    s.Program.Id,
                    s.Program.Name,
                    Ministry = s.Program.Ministry.Name,
                    Type = s.Program.Type.ToString(),
                    State = programState.ToString(),
                    StateLabel = ProgramSetupEndpoints.StateLabel(programState),
                },
                Status = RegistrationWindow(s, programState, clock),
                Pools = pools.Select(p => new
                {
                    p.Id,
                    p.Name,
                    Gender = p.Gender?.ToString(),
                    p.GradeMin,
                    p.GradeMax,
                    p.Capacity,
                    Taken = p.Reserved,
                    Open = p.Capacity - p.Reserved,
                    Waitlisted = waitlist.GetValueOrDefault(p.Id),
                    Removable = pools.Count > 1 && p.Reserved == 0 && !usedPools.Contains(p.Id),
                }),
                Totals = new
                {
                    Capacity = pools.Sum(p => p.Capacity),
                    Taken = pools.Sum(p => p.Reserved),
                    Open = pools.Sum(p => p.Capacity - p.Reserved),
                    Waitlisted = waitlist.Values.Sum(),
                },
                FullPools = pools.Where(p => p.Reserved >= p.Capacity && waitlist.GetValueOrDefault(p.Id) > 0).Select(p => p.Name),
                Locations = locations.Append(MountBerry).Append(RetreatCenter).Where(x => x.Length > 0).Distinct().Order(),
            });
        });

        setup.MapPut("/sessions/{id:int}", async (int id, SessionInput req, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var s = await db.Sessions.Include(x => x.Program).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Results.NotFound();
            var extra = await db.Set<SessionSetup>().FirstOrDefaultAsync(x => x.SessionId == id, ct);
            if (extra is null)
            {
                extra = new SessionSetup { SessionId = id, Location = s.Program.Location };
                db.Set<SessionSetup>().Add(extra);
            }
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(req.Name)) errors["name"] = ["Enter a session name."];
            else if (req.Name.Trim().Length > 80) errors["name"] = ["Session names are limited to 80 characters."];
            if (req.EndDate < req.StartDate) errors["endDate"] = ["The session can't end before it starts."];
            if (string.IsNullOrWhiteSpace(req.Location)) errors["location"] = ["Choose a location."];
            if (req.StartDate != s.StartDate && await db.Registrations.AnyAsync(r => r.SessionId == id && r.Status != RegistrationStatus.Cancelled, ct))
                errors["startDate"] = ["Campers are already registered, and their grades were worked out from this start date. Move them before changing it."];
            if (req.RegistrationOpensAt is { } open && DateOnly.FromDateTime(open) > req.StartDate)
                errors["registrationOpensAt"] = ["Registration has to open before the session starts."];
            if (req.PriorityOpensAt is { } priority && req.RegistrationOpensAt is { } opens && priority > opens)
                errors["priorityOpensAt"] = ["Priority registration opens before general registration."];
            if (req.PriorityOpensAt is not null && req.RegistrationOpensAt is null)
                errors["registrationOpensAt"] = ["Set when general registration opens too."];
            if (errors.Count > 0) return SetupResults.Invalid(errors);

            var before = (s.Name, s.StartDate, s.EndDate, Location: extra.Location, extra.RegistrationOpensAt, extra.PriorityOpensAt);
            s.Name = req.Name.Trim();
            s.StartDate = req.StartDate;
            s.EndDate = req.EndDate;
            extra.Location = req.Location.Trim();
            extra.RegistrationOpensAt = req.RegistrationOpensAt;
            extra.PriorityOpensAt = req.PriorityOpensAt;
            audit.Record(db, "session.updated", "Session", id, $"Edited {s.Program.Name} · {s.Name}.",
                ("Name", before.Name, s.Name),
                ("Dates", $"{SetupResults.Date(before.StartDate)} – {SetupResults.Date(before.EndDate)}", $"{SetupResults.Date(s.StartDate)} – {SetupResults.Date(s.EndDate)}"),
                ("Location", before.Location, extra.Location),
                ("Registration opens", SetupResults.Date(before.RegistrationOpensAt), SetupResults.Date(extra.RegistrationOpensAt)),
                ("Priority registration", SetupResults.Date(before.PriorityOpensAt), SetupResults.Date(extra.PriorityOpensAt)));
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        setup.MapPost("/programs/{programId:int}/sessions", async (int programId, NewSessionInput req, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var program = await db.Programs.FirstOrDefaultAsync(p => p.Id == programId, ct);
            if (program is null) return Results.NotFound();
            var state = await ProgramSetupEndpoints.StateOf(db, program, ct);
            if (state.State != PublishState.Draft)
                return SetupResults.Conflict($"{program.Name} is {ProgramSetupEndpoints.StateLabel(state.State).ToLowerInvariant()}. New sessions go through approval, so return it to draft first.");
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(req.Name)) errors["name"] = ["Enter a session name."];
            if (req.EndDate < req.StartDate) errors["endDate"] = ["The session can't end before it starts."];
            if (req.PriceCents <= 0) errors["priceCents"] = ["Enter a price above $0."];
            if (req.DepositCents < 0 || req.DepositCents > req.PriceCents) errors["depositCents"] = ["The deposit has to be between $0 and the price."];
            foreach (var (k, v) in ValidatePool(new PoolInput(req.PoolName, req.Gender, req.GradeMin, req.GradeMax, req.Capacity), [], null)) errors[k] = v;
            if (errors.Count > 0) return SetupResults.Invalid(errors);

            var session = new Session
            {
                ProgramId = programId,
                Name = req.Name.Trim(),
                StartDate = req.StartDate,
                EndDate = req.EndDate,
                PriceCents = req.PriceCents,
                DepositCents = req.DepositCents,
                BalanceDueDate = req.StartDate.AddDays(-30),
            };
            session.Pools.Add(new CapacityPool { Name = req.PoolName.Trim(), Gender = req.Gender, GradeMin = req.GradeMin, GradeMax = req.GradeMax, Capacity = req.Capacity, SortOrder = 1 });
            db.Sessions.Add(session);
            await db.SaveChangesAsync(ct);
            db.Set<SessionSetup>().Add(new SessionSetup { SessionId = session.Id, Location = program.Location });
            foreach (var t in RefundPolicy.Defaults) db.Set<RefundTier>().Add(new RefundTier { SessionId = session.Id, DaysBefore = t.DaysBefore, RefundPercent = t.RefundPercent, Basis = t.Basis });
            audit.Record(db, "session.created", "Session", session.Id, $"Added {program.Name} · {session.Name} with pool {req.PoolName.Trim()} ({req.Capacity}).",
                ("Session", null, session.Name), ("Price", null, SetupResults.Money(session.PriceCents)), ("Capacity", null, req.Capacity.ToString(CultureInfo.InvariantCulture)));
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { session.Id });
        });

        setup.MapPost("/sessions/{id:int}/pools", async (int id, PoolInput req, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var s = await db.Sessions.Include(x => x.Program).Include(x => x.Pools).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Results.NotFound();
            if (await FromOpera(db, s, ct)) return SetupResults.Conflict(OperaMessage);
            var errors = ValidatePool(req, s.Pools, null);
            if (errors.Count > 0) return SetupResults.Invalid(errors);
            var pool = new CapacityPool { SessionId = id, Name = req.Name.Trim(), Gender = req.Gender, GradeMin = req.GradeMin, GradeMax = req.GradeMax, Capacity = req.Capacity, SortOrder = s.Pools.Count == 0 ? 1 : s.Pools.Max(p => p.SortOrder) + 1 };
            db.CapacityPools.Add(pool);
            await db.SaveChangesAsync(ct);
            audit.Record(db, "capacity.pool_added", "CapacityPool", pool.Id, $"Added pool {pool.Name} to {s.Program.Name} · {s.Name}.",
                ("Pool", null, Describe(pool)), ("Capacity", null, pool.Capacity.ToString(CultureInfo.InvariantCulture)));
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { pool.Id });
        });

        setup.MapPut("/pools/{id:int}", async (int id, PoolInput req, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var pool = await db.CapacityPools.Include(p => p.Session).ThenInclude(s => s.Program).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (pool is null) return Results.NotFound();
            var siblings = await db.CapacityPools.AsNoTracking().Where(p => p.SessionId == pool.SessionId).ToListAsync(ct);
            var errors = ValidatePool(req, siblings, id);
            if (errors.Count > 0) return SetupResults.Invalid(errors);
            var rulesChanged = req.Gender != pool.Gender || req.GradeMin != pool.GradeMin || req.GradeMax != pool.GradeMax;
            if (rulesChanged && (await db.Registrations.AnyAsync(r => r.PoolId == id, ct) || await db.WaitlistEntries.AnyAsync(w => w.PoolId == id, ct)))
                return SetupResults.Invalid("gradeMin", $"Campers were already placed in {pool.Name} by its grades and gender. Add a new pool instead of changing who this one is for.");
            if (req.Capacity != pool.Capacity && await FromOpera(db, pool.Session, ct)) return SetupResults.Conflict(OperaMessage);

            var before = (pool.Name, Who: Describe(pool), pool.Capacity);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Conditional update so capacity can't drop below seats taken by a registration in flight.
            var updated = await db.CapacityPools.Where(p => p.Id == id && p.Reserved <= req.Capacity)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.Capacity, req.Capacity), ct);
            if (updated == 0)
            {
                var taken = await db.CapacityPools.Where(p => p.Id == id).Select(p => p.Reserved).SingleAsync(ct);
                return SetupResults.Invalid("capacity", $"{pool.Name} already has {taken} seats taken, so capacity can't go below {taken}.");
            }
            pool.Name = req.Name.Trim();
            pool.Gender = req.Gender;
            pool.GradeMin = req.GradeMin;
            pool.GradeMax = req.GradeMax;
            db.Entry(pool).Property(p => p.Capacity).IsModified = false;
            audit.Record(db, "capacity.changed", "CapacityPool", id, $"Edited pool {pool.Name} in {pool.Session.Program.Name} · {pool.Session.Name}.",
                ("Pool", before.Name, pool.Name), ("Who", before.Who, Describe(pool)),
                ("Capacity", before.Capacity.ToString(CultureInfo.InvariantCulture), req.Capacity.ToString(CultureInfo.InvariantCulture)));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok();
        });

        setup.MapDelete("/pools/{id:int}", async (int id, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var pool = await db.CapacityPools.Include(p => p.Session).ThenInclude(s => s.Program).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (pool is null) return Results.NotFound();
            if (await db.CapacityPools.CountAsync(p => p.SessionId == pool.SessionId, ct) == 1)
                return SetupResults.Conflict("A session needs at least one pool.");
            if (pool.Reserved > 0 || await db.Registrations.AnyAsync(r => r.PoolId == id, ct) || await db.WaitlistEntries.AnyAsync(w => w.PoolId == id, ct))
                return SetupResults.Conflict($"{pool.Name} has registrations or a waitlist, so it can't be removed. Set its capacity to what's taken instead.");
            db.CapacityPools.Remove(pool);
            audit.Record(db, "capacity.pool_removed", "CapacityPool", id, $"Removed empty pool {pool.Name} from {pool.Session.Program.Name} · {pool.Session.Name}.",
                ("Pool", Describe(pool), null), ("Capacity", pool.Capacity.ToString(CultureInfo.InvariantCulture), null));
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });
    }

    static async Task<bool> FromOpera(CampDbContext db, Session s, CancellationToken ct)
    {
        var location = await db.Set<SessionSetup>().Where(x => x.SessionId == s.Id).Select(x => x.Location).FirstOrDefaultAsync(ct);
        return (location is { Length: > 0 } ? location : s.Program.Location) == RetreatCenter;
    }

    /// <summary>
    /// What families can do today. Checkout doesn't read the open and priority dates yet, so the badge
    /// doesn't claim a window checkout wouldn't honour: a published session that hasn't ended is open.
    /// </summary>
    static string RegistrationWindow(Session s, PublishState programState, TimeProvider clock)
    {
        if (programState != PublishState.Published) return "Not published";
        if (clock.Today() > s.EndDate) return "Ended";
        return "Registration open";
    }

    /// <summary>Checks a pool's own fields and that no camper could match two pools.</summary>
    static Dictionary<string, string[]> ValidatePool(PoolInput req, IEnumerable<CapacityPool> siblings, int? selfId)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(req.Name)) errors["name"] = ["Name the pool, for example \"Boys G6–8\"."];
        else if (req.Name.Trim().Length > 60) errors["name"] = ["Pool names are limited to 60 characters."];
        if (req.Capacity is < 0 or > 5000) errors["capacity"] = ["Capacity has to be between 0 and 5,000."];
        var adults = req.GradeMin == 99 && req.GradeMax == 99;
        if (!adults && (req.GradeMin is < 0 or > 12 || req.GradeMax is < 0 or > 12)) errors["gradeMin"] = ["Grades run from K (0) to 12."];
        else if (req.GradeMax < req.GradeMin) errors["gradeMax"] = ["The last grade can't be before the first."];
        if (errors.Count > 0) return errors;
        var clash = siblings.FirstOrDefault(p => p.Id != selfId
            && p.GradeMin <= req.GradeMax && req.GradeMin <= p.GradeMax
            && (p.Gender is null || req.Gender is null || p.Gender == req.Gender));
        if (clash is not null)
            errors["gradeMin"] = [$"This overlaps {clash.Name}. A camper has to fit exactly one pool, so change the grades or gender."];
        return errors;
    }

    static string Describe(CapacityPool p)
    {
        var grades = p.GradeMin == 99 ? "adults" : p.GradeMin == p.GradeMax ? $"grade {p.GradeMin}" : $"grades {p.GradeMin}–{p.GradeMax}";
        return p.Gender switch { Gender.Male => $"Boys, {grades}", Gender.Female => $"Girls, {grades}", _ => $"Everyone, {grades}" };
    }
}
