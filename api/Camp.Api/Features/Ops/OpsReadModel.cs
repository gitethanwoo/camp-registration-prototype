using Camp.Api.Data;
using Camp.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Ops;

/// <summary>One open readiness item. The same three reasons drive O1 "needs attention" and O5 check-in blockers.</summary>
public enum ReadinessReason { Health, Waiver, Balance }

/// <summary>A confirmed camper in a session with everything the operations screens read about them.</summary>
internal sealed record Camper(
    int RegistrationId,
    int PersonId,
    int HouseholdId,
    string FirstName,
    string LastName,
    Gender Gender,
    int Grade,
    int PoolId,
    string PoolName,
    int BalanceCents,
    int WaiversSigned,
    int WaiversRequired,
    FormStatus Health,
    DateTime CreatedAt,
    string? ConfirmationCode,
    OpsPlacement? Placement)
{
    public string Name => $"{FirstName} {LastName}";

    public bool HealthOpen => Health is FormStatus.Incomplete or FormStatus.Missing;
    public bool WaiverOpen => WaiversSigned < WaiversRequired;
    public bool BalanceOpen => BalanceCents > 0;

    public IReadOnlyList<ReadinessReason> Reasons =>
        [.. new[] { (HealthOpen, ReadinessReason.Health), (WaiverOpen, ReadinessReason.Waiver), (BalanceOpen, ReadinessReason.Balance) }
            .Where(x => x.Item1).Select(x => x.Item2)];

    public string WaiverState => WaiversSigned >= WaiversRequired ? "Complete" : WaiversSigned == 0 ? "Missing" : "Incomplete";
}

internal sealed record SessionRoster(Session Session, bool UsesCampDoc, List<Camper> Campers);

/// <summary>Reads a session's confirmed campers with their readiness and operations placement.</summary>
internal static class OpsReadModel
{
    public const string CampDocUrl = "https://app.campdoc.com/";

    public static async Task<SessionRoster?> LoadAsync(CampDbContext db, int sessionId, CancellationToken ct)
    {
        var session = await db.Sessions.Include(s => s.Program).Include(s => s.Pools).AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return null;
        var required = await db.WaiverTemplates.CountAsync(w => w.ProgramId == session.ProgramId, ct);
        var rows = await db.Registrations.AsNoTracking()
            .Where(r => r.SessionId == sessionId && r.Status == RegistrationStatus.Confirmed)
            .Select(r => new
            {
                r.Id,
                r.PersonId,
                r.HouseholdId,
                r.Person.FirstName,
                r.Person.LastName,
                r.Person.Gender,
                r.Grade,
                r.PoolId,
                PoolName = r.Pool.Name,
                Balance = r.PriceCents - r.DiscountCents - r.PaidCents,
                Signed = r.WaiverAcceptances.Select(w => w.WaiverTemplateId).Distinct().Count(),
                r.HealthStatus,
                r.CreatedAt,
                Code = r.Order != null ? r.Order.ConfirmationCode : null,
            })
            .ToListAsync(ct);
        // A placement left behind by a camper who moved to another session no longer applies here.
        var placements = await db.Set<OpsPlacement>().AsNoTracking().Where(p => p.SessionId == sessionId).ToDictionaryAsync(p => p.RegistrationId, ct);
        var campers = rows
            .Select(r => new Camper(r.Id, r.PersonId, r.HouseholdId, r.FirstName, r.LastName, r.Gender, r.Grade, r.PoolId, r.PoolName,
                r.Balance, r.Signed, required, r.HealthStatus, r.CreatedAt, r.Code, placements.GetValueOrDefault(r.Id)))
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ThenBy(c => c.RegistrationId)
            .ToList();
        return new SessionRoster(session, session.Program.HealthMechanism == HealthMechanism.CampDoc, campers);
    }

    /// <summary>"CampDoc incomplete" for ON, "Health form incomplete" for everything else.</summary>
    public static string HealthLabel(bool usesCampDoc) => usesCampDoc ? "CampDoc incomplete" : "Health form incomplete";

    /// <summary>Plain words for a blocker, e.g. "Balance due · $1,200 outstanding".</summary>
    public static (string Label, string Detail) Describe(ReadinessReason reason, Camper c, bool usesCampDoc) => reason switch
    {
        ReadinessReason.Health => (HealthLabel(usesCampDoc), usesCampDoc
            ? "The family hasn't finished the health profile in CampDoc."
            : "The family hasn't finished the health form."),
        ReadinessReason.Waiver => ("Waiver missing", $"{c.WaiversSigned} of {c.WaiversRequired} required waivers signed."),
        _ => ("Balance due", $"{CheckoutService.Money(c.BalanceCents)} outstanding."),
    };

    /// <summary>Gets the placement row for a registration, creating it if the camper has none yet.</summary>
    public static async Task<OpsPlacement> PlacementFor(CampDbContext db, Registration reg, CancellationToken ct)
    {
        var p = await db.Set<OpsPlacement>().FirstOrDefaultAsync(x => x.RegistrationId == reg.Id, ct);
        if (p is null)
        {
            p = new OpsPlacement { RegistrationId = reg.Id, SessionId = reg.SessionId };
            db.Set<OpsPlacement>().Add(p);
        }
        else if (p.SessionId != reg.SessionId)
        {
            // The camper moved sessions: nothing from the old session carries over.
            p.SessionId = reg.SessionId;
            p.CabinId = null;
            p.GroupId = null;
            p.SuggestedGroupId = null;
            p.SuggestionReason = null;
            p.CheckedInAt = null;
            p.CheckedInBy = null;
            p.CheckInOverride = null;
            p.CheckedOutAt = null;
            p.CheckedOutBy = null;
            p.PickedUpBy = null;
        }
        return p;
    }
}
