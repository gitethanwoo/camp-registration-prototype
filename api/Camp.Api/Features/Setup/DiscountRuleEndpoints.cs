using System.Text.RegularExpressions;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Camp.Api.Features.StaffCx;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Setup;

public record DiscountRuleInput(string Code, string Name, DiscountKind Kind, int Value, int? ProgramId, int? SessionId,
    DateOnly ValidFrom, DateOnly ValidTo, int? MaxUses, bool Stackable);
public record DiscountPreviewInput(DiscountKind Kind, int Value, int? ProgramId, int? SessionId, DateOnly ValidFrom, DateOnly ValidTo, bool Stackable, int? CodeId, int? RegistrationId);

/// <summary>
/// K5 · Discount rules (FR-62). Admin-authored rules are live when saved. Host and partner codes
/// stay in the C8 approval queue and show here read-only. The guard refuses any rule that, stacked
/// with the other stackable rules it overlaps, could take more than 100% off a session's price.
/// </summary>
public sealed partial class DiscountRuleEndpoints : IEndpointModule
{
    /// <summary>A code as K5 sees it: the code, its rule if an admin wrote one, and its host request if it came through C8.</summary>
    sealed record Loaded(DiscountCode Code, DiscountRule? Rule, DiscountRequest? Request);

    /// <summary>The terms a code is judged by, from its rule or its host request.</summary>
    sealed record Terms(int? ProgramId, int? SessionId, DateOnly? From, DateOnly? To, int? MaxUses, bool Stackable);

    public void Map(IEndpointRouteBuilder app)
    {
        var setup = app.MapGroup("/api/admin/setup").RequireAuthorization(Policies.Admin);

        setup.MapGet("/discount-rules", async (CampDbContext db, CancellationToken ct, TimeProvider clock) =>
        {
            var all = await Load(db, ct);
            var sessions = await SessionPrices(db, ct);
            var programs = await db.Programs.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            var uses = await UsesByCode(db, ct);
            var rows = all.Select(x => ToRow(x, sessions, programs, uses, clock)).ToList();
            return Results.Ok(new
            {
                Counts = new
                {
                    All = rows.Count,
                    Active = rows.Count(r => r.Status == "Active"),
                    Pending = rows.Count(r => r.Status == "Pending approval"),
                    Inactive = rows.Count(r => r.Status == "Inactive"),
                },
                Rows = rows,
                Scopes = sessions.Values.GroupBy(s => s.ProgramId).Select(g => new
                {
                    Id = g.Key,
                    Name = programs[g.Key],
                    Sessions = g.OrderBy(s => s.StartDate).Select(s => new { s.Id, s.Name, s.PriceCents, s.StartDate }),
                }).OrderBy(p => p.Name),
            });
        });

        setup.MapPost("/discount-rules/preview", async (DiscountPreviewInput req, CampDbContext db, CancellationToken ct) =>
            Results.Ok(await Preview(db, req, ct)));

        setup.MapPost("/discount-rules", async (DiscountRuleInput req, CampDbContext db, StaffUser staff, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            var code = req.Code?.Trim().ToUpperInvariant() ?? "";
            var errors = await Validate(db, req, null, ct);
            if (!CodeShape().IsMatch(code)) errors["code"] = ["Codes are 3 to 20 letters and numbers, like SIBLING10."];
            else if (await db.DiscountCodes.AnyAsync(d => d.Code == code, ct)) errors["code"] = [$"{code} already exists. Pick another code."];
            if (errors.Count > 0) return SetupResults.Invalid(errors);

            var discount = new DiscountCode { Code = code, Kind = req.Kind, Value = req.Value, Status = DiscountStatus.Approved, CreatedBy = staff.Name };
            var rule = new DiscountRule
            {
                DiscountCode = discount,
                Name = req.Name.Trim(),
                ProgramId = req.SessionId is null ? req.ProgramId : await ProgramOf(db, req.SessionId.Value, ct),
                SessionId = req.SessionId,
                ValidFrom = req.ValidFrom,
                ValidTo = req.ValidTo,
                MaxUses = req.MaxUses,
                Stackable = req.Stackable,
                CreatedBy = staff.Actor,
                CreatedAt = clock.UtcNow(),
            };
            db.Set<DiscountRule>().Add(rule);
            await db.SaveChangesAsync(ct);
            audit.Record(db, "discount.rule_created", "DiscountCode", discount.Id, $"Created discount rule {code} ({Describe(req.Kind, req.Value)}). Live at checkout from {SetupResults.Date(req.ValidFrom)}.",
                ("Code", null, code), ("Discount", null, Describe(req.Kind, req.Value)), ("Valid dates", null, $"{SetupResults.Date(req.ValidFrom)} – {SetupResults.Date(req.ValidTo)}"),
                ("Usage cap", null, req.MaxUses?.ToString(CultureInfo.InvariantCulture) ?? "No cap"), ("Stacking", null, req.Stackable ? "Stacks" : "Does not stack"));
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { discount.Id });
        });

