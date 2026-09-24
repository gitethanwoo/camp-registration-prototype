using System.Text.Json;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.StaffCx;

public record DiscountDecisionRequest(string? Note);

/// <summary>
/// C8 · Discount approval queue (FR-62, FR-63). Host- and partner-requested codes are inert until
/// approved. Codes worth more than <see cref="ThresholdPercent"/>% of the session price need Finance.
/// </summary>
public sealed class DiscountEndpoints : IEndpointModule
{
    public const int ThresholdPercent = 10;

    public void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/discounts").RequireAuthorization(Policies.Staff);

        admin.MapGet("", async (CampDbContext db, StaffUser staff, string? status, CancellationToken ct) =>
        {
            var all = await Load(db).ToListAsync(ct);
            var prices = await PriceByProgram(db, ct);
            var filter = status?.ToLowerInvariant() switch
            {
                "approved" => (ReviewDecision?)ReviewDecision.Approved,
                "rejected" => ReviewDecision.Rejected,
                "all" => null,
                _ => ReviewDecision.Pending,
            };
            return Results.Ok(new
            {
                Counts = new
                {
                    Pending = all.Count(r => r.Decision == ReviewDecision.Pending),
                    Approved = all.Count(r => r.Decision == ReviewDecision.Approved),
                    Rejected = all.Count(r => r.Decision == ReviewDecision.Rejected),
                    All = all.Count,
                },
                ThresholdPercent,
                CanApproveOverThreshold = IsFinance(staff),
                Rows = all.Where(r => filter is null || r.Decision == filter)
                    .OrderBy(r => r.Decision != ReviewDecision.Pending).ThenByDescending(r => r.RequestedAt)
                    .Select(r => ToRow(r, prices.GetValueOrDefault(r.ProgramId))),
            });
        });

