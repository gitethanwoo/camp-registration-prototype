using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Setup;

public record TierInput(int DaysBefore, int RefundPercent, RefundBasis Basis, int AdminFeeCents);
public record PricingInput(int PriceCents, int DepositCents, int PlanInstallments, DateOnly BalanceDueDate, List<TierInput> Tiers);

/// <summary>
/// K4 · Pricing and policies (FR-47, FR-48, FR-49). Price, deposit, the payment plan template
/// (installment count and final due date, scheduled by <see cref="Pricing.PlanSchedule"/> exactly as
/// checkout does) and the cancellation and refund table. Orders keep the price they were sold at.
/// </summary>
public sealed class PricingSetupEndpoints : IEndpointModule
{
    public const int MaxInstallments = 6;

    public void Map(IEndpointRouteBuilder app)
    {
        var setup = app.MapGroup("/api/admin/setup").RequireAuthorization(Policies.Admin);

        setup.MapGet("/sessions/{id:int}/pricing", async (int id, CampDbContext db, CancellationToken ct) =>
        {
            var s = await db.Sessions.AsNoTracking().Include(x => x.Program).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Results.NotFound();
            var tiers = await RefundPolicy.TiersForAsync(db, id, ct);
            var input = new PricingInput(s.PriceCents, s.DepositCents, s.PlanInstallments, s.BalanceDueDate,
                [.. tiers.Select(t => new TierInput(t.DaysBefore, t.RefundPercent, t.Basis, t.AdminFeeCents))]);
            var orders = await ProgramSetupEndpoints.ActiveRegistrations(db).CountAsync(r => r.SessionId == id, ct);
            return Results.Ok(new
            {
                Session = new { s.Id, s.Name, s.StartDate, s.EndDate, Program = s.Program.Name, ProgramType = s.Program.Type.ToString() },
                Saved = input,
                Registrations = orders,
                Preview = Preview(s, input),
            });
        });

        // Live preview of unsaved edits: the page never does money or date math itself.
        setup.MapPost("/sessions/{id:int}/pricing/preview", async (int id, PricingInput req, CampDbContext db, CancellationToken ct) =>
        {
            var s = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return s is null ? Results.NotFound() : Results.Ok(Preview(s, req));
        });

        setup.MapPut("/sessions/{id:int}/pricing", async (int id, PricingInput req, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var s = await db.Sessions.Include(x => x.Program).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Results.NotFound();
            var errors = Validate(s, req);
            if (errors.Count > 0) return SetupResults.Invalid(errors);

            var oldTiers = await db.Set<RefundTier>().AsNoTracking().Where(t => t.SessionId == id).ToListAsync(ct);
            var shownOld = oldTiers.Count > 0 ? oldTiers : [.. RefundPolicy.Defaults];
            var before = (s.PriceCents, s.DepositCents, Plan: PlanLabel(s.PlanInstallments, s.BalanceDueDate), Policy: PolicyLabel(shownOld));

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Only the session changes. Registrations and orders carry their own price and totals,
            // so nothing already sold is repriced.
            s.PriceCents = req.PriceCents;
            s.DepositCents = req.DepositCents;
            s.PlanInstallments = req.PlanInstallments;
            s.BalanceDueDate = req.BalanceDueDate;
            await db.Set<RefundTier>().Where(t => t.SessionId == id).ExecuteDeleteAsync(ct);
            var newTiers = req.Tiers.Select(t => new RefundTier { SessionId = id, DaysBefore = t.DaysBefore, RefundPercent = t.RefundPercent, Basis = t.Basis, AdminFeeCents = t.AdminFeeCents }).ToList();
            db.Set<RefundTier>().AddRange(newTiers);
            audit.Record(db, "pricing.changed", "Session", id, $"Changed pricing and policies for {s.Program.Name} · {s.Name}. Existing orders keep their price.",
                ("Price", SetupResults.Money(before.PriceCents), SetupResults.Money(s.PriceCents)),
                ("Deposit", SetupResults.Money(before.DepositCents), SetupResults.Money(s.DepositCents)),
                ("Payment plan", before.Plan, PlanLabel(s.PlanInstallments, s.BalanceDueDate)),
                ("Cancellation policy", before.Policy, PolicyLabel(newTiers)));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(Preview(s, req));
        });
    }

