using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Forms;

/// <summary>
/// K6 demo rows. Day Camp · Atlanta and Overnight Camp get a live v1 that asks their existing
/// questions plus the medication question and its follow-up. This slice's own program, Day Camp ·
/// Rome ("Rome Day Camp"), has a live v1 and a v2 that Jamie Dalton sent for approval, so the demo admin can approve it
/// and then register against it. Only this slice's program is registered into by its e2e spec.
/// </summary>
public sealed class FormsSeed : ISeedModule
{
    public const string RomeSlug = "day-camp-rome";
    public const string Jamie = "Jamie Dalton (ADMIN)";
    public const string JamieEmail = "jamie.dalton@winshape.example";
    const string Brian = "Brian Hughes (Operations)";

    static readonly DateTime Drafted = new(2027, 8, 3, 15, 0, 0, DateTimeKind.Utc);
    static readonly DateTime Approved = new(2027, 8, 5, 16, 30, 0, DateTimeKind.Utc);
    static readonly DateTime Submitted = new(2028, 2, 28, 14, 10, 0, DateTimeKind.Utc);

    public int Order => 150;

    public async Task RunAsync(CampDbContext db, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        foreach (var slug in new[] { "day-camp-atlanta", "overnight-camp" })
        {
            var program = await db.Programs.AsNoTracking().Include(p => p.Questions).FirstOrDefaultAsync(p => p.Slug == slug, ct);
            if (program is null || await db.Set<FormVersion>().AnyAsync(v => v.ProgramId == program.Id, ct)) continue;
            var questions = program.Questions.OrderBy(q => q.SortOrder).Select(FormViews.FromLegacy).ToList();
            // The medication pair goes after the camper questions, before the household ones. The
            // yes/no is optional here so registrations built for the original questions stay valid;
            // a "Yes" still requires the follow-up.
            var at = questions.FindLastIndex(q => q.Scope == QuestionScope.Participant) + 1;
            questions.InsertRange(at, MedicationPair(required: false));
            db.Add(Live(program.Id, questions));
        }
        await SeedRome(db, ct);
        await db.SaveChangesAsync(ct);
    }

