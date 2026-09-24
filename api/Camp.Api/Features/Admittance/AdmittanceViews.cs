using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Admittance;

/// <summary>Read models for the family (R2, F7) and staff (C6) screens.</summary>
public static class AdmittanceViews
{
    /// <summary>
    /// Payment wording the screens show: Authorized (not charged), Expiring, Expired, Paid,
    /// Voided, CardNeeded (approved, waiting on a new card), or None (a draft).
    /// </summary>
    public static string PaymentState(AdmittanceApplication a, DateTime now) => a.Hold switch
    {
        HoldStatus.Captured => "Paid",
        HoldStatus.Voided => "Voided",
        HoldStatus.Authorized when a.AuthorizationExpiresAt <= now => "Expired",
        HoldStatus.Authorized when a.AuthorizationExpiresAt - now <= AdmittanceService.ExpiringWithin => "Expiring",
        HoldStatus.Authorized => "Authorized",
        _ => a.Stage == ApplicationStage.Approved ? "CardNeeded" : "None",
    };

    /// <summary>The registration-level status from the global vocabulary.</summary>
    public static string Status(AdmittanceApplication a) => a.Stage switch
    {
        ApplicationStage.Draft => "Draft",
        ApplicationStage.Approved => a.Hold == HoldStatus.Captured ? "Confirmed" : "PaymentPending",
        ApplicationStage.Waitlisted => "Waitlisted",
        ApplicationStage.Declined => "Declined",
        _ => "ApplicationPending",
    };

    public static IQueryable<AdmittanceApplication> WithPeople(this IQueryable<AdmittanceApplication> q) =>
        q.Include(a => a.Applicant).Include(a => a.Household).Include(a => a.Session).ThenInclude(s => s.Program).ThenInclude(p => p.Ministry);

    public static object Payment(AdmittanceApplication a, DateTime now) => new
    {
        State = PaymentState(a, now),
        a.AmountCents,
        a.CardLast4,
        a.AuthorizedAt,
        ExpiresAt = a.Hold == HoldStatus.Authorized ? a.AuthorizationExpiresAt : null,
        CanUpdateCard = (a.Stage == ApplicationStage.Approved && a.SeatHeld && a.Hold != HoldStatus.Captured)
            || (AdmittanceApplication.Pending.Contains(a.Stage) && !a.HoldUsable(now)),
    };

    static object SessionInfo(Session s) => new
    {
        s.Id,
        s.Name,
        s.StartDate,
        s.EndDate,
        s.PriceCents,
        Program = new { s.Program.Name, s.Program.Slug, s.Program.Location, Ministry = s.Program.Ministry.Name, MinistryCode = s.Program.Ministry.Code },
    };

    // ── Family ──────────────────────────────────────────────────────────────

