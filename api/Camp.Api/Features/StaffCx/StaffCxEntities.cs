using Camp.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Camp.Api.Features.StaffCx;

/// <summary>An internal note on a household (C2). Staff-only; always carries its author.</summary>
public class HouseholdNote
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public string Body { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>One ticked item on a household's verification checklist (C2). Absent row = not checked.</summary>
public class HouseholdVerification
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public string ItemKey { get; set; } = "";
    public string CheckedBy { get; set; } = "";
    public DateTime CheckedAt { get; set; }
}

public enum RequesterType { Host, Partner }
public enum ReviewDecision { Pending, Approved, Rejected }

/// <summary>
/// The full rule a host or partner asked for when requesting a discount code (C8, FR-62/63).
/// One per <see cref="DiscountCode"/>. The code stays inert (<see cref="DiscountStatus.PendingApproval"/>)
/// until an approval flips it.
/// </summary>
public class DiscountRequest
{
    public int Id { get; set; }
    public int DiscountCodeId { get; set; }
    public DiscountCode DiscountCode { get; set; } = null!;
    public RequesterType RequesterType { get; set; }
    public string RequestedBy { get; set; } = "";
    public string Organization { get; set; } = "";
    public int ProgramId { get; set; }
    public CampProgram Program { get; set; } = null!;
    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }
    public int? MaxUses { get; set; }
    public bool Stackable { get; set; }
    public bool OverridesOtherCodes { get; set; }
    public string RequesterNote { get; set; } = "";
    public DateTime RequestedAt { get; set; }
    public ReviewDecision Decision { get; set; }
    public string? ReviewNote { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

/// <summary>A completed duplicate merge (C9). The merged household stays as an archived record.</summary>
public class HouseholdMerge
{
    public int Id { get; set; }
    public int SurvivorHouseholdId { get; set; }
    public int MergedHouseholdId { get; set; }
    /// <summary>What was chosen: field picks, conflict resolutions, the losing values.</summary>
    public string DetailJson { get; set; } = "{}";
    public string Actor { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public enum TransferStatus { Pending, Approved, Denied }

/// <summary>A family's request to move a registration to another session of the same program (F8, C10).</summary>
public class TransferRequest
{
    public int Id { get; set; }
    public int RegistrationId { get; set; }
    public Registration Registration { get; set; } = null!;
    public int HouseholdId { get; set; }
    public int FromSessionId { get; set; }
    public Session FromSession { get; set; } = null!;
    public int FromPoolId { get; set; }
    public int ToSessionId { get; set; }
    public Session ToSession { get; set; } = null!;
    public string Reason { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public TransferStatus Status { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public int? PriceDifferenceCents { get; set; }
    public int? RefundCents { get; set; }
}

internal sealed class HouseholdNoteConfig : IEntityTypeConfiguration<HouseholdNote>
{
    public void Configure(EntityTypeBuilder<HouseholdNote> b)
    {
        b.ToTable("HouseholdNotes");
        b.Property(x => x.Body).HasMaxLength(2000);
        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.HouseholdId, x.CreatedAt });
    }
}

internal sealed class HouseholdVerificationConfig : IEntityTypeConfiguration<HouseholdVerification>
{
    public void Configure(EntityTypeBuilder<HouseholdVerification> b)
    {
        b.ToTable("HouseholdVerifications");
        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.HouseholdId, x.ItemKey }).IsUnique();
    }
}

internal sealed class DiscountRequestConfig : IEntityTypeConfiguration<DiscountRequest>
{
    public void Configure(EntityTypeBuilder<DiscountRequest> b)
    {
        b.ToTable("DiscountRequests");
        b.HasIndex(x => x.DiscountCodeId).IsUnique();
        b.HasOne(x => x.DiscountCode).WithMany().HasForeignKey(x => x.DiscountCodeId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Program).WithMany().HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.NoAction);
        b.Property(x => x.RequesterNote).HasMaxLength(1000);
        b.Property(x => x.ReviewNote).HasMaxLength(1000);
    }
}

internal sealed class HouseholdMergeConfig : IEntityTypeConfiguration<HouseholdMerge>
{
    public void Configure(EntityTypeBuilder<HouseholdMerge> b)
    {
        b.ToTable("HouseholdMerges");
        b.Property(x => x.DetailJson).HasMaxLength(4000);
        b.HasIndex(x => x.MergedHouseholdId).IsUnique();
        b.HasIndex(x => x.SurvivorHouseholdId);
    }
}

internal sealed class TransferRequestConfig : IEntityTypeConfiguration<TransferRequest>
{
    public void Configure(EntityTypeBuilder<TransferRequest> b)
    {
        b.ToTable("TransferRequests");
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.DecisionNote).HasMaxLength(500);
        b.HasOne(x => x.Registration).WithMany().HasForeignKey(x => x.RegistrationId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.FromSession).WithMany().HasForeignKey(x => x.FromSessionId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.ToSession).WithMany().HasForeignKey(x => x.ToSessionId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.Status, x.CreatedAt });
        // At most one open request per registration, enforced by the database.
        b.HasIndex(x => x.RegistrationId).IsUnique().HasFilter("[Status] = 'Pending'");
    }
}
