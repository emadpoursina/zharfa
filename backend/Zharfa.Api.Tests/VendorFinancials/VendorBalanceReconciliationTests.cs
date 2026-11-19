namespace Zharfa.Api.Tests.VendorFinancials;

using System.Text.Json;
using Zharfa.Api.Analytics;
using Zharfa.Api.Platform;
using Zharfa.Api.Purchasing;

/// <summary>
/// T040 (FR-021/FR-022, research R11, contracts/cli-and-config.md): the vendor-balance reconciliation compares
/// equal signed totals (negative credit included) as a pass, any difference as a mismatch of `system − source`
/// that exits 1 with signOffBlocked, and an unusable formula as `unavailable` that exits 0 without being a
/// sign-off pass. The purchasing command still parses and is not replaced. Pure: no database, no network.
/// </summary>
public class VendorBalanceReconciliationTests
{
    // ---- comparator (the shared exact comparison) ----

    [Fact]
    public void Equal_signed_totals_pass_including_a_negative_credit()
    {
        var equal = ReconciliationComparator.Compare("vendor_balance", -2500000m, -2500000m, null);

        Assert.Equal("vendor_balance", equal.Metric);
        Assert.Equal(ReconcileOutcome.Pass, equal.Outcome);
        Assert.Equal(0m, equal.Difference);

        var zero = ReconciliationComparator.Compare("vendor_balance", 0m, 0m, null);
        Assert.Equal(ReconcileOutcome.Pass, zero.Outcome);
    }

    [Fact]
    public void Any_difference_is_a_mismatch_of_system_minus_source()
    {
        var mismatch = ReconciliationComparator.Compare("vendor_balance", 1000m, 900m, null);

        Assert.Equal(ReconcileOutcome.Mismatch, mismatch.Outcome);
        Assert.Equal(100m, mismatch.Difference);

        var otherWay = ReconciliationComparator.Compare("vendor_balance", 900m, 1000m, null);
        Assert.Equal(-100m, otherWay.Difference);
    }

    [Fact]
    public void An_unavailable_formula_is_unavailable_with_its_reason_and_never_a_number()
    {
        var result = ReconciliationComparator.Compare("vendor_balance", null, null, "formula-unconfirmed");

        Assert.Equal(ReconcileOutcome.Unavailable, result.Outcome);
        Assert.Equal("formula-unconfirmed", result.UnavailableReason);
        Assert.Null(result.System);
        Assert.Null(result.Source);
        Assert.Equal(0, ReconciliationComparator.ExitCode([result]));
    }

    // ---- the vendor-balance report ----

    private static VendorBalanceReport UnavailableReport() =>
        new("1405-08", "vendor", "V-1", "production",
            [new MetricComparison("vendor_balance", null, null, null, ReconcileOutcome.Unavailable, "formula-unconfirmed")]);

    [Fact]
    public void Unavailable_exits_zero_and_is_not_a_sign_off_pass()
    {
        var report = UnavailableReport();

        Assert.Equal(ReconcileOutcome.Unavailable, report.Outcome);
        Assert.Equal(0, report.ExitCode);
        Assert.True(report.SignOffBlocked, "an unavailable formula must never read as a sign-off pass (T040).");
    }

    [Fact]
    public void A_pass_exits_zero_and_does_not_block_sign_off()
    {
        var report = new VendorBalanceReport(
            "1405-08", "none", null, "demo",
            [new MetricComparison("vendor_balance", 100m, 100m, 0m, ReconcileOutcome.Pass)]);

        Assert.Equal(ReconcileOutcome.Pass, report.Outcome);
        Assert.Equal(0, report.ExitCode);
        Assert.False(report.SignOffBlocked);
    }

    [Fact]
    public void A_mismatch_exits_one_and_blocks_sign_off()
    {
        var report = new VendorBalanceReport(
            "1405-08", "vendor", "V-1", "demo",
            [new MetricComparison("vendor_balance", 100m, 90m, 10m, ReconcileOutcome.Mismatch)]);

        Assert.Equal(ReconcileOutcome.Mismatch, report.Outcome);
        Assert.Equal(1, report.ExitCode);
        Assert.True(report.SignOffBlocked);
    }

