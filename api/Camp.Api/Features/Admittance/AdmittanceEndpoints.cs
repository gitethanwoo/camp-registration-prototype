using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Infrastructure;
using Camp.Api.Integrations;

namespace Camp.Api.Features.Admittance;

/// <summary>
/// R2 application, F7 status (family) and C6 review queue (staff). The household always comes
/// from the signed-in guest, never the request.
/// </summary>
public sealed class AdmittanceEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        var family = app.MapGroup("/api/admittance").RequireAuthorization(Policies.Family);

        family.MapGet("/sessions/{sessionId:int}/apply", async (int sessionId, CampDbContext db, CurrentUser me) =>
            await AdmittanceViews.ApplyContext(db, me.HouseholdId, me.Email, sessionId) is { } ctx ? Results.Ok(ctx) : Results.NotFound());

        family.MapPut("/sessions/{sessionId:int}/draft", (int sessionId, DraftRequest req, [AsParameters] Deps svc, CurrentUser me, CancellationToken ct) =>
            Run(async () =>
            {
                var saved = await svc.Service.SaveDraftAsync(me.HouseholdId, me.Email, sessionId, req, ct);
                return Results.Ok(new { saved.Id, saved.UpdatedAt });
            }));

        family.MapPost("/applications/{id:int}/submit", (int id, CardRequest req, [AsParameters] Deps svc, CurrentUser me, CancellationToken ct) =>
            Run(async () => { await svc.Service.SubmitAsync(me.HouseholdId, id, req, ct); return Results.Ok(); }));

        family.MapGet("/applications", async (CampDbContext db, CurrentUser me, TimeProvider clock) => Results.Ok(await AdmittanceViews.FamilyList(db, me.HouseholdId, clock)));

        // Another household's application is a 404, not a 403, so ids don't leak.
        family.MapGet("/applications/{id:int}", async (int id, CampDbContext db, CurrentUser me, TimeProvider clock) =>
            await AdmittanceViews.FamilyStatus(db, me.HouseholdId, id, clock) is { } view ? Results.Ok(view) : Results.NotFound());

        family.MapPost("/applications/{id:int}/reply", (int id, MessageRequest req, [AsParameters] Deps svc, CurrentUser me, CancellationToken ct) =>
            Run(async () => { await svc.Service.ReplyAsync(me.HouseholdId, id, req.Message, ct); return Results.Ok(); }));

        family.MapPost("/applications/{id:int}/reauthorize", (int id, CardRequest req, [AsParameters] Deps svc, CurrentUser me, CancellationToken ct) =>
            Run(async () => { await svc.Service.ReauthorizeAsync(me.HouseholdId, $"{me.Name} (guest)", id, req, ct); return Results.Ok(); }));

        // Reading the queue is open to console staff; deciding is CET work.
        var staff = app.MapGroup("/api/admin/admittance").RequireAuthorization(Policies.Staff);
        var cet = app.MapGroup("/api/admin/admittance").RequireAuthorization(Policies.Cet);

        staff.MapGet("/sessions", async (CampDbContext db, TimeProvider clock) => Results.Ok(await AdmittanceViews.StaffSessions(db, clock)));

        staff.MapGet("/sessions/{sessionId:int}/applications", async (int sessionId, CampDbContext db, TimeProvider clock) =>
            await AdmittanceViews.Queue(db, sessionId, clock) is { } queue ? Results.Ok(queue) : Results.NotFound());

        staff.MapGet("/applications/{id:int}", async (int id, CampDbContext db, TimeProvider clock) =>
            await AdmittanceViews.StaffDetail(db, id, clock) is { } detail ? Results.Ok(detail) : Results.NotFound());

        cet.MapPost("/applications/{id:int}/start-review", (int id, [AsParameters] Deps svc, StaffUser who, CancellationToken ct) =>
            Run(async () => { await svc.Service.StartReviewAsync(id, who.Actor, ct); return Results.Ok(); }));

        cet.MapPost("/applications/{id:int}/request-info", (int id, MessageRequest req, [AsParameters] Deps svc, StaffUser who, CancellationToken ct) =>
            Run(async () => { await svc.Service.RequestInfoAsync(id, who.Actor, req.Message, ct); return Results.Ok(); }));

        cet.MapPost("/applications/{id:int}/approve", (int id, [AsParameters] Deps svc, StaffUser who, CancellationToken ct) =>
            Run(async () => Results.Ok(new { Captured = await svc.Service.ApproveAsync(id, who.Actor, ct) })));

        cet.MapPost("/applications/{id:int}/decline", (int id, MessageRequest req, [AsParameters] Deps svc, StaffUser who, CancellationToken ct) =>
            Run(async () => { await svc.Service.DeclineAsync(id, who.Actor, req.Message, ct); return Results.Ok(); }));

        cet.MapPost("/applications/{id:int}/waitlist", (int id, [AsParameters] Deps svc, StaffUser who, CancellationToken ct) =>
            Run(async () => { await svc.Service.WaitlistAsync(id, who.Actor, ct); return Results.Ok(); }));
    }

    static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (AdmittanceException e) when (e.Errors is not null) { return Results.ValidationProblem(e.Errors, e.Message); }
        catch (AdmittanceException e) { return Results.Json(new { error = e.Message }, statusCode: e.Status); }
    }
}

/// <summary>
/// The service's dependencies, bound per request. Building the service here keeps the slice out of
/// Program.cs, which is where DI registrations would otherwise go.
/// </summary>
internal readonly record struct Deps(CampDbContext Db, IPaymentGateway Gateway, IAuditLog Audit, TimeProvider Clock)
{
    public AdmittanceService Service => new(Db, Gateway, Audit, Clock);
}
