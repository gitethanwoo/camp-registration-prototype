using Camp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.StaffCx;

/// <summary>A likely duplicate: two households that share a person (same name and DOB) and a phone number.</summary>
public sealed record DuplicatePair(int HouseholdA, int HouseholdB, List<string> SharedPeople, string Phone)
{
    /// <summary>Plain-words reason shown to staff, e.g. "Same name + phone + date of birth (Jordan Lee)".</summary>
    public string Reason => $"Same name + phone + date of birth ({string.Join(", ", SharedPeople)})";
}

/// <summary>
/// Finds likely duplicate households on real rows (FR-7). Households already merged away are ignored,
/// so a merged pair drops out of the queue.
/// </summary>
public static class DuplicateDetector
{
    public static async Task<List<DuplicatePair>> FindAsync(CampDbContext db, int? householdId = null, CancellationToken ct = default)
    {
        var merged = db.Set<HouseholdMerge>().Select(m => m.MergedHouseholdId);
        var matches = await (
            from p1 in db.People
            join p2 in db.People on new { p1.FirstName, p1.LastName, p1.DateOfBirth } equals new { p2.FirstName, p2.LastName, p2.DateOfBirth }
            where p1.HouseholdId < p2.HouseholdId
            join h1 in db.Households on p1.HouseholdId equals h1.Id
            join h2 in db.Households on p2.HouseholdId equals h2.Id
            where h1.Phone != "" &&
                h1.Phone.Replace("(", "").Replace(")", "").Replace("-", "").Replace(" ", "").Replace(".", "") ==
                h2.Phone.Replace("(", "").Replace(")", "").Replace("-", "").Replace(" ", "").Replace(".", "")
            where !merged.Contains(h1.Id) && !merged.Contains(h2.Id)
            where householdId == null || h1.Id == householdId || h2.Id == householdId
            select new { A = h1.Id, B = h2.Id, Name = p1.FirstName + " " + p1.LastName, IsAdult = p1.IsAdult, h1.Phone })
            .AsNoTracking().ToListAsync(ct);

        return matches
            .GroupBy(m => (m.A, m.B))
            .Select(g => new DuplicatePair(g.Key.A, g.Key.B,
                g.OrderBy(m => m.IsAdult).Select(m => m.Name).Distinct().ToList(), g.First().Phone))
            .OrderBy(p => p.HouseholdA)
            .ToList();
    }
}
