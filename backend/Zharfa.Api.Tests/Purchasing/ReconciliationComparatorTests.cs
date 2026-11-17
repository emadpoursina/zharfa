namespace Zharfa.Api.Tests.Purchasing;

using System.Collections.Generic;
using Zharfa.Api.Purchasing;

/// <summary>
/// T050 (spec 003 US4, FR-017/FR-018): exact per-metric comparison, the exit code and the printed line, and
/// the reconcile command-line parsing. Pure: no database, no network.
/// </summary>
public class ReconciliationComparatorTests
{
    [Fact]
    public void Equal_totals_pass_with_a_zero_difference()
    {
        var result = ReconciliationComparator.Compare(ReconcileMetrics.Net, 230m, 230.0000m, null);

        Assert.Equal(ReconcileOutcome.Pass, result.Outcome);
        Assert.Equal(0m, result.Difference);
    }

    [Fact]
    public void Any_difference_is_a_mismatch_with_system_minus_source()
    {
        var result = ReconciliationComparator.Compare(ReconcileMetrics.Net, 230m, 229.9999m, null);

        Assert.Equal(ReconcileOutcome.Mismatch, result.Outcome);
        Assert.Equal(0.0001m, result.Difference);
    }

    [Fact]
    public void An_unavailable_metric_is_listed_and_does_not_fail_the_run()
    {
        var vat = ReconciliationComparator.Compare(ReconcileMetrics.Vat, null, null, MetricAvailability.SourceItemUnmapped);
        var net = ReconciliationComparator.Compare(ReconcileMetrics.Net, 1m, 1m, null);

        Assert.Equal(ReconcileOutcome.Unavailable, vat.Outcome);
        Assert.Null(vat.System);
        Assert.Equal(0, ReconciliationComparator.ExitCode([vat, net]));
    }

    [Fact]
    public void A_missing_total_on_one_side_is_an_error_not_a_pass()
    {
        var result = ReconciliationComparator.Compare(ReconcileMetrics.Gross, 10m, null, null);

        Assert.Equal(ReconcileOutcome.Error, result.Outcome);
        Assert.Equal(2, ReconciliationComparator.ExitCode([result]));
    }

    [Fact]
    public void Exit_code_is_1_for_any_mismatch_and_2_for_an_error()
    {
        var pass = ReconciliationComparator.Compare(ReconcileMetrics.Net, 1m, 1m, null);
        var mismatch = ReconciliationComparator.Compare(ReconcileMetrics.Gross, 2m, 1m, null);
        var error = ReconciliationComparator.Compare(ReconcileMetrics.Returns, 1m, null, null);

        Assert.Equal(0, ReconciliationComparator.ExitCode([pass]));
        Assert.Equal(1, ReconciliationComparator.ExitCode([pass, mismatch]));
        Assert.Equal(2, ReconciliationComparator.ExitCode([mismatch, error]));
    }

    [Fact]
    public void A_mismatch_blocks_sign_off_and_is_reported_as_such()
    {
        var report = new ReconciliationReport(
            "1405-07", "none", null, "line", "demo",
            [ReconciliationComparator.Compare(ReconcileMetrics.Net, 5m, 4m, null)]);

        Assert.Equal(ReconcileOutcome.Mismatch, report.Outcome);
        Assert.True(report.SignOffBlocked);
        Assert.Equal(1, report.ExitCode);
        Assert.Contains("\"signOffBlocked\":true", report.ToJsonLine(showAmounts: false));
    }

    [Fact]
    public void A_passing_run_with_only_unavailable_metrics_is_not_blocked()
    {
        var report = new ReconciliationReport(
            "1405-07", "none", null, "line", "demo",
            [ReconciliationComparator.Compare(ReconcileMetrics.Vat, null, null, MetricAvailability.SourceItemUnmapped)]);

        Assert.Equal(ReconcileOutcome.Pass, report.Outcome);
        Assert.False(report.SignOffBlocked);
        Assert.Equal(0, report.ExitCode);
    }

    [Fact]
    public void A_failed_run_exits_2_and_names_no_results()
    {
        var report = ReconciliationReport.Failed("1405-07", "none", null, "line", "demo", "no successful sync");

        Assert.Equal(2, report.ExitCode);
        Assert.Contains("\"outcome\":\"error\"", report.ToJsonLine(showAmounts: false));
    }

    [Fact]
    public void Production_output_omits_amounts_unless_show_amounts_is_passed()
    {
        var report = new ReconciliationReport(
            "1405-07", "vendor", "V-102", "line", "production",
            [ReconciliationComparator.Compare(ReconcileMetrics.Net, 230m, 230m, null)]);

        var hidden = report.ToJsonLine(showAmounts: false);
        var shown = report.ToJsonLine(showAmounts: true);

        Assert.DoesNotContain("\"system\"", hidden);
        Assert.DoesNotContain("\"difference\"", hidden);
        Assert.Contains("\"outcome\":\"pass\"", hidden);
        Assert.Contains("\"system\":\"230.0000\"", shown);
        Assert.Contains("\"difference\":\"0.0000\"", shown);
    }

