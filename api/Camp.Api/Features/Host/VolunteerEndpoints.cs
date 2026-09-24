using System.Text;
using System.Text.Json;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Host;

public sealed record VolunteerRequest(string? FirstName, string? LastName, string? Email, string? Phone, string? DateOfBirth, string? Role);

public sealed record UploadRequest(string? FileName, string? Content);

/// <summary>
/// H2 · Volunteers and CSV batch upload (FR-88). Every row of an upload is stored with its outcome:
/// Valid rows wait for Submit, Error rows are held back until fixed or skipped, and nothing is dropped.
/// </summary>
public sealed class VolunteerEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        var host = app.MapGroup("/api/host").RequireAuthorization(Policies.Host);

        host.MapGet("/volunteers", async (CampDbContext db, StaffUser staff, CancellationToken ct) =>
        {
            var member = await HostScope.MemberAsync(db, staff, ct);
            if (member is null) return HostScope.NotLinkedResult();
            var rows = await db.Set<HostVolunteer>().AsNoTracking().Where(v => v.HostOrganizationId == member.HostOrganizationId)
                .OrderBy(v => v.LastName).ThenBy(v => v.FirstName).ToListAsync(ct);
            return Results.Ok(new
            {
                Counts = new
                {
                    Total = rows.Count,
                    Approved = rows.Count(v => v.VettingStatus == VettingStatus.Approved),
                    InProgress = rows.Count(v => v.VettingStatus == VettingStatus.InProgress),
                    NotStarted = rows.Count(v => v.VettingStatus == VettingStatus.NotStarted),
                },
                VolunteerCsv.Roles,
                Rows = rows.Select(v => new
                {
                    v.Id,
                    Name = $"{v.FirstName} {v.LastName}",
                    v.Email,
                    v.Phone,
                    v.Role,
                    v.VettingStatus,
                    v.CreatedAt,
                    FromUpload = v.UploadRowId != null,
                }),
            });
        });

        host.MapGet("/volunteers/template.csv", () =>
            Results.File(Encoding.UTF8.GetBytes(VolunteerCsv.Template), "text/csv", "volunteer-template.csv"));

        // Add one volunteer by hand. Same checks as a CSV row; sent to vetting straight away.
        host.MapPost("/volunteers", async (VolunteerRequest req, CampDbContext db, StaffUser staff, IAuditLog audit, CancellationToken ct, TimeProvider clock) =>
        {
            var member = await HostScope.MemberAsync(db, staff, ct);
            if (member is null) return HostScope.NotLinkedResult();
            var orgId = member.HostOrganizationId;
            var fields = Fields(req);
            var taken = await TakenEmails(db, orgId, ct);
            var problem = VolunteerCsv.Validate(fields, taken, new Dictionary<string, int>(), await EventStart(db, orgId, clock, ct));
            if (problem is not null) return HostScope.Invalid(problem.Field, $"{problem.Issue}. {problem.Detail}");

            var volunteer = ToVolunteer(orgId, fields, null, clock);
            db.Add(volunteer);
            audit.Record("host.volunteer_added", "HostVolunteer", fields.Email, $"Added {fields.FirstName} {fields.LastName} ({fields.Email}) and sent them to vetting.");
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException e) when (IsUniqueViolation(e))
            {
                return HostScope.Invalid("email", "Duplicate email. This email already exists in your volunteer list.");
            }
            db.OutboxEvents.Add(VettingEvent(volunteer, member.HostOrganization.Name, clock));
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/host/volunteers/{volunteer.Id}", new { volunteer.Id });
        });

        // The most recent upload, open (rows still to decide or submit) or not. 204 when there's none.
        host.MapGet("/uploads/latest", async (CampDbContext db, StaffUser staff, CancellationToken ct) =>
        {
            var member = await HostScope.MemberAsync(db, staff, ct);
            if (member is null) return HostScope.NotLinkedResult();
            var upload = await db.Set<VolunteerUpload>().AsNoTracking().Include(u => u.Rows)
                .Where(u => u.HostOrganizationId == member.HostOrganizationId)
                .OrderByDescending(u => u.Id).FirstOrDefaultAsync(ct);
            return upload is null ? Results.NoContent() : Results.Ok(View(upload));
        });

        host.MapPost("/uploads", async (UploadRequest req, CampDbContext db, StaffUser staff, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            var member = await HostScope.MemberAsync(db, staff, ct);
            if (member is null) return HostScope.NotLinkedResult();
            var orgId = member.HostOrganizationId;
            var fileName = Path.GetFileName(req.FileName?.Trim() ?? "");
            if (fileName.Length == 0) fileName = "volunteers.csv";
            if (fileName.Length > 200) fileName = fileName[^200..];
            var (parsed, error) = VolunteerCsv.Parse(req.Content ?? "");
            if (error is not null) return HostScope.Invalid("file", error);

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockOrganization(db, orgId, ct);
            var open = await OpenRows(db, orgId, ct);
            if (open > 0)
                return HostScope.Conflict($"Finish your open upload first: {open} {(open == 1 ? "row still needs" : "rows still need")} a fix, a skip, or Submit.");

            var upload = new VolunteerUpload { HostOrganizationId = orgId, FileName = fileName, UploadedBy = staff.Actor, CreatedAt = clock.UtcNow() };
            upload.Rows.AddRange(parsed.Select((f, i) => new VolunteerUploadRow
            {
                RowNumber = i + 1,
                FirstName = f.FirstName,
                LastName = f.LastName,
                Email = f.Email,
                Phone = f.Phone,
                DateOfBirth = f.DateOfBirth,
                Role = f.Role,
            }));
            Revalidate(upload.Rows, await TakenEmails(db, orgId, ct), await EventStart(db, orgId, clock, ct));
            db.Add(upload);
            var valid = upload.Rows.Count(r => r.Status == UploadRowStatus.Valid);
            audit.Record("host.volunteers_uploaded", "VolunteerUpload", fileName,
                $"Uploaded {fileName}: {upload.Rows.Count} rows, {valid} valid, {upload.Rows.Count - valid} with errors.");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(View(upload));
        });

        // Fix a row in place. The whole upload is checked again, since a changed email can
        // create or clear a duplicate elsewhere in the file.
        host.MapPut("/uploads/{id:int}/rows/{rowId:int}", async (int id, int rowId, VolunteerRequest req, CampDbContext db, StaffUser staff, IAuditLog audit, CancellationToken ct, TimeProvider clock) =>
            await ChangeRow(db, staff, id, rowId, (upload, row) =>
            {
                if (row.Status is not (UploadRowStatus.Error or UploadRowStatus.Valid)) return Decided(row);
                var f = Fields(req);
                (row.FirstName, row.LastName, row.Email, row.Phone, row.DateOfBirth, row.Role) = (f.FirstName, f.LastName, f.Email, f.Phone, f.DateOfBirth, f.Role);
                audit.Record("host.upload_row_fixed", "VolunteerUpload", upload.Id, $"Edited row {row.RowNumber} of {upload.FileName} ({f.FirstName} {f.LastName}).");
                return null;
            }, clock, ct));

        host.MapPost("/uploads/{id:int}/rows/{rowId:int}/skip", async (int id, int rowId, CampDbContext db, StaffUser staff, IAuditLog audit, CancellationToken ct, TimeProvider clock) =>
            await ChangeRow(db, staff, id, rowId, (upload, row) =>
            {
                if (row.Status is not (UploadRowStatus.Error or UploadRowStatus.Valid)) return Decided(row);
                row.Status = UploadRowStatus.Skipped;
                audit.Record("host.upload_row_skipped", "VolunteerUpload", upload.Id,
                    $"Skipped row {row.RowNumber} of {upload.FileName} ({Name(row)}){(row.Issue is null ? "" : $": {row.Issue}")}. Not sent to vetting.");
                return null;
            }, clock, ct));

        host.MapPost("/uploads/{id:int}/rows/{rowId:int}/restore", async (int id, int rowId, CampDbContext db, StaffUser staff, IAuditLog audit, CancellationToken ct, TimeProvider clock) =>
            await ChangeRow(db, staff, id, rowId, (upload, row) =>
            {
                if (row.Status != UploadRowStatus.Skipped) return HostScope.Conflict($"Row {row.RowNumber} isn't skipped.");
                row.Status = UploadRowStatus.Error; // checked again below
                audit.Record("host.upload_row_restored", "VolunteerUpload", upload.Id, $"Brought back row {row.RowNumber} of {upload.FileName} ({Name(row)}).");
                return null;
            }, clock, ct));

        // Valid rows become volunteers and go to vetting. Rows with errors stay held back and fixable.
        host.MapPost("/uploads/{id:int}/submit", async (int id, CampDbContext db, StaffUser staff, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            var member = await HostScope.MemberAsync(db, staff, ct);
            if (member is null) return HostScope.NotLinkedResult();
            var orgId = member.HostOrganizationId;

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockOrganization(db, orgId, ct);
            var upload = await db.Set<VolunteerUpload>().Include(u => u.Rows)
                .FirstOrDefaultAsync(u => u.Id == id && u.HostOrganizationId == orgId, ct);
            if (upload is null) return Results.NotFound();
            // Checked again under the lock: a volunteer added by hand since the preview may now clash.
            Revalidate(upload.Rows, await TakenEmails(db, orgId, ct), await EventStart(db, orgId, clock, ct));
            var ready = upload.Rows.Where(r => r.Status == UploadRowStatus.Valid).OrderBy(r => r.RowNumber).ToList();
            var heldBack = upload.Rows.Count(r => r.Status == UploadRowStatus.Error);
            if (ready.Count == 0)
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return HostScope.Conflict(heldBack > 0
                    ? $"Nothing to submit yet. Fix or skip the {heldBack} {(heldBack == 1 ? "row" : "rows")} with errors."
                    : "Everything in this upload was already sent to vetting.");
            }

            var volunteers = ready.Select(r =>
            {
                r.Status = UploadRowStatus.Submitted;
                return ToVolunteer(orgId, new VolunteerFields(r.FirstName, r.LastName, r.Email, r.Phone, r.DateOfBirth, r.Role), r.Id, clock);
            }).ToList();
            db.AddRange(volunteers);
            upload.LastSubmittedAt = clock.UtcNow();
            audit.Record("host.volunteers_submitted", "VolunteerUpload", upload.Id,
                $"Sent {volunteers.Count} volunteers from {upload.FileName} to vetting.{(heldBack > 0 ? $" {heldBack} {(heldBack == 1 ? "row" : "rows")} with errors held back." : "")}");
            await db.SaveChangesAsync(ct);
            db.OutboxEvents.AddRange(volunteers.Select(v => VettingEvent(v, member.HostOrganization.Name, clock)));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { Submitted = volunteers.Count, HeldBack = heldBack, Upload = View(upload) });
        });
    }

    static async Task<IResult> ChangeRow(CampDbContext db, StaffUser staff, int id, int rowId, Func<VolunteerUpload, VolunteerUploadRow, IResult?> change, TimeProvider clock, CancellationToken ct)
    {
        var member = await HostScope.MemberAsync(db, staff, ct);
        if (member is null) return HostScope.NotLinkedResult();
        var orgId = member.HostOrganizationId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockOrganization(db, orgId, ct);
        var upload = await db.Set<VolunteerUpload>().Include(u => u.Rows)
            .FirstOrDefaultAsync(u => u.Id == id && u.HostOrganizationId == orgId, ct);
        var row = upload?.Rows.FirstOrDefault(r => r.Id == rowId);
        if (upload is null || row is null) return Results.NotFound();
        if (change(upload, row) is { } refused) return refused;
        Revalidate(upload.Rows, await TakenEmails(db, orgId, ct), await EventStart(db, orgId, clock, ct));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(View(upload));
    }

    static IResult Decided(VolunteerUploadRow row) => HostScope.Conflict(row.Status == UploadRowStatus.Submitted
        ? $"Row {row.RowNumber} was already sent to vetting."
        : $"Row {row.RowNumber} is skipped. Bring it back before changing it.");

    /// <summary>Checks every undecided row in file order; an earlier row keeps an email, a later copy is the duplicate.</summary>
    static void Revalidate(List<VolunteerUploadRow> rows, ISet<string> taken, DateOnly eventStart)
    {
        var earlier = new Dictionary<string, int>();
        foreach (var row in rows.OrderBy(r => r.RowNumber))
        {
            if (row.Status is UploadRowStatus.Skipped or UploadRowStatus.Submitted) continue;
            var f = new VolunteerFields(row.FirstName, row.LastName, row.Email, row.Phone, row.DateOfBirth, row.Role);
            var problem = VolunteerCsv.Validate(f, taken, earlier, eventStart);
            row.Status = problem is null ? UploadRowStatus.Valid : UploadRowStatus.Error;
            (row.Issue, row.Detail, row.Field) = (problem?.Issue, problem?.Detail, problem?.Field);
            if (row.Email.Length > 0) earlier.TryAdd(VolunteerCsv.EmailKey(row.Email), row.RowNumber);
        }
    }

    static object View(VolunteerUpload u) => new
    {
        u.Id,
        u.FileName,
        u.UploadedBy,
        u.CreatedAt,
        u.LastSubmittedAt,
        Total = u.Rows.Count,
        Valid = u.Rows.Count(r => r.Status == UploadRowStatus.Valid),
        Errors = u.Rows.Count(r => r.Status == UploadRowStatus.Error),
        Skipped = u.Rows.Count(r => r.Status == UploadRowStatus.Skipped),
        Submitted = u.Rows.Count(r => r.Status == UploadRowStatus.Submitted),
        Rows = u.Rows.OrderBy(r => r.RowNumber).Select(r => new
        {
            r.Id,
            r.RowNumber,
            r.FirstName,
            r.LastName,
            r.Email,
            r.Phone,
            r.DateOfBirth,
            r.Role,
            r.Status,
            r.Issue,
            r.Detail,
            r.Field,
        }),
    };

    static VolunteerFields Fields(VolunteerRequest r) => new(
        VolunteerCsv.Clamp(r.FirstName), VolunteerCsv.Clamp(r.LastName), VolunteerCsv.Clamp(r.Email),
        VolunteerCsv.Clamp(r.Phone), VolunteerCsv.Clamp(r.DateOfBirth), VolunteerCsv.Clamp(r.Role));

    static HostVolunteer ToVolunteer(int orgId, VolunteerFields f, int? rowId, TimeProvider clock) => new()
    {
        HostOrganizationId = orgId,
        FirstName = f.FirstName,
        LastName = f.LastName,
        Email = f.Email,
        Phone = f.Phone,
        DateOfBirth = VolunteerCsv.ParseDate(f.DateOfBirth) ?? throw new InvalidOperationException("Only validated rows become volunteers."),
        Role = VolunteerCsv.RoleName(f.Role) ?? VolunteerCsv.DefaultRole,
        VettingStatus = VettingStatus.NotStarted,
        UploadRowId = rowId,
        CreatedAt = clock.UtcNow(),
    };

    static OutboxEvent VettingEvent(HostVolunteer v, string organization, TimeProvider clock) => new()
    {
        Type = "VolunteerVettingRequested",
        Target = "Vetting",
        AggregateId = $"volunteer-{v.Id}",
        PayloadJson = JsonSerializer.Serialize(new { v.Id, v.FirstName, v.LastName, v.Email, Organization = organization }),
        CreatedAt = clock.UtcNow(),
    };

    static string Name(VolunteerUploadRow r) => $"{r.FirstName} {r.LastName}".Trim() is { Length: > 0 } n ? n : "no name";

    static async Task<ISet<string>> TakenEmails(CampDbContext db, int orgId, CancellationToken ct) =>
        (await db.Set<HostVolunteer>().Where(v => v.HostOrganizationId == orgId).Select(v => v.Email).ToListAsync(ct))
            .Select(VolunteerCsv.EmailKey).ToHashSet();

    static async Task<DateOnly> EventStart(CampDbContext db, int orgId, TimeProvider clock, CancellationToken ct) =>
        (await HostScope.EventAsync(db, orgId, ct))?.Session.StartDate ?? clock.Today();

    static Task<int> OpenRows(CampDbContext db, int orgId, CancellationToken ct) =>
        db.Set<VolunteerUploadRow>()
            .Where(r => r.Status == UploadRowStatus.Valid || r.Status == UploadRowStatus.Error)
            .CountAsync(r => db.Set<VolunteerUpload>().Any(u => u.Id == r.UploadId && u.HostOrganizationId == orgId), ct);

    /// <summary>Serializes upload changes per organization, so two tabs can't submit the same rows twice.</summary>
    static Task<int> LockOrganization(CampDbContext db, int orgId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM HostOrganizations WITH (UPDLOCK, HOLDLOCK) WHERE Id = {orgId}", ct);

    static bool IsUniqueViolation(DbUpdateException e) =>
        e.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };
}
