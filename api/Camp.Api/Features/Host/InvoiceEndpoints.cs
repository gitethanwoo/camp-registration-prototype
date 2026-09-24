using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Infrastructure;
using Camp.Api.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Host;

/// <summary>H3 · The host's invoices from WinShape, line items, and Pay through Fiserv (FR-89).</summary>
public sealed class InvoiceEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        var host = app.MapGroup("/api/host/invoices").RequireAuthorization(Policies.Host);

        host.MapGet("", async (CampDbContext db, StaffUser staff, CancellationToken ct) =>
        {
            var member = await HostScope.MemberAsync(db, staff, ct);
            if (member is null) return HostScope.NotLinkedResult();
            var invoices = await Load(db, member.HostOrganizationId).OrderByDescending(i => i.DueDate).ToListAsync(ct);
            return Results.Ok(invoices.Select(Summary));
        });

        host.MapGet("/{id:int}", async (int id, CampDbContext db, StaffUser staff, CancellationToken ct) =>
        {
            var member = await HostScope.MemberAsync(db, staff, ct);
            if (member is null) return HostScope.NotLinkedResult();
            var invoice = await Load(db, member.HostOrganizationId).FirstOrDefaultAsync(i => i.Id == id, ct);
            if (invoice is null) return Results.NotFound();
            var ev = invoice.HostEventId is { } eventId
                ? await db.Set<HostEvent>().AsNoTracking().Include(e => e.Session).ThenInclude(s => s.Program)
                    .FirstOrDefaultAsync(e => e.Id == eventId, ct)
                : null;
            return Results.Ok(new
            {
                Invoice = Summary(invoice),
                Organization = member.HostOrganization.Name,
                member.HostOrganization.City,
                Event = ev is null ? null : new { Program = ev.Session.Program.Name, ev.Session.StartDate, ev.Session.EndDate, ev.Session.Program.Location },
                invoice.IssuedOn,
                Lines = invoice.Lines.OrderBy(l => l.SortOrder).Select(l => new { l.Id, l.Description, l.AmountCents }),
                Payments = invoice.Payments.Where(p => p.Status != HostPaymentStatus.Pending).OrderByDescending(p => p.CreatedAt)
                    .Select(p => new { p.Id, p.AmountCents, p.Status, p.CardLast4, p.DeclineReason, p.PaidBy, p.CreatedAt }),
            });
        });

        host.MapPost("/{id:int}/pay", async (int id, InvoicePayRequest req, CampDbContext db, StaffUser staff, IPaymentGateway gateway, IAuditLog audit, CancellationToken ct) =>
        {
            var member = await HostScope.MemberAsync(db, staff, ct);
            if (member is null) return HostScope.NotLinkedResult();
            var result = await new InvoicePaymentService(db, gateway, audit).PayAsync(member.HostOrganizationId, id, req, staff.Actor, ct);
            return result.Outcome switch
            {
                InvoicePayOutcome.Succeeded => Results.Ok(result),
                InvoicePayOutcome.Declined => Results.Json(result, statusCode: StatusCodes.Status402PaymentRequired),
                InvoicePayOutcome.NotFound => Results.NotFound(),
                InvoicePayOutcome.Invalid => Results.BadRequest(new { error = result.Message }),
                _ => Results.Conflict(new { error = result.Message }),
            };
        });
    }

    static IQueryable<HostInvoice> Load(CampDbContext db, int orgId) =>
        db.Set<HostInvoice>().AsNoTracking().AsSplitQuery().Include(i => i.Lines).Include(i => i.Payments)
            .Where(i => i.HostOrganizationId == orgId);

    static object Summary(HostInvoice i) => new
    {
        i.Id,
        i.Number,
        i.Description,
        i.Period,
        i.DueDate,
        TotalCents = HostScope.Total(i),
        PaidCents = HostScope.Paid(i),
        BalanceCents = HostScope.Balance(i),
        Status = HostScope.Balance(i) > 0 ? "Balance due" : "Paid",
        PaidOn = HostScope.Balance(i) > 0 ? null
            : i.Payments.Where(p => p.Status == HostPaymentStatus.Succeeded).Max(p => (DateTime?)p.CreatedAt),
    };
}
