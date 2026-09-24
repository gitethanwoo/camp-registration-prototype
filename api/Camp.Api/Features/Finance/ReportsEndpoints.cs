using System.Text;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Finance;

/// <summary>
/// FN1 · Reports (FR-72). Registrations, settled revenue, attendance and demographics by ministry,
/// program, session and date range. Settled revenue is the governed (certified) measure: it is read
/// from the same Fiserv settlement lines FN2 shows, so it ties to reconciliation to the cent.
/// </summary>
public sealed class ReportsEndpoints : IEndpointModule
{
    public const int MaxRangeDays = 731;

    public void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin/finance/reports").RequireAuthorization(Policies.Finance);

        g.MapGet("", async (CampDbContext db, int? ministryId, int? programId, int? sessionId, DateOnly? from, DateOnly? to, CancellationToken ct) =>
        {
            var scope = Scope.From(ministryId, programId, sessionId, from, to);
            if (scope.Error is { } error) return Fin.Invalid("to", error);
            return Results.Ok(await Build(db, scope, ct));
        });

        g.MapGet("/export", async (CampDbContext db, IAuditLog audit, int? ministryId, int? programId, int? sessionId, DateOnly? from, DateOnly? to, CancellationToken ct) =>
        {
            var scope = Scope.From(ministryId, programId, sessionId, from, to);
            if (scope.Error is { } error) return Fin.Invalid("to", error);
            var report = await Build(db, scope, ct);
            var csv = new StringBuilder("Program,Session,Registrations,Attended,Settled revenue,Contracted tuition\n");
            foreach (var r in report.Rows)
                csv.Append(CultureInfo.InvariantCulture, $"{Csv(r.Program)},{Csv(r.Session)},{r.Registrations},{r.Attended},{Dollars(r.SettledRevenueCents)},{Dollars(r.ContractedCents)}\n");
            csv.Append(CultureInfo.InvariantCulture, $"Total,,{report.Registrations},{report.Attendance.Attended},{Dollars(report.SettledRevenue.AmountCents)},{Dollars(report.Rows.Sum(r => r.ContractedCents))}\n");
            audit.Record("report.exported", "Report", "FN1", $"Exported the report for {Fin.Day(scope.Start)} – {Fin.Day(scope.To)} ({report.ScopeLabel}).");
            await db.SaveChangesAsync(ct);
            return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"winshape-report-{scope.Start:yyyy-MM-dd}-to-{scope.To:yyyy-MM-dd}.csv");
        });
    }

    static string Csv(string s) => s.Contains(',', StringComparison.Ordinal) || s.Contains('"', StringComparison.Ordinal) ? $"\"{s.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : s;

    static string Dollars(int cents) => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    sealed record Scope(int? MinistryId, int? ProgramId, int? SessionId, DateOnly Start, DateOnly To, string? Error)
    {
        public bool Filtered => MinistryId is not null || ProgramId is not null || SessionId is not null;

        public static Scope From(int? ministryId, int? programId, int? sessionId, DateOnly? from, DateOnly? to)
        {
            var end = to ?? Fin.Today;
            var start = from ?? end.AddDays(-89);
            var error = start > end ? "The start date is after the end date."
                : end.DayNumber - start.DayNumber > MaxRangeDays ? "Choose a range of two years or less." : null;
            return new Scope(ministryId, programId, sessionId, start, end, error);
        }

        public bool Includes(int? ministry, int? program, int? session) =>
            (MinistryId is null || MinistryId == ministry) && (ProgramId is null || ProgramId == program) && (SessionId is null || SessionId == session);
    }

    public sealed record ReportRow(int? ProgramId, int? SessionId, string Program, string Session, int Registrations, int Attended, int SettledRevenueCents, int ContractedCents);

    public sealed record SettledRevenue(int AmountCents, int BatchGrossCents, int UnattributedCents, int Batches, bool Certified);

    public sealed record Attendance(int Attended, int Registered, int Sessions);

    public sealed record Report(
        DateOnly From, DateOnly To, string ScopeLabel, object Options, int Registrations, SettledRevenue SettledRevenue,
        Attendance Attendance, object Demographics, object RegistrationsByPeriod, List<object> RevenueByProgram, List<ReportRow> Rows);

    static async Task<object> Options(CampDbContext db, CancellationToken ct) => new
    {
        Ministries = await db.Ministries.AsNoTracking().OrderBy(m => m.Id).Select(m => new { m.Id, m.Code, m.Name }).ToListAsync(ct),
        Programs = await db.Programs.AsNoTracking().Where(p => p.Sessions.Any()).OrderBy(p => p.Name).Select(p => new { p.Id, p.MinistryId, p.Name }).ToListAsync(ct),
        Sessions = await db.Sessions.AsNoTracking().OrderBy(s => s.StartDate).Select(s => new { s.Id, s.ProgramId, s.Name, s.StartDate, s.EndDate }).ToListAsync(ct),
    };

    static async Task<Report> Build(CampDbContext db, Scope scope, CancellationToken ct)
    {
        var fromTime = scope.Start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toTime = scope.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var today = Fin.Today;

        // Settlement lines in batches settled within the range: the certified revenue measure.
        var (firstDay, lastDay) = (scope.Start, scope.To);
        var lines = await Ledger.Lines(db, l => l.Batch.SettledOn >= firstDay && l.Batch.SettledOn <= lastDay && l.Kind != SettlementLineKind.Fee, ct);
        var batchGross = lines.Sum(l => l.AmountCents);
        var inScope = lines.Where(l => l.ProgramId is not null && scope.Includes(l.MinistryId, l.ProgramId, l.SessionId)).ToList();
        var unattributed = lines.Where(l => l.ProgramId is null).Sum(l => l.AmountCents);
        var revenue = scope.Filtered ? inScope.Sum(l => l.AmountCents) : batchGross;

        var regs = await db.Registrations.AsNoTracking()
            .Select(r => new
            {
                r.Id,
                r.Status,
                r.CreatedAt,
                r.SessionId,
                r.Session.ProgramId,
                r.Session.Program.MinistryId,
                Program = r.Session.Program.Name,
                Session = r.Session.Name,
                r.Session.EndDate,
                r.Grade,
                r.Person.Gender,
                Contracted = r.PriceCents - r.DiscountCents,
            })
            .ToListAsync(ct);
        var scoped = regs.Where(r => scope.Includes(r.MinistryId, r.ProgramId, r.SessionId)).ToList();
        var registered = scoped.Where(r => r.Status == RegistrationStatus.Confirmed && r.CreatedAt >= fromTime && r.CreatedAt < toTime).ToList();

        // Attendance: sessions that ended in the range. Confirmed campers attended; cancelled and transferred ones didn't.
        var ended = scoped.Where(r => r.EndDate >= scope.Start && r.EndDate <= scope.To && r.EndDate < today
            && r.Status is RegistrationStatus.Confirmed or RegistrationStatus.Cancelled or RegistrationStatus.Transferred).ToList();

        var male = registered.Count(r => r.Gender == Gender.Male);
        var bands = new (string Label, int Min, int Max)[] { ("K–2", 0, 2), ("Grades 3–5", 3, 5), ("Grades 6–8", 6, 8), ("Grades 9–12", 9, 12), ("Adults", 13, 99) };
        var demographics = new
        {
            Total = registered.Count,
            Gender = new[] { new { Label = "Male", Count = male }, new { Label = "Female", Count = registered.Count - male } },
            Grades = bands.Select(b => new { b.Label, Count = registered.Count(r => r.Grade >= b.Min && r.Grade <= b.Max) }).Where(b => b.Count > 0),
        };

        var monthly = scope.To.DayNumber - scope.Start.DayNumber > 120;
        var periods = new List<object>();
        for (var start = monthly ? new DateOnly(scope.Start.Year, scope.Start.Month, 1) : scope.Start; start <= scope.To;)
        {
            var next = monthly ? start.AddMonths(1) : start.AddDays(7);
            var last = next.AddDays(-1) > scope.To ? scope.To : next.AddDays(-1);
            var (s, e) = (start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), next.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            var periodLabel = monthly ? start.ToString("MMM yyyy", CultureInfo.InvariantCulture)
                : start.Month == last.Month ? $"{start.ToString("MMM d", CultureInfo.InvariantCulture)}–{last.Day}" : $"{start.ToString("MMM d", CultureInfo.InvariantCulture)}–{last.ToString("MMM d", CultureInfo.InvariantCulture)}";
            periods.Add(new { Label = periodLabel, Start = start < scope.Start ? scope.Start : start, End = last, Count = registered.Count(r => r.CreatedAt >= s && r.CreatedAt < e) });
            start = next;
        }

        var revenueByProgram = inScope.GroupBy(l => l.Program!).Select(g => new { Program = g.Key, AmountCents = g.Sum(l => l.AmountCents) })
            .OrderByDescending(x => x.AmountCents).ToList<object>();
        if (!scope.Filtered && unattributed != 0) revenueByProgram.Add(new { Program = "Unattributed", AmountCents = unattributed });

        var keys = registered.Select(r => (r.ProgramId, r.SessionId, r.Program, r.Session))
            .Concat(inScope.Select(l => (ProgramId: l.ProgramId!.Value, SessionId: l.SessionId!.Value, Program: l.Program!, Session: l.Session!)))
            .Concat(ended.Select(r => (r.ProgramId, r.SessionId, r.Program, r.Session)))
            .Distinct().OrderBy(k => k.Program, StringComparer.Ordinal).ThenBy(k => k.Session, StringComparer.Ordinal);
        var rows = keys.Select(k => new ReportRow(k.ProgramId, k.SessionId, k.Program, k.Session,
            registered.Count(r => r.SessionId == k.SessionId),
            ended.Count(r => r.SessionId == k.SessionId && r.Status == RegistrationStatus.Confirmed),
            inScope.Where(l => l.SessionId == k.SessionId).Sum(l => l.AmountCents),
            registered.Where(r => r.SessionId == k.SessionId).Sum(r => r.Contracted))).ToList();
        if (!scope.Filtered && unattributed != 0) rows.Add(new ReportRow(null, null, "Unattributed", "Not tied to a registration", 0, 0, unattributed, 0));

        var label = await ScopeLabel(db, scope, ct);
        var batches = await db.Set<SettlementBatch>().CountAsync(b => b.SettledOn >= firstDay && b.SettledOn <= lastDay, ct);
        return new Report(scope.Start, scope.To, label, await Options(db, ct), registered.Count,
            new SettledRevenue(revenue, batchGross, unattributed, batches, Certified: true),
            new Attendance(ended.Count(r => r.Status == RegistrationStatus.Confirmed), ended.Count, ended.Select(r => r.SessionId).Distinct().Count()),
            demographics, new { Unit = monthly ? "month" : "week", Periods = periods }, revenueByProgram, rows);
    }

    static async Task<string> ScopeLabel(CampDbContext db, Scope scope, CancellationToken ct)
    {
        if (scope.SessionId is { } s)
            return await db.Sessions.Where(x => x.Id == s).Select(x => x.Program.Name + " · " + x.Name).FirstOrDefaultAsync(ct) ?? "Unknown session";
        if (scope.ProgramId is { } p)
            return await db.Programs.Where(x => x.Id == p).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "Unknown program";
        if (scope.MinistryId is { } m)
            return await db.Ministries.Where(x => x.Id == m).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "Unknown ministry";
        return "All ministries";
    }
}
