namespace Zharfa.Api.Tests.DataQuality;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;
using Zharfa.Api.Audit;
using Zharfa.Api.DataQuality;
using Zharfa.Api.Purchasing.Reports;

/// <summary>
/// T091 (US5, data-model §3.3, §3.5, research R13, R14, contracts/api.md §6):
/// notice mapping, pure notice aggregation, and serialization. Pure: no database, no network.
/// </summary>
public class DataQualityNoticeTests
{
    [Fact]
    public void TypesFor_trend_excludes_unexpected_quantities_and_broken_relationships()
    {
        var types = DataQualityCatalog.TypesFor(ReportKeys.Trend);
        Assert.DoesNotContain(DataQualityCatalog.UnexpectedQuantities, types);
        Assert.DoesNotContain(DataQualityCatalog.BrokenRelationships, types);
    }

    [Fact]
    public void TypesFor_transactions_excludes_inconsistent_product_ids_and_unexpected_quantities()
    {
        var types = DataQualityCatalog.TypesFor(ReportKeys.Transactions);
        Assert.DoesNotContain(DataQualityCatalog.InconsistentProductIds, types);
        Assert.DoesNotContain(DataQualityCatalog.UnexpectedQuantities, types);
    }

    [Fact]
    public void TypesFor_vendor_profile_equals_vendors()
    {
        var profile = DataQualityCatalog.TypesFor(ReportKeys.VendorProfile);
        var vendors = DataQualityCatalog.TypesFor(ReportKeys.Vendors);
        Assert.Equal(vendors, profile);
    }

    [Fact]
    public void TypesFor_drill_down_lines_equals_drill_down()
    {
        var lines = DataQualityCatalog.TypesFor(ReportKeys.DrillDownLines);
        var drill = DataQualityCatalog.TypesFor(ReportKeys.DrillDown);
        Assert.Equal(drill, lines);
    }

    [Fact]
    public void TypesFor_by_vendor_includes_unexpected_quantities()
    {
        var types = DataQualityCatalog.TypesFor(ReportKeys.ByVendor);
        Assert.Contains(DataQualityCatalog.UnexpectedQuantities, types);
    }

    [Fact]
    public void Every_report_key_except_profile_and_lines_maps_to_at_least_missing_values()
    {
        foreach (var key in ReportKeys.All)
        {
            var types = DataQualityCatalog.TypesFor(key);
            Assert.Contains(DataQualityCatalog.MissingValues, types);
        }
    }

    [Fact]
    public void BuildNotice_empty_hits_returns_null()
    {
        var notice = DataQualityNoticeService.BuildNotice(ReportKeys.ByVendor, []);
        Assert.Null(notice);
    }

    [Fact]
    public void BuildNotice_unmapped_types_only_returns_null()
    {
        // trend does not map unexpected-quantities or broken-relationships
        var hits = new[]
        {
            (DataQualityCatalog.UnexpectedQuantities, "line:1"),
            (DataQualityCatalog.BrokenRelationships, "line:2"),
        };

        var notice = DataQualityNoticeService.BuildNotice(ReportKeys.Trend, hits);
        Assert.Null(notice);
    }

    [Fact]
    public void BuildNotice_transactions_excludes_inconsistent_product_ids_and_unexpected_quantities()
    {
        var hits = new[]
        {
            (DataQualityCatalog.InconsistentProductIds, "product:1"),
            (DataQualityCatalog.UnexpectedQuantities, "line:1"),
            (DataQualityCatalog.MissingValues, "document:1"),
        };

        var notice = DataQualityNoticeService.BuildNotice(ReportKeys.Transactions, hits);
        Assert.NotNull(notice);
        Assert.Single(notice.IssueTypes);
        Assert.Equal(DataQualityCatalog.MissingValues, notice.IssueTypes[0].Type);
        Assert.Equal(1, notice.AffectedRecords);
    }

    [Fact]
    public void BuildNotice_mixed_keeps_only_mapped_types_in_catalog_order_and_aggregates_counts()
    {
        // trend maps missing-values, duplicate-records, inconsistent-amounts, invalid-dates
        // but not unexpected-quantities or broken-relationships
        var hits = new[]
        {
            (DataQualityCatalog.UnexpectedQuantities, "line:99"), // dropped
            (DataQualityCatalog.InconsistentAmounts, "document:1"),
            (DataQualityCatalog.MissingValues, "document:1"), // same record ref as above
            (DataQualityCatalog.MissingValues, "document:2"),
            (DataQualityCatalog.BrokenRelationships, "line:98"), // dropped
        };

        var notice = DataQualityNoticeService.BuildNotice(ReportKeys.Trend, hits);
        Assert.NotNull(notice);
        Assert.False(notice.Unavailable);

        // Catalog order: missing-values (index 0), inconsistent-amounts (index 6)
        Assert.Equal(2, notice.IssueTypes.Count);
        Assert.Equal(DataQualityCatalog.MissingValues, notice.IssueTypes[0].Type);
        Assert.Equal("مقادیر ناقص", notice.IssueTypes[0].Label);
        Assert.Equal(2, notice.IssueTypes[0].Count);

        Assert.Equal(DataQualityCatalog.InconsistentAmounts, notice.IssueTypes[1].Type);
        Assert.Equal("مبالغ ناسازگار", notice.IssueTypes[1].Label);
        Assert.Equal(1, notice.IssueTypes[1].Count);

        // document:1 has two issues, document:2 has one -> distinct affected records = 2
        Assert.Equal(2, notice.AffectedRecords);
    }

    [Fact]
    public void BuildNotice_duplicate_pairs_in_current_and_comparison_count_once()
    {
        var currentHits = new[]
        {
            (DataQualityCatalog.MissingValues, "document:1"),
            (DataQualityCatalog.MissingValues, "document:2"),
        };
        var comparisonHits = new[]
        {
            (DataQualityCatalog.MissingValues, "document:2"), // in both
            (DataQualityCatalog.MissingValues, "document:3"),
        };

        var notice = DataQualityNoticeService.BuildNotice(ReportKeys.Overview, currentHits.Concat(comparisonHits));
        Assert.NotNull(notice);
        Assert.False(notice.Unavailable);

        Assert.Single(notice.IssueTypes);
        Assert.Equal(3, notice.IssueTypes[0].Count);
        Assert.Equal(3, notice.AffectedRecords);
    }

    [Fact]
    public void CheckUnavailable_serializes_to_exact_contract_json()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(DataQualityNotice.CheckUnavailable, options);
        Assert.Equal("""{"issueTypes":[],"affectedRecords":0,"unavailable":true}""", json);
    }
}