        setup.MapPut("/discount-rules/{codeId:int}", async (int codeId, DiscountRuleInput req, CampDbContext db, StaffUser staff, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            var discount = await db.DiscountCodes.FirstOrDefaultAsync(d => d.Id == codeId, ct);
            if (discount is null) return Results.NotFound();
            if (await db.Set<DiscountRequest>().AnyAsync(r => r.DiscountCodeId == codeId, ct))
                return SetupResults.Conflict($"{discount.Code} was requested by a host or partner. Review it in Discount approvals.");
            var errors = await Validate(db, req, codeId, ct);
            var rule = await db.Set<DiscountRule>().FirstOrDefaultAsync(r => r.DiscountCodeId == codeId, ct);
            var ordered = (await UsesByCode(db, ct)).GetValueOrDefault(discount.Code);
            var used = rule?.Uses ?? ordered;
            if (req.MaxUses is { } cap && cap < used) errors["maxUses"] = [$"{discount.Code} has already been used {used} times, so the cap can't be lower than {used}."];
            // Transfers reprice a moved camper from the code's current terms, so a used code's type and amount are fixed.
            var timesUsed = Math.Max(used, ordered);
            if (timesUsed > 0 && (req.Kind != discount.Kind || req.Value != discount.Value))
                errors["value"] = [$"{discount.Code} has been used {timesUsed} {(timesUsed == 1 ? "time" : "times")}. Create a new code to change the amount."];
            if (errors.Count > 0) return SetupResults.Invalid(errors);

            var before = (Name: rule?.Name ?? discount.Code, Discount: Describe(discount.Kind, discount.Value), Scope: await ScopeName(db, rule?.ProgramId, rule?.SessionId, ct),
                Dates: rule is null ? "No limit" : $"{SetupResults.Date(rule.ValidFrom)} – {SetupResults.Date(rule.ValidTo)}", Cap: rule?.MaxUses?.ToString(CultureInfo.InvariantCulture) ?? "No cap",
                Stack: rule?.Stackable == true ? "Stacks" : "Does not stack");
            if (rule is null)
            {
                // A code from before rules existed gets its first rule; its past uses count toward the cap.
                rule = new DiscountRule { DiscountCodeId = codeId, Uses = used, CreatedBy = staff.Actor, CreatedAt = clock.UtcNow() };
                db.Set<DiscountRule>().Add(rule);
            }
            discount.Kind = req.Kind;
            discount.Value = req.Value;
            rule.Name = req.Name.Trim();
            rule.ProgramId = req.SessionId is null ? req.ProgramId : await ProgramOf(db, req.SessionId.Value, ct);
            rule.SessionId = req.SessionId;
            rule.ValidFrom = req.ValidFrom;
            rule.ValidTo = req.ValidTo;
            rule.MaxUses = req.MaxUses;
            rule.Stackable = req.Stackable;
            audit.Record(db, "discount.rule_changed", "DiscountCode", codeId, $"Changed discount rule {discount.Code}. Orders already placed keep their discount.",
                ("Name", before.Name, rule.Name), ("Discount", before.Discount, Describe(req.Kind, req.Value)),
                ("Scope", before.Scope, await ScopeName(db, rule.ProgramId, rule.SessionId, ct)),
                ("Valid dates", before.Dates, $"{SetupResults.Date(rule.ValidFrom)} – {SetupResults.Date(rule.ValidTo)}"),
                ("Usage cap", before.Cap, rule.MaxUses?.ToString(CultureInfo.InvariantCulture) ?? "No cap"),
                ("Stacking", before.Stack, rule.Stackable ? "Stacks" : "Does not stack"));
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        setup.MapPost("/discount-rules/{codeId:int}/{action:regex(^(activate|deactivate)$)}", async (int codeId, string action, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var rule = await db.Set<DiscountRule>().Include(r => r.DiscountCode).FirstOrDefaultAsync(r => r.DiscountCodeId == codeId, ct);
            if (rule is null) return SetupResults.Conflict("Only codes with a discount rule can be turned on or off here.");
            var on = action == "activate";
            if (rule.Active == on) return Results.Ok();
            if (on)
            {
                var clash = await StackGuard(db, rule.DiscountCode.Kind, rule.DiscountCode.Value, rule.ProgramId, rule.SessionId, rule.ValidFrom, rule.ValidTo, rule.Stackable, codeId, ct);
                if (clash is not null) return SetupResults.Invalid("value", clash);
            }
            rule.Active = on;
            audit.Record(db, on ? "discount.rule_activated" : "discount.rule_deactivated", "DiscountCode", codeId,
                $"Turned {(on ? "on" : "off")} {rule.DiscountCode.Code}.", ("Status", on ? "Inactive" : "Active", on ? "Active" : "Inactive"));
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });
    }

    [GeneratedRegex("^[A-Z0-9]{3,20}$")]
    private static partial Regex CodeShape();

    static async Task<List<Loaded>> Load(CampDbContext db, CancellationToken ct)
    {
        var codes = await db.DiscountCodes.AsNoTracking().OrderBy(d => d.Id).ToListAsync(ct);
        var rules = await db.Set<DiscountRule>().AsNoTracking().ToDictionaryAsync(r => r.DiscountCodeId, ct);
        var requests = await db.Set<DiscountRequest>().AsNoTracking().ToDictionaryAsync(r => r.DiscountCodeId, ct);
        return [.. codes.Select(c => new Loaded(c, rules.GetValueOrDefault(c.Id), requests.GetValueOrDefault(c.Id)))];
    }

    static Terms TermsOf(Loaded x) => x.Rule is { } r
        ? new Terms(r.ProgramId, r.SessionId, r.ValidFrom, r.ValidTo, r.MaxUses, r.Stackable)
        : x.Request is { } q ? new Terms(q.ProgramId, null, q.ValidFrom, q.ValidTo, q.MaxUses, q.Stackable)
        : new Terms(null, null, null, null, null, false);

    static Task<Dictionary<int, Session>> SessionPrices(CampDbContext db, CancellationToken ct) =>
        db.Sessions.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);

