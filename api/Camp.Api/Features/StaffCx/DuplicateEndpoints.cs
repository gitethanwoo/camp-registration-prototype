using System.Text.Json;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.StaffCx;

public record MergeRequest(string Survivor, Dictionary<string, string> Fields, Dictionary<string, string>? Resolutions);

/// <summary>C9 · Duplicate review and merge (FR-7). Never one-click: fields and conflicts are chosen explicitly.</summary>
public sealed class DuplicateEndpoints : IEndpointModule
{
    static readonly string[] FieldKeys = ["name", "email", "phone", "city"];

    public void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/duplicates").RequireAuthorization(Policies.Cet);

        admin.MapGet("", async (CampDbContext db, CancellationToken ct) =>
        {
            var pairs = await DuplicateDetector.FindAsync(db, null, ct);
            var ids = pairs.SelectMany(p => new[] { p.HouseholdA, p.HouseholdB }).Distinct().ToList();
            var households = await db.Households.AsNoTracking().Where(h => ids.Contains(h.Id)).Include(h => h.Members).ToDictionaryAsync(h => h.Id, ct);
            var rows = new List<object>();
            foreach (var p in pairs)
            {
                var snap = await Snapshot(db, p.HouseholdA, p.HouseholdB, ct);
                rows.Add(new
                {
                    p.HouseholdA,
                    p.HouseholdB,
                    p.Reason,
                    p.SharedPeople,
                    A = Summary(households[p.HouseholdA]),
                    B = Summary(households[p.HouseholdB]),
                    Conflicts = snap.Conflicts.Count,
                });
            }
            return Results.Ok(rows);
        });

        admin.MapGet("/{a:int}/{b:int}", async (int a, int b, CampDbContext db, CancellationToken ct) =>
        {
            var pair = await FindPair(db, a, b, ct);
            if (pair is null) return Results.NotFound(new { error = "These two accounts aren't flagged as duplicates, or one was already merged." });
            var snap = await Snapshot(db, pair.HouseholdA, pair.HouseholdB, ct);
            return Results.Ok(new
            {
                pair.Reason,
                pair.SharedPeople,
                A = Account(snap.A, snap.HoldingsA),
                B = Account(snap.B, snap.HoldingsB),
                Fields = FieldKeys.Select(k => new { Key = k, Label = FieldLabel(k), A = FieldValue(snap.A, k), B = FieldValue(snap.B, k) }),
                snap.Conflicts,
                Salesforce = new { A = snap.A.SalesforceId, B = snap.B.SalesforceId },
            });
        });

        admin.MapPost("/{a:int}/{b:int}/merge", async (int a, int b, MergeRequest req, CampDbContext db, StaffUser staff, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            if (req.Survivor is not ("a" or "b")) return StaffCx.Invalid("survivor", "Choose which account survives.");
            foreach (var k in FieldKeys)
                if (!req.Fields.TryGetValue(k, out var pick) || pick is not ("a" or "b"))
                    return StaffCx.Invalid($"fields.{k}", $"Choose which {FieldLabel(k).ToLowerInvariant()} to keep.");

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Lock both accounts (lower id first), then check the pair inside the transaction. A merge racing
            // this one, in either direction, waits on the lock and then finds an account already merged away.
            var (lo, hi) = a < b ? (a, b) : (b, a);
            await db.Database.ExecuteSqlAsync($"SELECT Id FROM Households WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE Id IN ({lo}, {hi}) ORDER BY Id", ct);
            var pair = await FindPair(db, a, b, ct);
            if (pair is null) return StaffCx.Conflict("These two accounts aren't flagged as duplicates, or one was already merged.");
            var snap = await Snapshot(db, pair.HouseholdA, pair.HouseholdB, ct, tracking: true);
            var resolutions = req.Resolutions ?? [];
            foreach (var c in snap.Conflicts)
            {
                if (c.Options.Count == 0) return StaffCx.Conflict($"{c.Person}: {c.Summary} Cancel one registration from its registration page, then merge.");
                if (!resolutions.TryGetValue(c.Key, out var choice) || c.Options.All(o => o.Value != choice))
                    return StaffCx.Invalid($"resolutions.{c.Key}", $"Choose how to resolve {c.Person}'s {c.Session} conflict before merging.");
            }

            var (survivor, other) = req.Survivor == "a" ? (snap.A, snap.B) : (snap.B, snap.A);
            var before = new { A = Values(snap.A), B = Values(snap.B) };

            // 1. Conflicts first, while each holding still belongs to its own account.
            var resolved = new List<string>();
            foreach (var c in snap.Conflicts)
            {
                var drop = c.Options.Single(o => o.Value == resolutions[c.Key]).DropsWaitlistEntryId;
                var entry = await db.WaitlistEntries.Include(w => w.Person).Include(w => w.Pool).SingleAsync(w => w.Id == drop, ct);
                if (entry.Status == WaitlistStatus.Offered)
                    await db.CapacityPools.Where(p => p.Id == entry.PoolId && p.Reserved > 0).ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved - 1), ct);
                entry.Status = WaitlistStatus.Removed;
                audit.Record("waitlist.removed", "WaitlistEntry", entry.Id, $"Removed {entry.Person.FullName} (#{entry.Position}, {entry.Pool.Name}) while merging duplicate accounts; kept the other account's {c.Session} holding.");
                resolved.Add($"{c.Person} · {c.Session}: {c.Options.Single(o => o.Value == resolutions[c.Key]).Label}");
            }

