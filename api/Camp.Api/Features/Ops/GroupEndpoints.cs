using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Ops;

public record GroupMoveRequest(int? GroupId);
public record PoolRequest(int PoolId);

/// <summary>
/// O2 · Group assignment board (FR-80). Every move saves at once and returns the previous group so the
/// page can offer Undo. Auto-suggest only proposes moves; nothing changes until a person approves them.
/// </summary>
public sealed class GroupEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        var ops = app.MapGroup("/api/admin/ops");

        ops.MapGet("/sessions/{id:int}/groups", async (int id, int? poolId, CampDbContext db, CancellationToken ct) =>
        {
            var roster = await OpsReadModel.LoadAsync(db, id, ct);
            if (roster is null) return Results.NotFound();
            var board = await GroupBoard.LoadAsync(db, roster, poolId, ct);
            if (board is null) return Results.Ok(new { Pools = Array.Empty<object>(), PoolId = (int?)null, Groups = Array.Empty<object>(), Campers = Array.Empty<object>() });
            return Results.Ok(board.ToView());
        }).RequireAuthorization(Policies.Staff);

        ops.MapPut("/groups/placements/{registrationId:int}", async (int registrationId, GroupMoveRequest req, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var reg = await db.Registrations.Include(r => r.Person).FirstOrDefaultAsync(r => r.Id == registrationId, ct);
            if (reg is null || reg.Status != RegistrationStatus.Confirmed) return Results.NotFound();
            // The target group is row-locked so two people can't both take its last spot.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            OpsGroup? target = null;
            if (req.GroupId is { } gid)
            {
                target = await db.Set<OpsGroup>().FromSqlInterpolated($"SELECT * FROM OpsGroups WITH (UPDLOCK, HOLDLOCK) WHERE Id = {gid}").FirstOrDefaultAsync(ct);
                if (target is null || target.SessionId != reg.SessionId) return Results.NotFound();
                var pools = await db.CapacityPools.Where(p => p.Id == target.PoolId || p.Id == reg.PoolId).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
                if (target.PoolId != reg.PoolId)
                    return OpsResults.Conflict($"{target.Name} is a {pools[target.PoolId]} group and {reg.Person.FullName} is in {pools[reg.PoolId]}.");
            }
            var placement = await OpsReadModel.PlacementFor(db, reg, ct);
            var previous = placement.GroupId;
            if (previous == req.GroupId) return Results.Ok(new { PreviousGroupId = previous });
            if (target is not null)
            {
                var taken = await Occupancy(db, target.Id, ct);
                if (taken >= target.Capacity)
                    return OpsResults.Conflict($"{target.Name} is full ({taken} of {target.Capacity}). Move someone out first, or use Auto-suggest to swap.");
            }
            var from = previous is { } p ? (await db.Set<OpsGroup>().FindAsync([p], ct))?.Name : null;
            placement.GroupId = req.GroupId;
            placement.SuggestedGroupId = null;
            placement.SuggestionReason = null;
            audit.Record("ops.group_moved", "Registration", reg.Id, $"Moved {reg.Person.FullName} from {from ?? "Unassigned"} to {target?.Name ?? "Unassigned"}.");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { PreviousGroupId = previous });
        }).RequireAuthorization(Policies.Cet);

        ops.MapPost("/sessions/{id:int}/groups/suggest", async (int id, PoolRequest req, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var roster = await OpsReadModel.LoadAsync(db, id, ct);
            if (roster is null) return Results.NotFound();
            var board = await GroupBoard.LoadAsync(db, roster, req.PoolId, ct);
            if (board is null || board.PoolId != req.PoolId) return Results.NotFound();
            var proposals = board.Suggest();
            var regIds = board.Campers.Select(c => c.RegistrationId).ToList();
            var regs = await db.Registrations.Where(r => regIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
            foreach (var c in board.Campers)
            {
                var placement = await OpsReadModel.PlacementFor(db, regs[c.RegistrationId], ct);
                var proposal = proposals.GetValueOrDefault(c.RegistrationId);
                placement.SuggestedGroupId = proposal?.GroupId;
                placement.SuggestionReason = proposal?.Reason;
            }
            audit.Record("ops.groups_suggested", "Session", id, $"Auto-suggest proposed {OpsResults.Plural(proposals.Count, "move", "moves")} in {board.PoolName}, waiting for approval.");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { Suggested = proposals.Count });
        }).RequireAuthorization(Policies.Cet);

        ops.MapPost("/sessions/{id:int}/groups/suggestions/approve", async (int id, PoolRequest req, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var groups = await db.Set<OpsGroup>()
                .FromSqlInterpolated($"SELECT * FROM OpsGroups WITH (UPDLOCK, HOLDLOCK) WHERE SessionId = {id} AND PoolId = {req.PoolId}")
                .OrderBy(g => g.SortOrder).ToListAsync(ct);
            if (groups.Count == 0) return Results.NotFound();
            var groupIds = groups.Select(g => g.Id).ToList();
            var pending = await db.Set<OpsPlacement>()
                .Where(p => p.SessionId == id && p.SuggestedGroupId != null && groupIds.Contains(p.SuggestedGroupId.Value)
                    && db.Registrations.Any(r => r.Id == p.RegistrationId && r.SessionId == id && r.Status == RegistrationStatus.Confirmed))
                .ToListAsync(ct);
            if (pending.Count == 0) return OpsResults.Conflict("There are no suggestions waiting. Run Auto-suggest first.");
            var pendingIds = pending.Select(p => p.RegistrationId).ToList();
            var names = await db.Registrations.Where(r => pendingIds.Contains(r.Id))
                .Select(r => new { r.Id, Name = r.Person.FirstName + " " + r.Person.LastName })
                .ToDictionaryAsync(r => r.Id, r => r.Name, ct);
            var groupName = groups.ToDictionary(g => g.Id, g => g.Name);
            foreach (var p in pending)
            {
                var from = p.GroupId is { } g ? groupName.GetValueOrDefault(g, "another group") : "Unassigned";
                audit.Record("ops.group_moved", "Registration", p.RegistrationId,
                    $"Approved suggestion: moved {names[p.RegistrationId]} from {from} to {groupName[p.SuggestedGroupId!.Value]}. {p.SuggestionReason}");
                p.GroupId = p.SuggestedGroupId;
                p.SuggestedGroupId = null;
                p.SuggestionReason = null;
            }
            await db.SaveChangesAsync(ct);
            // Moves are applied together, so capacity is checked on the result, not move by move.
            foreach (var g in groups)
            {
                var taken = await Occupancy(db, g.Id, ct);
                if (taken > g.Capacity)
                {
                    await tx.RollbackAsync(ct);
                    return OpsResults.Conflict($"{g.Name} would have {taken} of {g.Capacity} after these moves because the board changed. Run Auto-suggest again.");
                }
            }
            await tx.CommitAsync(ct);
            return Results.Ok(new { Moved = pending.Count });
        }).RequireAuthorization(Policies.Cet);

        ops.MapPost("/sessions/{id:int}/groups/suggestions/dismiss", async (int id, PoolRequest req, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var groupIds = await db.Set<OpsGroup>().Where(g => g.SessionId == id && g.PoolId == req.PoolId).Select(g => g.Id).ToListAsync(ct);
            if (groupIds.Count == 0) return Results.NotFound();
            var pending = await db.Set<OpsPlacement>().Where(p => p.SessionId == id && p.SuggestedGroupId != null && groupIds.Contains(p.SuggestedGroupId.Value)).ToListAsync(ct);
            foreach (var p in pending)
            {
                p.SuggestedGroupId = null;
                p.SuggestionReason = null;
            }
            audit.Record("ops.groups_suggestions_dismissed", "Session", id, $"Dismissed {OpsResults.Plural(pending.Count, "suggested move", "suggested moves")}.");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { Dismissed = pending.Count });
        }).RequireAuthorization(Policies.Cet);
    }

    static Task<int> Occupancy(CampDbContext db, int groupId, CancellationToken ct) =>
        db.Set<OpsPlacement>().CountAsync(p => p.GroupId == groupId
            && db.Registrations.Any(r => r.Id == p.RegistrationId && r.SessionId == p.SessionId && r.Status == RegistrationStatus.Confirmed), ct);
}