        admin.MapPost("/{id:int}/approve", async (int id, DiscountDecisionRequest req, CampDbContext db, StaffUser staff, IAuditLog audit, CancellationToken ct) =>
        {
            var r = await db.Set<DiscountRequest>().Include(x => x.DiscountCode).Include(x => x.Program).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            if (r.Decision != ReviewDecision.Pending) return StaffCx.Conflict($"{r.DiscountCode.Code} was already {r.Decision.ToString().ToLowerInvariant()} by {r.ReviewedBy}.");
            var price = (await PriceByProgram(db, ct)).GetValueOrDefault(r.ProgramId);
            var percent = PercentOfPrice(r.DiscountCode, price);
            var over = percent > ThresholdPercent;
            var note = req.Note?.Trim();
            if (over && !IsFinance(staff))
                return StaffCx.Forbidden($"{r.DiscountCode.Code} is over the {ThresholdPercent}% threshold, so Finance approves it. Leave a note for Finance, or reject it.");
            if (over && string.IsNullOrEmpty(note))
                return StaffCx.Invalid("note", "This code is over the threshold. Add a note saying why you're approving it.");
            if (note?.Length > 1000) return StaffCx.Invalid("note", "Notes are limited to 1,000 characters.");

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (!await Decide(db, id, ReviewDecision.Approved, staff.Actor, note, ct)) return await AlreadyDecided(db, id, ct);
            r.DiscountCode.Status = DiscountStatus.Approved; // live at checkout from now on
            audit.Record("discount.approved", "DiscountCode", r.DiscountCodeId,
                $"Approved {r.DiscountCode.Code} ({Describe(r.DiscountCode)}) for {r.Organization}.{(note is null ? "" : $" Note: {note}")}");
            db.OutboxEvents.Add(new OutboxEvent { Type = "DiscountApproved", Target = "HubSpot", AggregateId = "discount-" + r.DiscountCodeId, PayloadJson = JsonSerializer.Serialize(new { r.DiscountCode.Code, r.RequestedBy, r.Organization }), CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok();
        });

        admin.MapPost("/{id:int}/reject", async (int id, DiscountDecisionRequest req, CampDbContext db, StaffUser staff, IAuditLog audit, CancellationToken ct) =>
        {
            var r = await db.Set<DiscountRequest>().Include(x => x.DiscountCode).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            if (r.Decision != ReviewDecision.Pending) return StaffCx.Conflict($"{r.DiscountCode.Code} was already {r.Decision.ToString().ToLowerInvariant()} by {r.ReviewedBy}.");
            var note = req.Note?.Trim();
            if (string.IsNullOrEmpty(note)) return StaffCx.Invalid("note", "Add a note for the requester saying why it was rejected.");
            if (note.Length > 1000) return StaffCx.Invalid("note", "Notes are limited to 1,000 characters.");

            // The code keeps its PendingApproval status: inert, and indistinguishable from an invalid code to guests.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (!await Decide(db, id, ReviewDecision.Rejected, staff.Actor, note, ct)) return await AlreadyDecided(db, id, ct);
            audit.Record("discount.rejected", "DiscountCode", r.DiscountCodeId, $"Rejected {r.DiscountCode.Code} for {r.Organization}. Note: {note}");
            db.OutboxEvents.Add(new OutboxEvent { Type = "DiscountRejected", Target = "HubSpot", AggregateId = "discount-" + r.DiscountCodeId, PayloadJson = JsonSerializer.Serialize(new { r.DiscountCode.Code, r.RequestedBy, note }), CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok();
        });
    }

    /// <summary>
    /// Records the decision as a conditional update, the first write in the caller's transaction. Of two
    /// decisions racing, the second blocks on the row lock and then changes nothing, so it writes no audit
    /// row or outbox event. Returns false when the request was no longer pending.
    /// </summary>
    static async Task<bool> Decide(CampDbContext db, int id, ReviewDecision decision, string actor, string? note, CancellationToken ct)
    {
        var now = (DateTime?)DateTime.UtcNow;
        return await db.Set<DiscountRequest>().Where(x => x.Id == id && x.Decision == ReviewDecision.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Decision, decision)
                .SetProperty(x => x.ReviewedBy, actor)
                .SetProperty(x => x.ReviewedAt, now)
                .SetProperty(x => x.ReviewNote, note), ct) == 1;
    }

    static async Task<IResult> AlreadyDecided(CampDbContext db, int id, CancellationToken ct)
    {
        var r = await db.Set<DiscountRequest>().AsNoTracking().Include(x => x.DiscountCode).SingleAsync(x => x.Id == id, ct);
        return StaffCx.Conflict($"{r.DiscountCode.Code} was already {r.Decision.ToString().ToLowerInvariant()} by {r.ReviewedBy}.");
    }

    static bool IsFinance(StaffUser staff) => staff.Role is "finance" or "admin";

    static IQueryable<DiscountRequest> Load(CampDbContext db) =>
        db.Set<DiscountRequest>().AsNoTracking().Include(r => r.DiscountCode).Include(r => r.Program);

    /// <summary>The lowest session price per program: the threshold is judged against the cheapest session the code touches.</summary>
    static Task<Dictionary<int, int>> PriceByProgram(CampDbContext db, CancellationToken ct) =>
        db.Sessions.GroupBy(s => s.ProgramId).Select(g => new { g.Key, Price = g.Min(s => s.PriceCents) }).ToDictionaryAsync(x => x.Key, x => x.Price, ct);

    static decimal PercentOfPrice(DiscountCode code, int priceCents) => code.Kind switch
    {
        DiscountKind.Percent => code.Value,
        _ => priceCents == 0 ? 100 : Math.Round(code.Value * 100m / priceCents, 1),
    };

    static string Describe(DiscountCode code) => code.Kind == DiscountKind.Percent
        ? $"{code.Value}% off"
        : $"{StaffCx.Money(code.Value)} off per camper";

    static object ToRow(DiscountRequest r, int priceCents)
    {
        var percent = PercentOfPrice(r.DiscountCode, priceCents);
        return new
        {
            r.Id,
            r.DiscountCode.Code,
            Kind = r.DiscountCode.Kind.ToString(),
            r.DiscountCode.Value,
            Description = Describe(r.DiscountCode),
            RequesterType = r.RequesterType.ToString(),
            r.RequestedBy,
            r.Organization,
            Program = r.Program.Name,
            SessionPriceCents = priceCents,
            r.ValidFrom,
            r.ValidTo,
            r.MaxUses,
            r.Stackable,
            r.OverridesOtherCodes,
            r.RequesterNote,
            r.RequestedAt,
            Decision = r.Decision.ToString(),
            r.ReviewedBy,
            r.ReviewNote,
            r.ReviewedAt,
            CodeStatus = r.DiscountCode.Status.ToString(),
            PercentOfPrice = percent,
            OverThreshold = percent > ThresholdPercent,
        };
    }
}
