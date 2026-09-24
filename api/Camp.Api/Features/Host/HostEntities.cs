using Camp.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Camp.Api.Features.Host;

/// <summary>A partner church or organization that hosts a WinShape event (e.g. Grace Community Church).</summary>
public class HostOrganization
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
}

/// <summary>
/// Links a host-role staff sign-in (by email) to the one organization it acts for. Host endpoints
/// resolve the organization through this row and never from the request.
/// </summary>
public class HostMember
{
    public int Id { get; set; }
    public int HostOrganizationId { get; set; }
    public HostOrganization HostOrganization { get; set; } = null!;
    public string Email { get; set; } = "";
    public string Title { get; set; } = "";
}

/// <summary>A session a host organization runs, plus the figures the host home compares against.</summary>
public class HostEvent
{
    public int Id { get; set; }
    public int HostOrganizationId { get; set; }
    public HostOrganization HostOrganization { get; set; } = null!;
    public int SessionId { get; set; }
    public Session Session { get; set; } = null!;
    /// <summary>Registrations for the same event last year, imported from the previous system.</summary>
    public int LastYearRegistrations { get; set; }
    /// <summary>Every volunteer must be through vetting by this date.</summary>
    public DateOnly VettingDeadline { get; set; }
}

public enum VettingStatus { NotStarted, InProgress, Approved }

