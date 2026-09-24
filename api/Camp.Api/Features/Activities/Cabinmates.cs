using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Ops;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Activities;

/// <summary>
/// R5 cabinmate requests (FR-25). A request names a friend and gives either a parent's email or the
/// friend's confirmation code. It matches a camper in the same session when the contact finds their
/// family and the name finds the camper (full name, or first name when only one camper fits). A
/// match becomes an OpsBuddyRequest, which O3 judges as met or not met. Nothing here promises a cabin.
/// </summary>
public static class Cabinmates
{
    sealed record Candidate(int RegistrationId, int HouseholdId, string FirstName, string LastName);

    /// <summary>The registration a request points to, or null. Never returns the family's own campers.</summary>
    public static async Task<int?> MatchAsync(CampDbContext db, int sessionId, int ownHouseholdId, string name, string contact, CancellationToken ct)
    {
        var want = Normalize(name);
        var key = contact.Trim();
        if (want.Length == 0 || key.Length == 0) return null;
        var active = db.Registrations.Where(r => r.SessionId == sessionId && r.HouseholdId != ownHouseholdId
            && (r.Status == RegistrationStatus.Confirmed || r.Status == RegistrationStatus.PaymentPending));
        if (key.Contains('@'))
        {
            var email = key.ToLowerInvariant();
            active = active.Where(r => r.Person.Household.Email == email || r.Person.Household.Members.Any(m => m.IsAdult && m.Email == email));
        }
        else
        {
            var code = key.ToUpperInvariant();
            if (!code.StartsWith("WS-", StringComparison.Ordinal)) code = "WS-" + code;
            active = active.Where(r => r.Order != null && r.Order.ConfirmationCode == code);
        }
        var candidates = await active.Select(r => new Candidate(r.Id, r.HouseholdId, r.Person.FirstName, r.Person.LastName)).ToListAsync(ct);
        return Pick(candidates, want);
    }

    /// <summary>Stores a camper's requests and mirrors matched ones to O3. Replaces any earlier requests for the camper.</summary>
    public static async Task SaveAsync(CampDbContext db, int sessionId, int registrationId, int householdId, List<CabinmateInput> inputs, DateTime now, CancellationToken ct)
    {
        await db.Set<CabinmateRequest>().Where(c => c.RegistrationId == registrationId).ExecuteDeleteAsync(ct);
        await db.Set<OpsBuddyRequest>().Where(b => b.RegistrationId == registrationId).ExecuteDeleteAsync(ct);
        var mirrored = new HashSet<int>();
        foreach (var input in inputs.Where(i => !string.IsNullOrWhiteSpace(i.Name)))
        {
            var match = await MatchAsync(db, sessionId, householdId, input.Name, input.Contact, ct);
            db.Set<CabinmateRequest>().Add(new CabinmateRequest
            {
                SessionId = sessionId,
                RegistrationId = registrationId,
                FriendName = input.Name.Trim(),
                Contact = input.Contact.Trim(),
                MatchedRegistrationId = match,
                CreatedAt = now,
            });
            if (match is { } m && mirrored.Add(m))
                db.Set<OpsBuddyRequest>().Add(new OpsBuddyRequest { SessionId = sessionId, RegistrationId = registrationId, RequestedRegistrationId = m, CreatedAt = now });
        }
    }

    /// <summary>A camper who just registered may be the friend an earlier request couldn't find yet.</summary>
    public static async Task MatchWaitingAsync(CampDbContext db, int sessionId, List<int> registrationIds, DateTime now, CancellationToken ct)
    {
        var waiting = await db.Set<CabinmateRequest>().Where(c => c.SessionId == sessionId && c.MatchedRegistrationId == null && !registrationIds.Contains(c.RegistrationId)).ToListAsync(ct);
        if (waiting.Count == 0) return;
        var owners = await db.Registrations.AsNoTracking().Where(r => waiting.Select(w => w.RegistrationId).Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.HouseholdId, ct);
        foreach (var w in waiting)
        {
            var match = await MatchAsync(db, sessionId, owners.GetValueOrDefault(w.RegistrationId), w.FriendName, w.Contact, ct);
            if (match is not { } m || !registrationIds.Contains(m)) continue;
            w.MatchedRegistrationId = m;
            if (!await db.Set<OpsBuddyRequest>().AnyAsync(b => b.RegistrationId == w.RegistrationId && b.RequestedRegistrationId == m, ct))
                db.Set<OpsBuddyRequest>().Add(new OpsBuddyRequest { SessionId = sessionId, RegistrationId = w.RegistrationId, RequestedRegistrationId = m, CreatedAt = now });
        }
    }

    /// <summary>Drops the requests these registrations made and unmatches requests that pointed at them.</summary>
    public static async Task ForgetAsync(CampDbContext db, List<int> registrationIds, CancellationToken ct)
    {
        await db.Set<CabinmateRequest>().Where(c => registrationIds.Contains(c.RegistrationId)).ExecuteDeleteAsync(ct);
        await db.Set<CabinmateRequest>().Where(c => c.MatchedRegistrationId != null && registrationIds.Contains(c.MatchedRegistrationId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.MatchedRegistrationId, (int?)null), ct);
        await db.Set<OpsBuddyRequest>().Where(b => registrationIds.Contains(b.RegistrationId) || registrationIds.Contains(b.RequestedRegistrationId)).ExecuteDeleteAsync(ct);
    }

    static int? Pick(List<Candidate> candidates, string want)
    {
        var full = candidates.Where(c => Normalize($"{c.FirstName} {c.LastName}") == want).ToList();
        if (full.Count == 1) return full[0].RegistrationId;
        if (want.Contains(' ')) return null;
        var first = candidates.Where(c => Normalize(c.FirstName) == want).ToList();
        return first.Count == 1 ? first[0].RegistrationId : null;
    }

    static string Normalize(string s) => string.Join(' ', s.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
