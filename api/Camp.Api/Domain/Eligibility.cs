namespace Camp.Api.Domain;

public static class Eligibility
{
    /// <summary>
    /// Grade the child enters in the fall of the session year (Sept 1 cutoff).
    /// Avery (Mar 4, 2017) is Grade 6 and Mia (Aug 19, 2019) is Grade 4 for summer 2028.
    /// </summary>
    public static int GradeFor(DateOnly dob, DateOnly sessionStart)
    {
        var cutoff = new DateOnly(sessionStart.Year, 9, 1);
        var age = cutoff.Year - dob.Year - (cutoff < dob.AddYears(cutoff.Year - dob.Year) ? 1 : 0);
        return age - 5;
    }

    public static string GradeLabel(int grade) => grade switch
    {
        0 => "Kindergarten",
        < 0 => "Pre-K",
        _ => $"Grade {grade}",
    };

    public static (CapacityPool? Pool, string? Reason) FindPool(Person person, Session session)
    {
        if (person.IsAdult) return (null, "Adults can't register as campers.");
        var grade = GradeFor(person.DateOfBirth, session.StartDate);
        var pool = session.Pools
            .Where(p => grade >= p.GradeMin && grade <= p.GradeMax)
            .FirstOrDefault(p => p.Gender is null || p.Gender == person.Gender);
        if (pool is not null) return (pool, null);

        var min = session.Pools.Min(p => p.GradeMin);
        var max = session.Pools.Max(p => p.GradeMax);
        return (null, $"{person.FirstName} is {GradeLabel(grade).ToLower()} in fall {session.StartDate.Year}; this session is for grades {min}–{max}.");
    }
}