public class HostVolunteer
{
    public int Id { get; set; }
    public int HostOrganizationId { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public DateOnly DateOfBirth { get; set; }
    public string Role { get; set; } = "";
    public VettingStatus VettingStatus { get; set; }
    public int? UploadRowId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>A CSV batch of volunteers. Every row in the file is stored, whatever its outcome.</summary>
public class VolunteerUpload
{
    public int Id { get; set; }
    public int HostOrganizationId { get; set; }
    public string FileName { get; set; } = "";
    public string UploadedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSubmittedAt { get; set; }
    public List<VolunteerUploadRow> Rows { get; set; } = [];
}

/// <summary>Valid rows wait for Submit; Error rows are held back until fixed or skipped.</summary>
public enum UploadRowStatus { Valid, Error, Skipped, Submitted }

public class VolunteerUploadRow
{
    public int Id { get; set; }
    public int UploadId { get; set; }
    /// <summary>1-based position among the file's volunteer rows (the header is not counted).</summary>
    public int RowNumber { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    /// <summary>As typed in the file, so an invalid date can be shown and fixed.</summary>
    public string DateOfBirth { get; set; } = "";
    public string Role { get; set; } = "";
    public UploadRowStatus Status { get; set; }
    /// <summary>Short reason, e.g. "Missing email". Null when the row is valid.</summary>
    public string? Issue { get; set; }
    /// <summary>What to do about it, e.g. "Email is required for all volunteers."</summary>
    public string? Detail { get; set; }
    /// <summary>The field to fix first (firstName, lastName, email, dateOfBirth, role).</summary>
    public string? Field { get; set; }
}

/// <summary>An invoice from WinShape to a host. Its status is worked out from its lines and payments.</summary>
public class HostInvoice
{
    public int Id { get; set; }
    public int HostOrganizationId { get; set; }
    public int? HostEventId { get; set; }
    public string Number { get; set; } = "";
    public string Description { get; set; } = "";
    public string Period { get; set; } = "";
    public DateOnly IssuedOn { get; set; }
    public DateOnly DueDate { get; set; }
    public List<HostInvoiceLine> Lines { get; set; } = [];
    public List<HostInvoicePayment> Payments { get; set; } = [];
}

public class HostInvoiceLine
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public string Description { get; set; } = "";
    public int AmountCents { get; set; }
    public int SortOrder { get; set; }
}

public enum HostPaymentStatus { Pending, Succeeded, Declined }

/// <summary>
/// One attempt to pay an invoice. Written as Pending before the card is charged, so a double-click
/// or a retry with the same key returns the first result, and only one attempt per invoice runs.
/// </summary>
public class HostInvoicePayment
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public int AmountCents { get; set; }
    public HostPaymentStatus Status { get; set; }
    public string? ProcessorRef { get; set; }
    public string? CardLast4 { get; set; }
    public string? DeclineReason { get; set; }
    public string PaidBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

internal sealed class HostOrganizationConfiguration : IEntityTypeConfiguration<HostOrganization>
{
    public void Configure(EntityTypeBuilder<HostOrganization> b)
    {
        b.ToTable("HostOrganizations");
        b.HasIndex(o => o.Name).IsUnique();
    }
}

internal sealed class HostMemberConfiguration : IEntityTypeConfiguration<HostMember>
{
    public void Configure(EntityTypeBuilder<HostMember> b)
    {
        b.ToTable("HostMembers");
        b.HasIndex(m => m.Email).IsUnique();
        b.HasOne(m => m.HostOrganization).WithMany().OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class HostEventConfiguration : IEntityTypeConfiguration<HostEvent>
{
    public void Configure(EntityTypeBuilder<HostEvent> b)
    {
        b.ToTable("HostEvents");
        b.HasIndex(e => e.SessionId).IsUnique();
        b.HasOne(e => e.HostOrganization).WithMany().OnDelete(DeleteBehavior.Cascade);
        b.HasOne(e => e.Session).WithMany().OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class HostVolunteerConfiguration : IEntityTypeConfiguration<HostVolunteer>
{
    public void Configure(EntityTypeBuilder<HostVolunteer> b)
    {
        b.ToTable("HostVolunteers");
        // One volunteer per email per organization: a duplicate is an upload error, never a second row.
        b.HasIndex(v => new { v.HostOrganizationId, v.Email }).IsUnique();
        b.HasOne<HostOrganization>().WithMany().HasForeignKey(v => v.HostOrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class VolunteerUploadConfiguration : IEntityTypeConfiguration<VolunteerUpload>
{
    public void Configure(EntityTypeBuilder<VolunteerUpload> b)
    {
        b.ToTable("VolunteerUploads");
        b.HasOne<HostOrganization>().WithMany().HasForeignKey(u => u.HostOrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(u => u.Rows).WithOne().HasForeignKey(r => r.UploadId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class VolunteerUploadRowConfiguration : IEntityTypeConfiguration<VolunteerUploadRow>
{
    public void Configure(EntityTypeBuilder<VolunteerUploadRow> b)
    {
        b.ToTable("VolunteerUploadRows");
        b.HasIndex(r => new { r.UploadId, r.RowNumber }).IsUnique();
    }
}

internal sealed class HostInvoiceConfiguration : IEntityTypeConfiguration<HostInvoice>
{
    public void Configure(EntityTypeBuilder<HostInvoice> b)
    {
        b.ToTable("HostInvoices");
        b.HasIndex(i => i.Number).IsUnique();
        b.HasOne<HostOrganization>().WithMany().HasForeignKey(i => i.HostOrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<HostEvent>().WithMany().HasForeignKey(i => i.HostEventId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(i => i.Payments).WithOne().HasForeignKey(p => p.InvoiceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class HostInvoiceLineConfiguration : IEntityTypeConfiguration<HostInvoiceLine>
{
    public void Configure(EntityTypeBuilder<HostInvoiceLine> b)
    {
        b.ToTable("HostInvoiceLines");
    }
}

internal sealed class HostInvoicePaymentConfiguration : IEntityTypeConfiguration<HostInvoicePayment>
{
    public void Configure(EntityTypeBuilder<HostInvoicePayment> b)
    {
        b.ToTable("HostInvoicePayments");
        b.Property(p => p.IdempotencyKey).HasMaxLength(100);
        b.HasIndex(p => p.IdempotencyKey).IsUnique();
        // One payment in flight per invoice: a second attempt while the first is charging is refused.
        b.HasIndex(p => p.InvoiceId).IsUnique().HasFilter("[Status] = 'Pending'");
    }
}