    static async Task SeedRome(CampDbContext db, CancellationToken ct)
    {
        var rome = await db.Programs.FirstOrDefaultAsync(p => p.Slug == RomeSlug, ct);
        if (rome is null)
        {
            var wsc = await db.Ministries.FirstAsync(m => m.Code == "WSC", ct);
            rome = new CampProgram
            {
                MinistryId = wsc.Id,
                Slug = RomeSlug,
                Name = "Rome Day Camp",
                Tagline = "A week of day camp for rising 1st–6th graders in Rome.",
                Description = "WinShape Day Camp spends a week on the Berry College campus. Campers spend 9 AM–4 PM in grade-based groups with games, Bible time, a pool day, and crafts, and families come back Thursday evening for Family Night.",
                Type = ProgramType.Standard,
                HealthMechanism = HealthMechanism.Embedded,
                Location = "Berry College · Rome, GA",
                ImageUrl = "/images/daycamp.jpg",
                IsPublished = true,
            };
            var week = new Session
            {
                Program = rome,
                Name = "July week",
                StartDate = new(2028, 7, 17),
                EndDate = new(2028, 7, 21),
                PriceCents = 32500,
                DepositCents = 10000,
                PlanInstallments = 3,
                BalanceDueDate = new(2028, 6, 1),
            };
            week.Pools.AddRange(Enumerable.Range(1, 6).Select(g => new CapacityPool { Name = $"Grade {g}", GradeMin = g, GradeMax = g, Capacity = 20, SortOrder = g }));
            rome.Sessions.Add(week);
            rome.Waivers.Add(new WaiverTemplate
            {
                Title = "Participant Release and Waiver of Liability",
                Version = 1,
                EffectiveDate = new(2027, 8, 1),
                PerParticipant = true,
                Body = "I give permission for my child to take part in WinShape Rome Day Camp, including games, swimming at the Berry College pool, crafts, and walks on campus. I understand these activities carry some risk of injury.\n\nIn an emergency, I authorize camp staff to seek medical care for my child if I can't be reached right away.\n\nI release WinShape Foundation, Berry College, and their staff and volunteers from claims arising from ordinary participation, except for gross negligence.",
            });
            db.Programs.Add(rome);
            await db.SaveChangesAsync(ct);
        }
        if (await db.Set<FormVersion>().AnyAsync(v => v.ProgramId == rome.Id, ct)) return;

        List<FormQuestion> V1() =>
        [
            Q("tshirt", "T-shirt size", FormQuestionType.SingleChoice, QuestionScope.Participant, true, "Youth S|Youth M|Youth L|Adult S|Adult M"),
            .. MedicationPair(required: true),
            Q("church", "Does your family attend a church?", FormQuestionType.YesNo, QuestionScope.Household, true),
            Q("churchName", "Church name", FormQuestionType.ShortText, QuestionScope.Household, true, showWhen: ("church", "Yes")),
        ];
        db.Add(Live(rome.Id, Ordered(V1())));

        var v2 = V1();
        v2.Insert(1, Q("swim", "Swimming ability", FormQuestionType.SingleChoice, QuestionScope.Participant, true, "Non-swimmer|Beginner|Confident swimmer",
            help: "Wednesday is pool day at the Berry College pool. We group campers by swimming ability."));
        v2.Insert(4, Q("activities", "Which afternoon activities interest your camper?", FormQuestionType.MultipleChoice, QuestionScope.Participant, false, "Crafts|Sports|Music|Nature walk",
            help: "Pick any. We use this to plan groups, not to promise a spot."));
        v2.Add(Q("familyNight", "How many adults will come to Family Night on Thursday?", FormQuestionType.Number, QuestionScope.Household, false,
            help: "Dinner is on us. We plan food from this number."));
        db.Add(new FormVersion
        {
            ProgramId = rome.Id,
            Version = 2,
            Status = FormVersionStatus.PendingApproval,
            ChangeNote = "Adds swimming ability for pool day, afternoon activity interests, and a Family Night head count.",
            CreatedBy = Jamie,
            CreatedByEmail = JamieEmail,
            CreatedAt = Submitted.AddHours(-2),
            UpdatedAt = Submitted.AddMinutes(-5),
            SubmittedBy = Jamie,
            SubmittedByEmail = JamieEmail,
            SubmittedAt = Submitted,
            Questions = Ordered(v2),
        });
    }

    static FormVersion Live(int programId, List<FormQuestion> questions) => new()
    {
        ProgramId = programId,
        Version = 1,
        Status = FormVersionStatus.Published,
        ChangeNote = "First form in the new platform. Same questions as WIN, plus medication at camp.",
        CreatedBy = Brian,
        CreatedAt = Drafted,
        UpdatedAt = Drafted,
        SubmittedBy = Brian,
        SubmittedAt = Drafted,
        ApprovedBy = Jamie,
        ApprovedAt = Approved,
        PublishedAt = Approved,
        Questions = Ordered(questions),
    };

    static List<FormQuestion> MedicationPair(bool required) =>
    [
        Q("medication", "Does your camper need medication at camp?", FormQuestionType.YesNo, QuestionScope.Participant, required, health: true),
        Q("medicationDetails", "Medication name and schedule", FormQuestionType.LongText, QuestionScope.Participant, true, showWhen: ("medication", "Yes"),
            help: "Name, dose, and times. Send it in the original container; the camp nurse gives it.", health: true),
    ];

    static List<FormQuestion> Ordered(List<FormQuestion> questions)
    {
        for (var i = 0; i < questions.Count; i++) questions[i].SortOrder = i + 1;
        return questions;
    }

    static FormQuestion Q(string key, string label, FormQuestionType type, QuestionScope scope, bool required, string? options = null,
        (string Key, string Value)? showWhen = null, string? help = null, bool health = false) => new()
        {
            Key = key,
            Label = label,
            HelpText = help,
            Type = type,
            Scope = scope,
            Required = required,
            Options = options,
            ShowWhenKey = showWhen?.Key,
            ShowWhenValue = showWhen?.Value,
            Health = health,
        };
}
