namespace Zharfa.Api.Tests.Purchasing.Reports;

using Zharfa.Api.Analytics;
using Zharfa.Api.Purchasing;
using Zharfa.Api.Purchasing.Reports;

/// <summary>
/// T007 (spec 004 FR-011, FR-014, research R4): trend windows. Pure: no database, no network.
/// </summary>
public class TrendWindowTests
{
    private static readonly JalaliDate Today = new(1405, 8, 25);

    private static JalaliPeriod Parse(string? period, string? value = null, string? from = null, string? to = null, JalaliDate? today = null)
    {
        var ok = JalaliPeriod.TryParse(period, value, from, to, today ?? Today, out var result, out var error);
        Assert.True(ok, error);
        return result!;
    }

    [Fact]
    public void Month_preset_trend_is_the_12_months_ending_with_the_selected_month()
    {
        var buckets = Parse("month", "1405-08", today: new JalaliDate(1406, 1, 1)).TrendMonths(profile: false);

        Assert.Equal(12, buckets.Count);
        Assert.Equal(new JalaliMonth(1404, 9), buckets[0].Month);
        Assert.Equal(new JalaliMonth(1405, 8), buckets[11].Month);
        Assert.All(buckets, b => Assert.False(b.Partial));
    }

    [Fact]
    public void Quarter_preset_trend_ends_with_the_quarters_last_month()
    {
        var buckets = Parse("quarter", "1405-Q1", today: new JalaliDate(1406, 1, 1)).TrendMonths(profile: false);

        Assert.Equal(12, buckets.Count);
        Assert.Equal(new JalaliMonth(1404, 4), buckets[0].Month);
        Assert.Equal(new JalaliMonth(1405, 3), buckets[11].Month);
    }

    [Fact]
    public void Year_preset_trend_is_exactly_its_own_12_months()
    {
        var buckets = Parse("year", "1405", today: new JalaliDate(1406, 1, 1)).TrendMonths(profile: false);

        Assert.Equal(12, buckets.Count);
        Assert.Equal(new JalaliMonth(1405, 1), buckets[0].Month);
        Assert.Equal(new JalaliMonth(1405, 12), buckets[11].Month);
    }

    [Fact]
    public void Custom_trend_flags_the_clipped_first_and_last_months_as_partial()
    {
        var buckets = Parse("custom", from: "1405-07-10", to: "1405-08-24").TrendMonths(profile: false);

        Assert.Equal(2, buckets.Count);
        Assert.Equal(new JalaliMonth(1405, 7), buckets[0].Month);
        Assert.True(buckets[0].Partial);
        Assert.Equal(new JalaliMonth(1405, 8), buckets[1].Month);
        Assert.True(buckets[1].Partial);
    }

    [Fact]
    public void Custom_trend_over_whole_months_has_no_partial_bucket()
    {
        var buckets = Parse("custom", from: "1405-07-01", to: "1405-08-30").TrendMonths(profile: false);

        Assert.Equal(2, buckets.Count);
        Assert.All(buckets, b => Assert.False(b.Partial));
    }

    [Fact]
    public void Profile_trend_of_a_custom_range_is_12_months_ending_with_its_last_month()
    {
        var buckets = Parse("custom", from: "1405-07-10", to: "1405-08-24").TrendMonths(profile: true);

        Assert.Equal(12, buckets.Count);
        Assert.Equal(new JalaliMonth(1404, 9), buckets[0].Month);
        Assert.Equal(new JalaliMonth(1405, 8), buckets[11].Month);
        Assert.All(buckets, b => Assert.False(b.Partial));
    }

    [Fact]
    public void A_1098_day_custom_range_has_at_most_38_month_buckets()
    {
        // Three Jalali years is about 36 months; a range starting late in a month touches one extra bucket.
        var today = new JalaliDate(1410, 1, 1);
        var endJdn = JalaliDate.JalaliToJdn(1404, 1, 30) + JalaliPeriod.MaxCustomDays - 1;
        var (y, m, d) = JalaliDate.JdnToJalali(endJdn);
        var period = Parse("custom", from: "1404-01-30", to: $"{y:D4}-{m:D2}-{d:D2}", today: today);

        var buckets = period.TrendMonths(profile: false);

        Assert.InRange(buckets.Count, 36, 38);
    }
}
