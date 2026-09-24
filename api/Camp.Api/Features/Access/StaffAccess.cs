using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Setup;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Access;

/// <summary>Who may view a program's health details (K9 + K11, FR-112). One rule, used by every read.</summary>
public static class HealthAccessRules
{
    /// <summary>Console roles a program can open health details to. Host coordinators never see camper health.</summary>
    public static readonly string[] ViewerRoleChoices = ["admin", "cet", "finance"];

    public static string RoleLabel(string slug) => slug switch
    {
        "admin" => "Administrator",
        "cet" => "Customer Experience",
        "finance" => "Finance",
        "host" => "Host coordinator",
        "" => "No role",
        _ => slug,
    };

    /// <summary>CampDoc is Overnight Camp's system of record (FR-22); no other program uses it.</summary>
    public static bool IsOvernight(CampProgram p) => IsOvernight(p.Slug, p.Name);

    public static bool IsOvernight(string slug, string name) =>
        slug == "overnight-camp" || name.Contains("Overnight", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Why <paramref name="member"/> can't view health details for <paramref name="program"/>, or null when they can.
    /// All four must hold: active, health-data access on, ministry scope covers the program, role allowed by the program.
    /// </summary>
    public static string? Refusal(StaffMember? member, CampProgram program, ProgramHealthSetting? setting)
    {
        if (member is null) return "You aren't on the staff list yet. Sign out, sign back in, and try again.";
        if (member.Status == StaffStatus.Revoked) return "Your staff access was revoked. Ask an admin if that's a mistake.";
        if (!member.HealthAccess)
            return "You have completion status only, not health-data access. An admin can change that under Setup › Users.";
        if (member.MinistryId is { } scope && scope != program.MinistryId)
            return $"Your access covers {member.Ministry?.Name ?? "one ministry"} only, and {program.Name} is in {program.Ministry.Name}.";
        var roles = setting?.Roles ?? [];
        if (!roles.Contains(member.Role))
            return roles.Count == 0
                ? $"No role can view health details for {program.Name}. An admin can change that under Setup › Health settings."
                : $"{program.Name} health details are open to {string.Join(" and ", roles.Select(RoleLabel))} only. An admin can change that under Setup › Health settings.";
        return null;
    }

    /// <summary>Active staff who can view health details for the program right now.</summary>
    public static IEnumerable<StaffMember> Viewers(IEnumerable<StaffMember> staff, CampProgram program, ProgramHealthSetting? setting) =>
        program.HealthMechanism == HealthMechanism.CampDoc ? [] : staff.Where(m => Refusal(m, program, setting) is null);
}

/// <summary>Keeps platform staff rows in step with the identity provider.</summary>
public static class StaffSync
{
    /// <summary>
    /// Adds new members, updates names and roles (auditing role changes), restores members who came back,
    /// and revokes active rows that are no longer in the staff organization. Stages everything; the caller saves.
    /// </summary>
    public static async Task<StaffSyncRun> ReconcileAsync(CampDbContext db, IAuditLog audit, IReadOnlyList<DirectoryMember> members,
        string actor, DateTime now, CancellationToken ct)
    {
        var rows = await db.Set<StaffMember>().ToListAsync(ct);
        var run = new StaffSyncRun { RanAt = now, Actor = actor, Members = members.Count };
        var seen = new HashSet<StaffMember>();

        foreach (var m in members)
        {
            var row = rows.FirstOrDefault(r => r.WorkOsUserId == m.UserId)
                ?? rows.FirstOrDefault(r => string.Equals(r.Email, m.Email, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new StaffMember
                {
                    WorkOsUserId = m.UserId,
                    Email = m.Email,
                    FirstName = m.FirstName,
                    LastName = m.LastName,
                    Role = m.Role,
                    Status = StaffStatus.Active,
                    SyncedAt = now,
                    CreatedAt = now,
                };
                db.Set<StaffMember>().Add(row);
                rows.Add(row);
                await db.SaveChangesAsync(ct); // the audit row needs the id
                audit.Record(db, "staff.added", "StaffMember", row.Id, $"Added {row.Name} ({HealthAccessRules.RoleLabel(m.Role)}) from the WorkOS staff organization.",
                    ("Status", null, "Active"), ("Role", null, HealthAccessRules.RoleLabel(m.Role)));
                run.Added++;
                seen.Add(row);
                continue;
            }

            seen.Add(row);
            var changed = row.WorkOsUserId != m.UserId || row.Email != m.Email || row.FirstName != m.FirstName || row.LastName != m.LastName;
            if (row.Role != m.Role)
            {
                audit.Record(db, "staff.role_synced", "StaffMember", row.Id,
                    $"{row.Name}'s role changed in WorkOS from {HealthAccessRules.RoleLabel(row.Role)} to {HealthAccessRules.RoleLabel(m.Role)}.",
                    ("Role", HealthAccessRules.RoleLabel(row.Role), HealthAccessRules.RoleLabel(m.Role)));
                changed = true;
            }
            if (row.Status == StaffStatus.Revoked)
            {
                audit.Record(db, "staff.restored", "StaffMember", row.Id, $"{row.Name} is back in the WorkOS staff organization; access restored.",
                    ("Status", "Revoked", "Active"));
                row.Status = StaffStatus.Active;
                row.RevokedAt = null;
                changed = true;
            }
            (row.WorkOsUserId, row.Email, row.FirstName, row.LastName, row.Role) = (m.UserId, m.Email, m.FirstName, m.LastName, m.Role);
            row.SyncedAt = now;
            if (changed) run.Updated++;
        }

        foreach (var row in rows.Where(r => r.Status == StaffStatus.Active && !seen.Contains(r)))
        {
            row.Status = StaffStatus.Revoked;
            row.RevokedAt = now;
            audit.Record(db, "staff.revoked", "StaffMember", row.Id, $"{row.Name} is no longer in the WorkOS staff organization; access revoked.",
                ("Status", "Active", "Revoked"));
            run.Revoked++;
        }

        db.Set<StaffSyncRun>().Add(run);
        return run;
    }

    /// <summary>
    /// Records a staff sign-in: WorkOS just said this person is in the staff organization with this role,
    /// so the row is created, updated or restored to match. Saves.
    /// </summary>
    public static async Task<StaffMember> RecordSignInAsync(CampDbContext db, IAuditLog audit, string userId, string email, string firstName,
        string lastName, string role, DateTime now, CancellationToken ct)
    {
        var row = await db.Set<StaffMember>().Include(m => m.Ministry).FirstOrDefaultAsync(m => m.WorkOsUserId == userId, ct)
            ?? await db.Set<StaffMember>().Include(m => m.Ministry).FirstOrDefaultAsync(m => m.Email == email, ct);
        var added = row is null;
        if (row is null)
        {
            row = new StaffMember { Email = email, Status = StaffStatus.Active, CreatedAt = now, Role = role };
            db.Set<StaffMember>().Add(row);
        }
        else if (row.Status == StaffStatus.Revoked)
        {
            audit.Record(db, "staff.restored", "StaffMember", row.Id, $"{row.Name} signed in as a WorkOS staff member again; access restored.",
                ("Status", "Revoked", "Active"));
            row.Status = StaffStatus.Active;
            row.RevokedAt = null;
        }
        if (row.Id != 0 && row.Role != role)
            audit.Record(db, "staff.role_synced", "StaffMember", row.Id,
                $"{row.Name}'s role changed in WorkOS from {HealthAccessRules.RoleLabel(row.Role)} to {HealthAccessRules.RoleLabel(role)}.",
                ("Role", HealthAccessRules.RoleLabel(row.Role), HealthAccessRules.RoleLabel(role)));
        (row.WorkOsUserId, row.Email, row.Role, row.LastSignInAt) = (userId, email, role, now);
        if (firstName.Length > 0 || lastName.Length > 0) (row.FirstName, row.LastName) = (firstName, lastName);
        await db.SaveChangesAsync(ct);
        if (added)
        {
            // Same trail as a sync-added member; the audit row needs the id, so it follows the first save.
            audit.Record(db, "staff.added", "StaffMember", row.Id, $"Added {row.Name} ({HealthAccessRules.RoleLabel(role)}) on their first sign-in as a WorkOS staff member.",
                ("Status", null, "Active"), ("Role", null, HealthAccessRules.RoleLabel(role)));
            await db.SaveChangesAsync(ct);
        }
        return row;
    }
}
