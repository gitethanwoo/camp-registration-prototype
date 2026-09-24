using Camp.Api.Domain;
using Camp.Api.Features;

namespace Camp.Api.Tests;

public class PricingTests
{
    static readonly Session DayCamp = new()
    {
        StartDate = new(2028, 6, 12),
        PriceCents = 32500,
        DepositCents = 10000,
        PlanInstallments = 3,
        BalanceDueDate = new(2028, 5, 1),
    };

    static readonly Person[] Kids =
    [
        new() { Id = 1, FirstName = "Avery", LastName = "Johnson" },
        new() { Id = 2, FirstName = "Mia", LastName = "Johnson" },
    ];

    [Fact]
    public void Johnson_plan_matches_the_canonical_dataset()
    {
        var q = Pricing.Build(DayCamp, Kids, PaymentOption.Plan, null, null);

        Assert.Equal(65000, q.TotalCents);
        Assert.Equal(20000, q.DueTodayCents);
        Assert.Equal([15000, 15000, 15000], q.Schedule.Skip(1).Select(s => s.AmountCents));
        Assert.Equal(q.TotalCents, q.Schedule.Sum(s => s.AmountCents));
        // Every installment lands before camp starts.
        Assert.All(q.Schedule.Skip(1), s => Assert.True(s.DueDate < DayCamp.StartDate));
    }

    [Fact]
    public void Plan_installments_absorb_rounding_and_still_sum_to_total()
    {
        var discount = new DiscountCode { Code = "EARLYBIRD", Kind = DiscountKind.Percent, Value = 10, Status = DiscountStatus.Approved };
        var q = Pricing.Build(DayCamp, Kids, PaymentOption.Plan, discount, "EARLYBIRD");

        Assert.Equal(58500, q.TotalCents);
        Assert.Equal(q.TotalCents, q.Schedule.Sum(s => s.AmountCents));
    }

    [Fact]
    public void Pending_discount_looks_exactly_like_an_invalid_one()
    {
        var pending = new DiscountCode { Code = "SUMMERFUN", Kind = DiscountKind.Flat, Value = 5000, Status = DiscountStatus.PendingApproval };
        var pendingQuote = Pricing.Build(DayCamp, Kids, PaymentOption.Full, pending, "SUMMERFUN");
        var bogusQuote = Pricing.Build(DayCamp, Kids, PaymentOption.Full, null, "NOPE");

        Assert.Equal(0, pendingQuote.DiscountCents);
        Assert.Equal(bogusQuote.DiscountError, pendingQuote.DiscountError);
    }

    [Fact]
    public void Discount_can_never_exceed_the_price()
    {
        var huge = new DiscountCode { Code = "X", Kind = DiscountKind.Flat, Value = 999_999, Status = DiscountStatus.Approved };
        var q = Pricing.Build(DayCamp, Kids, PaymentOption.Full, huge, "X");

        Assert.Equal(0, q.TotalCents);
        Assert.All(q.Lines, l => Assert.True(l.DiscountCents <= l.PriceCents));
    }

    [Theory]
    [InlineData(2017, 3, 4, 6)]   // Avery: Grade 6 in fall 2028
    [InlineData(2019, 8, 19, 4)]  // Mia: Grade 4 in fall 2028
    [InlineData(2017, 9, 1, 6)]   // born on the cutoff: turns 11 on Sept 1
    [InlineData(2017, 9, 2, 5)]   // a day later: one grade lower
    public void Grade_uses_a_September_1_cutoff(int y, int m, int d, int expected) =>
        Assert.Equal(expected, Eligibility.GradeFor(new DateOnly(y, m, d), new DateOnly(2028, 6, 12)));
}
