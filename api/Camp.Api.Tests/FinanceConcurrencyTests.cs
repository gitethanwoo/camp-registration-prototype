using System.Net;
using System.Net.Http.Json;
using Camp.Api.Domain;
using Camp.Api.Features.Finance;
using Camp.Api.Features.Polish;
using Microsoft.EntityFrameworkCore;

namespace Camp.Api.Tests;

/// <summary>
/// Slice 6: FN2 resolve under concurrency. A class of its own so the extra settlement lines it adds
/// live in a throwaway database and don't disturb the counts <see cref="FinanceTests"/> asserts.
/// </summary>
public class FinanceConcurrencyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Two_lines_matched_to_one_order_at_once_cannot_overpay_it()
    {
        var code = await factory.WithDb(db => db.Orders
            .Where(o => o.Household.Email == FinanceSeed.MitchellEmail)
            .Select(o => o.ConfirmationCode).FirstAsync());
        var owed = await Balance(code);
        // Each line alone fits the balance; both together would overpay it.
        var each = owed / 2 + 5000;
        Assert.True(each <= owed && each * 2 > owed);

        var ids = await factory.WithDb(async db =>
        {
            var batch = await db.Set<SettlementBatch>().OrderByDescending(b => b.SettledOn).FirstAsync();
            var lines = Enumerable.Range(1, 2).Select(i => new SettlementLine
            {
                BatchId = batch.Id,
                Kind = SettlementLineKind.Payment,
                ProcessorRef = $"fsv_race{i}",
                TransactedAt = factory.Clock.UtcNow(),
                AmountCents = each,
                Description = "Virtual terminal payment",
                CardholderName = "Dana Mitchell",
                Status = SettlementLineStatus.Unmatched,
                UnmatchedReason = "No registration reference.",
            }).ToList();
            db.AddRange(lines);
            await db.SaveChangesAsync();
            return lines.Select(l => l.Id).ToArray();
        });

        var marcus = await factory.SignInAsStaff("finance", "Marcus Reed");
        var results = await Task.WhenAll(ids.Select(id => marcus.PostAsJsonAsync(
            $"/api/admin/finance/settlement-lines/{id}/resolve",
            new { resolution = "MatchedToRegistration", code, note = "Race test." })));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.BadRequest);
        Assert.Equal(owed - each, await Balance(code));
        Assert.Equal(1, await factory.WithDb(db => db.Set<SettlementLine>().CountAsync(l => ids.Contains(l.Id) && l.Status == SettlementLineStatus.Resolved)));
    }

    Task<int> Balance(string code) =>
        factory.WithDb(db => db.Registrations.Where(r => r.Order!.ConfirmationCode == code && r.Status != RegistrationStatus.Cancelled).SumAsync(r => r.PriceCents - r.DiscountCents - r.PaidCents));
}