            // 2. Contact fields, picked one by one.
            foreach (var k in FieldKeys)
                SetField(survivor, k, FieldValue(req.Fields[k] == "a" ? snap.A : snap.B, k));
            // The archived account can't keep a sign-in email that now belongs to the survivor.
            other.Email = $"merged-into-{survivor.Id}:{other.Email}";

            // 3. People: the same person (name + DOB) keeps the survivor's record; everyone else moves over.
            var survivorHasPrimary = survivor.Members.Any(m => m.Role == "Primary");
            foreach (var person in other.Members.ToList())
            {
                var twin = survivor.Members.FirstOrDefault(m => m.FirstName == person.FirstName && m.LastName == person.LastName && m.DateOfBirth == person.DateOfBirth);
                if (twin is null)
                {
                    person.HouseholdId = survivor.Id;
                    if (person.IsAdult && person.Role == "Primary" && survivorHasPrimary) person.Role = "Co-owner";
                    continue;
                }
                await db.Registrations.Where(r => r.PersonId == person.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.PersonId, twin.Id), ct);
                await db.WaitlistEntries.Where(w => w.PersonId == person.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.PersonId, twin.Id), ct);
                twin.Email ??= person.Email;
                twin.Allergies ??= person.Allergies;
                twin.Dietary ??= person.Dietary;
                twin.AdaNeeds ??= person.AdaNeeds;
            }

            // 4. Everything the other account owned now belongs to the survivor.
            await db.Orders.Where(o => o.HouseholdId == other.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.HouseholdId, survivor.Id), ct);
            await db.Registrations.Where(r => r.HouseholdId == other.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.HouseholdId, survivor.Id), ct);
            await db.WaitlistEntries.Where(w => w.HouseholdId == other.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.HouseholdId, survivor.Id), ct);
            await db.Set<TransferRequest>().Where(t => t.HouseholdId == other.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.HouseholdId, survivor.Id), ct);
            await db.Set<HouseholdNote>().Where(n => n.HouseholdId == other.Id).ExecuteUpdateAsync(s => s.SetProperty(n => n.HouseholdId, survivor.Id), ct);

            db.Set<HouseholdMerge>().Add(new HouseholdMerge
            {
                SurvivorHouseholdId = survivor.Id,
                MergedHouseholdId = other.Id,
                DetailJson = JsonSerializer.Serialize(new { req.Survivor, req.Fields, Resolutions = resolved, Before = before }),
                Actor = staff.Actor,
                CreatedAt = clock.UtcNow(),
            });
            var fieldSummary = string.Join(", ", FieldKeys.Select(k => $"{FieldLabel(k).ToLowerInvariant()} from {(req.Fields[k] == req.Survivor ? "this account" : $"#{other.Id}")}"));
            audit.Record("household.merged", "Household", survivor.Id,
                $"Merged household #{other.Id} ({other.Name}) into this one. Kept {fieldSummary}.{(resolved.Count > 0 ? " Conflicts: " + string.Join("; ", resolved) + "." : "")}");
            audit.Record("household.merged_away", "Household", other.Id, $"Merged into household #{survivor.Id} by {staff.Actor}.");
            // Salesforce merges the two constituents downstream; the survivor's record id wins.
            db.OutboxEvents.Add(new OutboxEvent
            {
                Type = "ConstituentMerge",
                Target = "Salesforce",
                AggregateId = survivor.Id.ToString(CultureInfo.InvariantCulture),
                PayloadJson = JsonSerializer.Serialize(new { survivor = survivor.SalesforceId, merged = other.SalesforceId, survivorHouseholdId = survivor.Id, mergedHouseholdId = other.Id }),
                CreatedAt = clock.UtcNow(),
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { SurvivorHouseholdId = survivor.Id });
        });
    }

    // ── model ──

    public sealed record Holding(string Kind, int Id, int PersonId, string Person, int SessionId, string Session, string Detail, string Status, int? Position);
    public sealed record ConflictOption(string Value, string Label, int DropsWaitlistEntryId);
    public sealed record Conflict(string Key, string Person, string Session, string Summary, string A, string B, List<ConflictOption> Options);
    sealed record Snap(Household A, Household B, List<Holding> HoldingsA, List<Holding> HoldingsB, List<Conflict> Conflicts);

    static async Task<DuplicatePair?> FindPair(CampDbContext db, int a, int b, CancellationToken ct)
    {
        var (lo, hi) = a < b ? (a, b) : (b, a);
        return (await DuplicateDetector.FindAsync(db, lo, ct)).FirstOrDefault(p => p.HouseholdA == lo && p.HouseholdB == hi);
    }

    static async Task<Snap> Snapshot(CampDbContext db, int a, int b, CancellationToken ct, bool tracking = false)
    {
        var q = tracking ? db.Households : db.Households.AsNoTracking();
        var ha = await q.Include(h => h.Members).SingleAsync(h => h.Id == a, ct);
        var hb = await q.Include(h => h.Members).SingleAsync(h => h.Id == b, ct);
        var holdingsA = await Holdings(db, a, ct);
        var holdingsB = await Holdings(db, b, ct);

        var conflicts = new List<Conflict>();
        foreach (var pa in ha.Members)
        {
            var pb = hb.Members.FirstOrDefault(m => m.FirstName == pa.FirstName && m.LastName == pa.LastName && m.DateOfBirth == pa.DateOfBirth);
            if (pb is null) continue;
            foreach (var x in holdingsA.Where(h => h.PersonId == pa.Id))
            {
                var y = holdingsB.FirstOrDefault(h => h.PersonId == pb.Id && h.SessionId == x.SessionId);
                if (y is null) continue;
                conflicts.Add(Resolve(pa.FullName, x, y));
            }
        }
        return new Snap(ha, hb, holdingsA, holdingsB, conflicts);
    }

    /// <summary>One person, one session, a holding on each account: say what's at stake and what can be kept.</summary>
    static Conflict Resolve(string person, Holding a, Holding b)
    {
        var key = $"{a.PersonId}-{a.SessionId}";
        var options = new List<ConflictOption>();
        string summary;
        if (a.Kind == "registration" && b.Kind == "registration")
            summary = "Both accounts hold a registration for this session, and a person can only be registered once.";
        else if (a.Kind == "registration" || b.Kind == "registration")
        {
            var (reg, wait, regSide, waitSide) = a.Kind == "registration" ? (a, b, "A", "B") : (b, a, "B", "A");
            summary = $"Account {regSide} is registered ({reg.Detail}) and Account {waitSide} is waitlisted (#{wait.Position}) for the same session.";
            options.Add(new("keep-registration", $"Keep Account {regSide}'s registration and payment plan; release Account {waitSide}'s waitlist spot (#{wait.Position})", wait.Id));
        }
        else
        {
            summary = "Both accounts are on this session's waitlist.";
            var (keep, drop) = a.Position <= b.Position ? (a, b) : (b, a);
            options.Add(new("keep-earlier", $"Keep the earlier spot (#{keep.Position}); release #{drop.Position}", drop.Id));
        }
        return new Conflict(key, person, a.Session, summary, $"{a.Status} · {a.Detail}", $"{b.Status} · {b.Detail}", options);
    }

    static async Task<List<Holding>> Holdings(CampDbContext db, int householdId, CancellationToken ct)
    {
        var regs = await db.Registrations.AsNoTracking()
            .Where(r => r.HouseholdId == householdId && r.Status != RegistrationStatus.Cancelled && r.Order!.Status != OrderStatus.Declined)
            .Include(r => r.Person).Include(r => r.Pool).Include(r => r.Session).ThenInclude(s => s.Program)
            .Include(r => r.Order).ThenInclude(o => o!.Installments)
            .ToListAsync(ct);
        var waits = await db.WaitlistEntries.AsNoTracking()
            .Where(w => w.HouseholdId == householdId && (w.Status == WaitlistStatus.Waiting || w.Status == WaitlistStatus.Offered))
            .Include(w => w.Person).Include(w => w.Pool).ThenInclude(p => p.Session).ThenInclude(s => s.Program)
            .ToListAsync(ct);
        return regs.Select(r =>
        {
            var payment = StaffCx.PaymentState(r.Status, r.BalanceCents, r.PaidCents, r.Order?.Installments.Select(i => i.Status) ?? []);
            var detail = $"{r.Pool.Name} · {payment}{(r.BalanceCents > 0 ? $", {StaffCx.Money(r.BalanceCents)} balance" : "")}";
            return new Holding("registration", r.Id, r.PersonId, r.Person.FullName, r.SessionId, $"{r.Session.Program.Name} · {StaffCx.Dates(r.Session.StartDate, r.Session.EndDate)}", detail, TransferService.Label(r.Status), null);
        }).Concat(waits.Select(w => new Holding("waitlist", w.Id, w.PersonId, w.Person.FullName, w.Pool.SessionId,
            $"{w.Pool.Session.Program.Name} · {StaffCx.Dates(w.Pool.Session.StartDate, w.Pool.Session.EndDate)}", $"{w.Pool.Name} · #{w.Position} in line",
            w.Status == WaitlistStatus.Offered ? "Offered spot" : "Waitlisted", w.Position))).ToList();
    }

    static object Summary(Household h) => new { h.Id, h.Name, h.Email, h.Phone, Members = h.Members.Select(m => m.FullName) };

    static object Account(Household h, List<Holding> holdings) => new
    {
        h.Id,
        h.Name,
        h.Email,
        h.Phone,
        h.City,
        h.SalesforceId,
        Members = h.Members.OrderByDescending(m => m.IsAdult).Select(m => new { m.Id, Name = m.FullName, m.IsAdult, m.Role, m.DateOfBirth }),
        Holdings = holdings,
    };

    static object Values(Household h) => new { h.Id, h.Name, h.Email, h.Phone, h.City, h.SalesforceId };

    static string FieldLabel(string key) => key switch
    {
        "name" => "Household name",
        "email" => "Email",
        "phone" => "Phone",
        _ => "City",
    };

    static string FieldValue(Household h, string key) => key switch
    {
        "name" => h.Name,
        "email" => h.Email.StartsWith("merged-into-", StringComparison.Ordinal) ? h.Email[(h.Email.IndexOf(':', StringComparison.Ordinal) + 1)..] : h.Email,
        "phone" => h.Phone,
        _ => h.City,
    };

    static void SetField(Household h, string key, string value)
    {
        switch (key)
        {
            case "name": h.Name = value; break;
            case "email": h.Email = value; break;
            case "phone": h.Phone = value; break;
            default: h.City = value; break;
        }
    }
}
