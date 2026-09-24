using System.Diagnostics;

namespace Camp.Api.Features.Polish;

/// <summary>
/// The app's clock for the demo: "now" starts at a fixed instant in the camp season and moves forward
/// with real time, so a presenter sees March 2028 while timestamps still tick. Every read of the
/// current time goes through the injected <see cref="TimeProvider"/>, never <c>DateTime.UtcNow</c>.
/// </summary>
public sealed class DemoClock(DateTimeOffset anchor) : TimeProvider
{
    /// <summary>Default demo "now": the middle of registration season for the 2028 camps.</summary>
    public static readonly DateTimeOffset DefaultAnchor = new(2028, 3, 2, 15, 0, 0, TimeSpan.Zero);

    readonly long _started = Stopwatch.GetTimestamp();

    /// <summary>The instant the clock started from.</summary>
    public DateTimeOffset Anchor { get; } = anchor;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => Anchor + Stopwatch.GetElapsedTime(_started);

    /// <summary>Reads <c>Demo:Now</c> (ISO 8601); missing or unparseable falls back to <see cref="DefaultAnchor"/>.</summary>
    public static DemoClock FromConfiguration(IConfiguration config) =>
        new(DateTimeOffset.TryParse(config["Demo:Now"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
            ? at.ToUniversalTime()
            : DefaultAnchor);
}

/// <summary>Shorthands for the two ways the app reads the clock.</summary>
public static class ClockExtensions
{
    /// <summary>Current UTC time as a <see cref="DateTime"/> with <see cref="DateTimeKind.Utc"/>, the type entities store.</summary>
    public static DateTime UtcNow(this TimeProvider clock) => clock.GetUtcNow().UtcDateTime;

    /// <summary>Today's date in UTC, the day every date rule in the app compares against.</summary>
    public static DateOnly Today(this TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
}
