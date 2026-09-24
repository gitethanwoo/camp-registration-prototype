using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Setup;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Polish;

/// <summary>
/// K2 · Gives a draft program the standard release, so it can pass the publish guard (a program with no
/// waiver can't be submitted or published). The text is edited afterwards through K7's version history.
/// </summary>
public sealed class ProgramWaiverEndpoints : IEndpointModule
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/admin/setup/programs/{id:int}/waivers", async (int id, CampDbContext db, StaffUser staff, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            var program = await db.Programs.Include(p => p.Waivers).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (program is null) return Results.NotFound();
            var setup = await ProgramSetupEndpoints.StateOf(db, program, ct);
            if (setup.State != PublishState.Draft)
                return SetupResults.Conflict($"{program.Name} is {ProgramSetupEndpoints.StateLabel(setup.State).ToLowerInvariant()}. Return it to draft to add a waiver.");
            var title = $"{program.Name} Release and Waiver of Liability";
            if (program.Waivers.Any(w => w.Title == title)) return SetupResults.Conflict($"{program.Name} already has the standard release.");

            var today = clock.Today();
            var waiver = new WaiverTemplate { Title = title, Version = 1, EffectiveDate = today, PerParticipant = true, Body = PolishSeed.ReleaseText(program.Name) };
            program.Waivers.Add(waiver);
            await db.SaveChangesAsync(ct);
            // Version 1 needs no second admin: the program's own approval chain reviews it before families see it.
            db.Set<WaiverVersion>().Add(new WaiverVersion
            {
                WaiverTemplateId = waiver.Id,
                Version = 1,
                Body = waiver.Body,
                ChangeNote = "Standard release added in program setup.",
                Status = WaiverVersionStatus.Published,
                EffectiveDate = today,
                CreatedBy = staff.Actor,
                CreatedAt = clock.UtcNow(),
            });
            audit.Record(db, "waiver.added", "Program", id, $"Added {title} to {program.Name}.", ("Waivers", "None", $"{title} v1"));
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { waiver.Id, waiver.Title, waiver.Version });
        }).RequireAuthorization(Policies.Admin);
    }
}