    /// <summary>Orders that used each code, for codes whose uses aren't counted by a rule.</summary>
    static Task<Dictionary<string, int>> UsesByCode(CampDbContext db, CancellationToken ct) =>
        db.Orders.Where(o => o.DiscountCode != null && (o.Status == OrderStatus.Paid || o.Status == OrderStatus.Pending))
            .GroupBy(o => o.DiscountCode!).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    static async Task<int?> ProgramOf(CampDbContext db, int sessionId, CancellationToken ct) =>
        await db.Sessions.Where(s => s.Id == sessionId).Select(s => (int?)s.ProgramId).FirstOrDefaultAsync(ct);

    /// <summary>"Active", "Pending approval" or "Inactive", with the reason in words.</summary>
    static (string Status, string Reason) StatusOf(Loaded x, int uses, TimeProvider clock)
    {
        var today = clock.Today();
        if (x.Request is { Decision: ReviewDecision.Rejected } rejected) return ("Inactive", $"Rejected by {rejected.ReviewedBy} in Discount approvals.");
        if (x.Code.Status == DiscountStatus.PendingApproval)
            return ("Pending approval", $"Requested by {x.Request?.RequestedBy ?? x.Code.CreatedBy}. It reads as an invalid code to families until it's approved in Discount approvals.");
        if (x.Rule is not { } r) return ("Active", "No rule limits this code yet. Edit it to add dates, scope or a cap.");
        if (!r.Active) return ("Inactive", "Turned off.");
        if (today < r.ValidFrom) return ("Inactive", $"Starts {SetupResults.Date(r.ValidFrom)}.");
        if (today > r.ValidTo) return ("Inactive", $"Ended {SetupResults.Date(r.ValidTo)}.");
        if (r.MaxUses is { } cap && uses >= cap) return ("Inactive", $"Used {uses} of {cap} times; the cap is reached.");
        return ("Active", $"Works at checkout until {SetupResults.Date(r.ValidTo)}.");
    }

    sealed record Row(int Id, string Code, string Name, string Kind, int Value, string Description, int? ProgramId, int? SessionId, string Scope,
        DateOnly? ValidFrom, DateOnly? ValidTo, int? MaxUses, int Uses, bool Stackable, string Status, string StatusReason, string Source, bool Editable, bool HasRule, bool Active);

