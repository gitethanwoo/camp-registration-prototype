using Camp.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Camp.Api.Features.Family;

public enum InvitationStatus { Pending, Cancelled }

/// <summary>
/// F3: an adult invited by email to share this household (FR-8). Accepting is AU1 and needs the
/// sign-in flow to map the invitee to this household, so it is not part of this slice.
/// </summary>
public class HouseholdInvitation
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public Household Household { get; set; } = null!;
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string InvitedBy { get; set; } = "";
    public InvitationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public enum BalancePaymentKind { Balance, Installment }
public enum BalancePaymentStatus { Pending, Succeeded, Declined }

/// <summary>
/// F6: a family paying what's left on an order, or retrying a failed installment. The row is
/// written as Pending before the card is charged, so a double-click or a retry with the same key
/// returns the first result, and only one payment per order can be in flight.
/// </summary>
public class BalancePayment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public PaymentOrder Order { get; set; } = null!;
    public string IdempotencyKey { get; set; } = "";
    public BalancePaymentKind Kind { get; set; }
    public int? InstallmentSequence { get; set; }
    public int AmountCents { get; set; }
    public BalancePaymentStatus Status { get; set; }
    public string? CardLast4 { get; set; }
    public string? DeclineReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

internal sealed class HouseholdInvitationConfiguration : IEntityTypeConfiguration<HouseholdInvitation>
{
    public void Configure(EntityTypeBuilder<HouseholdInvitation> b)
    {
        b.ToTable("HouseholdInvitations");
        b.HasIndex(i => new { i.HouseholdId, i.Status });
        b.HasOne(i => i.Household).WithMany().OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BalancePaymentConfiguration : IEntityTypeConfiguration<BalancePayment>
{
    public void Configure(EntityTypeBuilder<BalancePayment> b)
    {
        b.ToTable("BalancePayments");
        b.Property(p => p.IdempotencyKey).HasMaxLength(100);
        b.HasIndex(p => p.IdempotencyKey).IsUnique();
        // One payment in flight per order: a second attempt while the first is charging is refused.
        b.HasIndex(p => p.OrderId).IsUnique().HasFilter("[Status] = 'Pending'");
        b.HasOne(p => p.Order).WithMany().OnDelete(DeleteBehavior.Cascade);
    }
}
