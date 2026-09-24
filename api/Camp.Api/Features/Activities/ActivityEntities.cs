using Camp.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Camp.Api.Features.Activities;

/// <summary>An activity in the catalog (K8, FR-23/24). Sessions schedule it per block and period as <see cref="ActivitySlot"/>s.</summary>
public class Activity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string WhatToBring { get; set; } = "";
    /// <summary>A local image under the web app's public folder, e.g. /images/activities/archery.svg.</summary>
    public string ImageUrl { get; set; } = "";
    public int GradeMin { get; set; }
    public int GradeMax { get; set; }
    public int DefaultCapacity { get; set; } = 24;
    /// <summary>Campers per staff member, e.g. 8 for 1:8.</summary>
    public int StaffRatio { get; set; }
    /// <summary>What an instructor must hold, e.g. "Certified lifeguard". Empty when any trained counselor can lead.</summary>
    public string Instructor { get; set; } = "";
    public string Space { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>
/// An age block of a session with its own schedule grid. ON Session 3 runs two: Juniors (grades 3–5)
/// and Seniors (grades 6–8), because 186 campers can't fit one 6 × 24 grid (144 seats per period).
/// </summary>
public class ActivityBlock
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string Name { get; set; } = "";
    public int GradeMin { get; set; }
    public int GradeMax { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>One activity in one period for one block. <see cref="Assigned"/> only changes by conditional SQL.</summary>
public class ActivitySlot
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public int BlockId { get; set; }
    public int ActivityId { get; set; }
    public int Period { get; set; }
    public int Capacity { get; set; }
    public int Assigned { get; set; }
    public string Instructor { get; set; } = "";
    public string Space { get; set; } = "";
}

/// <summary>A family's ranked choice for one camper and period (R4). Rank 1 is the first choice.</summary>
public class ActivityPreference
{
    public int Id { get; set; }
    public int RegistrationId { get; set; }
    public int Period { get; set; }
    public int Rank { get; set; }
    public int ActivityId { get; set; }
}

/// <summary>A camper's place in a slot. Server mutations keep one per camper and period; O4 flags any extra.</summary>
public class ActivityAssignment
{
    public int Id { get; set; }
    public int RegistrationId { get; set; }
    public int SessionId { get; set; }
    public int SlotId { get; set; }
    public int Period { get; set; }
    /// <summary>Checkout, Family, Preferences (O4 run), Staff (O4 move) or Seed.</summary>
    public string Source { get; set; } = "";
    public DateTime AssignedAt { get; set; }
}

/// <summary>
/// A cabinmate request as the family typed it (R5, FR-25). When it identifies a camper in the same
/// session, <see cref="MatchedRegistrationId"/> is set and an OpsBuddyRequest row is added for O3.
/// </summary>
public class CabinmateRequest
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public int RegistrationId { get; set; }
    public string FriendName { get; set; } = "";
    public string Contact { get; set; } = "";
    public int? MatchedRegistrationId { get; set; }
    public DateTime CreatedAt { get; set; }
}

internal sealed class ActivityConfig : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> b)
    {
        b.ToTable("Activities");
        b.Property(x => x.Name).HasMaxLength(60);
        b.Property(x => x.Category).HasMaxLength(40);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.WhatToBring).HasMaxLength(300);
        b.Property(x => x.ImageUrl).HasMaxLength(200);
        b.Property(x => x.Instructor).HasMaxLength(80);
        b.Property(x => x.Space).HasMaxLength(60);
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class ActivityBlockConfig : IEntityTypeConfiguration<ActivityBlock>
{
    public void Configure(EntityTypeBuilder<ActivityBlock> b)
    {
        b.ToTable("ActivityBlocks");
        b.Property(x => x.Name).HasMaxLength(40);
        b.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.SessionId, x.SortOrder });
    }
}

internal sealed class ActivitySlotConfig : IEntityTypeConfiguration<ActivitySlot>
{
    public void Configure(EntityTypeBuilder<ActivitySlot> b)
    {
        b.ToTable("ActivitySlots");
        b.Property(x => x.Instructor).HasMaxLength(80);
        b.Property(x => x.Space).HasMaxLength(60);
        b.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ActivityBlock>().WithMany().HasForeignKey(x => x.BlockId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Activity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.BlockId, x.ActivityId, x.Period }).IsUnique();
        b.HasIndex(x => x.SessionId);
        b.ToTable(t => t.HasCheckConstraint("CK_ActivitySlot_Period", "[Period] BETWEEN 1 AND 3"));
    }
}

internal sealed class ActivityPreferenceConfig : IEntityTypeConfiguration<ActivityPreference>
{
    public void Configure(EntityTypeBuilder<ActivityPreference> b)
    {
        b.ToTable("ActivityPreferences");
        b.HasOne<Registration>().WithMany().HasForeignKey(x => x.RegistrationId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Activity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.RegistrationId, x.Period, x.Rank }).IsUnique();
    }
}

internal sealed class ActivityAssignmentConfig : IEntityTypeConfiguration<ActivityAssignment>
{
    public void Configure(EntityTypeBuilder<ActivityAssignment> b)
    {
        b.ToTable("ActivityAssignments");
        b.Property(x => x.Source).HasMaxLength(20);
        b.HasOne<Registration>().WithMany().HasForeignKey(x => x.RegistrationId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ActivitySlot>().WithMany().HasForeignKey(x => x.SlotId).OnDelete(DeleteBehavior.NoAction);
        // One place per camper per period: a second concurrent write fails instead of double-booking.
        b.HasIndex(x => new { x.RegistrationId, x.Period }).IsUnique();
        b.HasIndex(x => x.SessionId);
        b.HasIndex(x => x.SlotId);
    }
}

internal sealed class CabinmateRequestConfig : IEntityTypeConfiguration<CabinmateRequest>
{
    public void Configure(EntityTypeBuilder<CabinmateRequest> b)
    {
        b.ToTable("CabinmateRequests");
        b.Property(x => x.FriendName).HasMaxLength(100);
        b.Property(x => x.Contact).HasMaxLength(200);
        b.HasOne<Registration>().WithMany().HasForeignKey(x => x.RegistrationId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Registration>().WithMany().HasForeignKey(x => x.MatchedRegistrationId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => x.RegistrationId);
        b.HasIndex(x => x.SessionId);
    }
}
