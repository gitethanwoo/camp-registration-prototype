using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Finance;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Polish;

/// <summary>
/// Family Camp was seeded published with no waiver, so its families registered without signing anything.
/// This gives it a release and records a signature for each existing registration (families would have
/// signed it at checkout). Runs after finance's seed (200) and before setup's backfill (900), which then
/// gives the new waiver its version history. Safe to run on every start.
/// </summary>
public sealed class PolishSeed : ISeedModule
{
    public const string FamilyCampWaiverTitle = "Family Camp Release and Waiver of Liability";

    /// <inheritdoc />
    public int Order => 210;

    /// <inheritdoc />
    public async Task RunAsync(CampDbContext db, CancellationToken ct)
    {
        var program = await db.Programs.Include(p => p.Waivers).FirstOrDefaultAsync(p => p.Slug == FinanceSeed.ProgramSlug, ct);
        if (program is null || program.Waivers.Count > 0) return;

        var waiver = new WaiverTemplate { Title = FamilyCampWaiverTitle, Version = 1, EffectiveDate = new(2027, 11, 1), PerParticipant = true, Body = ReleaseText("Family Camp") };
        program.Waivers.Add(waiver);
        await db.SaveChangesAsync(ct);

        var registrations = await db.Registrations
            .Where(r => r.Session.ProgramId == program.Id && r.Status != RegistrationStatus.Cancelled)
            .Select(r => new { Registration = r, Signer = db.People.Where(p => p.HouseholdId == r.HouseholdId && p.IsAdult).OrderBy(p => p.Id).Select(p => p.FirstName + " " + p.LastName).FirstOrDefault(), SignedAt = r.Order != null ? r.Order.CreatedAt : r.CreatedAt })
            .ToListAsync(ct);
        foreach (var r in registrations)
            r.Registration.WaiverAcceptances.Add(new WaiverAcceptance { WaiverTemplateId = waiver.Id, Version = waiver.Version, SignerName = r.Signer ?? "Parent or guardian", AcceptedAt = r.SignedAt });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The standard participant release, worded for one program. K2's "Add the standard release" uses it too.</summary>
    internal static string ReleaseText(string program) => $"""
        In consideration of my family's participation in WinShape {program}, I acknowledge that camp activities, including swimming, the ropes course, field games, and travel between activity areas, carry inherent risks of injury.

        I understand that WinShape Foundation, its staff, and volunteers take reasonable precautions but cannot eliminate all risk. On behalf of my children and myself, I release WinShape Foundation and its host partners from claims arising from ordinary negligence related to participation, to the extent permitted by Georgia law.

        I authorize camp staff to obtain emergency medical treatment for my child if I cannot be reached, and I accept responsibility for costs of such care.

        I confirm the information I have provided about my child's health and needs is accurate and complete, and I will notify WinShape of any changes before the session begins.
        """;
}
