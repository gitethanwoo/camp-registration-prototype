using Camp.Api.Auth;
using Camp.Api.Domain;

namespace Camp.Api.Features.Forms;

/// <summary>JSON shapes and labels shared by the form endpoints.</summary>
internal static class FormViews
{
    public static object Question(FormQuestion q) => new
    {
        q.Key,
        q.Label,
        q.HelpText,
        Type = q.Type.ToString(),
        Scope = q.Scope.ToString(),
        q.Required,
        Options = q.OptionList,
        q.ShowWhenKey,
        q.ShowWhenValue,
        q.Health,
    };

    public static string StatusLabel(FormVersionStatus s) => s switch
    {
        FormVersionStatus.PendingApproval => "Waiting for approval",
        FormVersionStatus.Published => "Live",
        FormVersionStatus.Retired => "Retired",
        _ => "Draft",
    };

    /// <summary>Why this admin can't approve the version, or null. Whoever started, edited or sent it can't approve it.</summary>
    public static string? ApprovalBlock(FormVersion v, StaffUser staff)
    {
        static bool Same(string? a, string? b) => a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        static bool In(string list, string? who) => who is not null && Split(list).Any(x => Same(x, who));
        return Same(v.CreatedByEmail, staff.Email) || Same(v.SubmittedByEmail, staff.Email) || Same(v.CreatedBy, staff.Actor) || Same(v.SubmittedBy, staff.Actor)
            || In(v.EditedByEmails, staff.Email) || In(v.EditedBy, staff.Actor)
            ? "You worked on this version, so a different admin has to approve it."
            : null;
    }

    /// <summary>Adds the admin saving a draft to its editors, once.</summary>
    public static void RecordEditor(FormVersion v, StaffUser staff)
    {
        static string Add(string list, string? who) =>
            string.IsNullOrWhiteSpace(who) || Split(list).Contains(who, StringComparer.OrdinalIgnoreCase) ? list : string.Join("; ", [.. Split(list), who]);
        v.EditedByEmails = Add(v.EditedByEmails, staff.Email);
        v.EditedBy = Add(v.EditedBy, staff.Actor);
    }

    public static IEnumerable<string> Editors(FormVersion v) => Split(v.EditedBy);

    static string[] Split(string list) => list.Split("; ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>A canonical text of a version's questions, to tell a real change from a copy of the live version.</summary>
    public static string Signature(IEnumerable<FormQuestion> questions) => string.Join("\n", questions.OrderBy(q => q.SortOrder)
        .Select(q => string.Join('\u001f', q.Key, q.Label, q.HelpText ?? "", q.Type, q.Scope, q.Required, q.Options ?? "", q.ShowWhenKey ?? "", q.ShowWhenValue ?? "", q.Health)));

    /// <summary>The question labels, for the before and after of an audit row.</summary>
    public static string Outline(IEnumerable<FormQuestion> questions) =>
        string.Join("; ", questions.OrderBy(q => q.SortOrder).Select(q => q.Label + (q.Required ? " *" : "")));

    /// <summary>Maps a core (pre-K6) question into a K6 question, for a program's first draft.</summary>
    public static FormQuestion FromLegacy(Question q) => new()
    {
        Key = q.Key,
        Label = q.Label,
        Type = q.Type switch
        {
            QuestionType.Select => FormQuestionType.SingleChoice,
            QuestionType.YesNo => FormQuestionType.YesNo,
            _ => FormQuestionType.ShortText,
        },
        Scope = q.Scope,
        Required = q.Required,
        Options = q.Type == QuestionType.Select ? q.Options : null,
        ShowWhenKey = q.ShowWhenKey,
        ShowWhenValue = q.ShowWhenValue,
        SortOrder = q.SortOrder,
    };

    public static FormQuestion Copy(FormQuestion q) => new()
    {
        Key = q.Key,
        Label = q.Label,
        HelpText = q.HelpText,
        Type = q.Type,
        Scope = q.Scope,
        Required = q.Required,
        Options = q.Options,
        ShowWhenKey = q.ShowWhenKey,
        ShowWhenValue = q.ShowWhenValue,
        SortOrder = q.SortOrder,
        Health = q.Health,
    };
}
