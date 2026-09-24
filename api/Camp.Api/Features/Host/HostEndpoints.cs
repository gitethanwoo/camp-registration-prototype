using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Host;

/// <summary>A dated to-do on the host home, linking to the page where it gets done.</summary>
public sealed record HostDeadline(string Kind, string Title, string Detail, DateOnly? Date, string Link, string LinkLabel);

/// <summary>H1 · Host home: the host's event, volunteers, invoice balance and deadlines (FR-87).</summary>
public sealed class HostEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        var host = app.MapGroup("/api/host").RequireAuthorization(Policies.Host);

        // The shell's header: which church this host acts for.
        host.MapGet("/me", async (CampDbContext db, StaffUser staff, CancellationToken ct) =>
        {
            var member = await HostScope.MemberAsync(db, staff, ct);
            if (member is null) return HostScope.NotLinkedResult();
            return Results.Ok(new
            {
                Organization = member.HostOrganization.Name,
                member.HostOrganization.City,
                member.Title,
                staff.Name,
                staff.Email,
            });
        });

        host.MapGet("/overview", async (CampDbContext db, StaffUser staff, CancellationToken ct) =>
        {
            var member = await HostScope.MemberAsync(db, staff, ct);
            if (member is null) return HostScope.NotLinkedResult();
            var orgId = member.HostOrganizationId;
            var ev = await HostScope.EventAsync(db, orgId, ct);

            var registrations = ev is null ? 0 : await db.Registrations
                .CountAsync(r => r.SessionId == ev.SessionId && HostScope.Counted.Contains(r.Status), ct);

            var byStatus = await db.Set<HostVolunteer>().Where(v => v.HostOrganizationId == orgId)
                .GroupBy(v => v.VettingStatus).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            int Count(VettingStatus s) => byStatus.FirstOrDefault(x => x.Key == s)?.Count ?? 0;
            var volunteers = new
            {
                Total = byStatus.Sum(x => x.Count),
                Approved = Count(VettingStatus.Approved),
                InProgress = Count(VettingStatus.InProgress),
                NotStarted = Count(VettingStatus.NotStarted),
            };

            var rows = await db.Set<VolunteerUploadRow>()
                .Where(r => db.Set<VolunteerUpload>().Any(u => u.Id == r.UploadId && u.HostOrganizationId == orgId))
                .Where(r => r.Status == UploadRowStatus.Error || r.Status == UploadRowStatus.Valid)
                .GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            var errorRows = rows.FirstOrDefault(r => r.Key == UploadRowStatus.Error)?.Count ?? 0;
            var readyRows = rows.FirstOrDefault(r => r.Key == UploadRowStatus.Valid)?.Count ?? 0;

            var invoices = await db.Set<HostInvoice>().AsNoTracking().Include(i => i.Lines).Include(i => i.Payments)
                .Where(i => i.HostOrganizationId == orgId).ToListAsync(ct);
            var open = invoices.Where(i => HostScope.Balance(i) > 0).OrderBy(i => i.DueDate).ToList();

            var deadlines = new List<HostDeadline>();
            if (errorRows > 0)
                deadlines.Add(new("upload", $"Fix {Plural(errorRows, "volunteer CSV row")}",
                    $"{Plural(errorRows, "row")} {(errorRows == 1 ? "needs" : "need")} a fix or a skip before {(errorRows == 1 ? "that volunteer" : "those volunteers")} can go to vetting.",
                    null, "/host/volunteers", "View volunteers"));
            if (readyRows > 0)
                deadlines.Add(new("upload", $"Submit {Plural(readyRows, "valid volunteer")} to vetting",
                    $"{Plural(readyRows, "row")} from your upload {(readyRows == 1 ? "is" : "are")} checked and waiting for Submit.",
                    null, "/host/volunteers", "View volunteers"));
            var notApproved = volunteers.Total - volunteers.Approved;
            if (ev is not null && notApproved > 0)
                deadlines.Add(new("vetting", $"Vetting done by {HostScope.Day(ev.VettingDeadline)}",
                    $"{volunteers.Approved} of {volunteers.Total} volunteers approved. {Plural(notApproved, "volunteer")} still {(notApproved == 1 ? "needs" : "need")} to finish their background check.",
                    ev.VettingDeadline, "/host/volunteers", "View volunteers"));
            foreach (var i in open)
                deadlines.Add(new("invoice", $"Pay {i.Number} by {HostScope.Day(i.DueDate)}",
                    $"{HostScope.Money(HostScope.Balance(i))} balance due for {i.Description}.",
                    i.DueDate, $"/host/invoices/{i.Id}", "View invoice"));

            return Results.Ok(new
            {
                Organization = member.HostOrganization.Name,
                member.HostOrganization.City,
                Event = ev is null ? null : new
                {
                    Program = ev.Session.Program.Name,
                    Session = ev.Session.Name,
                    ev.Session.StartDate,
                    ev.Session.EndDate,
                    ev.Session.Program.Location,
                    Registrations = registrations,
                    Capacity = ev.Session.Pools.Sum(p => p.Capacity),
                    LastYear = ev.LastYearRegistrations,
                    ev.VettingDeadline,
                },
                Volunteers = volunteers,
                Upload = new { NeedsDecision = errorRows, ReadyToSubmit = readyRows },
                Invoices = new
                {
                    BalanceCents = open.Sum(HostScope.Balance),
                    OpenCount = open.Count,
                    NextDue = open.Select(i => new { i.Id, i.Number, i.DueDate, BalanceCents = HostScope.Balance(i) }).FirstOrDefault(),
                },
                Deadlines = deadlines.OrderBy(d => d.Date.HasValue).ThenBy(d => d.Date),
            });
        });
    }

    static string Plural(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
}
