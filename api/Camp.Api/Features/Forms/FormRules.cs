using System.Text.RegularExpressions;
using Camp.Api.Data;
using Camp.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Forms;

public record FormQuestionInput(
    string? Key,
    string? Label,
    string? HelpText,
    FormQuestionType Type,
    QuestionScope Scope,
    bool Required,
    List<string>? Options,
    string? ShowWhenKey,
    string? ShowWhenValue,
    bool Health = false);

public record FormDraftInput(string? ChangeNote, List<FormQuestionInput>? Questions);

/// <summary>A cleaned answer: the question it answers and the stored value.</summary>
public sealed record CleanAnswer(FormQuestion Question, string Value);

/// <summary>
/// The rules the builder (K6) and checkout share: what a valid form looks like, which questions a
/// family sees, and what a valid answer to each type is. The wizard mirrors these; the server decides.
/// </summary>
public static partial class FormRules
{
    public const int MaxQuestions = 60;
    public const int MaxShortText = 200;
    public const int MaxLongText = 2000;

    [GeneratedRegex("^[a-z][A-Za-z0-9_]{0,39}$")]
    private static partial Regex KeyPattern();

    public static bool IsChoice(FormQuestionType t) => t is FormQuestionType.SingleChoice or FormQuestionType.MultipleChoice;

    static bool CanDriveCondition(FormQuestionType t) => t is FormQuestionType.YesNo || IsChoice(t);

    /// <summary>The live (published) version of a program's form, with questions in order, or null.</summary>
    public static async Task<FormVersion?> LiveAsync(CampDbContext db, int programId, CancellationToken ct = default)
    {
        var live = await db.Set<FormVersion>().AsNoTracking().Include(v => v.Questions)
            .FirstOrDefaultAsync(v => v.ProgramId == programId && v.Status == FormVersionStatus.Published, ct);
        live?.Questions.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        return live;
    }

    /// <summary>Checks a draft's structure. Keys are field paths such as <c>questions.2.options</c>.</summary>
    public static Dictionary<string, string[]> ValidateDraft(FormDraftInput input)
    {
        var errors = new Dictionary<string, string[]>();
        var questions = input.Questions ?? [];
        if ((input.ChangeNote ?? "").Trim().Length > 300) errors["changeNote"] = ["Change notes are limited to 300 characters."];
        if (questions.Count == 0) errors["questions"] = ["Add at least one question."];
        if (questions.Count > MaxQuestions) errors["questions"] = [$"A form can have up to {MaxQuestions} questions."];

        var seen = new Dictionary<string, (int Index, FormQuestionInput Q)>(StringComparer.Ordinal);
        for (var i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            var at = $"questions.{i}";
            var label = (q.Label ?? "").Trim();
            var key = (q.Key ?? "").Trim();
            if (label.Length == 0) errors[$"{at}.label"] = ["Write the question families will read."];
            else if (label.Length > 200) errors[$"{at}.label"] = ["Questions are limited to 200 characters."];
            if ((q.HelpText ?? "").Trim().Length > 300) errors[$"{at}.helpText"] = ["Help text is limited to 300 characters."];
            if (!KeyPattern().IsMatch(key)) errors[$"{at}.key"] = ["Use a short key that starts with a lowercase letter: letters, numbers and underscores only."];
            else if (seen.ContainsKey(key)) errors[$"{at}.key"] = [$"Another question already uses the key \"{key}\"."];

            if (IsChoice(q.Type))
            {
                var options = (q.Options ?? []).Select(o => (o ?? "").Trim()).ToList();
                if (options.Count < 2) errors[$"{at}.options"] = ["Add at least two choices."];
                else if (options.Any(o => o.Length == 0)) errors[$"{at}.options"] = ["Fill in or remove the empty choice."];
                else if (options.Any(o => o.Contains('|', StringComparison.Ordinal))) errors[$"{at}.options"] = ["Choices can't contain the | character."];
                else if (options.Any(o => o.Length > 100)) errors[$"{at}.options"] = ["Choices are limited to 100 characters."];
                else if (options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Count) errors[$"{at}.options"] = ["Each choice can appear only once."];
                else if (options.Count > 30) errors[$"{at}.options"] = ["A question can have up to 30 choices."];
            }

            if (!string.IsNullOrWhiteSpace(q.ShowWhenKey))
            {
                if (!seen.TryGetValue(q.ShowWhenKey.Trim(), out var source))
                    errors[$"{at}.showWhenKey"] = ["A follow-up can only depend on a question above it."];
                else if (!CanDriveCondition(source.Q.Type))
                    errors[$"{at}.showWhenKey"] = ["Only yes/no and choice questions can reveal a follow-up."];
                else if (q.Scope == QuestionScope.Household && source.Q.Scope == QuestionScope.Participant)
                    errors[$"{at}.showWhenKey"] = ["A household question can't depend on a camper question, because it's asked once for everyone."];
                else if (!ConditionValues(source.Q.Type, source.Q.Options).Contains((q.ShowWhenValue ?? "").Trim(), StringComparer.Ordinal))
                    errors[$"{at}.showWhenValue"] = [$"Pick one of the answers to \"{(source.Q.Label ?? "").Trim()}\"."];
            }
            if (key.Length > 0 && !seen.ContainsKey(key)) seen[key] = (i, q);
        }
        return errors;
    }

    static IEnumerable<string> ConditionValues(FormQuestionType t, List<string>? options) =>
        t == FormQuestionType.YesNo ? ["Yes", "No"] : (options ?? []).Select(o => (o ?? "").Trim());

