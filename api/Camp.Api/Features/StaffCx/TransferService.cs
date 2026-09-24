using System.Text.Json;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.StaffCx;

/// <summary>What the destination looks like for one camper: the pool, its live capacity, and anything that blocks the move.</summary>
public sealed record TransferCheck(
    int SessionId,
    string Session,
    int PriceCents,
    int PriceDifferenceCents,
    int? PoolId,
    string? Pool,
    int? Capacity,
    int? Reserved,
    int? SpotsLeft,
    List<string> Blockers,
    List<Requirement> Requirements,
    int NewBalanceCents,
    int RefundCents,
    int NewDiscountCents = 0)
{
    public bool CanMove => Blockers.Count == 0;
}

/// <summary>One line of the destination requirements review: Ok, Review or Blocked.</summary>
public sealed record Requirement(string Label, string State, string Detail);

public sealed record TransferOutcome(bool Ok, int StatusCode, string? Error, int RefundCents = 0);

/// <summary>
/// Moves a registration between two sessions of the same program (FR-42). The seat claim, the seat
/// release, any refund and the installment rebalance happen in one SQL transaction: all or nothing.
/// </summary>
public sealed class TransferService(CampDbContext db, IPaymentGateway gateway, IAuditLog audit)
{
    /// <summary>Evaluates a destination for a registration against live capacity. Never writes.</summary>
    public static async Task<TransferCheck> CheckAsync(CampDbContext db, Registration reg, Session to, CancellationToken ct)
    {
        var blockers = new List<string>();
        var requirements = new List<Requirement>();
        if (reg.Status != RegistrationStatus.Confirmed)
            blockers.Add($"Only confirmed registrations can move; this one is {Label(reg.Status)}.");
        if (to.ProgramId != reg.Session.ProgramId)
            blockers.Add("Transfers stay within the same program.");
        if (to.Id == reg.SessionId)
            blockers.Add($"{reg.Person.FirstName} is already in this session.");

        var (pool, reason) = Eligibility.FindPool(reg.Person, to);
        var grade = Eligibility.GradeFor(reg.Person.DateOfBirth, to.StartDate);
        if (pool is null)
        {
            blockers.Add(reason ?? "No pool in this session fits this camper.");
            requirements.Add(new("Grade and pool", "Blocked", reason ?? "No matching pool."));
        }
        else
        {
            if (pool.Reserved >= pool.Capacity)
            {
                blockers.Add($"The {pool.Name} pool is full ({pool.Reserved} of {pool.Capacity}). Approving would overbook it.");
                requirements.Add(new("Grade and pool", "Blocked", $"{Eligibility.GradeLabel(grade)} fits the {pool.Name} pool, but it's full ({pool.Reserved} of {pool.Capacity})."));
            }
            else
                requirements.Add(new("Grade and pool", "Ok", $"{Eligibility.GradeLabel(grade)} fits the {pool.Name} pool."));
        }

        var alreadyThere = await db.Registrations.AnyAsync(r => r.PersonId == reg.PersonId && r.SessionId == to.Id && r.Status != RegistrationStatus.Cancelled && r.Id != reg.Id, ct);
        if (alreadyThere) blockers.Add($"{reg.Person.FirstName} already has a registration for this session.");

        var overlapping = await db.Registrations
            .Where(r => r.PersonId == reg.PersonId && r.Id != reg.Id && r.SessionId != to.Id && r.Status != RegistrationStatus.Cancelled
                && r.Session.StartDate <= to.EndDate && r.Session.EndDate >= to.StartDate)
            .Select(r => r.Session.Program.Name).FirstOrDefaultAsync(ct);
        requirements.Add(overlapping is null
            ? new("Dates", "Ok", $"No other camp for {reg.Person.FirstName} during {StaffCx.Dates(to.StartDate, to.EndDate)}.")
            : new("Dates", "Review", $"{reg.Person.FirstName} is also registered for {overlapping} during these dates."));
        requirements.Add(new("Waivers and forms", "Ok", "Same program: signed waivers, answers and health form carry over."));

        var priceDiff = to.PriceCents - reg.Session.PriceCents;
        var newPrice = reg.PriceCents + priceDiff;
        var discount = await DiscountAfterMove(db, reg, Math.Max(newPrice, 0), ct);
        var newBalance = newPrice - discount - reg.PaidCents;
        var refund = Math.Max(0, -newBalance);
        requirements.Add(priceDiff == 0
            ? new("Price", "Ok", "Same price. No payment change.")
            : priceDiff > 0
                ? new("Price", "Review", $"{StaffCx.Money(priceDiff)} more. Added to the balance due.")
                : new("Price", "Review", refund > 0 ? $"{StaffCx.Money(-priceDiff)} less. {StaffCx.Money(refund)} refunded to the card on file." : $"{StaffCx.Money(-priceDiff)} less. Taken off the balance due."));

        return new TransferCheck(to.Id, $"{to.Name} · {StaffCx.Dates(to.StartDate, to.EndDate)}", to.PriceCents, priceDiff,
            pool?.Id, pool?.Name, pool?.Capacity, pool?.Reserved, pool is null ? null : Math.Max(0, pool.Capacity - pool.Reserved),
            blockers, requirements, Math.Max(0, newBalance), refund, discount);
    }

