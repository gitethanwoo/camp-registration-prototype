using System.Net.Mail;
using System.Text.Json;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Family;

public sealed record InviteRequest(string FirstName, string LastName, string Email);

/// <summary>F3 household access: who can use this account, invitations, and revoking (FR-8, FR-6).</summary>
public sealed class HouseholdAccessEndpoints : IEndpointModule
{
    public const string Primary = "Primary";
    public const string CoOwner = "Co-owner";
    static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(14);
    static readonly string[] PrimaryPermissions = ["Register household members", "View history and payments", "Manage who has access"];
    static readonly string[] CoOwnerPermissions = ["Register household members", "View history and payments"];

    public void Map(IEndpointRouteBuilder app)
    {
        var family = app.MapGroup("/api/family").RequireAuthorization(Policies.Family);

        family.MapGet("/access", async (CampDbContext db, CurrentUser me, TimeProvider clock) =>
        {
            var household = await db.Households.Include(h => h.Members).AsNoTracking().SingleAsync(h => h.Id == me.HouseholdId);
            var season = await FamilyReadModel.SeasonYearAsync(db, clock);
            var now = clock.UtcNow();
            var invitations = await db.Set<HouseholdInvitation>()
                .Where(i => i.HouseholdId == me.HouseholdId && i.Status == InvitationStatus.Pending)
                .OrderBy(i => i.CreatedAt).AsNoTracking().ToListAsync();
            var self = Self(household, me);

            return new
            {
                household.Name,
                SeasonYear = season,
                CanManage = self?.Role == Primary,
                Adults = household.Members.Where(m => m.IsAdult).OrderBy(m => m.Role == Primary ? 0 : m.Role == CoOwner ? 1 : 2).Select(m => new
                {
                    m.Id,
                    m.FirstName,
                    m.LastName,
                    m.Email,
                    m.Role,
                    IsYou = m.Id == self?.Id,
                    Access = m.Role switch
                    {
                        Primary => "Primary owner",
                        CoOwner => "Co-owner",
                        _ => "No account access",
                    },
                    // Co-owners register members and see history and payments; they don't manage
                    // payment methods another adult added, or who has access.
                    Permissions = m.Role switch
                    {
                        Primary => PrimaryPermissions,
                        CoOwner => CoOwnerPermissions,
                        _ => [],
                    },
                }),
                Invitations = invitations.Select(i => new
                {
                    i.Id,
                    i.FirstName,
                    i.LastName,
                    i.Email,
                    i.InvitedBy,
                    i.CreatedAt,
                    i.ExpiresAt,
                    Expired = i.ExpiresAt < now,
                }),
                Dependents = household.Members.Where(m => !m.IsAdult).OrderBy(m => m.DateOfBirth).Select(m => new
                {
                    m.Id,
                    m.FirstName,
                    m.LastName,
                    GradeLabel = Eligibility.GradeLabel(FamilyReadModel.GradeFor(m, season)),
                }),
            };
        });

        family.MapPost("/invitations", async (InviteRequest req, CampDbContext db, CurrentUser me, IAuditLog audit, TimeProvider clock) =>
        {
            var household = await db.Households.Include(h => h.Members).SingleAsync(h => h.Id == me.HouseholdId);
            if (Self(household, me)?.Role != Primary) return OnlyPrimary(household);

            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(req.FirstName)) errors["firstName"] = ["Enter their first name."];
            if (string.IsNullOrWhiteSpace(req.LastName)) errors["lastName"] = ["Enter their last name."];
            if (!MailAddress.TryCreate(req.Email?.Trim() ?? "", out _)) errors["email"] = ["Enter an email like name@example.com."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var email = req.Email!.Trim().ToLowerInvariant();
            if (household.Members.Any(m => m.IsAdult && m.Role is not null && string.Equals(m.Email, email, StringComparison.OrdinalIgnoreCase)))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = [$"{email} already has access to this household."] });
            if (await SignsInElsewhere(db, me.HouseholdId, email))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = [ElsewhereMessage(email)] });
            var pending = await db.Set<HouseholdInvitation>().AnyAsync(i => i.HouseholdId == me.HouseholdId && i.Status == InvitationStatus.Pending && i.Email == email && i.ExpiresAt > clock.UtcNow());
            if (pending)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = [$"You already invited {email}. Cancel that invitation to send a new one."] });

            var invitation = new HouseholdInvitation
            {
                HouseholdId = me.HouseholdId,
                FirstName = req.FirstName.Trim(),
                LastName = req.LastName.Trim(),
                Email = email,
                InvitedBy = me.Name,
                Status = InvitationStatus.Pending,
                CreatedAt = clock.UtcNow(),
                ExpiresAt = clock.UtcNow() + InvitationLifetime,
            };
            db.Add(invitation);
            audit.Record("household.invited", "Household", me.HouseholdId, $"{me.Name} invited {invitation.FirstName} {invitation.LastName} ({email}) as a co-owner.");
            // Emails go out through HubSpot via the outbox; the app never sends mail itself.
            db.OutboxEvents.Add(new OutboxEvent
            {
                Type = "HouseholdInvitation",
                Target = "HubSpot",
                AggregateId = $"household-{me.HouseholdId}",
                PayloadJson = JsonSerializer.Serialize(new { to = email, invitedBy = me.Name, household = household.Name }),
                CreatedAt = clock.UtcNow(),
            });
            await db.SaveChangesAsync();
            return Results.Created($"/api/family/invitations/{invitation.Id}", new { invitation.Id, invitation.Email, invitation.ExpiresAt });
        });

        family.MapDelete("/invitations/{id:int}", async (int id, CampDbContext db, CurrentUser me, IAuditLog audit) =>
        {
            var household = await db.Households.Include(h => h.Members).SingleAsync(h => h.Id == me.HouseholdId);
            var invitation = await db.Set<HouseholdInvitation>().FirstOrDefaultAsync(i => i.Id == id && i.HouseholdId == me.HouseholdId);
            if (invitation is null) return Results.NotFound();
            if (Self(household, me)?.Role != Primary) return OnlyPrimary(household);
            if (invitation.Status != InvitationStatus.Pending) return Results.NoContent();
            invitation.Status = InvitationStatus.Cancelled;
            audit.Record("household.invitation_cancelled", "Household", me.HouseholdId, $"{me.Name} cancelled the invitation to {invitation.Email}.");
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Revoking account access leaves the person in the household, so guardian links to the
        // children stay (the F3 trap: a revoke must not silently remove them).
        family.MapPost("/members/{id:int}/revoke-access", async (int id, CampDbContext db, CurrentUser me, IAuditLog audit) =>
        {
            var household = await db.Households.Include(h => h.Members).SingleAsync(h => h.Id == me.HouseholdId);
            var target = household.Members.FirstOrDefault(m => m.Id == id);
            if (target is null) return Results.NotFound();
            var self = Self(household, me);
            if (self?.Role != Primary) return OnlyPrimary(household);
            if (target.Id == self.Id || target.Role == Primary)
                return Results.Conflict(new { error = "The primary owner's access can't be revoked. Contact us to change the primary owner." });
            if (target.Role is null) return Results.NoContent();

            target.Role = null;
            await db.Set<HouseholdInvitation>().Where(i => i.HouseholdId == me.HouseholdId && i.Status == InvitationStatus.Pending && i.Email == target.Email)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvitationStatus.Cancelled));
            audit.Record("household.access_revoked", "Person", target.Id, $"{me.Name} revoked {target.FullName}'s account access. {target.FirstName} stays in the household as a guardian.");
            await db.SaveChangesAsync();
            return Results.Ok(new { target.Id, Access = "No account access" });
        });
    }

    /// <summary>
    /// Sign-in finds a household through an adult with access, or the household's own email
    /// (<c>AuthEndpoints.HouseholdFor</c>). One email may open only one household, so an email
    /// that already signs in somewhere else can't be given to an adult here.
    /// </summary>
    public static async Task<bool> SignsInElsewhere(CampDbContext db, int householdId, string email) =>
        await db.People.AnyAsync(p => p.HouseholdId != householdId && p.IsAdult && p.Role != null && p.Email == email && !p.Household.Email.StartsWith("merged-into-"))
        || await db.Households.AnyAsync(h => h.Id != householdId && h.Email == email);

    public static string ElsewhereMessage(string email) =>
        $"{email} already signs in to another family account. Use a different email, or contact us to combine the accounts.";

    /// <summary>The adult in the household who is signed in, matched by sign-in email.</summary>
    static Person? Self(Household household, CurrentUser me) =>
        household.Members.FirstOrDefault(m => m.IsAdult && string.Equals(m.Email, me.Email, StringComparison.OrdinalIgnoreCase));

    static IResult OnlyPrimary(Household household)
    {
        var owner = household.Members.FirstOrDefault(m => m.Role == Primary);
        return Results.Json(new { error = $"Only the primary owner{(owner is null ? "" : $", {owner.FirstName},")} can change who has access." }, statusCode: StatusCodes.Status403Forbidden);
    }
}
