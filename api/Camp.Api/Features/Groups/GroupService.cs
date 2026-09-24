using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Groups;

public record RosterRow(string? Name, string? Email);
public record RosterRequest(string? Name, List<RosterRow> Attendees);
public record CreateGroupRequest(int SessionId, string? Name, List<RosterRow> Attendees);
public record GroupCheckoutRequest(string IdempotencyKey, string CardToken);
public record GroupCheckoutResult(int GroupId, string ConfirmationCode, string Status, string? Message);
public record ResendRequest(List<int> AttendeeIds);
public record SentLink(int AttendeeId, string Name, string Email, string Link);
public record ResendResult(List<SentLink> Sent, List<string> Skipped);
public record EmailRequest(string Email);
public record WaiverSign(int WaiverId, bool Accepted);
public record FormsRequest(string Email, string? Phone, string SignerName, Dictionary<string, string>? Answers, List<WaiverSign> Waivers);
public record WithdrawalRequest(string? Reason);

/// <summary>A request the caller can fix. Keys are field paths, e.g. <c>attendees[2].email</c>.</summary>
public sealed class GroupValidationException(Dictionary<string, string[]> errors) : Exception("The request is invalid.")
{
    public Dictionary<string, string[]> Errors { get; } = errors;

    public static GroupValidationException One(string key, string message) => new(new() { [key] = [message] });
}

/// <summary>
/// Group registration rules: roster validation, seat claiming and payment, secure links, attendee
/// forms and withdrawals. Endpoints stay thin; every money and capacity change happens here.
/// </summary>
public sealed class GroupService(CampDbContext db, IPaymentGateway gateway, IAuditLog audit)
{
    public const int MaxAttendees = 60;

