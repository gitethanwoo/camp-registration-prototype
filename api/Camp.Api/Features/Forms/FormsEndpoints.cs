using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Setup;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Forms;

public record FormReturnInput(string? Note);

/// <summary>
/// K6 · Registration form builder (FR-74). The live version of a program's form is never edited:
/// an admin starts a draft (copied from the live version), edits it, and sends it for approval. A
/// different admin approves it, which retires the old version and makes the new one the one the
/// wizard asks. Registrations keep the version they answered (<see cref="FormAnswer.FormVersionId"/>).
/// </summary>
public sealed class FormsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        var forms = app.MapGroup("/api/admin/forms").RequireAuthorization(Policies.Admin);

        forms.MapGet("", async (CampDbContext db, CancellationToken ct) =>
        {
            // Only standard programs register through the wizard; admittance and cohort programs have their own forms.
            var programs = await db.Programs.AsNoTracking().Where(p => p.Type == ProgramType.Standard)
                .Select(p => new { p.Id, p.Name, Ministry = p.Ministry.Name, p.IsPublished, Legacy = p.Questions.Count })
                .OrderBy(p => p.Ministry).ThenBy(p => p.Name).ToListAsync(ct);
            var versions = await db.Set<FormVersion>().AsNoTracking()
                .Where(v => v.Status == FormVersionStatus.Published || v.Status == FormVersionStatus.Draft || v.Status == FormVersionStatus.PendingApproval)
                .Select(v => new { v.Id, v.ProgramId, v.Version, v.Status, v.PublishedAt, Questions = v.Questions.Count })
                .ToListAsync(ct);
            return Results.Ok(programs.Select(p =>
            {
                var live = versions.FirstOrDefault(v => v.ProgramId == p.Id && v.Status == FormVersionStatus.Published);
                var open = versions.FirstOrDefault(v => v.ProgramId == p.Id && v.Status != FormVersionStatus.Published);
                return new
                {
                    ProgramId = p.Id,
                    Program = p.Name,
                    p.Ministry,
                    ProgramPublished = p.IsPublished,
                    Live = live is null ? null : new { live.Id, live.Version, live.PublishedAt, live.Questions },
                    Open = open is null ? null : new { open.Id, open.Version, Status = open.Status.ToString(), StatusLabel = FormViews.StatusLabel(open.Status) },
                    LegacyQuestions = p.Legacy,
                };
            }));
        });

        forms.MapGet("/programs/{programId:int}", async (int programId, CampDbContext db, StaffUser staff, CancellationToken ct) =>
        {
            var p = await db.Programs.AsNoTracking().Include(x => x.Ministry).Include(x => x.Sessions)
                .FirstOrDefaultAsync(x => x.Id == programId && x.Type == ProgramType.Standard, ct);
            if (p is null) return Results.NotFound();
            var versions = await db.Set<FormVersion>().AsNoTracking().Include(v => v.Questions)
                .Where(v => v.ProgramId == programId).OrderByDescending(v => v.Version).ToListAsync(ct);
            var ids = versions.Select(v => v.Id).ToList();
            var registrations = await db.Set<FormAnswer>().Where(a => ids.Contains(a.FormVersionId) && a.RegistrationId != null)
                .GroupBy(a => a.FormVersionId).Select(g => new { g.Key, Count = g.Select(a => a.RegistrationId).Distinct().Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var session = p.Sessions.OrderBy(s => s.StartDate).FirstOrDefault();
            return Results.Ok(new
            {
                Program = new
                {
                    p.Id,
                    p.Name,
                    Ministry = p.Ministry.Name,
                    p.Location,
                    HealthMechanism = p.HealthMechanism.ToString(),
                    Session = session is null ? null : new { session.Name, session.StartDate, session.EndDate },
                },
                Versions = versions.Select(v => new
                {
                    v.Id,
                    v.Version,
                    Status = v.Status.ToString(),
                    StatusLabel = FormViews.StatusLabel(v.Status),
                    v.ChangeNote,
                    v.CreatedBy,
                    v.CreatedAt,
                    v.UpdatedAt,
                    v.SubmittedBy,
                    v.SubmittedAt,
                    EditedBy = FormViews.Editors(v),
                    v.ReturnNote,
                    v.ApprovedBy,
                    v.ApprovedAt,
                    v.PublishedAt,
                    v.RetiredAt,
                    Registrations = registrations.GetValueOrDefault(v.Id),
                    Editable = v.Status == FormVersionStatus.Draft,
                    CanApprove = v.Status == FormVersionStatus.PendingApproval && FormViews.ApprovalBlock(v, staff) is null,
                    ApprovalBlock = v.Status == FormVersionStatus.PendingApproval ? FormViews.ApprovalBlock(v, staff) : null,
                    Questions = v.Questions.OrderBy(q => q.SortOrder).Select(FormViews.Question),
                }),
            });
        });

        // Starts a draft from the live version (or, for a program's first form, from its existing questions).
        forms.MapPost("/programs/{programId:int}/drafts", async (int programId, CampDbContext db, StaffUser staff, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            var p = await db.Programs.AsNoTracking().Include(x => x.Questions).FirstOrDefaultAsync(x => x.Id == programId && x.Type == ProgramType.Standard, ct);
            if (p is null) return Results.NotFound();
            var versions = db.Set<FormVersion>().Where(v => v.ProgramId == programId);
            var inProgress = $"{p.Name} already has a form version in progress. Finish or discard it first.";
            if (await versions.AnyAsync(v => v.Status == FormVersionStatus.Draft || v.Status == FormVersionStatus.PendingApproval, ct))
                return SetupResults.Conflict(inProgress);
            var live = await FormRules.LiveAsync(db, programId, ct);
            var next = (await versions.MaxAsync(v => (int?)v.Version, ct) ?? 0) + 1;
            var now = clock.GetUtcNow().UtcDateTime;
            var draft = new FormVersion
            {
                ProgramId = programId,
                Version = next,
                Status = FormVersionStatus.Draft,
                CreatedBy = staff.Actor,
                CreatedByEmail = staff.Email,
                CreatedAt = now,
                UpdatedAt = now,
                Questions = live is not null
                    ? live.Questions.Select(FormViews.Copy).ToList()
                    : p.Questions.OrderBy(q => q.SortOrder).Select(FormViews.FromLegacy).ToList(),
            };
            db.Add(draft);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return SetupResults.Conflict(inProgress); }
            audit.Record(db, "form.draft_started", "Program", programId,
                live is null ? $"Started form v{next} for {p.Name} from its existing questions." : $"Started form v{next} for {p.Name} from live v{live.Version}.");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { draft.Id, draft.Version });
        });

        forms.MapPut("/versions/{id:int}", async (int id, FormDraftInput req, CampDbContext db, StaffUser staff, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            var v = await db.Set<FormVersion>().Include(x => x.Questions).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (v is null) return Results.NotFound();
            if (v.Status != FormVersionStatus.Draft) return SetupResults.Conflict(NotEditable(v.Status));
            var errors = FormRules.ValidateDraft(req);
            errors.Remove("questions"); // an empty draft can be saved; it can't be sent for approval
            if ((req.Questions?.Count ?? 0) > FormRules.MaxQuestions) errors["questions"] = [$"A form can have up to {FormRules.MaxQuestions} questions."];
            if (errors.Count > 0) return SetupResults.Invalid(errors);

            var before = (Outline: FormViews.Outline(v.Questions), v.ChangeNote);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.RemoveRange(v.Questions);
            await db.SaveChangesAsync(ct); // question keys are unique per version, so old rows go before new ones arrive
            v.Questions = FormRules.ToQuestions(req);
            v.ChangeNote = (req.ChangeNote ?? "").Trim();
            v.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            FormViews.RecordEditor(v, staff);
            var name = await ProgramName(db, v.ProgramId, ct);
            audit.Record(db, "form.draft_saved", "Program", v.ProgramId, $"Saved draft form v{v.Version} for {name}: {v.Questions.Count} questions.",
                ("Questions", before.Outline, FormViews.Outline(v.Questions)), ("Change note", before.ChangeNote, v.ChangeNote));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { v.UpdatedAt });
        });

        forms.MapPost("/versions/{id:int}/submit", async (int id, CampDbContext db, StaffUser staff, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            var v = await db.Set<FormVersion>().Include(x => x.Questions).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (v is null) return Results.NotFound();
            if (v.Status != FormVersionStatus.Draft) return SetupResults.Conflict("Only a draft can be sent for approval.");
            var errors = FormRules.ValidateDraft(new FormDraftInput(v.ChangeNote, v.Questions.OrderBy(q => q.SortOrder).Select(q =>
                new FormQuestionInput(q.Key, q.Label, q.HelpText, q.Type, q.Scope, q.Required, [.. q.OptionList], q.ShowWhenKey, q.ShowWhenValue, q.Health)).ToList()));
            if (string.IsNullOrWhiteSpace(v.ChangeNote)) errors["changeNote"] = ["Say what changed so the approving admin knows what to check."];
            var live = await FormRules.LiveAsync(db, v.ProgramId, ct);
            if (live is not null && FormViews.Signature(live.Questions) == FormViews.Signature(v.Questions))
                errors["questions"] = [$"This draft matches live v{live.Version}. Change a question before sending it for approval."];
            if (errors.Count > 0) return SetupResults.Invalid(errors);
            v.Status = FormVersionStatus.PendingApproval;
            v.SubmittedBy = staff.Actor;
            v.SubmittedByEmail = staff.Email;
            v.SubmittedAt = clock.GetUtcNow().UtcDateTime;
            v.ReturnNote = null;
            var name = await ProgramName(db, v.ProgramId, ct);
            audit.Record(db, "form.submitted", "Program", v.ProgramId, $"Sent form v{v.Version} for {name} for approval: {v.ChangeNote}",
                ("Status", "Draft", "Waiting for approval"));
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        forms.MapPost("/versions/{id:int}/approve", async (int id, CampDbContext db, StaffUser staff, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            var v = await db.Set<FormVersion>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (v is null) return Results.NotFound();
            if (v.Status != FormVersionStatus.PendingApproval) return SetupResults.Conflict("This version isn't waiting for approval.");
            if (FormViews.ApprovalBlock(v, staff) is { } block) return SetupResults.Forbidden(block);

            var now = clock.GetUtcNow().UtcDateTime;
            var live = await FormRules.LiveAsync(db, v.ProgramId, ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Retire first: one live version per program is a unique index.
            await db.Set<FormVersion>().Where(x => x.ProgramId == v.ProgramId && x.Status == FormVersionStatus.Published)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, FormVersionStatus.Retired).SetProperty(x => x.RetiredAt, now), ct);
            // Conditional: two admins approving at once publish it once.
            var won = await db.Set<FormVersion>().Where(x => x.Id == id && x.Status == FormVersionStatus.PendingApproval)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, FormVersionStatus.Published).SetProperty(x => x.PublishedAt, now)
                    .SetProperty(x => x.ApprovedBy, staff.Actor).SetProperty(x => x.ApprovedAt, now), ct);
            if (won == 0)
            {
                await tx.RollbackAsync(ct);
                return SetupResults.Conflict("Someone else already handled this version.");
            }
            var name = await ProgramName(db, v.ProgramId, ct);
            audit.Record(db, "form.published", "Program", v.ProgramId,
                $"Approved form v{v.Version} for {name}. Families registering from now answer v{v.Version}; earlier registrations keep the version they answered.",
                ("Live version", live is null ? "None" : $"v{live.Version}", $"v{v.Version}"));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok();
        });

        forms.MapPost("/versions/{id:int}/return", async (int id, FormReturnInput req, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Note)) return SetupResults.Invalid("note", "Say what needs to change so the author can fix it.");
            if (req.Note.Trim().Length > 500) return SetupResults.Invalid("note", "Notes are limited to 500 characters.");
            var v = await db.Set<FormVersion>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (v is null) return Results.NotFound();
            if (v.Status != FormVersionStatus.PendingApproval) return SetupResults.Conflict("This version isn't waiting for approval.");
            v.Status = FormVersionStatus.Draft;
            v.SubmittedAt = null;
            v.ReturnNote = req.Note.Trim();
            var name = await ProgramName(db, v.ProgramId, ct);
            audit.Record(db, "form.returned", "Program", v.ProgramId, $"Returned form v{v.Version} for {name} to draft: {v.ReturnNote}",
                ("Status", "Waiting for approval", "Draft"));
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        forms.MapDelete("/versions/{id:int}", async (int id, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var v = await db.Set<FormVersion>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (v is null) return Results.NotFound();
            if (v.Status != FormVersionStatus.Draft) return SetupResults.Conflict("Only a draft can be discarded.");
            var name = await ProgramName(db, v.ProgramId, ct);
            db.Remove(v);
            audit.Record(db, "form.draft_discarded", "Program", v.ProgramId, $"Discarded draft form v{v.Version} for {name}.");
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });
    }

    static string NotEditable(FormVersionStatus s) => s == FormVersionStatus.PendingApproval
        ? "This version is waiting for approval. Return it to draft to change it."
        : "Live and retired versions can't be changed. Start a new version instead.";

    static Task<string> ProgramName(CampDbContext db, int programId, CancellationToken ct) =>
        db.Programs.Where(p => p.Id == programId).Select(p => p.Name).FirstAsync(ct);
}
