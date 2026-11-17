namespace Zharfa.Api.Tests.Purchasing;

using System.Linq;
using System.Numerics;
using Zharfa.Api.Purchasing;

/// <summary>
/// T048 (spec 003 US4, FR-014): pro-rata allocation of a document discount for basis lineAndDocument.
/// The rules are mirrored by mabna.fn_purchase_line_amounts; the expected values below were also checked
/// against that SQL on a throwaway PostgreSQL 18 database.
/// </summary>
public class DiscountAllocationTests
{
    [Fact]
    public void Zero_discount_allocates_nothing()
    {
        var shares = DiscountAllocator.ShareOf([("L1", 100m), ("L2", 180m)], 0m);

        Assert.Equal(new[] { 0m, 0m }, shares);
    }

    [Fact]
    public void Discount_is_split_pro_rata_by_line_amount_at_four_decimals()
    {
        // 10 × 100/280 = 3.5714 and 10 × 180/280 = 6.4286; the shares sum to the discount exactly.
        var shares = DiscountAllocator.ShareOf([("L1", 100m), ("L2", 180m)], 10m);

        Assert.Equal(new[] { 3.5714m, 6.4286m }, shares);
        Assert.Equal(10m, shares.Sum());
    }

    [Fact]
    public void Rounding_remainder_goes_to_the_largest_line()
    {
        // Three equal lines of 1 share 1/3 each: 0.3333 × 3 = 0.9999, so the largest (first) line takes the 0.0001.
        var shares = DiscountAllocator.ShareOf([("L1", 1m), ("L2", 1m), ("L3", 1m)], 1m);

        Assert.Equal(new[] { 0.3334m, 0.3333m, 0.3333m }, shares);
        Assert.Equal(1m, shares.Sum());
    }

    [Fact]
    public void A_tie_for_largest_line_goes_to_the_lowest_key()
    {
        var shares = DiscountAllocator.ShareOf([("L3", 1m), ("L1", 1m), ("L2", 1m)], 0.0001m);

        // Only the remainder moves: the lowest key (L1, at index 1) takes the single unit.
        Assert.Equal(new[] { 0m, 0.0001m, 0m }, shares);
    }

    [Fact]
    public void The_remainder_follows_the_largest_amount_not_the_first_line()
    {
        // Amounts 1, 5 and 2 (Rial). The largest is the second line; its share takes the rounding remainder.
        var shares = DiscountAllocator.ShareOf([("L1", 1m), ("L2", 5m), ("L3", 2m)], 0.0003m);

        Assert.Equal(0.0003m, shares.Sum());
        Assert.True(shares[1] >= shares[0] && shares[1] >= shares[2]);
    }

    [Fact]
    public void Zero_line_total_gives_the_whole_discount_to_the_lowest_key()
    {
        var shares = DiscountAllocator.ShareOf([("B", 0m), ("A", 0m)], 2.5m);

        Assert.Equal(new[] { 0m, 2.5m }, shares);
    }

    [Fact]
    public void Shares_always_sum_to_the_discount_exactly()
    {
        // A deterministic spread of amounts, discounts and line counts (no randomness, so failures reproduce).
        var seed = 12345u;
        for (var round = 0; round < 500; round++)
        {
            var count = 1 + (int)(Next(ref seed) % 7);
            var lines = Enumerable.Range(0, count)
                .Select(i => ($"K{Next(ref seed) % 1000:D3}-{i}", (decimal)(Next(ref seed) % 2_000_000) / 10000m))
                .ToList();
            var discount = (decimal)(Next(ref seed) % 500_000) / 10000m;

            var shares = DiscountAllocator.ShareOf(lines, discount);

            Assert.Equal(discount, shares.Sum());
            Assert.All(shares, share => Assert.Equal(share, decimal.Round(share, 4)));
        }
    }

    [Fact]
    public void Negative_line_amounts_are_allocated_too_and_still_sum_exactly()
    {
        var shares = DiscountAllocator.ShareOf([("L1", 100m), ("L2", -20m)], 4m);

        Assert.Equal(4m, shares.Sum());
    }

    [Theory]
    [InlineData(5, 10, 1)]
    [InlineData(-5, 10, -1)]
    [InlineData(4, 10, 0)]
    [InlineData(-4, 10, 0)]
    [InlineData(15, 10, 2)]
    public void Ratio_rounds_half_away_from_zero(long numerator, long denominator, long expected)
    {
        Assert.Equal(new BigInteger(expected), DiscountAllocator.RoundedRatio(numerator, denominator));
    }

    [Fact]
    public void Amounts_round_half_away_from_zero_at_four_decimals_before_allocation()
    {
        // numeric(24,4) storage rounds 0.00005 to 0.0001 (half away from zero), so the allocator does too.
        Assert.Equal(BigInteger.One, DiscountAllocator.ToUnits(0.00005m));
        Assert.Equal(new BigInteger(-1), DiscountAllocator.ToUnits(-0.00005m));
    }

    private static uint Next(ref uint state)
    {
        state = unchecked(state * 1664525u + 1013904223u);
        return state >> 8;
    }
}
