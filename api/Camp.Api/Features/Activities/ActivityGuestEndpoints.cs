using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Polish;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Activities;

public record ActivityChoicesInput(List<ActivityChoice> Choices);
public record CabinmateCheckInput(string Name, string Contact);

/// <summary>
/// Guest side: P3 activity detail, the R4 choices for campers in the wizard, the R5 match check, and
/// choosing (or changing) activities after registering from the family home (F1).
/// </summary>
public sealed class ActivityGuestEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        // P3 · public: what it is, who it's for, and slots left per period in each block of the session.
        app.MapGet("/api/activities/{id:int}", async (int id, int? sessionId, CampDbContext db, CancellationToken ct) =>
        {
            var a = await db.Set<Activity>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
            if (a is null) return Results.NotFound();
            var slots = await db.Set<ActivitySlot>().AsNoTracking().Where(s => s.ActivityId == id && (sessionId == null || s.SessionId == sessionId))
                .Join(db.Set<ActivityBlock>(), s => s.BlockId, b => b.Id, (s, b) => new { s, b })
                .Join(db.Sessions, x => x.s.SessionId, s => s.Id, (x, session) => new { x.s, x.b, Session = session.Program.Name + " · " + session.Name, session.StartDate })
                .OrderBy(x => x.StartDate).ThenBy(x => x.b.SortOrder).ThenBy(x => x.s.Period).ToListAsync(ct);
            return Results.Ok(new
            {
                a.Id,
                a.Name,
                a.Category,
                a.Description,
                a.WhatToBring,
                a.ImageUrl,
                a.GradeMin,
                a.GradeMax,
                GradeLabel = ActivityCatalogEndpoints.Grades(a),
                a.StaffRatio,
                a.Instructor,
                a.Space,
                Schedule = slots.GroupBy(x => new { x.b.Id, x.b.Name, x.b.GradeMin, x.b.GradeMax, x.Session }).Select(g => new
                {
                    BlockId = g.Key.Id,
                    Block = g.Key.Name,
                    Grades = $"Grades {g.Key.GradeMin}–{g.Key.GradeMax}",
                    g.Key.Session,
                    Periods = g.Select(x => new { x.s.Period, Time = ActivityPeriods.Time(x.s.Period), x.s.Capacity, Remaining = Math.Max(0, x.s.Capacity - x.s.Assigned) }),
                }),
            });
        });

        var guest = app.MapGroup("/api/sessions").RequireAuthorization(Policies.Family);
        var family = app.MapGroup("/api/family").RequireAuthorization(Policies.Family);

        // R4 · the activities each chosen camper can pick from, with live slots left. Polled while the step is open.
        guest.MapGet("/{id:int}/activity-options", async (int id, string? personIds, CampDbContext db, CurrentUser me, CancellationToken ct) =>
        {
            var session = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.Program.IsPublished, ct);
            if (session is null) return Results.NotFound();
            var ids = (personIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out var n) ? n : 0).ToList();
            var people = await db.People.AsNoTracking().Where(p => ids.Contains(p.Id) && p.HouseholdId == me.HouseholdId && !p.IsAdult).ToListAsync(ct);
            var campers = new List<object>();
            foreach (var p in people.OrderBy(p => ids.IndexOf(p.Id)))
            {
                var grade = Eligibility.GradeFor(p.DateOfBirth, session.StartDate);
                var block = await ActivityRules.BlockForAsync(db, id, grade, ct);
                var offered = block is null ? [] : await ActivityRules.SlotsAsync(db, block.Id, grade, ct);
                campers.Add(new { PersonId = p.Id, p.FirstName, Grade = grade, GradeLabel = Eligibility.GradeLabel(grade), Block = block?.Name, Periods = Periods(offered) });
            }
            return Results.Ok(new { CabinmateLimit = ActivityPeriods.CabinmateLimit, MaxRanks = ActivityPeriods.MaxRanks, Campers = campers });
        });

        // R5 · does this name and email or code find a camper in the session? Says only yes or no.
        guest.MapPost("/{id:int}/cabinmates/check", async (int id, CabinmateCheckInput input, CampDbContext db, CurrentUser me, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Contact))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["contact"] = ["Enter a name and a parent's email or code."] });
            var match = await Cabinmates.MatchAsync(db, id, me.HouseholdId, input.Name, input.Contact, ct);
            return Results.Ok(new { Matched = match is not null });
        });

        // F1 → choose activities after registering. The registration must be this household's.
        family.MapGet("/activities/{registrationId:int}", async (int registrationId, CampDbContext db, CurrentUser me, CancellationToken ct) =>
        {
            var reg = await FindRegistration(db, me, registrationId, ct);
            if (reg is null) return Results.NotFound();
            var block = await ActivityRules.BlockForAsync(db, reg.SessionId, reg.Grade, ct);
            if (block is null) return Results.NotFound();
            var offered = await ActivityRules.SlotsAsync(db, block.Id, reg.Grade, ct);
            var prefs = await db.Set<ActivityPreference>().AsNoTracking().Where(p => p.RegistrationId == reg.Id).ToListAsync(ct);
            var placed = await db.Set<ActivityAssignment>().AsNoTracking().Where(a => a.RegistrationId == reg.Id).ToListAsync(ct);
            return Results.Ok(new
            {
                RegistrationId = reg.Id,
                reg.PersonId,
                reg.Person.FirstName,
                reg.Grade,
                GradeLabel = Eligibility.GradeLabel(reg.Grade),
                Block = block.Name,
                Session = $"{reg.Session.Program.Name} · {reg.Session.Name}",
                reg.Session.StartDate,
                reg.Session.EndDate,
                Periods = Periods(offered),
                Choices = prefs.GroupBy(p => p.Period).Select(g => new { Period = g.Key, Ranked = g.OrderBy(p => p.Rank).Select(p => p.ActivityId) }),
                Placed = placed.Select(a => new { a.Period, offered.FirstOrDefault(o => o.SlotId == a.SlotId)?.ActivityId, Name = offered.FirstOrDefault(o => o.SlotId == a.SlotId)?.Name }),
            });
        });

        family.MapPut("/activities/{registrationId:int}", async (int registrationId, ActivityChoicesInput input, CampDbContext db, CurrentUser me, IAuditLog audit, TimeProvider clock, CancellationToken ct) =>
        {
            var reg = await FindRegistration(db, me, registrationId, ct);
            if (reg is null) return Results.NotFound();
            var block = await ActivityRules.BlockForAsync(db, reg.SessionId, reg.Grade, ct);
            if (block is null) return Results.NotFound();
            var offered = await ActivityRules.SlotsAsync(db, block.Id, reg.Grade, ct);
            var choices = input.Choices ?? [];
            var problems = ActivityRules.Check(reg.PersonId, reg.Person.FirstName, reg.Grade, offered, choices, null);
            if (choices.Count == 0) problems.Add(("choices", "Rank at least one activity."));
            if (problems.Count > 0)
                return Results.ValidationProblem(problems.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Select(p => p.Message).Distinct().ToArray()));

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var conflicts = await ActivityRules.ApplyAsync(db, reg.Id, reg.SessionId, reg.PersonId, reg.Person.FirstName, offered, choices, "Family", clock.UtcNow(), ct);
            if (conflicts.Count > 0)
            {
                await tx.RollbackAsync(ct);
                return new ActivityFullException(conflicts).ToResult();
            }
            audit.Record("activities.chosen", "Registration", reg.Id,
                $"{reg.Person.FullName}'s family chose activities for Period {string.Join(", ", choices.Select(c => c.Period).Order())}.");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { Saved = true });
        });
    }

    static Task<Registration?> FindRegistration(CampDbContext db, CurrentUser me, int id, CancellationToken ct) =>
        db.Registrations.Include(r => r.Person).Include(r => r.Session).ThenInclude(s => s.Program)
            .FirstOrDefaultAsync(r => r.Id == id && r.HouseholdId == me.HouseholdId && r.Status == RegistrationStatus.Confirmed, ct);

    static IEnumerable<object> Periods(List<SlotView> offered) =>
        ActivityPeriods.All.Select(p => new
        {
            Period = p.Number,
            p.Time,
            Options = offered.Where(o => o.Period == p.Number).Select(o => new { o.ActivityId, o.Name, o.Capacity, o.Remaining }),
        });
}
