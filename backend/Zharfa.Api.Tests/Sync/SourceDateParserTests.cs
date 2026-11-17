namespace Zharfa.Api.Tests.Sync;

using Zharfa.Api.Mabna.Sync;

public class SourceDateParserTests
{
    // ---- jalaliText ----

    [Theory]
    [InlineData("1403/12/30", 1403, 12, 30)]   // Esfand 1403 (leap year, 30 days)
    [InlineData("1403-12-30", 1403, 12, 30)]
    [InlineData("1404/01/01", 1404, 1, 1)]     // Farvardin 1404
    [InlineData("1405/07/15", 1405, 7, 15)]
    public void JalaliText_Latin_digits_parses(string raw, int jy, int jm, int jd)
    {
        Assert.True(SourceDateParser.TryParse(raw, SourceDateParser.JalaliTextFormat, out var result));
        Assert.Equal(jy, result.JalaliYear);
        Assert.Equal(jm, result.JalaliMonth);
        Assert.Equal(jd, result.JalaliDay);
    }

    [Fact]
    public void JalaliText_Persian_digits_parse()
    {
        Assert.True(SourceDateParser.TryParse(
            "۱۴۰۳/۱۲/۳۰", SourceDateParser.JalaliTextFormat, out var result));
        Assert.Equal(1403, result.JalaliYear);
        Assert.Equal(12, result.JalaliMonth);
        Assert.Equal(30, result.JalaliDay);
    }

    [Fact]
    public void JalaliText_Esfand_30_leap_year_converts_to_Gregorian()
    {
        Assert.True(SourceDateParser.TryParse(
            "1403/12/30", SourceDateParser.JalaliTextFormat, out var result));
        // 1403/12/30 = 2025-03-20
        Assert.Equal(new DateOnly(2025, 3, 20), result.Gregorian);
    }

    // ---- jalaliInt ----

    [Theory]
    [InlineData("14031230", 1403, 12, 30)]
    [InlineData("14040101", 1404, 1, 1)]
    public void JalaliInt_parses(string raw, int jy, int jm, int jd)
    {
        Assert.True(SourceDateParser.TryParse(raw, SourceDateParser.JalaliIntFormat, out var result));
        Assert.Equal(jy, result.JalaliYear);
        Assert.Equal(jm, result.JalaliMonth);
        Assert.Equal(jd, result.JalaliDay);
    }

    [Fact]
    public void JalaliInt_Persian_digits_parse()
    {
        Assert.True(SourceDateParser.TryParse(
            "۱۴۰۳۰۱۰۱", SourceDateParser.JalaliIntFormat, out var result));
        Assert.Equal(1403, result.JalaliYear);
        Assert.Equal(1, result.JalaliMonth);
    }

    // ---- gregorian ----

    [Theory]
    [InlineData("2025-03-20")]
    [InlineData("2025-03-20T00:00:00")]
    [InlineData("2025-03-20 10:30:00")]
    public void Gregorian_parses_and_converts_to_Jalali(string raw)
    {
        Assert.True(SourceDateParser.TryParse(raw, SourceDateParser.GregorianFormat, out var result));
        Assert.Equal(new DateOnly(2025, 3, 20), result.Gregorian);
        Assert.Equal(1403, result.JalaliYear);
        Assert.Equal(12, result.JalaliMonth);
        Assert.Equal(30, result.JalaliDay);
    }

    // ---- invalid input: failure without exception ----

    [Theory]
    [InlineData("1403/13/01")]   // month out of range
    [InlineData("1403/06/32")]   // day out of range for Shahrivar (31 days max)
    [InlineData("1403/00/10")]   // month zero
    [InlineData("1403/01/00")]   // day zero
    [InlineData("not-a-date")]
    [InlineData("1403/12")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_jalali_values_fail_without_throwing(string? raw)
    {
        Assert.False(SourceDateParser.TryParse(raw, SourceDateParser.JalaliTextFormat, out _));
    }

    [Fact]
    public void Esfand_30_in_non_leap_year_is_rejected()
    {
        // 1404 is not a leap year: Esfand has 29 days.
        Assert.False(JalaliDateIsLeap(1404));
        Assert.False(SourceDateParser.TryParse("1404/12/30", SourceDateParser.JalaliTextFormat, out _));
    }

    [Fact]
    public void Esfand_29_in_non_leap_year_is_accepted()
    {
        Assert.True(SourceDateParser.TryParse("1404/12/29", SourceDateParser.JalaliTextFormat, out var result));
        Assert.Equal(1404, result.JalaliYear);
    }

    [Theory]
    [InlineData("14041230")]
    public void Non_leap_Esfand_30_rejected_in_jalaliInt(string raw)
    {
        Assert.False(SourceDateParser.TryParse(raw, SourceDateParser.JalaliIntFormat, out _));
    }

    [Fact]
    public void Unknown_format_fails()
    {
        Assert.False(SourceDateParser.TryParse("1403/01/01", "somethingElse", out _));
        Assert.False(SourceDateParser.TryParse("2025-01-01", null, out _));
    }

    [Fact]
    public void Gregorian_garbage_fails_without_throwing()
    {
        Assert.False(SourceDateParser.TryParse("abc", SourceDateParser.GregorianFormat, out _));
    }

    private static bool JalaliDateIsLeap(int year) =>
        Zharfa.Api.Analytics.JalaliDate.IsLeapYear(year);
}
