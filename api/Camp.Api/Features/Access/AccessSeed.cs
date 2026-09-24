using System.Text.Json;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Setup;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Access;

/// <summary>
/// Staff rows for the WorkOS personas (matched by email; sign-in and sync attach their WorkOS ids), one former
/// staff member already revoked by an earlier sync, a health setting for every program, and the content of Day Camp
/// health forms the core seed marked Complete but left empty. The active rows match the emulator's staff organization
/// (<c>infra/workos/workos-emulate.config.yaml</c>), so the first sync on fresh data changes no one.
/// Runs after every other slice's seed so programs they add get a setting too.
/// </summary>
public sealed class AccessSeed(TimeProvider time) : ISeedModule
{
    public const string FormerStaffEmail = "morgan.ellis@winshape.example";

    /// <summary>The seeded staff roster. Everyone but Morgan Ellis is active and in the WorkOS staff organization.</summary>
    public static readonly (string Email, string First, string Last, string Role, bool AllMinistries, bool Health, bool Active)[] Staff =
    [
        ("alex.morgan@winshape.example", "Alex", "Morgan", "admin", true, true, true),
        ("diane.carter@winshape.example", "Diane", "Carter", "cet", true, false, true),
        ("marcus.lee@winshape.example", "Marcus", "Lee", "finance", true, false, true),
        ("grace.patel@winshape.example", "Grace", "Patel", "host", false, false, true),
        (FormerStaffEmail, "Morgan", "Ellis", "cet", false, true, false),
    ];

    /// <summary>Morgan Ellis left the staff organization this many days before the demo's "today".</summary>
    public const int FormerStaffRevokedDaysAgo = 34;

    public int Order => 950;

    public async Task RunAsync(CampDbContext db, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var now = time.GetUtcNow().UtcDateTime;
        await SeedStaff(db, now, ct);
        await SeedHealthSettings(db, ct);
        await BackfillEmbeddedForms(db, ct);
    }

    static async Task SeedStaff(CampDbContext db, DateTime now, CancellationToken ct)
    {
        var wsc = await db.Ministries.Where(m => m.Code == "WSC").Select(m => (int?)m.Id).FirstOrDefaultAsync(ct);
        var existing = await db.Set<StaffMember>().Select(m => m.Email).ToListAsync(ct);
        var revokedAt = now.AddDays(-FormerStaffRevokedDaysAgo).Date.AddHours(14).AddMinutes(5);
        var added = new List<StaffMember>();
        foreach (var p in Staff.Where(p => !existing.Contains(p.Email, StringComparer.OrdinalIgnoreCase)))
        {
            var row = new StaffMember
            {
                Email = p.Email,
                FirstName = p.First,
                LastName = p.Last,
                Role = p.Role,
                MinistryId = p.AllMinistries ? null : wsc,
                HealthAccess = p.Health,
                Status = p.Active ? StaffStatus.Active : StaffStatus.Revoked,
                // Morgan last signed in a week before leaving; the sync that noticed revoked them.
                LastSignInAt = p.Active ? null : revokedAt.AddDays(-7).Date.AddHours(13).AddMinutes(12),
                SyncedAt = p.Active ? null : revokedAt.AddDays(-7),
                RevokedAt = p.Active ? null : revokedAt,
                CreatedAt = now.AddDays(-60).Date,
            };
            db.Set<StaffMember>().Add(row);
            added.Add(row);
        }
        await db.SaveChangesAsync(ct);

        // The earlier sync that revoked Morgan, so K11 opens with a history and Morgan's sheet says why.
        if (added.FirstOrDefault(m => m.Status == StaffStatus.Revoked) is { } former)
        {
            const string actor = "Alex Morgan (ADMIN)";
            db.Set<StaffSyncRun>().Add(new StaffSyncRun { RanAt = revokedAt, Actor = actor, Members = Staff.Count(p => p.Active), Revoked = 1 });
            var revoked = new AuditEvent
            {
                Actor = actor,
                Action = "staff.revoked",
                EntityType = "StaffMember",
                EntityId = former.Id.ToString(CultureInfo.InvariantCulture),
                Detail = $"{former.Name} is no longer in the WorkOS staff organization; access revoked.",
                CreatedAt = revokedAt,
            };
            db.AuditEvents.Add(revoked);
            db.Set<AuditChange>().Add(new AuditChange { AuditEvent = revoked, Field = "Status", Before = "Active", After = "Revoked" });
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Administrators only, to start; CampDoc programs hold no details here, so no one.</summary>
    static async Task SeedHealthSettings(CampDbContext db, CancellationToken ct)
    {
        var programs = await db.Programs.AsNoTracking().Select(p => new { p.Id, p.HealthMechanism }).ToListAsync(ct);
        var have = await db.Set<ProgramHealthSetting>().Select(s => s.ProgramId).ToHashSetAsync(ct);
        foreach (var p in programs.Where(p => !have.Contains(p.Id)))
            db.Set<ProgramHealthSetting>().Add(new ProgramHealthSetting
            {
                ProgramId = p.Id,
                ViewerRoles = p.HealthMechanism == HealthMechanism.CampDoc ? "" : "admin",
            });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Fills only a null HealthJson on embedded-form registrations (Day Camp, Family Camp and the other programs that
    /// collect health here) already marked Complete, so no status,
    /// count or balance changes. Allergies, dietary needs and ADA needs come from the camper's profile,
    /// as checkout would have prefilled them; only medications, physician and insurance are made up (deterministic).
    /// </summary>
    static async Task BackfillEmbeddedForms(CampDbContext db, CancellationToken ct)
    {
        var regs = await db.Registrations.Include(r => r.Person)
            .Where(r => r.Session.Program.HealthMechanism == HealthMechanism.Embedded && r.HealthStatus == FormStatus.Complete && r.HealthJson == null)
            .ToListAsync(ct);
        string[] medications = ["None", "None", "Albuterol inhaler as needed", "None", "Methylphenidate 10 mg at lunch, given by the camp nurse"];
        string[] doctors = ["Dr. Alicia Moore", "Dr. James Whitfield", "Dr. Priya Raman", "Dr. Thomas Greer", "Dr. Hannah Cole"];
        string[] insurers = ["Blue Cross Blue Shield of Georgia", "Aetna", "UnitedHealthcare", "Kaiser Permanente", "Cigna"];
        foreach (var r in regs)
        {
            var i = r.Id;
            var allergies = string.IsNullOrWhiteSpace(r.Person.Allergies) ? "None known" : r.Person.Allergies;
            var medication = allergies.Contains("peanut", StringComparison.OrdinalIgnoreCase) || allergies.Contains("bee", StringComparison.OrdinalIgnoreCase)
                ? "EpiPen, carried by counselor"
                : medications[i % medications.Length];
            r.HealthJson = JsonSerializer.Serialize(new HealthForm(
                string.IsNullOrWhiteSpace(r.Person.Dietary) ? "None" : r.Person.Dietary, allergies, r.Person.AdaNeeds,
                medication, doctors[i % doctors.Length], $"(404) 555-01{i % 100:00}", insurers[i % insurers.Length]));
        }
        await db.SaveChangesAsync(ct);
    }
}
