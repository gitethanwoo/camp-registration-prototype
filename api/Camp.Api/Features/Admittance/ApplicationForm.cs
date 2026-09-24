namespace Camp.Api.Features.Admittance;

/// <summary>One question on the application. Type is LongText, Text, Select, or YesNo.</summary>
public sealed record FormQuestion(
    string Key,
    string Section,
    string Label,
    string? Help,
    string Type,
    bool Required,
    string[] Options,
    int MaxLength,
    string? ShowWhenKey = null,
    string? ShowWhenValue = null);

public sealed record FormSection(string Key, string Title, string Description);

/// <summary>
/// The WSM retreat application (R2). Configured in code for the prototype; the admin forms
/// builder (K-screens) would own this in production. Validation runs on the server at submit.
/// </summary>
public static class ApplicationForm
{
    public static readonly FormSection[] Sections =
    [
        new("couple", "Couple details", "Who's coming. We use what's already on your family account."),
        new("marriage", "Your marriage", "Your answers help our team get to know you before the weekend."),
        new("questions", "A few questions", "So we can plan the weekend around you."),
    ];

    public static readonly FormQuestion[] Questions =
    [
        new("yearsMarried", "couple", "How long have you been married?", null, "Select", true,
            ["Less than 1 year", "1–5 years", "6–15 years", "16–25 years", "More than 25 years"], 40),
        new("why", "marriage", "Why do you want to attend the Fall Marriage Retreat?",
            "Share what drew you to this retreat and what you hope to experience.", "LongText", true, [], 750),
        new("goals", "marriage", "What are your goals for this retreat?",
            "Share what you hope to grow in as a couple.", "LongText", true, [], 750),
        new("heardFrom", "questions", "How did you hear about the retreat?", null, "Select", true,
            ["A friend or family member", "Our church", "A past WinShape event", "Social media", "Other"], 60),
        new("attendedBefore", "questions", "Have you attended a WinShape Marriage event before?", null, "YesNo", true, [], 3),
        new("attendedWhich", "questions", "Which event, and about when?", "For example, Spring Retreat 2026.", "Text", true, [], 120,
            ShowWhenKey: "attendedBefore", ShowWhenValue: "Yes"),
        new("needs", "questions", "Dietary or accessibility needs for either of you", "Leave blank if none.", "Text", false, [], 300),
        new("anythingElse", "questions", "Anything else our team should know?", null, "LongText", false, [], 500),
    ];

    public static bool Visible(FormQuestion q, IReadOnlyDictionary<string, string> answers) =>
        q.ShowWhenKey is null || (answers.TryGetValue(q.ShowWhenKey, out var v) && v == q.ShowWhenValue);

    /// <summary>Keeps only known, visible questions, trimmed and capped to each question's length.</summary>
    public static Dictionary<string, string> Clean(IReadOnlyDictionary<string, string>? answers)
    {
        var input = answers ?? new Dictionary<string, string>();
        var cleaned = new Dictionary<string, string>();
        foreach (var q in Questions)
        {
            if (!input.TryGetValue(q.Key, out var raw) || string.IsNullOrWhiteSpace(raw)) continue;
            var value = raw.Trim();
            if (q.Options.Length > 0 && !q.Options.Contains(value)) continue;
            if (q.Type == "YesNo" && value is not ("Yes" or "No")) continue;
            cleaned[q.Key] = value.Length > q.MaxLength ? value[..q.MaxLength] : value;
        }
        foreach (var q in Questions.Where(q => !Visible(q, cleaned))) cleaned.Remove(q.Key);
        return cleaned;
    }

    /// <summary>Field errors keyed like the web form: <c>answers.why</c>, <c>spouse</c>.</summary>
    public static Dictionary<string, string[]> Validate(IReadOnlyDictionary<string, string> answers, string spouseFirst, string spouseLast)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(spouseFirst) || string.IsNullOrWhiteSpace(spouseLast))
            errors["spouse"] = ["Add your spouse's first and last name."];
        foreach (var q in Questions.Where(q => q.Required && Visible(q, answers)))
            if (!answers.ContainsKey(q.Key)) errors[$"answers.{q.Key}"] = [$"Answer \"{q.Label}\"."];
        return errors;
    }
}