    static Dictionary<string, string[]> Validate(Session s, PricingInput req)
    {
        var errors = new Dictionary<string, string[]>();
        if (req.PriceCents is <= 0 or > 10_000_000) errors["priceCents"] = ["Enter a price between $1 and $100,000."];
        if (req.DepositCents < 0 || req.DepositCents > req.PriceCents) errors["depositCents"] = ["The deposit has to be between $0 and the total price."];
        if (req.PlanInstallments is < 0 or > MaxInstallments) errors["planInstallments"] = [$"Plans have between 0 and {MaxInstallments} installments."];
        if (req.BalanceDueDate >= s.StartDate) errors["balanceDueDate"] = [$"The balance has to be due before the session starts on {SetupResults.Date(s.StartDate)}."];
        if (req.PlanInstallments > 0 && req.DepositCents == req.PriceCents) errors["planInstallments"] = ["The deposit already covers the price, so there's nothing left to split into installments."];

        var tiers = req.Tiers ?? [];
        if (tiers.Count is 0 or > 6) errors["tiers"] = ["The policy needs between 1 and 6 time windows."];
        else if (!tiers.Any(t => t.DaysBefore == 0)) errors["tiers"] = ["Add a window that starts 0 days before the session, so every cancellation date is covered."];
        else if (tiers.Select(t => t.DaysBefore).Distinct().Count() != tiers.Count) errors["tiers"] = ["Two windows start on the same day. Each window needs its own number of days."];
        else if (tiers.Any(t => t.DaysBefore is < 0 or > 365)) errors["tiers"] = ["Days before start run from 0 to 365."];
        else if (tiers.Any(t => t.RefundPercent is < 0 or > 100)) errors["tiers"] = ["Refunds are between 0% and 100%."];
        else if (tiers.Any(t => t.AdminFeeCents < 0 || t.AdminFeeCents > req.PriceCents)) errors["tiers"] = ["Admin fees are between $0 and the price."];
        else
        {
            var ordered = tiers.OrderByDescending(t => t.DaysBefore).ToList();
            for (var i = 1; i < ordered.Count; i++)
                if (ordered[i].RefundPercent > ordered[i - 1].RefundPercent)
                    errors["tiers"] = [$"Cancelling {ordered[i].DaysBefore} days out can't refund more than cancelling {ordered[i - 1].DaysBefore} days out."];
        }
        return errors;
    }

    /// <summary>What a family sees for one camper, plus the policy table with its date windows.</summary>
    static object Preview(Session s, PricingInput req)
    {
        var errors = Validate(s, req);
        var draft = new Session
        {
            Id = s.Id,
            StartDate = s.StartDate,
            PriceCents = Math.Max(req.PriceCents, 0),
            DepositCents = Math.Clamp(req.DepositCents, 0, Math.Max(req.PriceCents, 0)),
            PlanInstallments = Math.Clamp(req.PlanInstallments, 0, MaxInstallments),
            BalanceDueDate = req.BalanceDueDate,
        };
        var camper = new Person { FirstName = "Camper" };
        var plan = Pricing.Build(draft, [camper], PaymentOption.Plan, null, null);
        var deposit = Pricing.Build(draft, [camper], PaymentOption.Deposit, null, null);
        var tiers = (req.Tiers ?? []).Select(t => new RefundTier { DaysBefore = t.DaysBefore, RefundPercent = t.RefundPercent, Basis = t.Basis, AdminFeeCents = t.AdminFeeCents })
            .OrderByDescending(t => t.DaysBefore).ToList();
        var paidInFull = draft.PriceCents;
        return new
        {
            Errors = errors,
            TotalCents = draft.PriceCents,
            DepositCents = draft.DepositCents,
            RemainingCents = draft.PriceCents - draft.DepositCents,
            PlanOffered = draft.PlanInstallments > 0,
            Schedule = (draft.PlanInstallments > 0 ? plan : deposit).Schedule.Select((x, i) => new
            {
                Label = i == 0 ? "Deposit" : x.Label,
                x.DueDate,
                x.AmountCents,
            }),
            ScheduleTotalCents = (draft.PlanInstallments > 0 ? plan : deposit).Schedule.Sum(x => x.AmountCents),
            Tiers = tiers.Select((t, i) => new
            {
                t.DaysBefore,
                t.RefundPercent,
                Basis = t.Basis.ToString(),
                t.AdminFeeCents,
                // Cancelling on or after From and on or before To lands in this window.
                From = i == 0 ? (DateOnly?)null : s.StartDate.AddDays(-(tiers[i - 1].DaysBefore - 1)),
                To = s.StartDate.AddDays(-t.DaysBefore),
                Rule = RefundPolicy.Describe(tiers, t),
                Terms = RefundPolicy.Terms(t),
                ExampleRefundCents = RefundPolicy.Refund(t, paidInFull, draft.DepositCents),
            }),
        };
    }

    static string PlanLabel(int n, DateOnly due) => n == 0 ? $"No plan; balance due {SetupResults.Date(due)}" : $"{n} installments, last due {SetupResults.Date(due)}";

    static string PolicyLabel(IEnumerable<RefundTier> tiers)
    {
        var list = tiers.OrderByDescending(t => t.DaysBefore).ToList();
        return string.Join("; ", list.Select(t => RefundPolicy.Describe(list, t)));
    }
}