    /// <summary>
    /// The camper's discount at the new price. A percent code is worked out again on the new price, the
    /// same way checkout priced it; a flat code keeps its amount, capped at the new price.
    /// </summary>
    static async Task<int> DiscountAfterMove(CampDbContext db, Registration reg, int newPrice, CancellationToken ct)
    {
        if (reg.DiscountCents == 0) return 0;
        var code = reg.OrderId is null ? null : await db.Orders.Where(o => o.Id == reg.OrderId)
            .Join(db.DiscountCodes, o => o.DiscountCode, d => d.Code, (o, d) => d).AsNoTracking().FirstOrDefaultAsync(ct);
        return code is { Kind: DiscountKind.Percent }
            ? Math.Min((int)Math.Round(newPrice * Math.Min(code.Value, 100) / 100m), newPrice)
            : Math.Min(reg.DiscountCents, newPrice);
    }

    public async Task<TransferOutcome> ApproveAsync(int requestId, string actor, string? note, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Claim the decision first, as a conditional update. A second approval arriving at the same time
        // blocks on this row lock, then finds the request no longer pending and changes nothing. Every
        // failure below returns without committing, so the request goes back to pending.
        var won = await db.Set<TransferRequest>().Where(t => t.Id == requestId && t.Status == TransferStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TransferStatus.Approved), ct);
        var req = await db.Set<TransferRequest>().FirstOrDefaultAsync(t => t.Id == requestId, ct);
        if (req is null) return new(false, 404, "Transfer request not found.");
        if (won == 0) return new(false, 409, AlreadyDecided(req));

        var reg = await db.Registrations
            .Include(r => r.Person).Include(r => r.Pool).Include(r => r.Session)
            .Include(r => r.Order).ThenInclude(o => o!.Operations)
            .Include(r => r.Order).ThenInclude(o => o!.Installments)
            .SingleAsync(r => r.Id == req.RegistrationId, ct);
        var to = await db.Sessions.Include(s => s.Pools).SingleAsync(s => s.Id == req.ToSessionId, ct);
        var check = await CheckAsync(db, reg, to, ct);
        if (!check.CanMove) return new(false, 409, string.Join(" ", check.Blockers));

