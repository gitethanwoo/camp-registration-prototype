using Camp.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Camp.Api.Features.Admittance;

/// <summary>Where an application is in review. Staff never see drafts.</summary>
public enum ApplicationStage { Draft, Submitted, UnderReview, InfoRequested, Approved, Declined, Waitlisted }

/// <summary>The card hold behind an application (FR-46: authorized, not charged, until approval).</summary>
public enum HoldStatus { None, Authorized, Captured, Voided }

/// <summary>
/// A couple's application to an admittance program (FR-26). It carries the card authorization
/// itself; a <see cref="PaymentOrder"/> and <see cref="Registration"/> exist only once the hold is
/// captured, so no other screen ever shows an authorized amount as a balance due.
/// </summary>
public class AdmittanceApplication
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public Household Household { get; set; } = null!;
    public int SessionId { get; set; }
    public Session Session { get; set; } = null!;
    public int ApplicantPersonId { get; set; }
    public Person Applicant { get; set; } = null!;

    // The spouse is a household adult when there is one; otherwise just a name on the application.
    public int? SpousePersonId { get; set; }
    public Person? Spouse { get; set; }
    public string SpouseFirstName { get; set; } = "";
    public string SpouseLastName { get; set; } = "";
    public string? SpouseEmail { get; set; }

    public ApplicationStage Stage { get; set; }
    /// <summary>Wizard step the family last saved on, so "resume later" lands in the right place.</summary>
    public int CurrentStep { get; set; }
    public string AnswersJson { get; set; } = "{}";

    /// <summary>
    /// The program's waivers the couple accepted at submit, as "templateId:version" pairs joined by ';', and
    /// who signed. Approval writes them onto the registration as <see cref="WaiverAcceptance"/> rows.
    /// </summary>
    public string? WaiversAccepted { get; set; }
    public string? WaiverSignerName { get; set; }
    public DateTime? WaiverSignedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ReviewStartedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? ReviewedBy { get; set; }
    /// <summary>Message sent to the couple with a decline or waitlist decision.</summary>
    public string? DecisionNote { get; set; }

    public string? InfoRequest { get; set; }
    public DateTime? InfoRequestedAt { get; set; }
    public string? InfoResponse { get; set; }
    public DateTime? InfoRespondedAt { get; set; }

    public HoldStatus Hold { get; set; }
    public int AmountCents { get; set; }
    public string? AuthorizationRef { get; set; }
    public string? CardLast4 { get; set; }
    public DateTime? AuthorizedAt { get; set; }
    public DateTime? AuthorizationExpiresAt { get; set; }

    /// <summary>True once approval has claimed a seat in the session's pool.</summary>
    public bool SeatHeld { get; set; }
    /// <summary>The pool approval claimed a seat in; a decline or capture uses exactly this pool.</summary>
    public int? PoolId { get; set; }
    public int? RegistrationId { get; set; }
    public int? OrderId { get; set; }

    /// <summary>Optimistic concurrency: two staff deciding at once can't both win.</summary>
    public byte[] RowVersion { get; set; } = [];

    public string CoupleName =>
        SpouseFirstName.Length == 0 ? Applicant.FullName
        : SpouseLastName == Applicant.LastName ? $"{Applicant.FirstName} and {SpouseFirstName} {Applicant.LastName}"
        : $"{Applicant.FullName} and {SpouseFirstName} {SpouseLastName}";

    public bool HoldUsable(DateTime now) => Hold == HoldStatus.Authorized && AuthorizationExpiresAt > now;

    public static readonly ApplicationStage[] Pending = [ApplicationStage.Submitted, ApplicationStage.UnderReview, ApplicationStage.InfoRequested];
}

internal sealed class AdmittanceApplicationConfiguration : IEntityTypeConfiguration<AdmittanceApplication>
{
    public void Configure(EntityTypeBuilder<AdmittanceApplication> b)
    {
        b.ToTable("AdmittanceApplications");
        // One application per household per session; a second Apply resumes the first.
        b.HasIndex(a => new { a.HouseholdId, a.SessionId }).IsUnique();
        b.HasIndex(a => new { a.SessionId, a.Stage });
        b.HasOne(a => a.Household).WithMany().OnDelete(DeleteBehavior.NoAction);
        b.HasOne(a => a.Session).WithMany().OnDelete(DeleteBehavior.NoAction);
        b.HasOne(a => a.Applicant).WithMany().HasForeignKey(a => a.ApplicantPersonId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(a => a.Spouse).WithMany().HasForeignKey(a => a.SpousePersonId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Registration>().WithMany().HasForeignKey(a => a.RegistrationId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<PaymentOrder>().WithMany().HasForeignKey(a => a.OrderId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<CapacityPool>().WithMany().HasForeignKey(a => a.PoolId).OnDelete(DeleteBehavior.NoAction);
        b.Property(a => a.AnswersJson).HasMaxLength(8000);
        b.Property(a => a.WaiversAccepted).HasMaxLength(500);
        b.Property(a => a.WaiverSignerName).HasMaxLength(120);
        b.Property(a => a.InfoRequest).HasMaxLength(2000);
        b.Property(a => a.InfoResponse).HasMaxLength(2000);
        b.Property(a => a.DecisionNote).HasMaxLength(2000);
        b.Property(a => a.RowVersion).IsRowVersion();
        b.Ignore(a => a.CoupleName);
    }
}
