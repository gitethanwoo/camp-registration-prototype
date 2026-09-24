using Camp.Api.Infrastructure;

namespace Camp.Api.Features.Polish;

/// <summary>The demo clock's current time, so the web app shows the same "today" as the API.</summary>
public sealed class ClockEndpoints : IEndpointModule
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder app)
    {
        // Anonymous on purpose: the public program pages need "today" before anyone signs in, and the
        // demo time isn't secret. offsetMs is demo time minus the server's real time.
        app.MapGet("/api/clock", (TimeProvider clock) =>
        {
            var now = clock.GetUtcNow();
            return Results.Ok(new { Now = now, OffsetMs = (long)(now - TimeProvider.System.GetUtcNow()).TotalMilliseconds });
        }).AllowAnonymous();
    }
}
