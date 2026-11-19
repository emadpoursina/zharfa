namespace Zharfa.Api.Tests.Purchasing.Reports;

using Zharfa.Api.Analytics;
using Zharfa.Api.Purchasing;
using Zharfa.Api.Purchasing.Reports;

/// <summary>
/// T004 (spec 007 US1, research R3): JalaliPeriod.LastNMonths(n, end).
/// Six months ending in Aban 1405 (current Overview month in tests) spans Khordad 1405 through Aban 1405.
/// Pure: no database, no network.
/// </summary>
public class LastNMonthsTests
{
    [Fact]
    public void Six_months_ending_aban_1405_spans_first_day_of_khordad_through_last_day_of_aban()
    {
        var end = new JalaliMonth(1405, 8); // Aban 1405
        var period = JalaliPeriod.LastNMonths(6, end);

        Assert.Equal(JalaliPeriodKind.Custom, period.Kind);
        Assert.Equal(new JalaliDate(1405, 3, 1), period.From);
        Assert.Equal(new JalaliDate(1405, 8, 30), period.To);
        Assert.Equal(14050301, period.FromKey);
        Assert.Equal(14050830, period.ToKey);
        Assert.Equal("1405-03-01", period.FromWire);
        Assert.Equal("1405-08-30", period.ToWire);
        Assert.Null(period.TrimmedTo);
    }

    [Fact]
    public void One_month_ending_aban_1405_spans_that_month_only_with_custom_kind()
    {
        var end = new JalaliMonth(1405, 8);
        var period = JalaliPeriod.LastNMonths(1, end);

        Assert.Equal(JalaliPeriodKind.Custom, period.Kind);
        Assert.Equal(new JalaliDate(1405, 8, 1), period.From);
        Assert.Equal(new JalaliDate(1405, 8, 30), period.To);
        Assert.Equal("1405-08-01", period.FromWire);
        Assert.Equal("1405-08-30", period.ToWire);
    }

    [Fact]
    public void Crosses_year_boundary_correctly()
    {
        var end = new JalaliMonth(1406, 2); // Ordibehesht 1406
        var period = JalaliPeriod.LastNMonths(6, end);

        // Ordibehesht (month 2) - 5 months = Azar 1405 (month 9)
        Assert.Equal(JalaliPeriodKind.Custom, period.Kind);
        Assert.Equal(new JalaliDate(1405, 9, 1), period.From);
        Assert.Equal(new JalaliDate(1406, 2, 31), period.To);
        Assert.Equal("1405-09-01", period.FromWire);
        Assert.Equal("1406-02-31", period.ToWire);
    }

    [Fact]
    public void Throws_when_n_is_less_than_one()
    {
        var end = new JalaliMonth(1405, 8);
        Assert.Throws<ArgumentOutOfRangeException>(() => JalaliPeriod.LastNMonths(0, end));
    }
}
