using System.Text;
using System.Text.RegularExpressions;

namespace Camp.Api.Features.Host;

/// <summary>One volunteer as typed, before validation.</summary>
public sealed record VolunteerFields(string FirstName, string LastName, string Email, string Phone, string DateOfBirth, string Role);

/// <summary>The first problem with a row, or none. <see cref="Field"/> is where the fix goes.</summary>
public sealed record RowProblem(string Issue, string Detail, string Field);

/// <summary>Reading the H2 volunteer CSV and checking each row (FR-88).</summary>
public static partial class VolunteerCsv
{
    public const int MaxRows = 2000;
    public const int MaxChars = 1_000_000;
    public const string DefaultRole = "Group leader";
    public static readonly string[] Roles = ["Group leader", "Check-in", "Kitchen", "Games", "Crafts", "First aid"];
    public static readonly string[] Headers = ["first_name", "last_name", "email", "phone", "date_of_birth", "role"];

    public const string Template =
        "first_name,last_name,email,phone,date_of_birth,role\r\n" +
        "Jane,Example,jane.example@example.org,(404) 555-0100,04/18/1985,Group leader\r\n";

    static readonly string[] DateFormats = ["M/d/yyyy", "MM/dd/yyyy", "yyyy-MM-dd"];

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    /// <summary>
    /// Parses the file into volunteer rows. Blank lines are not rows. Returns an error message
    /// instead when the file can't be read as a volunteer list at all.
    /// </summary>
    public static (List<VolunteerFields> Rows, string? Error) Parse(string content)
    {
        if (content.Length > MaxChars) return ([], "That file is over 1 MB. Split it into smaller files.");
        var records = ReadRecords(content.TrimStart('\uFEFF'));
        if (records.Count == 0) return ([], "That file is empty. Download the template and add one volunteer per row.");

        var header = records[0].Select(Normalize).ToList();
        int Col(params string[] names) => header.FindIndex(names.Contains);
        var first = Col("firstname");
        var last = Col("lastname");
        var email = Col("email", "emailaddress");
        var phone = Col("phone", "phonenumber");
        var dob = Col("dateofbirth", "dob", "birthdate");
        var role = Col("role");
        var missing = new[] { ("first_name", first), ("last_name", last), ("email", email), ("date_of_birth", dob) }
            .Where(c => c.Item2 < 0).Select(c => c.Item1).ToList();
        if (missing.Count > 0)
            return ([], $"The first row must be the column names from the template. Missing: {string.Join(", ", missing)}.");

        var rows = records.Skip(1)
            .Where(r => r.Any(cell => cell.Trim().Length > 0))
            .Select(r =>
            {
                string At(int i) => Clamp(i >= 0 && i < r.Count ? r[i] : "");
                return new VolunteerFields(At(first), At(last), At(email), At(phone), At(dob), At(role));
            })
            .ToList();
        if (rows.Count == 0) return ([], "That file has column names but no volunteers. Add one volunteer per row.");
        if (rows.Count > MaxRows) return ([], $"That file has {rows.Count} volunteers. Upload at most {MaxRows} at a time.");
        return (rows, null);
    }

    /// <summary>
    /// Checks one row. <paramref name="takenEmails"/> is the organization's volunteer list;
    /// <paramref name="earlierRows"/> maps emails used by earlier rows in the same file to their row number.
    /// </summary>
    public static RowProblem? Validate(VolunteerFields f, ISet<string> takenEmails, IReadOnlyDictionary<string, int> earlierRows, DateOnly eventStart)
    {
        if (f.FirstName.Length == 0) return new("Missing first name", "First and last name are required.", "firstName");
        if (f.LastName.Length == 0) return new("Missing last name", "First and last name are required.", "lastName");
        if (f.FirstName.Length > 100 || f.LastName.Length > 100) return new("Name too long", "Names are limited to 100 characters.", "firstName");
        if (f.Email.Length == 0) return new("Missing email", "Email is required for all volunteers.", "email");
        if (f.Email.Length > 200 || !EmailPattern().IsMatch(f.Email))
            return new("Invalid email", $"“{f.Email}” isn't an email address. Check it for typos.", "email");
        var key = EmailKey(f.Email);
        if (takenEmails.Contains(key)) return new("Duplicate email", "This email already exists in your volunteer list.", "email");
        if (earlierRows.TryGetValue(key, out var row))
            return new("Duplicate email", $"Same email as row {row}. Each volunteer needs their own email.", "email");
        if (f.Phone.Length > 40) return new("Invalid phone", "Phone numbers are limited to 40 characters.", "phone");
        if (f.DateOfBirth.Length == 0) return new("Missing date of birth", "Date of birth is required for the background check.", "dateOfBirth");
        if (ParseDate(f.DateOfBirth) is not { } born)
            return new("Invalid date of birth", $"Date of birth must be a real date written MM/DD/YYYY. “{f.DateOfBirth}” isn't.", "dateOfBirth");
        if (born.AddYears(18) > eventStart)
            return new("Under 18", $"Volunteers must be 18 or older on {eventStart.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)}.", "dateOfBirth");
        if (f.Role.Length > 0 && RoleName(f.Role) is null)
            return new("Unknown role", $"Role must be one of: {string.Join(", ", Roles)}.", "role");
        return null;
    }

    /// <summary>Trims a cell and cuts it to what the database stores; validation then reports anything too long.</summary>
    public static string Clamp(string? value) => (value ?? "").Trim() is { Length: > 300 } v ? v[..300] : (value ?? "").Trim();

    public static string EmailKey(string email) => email.Trim().ToLowerInvariant();

    public static DateOnly? ParseDate(string value) =>
        DateOnly.TryParseExact(value.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d.Year >= 1900
            ? d : null;

    /// <summary>The canonical role name for what was typed (case-insensitive); blank means the default.</summary>
    public static string? RoleName(string typed) =>
        typed.Trim().Length == 0 ? DefaultRole : Roles.FirstOrDefault(r => string.Equals(r, typed.Trim(), StringComparison.OrdinalIgnoreCase));

    static string Normalize(string h) => new(h.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>RFC 4180: commas, double-quoted fields, doubled quotes, CRLF or LF line ends.</summary>
    static List<List<string>> ReadRecords(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"') { cell.Append(c); continue; }
                if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; continue; }
                quoted = false;
                continue;
            }
            switch (c)
            {
                case '"' when cell.Length == 0:
                    quoted = true;
                    break;
                case ',':
                    record.Add(cell.ToString());
                    cell.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    record.Add(cell.ToString());
                    cell.Clear();
                    records.Add(record);
                    record = [];
                    break;
                default:
                    cell.Append(c);
                    break;
            }
        }
        if (cell.Length > 0 || record.Count > 0)
        {
            record.Add(cell.ToString());
            records.Add(record);
        }
        return records;
    }
}
