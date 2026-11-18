namespace Zharfa.Api.Tests.Purchasing.Reports;

using System.Linq;
using Zharfa.Api.Purchasing.Reports;

/// <summary>
/// T024 (spec 004 contracts/api.md "Print twin", FR-022): the print shape is capped at 1,000 rows and
/// reports truncation. Pure: no database, no network.
/// </summary>
public class PrintLimitTests
{
    private static List<int> Rows(int count) => Enumerable.Range(1, count).ToList();

    [Fact]
    public void The_print_query_fetches_one_row_more_than_the_limit()
    {
        Assert.Equal(1000, PrintLimit.Limit);
        Assert.Equal(1001, PrintLimit.FetchLimit);
    }

    [Fact]
    public void Print_returns_at_most_1000_rows_and_reports_the_limit()
    {
        var shaped = PrintLimit.Shape(Rows(PrintLimit.FetchLimit), totalRows: 1500);

        Assert.Equal(1000, shaped.Rows.Count);
        Assert.Equal(1000, shaped.Limit);
        Assert.Equal(1500, shaped.TotalRows);
        Assert.True(shaped.Truncated);
    }

    [Fact]
    public void Truncated_is_false_when_the_total_is_exactly_the_limit()
    {
        var shaped = PrintLimit.Shape(Rows(1000), totalRows: 1000);

        Assert.Equal(1000, shaped.Rows.Count);
        Assert.False(shaped.Truncated);
    }

    [Fact]
    public void Truncated_is_true_only_when_the_total_exceeds_the_limit()
    {
        Assert.False(PrintLimit.Shape(Rows(3), totalRows: 3).Truncated);
        Assert.True(PrintLimit.Shape(Rows(1000), totalRows: 1001).Truncated);
    }

    [Fact]
    public void Print_keeps_the_first_rows_in_the_current_order()
    {
        var shaped = PrintLimit.Shape(Rows(PrintLimit.FetchLimit), totalRows: 1001);

        Assert.Equal(1, shaped.Rows.First());
        Assert.Equal(1000, shaped.Rows.Last());
    }

    [Fact]
    public void Empty_result_prints_no_rows_and_is_not_truncated()
    {
        var shaped = PrintLimit.Shape(Rows(0), totalRows: 0);

        Assert.Empty(shaped.Rows);
        Assert.False(shaped.Truncated);
    }
}
