using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Setup;

public record WaiverDraftInput(string Body, string ChangeNote);
public record WaiverReturnInput(string? Note);

/// <summary>
/// K7 · Waiver templates and versions (FR-73). A published version is never edited: changes go into
/// a new draft, which a second admin approves. Approval makes it the version checkout asks for from
/// that day on; families who signed earlier keep their signed version (WaiverAcceptance.Version).
/// </summary>
public sealed class WaiverSetupEndpoints : IEndpointModule
{
    public const int MaxBody = 8000;

    public void Map(IEndpointRouteBuilder app)
    {
        var setup = app.MapGroup("/api/admin/setup").RequireAuthorization(Policies.Admin);

        setup.MapGet("/waivers", async (CampDbContext db, CancellationToken ct) =>
        {
            var templates = await db.WaiverTemplates.AsNoTracking().OrderBy(t => t.Title).ToListAsync(ct);
            var programs = await db.Programs.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            var open = await db.Set<WaiverVersion>().AsNoTracking()
                .Where(v => v.Status == WaiverVersionStatus.Draft || v.Status == WaiverVersionStatus.PendingApproval)
                .ToDictionaryAsync(v => v.WaiverTemplateId, ct);
            var signed = await db.WaiverAcceptances.GroupBy(a => a.WaiverTemplateId).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            return Results.Ok(templates.Select(t => new
            {
                t.Id,
                t.Title,
                Program = programs.GetValueOrDefault(t.ProgramId),
                LiveVersion = t.Version,
                t.EffectiveDate,
                Signer = t.PerParticipant ? "Each camper" : "Once per household",
                Signatures = signed.GetValueOrDefault(t.Id),
                OpenVersion = open.TryGetValue(t.Id, out var v) ? new { v.Id, v.Version, Status = StatusLabel(v.Status) } : null,
            }));
        });

        setup.MapGet("/waivers/{id:int}", async (int id, CampDbContext db, StaffUser staff, CancellationToken ct) =>
        {
            var t = await db.WaiverTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Results.NotFound();
            var program = await db.Programs.AsNoTracking().Where(p => p.Id == t.ProgramId).Select(p => p.Name).FirstAsync(ct);
            var versions = await db.Set<WaiverVersion>().AsNoTracking().Where(v => v.WaiverTemplateId == id).OrderByDescending(v => v.Version).ToListAsync(ct);
            var counts = await db.WaiverAcceptances.Where(a => a.WaiverTemplateId == id).GroupBy(a => a.Version)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var recent = await db.WaiverAcceptances.AsNoTracking().Where(a => a.WaiverTemplateId == id).OrderByDescending(a => a.AcceptedAt).Take(8)
                .Join(db.Registrations, a => a.RegistrationId, r => r.Id, (a, r) => new
                {
                    a.Id,
                    a.SignerName,
                    Camper = r.Person.FirstName + " " + r.Person.LastName,
                    Session = r.Session.Name,
                    a.Version,
                    a.AcceptedAt,
                }).ToListAsync(ct);
            return Results.Ok(new
            {
                t.Id,
                t.Title,
                Program = program,
                LiveVersion = t.Version,
                t.EffectiveDate,
                Signer = t.PerParticipant ? "Each camper" : "Once per household",
                Signatures = counts.Values.Sum(),
                Versions = versions.Select(v => new
                {
                    v.Id,
                    v.Version,
                    v.Body,
                    v.ChangeNote,
                    Status = v.Status.ToString(),
                    StatusLabel = StatusLabel(v.Status),
                    v.EffectiveDate,
                    v.RetiredDate,
                    v.CreatedBy,
                    v.CreatedAt,
                    v.SubmittedBy,
                    v.SubmittedAt,
                    v.ApprovedBy,
                    v.ApprovedAt,
                    Signatures = counts.GetValueOrDefault(v.Version),
                    Editable = v.Status == WaiverVersionStatus.Draft,
                    CanApprove = v.Status == WaiverVersionStatus.PendingApproval && ApprovalBlock(v, staff) is null,
                    ApprovalBlock = v.Status == WaiverVersionStatus.PendingApproval ? ApprovalBlock(v, staff) : null,
                }),
                Recent = recent,
            });
        });

        // Starts a new draft from the live text. One open draft per template at a time.
        setup.MapPost("/waivers/{id:int}/drafts", async (int id, CampDbContext db, StaffUser staff, IAuditLog audit, CancellationToken ct) =>
        {
            var t = await db.WaiverTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Results.NotFound();
            var versions = db.Set<WaiverVersion>().Where(v => v.WaiverTemplateId == id);
            if (await versions.AnyAsync(v => v.Status == WaiverVersionStatus.Draft || v.Status == WaiverVersionStatus.PendingApproval, ct))
                return SetupResults.Conflict($"{t.Title} already has a version in progress. Finish or discard it first.");
            var next = Math.Max(t.Version, await versions.MaxAsync(v => (int?)v.Version, ct) ?? 0) + 1;
            var draft = new WaiverVersion
            {
                WaiverTemplateId = id,
                Version = next,
                Body = t.Body,
                ChangeNote = "",
                Status = WaiverVersionStatus.Draft,
                CreatedBy = staff.Actor,
                CreatedAt = DateTime.UtcNow,
            };
            db.Set<WaiverVersion>().Add(draft);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return SetupResults.Conflict($"{t.Title} already has a version in progress. Finish or discard it first."); }
            audit.Record(db, "waiver.draft_started", "WaiverTemplate", id, $"Started version {next} of {t.Title}.");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { draft.Id, draft.Version });
        });

        setup.MapPut("/waiver-versions/{vid:int}", async (int vid, WaiverDraftInput req, CampDbContext db, CancellationToken ct) =>
        {
            var v = await db.Set<WaiverVersion>().FirstOrDefaultAsync(x => x.Id == vid, ct);
            if (v is null) return Results.NotFound();
            if (v.Status != WaiverVersionStatus.Draft)
                return SetupResults.Conflict(v.Status == WaiverVersionStatus.PendingApproval
                    ? "This version is waiting for approval. Return it to draft to change it."
                    : "Published versions can't be changed. Start a new version instead.");
            var errors = ValidateDraft(req.Body, req.ChangeNote, requireNote: false);
            if (errors.Count > 0) return SetupResults.Invalid(errors);
            v.Body = req.Body.Trim();
            v.ChangeNote = (req.ChangeNote ?? "").Trim();
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        setup.MapPost("/waiver-versions/{vid:int}/submit", async (int vid, CampDbContext db, StaffUser staff, IAuditLog audit, CancellationToken ct) =>
        {
            var v = await db.Set<WaiverVersion>().FirstOrDefaultAsync(x => x.Id == vid, ct);
            if (v is null) return Results.NotFound();
            if (v.Status != WaiverVersionStatus.Draft) return SetupResults.Conflict("Only a draft can be sent for approval.");
            var t = await db.WaiverTemplates.AsNoTracking().FirstAsync(x => x.Id == v.WaiverTemplateId, ct);
            var errors = ValidateDraft(v.Body, v.ChangeNote, requireNote: true);
            if (v.Body == t.Body) errors["body"] = ["This draft matches the live version. Change the text before sending it for approval."];
            if (errors.Count > 0) return SetupResults.Invalid(errors);
            v.Status = WaiverVersionStatus.PendingApproval;
            v.SubmittedBy = staff.Actor;
            v.SubmittedByEmail = staff.Email;
            v.SubmittedAt = DateTime.UtcNow;
            audit.Record(db, "waiver.submitted", "WaiverTemplate", t.Id, $"Sent version {v.Version} of {t.Title} for approval: {v.ChangeNote}");
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        setup.MapPost("/waiver-versions/{vid:int}/approve", async (int vid, CampDbContext db, StaffUser staff, IAuditLog audit, CancellationToken ct) =>
        {
            var v = await db.Set<WaiverVersion>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == vid, ct);
            if (v is null) return Results.NotFound();
            if (v.Status != WaiverVersionStatus.PendingApproval) return SetupResults.Conflict("This version isn't waiting for approval.");
            if (ApprovalBlock(v, staff) is { } block) return SetupResults.Forbidden(block);

            var today = SetupResults.Today;
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Conditional: two admins approving at once publish it once.
            var won = await db.Set<WaiverVersion>().Where(x => x.Id == vid && x.Status == WaiverVersionStatus.PendingApproval)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, WaiverVersionStatus.Published).SetProperty(x => x.EffectiveDate, today)
                    .SetProperty(x => x.ApprovedBy, staff.Actor).SetProperty(x => x.ApprovedAt, DateTime.UtcNow), ct);
            if (won == 0) return SetupResults.Conflict("Someone else already handled this version.");
            await db.Set<WaiverVersion>().Where(x => x.WaiverTemplateId == v.WaiverTemplateId && x.Id != vid && x.Status == WaiverVersionStatus.Published)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, WaiverVersionStatus.Archived).SetProperty(x => x.RetiredDate, today), ct);
            var t = await db.WaiverTemplates.FirstAsync(x => x.Id == v.WaiverTemplateId, ct);
            var old = t.Version;
            t.Version = v.Version;
            t.Body = v.Body;
            t.EffectiveDate = today;
            audit.Record(db, "waiver.published", "WaiverTemplate", t.Id,
                $"Approved version {v.Version} of {t.Title}. Checkout asks for it from {SetupResults.Date(today)}; earlier signatures stay on their version.",
                ("Live version", $"v{old}", $"v{v.Version}"));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok();
        });

        setup.MapPost("/waiver-versions/{vid:int}/return", async (int vid, WaiverReturnInput req, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Note)) return SetupResults.Invalid("note", "Say what needs to change so the author can fix it.");
            var v = await db.Set<WaiverVersion>().FirstOrDefaultAsync(x => x.Id == vid, ct);
            if (v is null) return Results.NotFound();
            if (v.Status != WaiverVersionStatus.PendingApproval) return SetupResults.Conflict("This version isn't waiting for approval.");
            var title = await db.WaiverTemplates.Where(x => x.Id == v.WaiverTemplateId).Select(x => x.Title).FirstAsync(ct);
            v.Status = WaiverVersionStatus.Draft;
            v.SubmittedAt = null;
            audit.Record(db, "waiver.returned", "WaiverTemplate", v.WaiverTemplateId, $"Returned version {v.Version} of {title} to draft: {req.Note.Trim()}");
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        setup.MapDelete("/waiver-versions/{vid:int}", async (int vid, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var v = await db.Set<WaiverVersion>().FirstOrDefaultAsync(x => x.Id == vid, ct);
            if (v is null) return Results.NotFound();
            if (v.Status != WaiverVersionStatus.Draft) return SetupResults.Conflict("Only a draft can be discarded.");
            var title = await db.WaiverTemplates.Where(x => x.Id == v.WaiverTemplateId).Select(x => x.Title).FirstAsync(ct);
            db.Remove(v);
            audit.Record(db, "waiver.draft_discarded", "WaiverTemplate", v.WaiverTemplateId, $"Discarded draft version {v.Version} of {title}.");
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });
    }

    /// <summary>Why this admin can't approve the version, or null. The author can't approve their own change.</summary>
    static string? ApprovalBlock(WaiverVersion v, StaffUser staff) =>
        (v.SubmittedByEmail is not null && string.Equals(v.SubmittedByEmail, staff.Email, StringComparison.OrdinalIgnoreCase))
        || string.Equals(v.SubmittedBy, staff.Actor, StringComparison.Ordinal)
        || string.Equals(v.CreatedBy, staff.Actor, StringComparison.Ordinal)
            ? "You wrote this version, so a different admin has to approve it."
            : null;

    static Dictionary<string, string[]> ValidateDraft(string? body, string? note, bool requireNote)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(body)) errors["body"] = ["The waiver text can't be empty."];
        else if (body.Trim().Length > MaxBody) errors["body"] = [$"Waiver text is limited to {MaxBody:N0} characters."];
        if (requireNote && string.IsNullOrWhiteSpace(note)) errors["changeNote"] = ["Add a change note so approvers and auditors know what changed."];
        else if ((note ?? "").Trim().Length > 300) errors["changeNote"] = ["Change notes are limited to 300 characters."];
        return errors;
    }

    static string StatusLabel(WaiverVersionStatus s) => s switch
    {
        WaiverVersionStatus.PendingApproval => "Waiting for approval",
        WaiverVersionStatus.Published => "Live",
        WaiverVersionStatus.Archived => "Retired",
        _ => "Draft",
    };
}