internal sealed record Proposal(int GroupId, string Reason);

/// <summary>One pool's groups and campers, with the Auto-suggest rules.</summary>
internal sealed class GroupBoard
{
    public required int PoolId { get; init; }
    public required string PoolName { get; init; }
    public required List<object> Pools { get; init; }
    public required List<OpsGroup> Groups { get; init; }
    public required List<Camper> Campers { get; init; }
    public required List<OpsBuddyRequest> Requests { get; init; }

    public static async Task<GroupBoard?> LoadAsync(CampDbContext db, SessionRoster roster, int? poolId, CancellationToken ct)
    {
        var id = roster.Session.Id;
        var groups = await db.Set<OpsGroup>().AsNoTracking().Where(g => g.SessionId == id).OrderBy(g => g.SortOrder).ToListAsync(ct);
        if (groups.Count == 0) return null;
        var requests = await db.Set<OpsBuddyRequest>().AsNoTracking().Where(r => r.SessionId == id).ToListAsync(ct);
        var pools = roster.Session.Pools.Where(p => groups.Any(g => g.PoolId == p.Id)).OrderBy(p => p.SortOrder).ToList();
        var boards = pools.Select(p => new GroupBoard
        {
            PoolId = p.Id,
            PoolName = p.Name,
            Pools = [],
            Groups = [.. groups.Where(g => g.PoolId == p.Id)],
            Campers = [.. roster.Campers.Where(c => c.PoolId == p.Id)],
            Requests = requests,
        }).ToList();
        var chosen = boards.FirstOrDefault(b => b.PoolId == poolId) ?? boards.FirstOrDefault(b => b.NeedsReview) ?? boards.FirstOrDefault();
        if (chosen is null) return null;
        chosen.Pools.AddRange(boards.Select(b => (object)new { Id = b.PoolId, Name = b.PoolName, Campers = b.Campers.Count, b.NeedsReview }));
        return chosen;
    }

