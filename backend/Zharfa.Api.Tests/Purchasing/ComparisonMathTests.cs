namespace Zharfa.Api.Tests.Purchasing;

using Zharfa.Api.Purchasing;

/// <summary>
/// T057 (spec 003 US5, FR-025/FR-026/FR-027): Overview comparison arithmetic and Jalali month handling.
/// Pure: no database, no network.
/// </summary>
public class ComparisonMathTests
{
    [Fact]
    public void Delta_is_current_minus_previous_exactly()
    {
        Assert.Equal(150_000_000m, ComparisonMath.Delta(1_250_000_000m, 1_100_000_000m));
        Assert.Equal(-40m, ComparisonMath.Delta(-40m, 0m));
    }

    [Fact]
    public void Percent_is_rounded_to_two_decimals_as_in_the_contract_example()
    {
        // (1 250 000 000 − 1 100 000 000) / 1 100 000 000 = 13.636… → 13.64 (contracts/api.md §3)
        var percent = ComparisonMath.Percent(1_250_000_000m, 1_100_000_000m);
        Assert.Equal(13.64m, percent);
        Assert.Equal("13.64", ComparisonMath.Percent2(percent!.Value));
    }

    [Fact]
    public void Percent_rounds_a_tie_half_away_from_zero()
    {
        // 0.125 exactly at the second decimal: away from zero gives 0.13 (not banker's 0.12).
        Assert.Equal(0.13m, ComparisonMath.Percent(8.01m, 8m));
    }

    [Fact]
    public void Percent_is_negative_when_net_falls()
    {
        Assert.Equal(-33.33m, ComparisonMath.Percent(2m, 3m));
        Assert.Equal(-100m, ComparisonMath.Percent(0m, 3m));
    }

    [Fact]
    public void Percent_is_null_when_previous_net_is_zero()
    {
        Assert.Null(ComparisonMath.Percent(150m, 0m));
        Assert.Null(ComparisonMath.Percent(-10m, 0m));
    }

    [Fact]
    public void Previous_month_crosses_the_year_boundary()
    {
        Assert.Equal(new JalaliMonth(1404, 12), new JalaliMonth(1405, 1).Previous());
        Assert.Equal(new JalaliMonth(1405, 7), new JalaliMonth(1405, 8).Previous());
    }

    [Fact]
    public void Comparison_has_no_previous_data_before_the_first_synced_month()
    {
        var first = new JalaliMonth(1405, 1);
        Assert.False(ComparisonMath.HasPreviousData(new JalaliMonth(1404, 12), first));
        Assert.True(ComparisonMath.HasPreviousData(new JalaliMonth(1405, 1), first));
        Assert.False(ComparisonMath.HasPreviousData(new JalaliMonth(1405, 7), null));
    }

    [Fact]
    public void Twelve_month_window_is_oldest_first_and_ends_at_the_selected_month()
    {
        var window = JalaliMonth.Window(new JalaliMonth(1405, 8), 12);

        Assert.Equal(12, window.Count);
        Assert.Equal("1404-09", window[0].Key);
        Assert.Equal("1404-12", window[3].Key);
        Assert.Equal("1405-01", window[4].Key);
        Assert.Equal("1405-08", window[^1].Key);
    }

    [Theory]
    [InlineData("1405-08", 1405, 8)]
    [InlineData("۱۴۰۵-۰۸", 1405, 8)]
    [InlineData("۱۴۰۵-۱۲", 1405, 12)]
    public void Month_parses_latin_and_persian_digits(string text, int year, int month)
    {
        Assert.True(JalaliMonth.TryParse(text, out var parsed));
        Assert.Equal(new JalaliMonth(year, month), parsed);
    }

    [Theory]
    [InlineData("1405-13")]
    [InlineData("1405-00")]
    [InlineData("۱۴۰۵-۱۳")]
    [InlineData("1405-1")]
    [InlineData("۱۴۰۵-۱")]
    [InlineData("1405/08")]
    [InlineData("1405/8")]
    [InlineData("1405-8-1")]
    [InlineData("1405-008")]
    [InlineData("1405")]
    [InlineData("1405-")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("140-08")]
    [InlineData("9999-01")]
    [InlineData(" 1404-12 ")]
    [InlineData(" 1404-12")]
    [InlineData("1404-12 ")]
    public void Month_rejects_invalid_text(string text)
    {
        Assert.False(JalaliMonth.TryParse(text, out _));
    }

    [Fact]
    public void Month_rejects_null()
    {
        Assert.False(JalaliMonth.TryParse(null, out _));
    }

    [Fact]
    public void Month_label_and_key_use_persian_names_and_digits()
    {
        var month = new JalaliMonth(1405, 8);

        Assert.Equal("1405-08", month.Key);
        Assert.Equal("آبان ۱۴۰۵", month.Label);
    }

    [Fact]
    public void Amounts_are_strings_with_four_decimals()
    {
        Assert.Equal("1250000000.0000", ComparisonMath.Amount(1_250_000_000m));
        Assert.Equal("-50.0000", ComparisonMath.Amount(-50m));
        Assert.Equal("0.0000", ComparisonMath.Amount(0m));
        Assert.Equal("0.5000", ComparisonMath.Amount(0.5m));
    }
}
