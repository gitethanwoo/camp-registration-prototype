using Camp.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Camp.Api.Features.Finance;

/// <summary>One day's Fiserv settlement file: the card transactions Fiserv paid out, and its fees (FN2).</summary>
public class SettlementBatch
{
    public int Id { get; set; }
    /// <summary>Fiserv's batch id, e.g. "FS-2026-09-23".</summary>
    public string Reference { get; set; } = "";
    public DateOnly SettledOn { get; set; }
    public DateTime ReceivedAt { get; set; }
    public List<SettlementLine> Lines { get; set; } = [];
}

public enum SettlementLineKind { Payment, Refund, Fee }

/// <summary>Matched: ties to a platform payment. Unmatched: nothing on our side yet. Resolved: a person decided.</summary>
public enum SettlementLineStatus { Matched, Unmatched, Resolved }

public enum SettlementResolution { MatchedToRegistration, Adjustment }

/// <summary>
/// One line of a settlement file. Payment and refund lines carry the processor reference; fee lines
/// are Fiserv's charges for the day. <see cref="AmountCents"/> is signed as it lands in the bank:
/// payments positive, refunds and fees negative.
/// </summary>
public class SettlementLine
{
    public int Id { get; set; }
    public int BatchId { get; set; }
    public SettlementBatch Batch { get; set; } = null!;
    public SettlementLineKind Kind { get; set; }
    public string ProcessorRef { get; set; } = "";
    public DateTime TransactedAt { get; set; }
    public int AmountCents { get; set; }
    public string Description { get; set; } = "";
    public string? CardholderName { get; set; }
    public string? CardLast4 { get; set; }
    /// <summary>The platform payment this line is, once matched or resolved as a match.</summary>
    public int? PaymentOperationId { get; set; }
    public PaymentOperation? PaymentOperation { get; set; }
    public SettlementLineStatus Status { get; set; }
    /// <summary>Why the line didn't match, in words, e.g. "No registration reference".</summary>
    public string? UnmatchedReason { get; set; }
    public SettlementResolution? Resolution { get; set; }
    public string? ResolutionNote { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public enum JournalStatus { Pending, Posted, Failed }

/// <summary>The Oracle Fusion journal entry for one settlement batch (FN4, FR-111).</summary>
public class JournalBatch
{
    public int Id { get; set; }
    /// <summary>e.g. "JRN-2026-09-23".</summary>
    public string Reference { get; set; } = "";
    public int SettlementBatchId { get; set; }
    public SettlementBatch SettlementBatch { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public JournalStatus Status { get; set; }
    public string? ErrorDetail { get; set; }
    public List<JournalEvent> Events { get; set; } = [];
}

/// <summary>A status change on a journal batch: its export trail.</summary>
public class JournalEvent
{
    public int Id { get; set; }
    public int JournalBatchId { get; set; }
    public JournalStatus Status { get; set; }
    public string Detail { get; set; } = "";
    public string Actor { get; set; } = "";
    public DateTime At { get; set; }
}

/// <summary>
/// What happened to a failed installment (FN3): when it failed, how many charges were tried, and
/// when the next automatic retry is. Retries follow Program Policies: 3 and 7 days after the failure;
/// the grace period ends 7 days after it.
/// </summary>
public class InstallmentFailure
{
    public int Id { get; set; }
    public int InstallmentId { get; set; }
    public Installment Installment { get; set; } = null!;
    public DateOnly FailedOn { get; set; }
    public int Attempts { get; set; }
    public string DeclineReason { get; set; } = "";
    public DateOnly? NextRetryOn { get; set; }
    public DateTime? LastContactedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

/// <summary>
/// The card a payment plan charges (FR-48). Production keeps only Fiserv's vault token; the sandbox
/// gateway's tokens live in memory, so <see cref="VaultRef"/> names the sandbox test card.
/// </summary>
public class FinanceCardOnFile
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string Brand { get; set; } = "";
    public string Last4 { get; set; } = "";
    public string VaultRef { get; set; } = "";
}

public enum ScholarshipStatus { Submitted, Approved, Denied }

/// <summary>A family's request for financial assistance toward one registration order (O6, O7).</summary>
public class ScholarshipApplication
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public Household Household { get; set; } = null!;
    public int OrderId { get; set; }
    public PaymentOrder Order { get; set; } = null!;
    public string SubmittedBy { get; set; } = "";
    public DateTime SubmittedAt { get; set; }
    public int RequestedCents { get; set; }
    public string IncomeBand { get; set; } = "";
    public string Reason { get; set; } = "";
    public ScholarshipStatus Status { get; set; }
    public int AwardCents { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public List<ScholarshipAwardLine> Lines { get; set; } = [];
    public ScholarshipDocument? Document { get; set; }
}

/// <summary>A camper the application covers, and the share of the award that went to their registration.</summary>
public class ScholarshipAwardLine
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public int RegistrationId { get; set; }
    public Registration Registration { get; set; } = null!;
    public int AmountCents { get; set; }
}

/// <summary>The supporting document (tax return, pay stub). Staff-only after upload.</summary>
public class ScholarshipDocument
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public int SizeBytes { get; set; }
    public byte[] Content { get; set; } = [];
    public DateTime UploadedAt { get; set; }
}

internal sealed class SettlementBatchConfig : IEntityTypeConfiguration<SettlementBatch>
{
    public void Configure(EntityTypeBuilder<SettlementBatch> b)
    {
        b.ToTable("SettlementBatches");
        b.HasIndex(x => x.Reference).IsUnique();
        b.HasIndex(x => x.SettledOn);
        b.HasMany(x => x.Lines).WithOne(l => l.Batch).HasForeignKey(l => l.BatchId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SettlementLineConfig : IEntityTypeConfiguration<SettlementLine>
{
    public void Configure(EntityTypeBuilder<SettlementLine> b)
    {
        b.ToTable("SettlementLines");
        b.Property(x => x.ResolutionNote).HasMaxLength(500);
        b.HasOne(x => x.PaymentOperation).WithMany().HasForeignKey(x => x.PaymentOperationId).OnDelete(DeleteBehavior.NoAction);
        // A platform payment settles once.
        b.HasIndex(x => x.PaymentOperationId).IsUnique().HasFilter("[PaymentOperationId] IS NOT NULL");
        b.HasIndex(x => new { x.BatchId, x.Status });
    }
}

internal sealed class JournalBatchConfig : IEntityTypeConfiguration<JournalBatch>
{
    public void Configure(EntityTypeBuilder<JournalBatch> b)
    {
        b.ToTable("JournalBatches");
        b.HasIndex(x => x.Reference).IsUnique();
        b.HasIndex(x => x.SettlementBatchId).IsUnique();
        b.HasOne(x => x.SettlementBatch).WithMany().HasForeignKey(x => x.SettlementBatchId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(x => x.Events).WithOne().HasForeignKey(e => e.JournalBatchId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class InstallmentFailureConfig : IEntityTypeConfiguration<InstallmentFailure>
{
    public void Configure(EntityTypeBuilder<InstallmentFailure> b)
    {
        b.ToTable("InstallmentFailures");
        b.HasIndex(x => x.InstallmentId).IsUnique();
        b.HasOne(x => x.Installment).WithMany().HasForeignKey(x => x.InstallmentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FinanceCardOnFileConfig : IEntityTypeConfiguration<FinanceCardOnFile>
{
    public void Configure(EntityTypeBuilder<FinanceCardOnFile> b)
    {
        b.ToTable("FinanceCardsOnFile");
        b.HasIndex(x => x.OrderId).IsUnique();
        b.HasOne<PaymentOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ScholarshipApplicationConfig : IEntityTypeConfiguration<ScholarshipApplication>
{
    public void Configure(EntityTypeBuilder<ScholarshipApplication> b)
    {
        b.ToTable("ScholarshipApplications");
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.DecisionNote).HasMaxLength(1000);
        b.HasOne(x => x.Household).WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.NoAction);
        // One open application per order, enforced by the database.
        b.HasIndex(x => x.OrderId).IsUnique().HasFilter("[Status] = 'Submitted'");
        b.HasIndex(x => new { x.Status, x.SubmittedAt });
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Document).WithOne().HasForeignKey<ScholarshipDocument>(d => d.ApplicationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ScholarshipAwardLineConfig : IEntityTypeConfiguration<ScholarshipAwardLine>
{
    public void Configure(EntityTypeBuilder<ScholarshipAwardLine> b)
    {
        b.ToTable("ScholarshipAwardLines");
        b.HasOne(x => x.Registration).WithMany().HasForeignKey(x => x.RegistrationId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.ApplicationId, x.RegistrationId }).IsUnique();
    }
}

internal sealed class ScholarshipDocumentConfig : IEntityTypeConfiguration<ScholarshipDocument>
{
    public void Configure(EntityTypeBuilder<ScholarshipDocument> b)
    {
        b.ToTable("ScholarshipDocuments");
        b.HasIndex(x => x.ApplicationId).IsUnique();
    }
}