    [Fact]
    public void Demo_output_always_includes_the_amounts()
    {
        var report = new ReconciliationReport(
            "1405-07", "none", null, "line", "demo",
            [ReconciliationComparator.Compare(ReconcileMetrics.Net, 230m, 230m, null)]);

        Assert.Contains("\"source\":\"230.0000\"", report.ToJsonLine(showAmounts: false));
    }

    [Theory]
    [InlineData("1405-07", 1405, 7)]
    [InlineData("1405-7", 1405, 7)]
    [InlineData("۱۴۰۵-۰۷", 1405, 7)]
    [InlineData("1403-12", 1403, 12)]
    public void Month_accepts_latin_and_persian_digits(string text, int year, int month)
    {
        Assert.Equal((year, month), ReconcileCommandLine.ParseMonth(text));
    }

    [Theory]
    [InlineData("1405-13")]
    [InlineData("1405-00")]
    [InlineData("14-07")]
    [InlineData("1405/07")]
    [InlineData("")]
    public void Malformed_months_are_refused(string text)
    {
        Assert.Null(ReconcileCommandLine.ParseMonth(text));
    }

    [Fact]
    public void Minimal_command_parses_with_no_filter_and_the_configured_basis()
    {
        var parsed = ReconcileCommandLine.Parse(["reconcile", "purchasing", "--month", "1405-07"]);

        Assert.Null(parsed.Error);
        Assert.Equal("none", parsed.Command!.FilterKind);
        Assert.Null(parsed.Command.FilterValue);
        Assert.Null(parsed.Command.Basis);
        Assert.False(parsed.Command.ShowAmounts);
        Assert.Equal("1405-07", parsed.Command.MonthText);
    }

    [Fact]
    public void Vendor_or_product_filter_and_basis_and_show_amounts_parse()
    {
        var vendor = ReconcileCommandLine.Parse(["reconcile", "purchasing", "--month", "1405-08", "--vendor", "V-102", "--show-amounts"]);
        var product = ReconcileCommandLine.Parse(["reconcile", "purchasing", "--month", "1405-08", "--product", "P-9", "--basis", "lineAndDocument"]);

        Assert.Equal("vendor", vendor.Command!.FilterKind);
        Assert.Equal("V-102", vendor.Command.FilterValue);
        Assert.True(vendor.Command.ShowAmounts);
        Assert.Equal("product", product.Command!.FilterKind);
        Assert.Equal("P-9", product.Command.FilterValue);
        Assert.Equal("lineAndDocument", product.Command.Basis);
    }

    [Fact]
    public void Vendor_and_product_are_mutually_exclusive()
    {
        var parsed = ReconcileCommandLine.Parse(["reconcile", "purchasing", "--month", "1405-07", "--vendor", "V1", "--product", "P1"]);

        Assert.Null(parsed.Command);
        Assert.Contains("mutually exclusive", parsed.Error);
    }

    public static IEnumerable<object[]> InvalidCommands()
    {
        yield return new object[] { new[] { "reconcile", "purchasing" } };
        yield return new object[] { new[] { "reconcile", "purchasing", "--vendor", "V1" } };
        yield return new object[] { new[] { "reconcile", "purchasing", "--month", "1405-13" } };
        yield return new object[] { new[] { "reconcile", "purchasing", "--month", "1405-07", "--basis", "gross" } };
        yield return new object[] { new[] { "reconcile", "purchasing", "--month", "1405-07", "--bogus" } };
        yield return new object[] { new[] { "reconcile", "purchasing", "--month", "1405-07", "--month", "1405-08" } };
        yield return new object[] { new[] { "reconcile", "sales", "--month", "1405-07" } };
    }

    [Theory]
    [MemberData(nameof(InvalidCommands))]
    public void Invalid_commands_are_usage_errors(string[] args)
    {
        var parsed = ReconcileCommandLine.Parse(args);

        Assert.Null(parsed.Command);
        Assert.NotNull(parsed.Error);
    }

    [Fact]
    public void Only_the_reconcile_word_starts_the_cli()
    {
        Assert.True(ReconcileCommandLine.IsReconcile(new List<string> { "reconcile", "purchasing" }));
        Assert.False(ReconcileCommandLine.IsReconcile(new List<string>()));
        Assert.False(ReconcileCommandLine.IsReconcile(new List<string> { "--urls", "http://localhost:8080" }));
    }
}