    Dictionary<int, int?> CurrentGroups() => Campers.ToDictionary(c => c.RegistrationId, c => c.Placement?.GroupId is { } g && Groups.Any(x => x.Id == g) ? g : (int?)null);

    /// <summary>Requests where both campers are in this pool (each pair once).</summary>
    IEnumerable<(Camper A, Camper B)> Pairs()
    {
        var byId = Campers.ToDictionary(c => c.RegistrationId);
        var seen = new HashSet<(int, int)>();
        foreach (var r in Requests)
        {
            if (!byId.TryGetValue(r.RegistrationId, out var a) || !byId.TryGetValue(r.RequestedRegistrationId, out var b)) continue;
            var key = (Math.Min(a.RegistrationId, b.RegistrationId), Math.Max(a.RegistrationId, b.RegistrationId));
            if (seen.Add(key)) yield return (a, b);
        }
    }

    bool NeedsReview
    {
        get
        {
            var at = CurrentGroups();
            return at.Values.Any(g => g is null) || Pairs().Any(p => at[p.A.RegistrationId] != at[p.B.RegistrationId]);
        }
    }

    /// <summary>
    /// Proposes moves: first bring separated friends together (into whichever group has room, or by a
    /// swap with a camper who asked for no one), then place unassigned campers with a friend or in the
    /// smallest group. Returns only campers whose group would change.
    /// </summary>
    public Dictionary<int, Proposal> Suggest()
    {
        var at = CurrentGroups();
        var cap = Groups.ToDictionary(g => g.Id, g => g.Capacity);
        int Count(int g) => at.Values.Count(x => x == g);
        var reasons = new Dictionary<int, string>();
        var inPair = Pairs().SelectMany(p => new[] { p.A.RegistrationId, p.B.RegistrationId }).ToHashSet();

        foreach (var (a, b) in Pairs())
        {
            if (at[a.RegistrationId] is not { } ga || at[b.RegistrationId] is not { } gb || ga == gb) continue;
            if (Count(gb) < cap[gb]) Move(a, gb, $"Joins {b.Name}, who they asked to be with.");
            else if (Count(ga) < cap[ga]) Move(b, ga, $"Joins {a.Name}, who asked to be with them.");
            else
            {
                var swap = Campers.Where(c => at[c.RegistrationId] == gb && !inPair.Contains(c.RegistrationId)).OrderBy(c => c.Grade == a.Grade ? 0 : 1).FirstOrDefault();
                if (swap is null) continue;
                Move(a, gb, $"Joins {b.Name}, who they asked to be with.");
                Move(swap, ga, $"Swaps with {a.Name} so {a.FirstName} can join {b.Name}.");
            }
        }
        var byId = Campers.ToDictionary(c => c.RegistrationId);
        foreach (var c in Campers.Where(c => at[c.RegistrationId] is null).OrderBy(c => c.Grade).ThenBy(c => c.LastName))
        {
            var friendGroup = Requests.Where(r => r.RegistrationId == c.RegistrationId || r.RequestedRegistrationId == c.RegistrationId)
                .Select(r => r.RegistrationId == c.RegistrationId ? r.RequestedRegistrationId : r.RegistrationId)
                .Where(byId.ContainsKey).Select(f => at[f]).FirstOrDefault(g => g is { } x && Count(x) < cap[x]);
            if (friendGroup is { } fg) Move(c, fg, "Placed with a camper they asked to be with.");
            else if (Groups.Where(g => Count(g.Id) < g.Capacity).OrderBy(g => Count(g.Id)).ThenBy(g => g.SortOrder).FirstOrDefault() is { } open)
                Move(c, open.Id, $"Not in a group yet; {open.Name} has the most room.");
        }

        return Campers
            .Where(c => at[c.RegistrationId] is { } g && g != (c.Placement?.GroupId ?? 0))
            .ToDictionary(c => c.RegistrationId, c => new Proposal(at[c.RegistrationId]!.Value, reasons[c.RegistrationId]));

        void Move(Camper c, int group, string why)
        {
            at[c.RegistrationId] = group;
            reasons[c.RegistrationId] = why;
        }
    }