    // ── Roster ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Cleans a roster. Blank rows are dropped. A row needs a name; the email may be left for later
    /// (R8: rows can be partial), but when present it must be valid and unique in the group.
    /// </summary>
    public static List<(string Name, string? Email)> CleanRoster(List<RosterRow>? rows)
    {
        var errors = new Dictionary<string, string[]>();
        var clean = new List<(string, string?)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < (rows?.Count ?? 0); i++)
        {
            var name = rows![i].Name?.Trim() ?? "";
            var email = rows[i].Email?.Trim();
            if (name.Length == 0 && string.IsNullOrEmpty(email)) continue;
            if (name.Length == 0) errors[$"attendees[{i}].name"] = [$"Add a name for {email}."];
            else if (name.Length > 120) errors[$"attendees[{i}].name"] = ["Names can be up to 120 characters."];
            if (!string.IsNullOrEmpty(email))
            {
                if (!IsEmail(email)) errors[$"attendees[{i}].email"] = [$"{email} doesn't look like an email address."];
                else if (!seen.Add(email)) errors[$"attendees[{i}].email"] = [$"{email} is on the roster twice. Each attendee needs their own email."];
            }
            clean.Add((name, string.IsNullOrEmpty(email) ? null : email.ToLowerInvariant()));
        }
        if (clean.Count == 0 && errors.Count == 0) errors["attendees"] = ["Add at least one attendee."];
        if (clean.Count > MaxAttendees) errors["attendees"] = [$"A group can have up to {MaxAttendees} attendees."];
        if (errors.Count > 0) throw new GroupValidationException(errors);
        return clean;
    }

    public static void ReplaceRoster(GroupRegistration group, List<(string Name, string? Email)> roster)
    {
        group.Attendees.Clear();
        var order = 0;
        foreach (var (name, email) in roster)
            group.Attendees.Add(new GroupAttendee { Name = name, Email = email, SortOrder = order++ });
    }

    // ── Checkout ────────────────────────────────────────────────────────────

    /// <summary>
    /// Claims one seat per attendee, charges the leader for the whole group, then confirms the group
    /// and sends each attendee with an email their secure link. A decline releases every seat and
    /// leaves the group in Draft with its roster, so the leader can retry with another card.
    /// </summary>
    public async Task<GroupCheckoutResult> CheckoutAsync(int householdId, int groupId, GroupCheckoutRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.IdempotencyKey) || req.IdempotencyKey.Length > 100)
            throw GroupValidationException.One("idempotencyKey", "Start the payment again.");

        // FR-45: a repeated submit returns the first outcome instead of charging twice.
        var prior = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.IdempotencyKey == req.IdempotencyKey && o.HouseholdId == householdId, ct);
        if (prior is not null) return Outcome(groupId, prior);

        // A payment interrupted after its seat claim may since have been settled by the reconciler.
        await ReconcileAsync(householdId, groupId, ct);

        var group = await db.Set<GroupRegistration>().Include(g => g.Attendees).Include(g => g.Session).ThenInclude(s => s.Pools)
            .Include(g => g.Session).ThenInclude(s => s.Program)
            .FirstOrDefaultAsync(g => g.Id == groupId && g.LeaderHouseholdId == householdId, ct)
            ?? throw new KeyNotFoundException();
        if (group.Status == GroupStatus.Confirmed)
            throw GroupValidationException.One("group", "This group is already paid for.");
        // K2: the same rule as Checkout.cs. A program sent back to draft takes no new registrations.
        if (!group.Session.Program.IsPublished)
            throw GroupValidationException.One("sessionId", "This program isn't open for registration.");
        var count = group.Attendees.Count;
        if (count == 0) throw GroupValidationException.One("attendees", "Add at least one attendee.");
        var pool = group.Session.Pools.OrderBy(p => p.SortOrder).First();
        var price = group.Session.PriceCents;

        var order = new PaymentOrder
        {
            HouseholdId = householdId,
            SessionId = group.SessionId,
            IdempotencyKey = req.IdempotencyKey,
            ConfirmationCode = "WS-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(3)),
            PaymentOption = PaymentOption.Full,
            SubtotalCents = price * count,
            TotalCents = price * count,
            DueTodayCents = price * count,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };

        // Step 1: one transaction claims all seats with a conditional UPDATE, so a group can never
        // overbook the pool, and marks the group as in-flight so a second submit can't claim again.
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            db.Orders.Add(order);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException e) when (e.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
            {
                // Lost a race with an identical submit: report that one instead of charging twice.
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                return Outcome(groupId, await db.Orders.AsNoTracking().SingleAsync(o => o.IdempotencyKey == req.IdempotencyKey, ct));
            }
            var claimedGroup = await db.Set<GroupRegistration>()
                .Where(g => g.Id == group.Id && g.OrderId == null && g.Status == GroupStatus.Draft)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.OrderId, order.Id), ct);
            if (claimedGroup == 0)
            {
                await tx.RollbackAsync(ct);
                throw GroupValidationException.One("group", "A payment for this group is already in progress. Refresh to see it.");
            }
            var claimed = await db.CapacityPools
                .Where(p => p.Id == pool.Id && p.Reserved + count <= p.Capacity)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved + count), ct);
            if (claimed == 0)
            {
                await tx.RollbackAsync(ct);
                var left = await db.CapacityPools.Where(p => p.Id == pool.Id).Select(p => p.Capacity - p.Reserved).SingleAsync(ct);
                throw GroupValidationException.One("attendees", left <= 0
                    ? $"{group.Session.Program.Name} is full. You haven't been charged."
                    : $"Only {left} {(left == 1 ? "spot is" : "spots are")} left and your group has {count}. Remove {count - left} to continue. You haven't been charged.");
            }
            await tx.CommitAsync(ct);
        }

        // Step 2: charge. The processor and SQL can't share a transaction; seats stay held meanwhile.
        // From here on the request's token is ignored: a leader closing the tab mid-charge must not
        // strand a Pending order with the seats claimed.
        var none = CancellationToken.None;
        var result = await gateway.ChargeAsync(req.CardToken, order.DueTodayCents, req.IdempotencyKey, none);

        // Step 3: confirm or compensate.
        await using (var tx = await db.Database.BeginTransactionAsync(none))
        {
            // Finalize only if nobody else did (the reconciler picks up Pending orders after two minutes).
            var finalized = await db.Orders.Where(o => o.Id == order.Id && o.Status == OrderStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, result.Succeeded ? OrderStatus.Paid : OrderStatus.Declined), none);
            if (finalized == 0)
            {
                await tx.RollbackAsync(none);
                db.ChangeTracker.Clear();
                await ReconcileAsync(householdId, group.Id, none);
                return Outcome(group.Id, await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id, none));
            }
            db.PaymentOperations.Add(new PaymentOperation
            {
                OrderId = order.Id,
                Kind = PaymentKind.Charge,
                AmountCents = order.DueTodayCents,
                Succeeded = result.Succeeded,
                ProcessorRef = result.ProcessorRef,
                CardLast4 = result.CardLast4,
                Reason = result.DeclineReason,
                CreatedAt = DateTime.UtcNow,
            });
            if (result.Succeeded)
            {
                order.Status = OrderStatus.Paid;
                group.OrderId = order.Id;
                group.Status = GroupStatus.Confirmed;
                group.ConfirmedAt = DateTime.UtcNow;
                var sent = 0;
                foreach (var a in group.Attendees.Where(a => a.Email is not null))
                {
                    IssueLink(group, a);
                    sent++;
                }
                audit.Record("group.confirmed", "GroupRegistration", group.Id,
                    $"{group.Name}: {count} attendees for {group.Session.Program.Name} · {group.Session.Name}, {Money(order.TotalCents)} charged; {sent} secure links sent.");
                Outbox("GroupRegistrationConfirmed", order.ConfirmationCode, new { order.ConfirmationCode, group = group.Name, attendees = count });
            }
            else
            {
                order.Status = OrderStatus.Declined;
                order.DeclineReason = result.DeclineReason;
                // The in-flight marker was set with ExecuteUpdate, so clear it the same way.
                await db.Set<GroupRegistration>().Where(g => g.Id == group.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(g => g.OrderId, (int?)null), none);
                await db.CapacityPools.Where(p => p.Id == pool.Id && p.Reserved >= count)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved - count), none);
                audit.Record("group.payment_declined", "GroupRegistration", group.Id, $"Charge of {Money(order.TotalCents)} declined; {count} seats released.");
            }
            await db.SaveChangesAsync(none);
            await tx.CommitAsync(none);
        }
        return Outcome(group.Id, order);
    }

    /// <summary>
    /// Heals a Draft group whose payment was interrupted between the seat claim and step 3 and then
    /// settled elsewhere (the shared PendingPaymentReconciler only knows about Registration rows).
    /// Paid: confirm the group and send the links. Declined: release the seats and unlock the
    /// roster. A conditional update picks one winner, so parallel calls can't heal twice.
    /// </summary>
    public async Task ReconcileAsync(int householdId, int groupId, CancellationToken ct)
    {
        var stuck = await db.Set<GroupRegistration>().AsNoTracking()
            .Where(g => g.Id == groupId && g.LeaderHouseholdId == householdId && g.Status == GroupStatus.Draft
                && g.OrderId != null && g.Order!.Status != OrderStatus.Pending)
            .Select(g => new { OrderId = g.OrderId!.Value, g.Order!.Status, g.Order.TotalCents, g.Order.ConfirmationCode, Seats = g.Attendees.Count })
            .FirstOrDefaultAsync(ct);
        if (stuck is null) return;

        var none = CancellationToken.None;
        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(none);
        var groups = db.Set<GroupRegistration>().Where(g => g.Id == groupId && g.Status == GroupStatus.Draft && g.OrderId == stuck.OrderId);
        var won = stuck.Status == OrderStatus.Paid
            ? await groups.ExecuteUpdateAsync(s => s.SetProperty(g => g.Status, GroupStatus.Confirmed).SetProperty(g => g.ConfirmedAt, now), none)
            : await groups.ExecuteUpdateAsync(s => s.SetProperty(g => g.OrderId, (int?)null), none);
        if (won == 0)
        {
            await tx.RollbackAsync(none);
            return;
        }
        var group = await db.Set<GroupRegistration>().Include(g => g.Attendees).Include(g => g.Session).ThenInclude(s => s.Program)
            .SingleAsync(g => g.Id == groupId, none);
        if (stuck.Status == OrderStatus.Paid)
        {
            group.Status = GroupStatus.Confirmed;
            group.ConfirmedAt = now;
            var sent = 0;
            foreach (var a in group.Attendees.Where(a => a.Email is not null))
            {
                IssueLink(group, a);
                sent++;
            }
            audit.Record("group.confirmed", "GroupRegistration", group.Id,
                $"{group.Name}: interrupted payment settled as paid ({Money(stuck.TotalCents)}); {stuck.Seats} attendees confirmed, {sent} secure links sent.");
            Outbox("GroupRegistrationConfirmed", stuck.ConfirmationCode, new { stuck.ConfirmationCode, group = group.Name, attendees = stuck.Seats });
        }
        else
        {
            group.OrderId = null;
            var pool = await db.CapacityPools.Where(p => p.SessionId == group.SessionId).OrderBy(p => p.SortOrder).Select(p => p.Id).FirstAsync(none);
            await db.CapacityPools.Where(p => p.Id == pool && p.Reserved >= stuck.Seats)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved - stuck.Seats), none);
            audit.Record("group.payment_declined", "GroupRegistration", group.Id,
                $"Interrupted payment of {Money(stuck.TotalCents)} was not completed; {stuck.Seats} seats released and the roster unlocked.");
        }
        await db.SaveChangesAsync(none);
        await tx.CommitAsync(none);
    }

    static GroupCheckoutResult Outcome(int groupId, PaymentOrder o) =>
        new(groupId, o.ConfirmationCode, o.Status == OrderStatus.Paid ? "Confirmed" : o.Status.ToString(), o.DeclineReason);

    // ── Secure links ────────────────────────────────────────────────────────

    /// <summary>Rotates the attendee's token (the previous link stops working) and queues the email.</summary>
    public SentLink IssueLink(GroupRegistration group, GroupAttendee a)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        a.TokenHash = Hash(token);
        a.LinkSentAt = DateTime.UtcNow;
        var link = $"/g/{token}";
        // The emailed link has to travel to HubSpot; this payload is the only place the raw token lives.
        Outbox("GroupFormLink", $"group-{group.Id}", new { to = a.Email, attendee = a.Name, leader = group.LeaderName, link });
        return new SentLink(a.Id, a.Name, a.Email!, link);
    }

    public async Task<GroupAttendee?> FindByTokenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 64) return null;
        var hash = Hash(token);
        return await db.Set<GroupAttendee>().Include(a => a.WaiverAcceptances)
            .Include(a => a.Group).ThenInclude(g => g.Session).ThenInclude(s => s.Program).ThenInclude(p => p.Waivers)
            .Include(a => a.Group).ThenInclude(g => g.Session).ThenInclude(s => s.Program).ThenInclude(p => p.Questions)
            .FirstOrDefaultAsync(a => a.TokenHash == hash && a.Group.Status == GroupStatus.Confirmed, ct);
    }

    // ── Attendee forms (G1) ─────────────────────────────────────────────────

    public void SubmitForms(GroupAttendee a, FormsRequest req)
    {
        if (!a.IsActive) throw GroupValidationException.One("attendee", "You've been withdrawn from this group, so there's nothing left to complete.");
        var program = a.Group.Session.Program;
        var errors = new Dictionary<string, string[]>();
        var email = req.Email?.Trim() ?? "";
        if (!IsEmail(email)) errors["email"] = ["Enter an email address we can reach you at."];
        var phone = req.Phone?.Trim();
        if (phone is { Length: > 40 }) errors["phone"] = ["Phone numbers can be up to 40 characters."];
        var signer = req.SignerName?.Trim() ?? "";
        if (signer.Length is 0 or > 120) errors["signerName"] = ["Type your full name to sign."];

        var answers = new Dictionary<string, string>();
        foreach (var q in program.Questions.OrderBy(q => q.SortOrder))
        {
            var value = req.Answers?.GetValueOrDefault(q.Key)?.Trim() ?? "";
            var shown = q.ShowWhenKey is null || answers.GetValueOrDefault(q.ShowWhenKey) == q.ShowWhenValue;
            if (!shown) continue;
            if (value.Length == 0)
            {
                if (q.Required) errors[$"answers.{q.Key}"] = [$"{q.Label} is required."];
                continue;
            }
            if (value.Length > 500) errors[$"answers.{q.Key}"] = [$"{q.Label} can be up to 500 characters."];
            else if (q.Type == QuestionType.Select && !(q.Options ?? "").Split('|').Contains(value)) errors[$"answers.{q.Key}"] = [$"Choose one of the options for {q.Label}."];
            else if (q.Type == QuestionType.YesNo && value is not ("Yes" or "No")) errors[$"answers.{q.Key}"] = [$"Answer yes or no for {q.Label}."];
            answers[q.Key] = value;
        }
        foreach (var w in program.Waivers)
            if (!req.Waivers.Any(s => s.WaiverId == w.Id && s.Accepted)) errors[$"waivers.{w.Id}"] = [$"Accept the {w.Title} to continue."];
        if (errors.Count > 0) throw new GroupValidationException(errors);

        var now = DateTime.UtcNow;
        a.Email = email.ToLowerInvariant();
        a.Phone = string.IsNullOrEmpty(phone) ? null : phone;
        a.AnswersJson = JsonSerializer.Serialize(answers);
        a.WaiverAcceptances.Clear();
        foreach (var w in program.Waivers)
            a.WaiverAcceptances.Add(new GroupWaiverAcceptance { WaiverTemplateId = w.Id, Version = w.Version, SignerName = signer, AcceptedAt = now });
        a.FormStatus = FormStatus.Complete;
        a.SubmittedAt = now;
        audit.Record("group.forms_submitted", "GroupAttendee", a.Id, $"{a.Name} submitted forms and accepted {program.Waivers.Count} waivers by secure link.");
        Outbox("GroupFormsSubmitted", $"group-{a.GroupId}", new { attendee = a.Name, group = a.Group.Name });
    }

    public void RequestWithdrawal(GroupAttendee a, WithdrawalRequest req)
    {
        if (!a.IsActive) throw GroupValidationException.One("attendee", "You've already been withdrawn from this group.");
        if (a.Withdrawal is WithdrawalStatus.Requested or WithdrawalStatus.Refunding) throw GroupValidationException.One("attendee", "Your withdrawal request is already with your group leader.");
        var reason = req.Reason?.Trim();
        if (reason is { Length: > 1000 }) throw GroupValidationException.One("reason", "Keep the reason under 1,000 characters.");
        a.Withdrawal = WithdrawalStatus.Requested;
        a.WithdrawalReason = string.IsNullOrEmpty(reason) ? null : reason;
        a.WithdrawalRequestedAt = DateTime.UtcNow;
        a.WithdrawalResolvedAt = null;
        audit.Record("group.withdrawal_requested", "GroupAttendee", a.Id, $"{a.Name} asked to withdraw from {a.Group.Name}.");
        Outbox("GroupWithdrawalRequested", $"group-{a.GroupId}", new { attendee = a.Name, group = a.Group.Name });
    }

    // ── Withdrawals (G2) ────────────────────────────────────────────────────

    /// <summary>
    /// Refunds one attendee's share to the leader's card and releases their seat. The request is
    /// claimed first with a conditional UPDATE (Requested → Refunding), so only one of two parallel
    /// approves reaches the processor; the loser gets "no withdrawal request waiting".
    /// </summary>
    public async Task ApproveWithdrawalAsync(GroupRegistration group, GroupAttendee a, CancellationToken ct)
    {
        var claimed = await db.Set<GroupAttendee>()
            .Where(x => x.Id == a.Id && x.GroupId == group.Id && x.IsActive && x.Withdrawal == WithdrawalStatus.Requested)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Withdrawal, WithdrawalStatus.Refunding), ct);
        if (claimed == 0)
            throw GroupValidationException.One("attendee", $"{a.Name} has no withdrawal request waiting.");

        // Past the claim, a client abort must not leave the attendee stuck in Refunding.
        var none = CancellationToken.None;
        GatewayResult result;
        PaymentOperation charge;
        int share;
        PaymentOrder order;
        try
        {
            order = await db.Orders.Include(o => o.Operations).SingleAsync(o => o.Id == group.OrderId, none);
            charge = order.Operations.First(o => o.Kind == PaymentKind.Charge && o.Succeeded);
            share = order.TotalCents / group.Attendees.Count;
            var refunded = order.Operations.Where(o => o.Kind == PaymentKind.Refund && o.Succeeded).Sum(o => o.AmountCents);
            if (refunded + share > charge.AmountCents)
                throw GroupValidationException.One("attendee", "This refund would exceed what was charged. Contact WinShape to finish it.");
            result = await gateway.RefundAsync(charge.ProcessorRef, share, none);
        }
        catch
        {
            await ReleaseClaimAsync(a.Id);
            throw;
        }

        await using var tx = await db.Database.BeginTransactionAsync(none);
        db.PaymentOperations.Add(new PaymentOperation
        {
            OrderId = order.Id,
            Kind = PaymentKind.Refund,
            AmountCents = share,
            Succeeded = result.Succeeded,
            ProcessorRef = result.ProcessorRef,
            CardLast4 = charge.CardLast4,
            Reason = $"Withdrawal: {a.Name}",
            CreatedAt = DateTime.UtcNow,
        });
        if (!result.Succeeded)
        {
            await db.SaveChangesAsync(none);
            await tx.CommitAsync(none);
            await ReleaseClaimAsync(a.Id);
            throw GroupValidationException.One("attendee", "The refund didn't go through. Nothing changed; try again in a few minutes.");
        }
        // Only the claim winner gets here, so the seat is released exactly once. If this save fails
        // after the refund, the attendee stays in Refunding (never re-claimable), so a retry can't
        // refund again; staff finish it from the processor record.
        a.IsActive = false;
        a.Withdrawal = WithdrawalStatus.Approved;
        a.WithdrawalResolvedAt = DateTime.UtcNow;
        var pool = await db.CapacityPools.Where(p => p.SessionId == group.SessionId).OrderBy(p => p.SortOrder).Select(p => p.Id).FirstAsync(none);
        await db.CapacityPools.Where(p => p.Id == pool && p.Reserved > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Reserved - 1), none);
        audit.Record("group.withdrawal_approved", "GroupAttendee", a.Id, $"{a.Name} withdrawn from {group.Name}; {Money(share)} refunded to card ending {charge.CardLast4}; seat released.");
        Outbox("GroupWithdrawalApproved", $"group-{group.Id}", new { to = a.Email, attendee = a.Name, refundCents = share });
        await db.SaveChangesAsync(none);
        await tx.CommitAsync(none);
    }

    Task<int> ReleaseClaimAsync(int attendeeId) =>
        db.Set<GroupAttendee>().Where(x => x.Id == attendeeId && x.Withdrawal == WithdrawalStatus.Refunding)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Withdrawal, WithdrawalStatus.Requested), CancellationToken.None);

    /// <summary>Keeps the attendee on the group. Conditional, so it can't race an approve that's refunding.</summary>
    public async Task DeclineWithdrawalAsync(GroupRegistration group, GroupAttendee a, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var declined = await db.Set<GroupAttendee>()
            .Where(x => x.Id == a.Id && x.GroupId == group.Id && x.IsActive && x.Withdrawal == WithdrawalStatus.Requested)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Withdrawal, WithdrawalStatus.Declined).SetProperty(x => x.WithdrawalResolvedAt, now), ct);
        if (declined == 0)
            throw GroupValidationException.One("attendee", $"{a.Name} has no withdrawal request waiting.");
        audit.Record("group.withdrawal_declined", "GroupAttendee", a.Id, $"{group.LeaderName} kept {a.Name} on {group.Name}; no refund.");
        Outbox("GroupWithdrawalDeclined", $"group-{group.Id}", new { to = a.Email, attendee = a.Name });
        await db.SaveChangesAsync(ct);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    public static bool IsEmail(string? s) =>
        s is { Length: > 3 and <= 254 } && MailAddress.TryCreate(s, out var m) && m.Address == s && s.Contains('.', StringComparison.Ordinal);

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static string Money(int cents) => (cents / 100m).ToString("C0", CultureInfo.GetCultureInfo("en-US"));

    void Outbox(string type, string aggregateId, object payload) =>
        db.OutboxEvents.Add(new OutboxEvent { Type = type, Target = "HubSpot", AggregateId = aggregateId, PayloadJson = JsonSerializer.Serialize(payload), CreatedAt = DateTime.UtcNow });
}
