using System.Text.RegularExpressions;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Access;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Setup;

public record ProgramInput(int MinistryId, string Name, ProgramType Type, HealthMechanism HealthMechanism, string Location, string? Tagline, string? Description);
public record ReturnToDraftRequest(string? Note);

/// <summary>
/// K2 · Program list and editor (FR-67, FR-73). A program is edited only as a Draft, submitted for
/// approval, and published by the last step of its approval chain. The submitter never approves,
/// and one person never approves two steps.
/// </summary>
public sealed partial class ProgramSetupEndpoints : IEndpointModule
{
    /// <summary>The approval chain every program goes through, in order.</summary>
    public static readonly (string Role, string Description)[] Chain =
    [
        ("Camp director", "Reviews program details, forms, waivers and sessions."),
        ("Ministry owner", "Final approval. Approving publishes the program to families."),
    ];

    public void Map(IEndpointRouteBuilder app)
    {
        var setup = app.MapGroup("/api/admin/setup").RequireAuthorization(Policies.Admin);

        setup.MapGet("/programs", async (CampDbContext db, StaffUser staff, CancellationToken ct) =>
        {
            var programs = await db.Programs.AsNoTracking().Include(p => p.Ministry).Include(p => p.Waivers).Include(p => p.Questions)
                .Include(p => p.Sessions).ThenInclude(s => s.Pools)
                .OrderBy(p => p.Ministry.Id).ThenBy(p => p.Id).ToListAsync(ct);
            var setups = await db.Set<ProgramSetup>().AsNoTracking().Include(x => x.Steps).ToDictionaryAsync(x => x.ProgramId, ct);
            var registered = await ActiveRegistrations(db).GroupBy(r => r.SessionId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var ministries = await db.Ministries.AsNoTracking().OrderBy(m => m.Id).Select(m => new { m.Id, m.Code, m.Name }).ToListAsync(ct);
            return Results.Ok(new
            {
                Ministries = ministries,
                Rows = programs.Select(p => ToRow(p, setups.GetValueOrDefault(p.Id), registered, staff)),
            });
        });

        setup.MapPost("/programs", async (ProgramInput req, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var errors = await Validate(db, req, ct);
            // K9's rule, kept in one place: CampDoc outside Overnight Camp needs Health settings' explicit confirmation.
            if (req.HealthMechanism == HealthMechanism.CampDoc && !HealthAccessRules.IsOvernight("", $"{req.Name}"))
                errors["healthMechanism"] = ["CampDoc is used by Overnight Camp only. Create the program with another method, then change it under Setup › Health settings if you mean it."];
            if (errors.Count > 0) return SetupResults.Invalid(errors);
            var ministry = await db.Ministries.SingleAsync(m => m.Id == req.MinistryId, ct);
            var program = new CampProgram
            {
                MinistryId = req.MinistryId,
                Slug = await UniqueSlug(db, req.Name, ct),
                Name = req.Name.Trim(),
                Tagline = req.Tagline?.Trim() ?? "",
                Description = req.Description?.Trim() ?? "",
                Type = req.Type,
                HealthMechanism = req.HealthMechanism,
                Location = req.Location.Trim(),
                ImageUrl = "/images/overnight.jpg",
                IsPublished = false,
            };
            db.Programs.Add(program);
            await db.SaveChangesAsync(ct);
            db.Set<ProgramSetup>().Add(new ProgramSetup { ProgramId = program.Id, State = PublishState.Draft });
            audit.Record(db, "program.created", "Program", program.Id, $"Created {program.Name} ({ministry.Name}) as a draft.",
                ("Name", null, program.Name), ("Type", null, TypeLabel(program.Type)), ("State", null, "Draft"));
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { program.Id });
        });

        setup.MapPut("/programs/{id:int}", async (int id, ProgramInput req, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var program = await db.Programs.Include(p => p.Ministry).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (program is null) return Results.NotFound();
            var state = await StateOf(db, program, ct);
            if (state.State != PublishState.Draft)
                return SetupResults.Conflict($"{program.Name} is {StateLabel(state.State).ToLowerInvariant()}, so it can't be edited. Return it to draft first.");
            var errors = await Validate(db, req, ct);
            if (errors.Count > 0) return SetupResults.Invalid(errors);
            // Health collection changes go through Setup › Health settings (K9), which guards CampDoc and audits
            // health.settings_changed; this sheet shows the method read-only.
            if (req.HealthMechanism != program.HealthMechanism)
                return SetupResults.Invalid("healthMechanism", $"Change how {program.Name} collects health information under Setup › Health settings.");
            if (req.Type != program.Type && await db.Registrations.AnyAsync(r => r.Session.ProgramId == id, ct))
                return SetupResults.Invalid("type", $"{program.Name} already has registrations, so its type can't change.");

            var before = (program.Name, Type: TypeLabel(program.Type), Health: program.HealthMechanism.ToString(), program.Location, program.Tagline, program.Description, Ministry: program.Ministry.Name);
            program.MinistryId = req.MinistryId;
            program.Name = req.Name.Trim();
            program.Type = req.Type;
            program.HealthMechanism = req.HealthMechanism;
            program.Location = req.Location.Trim();
            program.Tagline = req.Tagline?.Trim() ?? "";
            program.Description = req.Description?.Trim() ?? "";
            var ministry = await db.Ministries.AsNoTracking().SingleAsync(m => m.Id == req.MinistryId, ct);
            audit.Record(db, "program.updated", "Program", id, $"Edited {program.Name}.",
                ("Name", before.Name, program.Name), ("Ministry", before.Ministry, ministry.Name), ("Type", before.Type, TypeLabel(program.Type)),
                ("Health information", before.Health, program.HealthMechanism.ToString()), ("Location", before.Location, program.Location),
                ("Tagline", before.Tagline, program.Tagline), ("Description", before.Description, program.Description));
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        setup.MapPost("/programs/{id:int}/submit", async (int id, CampDbContext db, StaffUser staff, IAuditLog audit, CancellationToken ct) =>
        {
            var program = await db.Programs.Include(p => p.Sessions).ThenInclude(s => s.Pools).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (program is null) return Results.NotFound();
            var setup = await StateOf(db, program, ct);
            if (setup.State != PublishState.Draft) return SetupResults.Conflict($"{program.Name} is already {StateLabel(setup.State).ToLowerInvariant()}.");
            if (!program.Sessions.Any(s => s.Pools.Count > 0))
                return SetupResults.Invalid("sessions", "Add at least one session with a capacity pool before submitting.");

            setup.State = PublishState.PendingApproval;
            setup.SubmittedBy = staff.Actor;
            setup.SubmittedByEmail = staff.Email;
            setup.SubmittedAt = DateTime.UtcNow;
            setup.ReturnNote = null;
            ResetSteps(db, setup);
            audit.Record(db, "program.submitted", "Program", id, $"Submitted {program.Name} for approval.", ("State", "Draft", "Pending approval"));
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        setup.MapPost("/programs/{id:int}/approve", async (int id, CampDbContext db, StaffUser staff, IAuditLog audit, CancellationToken ct) =>
        {
            var program = await db.Programs.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (program is null) return Results.NotFound();
            var setup = await StateOf(db, program, ct);
            if (setup.State != PublishState.PendingApproval) return SetupResults.Conflict($"{program.Name} isn't waiting for approval.");
            var step = setup.Steps.OrderBy(s => s.Sequence).FirstOrDefault(s => s.ApprovedAt is null);
            if (step is null) return SetupResults.Conflict($"Every step for {program.Name} is already approved.");
            var blocked = ApprovalBlock(setup, staff);
            if (blocked is not null) return SetupResults.Forbidden(blocked);

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Conditional update: of two people approving the same step at once, only one counts.
            var now = (DateTime?)DateTime.UtcNow;
            var won = await db.Set<ProgramApprovalStep>().Where(s => s.Id == step.Id && s.ApprovedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ApprovedAt, now).SetProperty(x => x.ApprovedBy, staff.Actor).SetProperty(x => x.ApprovedByEmail, staff.Email), ct);
            if (won == 0) return SetupResults.Conflict($"The {step.Role.ToLowerInvariant()} step was just approved by someone else. Reload to see it.");
            var last = setup.Steps.All(s => s.Id == step.Id || s.ApprovedAt is not null);
            if (last)
            {
                setup.State = PublishState.Published;
                program.IsPublished = true;
                audit.Record(db, "program.published", "Program", id, $"Approved the {step.Role.ToLowerInvariant()} step for {program.Name}. It is now published to families.",
                    ("State", "Pending approval", "Published"), ($"{step.Role} approval", "Pending", staff.Actor));
            }
            else
            {
                audit.Record(db, "program.step_approved", "Program", id, $"Approved the {step.Role.ToLowerInvariant()} step for {program.Name}.",
                    ($"{step.Role} approval", "Pending", staff.Actor));
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { Published = last });
        });

        setup.MapPost("/programs/{id:int}/return", async (int id, ReturnToDraftRequest req, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var program = await db.Programs.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (program is null) return Results.NotFound();
            var setup = await StateOf(db, program, ct);
            if (setup.State == PublishState.Draft) return SetupResults.Conflict($"{program.Name} is already a draft.");
            var note = req.Note?.Trim();
            if (string.IsNullOrEmpty(note)) return SetupResults.Invalid("note", "Say why it's going back to draft. The submitter sees this note.");
            if (note.Length > 1000) return SetupResults.Invalid("note", "Notes are limited to 1,000 characters.");

            var from = StateLabel(setup.State);
            setup.State = PublishState.Draft;
            setup.ReturnNote = note;
            program.IsPublished = false;
            ResetSteps(db, setup);
            audit.Record(db, "program.returned", "Program", id, $"Returned {program.Name} to draft. Note: {note}", ("State", from, "Draft"));
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });
    }

    internal static IQueryable<Registration> ActiveRegistrations(CampDbContext db) =>
        db.Registrations.Where(r => r.Status != RegistrationStatus.Cancelled && r.Order!.Status != OrderStatus.Declined);

    /// <summary>The program's setup row, created on first touch for programs added after the seed ran.</summary>
    internal static async Task<ProgramSetup> StateOf(CampDbContext db, CampProgram program, CancellationToken ct)
    {
        var setup = await db.Set<ProgramSetup>().Include(x => x.Steps).FirstOrDefaultAsync(x => x.ProgramId == program.Id, ct);
        if (setup is not null) return setup;
        setup = new ProgramSetup { ProgramId = program.Id, State = program.IsPublished ? PublishState.Published : PublishState.Draft };
        db.Set<ProgramSetup>().Add(setup);
        return setup;
    }

    static void ResetSteps(CampDbContext db, ProgramSetup setup)
    {
        db.Set<ProgramApprovalStep>().RemoveRange(setup.Steps);
        setup.Steps.Clear();
        var seq = 1;
        foreach (var (role, description) in Chain)
            setup.Steps.Add(new ProgramApprovalStep { Sequence = seq++, Role = role, Description = description });
    }

    /// <summary>Why this person can't approve the next step, or null when they can.</summary>
    internal static string? ApprovalBlock(ProgramSetup setup, StaffUser staff)
    {
        if (string.Equals(setup.SubmittedByEmail, staff.Email, StringComparison.OrdinalIgnoreCase))
            return "You submitted this program, so someone else has to approve it.";
        if (setup.Steps.Any(s => string.Equals(s.ApprovedByEmail, staff.Email, StringComparison.OrdinalIgnoreCase)))
            return "You approved an earlier step. The next step needs a different approver.";
        return null;
    }

    static async Task<Dictionary<string, string[]>> Validate(CampDbContext db, ProgramInput req, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(req.Name)) errors["name"] = ["Enter a program name."];
        else if (req.Name.Trim().Length > 120) errors["name"] = ["Program names are limited to 120 characters."];
        if (string.IsNullOrWhiteSpace(req.Location)) errors["location"] = ["Enter where the program happens."];
        if (req.Tagline?.Length > 200) errors["tagline"] = ["The tagline is limited to 200 characters."];
        if (req.Description?.Length > 4000) errors["description"] = ["The description is limited to 4,000 characters."];
        if (!Enum.IsDefined(req.Type)) errors["type"] = ["Choose a program type."];
        if (!Enum.IsDefined(req.HealthMechanism)) errors["healthMechanism"] = ["Choose how health information is collected."];
        if (!await db.Ministries.AnyAsync(m => m.Id == req.MinistryId, ct)) errors["ministryId"] = ["Choose a ministry."];
        return errors;
    }

    static async Task<string> UniqueSlug(CampDbContext db, string name, CancellationToken ct)
    {
        var baseSlug = NonSlug().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        if (baseSlug.Length == 0) baseSlug = "program";
        var slug = baseSlug;
        for (var n = 2; await db.Programs.AnyAsync(p => p.Slug == slug, ct); n++) slug = $"{baseSlug}-{n}";
        return slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlug();

    internal static string TypeLabel(ProgramType t) => t switch
    {
        ProgramType.Standard => "Single session",
        ProgramType.Admittance => "Admittance",
        _ => "Cohort",
    };

    internal static string StateLabel(PublishState s) => s switch
    {
        PublishState.PendingApproval => "Pending approval",
        PublishState.Published => "Published",
        _ => "Draft",
    };

    static object ToRow(CampProgram p, ProgramSetup? setup, Dictionary<int, int> registered, StaffUser staff)
    {
        var state = setup?.State ?? (p.IsPublished ? PublishState.Published : PublishState.Draft);
        var steps = setup?.Steps.OrderBy(s => s.Sequence).ToList() ?? [];
        var next = state == PublishState.PendingApproval ? steps.FirstOrDefault(s => s.ApprovedAt is null) : null;
        var block = next is null || setup is null ? null : ApprovalBlock(setup, staff);
        return new
        {
            p.Id,
            p.Name,
            p.Slug,
            Ministry = new { p.Ministry.Id, p.Ministry.Code, p.Ministry.Name },
            Type = p.Type.ToString(),
            TypeLabel = TypeLabel(p.Type),
            HealthMechanism = p.HealthMechanism.ToString(),
            p.Location,
            p.Tagline,
            p.Description,
            p.IsPublished,
            State = state.ToString(),
            StateLabel = StateLabel(state),
            Questions = p.Questions.Count,
            Waivers = p.Waivers.OrderBy(w => w.Id).Select(w => new { w.Id, w.Title, w.Version }),
            Sessions = p.Sessions.OrderBy(s => s.StartDate).Select(s => new
            {
                s.Id,
                s.Name,
                s.StartDate,
                s.EndDate,
                s.PriceCents,
                Capacity = s.Pools.Sum(x => x.Capacity),
                Pools = s.Pools.Count,
                Registered = registered.GetValueOrDefault(s.Id),
            }),
            Registered = p.Sessions.Sum(s => registered.GetValueOrDefault(s.Id)),
            SubmittedBy = state == PublishState.Draft ? null : setup?.SubmittedBy,
            SubmittedAt = state == PublishState.Draft ? null : setup?.SubmittedAt,
            setup?.ReturnNote,
            Steps = steps.Select(s => new { s.Sequence, s.Role, s.Description, s.ApprovedBy, s.ApprovedAt, Next = s == next }),
            NextStep = next?.Role,
            CanApprove = next is not null && block is null,
            ApprovalBlock = block,
        };
    }
}