    static Row ToRow(Loaded x, Dictionary<int, Session> sessions, Dictionary<int, string> programs, Dictionary<string, int> usesByCode, TimeProvider clock)
    {
        var t = TermsOf(x);
        var uses = x.Rule?.Uses ?? usesByCode.GetValueOrDefault(x.Code.Code);
        var (status, reason) = StatusOf(x, uses, clock);
        var scope = t.SessionId is { } sid && sessions.TryGetValue(sid, out var s)
            ? $"{programs.GetValueOrDefault(s.ProgramId)} · {s.Name}"
            : t.ProgramId is { } pid ? $"{programs.GetValueOrDefault(pid)}, all sessions" : "All programs";
        var source = x.Request is not null ? $"{x.Request.RequesterType} request" : x.Rule is not null ? "Admin rule" : "Admin code";
        return new Row(x.Code.Id, x.Code.Code, x.Rule?.Name ?? x.Code.Code, x.Code.Kind.ToString(), x.Code.Value, Describe(x.Code.Kind, x.Code.Value),
            t.ProgramId, t.SessionId, scope, t.From, t.To, t.MaxUses, uses, t.Stackable, status, reason, source,
            Editable: x.Request is null, HasRule: x.Rule is not null, Active: x.Rule?.Active ?? true);
    }

    /// <summary>A scope in the words K5's list uses: "Day Camp · Atlanta · June week", "Day Camp, all sessions" or "All programs".</summary>
    static async Task<string> ScopeName(CampDbContext db, int? programId, int? sessionId, CancellationToken ct)
    {
        if (sessionId is { } sid)
            return await db.Sessions.Where(s => s.Id == sid).Select(s => s.Program.Name + " · " + s.Name).FirstOrDefaultAsync(ct) ?? "All programs";
        if (programId is { } pid)
            return (await db.Programs.Where(p => p.Id == pid).Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "") + ", all sessions";
        return "All programs";
    }

    static string Describe(DiscountKind kind, int value) => kind == DiscountKind.Percent ? $"{value}% off" : $"{SetupResults.Money(value)} off per camper";

    static int Off(DiscountKind kind, int value, int priceCents) => kind == DiscountKind.Percent
        ? Math.Min((int)Math.Round(priceCents * Math.Min(value, 100) / 100m), priceCents)
        : Math.Min(value, priceCents);

    static async Task<Dictionary<string, string[]>> Validate(CampDbContext db, DiscountRuleInput req, int? selfId, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(req.Name)) errors["name"] = ["Name the rule so staff recognize it, like \"Sibling discount\"."];
        else if (req.Name.Trim().Length > 80) errors["name"] = ["Names are limited to 80 characters."];
        if (!Enum.IsDefined(req.Kind)) errors["kind"] = ["Choose percentage or flat amount."];
        else if (req.Kind == DiscountKind.Percent && req.Value is < 1 or > 100) errors["value"] = ["A percentage discount is between 1% and 100%."];
        else if (req.Kind == DiscountKind.Flat && req.Value < 100) errors["value"] = ["A flat discount is at least $1."];
        if (req.ValidTo < req.ValidFrom) errors["validTo"] = ["The end date can't be before the start date."];
        if (req.MaxUses is < 1) errors["maxUses"] = ["Leave the cap empty for no limit, or enter at least 1."];
        if (req.SessionId is { } sid && !await db.Sessions.AnyAsync(s => s.Id == sid, ct)) errors["sessionId"] = ["That session doesn't exist."];
        if (req.SessionId is null && req.ProgramId is { } pid && !await db.Programs.AnyAsync(p => p.Id == pid, ct)) errors["programId"] = ["That program doesn't exist."];
        if (errors.Count > 0) return errors;