    public object ToView()
    {
        var at = CurrentGroups();
        var groupName = Groups.ToDictionary(g => g.Id, g => g.Name);
        string Where(int regId) => at[regId] is { } g ? groupName[g] : "Unassigned";
        var pairs = Pairs().ToList();

        return new
        {
            Pools,
            PoolId,
            PoolName,
            Groups = Groups.Select(g => new { g.Id, g.Name, g.Capacity, Count = at.Values.Count(x => x == g.Id) }),
            Totals = new { Total = Campers.Count, Assigned = at.Values.Count(x => x is not null), Unassigned = at.Values.Count(x => x is null) },
            Suggestions = Campers.Count(c => c.Placement?.SuggestedGroupId is { } s && groupName.ContainsKey(s)),
            Separated = pairs.Where(p => at[p.A.RegistrationId] != at[p.B.RegistrationId]).Select(p => new
            {
                A = new { p.A.RegistrationId, p.A.Name, Group = Where(p.A.RegistrationId) },
                B = new { p.B.RegistrationId, p.B.Name, Group = Where(p.B.RegistrationId) },
            }),
            Campers = Campers.OrderBy(c => c.LastName).ThenBy(c => c.FirstName).Select(c =>
            {
                var friends = pairs.Where(p => p.A == c || p.B == c).Select(p => p.A == c ? p.B : p.A).ToList();
                var apart = friends.Where(f => at[f.RegistrationId] != at[c.RegistrationId]).ToList();
                var suggested = c.Placement?.SuggestedGroupId is { } s && groupName.ContainsKey(s) ? s : (int?)null;
                return new
                {
                    c.RegistrationId,
                    c.Name,
                    c.Grade,
                    GroupId = at[c.RegistrationId],
                    SuggestedGroupId = suggested,
                    SuggestionReason = suggested is null ? null : c.Placement?.SuggestionReason,
                    Requests = friends.Select(f => new { f.RegistrationId, f.Name, Group = Where(f.RegistrationId), Met = at[f.RegistrationId] == at[c.RegistrationId] && at[c.RegistrationId] is not null }),
                    ReviewReason = at[c.RegistrationId] is null ? "Not in a group yet."
                        : apart.Count > 0 ? $"Asked to be with {apart[0].Name}, who is in {Where(apart[0].RegistrationId)}." : null,
                };
            }),
        };
    }
}
