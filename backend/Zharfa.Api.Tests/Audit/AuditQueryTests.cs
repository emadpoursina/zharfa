namespace Zharfa.Api.Tests.Audit;

using System;
using System.Linq;
using Zharfa.Api.Analytics;
using Zharfa.Api.Audit;

/// <summary>
/// T063 (contracts/api.md §3, research R10): the audit query parser. Jalali days are read in the sync zone; the
/// zone is a fixed +03:30 custom zone so the test does not depend on the host's time-zone data. Pure.
/// </summary>
public class AuditQueryTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromMinutes(210), "t", "t");

    private static bool Parse(string? from = null, string? to = null)
    {
        return AuditQuery.TryParse(null, null, from, to, null, null, Zone, out _, out _);
    }

    private static AuditQuery ParseOk(string? from = null, string? to = null, string? page = null, string? pageSize = null, string? user = null, string? action = null)
    {
        Assert.True(AuditQuery.TryParse(user, action, from, to, page, pageSize, Zone, out var query, out var error), error);
        Assert.Null(error);
        return query;
    }

    private static string ErrorOf(string? from = null, string? to = null, string? page = null, string? pageSize = null)
    {
        Assert.False(AuditQuery.TryParse(null, null, from, to, page, pageSize, Zone, out _, out var error));
        Assert.NotNull(error);
        return error!;
    }

    /// <summary>Start of a Jalali day as UTC, computed here from the Gregorian date and the fixed offset.</summary>
    private static DateTime StartUtc(int jy, int jm, int jd)
    {
        var date = new JalaliDate(jy, jm, jd).ToDateOnly();
        return new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc).AddMinutes(-210);
    }

    private static bool HasPersian(string text) => text.Any(ch => ch is >= '\u0600' and <= '\u06FF');

    [Fact]
    public void Empty_query_has_no_filters_page_one_and_fifty_rows()
    {
        var query = ParseOk();

        Assert.Null(query.User);
        Assert.Null(query.Action);
        Assert.Null(query.FromUtc);
        Assert.Null(query.ToUtcExclusive);
        Assert.Equal(1, query.Page);
        Assert.Equal(50, query.PageSize);
    }

    [Fact]
    public void A_date_range_covers_whole_jalali_days_in_the_sync_zone()
    {
        var query = ParseOk(from: "1405-08-01", to: "1405-08-27");

        Assert.Equal(StartUtc(1405, 8, 1), query.FromUtc);
        Assert.Equal(StartUtc(1405, 8, 28), query.ToUtcExclusive);
    }

    [Fact]
    public void Persian_digits_parse_the_same_as_latin_digits()
    {
        var latin = ParseOk(from: "1405-08-01", to: "1405-08-27");
        var persian = ParseOk(from: "۱۴۰۵-۰۸-۰۱", to: "۱۴۰۵-۰۸-۲۷");

        Assert.Equal(latin, persian);
    }

    [Fact]
    public void A_range_ending_before_it_starts_is_refused_in_persian()
    {
        var error = ErrorOf(from: "1405-08-27", to: "1405-08-01");

        Assert.Equal(AuditQuery.EndBeforeStartMessage, error);
        Assert.Contains("تاریخ پایان", error);
    }

    [Fact]
    public void A_month_above_twelve_is_refused_in_persian()
    {
        var error = ErrorOf(from: "1405-13-01");

        Assert.True(HasPersian(error));
    }

    [Fact]
    public void A_day_that_does_not_exist_in_its_month_is_refused()
    {
        // Mehr (month 7) has 30 days, so day 31 does not exist.
        Assert.False(Parse(from: "1405-07-31"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("x")]
    public void Page_size_must_be_between_one_and_one_hundred(string pageSize)
    {
        var error = ErrorOf(pageSize: pageSize);

        Assert.True(HasPersian(error));
    }

    [Fact]
    public void Page_zero_is_refused()
    {
        var error = ErrorOf(page: "0");

        Assert.True(HasPersian(error));
    }

    [Fact]
    public void Page_size_defaults_to_fifty_when_absent()
    {
        var query = ParseOk(page: "3");

        Assert.Equal(50, query.PageSize);
        Assert.Equal(3, query.Page);
    }

    [Fact]
    public void User_and_action_are_trimmed_and_empty_values_are_dropped()
    {
        var query = ParseOk(user: "  admin  ", action: "   ");

        Assert.Equal("admin", query.User);
        Assert.Null(query.Action);
    }
}