        var clash = await StackGuard(db, req.Kind, req.Value, req.SessionId is null ? req.ProgramId : await ProgramOf(db, req.SessionId.Value, ct), req.SessionId,
            req.ValidFrom, req.ValidTo, req.Stackable, selfId, ct);
        if (clash is not null) errors["value"] = [clash];
        return errors;
    }

    /// <summary>The sessions a scope covers.</summary>
    static IEnumerable<Session> InScope(IEnumerable<Session> sessions, int? programId, int? sessionId) =>
        sessions.Where(s => sessionId is { } id ? s.Id == id : programId is null || s.ProgramId == programId);

    /// <summary>
    /// The combination guard. A non-stacking rule can't exceed 100% by itself. A stacking rule, added
    /// to every other approved, switched-on stacking code whose scope and dates overlap, can't take
    /// more than a session's price. Returns the reason in words, or null when the rule is safe.
    /// </summary>
    static async Task<string?> StackGuard(CampDbContext db, DiscountKind kind, int value, int? programId, int? sessionId, DateOnly from, DateOnly to,
        bool stackable, int? selfId, CancellationToken ct)
    {
        var sessions = (await SessionPrices(db, ct)).Values.ToList();
        var scope = InScope(sessions, programId, sessionId).ToList();
        foreach (var s in scope)
        {
            var own = kind == DiscountKind.Percent ? (long)s.PriceCents * value / 100 : value;
            if (own > s.PriceCents) return $"{Describe(kind, value)} is more than the {SetupResults.Money(s.PriceCents)} price of {s.Name}. Discounts can't exceed 100% of the price.";
        }
        if (!stackable) return null;

        var others = (await Load(db, ct)).Where(x => x.Code.Id != selfId && x.Code.Status == DiscountStatus.Approved && (x.Rule?.Active ?? true))
            .Select(x => (x.Code, Terms: TermsOf(x))).Where(x => x.Terms.Stackable)
            .Where(x => (x.Terms.From ?? DateOnly.MinValue) <= to && from <= (x.Terms.To ?? DateOnly.MaxValue)).ToList();
        foreach (var s in scope)
        {
            var overlapping = others.Where(o => InScope([s], o.Terms.ProgramId, o.Terms.SessionId).Any()).ToList();
            if (overlapping.Count == 0) continue;
            var total = Off(kind, value, s.PriceCents) + overlapping.Sum(o => (long)Off(o.Code.Kind, o.Code.Value, s.PriceCents));
            var raw = (kind == DiscountKind.Percent ? (long)s.PriceCents * value / 100 : value) + overlapping.Sum(o => o.Code.Kind == DiscountKind.Percent ? (long)s.PriceCents * o.Code.Value / 100 : o.Code.Value);
            if (Math.Max(total, raw) > s.PriceCents)
                return $"Stacked with {string.Join(" and ", overlapping.Select(o => o.Code.Code))}, this takes {SetupResults.Money((int)raw)} off the {SetupResults.Money(s.PriceCents)} price of {s.Name}. Combined discounts can't exceed 100%. Turn off stacking or lower the value.";
        }
        return null;
    }

    static async Task<object> Preview(CampDbContext db, DiscountPreviewInput req, CancellationToken ct)
    {
        var programId = req.SessionId is { } sid ? await ProgramOf(db, sid, ct) : req.ProgramId;
        var campers = await ProgramSetupEndpoints.ActiveRegistrations(db)
            .Where(r => req.SessionId != null ? r.SessionId == req.SessionId : programId == null || r.Session.ProgramId == programId)
            .OrderBy(r => r.Person.FirstName).ThenBy(r => r.Person.LastName).Take(40)
            .Select(r => new { RegistrationId = r.Id, Name = r.Person.FirstName + " " + r.Person.LastName, Session = r.Session.Program.Name + " · " + r.Session.Name, r.Session.PriceCents })
            .ToListAsync(ct);
        var camper = campers.FirstOrDefault(c => c.RegistrationId == req.RegistrationId) ?? campers.FirstOrDefault();
        var valueOk = req.Kind == DiscountKind.Percent ? req.Value is >= 1 and <= 100 : req.Value >= 1;
        var guard = valueOk ? await StackGuard(db, req.Kind, req.Value, programId, req.SessionId, req.ValidFrom, req.ValidTo, req.Stackable, req.CodeId, ct) : null;
        var off = camper is null || !valueOk ? 0 : Off(req.Kind, req.Value, camper.PriceCents);
        return new
        {
            Campers = campers.Select(c => new { c.RegistrationId, c.Name, c.Session }),
            Camper = camper is null ? null : new
            {
                camper.RegistrationId,
                camper.Name,
                FirstName = camper.Name.Split(' ')[0],
                camper.Session,
                camper.PriceCents,
                DiscountCents = off,
                FinalCents = camper.PriceCents - off,
                Percent = camper.PriceCents == 0 ? 0 : Math.Round(off * 100m / camper.PriceCents, 1),
            },
            Blocked = guard is not null,
            Guard = guard,
        };
    }
}
