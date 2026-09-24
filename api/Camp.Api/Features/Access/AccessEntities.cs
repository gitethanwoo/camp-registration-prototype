using Camp.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Camp.Api.Features.Access;

/// <summary>Active while the person is a member of the staff organization in WorkOS; Revoked once they aren't.</summary>
public enum StaffStatus { Active, Revoked }

/// <summary>
/// A staff member as this platform knows them (K11, FR-2, FR-6). Who is staff, and in what role, comes
/// from the identity provider (WorkOS locally, Entra in production) and is never edited here. Ministry
/// scope and health-data access are this platform's own attributes.
/// </summary>
public class StaffMember
{
    public int Id { get; set; }
    /// <summary>The WorkOS user id. Null until the person signs in or a sync matches them by email.</summary>
    public string? WorkOsUserId { get; set; }
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    /// <summary>WorkOS role slug: cet, finance, host or admin. Kept after revocation to show what they had.</summary>
    public string Role { get; set; } = "";
    /// <summary>The one ministry this person works in, or null for all ministries.</summary>
    public int? MinistryId { get; set; }
    public Ministry? Ministry { get; set; }
    /// <summary>On: may view health details where the program allows their role. Off: completion status only.</summary>
    public bool HealthAccess { get; set; }
    public StaffStatus Status { get; set; }
    public DateTime? LastSignInAt { get; set; }
    /// <summary>When a sync last confirmed this person in the staff organization.</summary>
    public DateTime? SyncedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public string Name => $"{FirstName} {LastName}".Trim();
}

/// <summary>One reconciliation of platform staff rows against the WorkOS staff organization.</summary>
public class StaffSyncRun
{
    public int Id { get; set; }
    public DateTime RanAt { get; set; }
    public string Actor { get; set; } = "";
    public int Members { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Revoked { get; set; }
}

/// <summary>
/// Who may view a program's health details, and the third-party form link when the program uses one (K9).
/// The collection mechanism itself stays on <see cref="CampProgram.HealthMechanism"/>, which checkout reads.
/// </summary>
public class ProgramHealthSetting
{
    public int Id { get; set; }
    public int ProgramId { get; set; }
    /// <summary>Comma-separated WorkOS role slugs allowed to view health details, e.g. "admin,cet".</summary>
    public string ViewerRoles { get; set; } = "";
    public string? ThirdPartyFormUrl { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public IReadOnlyList<string> Roles => ViewerRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal sealed class StaffMemberConfig : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> b)
    {
        b.ToTable("StaffMembers");
        b.HasIndex(x => x.Email).IsUnique();
        b.HasIndex(x => x.WorkOsUserId).IsUnique().HasFilter("[WorkOsUserId] IS NOT NULL");
        b.Property(x => x.Email).HasMaxLength(320);
        b.Property(x => x.WorkOsUserId).HasMaxLength(200);
        b.Property(x => x.FirstName).HasMaxLength(100);
        b.Property(x => x.LastName).HasMaxLength(100);
        b.Property(x => x.Role).HasMaxLength(40);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasOne(x => x.Ministry).WithMany().HasForeignKey(x => x.MinistryId).OnDelete(DeleteBehavior.SetNull);
        b.Ignore(x => x.Name);
    }
}

internal sealed class StaffSyncRunConfig : IEntityTypeConfiguration<StaffSyncRun>
{
    public void Configure(EntityTypeBuilder<StaffSyncRun> b)
    {
        b.ToTable("StaffSyncRuns");
        b.Property(x => x.Actor).HasMaxLength(200);
    }
}

internal sealed class ProgramHealthSettingConfig : IEntityTypeConfiguration<ProgramHealthSetting>
{
    public void Configure(EntityTypeBuilder<ProgramHealthSetting> b)
    {
        b.ToTable("ProgramHealthSettings");
        b.HasOne<CampProgram>().WithOne().HasForeignKey<ProgramHealthSetting>(x => x.ProgramId).OnDelete(DeleteBehavior.Cascade);
        b.Property(x => x.ViewerRoles).HasMaxLength(200);
        b.Property(x => x.ThirdPartyFormUrl).HasMaxLength(500);
        b.Property(x => x.UpdatedBy).HasMaxLength(200);
        b.Ignore(x => x.Roles);
    }
}

/// <summary>Session claims added at staff sign-in. Display hints only; health reads check the database.</summary>
public static class AccessClaims
{
    /// <summary>The staff member's ministry scope id. Absent means all ministries.</summary>
    public const string MinistryId = "camp:ministry_id";

    /// <summary>"true" when the staff member has health-data access.</summary>
    public const string HealthAccess = "camp:health_access";
}
