using Camp.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Camp.Api.Features.Groups;

/// <summary>Draft while the leader builds the roster; Confirmed once the group is paid for.</summary>
public enum GroupStatus { Draft, Confirmed }

/// <summary>Where an attendee's withdrawal request stands. The leader approves the refund (G2).</summary>
public enum WithdrawalStatus { None, Requested, Approved, Declined }

/// <summary>
/// One group registration for a cohort session (FR-35), made by a leader for a list of attendees.
/// Paid in full by the leader; each attendee completes their own forms by secure link (FR-34).
/// </summary>
public class GroupRegistration
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public Session Session { get; set; } = null!;
    public int LeaderHouseholdId { get; set; }
    public Household LeaderHousehold { get; set; } = null!;
    /// <summary>Snapshot of the leader's name, shown to attendees ("Dave Kim registered you…").</summary>
    public string LeaderName { get; set; } = "";
    public string Name { get; set; } = "";
    public GroupStatus Status { get; set; }
    public int? OrderId { get; set; }
    public PaymentOrder? Order { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public List<GroupAttendee> Attendees { get; set; } = [];
}

/// <summary>A person on a group roster. They have no account; the token is their only key.</summary>
public class GroupAttendee
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public GroupRegistration Group { get; set; } = null!;
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public int SortOrder { get; set; }
    /// <summary>SHA-256 of the secure-link token, hex. Null until the group is confirmed.</summary>
    public string? TokenHash { get; set; }
    public DateTime? LinkSentAt { get; set; }
    public FormStatus FormStatus { get; set; } = FormStatus.Incomplete;
    public string? Phone { get; set; }
    public string AnswersJson { get; set; } = "{}";
    public DateTime? SubmittedAt { get; set; }
    /// <summary>False once a withdrawal is approved: the seat is released and the price refunded.</summary>
    public bool IsActive { get; set; } = true;
    public WithdrawalStatus Withdrawal { get; set; }
    public string? WithdrawalReason { get; set; }
    public DateTime? WithdrawalRequestedAt { get; set; }
    public DateTime? WithdrawalResolvedAt { get; set; }
    public List<GroupWaiverAcceptance> WaiverAcceptances { get; set; } = [];
}

/// <summary>An attendee's acceptance of one waiver version (FR-37), kept even if the template changes.</summary>
public class GroupWaiverAcceptance
{
    public int Id { get; set; }
    public int AttendeeId { get; set; }
    public int WaiverTemplateId { get; set; }
    public WaiverTemplate WaiverTemplate { get; set; } = null!;
    public int Version { get; set; }
    public string SignerName { get; set; } = "";
    public DateTime AcceptedAt { get; set; }
}

internal sealed class GroupRegistrationConfig : IEntityTypeConfiguration<GroupRegistration>
{
    public void Configure(EntityTypeBuilder<GroupRegistration> b)
    {
        b.ToTable("GroupRegistrations");
        b.HasOne(g => g.Session).WithMany().OnDelete(DeleteBehavior.NoAction);
        b.HasOne(g => g.LeaderHousehold).WithMany().OnDelete(DeleteBehavior.NoAction);
        b.HasOne(g => g.Order).WithMany().HasForeignKey(g => g.OrderId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(g => g.Attendees).WithOne(a => a.Group).HasForeignKey(a => a.GroupId);
        b.HasIndex(g => g.LeaderHouseholdId);
        b.Property(g => g.Name).HasMaxLength(120);
    }
}

internal sealed class GroupAttendeeConfig : IEntityTypeConfiguration<GroupAttendee>
{
    public void Configure(EntityTypeBuilder<GroupAttendee> b)
    {
        b.ToTable("GroupAttendees");
        b.Property(a => a.Name).HasMaxLength(120);
        b.Property(a => a.Email).HasMaxLength(254);
        b.Property(a => a.TokenHash).HasMaxLength(64);
        b.Property(a => a.AnswersJson).HasMaxLength(4000);
        b.Property(a => a.WithdrawalReason).HasMaxLength(1000);
        b.HasIndex(a => a.TokenHash).IsUnique().HasFilter("[TokenHash] IS NOT NULL");
        b.HasMany(a => a.WaiverAcceptances).WithOne().HasForeignKey(w => w.AttendeeId);
    }
}

internal sealed class GroupWaiverAcceptanceConfig : IEntityTypeConfiguration<GroupWaiverAcceptance>
{
    public void Configure(EntityTypeBuilder<GroupWaiverAcceptance> b)
    {
        b.ToTable("GroupWaiverAcceptances");
        b.HasOne(w => w.WaiverTemplate).WithMany().OnDelete(DeleteBehavior.NoAction);
    }
}