    /// <summary>Builds question rows from a draft that passed <see cref="ValidateDraft"/>.</summary>
    public static List<FormQuestion> ToQuestions(FormDraftInput input) =>
        (input.Questions ?? []).Select((q, i) => new FormQuestion
        {
            Key = (q.Key ?? "").Trim(),
            Label = (q.Label ?? "").Trim(),
            HelpText = string.IsNullOrWhiteSpace(q.HelpText) ? null : q.HelpText.Trim(),
            Type = q.Type,
            Scope = q.Scope,
            Required = q.Required,
            Options = IsChoice(q.Type) ? string.Join('|', (q.Options ?? []).Select(o => o.Trim())) : null,
            ShowWhenKey = string.IsNullOrWhiteSpace(q.ShowWhenKey) ? null : q.ShowWhenKey.Trim(),
            ShowWhenValue = string.IsNullOrWhiteSpace(q.ShowWhenKey) ? null : (q.ShowWhenValue ?? "").Trim(),
            SortOrder = i + 1,
            Health = q.Health,
        }).ToList();

    /// <summary>
    /// Validates one set of answers against the questions of one scope. Questions are read in order;
    /// a hidden question's answer is dropped, so it can't reveal anything below it. <paramref name="known"/>
    /// holds answers already accepted in an outer scope (household answers, when checking a camper).
    /// </summary>
    public static (List<CleanAnswer> Answers, List<string> Errors) Check(
        IEnumerable<FormQuestion> questions, IReadOnlyDictionary<string, string>? given, IReadOnlyDictionary<string, string>? known = null)
    {
        var accepted = new Dictionary<string, string>(known ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        var answers = new List<CleanAnswer>();
        var errors = new List<string>();
        foreach (var q in questions.OrderBy(q => q.SortOrder))
        {
            if (!IsVisible(q, accepted)) continue;
            var raw = (given?.GetValueOrDefault(q.Key) ?? "").Trim();
            if (raw.Length == 0)
            {
                if (q.Required) errors.Add($"{q.Label} is required.");
                continue;
            }
            var (value, error) = Normalize(q, raw);
            if (error is not null) { errors.Add(error); continue; }
            accepted[q.Key] = value!;
            answers.Add(new CleanAnswer(q, value!));
        }
        return (answers, errors);
    }

    /// <summary>Shown when it has no condition, or when the answer it depends on matches.</summary>
    public static bool IsVisible(FormQuestion q, IReadOnlyDictionary<string, string> answers)
    {
        if (q.ShowWhenKey is null) return true;
        if (!answers.TryGetValue(q.ShowWhenKey, out var v)) return false;
        return v == q.ShowWhenValue || v.Split('|').Contains(q.ShowWhenValue, StringComparer.Ordinal);
    }

    static (string? Value, string? Error) Normalize(FormQuestion q, string raw)
    {
        switch (q.Type)
        {
            case FormQuestionType.ShortText:
                return raw.Length > MaxShortText ? (null, $"{q.Label} is limited to {MaxShortText} characters.") : (raw, null);
            case FormQuestionType.LongText:
                return raw.Length > MaxLongText ? (null, $"{q.Label} is limited to {MaxLongText:N0} characters.") : (raw, null);
            case FormQuestionType.YesNo:
                if (raw.Equals("Yes", StringComparison.OrdinalIgnoreCase)) return ("Yes", null);
                if (raw.Equals("No", StringComparison.OrdinalIgnoreCase)) return ("No", null);
                return (null, $"Answer yes or no for {q.Label}.");
            case FormQuestionType.SingleChoice:
                // Matched without regard to case, stored as the form spells it.
                var option = q.OptionList.FirstOrDefault(o => o.Equals(raw, StringComparison.OrdinalIgnoreCase));
                return option is not null ? (option, null) : (null, $"Choose one of the options for {q.Label}.");
            case FormQuestionType.MultipleChoice:
                var picked = raw.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (picked.Any(p => !q.OptionList.Contains(p, StringComparer.OrdinalIgnoreCase))) return (null, $"Choose from the listed options for {q.Label}.");
                // Stored in the form's own order and spelling so the same picks always read the same way.
                return (string.Join('|', q.OptionList.Where(o => picked.Contains(o, StringComparer.OrdinalIgnoreCase))), null);
            case FormQuestionType.Date:
                return DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? (d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null)
                    : (null, $"Enter a date for {q.Label}.");
            case FormQuestionType.Number:
                return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) && Math.Abs(n) < 1_000_000_000m
                    ? (n.ToString(CultureInfo.InvariantCulture), null)
                    : (null, $"Enter a number for {q.Label}.");
            default:
                return (null, $"{q.Label} can't be answered.");
        }
    }

    /// <summary>Plain-language summary of a question for audit rows and lists ("Yes/no · each camper · required").</summary>
    public static string Describe(FormQuestion q) =>
        $"{TypeLabel(q.Type)} · {(q.Scope == QuestionScope.Household ? "once per household" : "each camper")}{(q.Required ? " · required" : "")}";

    public static string TypeLabel(FormQuestionType t) => t switch
    {
        FormQuestionType.ShortText => "Short answer",
        FormQuestionType.LongText => "Long answer",
        FormQuestionType.YesNo => "Yes/no",
        FormQuestionType.SingleChoice => "Single choice",
        FormQuestionType.MultipleChoice => "Multiple choice",
        FormQuestionType.Date => "Date",
        FormQuestionType.Number => "Number",
        _ => t.ToString(),
    };
}
