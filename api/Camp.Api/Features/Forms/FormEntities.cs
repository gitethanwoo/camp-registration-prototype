using Camp.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Camp.Api.Features.Forms;

public enum FormVersionStatus { Draft, PendingApproval, Published, Retired }

/// <summary>The seven K6 question types (FR-74).</summary>
public enum FormQuestionType { ShortText, LongText, YesNo, SingleChoice, MultipleChoice, Date, Number }

/// <summary>
/// One version of a program's registration form (K6). Published and retired versions are never
/// edited: changes go into a new draft, which a different admin approves. At most one version per
/// program is published (the one the wizard asks), and at most one is open (draft or pending).
/// </summary>
public class FormVersion
{
    public int Id { get; set; }
    public int ProgramId { get; set; }
    public int Version { get; set; }
    public FormVersionStatus Status { get; set; }
    public string ChangeNote { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public string? CreatedByEmail { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public string? SubmittedByEmail { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? ReturnNote { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? RetiredAt { get; set; }
    public List<FormQuestion> Questions { get; set; } = [];
}

/// <summary>A question in one form version. Keys are stable across versions so answers line up.</summary>
public class FormQuestion
{
    public int Id { get; set; }
    public int FormVersionId { get; set; }
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string? HelpText { get; set; }
    public FormQuestionType Type { get; set; }
    public QuestionScope Scope { get; set; }
    public bool Required { get; set; }
    /// <summary>Pipe-separated choices for single and multiple choice.</summary>
    public string? Options { get; set; }
    public string? ShowWhenKey { get; set; }
    public string? ShowWhenValue { get; set; }
    public int SortOrder { get; set; }

    public IReadOnlyList<string> OptionList => string.IsNullOrEmpty(Options) ? [] : Options.Split('|');
}

/// <summary>
/// One answer given at checkout, tied to the form version and question it answered. Household
/// answers belong to the order (RegistrationId is null); camper answers to the registration.
/// </summary>
public class FormAnswer
{
    public long Id { get; set; }
    public int OrderId { get; set; }
    public int? RegistrationId { get; set; }
    public int FormVersionId { get; set; }
    public int FormQuestionId { get; set; }
    public FormQuestion Question { get; set; } = null!;
    public string Value { get; set; } = "";
}

internal sealed class FormVersionConfig : IEntityTypeConfiguration<FormVersion>
{
    public void Configure(EntityTypeBuilder<FormVersion> b)
    {
        b.ToTable("FormVersions");
        b.HasOne<CampProgram>().WithMany().HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ProgramId, x.Version }).IsUnique();
        b.HasIndex(x => x.ProgramId, "IX_FormVersions_OneOpen").IsUnique().HasFilter("[Status] IN ('Draft', 'PendingApproval')");
        b.HasIndex(x => x.ProgramId, "IX_FormVersions_OneLive").IsUnique().HasFilter("[Status] = 'Published'");
        b.Property(x => x.ChangeNote).HasMaxLength(300);
        b.Property(x => x.ReturnNote).HasMaxLength(500);
        b.HasMany(x => x.Questions).WithOne().HasForeignKey(q => q.FormVersionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FormQuestionConfig : IEntityTypeConfiguration<FormQuestion>
{
    public void Configure(EntityTypeBuilder<FormQuestion> b)
    {
        b.ToTable("FormQuestions");
        b.HasIndex(x => new { x.FormVersionId, x.Key }).IsUnique();
        b.Property(x => x.Key).HasMaxLength(40);
        b.Property(x => x.Label).HasMaxLength(200);
        b.Property(x => x.HelpText).HasMaxLength(300);
        b.Property(x => x.Options).HasMaxLength(2000);
        b.Property(x => x.ShowWhenKey).HasMaxLength(40);
        b.Property(x => x.ShowWhenValue).HasMaxLength(200);
        b.Ignore(x => x.OptionList);
    }
}

internal sealed class FormAnswerConfig : IEntityTypeConfiguration<FormAnswer>
{
    public void Configure(EntityTypeBuilder<FormAnswer> b)
    {
        b.ToTable("FormAnswers");
        b.HasOne<PaymentOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Registration>().WithMany().HasForeignKey(x => x.RegistrationId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<FormVersion>().WithMany().HasForeignKey(x => x.FormVersionId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Question).WithMany().HasForeignKey(x => x.FormQuestionId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.OrderId, x.RegistrationId });
        b.Property(x => x.Value).HasMaxLength(2000);
    }
}
