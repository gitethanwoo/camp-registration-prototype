using Camp.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Camp.Api.Features.Setup;

/// <summary>Draft → Pending approval → Published (K2, FR-67). Only the last approval publishes.</summary>
public enum PublishState { Draft, PendingApproval, Published }

/// <summary>
/// Publish state of a program. <see cref="CampProgram.IsPublished"/> stays the flag the guest site
/// reads; this row explains how it got there and who asked.
/// </summary>
public class ProgramSetup
{
    public int Id { get; set; }
    public int ProgramId { get; set; }
    public PublishState State { get; set; }
    public string? SubmittedBy { get; set; }
    public string? SubmittedByEmail { get; set; }
    public DateTime? SubmittedAt { get; set; }
    /// <summary>Why the last submission went back to draft, if it did.</summary>
    public string? ReturnNote { get; set; }
    public List<ProgramApprovalStep> Steps { get; set; } = [];
}

/// <summary>One step of a program's approval chain. Reset whenever the program is resubmitted.</summary>
public class ProgramApprovalStep
{
    public int Id { get; set; }
    public int ProgramSetupId { get; set; }
    public int Sequence { get; set; }
    public string Role { get; set; } = "";
    public string Description { get; set; } = "";
    public string? ApprovedBy { get; set; }
    public string? ApprovedByEmail { get; set; }
    public DateTime? ApprovedAt { get; set; }
}

/// <summary>Session fields the core model doesn't carry (K3).</summary>
public class SessionSetup
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string Location { get; set; } = "";
    public DateTime? RegistrationOpensAt { get; set; }
    public DateTime? PriorityOpensAt { get; set; }
}

/// <summary>What a refund percentage applies to.</summary>
public enum RefundBasis { AmountPaid, BeyondDeposit }

/// <summary>
/// One row of a session's cancellation and refund table (K4, FR-49): cancellations at least
/// <see cref="DaysBefore"/> days before the start get <see cref="RefundPercent"/> of the basis,
/// less the admin fee. The row with the largest DaysBefore that the cancellation meets wins.
/// </summary>
public class RefundTier
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public int DaysBefore { get; set; }
    public int RefundPercent { get; set; }
    public RefundBasis Basis { get; set; }
    public int AdminFeeCents { get; set; }
}

/// <summary>
/// An admin-authored discount rule (K5, FR-62) wrapped around an existing <see cref="DiscountCode"/>.
/// Scope is a program, or one session of it; <see cref="Uses"/> only changes with a conditional update.
/// </summary>
public class DiscountRule
{
    public int Id { get; set; }
    public int DiscountCodeId { get; set; }
    public DiscountCode DiscountCode { get; set; } = null!;
    public string Name { get; set; } = "";
    public int? ProgramId { get; set; }
    public int? SessionId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }
    public int? MaxUses { get; set; }
    public int Uses { get; set; }
    public bool Stackable { get; set; }
    public bool Active { get; set; } = true;
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public enum WaiverVersionStatus { Draft, PendingApproval, Published, Archived }

/// <summary>
/// Every version of a waiver's text (K7, FR-37/103). Published and archived versions are never
/// edited; the live version's text is also on the <see cref="WaiverTemplate"/> row checkout reads.
/// </summary>
public class WaiverVersion
{
    public int Id { get; set; }
    public int WaiverTemplateId { get; set; }
    public int Version { get; set; }
    public string Body { get; set; } = "";
    public string ChangeNote { get; set; } = "";
    public WaiverVersionStatus Status { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public DateOnly? RetiredDate { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public string? SubmittedByEmail { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
}

/// <summary>A field's before and after value on an audit entry (K12).</summary>
public class AuditChange
{
    public long Id { get; set; }
    public long AuditEventId { get; set; }
    public AuditEvent AuditEvent { get; set; } = null!;
    public string Field { get; set; } = "";
    public string? Before { get; set; }
    public string? After { get; set; }
}

internal sealed class ProgramSetupConfig : IEntityTypeConfiguration<ProgramSetup>
{
    public void Configure(EntityTypeBuilder<ProgramSetup> b)
    {
        b.ToTable("ProgramSetups");
        b.HasOne<CampProgram>().WithOne().HasForeignKey<ProgramSetup>(x => x.ProgramId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.ProgramSetupId).OnDelete(DeleteBehavior.Cascade);
        b.Property(x => x.ReturnNote).HasMaxLength(1000);
    }
}

internal sealed class ProgramApprovalStepConfig : IEntityTypeConfiguration<ProgramApprovalStep>
{
    public void Configure(EntityTypeBuilder<ProgramApprovalStep> b)
    {
        b.ToTable("ProgramApprovalSteps");
        b.HasIndex(x => new { x.ProgramSetupId, x.Sequence }).IsUnique();
    }
}

internal sealed class SessionSetupConfig : IEntityTypeConfiguration<SessionSetup>
{
    public void Configure(EntityTypeBuilder<SessionSetup> b)
    {
        b.ToTable("SessionSetups");
        b.HasOne<Session>().WithOne().HasForeignKey<SessionSetup>(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RefundTierConfig : IEntityTypeConfiguration<RefundTier>
{
    public void Configure(EntityTypeBuilder<RefundTier> b)
    {
        b.ToTable("RefundTiers", t => t.HasCheckConstraint("CK_RefundTier_Percent", "[RefundPercent] BETWEEN 0 AND 100 AND [DaysBefore] >= 0 AND [AdminFeeCents] >= 0"));
        b.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.SessionId, x.DaysBefore }).IsUnique();
    }
}

internal sealed class DiscountRuleConfig : IEntityTypeConfiguration<DiscountRule>
{
    public void Configure(EntityTypeBuilder<DiscountRule> b)
    {
        b.ToTable("DiscountRules", t => t.HasCheckConstraint("CK_DiscountRule_Uses", "[Uses] >= 0 AND ([MaxUses] IS NULL OR [Uses] <= [MaxUses])"));
        b.HasOne(x => x.DiscountCode).WithMany().HasForeignKey(x => x.DiscountCodeId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.DiscountCodeId).IsUnique();
        b.HasOne<CampProgram>().WithMany().HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class WaiverVersionConfig : IEntityTypeConfiguration<WaiverVersion>
{
    public void Configure(EntityTypeBuilder<WaiverVersion> b)
    {
        b.ToTable("WaiverVersions");
        b.HasOne<WaiverTemplate>().WithMany().HasForeignKey(x => x.WaiverTemplateId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.WaiverTemplateId, x.Version }).IsUnique();
        // At most one draft or pending version per waiver.
        b.HasIndex(x => x.WaiverTemplateId).IsUnique().HasFilter("[Status] IN ('Draft', 'PendingApproval')").HasDatabaseName("IX_WaiverVersions_OneOpenDraft");
        b.Property(x => x.Body).HasMaxLength(8000);
        b.Property(x => x.ChangeNote).HasMaxLength(500);
    }
}

internal sealed class AuditChangeConfig : IEntityTypeConfiguration<AuditChange>
{
    public void Configure(EntityTypeBuilder<AuditChange> b)
    {
        b.ToTable("AuditChanges");
        b.HasOne(x => x.AuditEvent).WithMany().HasForeignKey(x => x.AuditEventId).OnDelete(DeleteBehavior.NoAction);
        b.Property(x => x.Before).HasMaxLength(1000);
        b.Property(x => x.After).HasMaxLength(1000);
    }
}
