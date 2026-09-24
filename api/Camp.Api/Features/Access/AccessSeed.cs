using System.Text.Json;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Access;

/// <summary>
/// Staff rows for the WorkOS personas (matched by email; sign-in and sync attach their WorkOS ids), one former
/// staff member who is no longer in the WorkOS organization so the first sync shows a revocation, a health
/// setting for every program, and the content of Day Camp health forms the core seed marked Complete but left empty.
/// Runs after every other slice's seed so programs they add get a setting too.
/// </summary>
public sealed class AccessSeed(TimeProvider time) : ISeedModule
{
    public const string FormerStaffEmail = "morgan.ellis@winshape.example";

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
        (string Email, string First, string Last, string Role, int? Ministry, bool Health, DateTime? LastSignIn)[] people =
        [
            ("alex.morgan@winshape.example", "Alex", "Morgan", "admin", null, true, null),
            ("diane.carter@winshape.example", "Diane", "Carter", "cet", null, false, null),
            ("marcus.lee@winshape.example", "Marcus", "Lee", "finance", null, false, null),
            ("grace.patel@winshape.example", "Grace", "Patel", "host", wsc, false, null),
            (FormerStaffEmail, "Morgan", "Ellis", "cet", wsc, true, now.AddDays(-41).Date.AddHours(13).AddMinutes(12)),
        ];
        var existing = await db.Set<StaffMember>().Select(m => m.Email).ToListAsync(ct);
        foreach (var p in people.Where(p => !existing.Contains(p.Email, StringComparer.OrdinalIgnoreCase)))
            db.Set<StaffMember>().Add(new StaffMember
            {
                Email = p.Email,
                FirstName = p.First,
                LastName = p.Last,
                Role = p.Role,
                MinistryId = p.Ministry,
                HealthAccess = p.Health,
                Status = StaffStatus.Active,
                LastSignInAt = p.LastSignIn,
                CreatedAt = now.AddDays(-60).Date,
            });
        await db.SaveChangesAsync(ct);
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
