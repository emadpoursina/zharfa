namespace Zharfa.Api.Tests.Audit;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Zharfa.Api.Analytics;
using Zharfa.Api.Controllers;
using Zharfa.Api.Platform;
using Zharfa.Api.Purchasing;

/// <summary>
/// T109 (FR-015, analyze I9, data-model §2.2): the reconciliation audit details hold the period, the filter, the metric
/// and the match flag only, in every data mode. No amount reaches the audit log. Pure: no database, no network.
/// </summary>
public class ReconciliationAuditDetailTests
{
    private const string SystemAmount = "123456789";
    private const string SourceAmount = "123450000";
    private const string DifferenceAmount = "6789";

    private static readonly DataModeOptions Production = new("production");
    private static readonly DataModeOptions Demo = new("demo");

    private static (ReconciliationReport Report, MetricComparison Mismatch) Mismatch()
    {
        var mismatch = new MetricComparison(
            ReconcileMetrics.Gross,
            123456789m,
            123450000m,
            6789m,
            ReconcileOutcome.Mismatch);

        var report = new ReconciliationReport(
            "1405-08",
            "vendor",
            "V1",
            PurchasingDiscountBasis.Line,
            "demo",
            new[] { mismatch });

        return (report, mismatch);
    }

    private static HashSet<string> KeysOf(string detail)
    {
        using var document = JsonDocument.Parse(detail);
        return document.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet();
    }

    [Theory]
    [InlineData("production")]
    [InlineData("demo")]
    public void Mismatch_detail_has_period_filter_metric_and_mode_only(string mode)
    {
        var (report, mismatch) = Mismatch();
        var dataMode = mode == "production" ? Production : Demo;

        var detail = PurchasingReconciliation.MismatchAuditDetail(report, mismatch, dataMode);

        Assert.Equal(
            new HashSet<string> { "month", "filter", "filterValue", "metric", "basis", "dataMode" },
            KeysOf(detail));
    }

    [Theory]
    [InlineData("production")]
    [InlineData("demo")]
    public void Mismatch_detail_never_contains_an_amount(string mode)
    {
        var (report, mismatch) = Mismatch();
        var dataMode = mode == "production" ? Production : Demo;

        var detail = PurchasingReconciliation.MismatchAuditDetail(report, mismatch, dataMode);

        Assert.DoesNotContain(SystemAmount, detail);
        Assert.DoesNotContain(SourceAmount, detail);
        Assert.DoesNotContain(DifferenceAmount, detail);
    }

    [Fact]
    public void Admin_reconciliation_detail_has_period_and_match_only()
    {
        var range = PeriodFilters.Resolve("2026-11-01", "2026-11-18");

        var detail = AdminController.ReconciliationAuditDetail(range, match: false);
        var parts = detail.Split("; ").Select(part => part.Split('=')[0]).ToHashSet();

        Assert.Equal(new HashSet<string> { "from", "to", "match" }, parts);
        Assert.Contains("match=false", detail);
    }

    [Theory]
    [InlineData("apiSales")]
    [InlineData("viewSales")]
    [InlineData("apiReceivables")]
    [InlineData("viewReceivables")]
    public void Admin_reconciliation_detail_has_no_amount_keys(string key)
    {
        var range = PeriodFilters.Resolve("2026-11-01", "2026-11-18");

        Assert.DoesNotContain(key, AdminController.ReconciliationAuditDetail(range, match: false));
    }
}
