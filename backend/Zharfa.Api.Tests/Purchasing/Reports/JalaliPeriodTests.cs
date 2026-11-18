namespace Zharfa.Api.Tests.Purchasing.Reports;

using Zharfa.Api.Analytics;
using Zharfa.Api.Purchasing.Reports;

/// <summary>
/// T006 (spec 004 US5, FR-018/FR-020, research R3): Jalali period parsing, previous periods, labels and
/// validation. Pure: no database, no network.
/// </summary>
public class JalaliPeriodTests
{
    // Today is in Aban 1405, so presets ending later are trimmed.
    private static readonly JalaliDate Today = new(1405, 8, 25);

    private static JalaliPeriod Parse(string? period, string? value = null, string? from = null, string? to = null, JalaliDate? today = null)
    {
        var ok = JalaliPeriod.TryParse(period, value, from, to, today ?? Today, out var result, out var error);
        Assert.True(ok, error);
        return result!;
    }

    private static string? Error(string? period, string? value = null, string? from = null, string? to = null, JalaliDate? today = null)
    {
        var ok = JalaliPeriod.TryParse(period, value, from, to, today ?? Today, out var result, out var error);
        Assert.False(ok);
        Assert.Null(result);
        return error;
    }

    [Fact]
    public void Default_period_is_the_current_jalali_month_trimmed_to_today()
    {
        var period = Parse(null);

        Assert.Equal(JalaliPeriodKind.Month, period.Kind);
        Assert.Equal(14050801, period.FromKey);
        Assert.Equal(14050825, period.ToKey);
        Assert.Equal(new JalaliDate(1405, 8, 25), period.TrimmedTo);
        Assert.Equal("آبان ۱۴۰۵", period.Label);
    }

    [Fact]
    public void Month_accepts_latin_and_persian_digits_and_is_not_trimmed_in_the_past()
    {
        var today = new JalaliDate(1406, 1, 1);
        var latin = Parse("month", "1405-08", today: today);
        var persian = Parse("month", "۱۴۰۵-۰۸", today: today);

        Assert.Equal(14050801, latin.FromKey);
        Assert.Equal(14050830, latin.ToKey);
        Assert.Null(latin.TrimmedTo);
        Assert.Equal(latin.FromKey, persian.FromKey);
        Assert.Equal(latin.ToKey, persian.ToKey);
    }

    [Fact]
    public void Quarter_covers_three_months_with_a_persian_label()
    {
        var today = new JalaliDate(1406, 1, 1);
        var quarter = Parse("quarter", "1405-Q3", today: today);

        Assert.Equal(14050701, quarter.FromKey);
        Assert.Equal(14050900 + JalaliDate.DaysInMonth(1405, 9), quarter.ToKey);
        Assert.Equal("فصل سوم ۱۴۰۵", quarter.Label);
    }

    [Fact]
    public void Year_covers_the_whole_jalali_year()
    {
        var today = new JalaliDate(1406, 1, 1);
        var year = Parse("year", "1405", today: today);

        Assert.Equal(14050101, year.FromKey);
        Assert.Equal(14051200 + JalaliDate.DaysInMonth(1405, 12), year.ToKey);
        Assert.Equal("سال ۱۴۰۵", year.Label);
    }

    [Fact]
    public void Previous_month_of_farvardin_is_esfand_of_the_year_before()
    {
        var today = new JalaliDate(1406, 1, 1);
        var previous = Parse("month", "1405-01", today: today).Previous();

        Assert.Equal(new JalaliDate(1404, 12, 1), previous.From);
        Assert.Equal(new JalaliDate(1404, 12, JalaliDate.DaysInMonth(1404, 12)), previous.To);
        Assert.Null(previous.TrimmedTo);
    }

    [Fact]
    public void Previous_quarter_of_the_first_quarter_is_the_fourth_quarter_of_the_year_before()
    {
        var today = new JalaliDate(1406, 1, 1);
        var previous = Parse("quarter", "1405-Q1", today: today).Previous();

        Assert.Equal(new JalaliDate(1404, 10, 1), previous.From);
        Assert.Equal(new JalaliDate(1404, 12, JalaliDate.DaysInMonth(1404, 12)), previous.To);
        Assert.Equal("فصل چهارم ۱۴۰۴", previous.Label);
    }