    public static async Task<object?> ApplyContext(CampDbContext db, int householdId, string email, int sessionId)
    {
        var s = await db.Sessions.Include(x => x.Program).ThenInclude(p => p.Ministry).Include(x => x.Pools).AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.Program.Type == ProgramType.Admittance && x.Program.IsPublished);
        if (s is null) return null;
        var adults = await db.People.Where(p => p.HouseholdId == householdId && p.IsAdult).OrderBy(p => p.Id).AsNoTracking().ToListAsync();
        var app = await db.Set<AdmittanceApplication>().AsNoTracking().FirstOrDefaultAsync(a => a.HouseholdId == householdId && a.SessionId == sessionId);
        var applicant = app is not null ? adults.FirstOrDefault(p => p.Id == app.ApplicantPersonId)
            : adults.FirstOrDefault(p => string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase)) ?? adults.FirstOrDefault();
        return new
        {
            Session = SessionInfo(s),
            SeatsLeft = s.Pools.Sum(p => p.Capacity - p.Reserved),
            Applicant = applicant is null ? null : new { applicant.Id, applicant.FirstName, applicant.LastName, applicant.Email },
            OtherAdults = adults.Where(p => p.Id != applicant?.Id).Select(p => new { p.Id, p.FirstName, p.LastName, p.Email }),
            ApplicationForm.Sections,
            ApplicationForm.Questions,
            Application = app is null ? null : new
            {
                app.Id,
                Stage = app.Stage.ToString(),
                app.CurrentStep,
                app.SpousePersonId,
                app.SpouseFirstName,
                app.SpouseLastName,
                app.SpouseEmail,
                Answers = AdmittanceService.Answers(app),
                app.UpdatedAt,
            },
        };
    }

    public static async Task<object> FamilyList(CampDbContext db, int householdId, TimeProvider clock)
    {
        var now = clock.UtcNow();
        var apps = await db.Set<AdmittanceApplication>().Where(a => a.HouseholdId == householdId).WithPeople().AsNoTracking().ToListAsync();
        return apps.OrderByDescending(a => a.UpdatedAt).Select(a => new
        {
            a.Id,
            Stage = a.Stage.ToString(),
            Status = Status(a),
            Couple = a.CoupleName,
            Session = SessionInfo(a.Session),
            a.SubmittedAt,
            PaymentState = PaymentState(a, now),
        });
    }

    public static async Task<object?> FamilyStatus(CampDbContext db, int householdId, int id, TimeProvider clock)
    {
        var a = await db.Set<AdmittanceApplication>().Where(x => x.Id == id && x.HouseholdId == householdId).WithPeople().AsNoTracking().FirstOrDefaultAsync();
        if (a is null) return null;
        var now = clock.UtcNow();
        var code = a.OrderId is null ? null : await db.Orders.Where(o => o.Id == a.OrderId).Select(o => o.ConfirmationCode).FirstOrDefaultAsync();
        return new
        {
            a.Id,
            Stage = a.Stage.ToString(),
            Status = Status(a),
            Couple = a.CoupleName,
            Session = SessionInfo(a.Session),
            a.CurrentStep,
            a.SubmittedAt,
            a.ReviewStartedAt,
            a.DecidedAt,
            // Decline and waitlist messages are written to the couple; internal notes never are.
            DecisionNote = a.Stage is ApplicationStage.Declined or ApplicationStage.Waitlisted ? a.DecisionNote : null,
            a.InfoRequest,
            a.InfoRequestedAt,
            a.InfoResponse,
            a.InfoRespondedAt,
            Payment = Payment(a, now),
            ConfirmationCode = code,
        };
    }

    // ── Staff ───────────────────────────────────────────────────────────────

    public static async Task<object> StaffSessions(CampDbContext db, TimeProvider clock)
    {
        var sessions = await db.Sessions.Where(s => s.Program.Type == ProgramType.Admittance)
            .Include(s => s.Program).ThenInclude(p => p.Ministry).Include(s => s.Pools).AsNoTracking().ToListAsync();
        var pending = await db.Set<AdmittanceApplication>().Where(a => AdmittanceApplication.Pending.Contains(a.Stage))
            .GroupBy(a => a.SessionId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        // Upcoming sessions first: the queue opens on the first one, and a past retreat has nothing to review.
        var today = clock.Today();
        return sessions.OrderBy(s => s.EndDate < today).ThenBy(s => s.StartDate).Select(s => new
        {
            Session = SessionInfo(s),
            Capacity = s.Pools.Sum(p => p.Capacity),
            Remaining = s.Pools.Sum(p => p.Capacity - p.Reserved),
            Pending = pending.GetValueOrDefault(s.Id),
        });
    }

    public static async Task<object?> Queue(CampDbContext db, int sessionId, TimeProvider clock)
    {
        var s = await db.Sessions.Include(x => x.Program).ThenInclude(p => p.Ministry).Include(x => x.Pools).AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.Program.Type == ProgramType.Admittance);
        if (s is null) return null;
        var now = clock.UtcNow();
        var apps = await db.Set<AdmittanceApplication>().Where(a => a.SessionId == sessionId && a.Stage != ApplicationStage.Draft)
            .Include(a => a.Applicant).Include(a => a.Household).AsNoTracking().ToListAsync();
        int Count(params ApplicationStage[] stages) => apps.Count(a => stages.Contains(a.Stage));
        return new
        {
            Session = SessionInfo(s),
            Capacity = s.Pools.Sum(p => p.Capacity),
            Reserved = s.Pools.Sum(p => p.Reserved),
            Remaining = s.Pools.Sum(p => p.Capacity - p.Reserved),
            Counts = new
            {
                All = apps.Count,
                Submitted = Count(ApplicationStage.Submitted),
                UnderReview = Count(ApplicationStage.UnderReview, ApplicationStage.InfoRequested),
                Approved = Count(ApplicationStage.Approved),
                Waitlisted = Count(ApplicationStage.Waitlisted),
                Declined = Count(ApplicationStage.Declined),
            },
            Rows = apps.OrderBy(a => a.SubmittedAt).Select(a => new
            {
                a.Id,
                Couple = a.CoupleName,
                a.Household.Email,
                Stage = a.Stage.ToString(),
                PaymentState = PaymentState(a, now),
                a.AmountCents,
                ExpiresAt = a.Hold == HoldStatus.Authorized ? a.AuthorizationExpiresAt : null,
                a.SubmittedAt,
                LastActivity = a.UpdatedAt,
            }),
        };
    }

    public static async Task<object?> StaffDetail(CampDbContext db, int id, TimeProvider clock)
    {
        var a = await db.Set<AdmittanceApplication>().Where(x => x.Id == id && x.Stage != ApplicationStage.Draft).WithPeople().AsNoTracking().FirstOrDefaultAsync();
        if (a is null) return null;
        var now = clock.UtcNow();
        var answers = AdmittanceService.Answers(a);
        var key = id.ToString(CultureInfo.InvariantCulture);
        var history = await db.AuditEvents.Where(e => e.EntityType == "AdmittanceApplication" && e.EntityId == key)
            .OrderByDescending(e => e.Id).AsNoTracking().ToListAsync();
        var remaining = await db.CapacityPools.Where(p => p.SessionId == a.SessionId).SumAsync(p => p.Capacity - p.Reserved);
        return new
        {
            a.Id,
            Stage = a.Stage.ToString(),
            Status = Status(a),
            Couple = a.CoupleName,
            Session = SessionInfo(a.Session),
            SessionRemaining = remaining,
            Applicant = new { a.Applicant.FirstName, a.Applicant.LastName, Email = a.Applicant.Email ?? a.Household.Email },
            Spouse = new { FirstName = a.SpouseFirstName, LastName = a.SpouseLastName, Email = a.SpouseEmail },
            Household = new { a.Household.Phone, a.Household.City },
            a.SubmittedAt,
            a.ReviewStartedAt,
            a.DecidedAt,
            a.ReviewedBy,
            a.DecisionNote,
            a.InfoRequest,
            a.InfoRequestedAt,
            a.InfoResponse,
            a.InfoRespondedAt,
            a.RegistrationId,
            LastActivity = a.UpdatedAt,
            Answers = ApplicationForm.Questions.Where(q => answers.ContainsKey(q.Key))
                .Select(q => new { q.Key, q.Label, Section = ApplicationForm.Sections.First(s => s.Key == q.Section).Title, Answer = answers[q.Key] }),
            Payment = Payment(a, now),
            History = history.Select(e => new { e.Actor, e.Action, e.Detail, e.CreatedAt }),
        };
    }
}
