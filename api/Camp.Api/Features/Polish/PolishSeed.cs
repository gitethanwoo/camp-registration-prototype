using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Polish;

/// <summary>
/// Family Camp and the Fall Marriage Retreat were seeded published with no waiver, so families registered
/// without signing anything. Every published program with no waiver gets the standard release, and each of its
/// existing registrations gets a signature (families would have signed it at checkout). Runs after the slice
/// seeds that add programs (finance 200, admittance and groups at 100+) and before setup's backfill (900),
/// which gives the new waivers their version history. Safe to run on every start.
/// </summary>
public sealed class PolishSeed : ISeedModule
{
    public const string FamilyCampWaiverTitle = "Family Camp Release and Waiver of Liability";

    /// <inheritdoc />
    public int Order => 850;

    /// <inheritdoc />
    public async Task RunAsync(CampDbContext db, CancellationToken ct)
    {
        var programs = await db.Programs.Where(p => p.IsPublished && !p.Waivers.Any()).ToListAsync(ct);
        foreach (var program in programs)
        {
            var waiver = new WaiverTemplate { Title = StandardTitle(program.Name), Version = 1, EffectiveDate = new(2027, 11, 1), PerParticipant = true, Body = ReleaseText(program.Name) };
            program.Waivers.Add(waiver);
            await db.SaveChangesAsync(ct);

            var registrations = await db.Registrations
                .Where(r => r.Session.ProgramId == program.Id && r.Status != RegistrationStatus.Cancelled)
                .Select(r => new
                {
                    Registration = r,
                    Signer = db.People.Where(p => p.HouseholdId == r.HouseholdId && p.IsAdult).OrderBy(p => p.Id).Select(p => p.FirstName + " " + p.LastName).FirstOrDefault(),
                    SignedAt = r.Order != null ? r.Order.CreatedAt : r.CreatedAt,
                })
                .ToListAsync(ct);
            foreach (var r in registrations)
                r.Registration.WaiverAcceptances.Add(new WaiverAcceptance { WaiverTemplateId = waiver.Id, Version = waiver.Version, SignerName = r.Signer ?? "Parent or guardian", AcceptedAt = r.SignedAt });
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>The standard release's title for one program.</summary>
    internal static string StandardTitle(string program) => $"{program} Release and Waiver of Liability";

    /// <summary>The standard participant release, worded for one program. K2's "Add the standard release" uses it too.</summary>
    internal static string ReleaseText(string program) => $"""
        In consideration of participation in WinShape {program} by me and anyone I register, I acknowledge that program activities, including swimming, the ropes course, field games, and travel between activity areas, carry inherent risks of injury.

        I understand that WinShape Foundation, its staff, and volunteers take reasonable precautions but cannot eliminate all risk. On behalf of myself and anyone I register, I release WinShape Foundation and its host partners from claims arising from ordinary negligence related to participation, to the extent permitted by Georgia law.

        I authorize staff to obtain emergency medical treatment for anyone I register if I cannot be reached, and I accept responsibility for costs of such care.

        I confirm the health information I have provided is accurate and complete, and I will notify WinShape of any changes before the program begins.
        """;
}
