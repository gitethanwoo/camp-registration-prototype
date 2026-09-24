using System.Text.Json;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Setup;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Camp.Api.Features.Access;

public record StaffAccessInput(int? MinistryId, bool HealthAccess);
public record HealthSettingInput(HealthMechanism Mechanism, List<string>? ViewerRoles, string? ThirdPartyFormUrl, bool ConfirmMismatch);

/// <summary>
/// K11 · Staff users and roles (FR-2, FR-6) and K9 · Health collection settings (FR-20, FR-22, FR-112),
/// plus the one read that returns health details, which enforces both and audits every attempt.
/// </summary>
public sealed class AccessEndpoints : IEndpointModule
{
    /// <summary>Opening K11 syncs again when the last sync is older than this.</summary>
    public static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(15);

    public const string CampDocUrl = "https://app.campdoc.com/";

    // One sync at a time: two tabs opening K11 together must not revoke (and audit) the same person twice.
    static readonly SemaphoreSlim SyncGate = new(1, 1);

    static readonly JsonSerializerOptions HealthJson = new() { PropertyNameCaseInsensitive = true };

    public void Map(IEndpointRouteBuilder app)
    {
        var access = app.MapGroup("/api/access");
        var admin = access.MapGroup("").RequireAuthorization(Policies.Admin);

        // ── K11 · Staff access ──────────────────────────────────────────────
        admin.MapGet("/staff", async (CampDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var rows = await db.Set<StaffMember>().AsNoTracking().Include(m => m.Ministry)
                .OrderBy(m => m.Status).ThenBy(m => m.LastName).ThenBy(m => m.FirstName).ToListAsync(ct);
            var last = await db.Set<StaffSyncRun>().AsNoTracking().OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
            var now = time.GetUtcNow().UtcDateTime;
            return Results.Ok(new
            {
                Ministries = await Ministries(db, ct),
                LastSync = last,
                SyncDue = last is null || now - last.RanAt > SyncInterval,
                Rows = rows.Select(StaffRow),
            });
        });

        admin.MapPost("/staff/sync", async (CampDbContext db, IAuditLog audit, StaffUser staff, TimeProvider time,
            IHttpClientFactory http, IOptions<WorkOsOptions> options, CancellationToken ct) =>
        {
            List<DirectoryMember> members;
            try
            {
                using var client = http.CreateClient(StaffDirectory.ClientName);
                client.Timeout = TimeSpan.FromSeconds(10);
                members = await new StaffDirectory(client, options.Value).ActiveMembersAsync(ct);
            }
            catch (DirectoryUnavailableException e)
            {
                return Results.Json(new { error = $"{e.Message} No one's access changed. Try Sync now again in a minute." },
                    statusCode: StatusCodes.Status502BadGateway);
            }
            await SyncGate.WaitAsync(ct);
            try
            {
                var run = await StaffSync.ReconcileAsync(db, audit, members, staff.Actor, time.GetUtcNow().UtcDateTime, ct);
                await db.SaveChangesAsync(ct);
                return Results.Ok(run);
            }
            finally
            {
                SyncGate.Release();
            }
        });

        admin.MapGet("/staff/{id:int}", async (int id, CampDbContext db, CancellationToken ct) =>
        {
            var m = await db.Set<StaffMember>().AsNoTracking().Include(x => x.Ministry).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (m is null) return Results.NotFound();
            var key = id.ToString(CultureInfo.InvariantCulture);
            var activity = await db.AuditEvents.AsNoTracking().Where(a => a.EntityType == "StaffMember" && a.EntityId == key)
                .OrderByDescending(a => a.Id).Take(10).Select(a => new { a.Id, a.CreatedAt, a.Actor, a.Action, a.Detail }).ToListAsync(ct);
            var (programs, settings) = await ProgramsWithSettings(db, ct);
            var healthPrograms = programs
                .Where(p => p.HealthMechanism != HealthMechanism.CampDoc && HealthAccessRules.Refusal(m, p, settings.GetValueOrDefault(p.Id)) is null)
                .Select(p => new { p.Id, p.Name });
            return Results.Ok(new { Member = StaffRow(m), Activity = activity, HealthPrograms = healthPrograms });
        });

        admin.MapPut("/staff/{id:int}", async (int id, StaffAccessInput req, CampDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var m = await db.Set<StaffMember>().Include(x => x.Ministry).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (m is null) return Results.NotFound();
            if (m.Status == StaffStatus.Revoked)
                return Results.Conflict(new { error = $"{m.Name}'s access was revoked in WorkOS, so there's nothing to change here. Add them back to the staff organization first." });
            Ministry? ministry = null;
            if (req.MinistryId is { } ministryId && (ministry = await db.Ministries.FirstOrDefaultAsync(x => x.Id == ministryId, ct)) is null)
                return SetupResults.Invalid("ministryId", "Choose a ministry from the list, or All ministries.");
            if (req.HealthAccess && !HealthAccessRules.ViewerRoleChoices.Contains(m.Role))
                return SetupResults.Invalid("healthAccess", $"{HealthAccessRules.RoleLabel(m.Role)}s never see camper health details, so health-data access can't be turned on.");

            var scopeBefore = m.Ministry?.Name ?? "All ministries";
            var scopeAfter = ministry?.Name ?? "All ministries";
            if (scopeBefore != scopeAfter)
                audit.Record(db, "staff.scope_changed", "StaffMember", m.Id, $"{m.Name}'s ministry scope changed from {scopeBefore} to {scopeAfter}.",
                    ("Ministry scope", scopeBefore, scopeAfter));
            if (m.HealthAccess != req.HealthAccess)
                audit.Record(db, "staff.health_access_changed", "StaffMember", m.Id,
                    req.HealthAccess ? $"{m.Name} can now view health details where a program allows their role." : $"{m.Name} now sees health completion status only.",
                    ("Health-data access", HealthLabel(m.HealthAccess), HealthLabel(req.HealthAccess)));
            m.MinistryId = ministry?.Id;
            m.Ministry = ministry;
            m.HealthAccess = req.HealthAccess;
            await db.SaveChangesAsync(ct);
            return Results.Ok(StaffRow(m));
        });

        // ── K9 · Health collection settings ────────────────────────────────
        admin.MapGet("/health", async (CampDbContext db, CancellationToken ct) =>
        {
            var (programs, settings) = await ProgramsWithSettings(db, ct);
            var staff = await db.Set<StaffMember>().AsNoTracking().Include(m => m.Ministry).Where(m => m.Status == StaffStatus.Active).ToListAsync(ct);
            var onFile = await db.Registrations.Where(r => r.HealthJson != null).GroupBy(r => r.Session.ProgramId)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var registered = await db.Registrations.Where(r => r.Status != RegistrationStatus.Cancelled).GroupBy(r => r.Session.ProgramId)
                .Select(g => new { g.Key, Count = g.Count(), Complete = g.Count(r => r.HealthStatus == FormStatus.Complete) })
                .ToDictionaryAsync(x => x.Key, ct);
            return Results.Ok(new
            {
                Roles = HealthAccessRules.ViewerRoleChoices.Select(r => new { Slug = r, Label = HealthAccessRules.RoleLabel(r) }),
                Programs = programs.Select(p =>
                {
                    var s = settings.GetValueOrDefault(p.Id);
                    return new
                    {
                        p.Id,
                        p.Name,
                        Ministry = new { p.Ministry.Id, p.Ministry.Name },
                        Type = p.Type.ToString(),
                        p.Location,
                        IsOvernight = HealthAccessRules.IsOvernight(p),
                        Mechanism = p.HealthMechanism.ToString(),
                        ViewerRoles = s?.Roles ?? [],
                        s?.ThirdPartyFormUrl,
                        s?.UpdatedAt,
                        s?.UpdatedBy,
                        Sessions = p.Sessions.OrderBy(x => x.StartDate).Select(x => new { x.Id, x.Name, x.StartDate, x.EndDate }),
                        Registered = registered.GetValueOrDefault(p.Id)?.Count ?? 0,
                        HealthComplete = registered.GetValueOrDefault(p.Id)?.Complete ?? 0,
                        FormsOnFile = onFile.GetValueOrDefault(p.Id),
                        Viewers = HealthAccessRules.Viewers(staff, p, s).OrderBy(m => m.LastName)
                            .Select(m => new { m.Id, m.Name, Role = HealthAccessRules.RoleLabel(m.Role) }),
                        // People in an allowed role who still can't view, and why: the usual "I added CET but Diane still can't" question.
                        Blocked = p.HealthMechanism == HealthMechanism.CampDoc ? [] : staff
                            .Where(m => (s?.Roles ?? []).Contains(m.Role) && HealthAccessRules.Refusal(m, p, s) is not null)
                            .OrderBy(m => m.LastName)
                            .Select(m => new { m.Id, m.Name, Role = HealthAccessRules.RoleLabel(m.Role), Reason = m.HealthAccess ? $"Scoped to {m.Ministry?.Name}" : "No health-data access" }),
                    };
                }),
            });
        });

        admin.MapPut("/health/{programId:int}", async (int programId, HealthSettingInput req, CampDbContext db, IAuditLog audit,
            StaffUser staff, TimeProvider time, CancellationToken ct) =>
        {
            var program = await db.Programs.Include(p => p.Ministry).FirstOrDefaultAsync(p => p.Id == programId, ct);
            if (program is null) return Results.NotFound();
            var roles = (req.ViewerRoles ?? []).Select(r => r.Trim()).Distinct().ToList();
            var errors = new Dictionary<string, string[]>();
            if (!Enum.IsDefined(req.Mechanism)) errors["mechanism"] = ["Choose how health information is collected."];
            if (roles.FirstOrDefault(r => !HealthAccessRules.ViewerRoleChoices.Contains(r)) is { } bad)
                errors["viewerRoles"] = [$"\"{bad}\" can't view health details. Choose from Administrator, Customer Experience and Finance."];
            var url = req.ThirdPartyFormUrl?.Trim();
            if (req.Mechanism == HealthMechanism.ThirdParty
                && (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps))
                errors["thirdPartyFormUrl"] = ["Enter the form's full link, starting with https://."];
            var overnight = HealthAccessRules.IsOvernight(program);
            if (errors.Count == 0 && !req.ConfirmMismatch && (req.Mechanism == HealthMechanism.CampDoc) != overnight)
                errors["mechanism"] = [overnight
                    ? $"{program.Name} keeps health profiles in CampDoc (FR-22). Confirm to collect them in this platform instead."
                    : $"CampDoc is used by Overnight Camp only. Confirm to use it for {program.Name} anyway."];
            if (errors.Count > 0) return SetupResults.Invalid(errors);

            var setting = await db.Set<ProgramHealthSetting>().FirstOrDefaultAsync(s => s.ProgramId == programId, ct);
            if (setting is null) db.Set<ProgramHealthSetting>().Add(setting = new ProgramHealthSetting { ProgramId = programId });
            var before = (Mechanism: MechanismLabel(program.HealthMechanism), Roles: RolesLabel(setting.Roles), Url: setting.ThirdPartyFormUrl);
            var ordered = HealthAccessRules.ViewerRoleChoices.Where(roles.Contains).ToList();
            program.HealthMechanism = req.Mechanism;
            setting.ViewerRoles = string.Join(',', ordered);
            setting.ThirdPartyFormUrl = req.Mechanism == HealthMechanism.ThirdParty ? url : setting.ThirdPartyFormUrl;
            setting.UpdatedAt = time.GetUtcNow().UtcDateTime;
            setting.UpdatedBy = staff.Actor;
            var after = (Mechanism: MechanismLabel(program.HealthMechanism), Roles: RolesLabel(ordered), Url: setting.ThirdPartyFormUrl);
            if (before != after)
                audit.Record(db, "health.settings_changed", "Program", program.Id, $"Health collection settings for {program.Name} changed.",
                    ("Collection method", before.Mechanism, after.Mechanism), ("Can view health details", before.Roles, after.Roles),
                    ("Third-party form link", before.Url, after.Url));
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { program.Id });
        });

