using System.Net.Mail;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Family;

/// <summary>Add or edit a household member (F2). Household contact and payment are not asked again.</summary>
public sealed record MemberRequest(
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    Gender? Gender,
    bool IsAdult,
    string? Email,
    string? Dietary,
    string? Allergies,
    string? AdaNeeds);

/// <summary>F1 family home and F2 member profile.</summary>
public sealed class FamilyEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        var family = app.MapGroup("/api/family").RequireAuthorization(Policies.Family);

        // F1 · members, upcoming registrations, payment plans, and one checklist across all kids (FR-3, FR-27).
        family.MapGet("/overview", async (CampDbContext db, CurrentUser me) =>
        {
            var season = await FamilyReadModel.SeasonYearAsync(db);
            var household = await db.Households.Include(h => h.Members).AsNoTracking().SingleAsync(h => h.Id == me.HouseholdId);
            var today = FamilyReadModel.Today;
            var upcoming = (await FamilyReadModel.Orders(db, me.HouseholdId).AsNoTracking().ToListAsync())
                .Where(o => o.Session.EndDate >= today && FamilyReadModel.Active(o).Count > 0)
                .OrderBy(o => o.Session.StartDate).ToList();

            return new
            {
                household.Name,
                SeasonYear = season,
                Members = household.Members.OrderByDescending(m => m.IsAdult).ThenBy(m => m.Role == "Primary" ? 0 : 1).ThenBy(m => m.DateOfBirth)
                    .Select(m => new
                    {
                        m.Id,
                        m.FirstName,
                        m.LastName,
                        m.IsAdult,
                        m.Role,
                        GradeLabel = m.IsAdult ? null : Eligibility.GradeLabel(FamilyReadModel.GradeFor(m, season)),
                    }),
                Registrations = upcoming.Select(o =>
                {
                    var money = FamilyReadModel.Money(o);
                    var active = FamilyReadModel.Active(o);
                    var next = o.Installments.Where(i => i.Status == InstallmentStatus.Scheduled).OrderBy(i => i.DueDate).FirstOrDefault();
                    return new
                    {
                        o.ConfirmationCode,
                        Program = o.Session.Program.Name,
                        Session = o.Session.Name,
                        o.Session.StartDate,
                        o.Session.EndDate,
                        Participants = FamilyReadModel.Participants(active.Select(r => r.Person.FirstName)),
                        Count = active.Count,
                        money.TotalCents,
                        money.PaidCents,
                        money.BalanceCents,
                        money.PaymentStatus,
                        Plan = o.Installments.Count == 0 ? null : new
                        {
                            Installments = o.Installments.Count,
                            EachCents = o.Installments.OrderBy(i => i.Sequence).First().AmountCents,
                            NextDueDate = next?.DueDate,
                            NextAmountCents = next?.AmountCents,
                        },
                    };
                }),
                Checklist = upcoming.SelectMany(FamilyReadModel.Checklist).ToList(),
            };
        });

        // F2 · one member, with what the page needs to explain locked fields.
        family.MapGet("/members/{id:int}", async (int id, CampDbContext db, CurrentUser me) =>
        {
            var m = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.HouseholdId == me.HouseholdId);
            if (m is null) return Results.NotFound();
            var season = await FamilyReadModel.SeasonYearAsync(db);
            var registeredFor = await ActiveRegistrationNames(db, m.Id);
            return Results.Ok(ToProfile(m, season, registeredFor));
        });

        family.MapPost("/members", async (MemberRequest req, CampDbContext db, CurrentUser me, IAuditLog audit) =>
        {
            var errors = Validate(req);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var first = req.FirstName.Trim();
            var dob = req.DateOfBirth ?? default;
            var duplicate = await db.People.AnyAsync(p => p.HouseholdId == me.HouseholdId && p.FirstName == first && p.DateOfBirth == dob);
            if (duplicate)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["firstName"] = [$"{first} is already in your family. Open their profile to change details."] });

            if (await EmailTaken(db, me.HouseholdId, null, req) is { } taken) return taken;

            var person = new Person { HouseholdId = me.HouseholdId };
            Apply(person, req);
            db.People.Add(person);
            audit.Record("member.added", "Household", me.HouseholdId, $"{me.Name} added {person.FullName} to the household.");
            await db.SaveChangesAsync();
            var season = await FamilyReadModel.SeasonYearAsync(db);
            return Results.Created($"/api/family/members/{person.Id}", ToProfile(person, season, []));
        });

        family.MapPut("/members/{id:int}", async (int id, MemberRequest req, CampDbContext db, CurrentUser me, IAuditLog audit) =>
        {
            var person = await db.People.FirstOrDefaultAsync(p => p.Id == id && p.HouseholdId == me.HouseholdId);
            if (person is null) return Results.NotFound();
            if (req.IsAdult != person.IsAdult)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["isAdult"] = ["A child can't be changed to an adult here. Contact us to set up their own account."] });
            var errors = Validate(req);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            // The primary owner's email is how sign-in finds who may manage access; it isn't edited here.
            if (person.Role == HouseholdAccessEndpoints.Primary && !string.Equals(Blank(req.Email), person.Email, StringComparison.OrdinalIgnoreCase))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["This is the email you sign in with. Contact us to change it."] });
            if (await EmailTaken(db, me.HouseholdId, person.Id, req) is { } taken) return taken;

            // Placement (pool and grade) was chosen from these; changing them would leave a camper in the wrong group.
            var registeredFor = await ActiveRegistrationNames(db, person.Id);
            var placementChanged = !person.IsAdult && (req.DateOfBirth != person.DateOfBirth || req.Gender != person.Gender);
            if (registeredFor.Count > 0 && placementChanged)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["dateOfBirth"] = [$"{person.FirstName} is registered for {string.Join(" and ", registeredFor)}, so date of birth and gender are locked. Contact us if they're wrong."],
                });

            Apply(person, req);
            audit.Record("member.updated", "Person", person.Id, $"{me.Name} updated {person.FullName}'s profile.");
            await db.SaveChangesAsync();
            return Results.Ok(ToProfile(person, await FamilyReadModel.SeasonYearAsync(db), registeredFor));
        });
    }

    /// <summary>Two adults with one email would make "who is signed in" ambiguous for household access.</summary>
    static async Task<IResult?> EmailTaken(CampDbContext db, int householdId, int? personId, MemberRequest req)
    {
        var email = req.IsAdult ? Blank(req.Email) : null;
        if (email is null) return null;
        var taken = await db.People.AnyAsync(p => p.HouseholdId == householdId && p.Id != personId && p.Email == email);
        return taken ? Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = [$"{email} already belongs to someone in your household."] }) : null;
    }

    static async Task<List<string>> ActiveRegistrationNames(CampDbContext db, int personId)
    {
        var today = FamilyReadModel.Today;
        return await db.Registrations
            .Where(r => r.PersonId == personId && r.Status != RegistrationStatus.Cancelled && r.Session.EndDate >= today)
            .Select(r => r.Session.Program.Name).Distinct().ToListAsync();
    }

    static object ToProfile(Person m, int season, List<string> registeredFor)
    {
        var today = FamilyReadModel.Today;
        var age = m.DateOfBirth == default ? (int?)null : FamilyReadModel.AgeOn(m.DateOfBirth, today);
        return new
        {
            m.Id,
            m.FirstName,
            m.LastName,
            DateOfBirth = m.DateOfBirth == default ? (DateOnly?)null : m.DateOfBirth,
            Gender = m.Gender.ToString(),
            m.IsAdult,
            m.Role,
            m.Email,
            m.Dietary,
            m.Allergies,
            m.AdaNeeds,
            SeasonYear = season,
            Age = age,
            GradeLabel = m.IsAdult || m.DateOfBirth == default ? null : Eligibility.GradeLabel(FamilyReadModel.GradeFor(m, season)),
            // Age of majority: a child who is 18 can have their own account.
            CanClaimOwnAccount = !m.IsAdult && age >= 18,
            RegisteredFor = registeredFor,
        };
    }

    static void Apply(Person p, MemberRequest req)
    {
        p.FirstName = req.FirstName.Trim();
        p.LastName = req.LastName.Trim();
        if (req.DateOfBirth is { } dob) p.DateOfBirth = dob;
        p.IsAdult = req.IsAdult;
        if (req.Gender is { } g) p.Gender = g;
        p.Email = req.IsAdult ? Blank(req.Email) : null;
        p.Dietary = Blank(req.Dietary);
        p.Allergies = Blank(req.Allergies);
        p.AdaNeeds = Blank(req.AdaNeeds);
    }

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    static Dictionary<string, string[]> Validate(MemberRequest req)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(req.FirstName)) errors["firstName"] = ["Enter a first name."];
        if (string.IsNullOrWhiteSpace(req.LastName)) errors["lastName"] = ["Enter a last name."];
        var today = FamilyReadModel.Today;
        if (req.DateOfBirth is not { } dob)
        {
            // Adults' birthdays don't place anyone, so they're optional.
            if (!req.IsAdult) errors["dateOfBirth"] = ["Enter a date of birth. We use it to work out their grade."];
        }
        else if (dob > today) errors["dateOfBirth"] = ["Date of birth can't be in the future."];
        else if (req.IsAdult && FamilyReadModel.AgeOn(dob, today) < 18) errors["dateOfBirth"] = ["Adults must be 18 or older. Add them as a child instead."];
        else if (!req.IsAdult && FamilyReadModel.AgeOn(dob, today) > 25) errors["dateOfBirth"] = ["That date of birth makes them an adult. Add them as an adult instead."];
        if (!req.IsAdult && req.Gender is null) errors["gender"] = ["Choose boy or girl. Camps group campers by it."];
        if (req.IsAdult && !string.IsNullOrWhiteSpace(req.Email) && !MailAddress.TryCreate(req.Email.Trim(), out _))
            errors["email"] = ["Enter an email like name@example.com."];
        foreach (var (key, value) in new[] { ("dietary", req.Dietary), ("allergies", req.Allergies), ("adaNeeds", req.AdaNeeds) })
            if (value?.Length > 400) errors[key] = ["Keep this under 400 characters."];
        return errors;
    }
}