    [Fact]
    public void A_failed_run_exits_two()
    {
        var report = VendorBalanceReport.Failed("1405-08", "none", null, "demo", "map invalid");

        Assert.Equal(ReconcileOutcome.Error, report.Outcome);
        Assert.Equal(2, report.ExitCode);
        Assert.True(report.SignOffBlocked);
    }

    [Fact]
    public void The_json_line_has_the_contract_shape_with_amounts_in_demo()
    {
        var report = new VendorBalanceReport(
            "1405-08", "vendor", "V-102", "demo",
            [new MetricComparison("vendor_balance", 123m, 123m, 0m, ReconcileOutcome.Pass)]);
        var line = report.ToJsonLine(showAmounts: false);

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;

        Assert.Equal("1405-08", root.GetProperty("month").GetString());
        Assert.Equal("vendor", root.GetProperty("filter").GetProperty("kind").GetString());
        Assert.Equal("V-102", root.GetProperty("filter").GetProperty("value").GetString());
        Assert.Equal("demo", root.GetProperty("dataMode").GetString());
        Assert.Equal("pass", root.GetProperty("outcome").GetString());
        Assert.False(root.GetProperty("signOffBlocked").GetBoolean());

        var result = root.GetProperty("results")[0];
        Assert.Equal("vendor_balance", result.GetProperty("metric").GetString());
        Assert.Equal("123.0000", result.GetProperty("system").GetString());
        Assert.Equal("123.0000", result.GetProperty("source").GetString());
        Assert.Equal("0.0000", result.GetProperty("difference").GetString());
    }

    [Fact]
    public void Production_omits_the_amounts_unless_show_amounts_is_set()
    {
        var report = new VendorBalanceReport(
            "1405-08", "none", null, "production",
            [new MetricComparison("vendor_balance", 123m, 120m, 3m, ReconcileOutcome.Mismatch)]);

        var hidden = report.ToJsonLine(showAmounts: false);
        using (var document = JsonDocument.Parse(hidden))
        {
            var result = document.RootElement.GetProperty("results")[0];
            Assert.False(result.TryGetProperty("system", out _));
            Assert.False(result.TryGetProperty("source", out _));
            Assert.False(result.TryGetProperty("difference", out _));
            Assert.Equal("mismatch", result.GetProperty("outcome").GetString());
        }

        var shown = report.ToJsonLine(showAmounts: true);
        using (var document = JsonDocument.Parse(shown))
        {
            var result = document.RootElement.GetProperty("results")[0];
            Assert.Equal("123.0000", result.GetProperty("system").GetString());
        }
    }

    [Fact]
    public void The_unavailable_line_carries_the_reason()
    {
        var line = UnavailableReport().ToJsonLine(showAmounts: false);

        using var document = JsonDocument.Parse(line);
        var result = document.RootElement.GetProperty("results")[0];

        Assert.Equal("unavailable", result.GetProperty("outcome").GetString());
        Assert.Equal("formula-unconfirmed", result.GetProperty("reason").GetString());
        Assert.Equal("unavailable", document.RootElement.GetProperty("outcome").GetString());
        Assert.True(document.RootElement.GetProperty("signOffBlocked").GetBoolean());
    }

    [Fact]
    public void The_mismatch_audit_detail_names_period_filter_and_metric_but_never_amounts()
    {
        var report = new VendorBalanceReport(
            "1405-08", "vendor", "V-1", "production",
            [new MetricComparison("vendor_balance", 12345678m, 12345670m, 8m, ReconcileOutcome.Mismatch)]);
        var detail = VendorBalanceReconciliation.MismatchAuditDetail(
            report, report.Results[0], new DataModeOptions("production"));

        using var document = JsonDocument.Parse(detail);
        Assert.Equal("1405-08", document.RootElement.GetProperty("month").GetString());
        Assert.Equal("vendor", document.RootElement.GetProperty("filter").GetString());
        Assert.Equal("V-1", document.RootElement.GetProperty("filterValue").GetString());
        Assert.Equal("vendor_balance", document.RootElement.GetProperty("metric").GetString());
        Assert.Equal("-", document.RootElement.GetProperty("basis").GetString());
        Assert.False(document.RootElement.TryGetProperty("system", out _));
        Assert.DoesNotContain("12345678", detail);
    }