        // ── Health details, enforced ───────────────────────────────────────
        access.MapGet("/registrations/{id:int}/health", async (int id, CampDbContext db, IAuditLog audit, StaffUser staff, CancellationToken ct) =>
        {
            var r = await db.Registrations.Include(x => x.Person).Include(x => x.Session).ThenInclude(s => s.Program).ThenInclude(p => p.Ministry)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            var program = r.Session.Program;
            var camper = r.Person.FullName;
            var status = r.HealthStatus.ToString();
            var setting = await db.Set<ProgramHealthSetting>().AsNoTracking().FirstOrDefaultAsync(s => s.ProgramId == program.Id, ct);

            // Nothing held here to protect: say where the details live instead.
            if (program.HealthMechanism == HealthMechanism.CampDoc && r.HealthJson is null)
                return Results.Ok(new
                {
                    Camper = camper,
                    Program = program.Name,
                    Mechanism = "CampDoc",
                    Status = status,
                    Details = (object?)null,
                    Link = CampDocUrl,
                    Message = $"{program.Name} keeps health profiles in CampDoc. This platform stores completion status only."
                });

            var member = await db.Set<StaffMember>().AsNoTracking().Include(m => m.Ministry)
                .FirstOrDefaultAsync(m => m.WorkOsUserId == staff.UserId, ct)
                ?? await db.Set<StaffMember>().AsNoTracking().Include(m => m.Ministry).FirstOrDefaultAsync(m => m.Email == staff.Email, ct);
            if (HealthAccessRules.Refusal(member, program, setting) is { } refusal)
            {
                audit.Record("health.view_denied", "Registration", r.Id, $"Refused {camper}'s health form ({program.Name}): {refusal}");
                await db.SaveChangesAsync(ct);
                return Results.Json(new { error = refusal }, statusCode: StatusCodes.Status403Forbidden);
            }
            if (r.HealthJson is null)
                return Results.Ok(new
                {
                    Camper = camper,
                    Program = program.Name,
                    Mechanism = program.HealthMechanism.ToString(),
                    Status = status,
                    Details = (object?)null,
                    Link = program.HealthMechanism == HealthMechanism.ThirdParty ? setting?.ThirdPartyFormUrl : null,
                    Message = program.HealthMechanism == HealthMechanism.ThirdParty
                        ? $"{program.Name} collects health forms in a third-party tool. This platform stores completion status only."
                        : "No health form is on file for this registration yet."
                });

            var form = JsonSerializer.Deserialize<HealthForm>(r.HealthJson, HealthJson);
            audit.Record("health.viewed", "Registration", r.Id, $"Viewed {camper}'s health form ({program.Name}).");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new
            {
                Camper = camper,
                Program = program.Name,
                Mechanism = program.HealthMechanism.ToString(),
                Status = status,
                Link = (string?)null,
                Message = (string?)null,
                Details = new
                {
                    Allergies = Blank(form?.Allergies) ?? Blank(r.Person.Allergies),
                    Medications = Blank(form?.Medications),
                    Dietary = Blank(form?.Dietary) ?? Blank(r.Person.Dietary),
                    AdaNeeds = Blank(form?.AdaNeeds) ?? Blank(r.Person.AdaNeeds),
                    PhysicianName = Blank(form?.PhysicianName),
                    PhysicianPhone = Blank(form?.PhysicianPhone),
                    InsuranceProvider = Blank(form?.InsuranceProvider),
                },
            });
        }).RequireAuthorization(Policies.Staff);
    }

    static object StaffRow(StaffMember m) => new
    {
        m.Id,
        m.Name,
        m.Email,
        m.Role,
        RoleLabel = HealthAccessRules.RoleLabel(m.Role),
        Ministry = m.Ministry is null ? null : new { m.Ministry.Id, m.Ministry.Name },
        m.HealthAccess,
        Status = m.Status.ToString(),
        m.LastSignInAt,
        m.SyncedAt,
        m.RevokedAt,
        CanViewHealth = HealthAccessRules.ViewerRoleChoices.Contains(m.Role),
    };

    static async Task<List<object>> Ministries(CampDbContext db, CancellationToken ct) =>
        [.. await db.Ministries.AsNoTracking().OrderBy(m => m.Id).Select(m => new { m.Id, m.Name }).ToListAsync(ct)];

    static async Task<(List<CampProgram> Programs, Dictionary<int, ProgramHealthSetting> Settings)> ProgramsWithSettings(CampDbContext db, CancellationToken ct)
    {
        var programs = await db.Programs.AsNoTracking().Include(p => p.Ministry).Include(p => p.Sessions)
            .OrderBy(p => p.MinistryId).ThenBy(p => p.Id).ToListAsync(ct);
        var settings = await db.Set<ProgramHealthSetting>().AsNoTracking().ToDictionaryAsync(s => s.ProgramId, ct);
        return (programs, settings);
    }

    static string HealthLabel(bool on) => on ? "Health details" : "Completion status only";

    static string MechanismLabel(HealthMechanism m) => m switch
    {
        HealthMechanism.Embedded => "Embedded form",
        HealthMechanism.ThirdParty => "Third-party form",
        _ => "CampDoc",
    };

    static string RolesLabel(IEnumerable<string> roles)
    {
        var list = roles.Select(HealthAccessRules.RoleLabel).ToList();
        return list.Count == 0 ? "No one" : string.Join(", ", list);
    }

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