        // Claim the destination seat first; a lost race leaves both pools untouched.
        var claimed = await db.CapacityPools.Where(p => p.Id == check.PoolId && p.Reserved < p.Capacity)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved + 1), ct);
        if (claimed == 0) return new(false, 409, $"The {check.Pool} pool just filled up. Nothing was moved.");
        await db.CapacityPools.Where(p => p.Id == reg.PoolId && p.Reserved > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved - 1), ct);

        var from = reg.Session;
        var fromPool = reg.Pool.Name;
        reg.SessionId = to.Id;
        reg.Session = to;
        reg.PoolId = check.PoolId!.Value;
        reg.Pool = to.Pools.Single(p => p.Id == check.PoolId);
        reg.Grade = Eligibility.GradeFor(reg.Person.DateOfBirth, to.StartDate);
        reg.PriceCents += check.PriceDifferenceCents;
        var discountChange = check.NewDiscountCents - reg.DiscountCents;
        reg.DiscountCents = check.NewDiscountCents;

        var refunded = 0;
        if (check.RefundCents > 0 && reg.Order is not null)
        {
            refunded = await RefundAsync(reg.Order, check.RefundCents, $"Transfer to {check.Session}", ct);
            if (refunded < check.RefundCents)
            {
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                return new(false, 502, "The refund didn't go through, so nothing was moved. Try again, or deny the request.");
            }
            reg.PaidCents -= refunded;
        }

        if (reg.Order is { } order)
        {
            order.SubtotalCents += check.PriceDifferenceCents;
            order.DiscountCents = Math.Max(0, order.DiscountCents + discountChange);
            order.TotalCents = Math.Max(0, order.TotalCents + check.PriceDifferenceCents - discountChange);
            var siblings = await db.Registrations.Where(r => r.OrderId == order.Id && r.Id != reg.Id && r.Status != RegistrationStatus.Cancelled).ToListAsync(ct);
            if (siblings.All(r => r.SessionId == to.Id)) order.SessionId = to.Id;
            Rebalance(order, siblings.Sum(r => r.BalanceCents) + reg.BalanceCents);
        }

        req.Status = TransferStatus.Approved;
        req.DecidedBy = actor;
        req.DecidedAt = DateTime.UtcNow;
        req.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        req.PriceDifferenceCents = check.PriceDifferenceCents;
        req.RefundCents = refunded;

        var money = check.PriceDifferenceCents == 0 ? "No price change."
            : check.PriceDifferenceCents > 0 ? $"Balance up {StaffCx.Money(check.PriceDifferenceCents)}."
            : refunded > 0 ? $"Refunded {StaffCx.Money(refunded)}." : $"Balance down {StaffCx.Money(-check.PriceDifferenceCents)}.";
        audit.Record("registration.transferred", "Registration", reg.Id,
            $"Moved {reg.Person.FullName} from {from.Name} ({fromPool}) to {to.Name} ({check.Pool}). {money}");
        audit.Record("transfer.approved", "Household", reg.HouseholdId,
            $"Approved the transfer of {reg.Person.FullName} to {check.Session}. {money}");
        db.OutboxEvents.Add(new OutboxEvent
        {
            Type = "RegistrationTransferred",
            Target = "HubSpot",
            AggregateId = "reg-" + reg.Id,
            PayloadJson = JsonSerializer.Serialize(new { registrationId = reg.Id, fromSessionId = from.Id, toSessionId = to.Id, priceDifferenceCents = check.PriceDifferenceCents, refundedCents = refunded }),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(true, 200, null, refunded);
    }

    /// <summary>Refunds across the order's charges, newest first. Returns how much actually came back.</summary>
    async Task<int> RefundAsync(PaymentOrder order, int cents, string reason, CancellationToken ct)
    {
        // Earlier refunds are assumed to have come off the newest charges first, the same order used here.
        var priorRefunds = order.Operations.Where(o => o.Kind == PaymentKind.Refund && o.Succeeded).Sum(o => o.AmountCents);
        var remaining = cents;
        foreach (var charge in order.Operations.Where(o => o.Kind == PaymentKind.Charge && o.Succeeded).OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).ToList())
        {
            var consumed = Math.Min(charge.AmountCents, priorRefunds);
            priorRefunds -= consumed;
            var amount = Math.Min(charge.AmountCents - consumed, remaining);
            if (amount <= 0) continue;
            var result = await gateway.RefundAsync(charge.ProcessorRef, amount, ct);
            if (!result.Succeeded) return cents - remaining;
            db.PaymentOperations.Add(new PaymentOperation
            {
                OrderId = order.Id,
                Kind = PaymentKind.Refund,
                AmountCents = amount,
                Succeeded = true,
                ProcessorRef = result.ProcessorRef,
                CardLast4 = charge.CardLast4,
                Reason = reason,
                CreatedAt = DateTime.UtcNow,
            });
            remaining -= amount;
            if (remaining == 0) break;
        }
        return cents - remaining;
    }

    /// <summary>
    /// Spreads the order's outstanding balance over the installments still scheduled, keeping their dates.
    /// Failed installments keep their amounts (a retry charges them), so that part of the balance is left out.
    /// </summary>
    static void Rebalance(PaymentOrder order, int outstanding)
    {
        var scheduled = order.Installments.Where(i => i.Status == InstallmentStatus.Scheduled).OrderBy(i => i.Sequence).ToList();
        if (scheduled.Count == 0) return;
        var failed = order.Installments.Where(i => i.Status == InstallmentStatus.Failed).Sum(i => i.AmountCents);
        var target = Math.Max(0, outstanding - failed);
        var each = target / scheduled.Count;
        for (var i = 0; i < scheduled.Count; i++)
            scheduled[i].AmountCents = i == scheduled.Count - 1 ? target - each * (scheduled.Count - 1) : each;
    }

    public static string AlreadyDecided(TransferRequest req) =>
        $"This request was already {req.Status.ToString().ToLowerInvariant()} by {req.DecidedBy ?? "another staff member"}.";

    public static string Label(RegistrationStatus s) => s switch
    {
        RegistrationStatus.PaymentPending => "Payment pending",
        RegistrationStatus.ApplicationPending => "Application pending",
        RegistrationStatus.OfferedSpot => "Offered spot",
        _ => s.ToString(),
    };
}
