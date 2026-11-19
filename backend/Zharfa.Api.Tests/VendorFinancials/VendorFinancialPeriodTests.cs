namespace Zharfa.Api.Tests.VendorFinancials;

using Zharfa.Api.Analytics;
using Zharfa.Api.Purchasing;
using Zharfa.Api.Purchasing.Reports;
using Zharfa.Api.VendorFinancials;

/// <summary>
/// T029 (research R2, contracts/api.md §1): the as-of date of balance/outstanding (last Jalali day of a preset,
/// leap Esfand included; `to` of a custom range), the previous period of the comparison (previous Jalali unit /
/// equal-day custom range), and the no-previous-data base rule — a missing previous base never turns the
/// current zeros into unavailable figures. Pure: no database, no network.
/// </summary>
public class VendorFinancialPeriodTests
{
    private static JalaliPeriod Parse(string kind, string? value, string? from, string? to, string today) =>
        JalaliPeriod.TryParse(kind, value, from, to, Day(today), out var period, out var error)
            ? period!
            : throw new Xunit.Sdk.XunitException(error ?? "period parse failed");

    private static JalaliDate Day(string text)
    {
        var parts = text.Split('-');
        return new JalaliDate(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    }

    private static JalaliDate Back(DateOnly date) => JalaliDate.FromDate(date);

    [Fact]
    public void Month_preset_as_of_is_the_last_day_of_the_month()
    {
        var period = Parse("month", "1405-08", null, null, "1405-09-05");

        var asOf = VendorFinancialPeriod.AsOf(period);
        Assert.Equal(new JalaliDate(1405, 8, 30), Back(asOf));
        Assert.Equal(new JalaliDate(1405, 8, 1), Back(VendorFinancialPeriod.From(period)));
        Assert.Equal(new JalaliDate(1405, 8, 30), Back(VendorFinancialPeriod.To(period)));
    }

    [Fact]
    public void Leap_esfand_as_of_is_the_thirtieth()
    {
        // 1403 is a Jalali leap year: Esfand has 30 days, and the as-of uses that day.
        Assert.True(JalaliDate.IsLeapYear(1403));
        var period = Parse("month", "1403-12", null, null, "1404-01-15");

        Assert.Equal("1403-12-30", period.ToWire);
        Assert.Equal(new DateOnly(2025, 3, 20), VendorFinancialPeriod.AsOf(period));
    }

    [Fact]
    public void Custom_range_as_of_is_the_to_date()
    {
        var period = Parse("custom", null, "1404-05-10", "1404-05-20", "1405-01-01");

        Assert.Equal(VendorFinancialPeriod.To(period), VendorFinancialPeriod.AsOf(period));
        Assert.Equal(new JalaliDate(1404, 5, 20), Back(VendorFinancialPeriod.AsOf(period)));
    }

    [Fact]
    public void Quarter_and_year_presets_as_of_is_their_last_jalali_day()
    {
        var quarter = Parse("quarter", "1405-Q3", null, null, "1405-09-01");
        var year = Parse("year", "1403", null, null, "1404-06-01");

        Assert.Equal(new JalaliDate(1405, 9, 30), Back(VendorFinancialPeriod.AsOf(quarter)));
        Assert.Equal(new JalaliDate(1403, 12, 30), Back(VendorFinancialPeriod.AsOf(year)));
    }

    [Fact]
    public void Previous_of_a_month_is_the_previous_jalali_month()
    {
        var period = Parse("month", "1405-08", null, null, "1405-09-01");
        var previous = period.Previous();

        Assert.Equal(JalaliPeriodKind.Month, previous.Kind);
        Assert.Equal("1405-07", previous.StartMonth.Key);
        Assert.Equal(period.StartMonth.AddMonths(-1).Key, previous.StartMonth.Key);
    }

    [Fact]
    public void Previous_of_a_quarter_and_a_year_move_one_unit_back()
    {
        var quarter = Parse("quarter", "1405-Q2", null, null, "1405-07-01");
        var year = Parse("year", "1405", null, null, "1405-06-01");

        Assert.Equal(new JalaliDate(1405, 1, 1), quarter.Previous().From);
        Assert.Equal(new JalaliDate(1404, 1, 1), year.Previous().From);
        Assert.Equal(new JalaliDate(1404, 12, JalaliDate.DaysInMonth(1404, 12)), year.Previous().To);
    }

    [Fact]
    public void Previous_of_a_custom_range_is_equal_day()
    {
        var period = Parse("custom", null, "1404-05-10", "1404-05-20", "1405-01-01");
        var previous = period.Previous();

        // Same number of days (11), ending the day before the range starts.
        Assert.Equal("1404-04-30", previous.FromWire);
        Assert.Equal("1404-05-09", previous.ToWire);
        Assert.Equal(period.Days, previous.Days);
    }

    [Fact]
    public void Previous_base_exists_when_the_previous_unit_has_synced_data()
    {
        var period = Parse("month", "1405-08", null, null, "1405-09-01");

        // The previous unit of 1405-08 is 1405-07: a first data month on or before it gives a base.
        Assert.True(VendorFinancialPeriod.HasPreviousBase(period, new JalaliMonth(1405, 7)));
        Assert.True(VendorFinancialPeriod.HasPreviousBase(period, new JalaliMonth(1404, 1)));
    }

    [Fact]
    public void Previous_base_is_missing_before_the_first_synced_month()
    {
        var period = Parse("month", "1405-08", null, null, "1405-09-01");

        Assert.False(VendorFinancialPeriod.HasPreviousBase(period, new JalaliMonth(1405, 8)));
        Assert.False(VendorFinancialPeriod.HasPreviousBase(period, null));
    }

    [Fact]
    public void A_missing_previous_base_keeps_the_current_zeros_and_only_marks_the_comparison()
    {
        // The response shape of a compare request whose previous base is missing: the comparison is listed as
        // no-previous-data, the current column stays "0.0000" (usable formula, no activity) and the previous
        // columns are null — never a whole-figure unavailability.
        var row = new VendorFinancialSummaryRow("V-1", "نام", null, "0.0000", "0.0000",
            PreviousVendorBalance: null,
            PreviousOutstandingPayments: null,
            DeltaVendorBalance: null,
            PercentVendorBalance: null,
            DeltaOutstanding: null,
            PercentOutstanding: null);
        var unavailable = VendorFinancialUnavailable.For("comparison", VendorFinancialAvailability.NoPreviousData);

        Assert.Equal("0.0000", row.VendorBalance);
        Assert.Null(row.PreviousVendorBalance);
        Assert.Null(row.DeltaVendorBalance);
        Assert.Equal("comparison", unavailable.Figure);
        Assert.Equal("no-previous-data", unavailable.Reason);
        Assert.Equal("داده در دسترس نیست.", unavailable.Message);
    }

    [Fact]
    public void The_period_dto_carries_the_kind_month_value_and_bounds()
    {
        var period = Parse("month", "1405-08", null, null, "1405-09-01");
        var dto = VendorFinancialPeriod.ToDto(period);

        Assert.Equal("month", dto.Kind);
        Assert.Equal("1405-08", dto.Month);
        Assert.Equal("1405-08-01", dto.From);
        Assert.Equal("1405-08-30", dto.To);

        var custom = VendorFinancialPeriod.ToDto(Parse("custom", null, "1404-05-10", "1404-05-20", "1405-01-01"));
        Assert.Equal("custom", custom.Kind);
        Assert.Null(custom.Month);
    }

    [Fact]
    public void The_request_parser_reads_the_kind_period_and_sort_allowlists()
    {
        var today = Day("1405-09-01");
        var query = new Dictionary<string, string?>
        {
            ["kind"] = "month",
            ["month"] = "1405-08",
            ["compare"] = "1",
            ["q"] = "  کریمی ",
            ["pageSize"] = "50",
        };

        Assert.True(VendorFinancialRequestParser.TryParse(
            query, VendorFinancialSurface.Summary, today, out var request, out var error));
        Assert.Null(error);
        Assert.True(request!.Compare);
        Assert.Equal("1405-08", request.Period.StartMonth.Key);
        Assert.Equal("کریمی", request.Search);
        Assert.Equal("کریمی", request.NormalizedSearch); // whitespace removed, letters folded
        Assert.Equal(50, request.PageSize);
        Assert.Equal("vendorBalance", request.Sort);
        Assert.True(request.Descending);
    }

    [Fact]
    public void Truthy_compare_on_the_document_lists_is_refused_with_compare_not_allowed()
    {
        var today = Day("1405-09-01");
        var query = new Dictionary<string, string?> { ["compare"] = "1" };

        Assert.False(VendorFinancialRequestParser.TryParse(
            query, VendorFinancialSurface.History, today, out _, out var historyError));
        Assert.Equal("compare-not-allowed", historyError!.Code);
        Assert.Equal("مقایسه دوره برای تاریخچه پرداخت در دسترس نیست.", historyError.Message);

        Assert.False(VendorFinancialRequestParser.TryParse(
            query, VendorFinancialSurface.Outstanding, today, out _, out var outstandingError));
        Assert.Equal("compare-not-allowed", outstandingError!.Code);

        // compare=0 stays a valid no-op on the same lists.
        query["compare"] = "0";
        Assert.True(VendorFinancialRequestParser.TryParse(
            query, VendorFinancialSurface.History, today, out var request, out _));
        Assert.False(request!.Compare);
    }

    [Fact]
    public void An_unknown_sort_is_a_bad_request_not_a_silent_fallback()
    {
        var today = Day("1405-09-01");
        var query = new Dictionary<string, string?> { ["sort"] = "profit" };

        Assert.False(VendorFinancialRequestParser.TryParse(
            query, VendorFinancialSurface.Summary, today, out _, out var error));
        Assert.Null(error!.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void The_search_key_folds_arabic_letters_digits_and_separators_like_the_sql_twin()
    {
        Assert.Equal("داروخانهکریمی", VendorFinancialPeriod.SearchKey("داروخانه كريمي")); // Arabic kaf/yeh folded, space removed
        Assert.Equal("1405test", VendorFinancialPeriod.SearchKey("۱۴۰۵ Test"));
        Assert.Equal("یلدایآبان", VendorFinancialPeriod.SearchKey("یلدای|آبان"));
        Assert.Equal(string.Empty, VendorFinancialPeriod.SearchKey(null));
    }
}
