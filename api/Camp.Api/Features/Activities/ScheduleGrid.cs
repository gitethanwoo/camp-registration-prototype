using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Features.Ops;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Activities;

/// <summary>One period of "Assign from preferences": the camper's ranked slots and the one the plan gives them (null: all full).</summary>
internal sealed record PlannedPlace(int RegistrationId, int Period, List<SlotView> Ranked, SlotView? Slot);

/// <summary>O4's view of one block: its slots, the confirmed campers in its grades, their choices and places.</summary>
internal sealed class ScheduleGrid
{
    public required Session Session { get; init; }
    public required List<ActivityBlock> Blocks { get; init; }
    public required ActivityBlock Block { get; init; }
    public required List<Camper> Campers { get; init; }
    public required Dictionary<int, int> BlockCounts { get; init; }
    public required List<SlotView> Slots { get; init; }
    public required Dictionary<int, string> Images { get; init; }
    public required List<ActivityAssignment> Places { get; init; }
    public required List<ActivityPreference> Choices { get; init; }

    public static async Task<ScheduleGrid?> LoadAsync(CampDbContext db, int sessionId, int? blockId, CancellationToken ct)
    {
        var roster = await OpsReadModel.LoadAsync(db, sessionId, ct);
        if (roster is null) return null;
        var blocks = await db.Set<ActivityBlock>().AsNoTracking().Where(b => b.SessionId == sessionId).OrderBy(b => b.SortOrder).ToListAsync(ct);
        var block = blocks.FirstOrDefault(b => b.Id == blockId) ?? blocks.FirstOrDefault();
        if (block is null) return null;
        var campers = roster.Campers.Where(c => c.Grade >= block.GradeMin && c.Grade <= block.GradeMax).ToList();
        var ids = campers.Select(c => c.RegistrationId).ToList();
        var slots = await ActivityRules.SlotsAsync(db, block.Id, null, ct);
        var slotIds = slots.Select(s => s.SlotId).ToList();
        return new ScheduleGrid
        {
            Session = roster.Session,
            Blocks = blocks,
            Block = block,
            Campers = campers,
            BlockCounts = blocks.ToDictionary(b => b.Id, b => roster.Campers.Count(c => c.Grade >= b.GradeMin && c.Grade <= b.GradeMax)),
            Slots = slots,
            Images = await db.Set<Activity>().AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.ImageUrl, ct),
            // Places of this block's campers, plus anyone else sitting in this block's slots (O4 must see them to count them).
            Places = await db.Set<ActivityAssignment>().AsNoTracking()
                .Where(a => a.SessionId == sessionId && (ids.Contains(a.RegistrationId) || slotIds.Contains(a.SlotId))).ToListAsync(ct),
            Choices = await db.Set<ActivityPreference>().AsNoTracking().Where(p => ids.Contains(p.RegistrationId)).ToListAsync(ct),
        };
    }

    /// <summary>
    /// Fills each period a camper chose for but has no place in, first come first served, with the
    /// highest-ranked choice that still has room. Campers who haven't chosen are left for their family.
    /// </summary>
    public List<PlannedPlace> PlanFromPreferences()
    {
        var left = Slots.ToDictionary(s => s.SlotId, s => s.Capacity - s.Assigned);
        var plan = new List<PlannedPlace>();
        foreach (var c in Campers.OrderBy(c => c.CreatedAt).ThenBy(c => c.RegistrationId))
            for (var period = 1; period <= ActivityPeriods.All.Length; period++)
            {
                if (Places.Any(p => p.RegistrationId == c.RegistrationId && p.Period == period)) continue;
                var ranked = Choices.Where(p => p.RegistrationId == c.RegistrationId && p.Period == period).OrderBy(p => p.Rank)
                    .Select(p => Slots.FirstOrDefault(s => s.ActivityId == p.ActivityId && s.Period == period && c.Grade >= s.GradeMin && c.Grade <= s.GradeMax))
                    .OfType<SlotView>().ToList();
                if (ranked.Count == 0) continue;
                var slot = ranked.FirstOrDefault(s => left[s.SlotId] > 0);
                if (slot is not null) left[slot.SlotId]--;
                plan.Add(new PlannedPlace(c.RegistrationId, period, ranked, slot));
            }
        return plan;
    }

    public object View()
    {
        var campers = Campers.ToDictionary(c => c.RegistrationId);
        var bySlot = Places.GroupBy(p => p.SlotId).ToDictionary(g => g.Key, g => g.ToList());
        var slot = Slots.ToDictionary(s => s.SlotId);
        string Name(int registrationId) => campers.TryGetValue(registrationId, out var c) ? c.Name : "A camper outside this block";

        var conflicts = new List<object>();
        // A camper can't hold two places in one period (a unique index), so conflicts are about slots and grades.
        foreach (var s in Slots.Where(s => bySlot.GetValueOrDefault(s.SlotId)?.Count > s.Capacity))
            conflicts.Add(new
            {
                Kind = "OverCapacity",
                RegistrationId = (int?)null,
                Camper = (string?)null,
                Grade = (int?)null,
                s.Period,
                SlotIds = new[] { s.SlotId },
                Message = $"{s.Name} in Period {s.Period} has {bySlot[s.SlotId].Count} campers for {s.Capacity} places.",
            });
        foreach (var p in Places.Where(p => slot.ContainsKey(p.SlotId) && campers.TryGetValue(p.RegistrationId, out var c) && (c.Grade < slot[p.SlotId].GradeMin || c.Grade > slot[p.SlotId].GradeMax)))
            conflicts.Add(new
            {
                Kind = "OutsideGrades",
                p.RegistrationId,
                Camper = Name(p.RegistrationId),
                Grade = (int?)campers[p.RegistrationId].Grade,
                p.Period,
                SlotIds = new[] { p.SlotId },
                Message = $"{Name(p.RegistrationId)} (grade {campers[p.RegistrationId].Grade}) is in {slot[p.SlotId].Name}, which is for grades {slot[p.SlotId].GradeMin}–{slot[p.SlotId].GradeMax}.",
            });

        var chose = Choices.Select(c => (c.RegistrationId, c.Period)).ToHashSet();
        var plan = PlanFromPreferences();
        return new
        {
            Session = new { Session.Id, Session.Name, Session.StartDate, Session.EndDate },
            Blocks = Blocks.Select(b => new { b.Id, b.Name, Grades = $"Grades {b.GradeMin}–{b.GradeMax}", Campers = BlockCounts[b.Id] }),
            Block = new { Block.Id, Block.Name, Grades = $"Grades {Block.GradeMin}–{Block.GradeMax}", Campers = Campers.Count },
            Periods = ActivityPeriods.All.Select(p =>
            {
                var placed = Campers.Count(c => Places.Any(x => x.RegistrationId == c.RegistrationId && x.Period == p.Number));
                var waiting = Campers.Count(c => !Places.Any(x => x.RegistrationId == c.RegistrationId && x.Period == p.Number) && chose.Contains((c.RegistrationId, p.Number)));
                return new
                {
                    Period = p.Number,
                    p.Time,
                    Capacity = Slots.Where(s => s.Period == p.Number).Sum(s => s.Capacity),
                    Placed = placed,
                    Waiting = waiting,
                    NotChosen = Campers.Count - placed - waiting,
                };
            }),
            Rows = Slots.GroupBy(s => new { s.ActivityId, s.Name, s.GradeMin, s.GradeMax }).Select(g => new
            {
                g.Key.ActivityId,
                g.Key.Name,
                g.Key.GradeMin,
                g.Key.GradeMax,
                ImageUrl = Images.GetValueOrDefault(g.Key.ActivityId),
                Grades = $"Grades {g.Key.GradeMin}–{g.Key.GradeMax}",
                Cells = g.OrderBy(s => s.Period).Select(s =>
                {
                    var here = bySlot.GetValueOrDefault(s.SlotId) ?? [];
                    return new
                    {
                        s.SlotId,
                        s.Period,
                        s.Capacity,
                        Assigned = here.Count,
                        Status = here.Count > s.Capacity ? "Over capacity" : here.Count == s.Capacity ? "Full" : "Open",
                        s.Instructor,
                        s.Space,
                        Campers = here.Select(p => new
                        {
                            p.RegistrationId,
                            Name = Name(p.RegistrationId),
                            Grade = campers.GetValueOrDefault(p.RegistrationId)?.Grade,
                            Choices = Choices.Where(c => c.RegistrationId == p.RegistrationId && c.Period == s.Period).OrderBy(c => c.Rank)
                                .Select(c => Slots.FirstOrDefault(x => x.ActivityId == c.ActivityId)?.Name ?? ""),
                        }).OrderBy(x => x.Name),
                    };
                }),
            }),
            Conflicts = conflicts,
            Pending = new { Campers = plan.Select(p => p.RegistrationId).Distinct().Count(), Places = plan.Count(p => p.Slot is not null), NoRoom = plan.Count(p => p.Slot is null) },
        };
    }
}
