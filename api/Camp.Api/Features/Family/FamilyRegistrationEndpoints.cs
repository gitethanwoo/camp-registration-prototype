using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Family;

public sealed record SignWaiverRequest(int WaiverId, int? PersonId, string SignerName);

/// <summary>F4 my registrations, F5 registration detail, F6 payments and receipts.</summary>
public sealed class FamilyRegistrationEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        var family = app.MapGroup("/api/family/registrations").RequireAuthorization(Policies.Family);

        // F4 · every registration across ministries, upcoming / past / cancelled (FR-50). One card per
        // order, because one checkout is one card, one payment plan, and one confirmation number.
        family.MapGet("", async (CampDbContext db, CurrentUser me) =>
        {
            var today = FamilyReadModel.Today;
            var orders = await FamilyReadModel.Orders(db, me.HouseholdId).AsNoTracking().ToListAsync();
            var waitlist = await db.WaitlistEntries.Where(w => w.HouseholdId == me.HouseholdId && w.OrderId != null)
                .Include(w => w.Person).AsNoTracking().ToListAsync();

            var cards = orders.Select(o =>
            {
                var waiting = waitlist.Where(w => w.OrderId == o.Id && w.Status is WaitlistStatus.Waiting or WaitlistStatus.Offered).ToList();
                var active = FamilyReadModel.Active(o);
                var group = o.Registrations.Count > 0 && active.Count == 0 ? "cancelled"
                    : o.Session.EndDate < today ? "past" : "upcoming";
                var money = FamilyReadModel.Money(o);
                return new
                {
                    Group = group,
                    o.ConfirmationCode,
                    Ministry = o.Session.Program.Ministry.Name,
                    Program = o.Session.Program.Name,
                    Session = o.Session.Name,
                    o.Session.StartDate,
                    o.Session.EndDate,
                    Status = active.Count > 0 ? active[0].Status.ToString() : waiting.Count > 0 ? "Waitlisted" : "Cancelled",
                    Participants = o.Registrations.OrderBy(r => r.Person.DateOfBirth).Select(r => new
                    {
                        r.Person.Id,
                        Name = r.Person.FullName,
                        GradeLabel = FamilyReadModel.GradeLabel(r),
                        Status = r.Status.ToString(),
                        Position = (int?)null,
                    }).Concat(waiting.Select(w => new
                    {
                        w.Person.Id,
                        Name = w.Person.FullName,
                        GradeLabel = (string?)null,
                        Status = w.Status == WaitlistStatus.Offered ? "OfferedSpot" : "Waitlisted",
                        Position = (int?)w.Position,
                    })).ToList(),
                    money.TotalCents,
                    money.PaidCents,
                    money.BalanceCents,
                    money.PaymentStatus,
                    PlanInstallments = o.Installments.Count,
                    PlanEachCents = o.Installments.OrderBy(i => i.Sequence).Select(i => (int?)i.AmountCents).FirstOrDefault(),
                };
            }).Where(c => c.Participants.Count > 0).ToList();

            return new
            {
                Upcoming = cards.Where(c => c.Group == "upcoming").OrderBy(c => c.StartDate),
                Past = cards.Where(c => c.Group == "past").OrderByDescending(c => c.StartDate),
                Cancelled = cards.Where(c => c.Group == "cancelled").OrderByDescending(c => c.StartDate),
            };
        });

        // F5 · one registration (order) as the family sees it: participants, checklist, money (FR-27, FR-42, FR-50).
        family.MapGet("/{code}", async (string code, CampDbContext db, CurrentUser me) =>
        {
            var o = await FamilyReadModel.Orders(db, me.HouseholdId).AsNoTracking().FirstOrDefaultAsync(x => x.ConfirmationCode == code);
            if (o is null) return Results.NotFound();
            var program = o.Session.Program;
            var money = FamilyReadModel.Money(o);
            var household = await db.Households.AsNoTracking().SingleAsync(h => h.Id == me.HouseholdId);
            var today = FamilyReadModel.Today;

            return Results.Ok(new
            {
                o.ConfirmationCode,
                Program = new { program.Name, program.Slug, program.Location, Ministry = program.Ministry.Name, HealthMechanism = program.HealthMechanism.ToString(), program.IsPublished },
                Session = new { o.Session.Id, o.Session.Name, o.Session.StartDate, o.Session.EndDate },
                IsPast = o.Session.EndDate < today,
                Signer = me.Name,
                Participants = o.Registrations.OrderBy(r => r.Person.DateOfBirth).Select(r =>
                {
                    var signed = r.WaiverAcceptances.Select(a => a.WaiverTemplateId).ToHashSet();
                    return new
                    {
                        RegistrationId = r.Id,
                        r.PersonId,
                        r.Person.FirstName,
                        r.Person.LastName,
                        GradeLabel = FamilyReadModel.GradeLabel(r),
                        Pool = r.Pool.Name,
                        Status = r.Status.ToString(),
                        HealthStatus = r.HealthStatus.ToString(),
                        Waivers = program.Waivers.OrderBy(w => w.Id).Select(w => new
                        {
                            w.Id,
                            w.Title,
                            w.Version,
                            w.PerParticipant,
                            Signed = signed.Contains(w.Id),
                            SignedBy = r.WaiverAcceptances.FirstOrDefault(a => a.WaiverTemplateId == w.Id)?.SignerName,
                        }),
                    };
                }),
                Waivers = program.Waivers.OrderBy(w => w.Id).Select(w => new { w.Id, w.Title, w.Version, w.EffectiveDate, w.Body, w.PerParticipant }),
                Checklist = FamilyReadModel.Checklist(o).ToList(),
                Payment = new
                {
                    Option = o.PaymentOption.ToString(),
                    o.Session.PriceCents,
                    ActiveCount = FamilyReadModel.Active(o).Count,
                    DiscountCents = FamilyReadModel.Active(o).Sum(r => r.DiscountCents),
                    o.DiscountCode,
                    money.TotalCents,
                    money.PaidCents,
                    money.BalanceCents,
                    money.PaymentStatus,
                    o.Session.BalanceDueDate,
                    Installments = o.Installments.OrderBy(i => i.Sequence).Select(i => new { i.Sequence, i.DueDate, i.AmountCents, Status = i.Status.ToString() }),
                },
                Household = new { household.Email },
            });
        });

        // F5 · sign a waiver that's still missing, e.g. after a new version was published.
        family.MapPost("/{code}/waivers", async (string code, SignWaiverRequest req, CampDbContext db, CurrentUser me, IAuditLog audit) =>
        {
            var o = await db.Orders.Include(x => x.Session).ThenInclude(s => s.Program).ThenInclude(p => p.Waivers)
                .Include(x => x.Registrations).ThenInclude(r => r.WaiverAcceptances)
                .Include(x => x.Registrations).ThenInclude(r => r.Person)
                .FirstOrDefaultAsync(x => x.ConfirmationCode == code && x.HouseholdId == me.HouseholdId && x.Status != OrderStatus.Declined);
            if (o is null) return Results.NotFound();
            var waiver = o.Session.Program.Waivers.FirstOrDefault(w => w.Id == req.WaiverId);
            if (waiver is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["waiverId"] = ["That waiver isn't part of this program."] });
            if (string.IsNullOrWhiteSpace(req.SignerName)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["signerName"] = ["Type your full name to sign."] });

            // A per-participant waiver is signed for one child; a household waiver covers everyone on the order.
            var targets = o.Registrations.Where(r => r.Status == RegistrationStatus.Confirmed)
                .Where(r => !waiver.PerParticipant || r.PersonId == req.PersonId)
                .Where(r => r.WaiverAcceptances.All(a => a.WaiverTemplateId != waiver.Id)).ToList();
            if (targets.Count == 0) return Results.Conflict(new { error = $"{waiver.Title} is already signed." });

            foreach (var r in targets)
                r.WaiverAcceptances.Add(new WaiverAcceptance { WaiverTemplateId = waiver.Id, Version = waiver.Version, SignerName = req.SignerName.Trim(), AcceptedAt = DateTime.UtcNow });
            audit.Record("waiver.signed", "PaymentOrder", o.Id, $"{req.SignerName.Trim()} signed {waiver.Title} v{waiver.Version} for {FamilyReadModel.Participants(targets.Select(r => r.Person.FirstName))}.");
            await db.SaveChangesAsync();
            return Results.Ok(new { Signed = targets.Count });
        });

        // F6 · payment history with receipts, the plan schedule, and what can be paid now (FR-47, FR-48, FR-50).
        family.MapGet("/{code}/payments", async (string code, CampDbContext db, CurrentUser me) =>
        {
            var o = await FamilyReadModel.Orders(db, me.HouseholdId).AsNoTracking().FirstOrDefaultAsync(x => x.ConfirmationCode == code);
            if (o is null) return Results.NotFound();
            var household = await db.Households.AsNoTracking().SingleAsync(h => h.Id == me.HouseholdId);
            var money = FamilyReadModel.Money(o);
            var active = FamilyReadModel.Active(o);
            var charges = o.Operations.Where(x => x.Succeeded && x.Kind is PaymentKind.Charge or PaymentKind.Refund).OrderBy(x => x.CreatedAt).ToList();
            var firstCharge = charges.FirstOrDefault(x => x.Kind == PaymentKind.Charge);
            var failed = o.Installments.Where(i => i.Status == InstallmentStatus.Failed).OrderBy(i => i.DueDate).FirstOrDefault();
            var next = o.Installments.Where(i => i.Status == InstallmentStatus.Scheduled).OrderBy(i => i.DueDate).FirstOrDefault();

            return Results.Ok(new
            {
                o.ConfirmationCode,
                Program = o.Session.Program.Name,
                Session = new { o.Session.Name, o.Session.StartDate, o.Session.EndDate, o.Session.BalanceDueDate },
                Household = new { household.Name, household.Email, Payer = me.Name },
                Lines = o.Registrations.OrderBy(r => r.Person.DateOfBirth).Select(r => new
                {
                    Name = r.Person.FullName,
                    GradeLabel = FamilyReadModel.GradeLabel(r),
                    r.PriceCents,
                    r.DiscountCents,
                    Cancelled = r.Status == RegistrationStatus.Cancelled,
                }),
                ActiveCount = active.Count,
                o.DiscountCode,
                DiscountCents = active.Sum(r => r.DiscountCents),
                money.TotalCents,
                money.PaidCents,
                money.BalanceCents,
                money.PaymentStatus,
                Option = o.PaymentOption.ToString(),
                History = charges.Select(x => new
                {
                    x.Id,
                    Kind = x.Kind.ToString(),
                    Label = x.Kind == PaymentKind.Refund ? "Refund"
                        : x == firstCharge ? o.PaymentOption switch { PaymentOption.Full => "Paid in full", _ => "Deposit" }
                        : x.Reason ?? "Payment",
                    x.AmountCents,
                    x.CardLast4,
                    x.ProcessorRef,
                    x.CreatedAt,
                    ReceiptNumber = $"{o.ConfirmationCode}-{x.Id}",
                }),
                Installments = o.Installments.OrderBy(i => i.Sequence).Select(i => new
                {
                    i.Sequence,
                    i.DueDate,
                    i.AmountCents,
                    Status = i.Status.ToString(),
                    GraceUntil = i.Status == InstallmentStatus.Failed ? i.DueDate.AddDays(FamilyReadModel.GraceDays) : (DateOnly?)null,
                }),
                FailedInstallment = failed is null ? null : new { failed.Sequence, failed.DueDate, failed.AmountCents, GraceUntil = failed.DueDate.AddDays(FamilyReadModel.GraceDays) },
                NextCharge = next is null ? null : new { next.DueDate, next.AmountCents },
            });
        });

        family.MapPost("/{code}/pay", async (string code, PayRequest req, CampDbContext db, IPaymentGateway gateway, IAuditLog audit, CurrentUser me, CancellationToken ct) =>
        {
            var result = await new BalancePaymentService(db, gateway, audit).PayAsync(me.HouseholdId, code, req, ct);
            return result.Outcome switch
            {
                PayOutcome.Succeeded => Results.Ok(result),
                PayOutcome.Declined => Results.Json(result, statusCode: StatusCodes.Status402PaymentRequired),
                PayOutcome.NotFound => Results.NotFound(),
                PayOutcome.Invalid => Results.ValidationProblem(new Dictionary<string, string[]> { ["payment"] = [result.Message ?? "Payment is invalid."] }),
                _ => Results.Conflict(new { error = result.Message }),
            };
        });
    }
}