    [Fact]
    public void The_as_of_is_the_last_jalali_day_of_the_month()
    {
        var asOf = VendorBalanceReconciliation.AsOfDate(1405, 8);
        var back = JalaliDate.FromDate(asOf);

        Assert.Equal(new JalaliDate(1405, 8, 30), back);
        Assert.Equal(new JalaliDate(1403, 12, 30), JalaliDate.FromDate(VendorBalanceReconciliation.AsOfDate(1403, 12)));
    }

    // ---- command line ----

    [Fact]
    public void Vendor_balance_command_parses_month_vendor_and_show_amounts()
    {
        var parsed = ReconcileCommandLine.ParseVendorBalance(
            ["reconcile", "vendor-balance", "--month", "1405-08", "--vendor", "V-102", "--show-amounts"]);

        Assert.Null(parsed.Error);
        Assert.NotNull(parsed.Command);
        Assert.Equal(1405, parsed.Command!.Year);
        Assert.Equal(8, parsed.Command.Month);
        Assert.Equal("vendor", parsed.Command.FilterKind);
        Assert.Equal("V-102", parsed.Command.FilterValue);
        Assert.Null(parsed.Command.Basis);
        Assert.True(parsed.Command.ShowAmounts);
        Assert.Equal("1405-08", parsed.Command.MonthText);
    }

    [Fact]
    public void Vendor_balance_command_defaults_to_no_filter()
    {
        var parsed = ReconcileCommandLine.ParseVendorBalance(["reconcile", "vendor-balance", "--month", "1404-12"]);

        Assert.Null(parsed.Error);
        Assert.Equal("none", parsed.Command!.FilterKind);
        Assert.Null(parsed.Command.FilterValue);
    }

    [Theory]
    [InlineData("--product")]
    [InlineData("--basis")]
    public void Vendor_balance_refuses_the_purchasing_only_options(string option)
    {
        var parsed = ReconcileCommandLine.ParseVendorBalance(
            ["reconcile", "vendor-balance", "--month", "1405-08", option, "x"]);

        Assert.NotNull(parsed.Error);
        Assert.Null(parsed.Command);
    }

    [Fact]
    public void Vendor_balance_requires_a_valid_month()
    {
        Assert.NotNull(ReconcileCommandLine.ParseVendorBalance(["reconcile", "vendor-balance"]).Error);
        Assert.NotNull(ReconcileCommandLine.ParseVendorBalance(
            ["reconcile", "vendor-balance", "--month", "1405-13"]).Error);
    }

    [Fact]
    public void The_purchasing_command_still_parses_and_is_not_replaced()
    {
        var parsed = ReconcileCommandLine.Parse(
            ["reconcile", "purchasing", "--month", "1405-08", "--vendor", "V-1"]);

        Assert.Null(parsed.Error);
        Assert.Equal("vendor", parsed.Command!.FilterKind);

        // ... and the purchasing parse refuses the Phase 6 subcommand (dispatch decides before parsing).
        var wrong = ReconcileCommandLine.Parse(["reconcile", "vendor-balance", "--month", "1405-08"]);
        Assert.NotNull(wrong.Error);
    }

    [Fact]
    public void Dispatch_recognizes_both_subcommands()
    {
        Assert.True(ReconcileCommandLine.IsReconcile(["reconcile", "vendor-balance"]));
        Assert.True(ReconcileCommandLine.IsVendorBalance(["reconcile", "vendor-balance", "--month", "1405-08"]));
        Assert.False(ReconcileCommandLine.IsVendorBalance(["reconcile", "purchasing", "--month", "1405-08"]));
        Assert.False(ReconcileCommandLine.IsReconcile(["--help"]));
    }
}
