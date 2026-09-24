using System.Text.Json;
using Camp.Api.Auth;
using Camp.Api.Data;
using Camp.Api.Domain;
using Camp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Features.Forms;

/// <summary>One answer as a reader sees it. Values are raw; the page formats dates, numbers and picks by type.</summary>
public sealed record AnswerView(string Key, string Label, string Type, string Value);

/// <summary>
/// Registration answers for staff (C3) and for the family that gave them (F5). Answers given
/// through a K6 form carry their version; registrations from before K6 show their stored answers
/// with the program's original question labels.
/// </summary>
public sealed class FormAnswersEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/forms/registrations/{id:int}/answers", async (int id, CampDbContext db, CancellationToken ct) =>
        {
            var r = await db.Registrations.AsNoTracking().Include(x => x.Session).ThenInclude(s => s.Program).ThenInclude(p => p.Questions)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            var answers = r.OrderId is { } orderId
                ? await db.Set<FormAnswer>().AsNoTracking().Include(a => a.Question)
                    .Where(a => a.OrderId == orderId && (a.RegistrationId == id || a.RegistrationId == null)).ToListAsync(ct)
                : [];
            if (answers.Count == 0) return Results.Ok(Legacy(r));
            var version = await VersionNumber(db, answers[0].FormVersionId, ct);
            return Results.Ok(new
            {
                FormVersion = version,
                Household = Views(answers.Where(a => a.RegistrationId is null)),
                Participant = Views(answers.Where(a => a.RegistrationId == id)),
            });
        }).RequireAuthorization(Policies.Staff);

        app.MapGet("/api/family/forms/orders/{code}/answers", async (string code, CampDbContext db, CurrentUser me, CancellationToken ct) =>
        {
            // The household comes from the session, never the request: another family's code reads as not found.
            var order = await db.Orders.AsNoTracking()
                .Include(o => o.Registrations).ThenInclude(r => r.Person)
                .Include(o => o.Session).ThenInclude(s => s.Program).ThenInclude(p => p.Questions)
                .FirstOrDefaultAsync(o => o.ConfirmationCode == code && o.HouseholdId == me.HouseholdId, ct);
            if (order is null) return Results.NotFound();
            var regs = order.Registrations.Where(r => r.Status != RegistrationStatus.Cancelled).OrderBy(r => r.Person.DateOfBirth).ToList();
            var answers = await db.Set<FormAnswer>().AsNoTracking().Include(a => a.Question).Where(a => a.OrderId == order.Id).ToListAsync(ct);
            if (answers.Count == 0)
            {
                var legacy = regs.Select(r => new { Registration = r, View = Legacy(r) }).ToList();
                return Results.Ok(new
                {
                    FormVersion = (int?)null,
                    Household = legacy.FirstOrDefault()?.View.Household ?? [],
                    Participants = legacy.Select(x => new { RegistrationId = x.Registration.Id, x.Registration.Person.FirstName, Answers = x.View.Participant }),
                });
            }
            return Results.Ok(new
            {
                FormVersion = await VersionNumber(db, answers[0].FormVersionId, ct),
                Household = Views(answers.Where(a => a.RegistrationId is null)),
                Participants = regs.Select(r => new { RegistrationId = r.Id, r.Person.FirstName, Answers = Views(answers.Where(a => a.RegistrationId == r.Id)) }),
            });
        }).RequireAuthorization(Policies.Family);
    }

    static Task<int> VersionNumber(CampDbContext db, int formVersionId, CancellationToken ct) =>
        db.Set<FormVersion>().Where(v => v.Id == formVersionId).Select(v => v.Version).FirstAsync(ct);

    static List<AnswerView> Views(IEnumerable<FormAnswer> answers) =>
        answers.OrderBy(a => a.Question.SortOrder).Select(a => new AnswerView(a.Question.Key, a.Question.Label, a.Question.Type.ToString(), a.Value)).ToList();

    sealed record LegacyView(int? FormVersion, List<AnswerView> Household, List<AnswerView> Participant);

    /// <summary>Answers stored before K6 forms: the JSON on the registration, labelled by the program's original questions.</summary>
    static LegacyView Legacy(Registration r)
    {
        Dictionary<string, string> stored;
        try { stored = JsonSerializer.Deserialize<Dictionary<string, string>>(r.AnswersJson) ?? []; }
        catch (JsonException) { stored = []; }
        var questions = r.Session.Program.Questions.OrderBy(q => q.SortOrder).ToList();
        AnswerView View(Question q) => new(q.Key, q.Label, FormViews.FromLegacy(q).Type.ToString(), stored[q.Key]);
        var known = questions.Where(q => stored.ContainsKey(q.Key) && !string.IsNullOrWhiteSpace(stored[q.Key])).ToList();
        var unknown = stored.Where(kv => questions.All(q => q.Key != kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => new AnswerView(kv.Key, kv.Key, nameof(FormQuestionType.ShortText), kv.Value));
        return new LegacyView(
            null,
            known.Where(q => q.Scope == QuestionScope.Household).Select(View).ToList(),
            [.. known.Where(q => q.Scope == QuestionScope.Participant).Select(View), .. unknown]);
    }
}
