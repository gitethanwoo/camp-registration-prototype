using Camp.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Camp.Api.Features.Ops;

/// <summary>A cabin for one gender (O3, FR-82). Beds is its capacity; campers are placements pointing here.</summary>
public class OpsCabin
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string Name { get; set; } = "";
    public Gender Gender { get; set; }
    public int Beds { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>An activity group inside one capacity pool (O2, FR-80), e.g. Boys G6–8 · Group 2 of 10.</summary>
public class OpsGroup
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public int PoolId { get; set; }
    public string Name { get; set; } = "";
    public int Capacity { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// Operations state for one registration: cabin, group, first activity choice, reminders and
/// arrival. The row is optional; a registration without one is simply unassigned and not checked in.
/// </summary>
public class OpsPlacement
{
    public int Id { get; set; }
    public int RegistrationId { get; set; }
    public int SessionId { get; set; }
    public int? CabinId { get; set; }
    public int? GroupId { get; set; }
    /// <summary>An Auto-suggest proposal waiting for a person to approve it. Never applied on its own.</summary>
    public int? SuggestedGroupId { get; set; }
    public string? SuggestionReason { get; set; }
    public string? Activity { get; set; }
    public DateTime? RemindedAt { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public string? CheckedInBy { get; set; }
    public string? CheckInOverride { get; set; }
    public DateTime? CheckedOutAt { get; set; }
    public string? CheckedOutBy { get; set; }
    public string? PickedUpBy { get; set; }
}

/// <summary>A camper asked to be with another camper (FR-25). O2 judges it by group, O3 by cabin.</summary>
public class OpsBuddyRequest
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public int RegistrationId { get; set; }
    public int RequestedRegistrationId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>An adult the household authorized to collect its campers at check-out (O5, FR-84).</summary>
public class OpsPickupAdult
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public string Name { get; set; } = "";
    public string Relationship { get; set; } = "";
    public string Phone { get; set; } = "";
}

/// <summary>When staff last reviewed a session's rooming. Roster changes after this are flagged for re-review.</summary>
public class OpsRoomingReview
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public DateTime ReviewedAt { get; set; }
    public string ReviewedBy { get; set; } = "";
}

internal sealed class OpsCabinConfig : IEntityTypeConfiguration<OpsCabin>
{
    public void Configure(EntityTypeBuilder<OpsCabin> b)
    {
        b.ToTable("OpsCabins");
        b.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.SessionId, x.SortOrder });
        b.ToTable(t => t.HasCheckConstraint("CK_OpsCabin_Beds", "[Beds] > 0"));
    }
}

internal sealed class OpsGroupConfig : IEntityTypeConfiguration<OpsGroup>
{
    public void Configure(EntityTypeBuilder<OpsGroup> b)
    {
        b.ToTable("OpsGroups");
        b.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<CapacityPool>().WithMany().HasForeignKey(x => x.PoolId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.PoolId, x.SortOrder });
        b.ToTable(t => t.HasCheckConstraint("CK_OpsGroup_Capacity", "[Capacity] > 0"));
    }
}

internal sealed class OpsPlacementConfig : IEntityTypeConfiguration<OpsPlacement>
{
    public void Configure(EntityTypeBuilder<OpsPlacement> b)
    {
        b.ToTable("OpsPlacements");
        b.HasOne<Registration>().WithMany().HasForeignKey(x => x.RegistrationId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<OpsCabin>().WithMany().HasForeignKey(x => x.CabinId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<OpsGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<OpsGroup>().WithMany().HasForeignKey(x => x.SuggestedGroupId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => x.RegistrationId).IsUnique();
        b.HasIndex(x => x.SessionId);
        b.Property(x => x.CheckInOverride).HasMaxLength(500);
    }
}

internal sealed class OpsBuddyRequestConfig : IEntityTypeConfiguration<OpsBuddyRequest>
{
    public void Configure(EntityTypeBuilder<OpsBuddyRequest> b)
    {
        b.ToTable("OpsBuddyRequests");
        b.HasOne<Registration>().WithMany().HasForeignKey(x => x.RegistrationId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Registration>().WithMany().HasForeignKey(x => x.RequestedRegistrationId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.RegistrationId, x.RequestedRegistrationId }).IsUnique();
        b.HasIndex(x => x.SessionId);
    }
}

internal sealed class OpsPickupAdultConfig : IEntityTypeConfiguration<OpsPickupAdult>
{
    public void Configure(EntityTypeBuilder<OpsPickupAdult> b)
    {
        b.ToTable("OpsPickupAdults");
        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => x.HouseholdId);
    }
}

internal sealed class OpsRoomingReviewConfig : IEntityTypeConfiguration<OpsRoomingReview>
{
    public void Configure(EntityTypeBuilder<OpsRoomingReview> b)
    {
        b.ToTable("OpsRoomingReviews");
        b.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => x.SessionId).IsUnique();
    }
}
