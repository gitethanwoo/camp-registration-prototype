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

            r.Decision = ReviewDecision.Approved;
            r.ReviewedBy = staff.Actor;
            r.ReviewedAt = DateTime.UtcNow;
            r.ReviewNote = note;
            r.DiscountCode.Status = DiscountStatus.Approved; // live at checkout from now on
            audit.Record("discount.approved", "DiscountCode", r.DiscountCodeId,
                $"Approved {r.DiscountCode.Code} ({Describe(r.DiscountCode)}) for {r.Organization}.{(note is null ? "" : $" Note: {note}")}");
            db.OutboxEvents.Add(new OutboxEvent { Type = "DiscountApproved", Target = "HubSpot", AggregateId = "discount-" + r.DiscountCodeId, PayloadJson = JsonSerializer.Serialize(new { r.DiscountCode.Code, r.RequestedBy, r.Organization }), CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
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
            r.Decision = ReviewDecision.Rejected;
            r.ReviewedBy = staff.Actor;
            r.ReviewedAt = DateTime.UtcNow;
            r.ReviewNote = note;
            audit.Record("discount.rejected", "DiscountCode", r.DiscountCodeId, $"Rejected {r.DiscountCode.Code} for {r.Organization}. Note: {note}");
            db.OutboxEvents.Add(new OutboxEvent { Type = "DiscountRejected", Target = "HubSpot", AggregateId = "discount-" + r.DiscountCodeId, PayloadJson = JsonSerializer.Serialize(new { r.DiscountCode.Code, r.RequestedBy, note }), CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });
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