    [Fact]
    public void Previous_year_includes_esfand_30_of_a_leap_year()
    {
        var today = new JalaliDate(1406, 1, 1);
        var previous = Parse("year", "1404", today: today).Previous();

        Assert.Equal(new JalaliDate(1403, 1, 1), previous.From);
        Assert.Equal(new JalaliDate(1403, 12, 30), previous.To);
        Assert.Equal(30, JalaliDate.DaysInMonth(1403, 12));
    }

    [Fact]
    public void Previous_of_a_45_day_custom_range_is_the_45_days_ending_the_day_before()
    {
        var period = Parse("custom", from: "1405-07-10", to: "1405-08-24");

        Assert.Equal(45, period.Days);
        var previous = period.Previous();

        Assert.Equal(new JalaliDate(1405, 5, 27), previous.From);
        Assert.Equal(new JalaliDate(1405, 7, 9), previous.To);
        Assert.Equal(45, previous.Days);
    }

    [Fact]
    public void Custom_range_accepts_persian_digits()
    {
        var period = Parse("custom", from: "۱۴۰۵-۰۷-۱۰", to: "۱۴۰۵-۰۸-۲۴");

        Assert.Equal(14050710, period.FromKey);
        Assert.Equal(14050824, period.ToKey);
        Assert.Equal("۱۴۰۵/۰۷/۱۰ تا ۱۴۰۵/۰۸/۲۴", period.Label);
    }

    [Fact]
    public void Custom_range_reaching_past_today_is_trimmed_and_says_so()
    {
        var period = Parse("custom", from: "1405-08-01", to: "1405-09-10");

        Assert.Equal(14050825, period.ToKey);
        Assert.Equal(new JalaliDate(1405, 8, 25), period.TrimmedTo);
    }

    [Theory]
    [InlineData("month", "1405-13")]
    [InlineData("month", "1405-8")]
    [InlineData("quarter", "1405-Q5")]
    [InlineData("quarter", "1405-Q0")]
    [InlineData("year", "14")]
    public void Malformed_or_out_of_range_presets_are_refused_in_persian(string period, string value)
    {
        var message = Error(period, value);

        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Fact]
    public void Esfand_30_is_refused_in_a_non_leap_year()
    {
        // 1404 is not a leap year (only 1403 is in the test window), so Esfand has 29 days.
        Assert.Equal(29, JalaliDate.DaysInMonth(1404, 12));
        var message = Error("custom", from: "1404-12-30", to: "1404-12-30", today: new JalaliDate(1406, 1, 1));

        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Fact]
    public void Esfand_30_is_accepted_in_a_leap_year()
    {
        Assert.Equal(30, JalaliDate.DaysInMonth(1403, 12));
        var period = Parse("custom", from: "1403-12-30", to: "1403-12-30", today: new JalaliDate(1406, 1, 1));

        Assert.Equal(14031230, period.FromKey);
        Assert.Equal(14031230, period.ToKey);
    }

    [Fact]
    public void End_before_start_is_refused()
    {
        var message = Error("custom", from: "1405-08-10", to: "1405-08-09");

        Assert.Equal("تاریخ پایان بازه نمی‌تواند پیش از تاریخ آغاز باشد.", message);
    }

    [Fact]
    public void A_range_wholly_in_the_future_is_refused()
    {
        var message = Error("custom", from: "1405-09-01", to: "1405-09-10");

        Assert.Equal("این بازه کاملاً در آینده است.", message);
    }

    [Fact]
    public void A_custom_range_longer_than_1098_days_is_refused()
    {
        // 1404, 1405 and 1406 are not leap years (1403 is the only leap year in this window), so
        // 1404-01-01 to 1407-01-01 is 1,096 days inclusive and is accepted. 1407-01-04 makes it 1,099.
        var today = new JalaliDate(1410, 1, 1);
        var message = Error("custom", from: "1404-01-01", to: "1407-01-04", today: today);

        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Fact]
    public void A_custom_range_of_exactly_1098_days_is_accepted()
    {
        var today = new JalaliDate(1410, 1, 1);
        var start = new JalaliDate(1404, 1, 1);
        var endJdn = JalaliDate.JalaliToJdn(1404, 1, 1) + JalaliPeriod.MaxCustomDays - 1;
        var (y, m, d) = JalaliDate.JdnToJalali(endJdn);
        var to = $"{y:D4}-{m:D2}-{d:D2}";

        var period = Parse("custom", from: "1404-01-01", to: to, today: today);

        Assert.Equal(JalaliPeriod.MaxCustomDays, period.Days);
        Assert.Equal(start, period.From);
    }
}
