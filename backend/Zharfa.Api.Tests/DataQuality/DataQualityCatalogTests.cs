namespace Zharfa.Api.Tests.DataQuality;

using System.Collections.Generic;
using System.Linq;
using Zharfa.Api.Audit;
using Zharfa.Api.DataQuality;

/// <summary>
/// T074 (data-model §3.2–§3.3, research R13, contracts/api.md §4 and §9): the seven issue types, their labels, the
/// reports each one affects, the deferred sub-checks and the heuristic note. Pure: no database, no network.
/// </summary>
public class DataQualityCatalogTests
{
    private const string NetReason = "ستون مبلغ خالص ردیف در نقشهٔ منبع تأیید نشده است.";
    private const string TotalReason = "ستون جمع سند در نقشهٔ منبع تأیید نشده است.";
    private const string Heuristic = "بررسی تقریبی: یک نام کالا با چند کد کالا. بررسی بر پایهٔ کد کالا پس از تأیید ستون‌های کد در محل فعال می‌شود.";

    private static readonly string[] AllEleven =
    [
        "overview", "vendors", "by-vendor", "by-product", "by-category", "trend",
        "top-products", "returns", "transactions", "vendors-compare", "drill-down",
    ];

    private static bool HasPersian(string text) => text.Any(ch => ch is >= '\u0600' and <= '\u06FF');

    [Fact]
    public void Exactly_seven_issue_types_in_the_data_model_order_with_their_labels()
    {
        var expected = new (string Id, string Label)[]
        {
            ("missing-values", "مقادیر ناقص"),
            ("duplicate-records", "رکوردهای تکراری"),
            ("broken-relationships", "ارتباط‌های ناقص"),
            ("inconsistent-product-ids", "شناسه‌های ناسازگار کالا"),
            ("invalid-dates", "تاریخ‌های نامعتبر"),
            ("unexpected-quantities", "مقادیر غیرمنتظره"),
            ("inconsistent-amounts", "مبالغ ناسازگار"),
        };

        var actual = DataQualityCatalog.IssueTypes.Select(type => (type.Id, type.Label)).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Every_issue_type_has_a_non_empty_description()
    {
        Assert.All(DataQualityCatalog.IssueTypes, type => Assert.False(string.IsNullOrWhiteSpace(type.Description)));
    }

    [Fact]
    public void Every_affected_report_is_a_known_key_other_than_the_profile_and_the_lines()
    {
        foreach (var type in DataQualityCatalog.IssueTypes)
        {
            foreach (var key in type.AffectedReports)
            {
                Assert.Contains(key, ReportKeys.All);
                Assert.NotEqual(ReportKeys.VendorProfile, key);
                Assert.NotEqual(ReportKeys.DrillDownLines, key);
            }
        }
    }

    [Theory]
    [InlineData("missing-values", "overview", "vendors", "by-vendor", "by-product", "by-category", "trend", "top-products", "returns", "transactions", "vendors-compare", "drill-down", "vendor-financials", "vendor-financials-history", "vendor-financials-outstanding")]
    [InlineData("duplicate-records", "overview", "vendors", "by-vendor", "by-product", "by-category", "trend", "top-products", "returns", "transactions", "vendors-compare", "drill-down", "vendor-financials", "vendor-financials-history", "vendor-financials-outstanding")]
    [InlineData("broken-relationships", "overview", "vendors", "by-vendor", "by-product", "by-category", "top-products", "returns", "transactions", "vendors-compare", "drill-down", "vendor-financials", "vendor-financials-history", "vendor-financials-outstanding")]
    [InlineData("inconsistent-product-ids", "overview", "vendors", "by-product", "by-category", "top-products", "returns", "drill-down")]
    [InlineData("invalid-dates", "overview", "vendors", "by-vendor", "by-product", "by-category", "trend", "top-products", "returns", "transactions", "vendors-compare", "drill-down", "vendor-financials", "vendor-financials-history", "vendor-financials-outstanding")]
    [InlineData("unexpected-quantities", "vendors", "by-vendor", "by-product", "by-category", "top-products", "returns", "vendors-compare", "drill-down")]
    [InlineData("inconsistent-amounts", "overview", "vendors", "by-vendor", "by-product", "by-category", "trend", "top-products", "returns", "transactions", "vendors-compare", "drill-down", "vendor-financials", "vendor-financials-history", "vendor-financials-outstanding")]
    public void Affected_reports_equal_research_R13(string issueType, params string[] expected)
    {
        var actual = DataQualityCatalog.Find(issueType)!.AffectedReports;

        Assert.Equal(expected.OrderBy(key => key, StringComparer.Ordinal), actual.OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void Unexpected_quantities_is_not_mapped_to_overview_trend_or_transactions()
    {
        var reports = DataQualityCatalog.Find("unexpected-quantities")!.AffectedReports;

        Assert.DoesNotContain("overview", reports);
        Assert.DoesNotContain("trend", reports);
        Assert.DoesNotContain("transactions", reports);
    }

    [Fact]
    public void Broken_relationships_is_not_mapped_to_trend()
    {
        Assert.DoesNotContain("trend", DataQualityCatalog.Find("broken-relationships")!.AffectedReports);
    }

    [Fact]
    public void Every_field_code_used_by_the_rules_has_a_persian_label()
    {
        foreach (var rule in DataQualitySql.Rules)
        {
            var label = DataQualityCatalog.FieldLabel(rule.Field);

            Assert.NotEqual(rule.Field, label);
            Assert.True(HasPersian(label));
        }
    }

    [Fact]
    public void Inconsistent_amounts_lists_the_two_deferred_sub_checks_with_their_reasons()
    {
        var notChecked = DataQualityCatalog.Find("inconsistent-amounts")!.NotChecked;

        Assert.Equal(
            new[]
            {
                ("line-net-mismatch", NetReason),
                ("document-total-mismatch", TotalReason),
            },
            notChecked.Select(item => (item.Code, item.Reason)).ToArray());
    }

    [Fact]
    public void No_other_issue_type_has_deferred_sub_checks()
    {
        foreach (var type in DataQualityCatalog.IssueTypes.Where(type => type.Id != "inconsistent-amounts"))
        {
            Assert.Empty(type.NotChecked);
        }
    }

    [Fact]
    public void Only_inconsistent_product_ids_has_the_heuristic_note()
    {
        Assert.Equal(Heuristic, DataQualityCatalog.Find("inconsistent-product-ids")!.Heuristic);

        foreach (var type in DataQualityCatalog.IssueTypes.Where(type => type.Id != "inconsistent-product-ids"))
        {
            Assert.Null(type.Heuristic);
        }
    }

    [Fact]
    public void Notice_key_maps_the_profile_to_vendors_and_the_lines_to_the_document_list()
    {
        Assert.Equal("vendors", DataQualityCatalog.NoticeKey("vendor-profile"));
        Assert.Equal("drill-down", DataQualityCatalog.NoticeKey("drill-down-lines"));
        Assert.Equal("trend", DataQualityCatalog.NoticeKey("trend"));
        Assert.Equal("vendor-financials", DataQualityCatalog.NoticeKey("vendor-financials-detail"));
    }

    // ---- Phase 6 (T021): the five types whose rules can flag payable / cheque / payment rows ----

    private static readonly string[] FinancialReportKeys =
        ["vendor-financials", "vendor-financials-history", "vendor-financials-outstanding"];

    [Theory]
    [InlineData("missing-values")]
    [InlineData("duplicate-records")]
    [InlineData("broken-relationships")]
    [InlineData("invalid-dates")]
    [InlineData("inconsistent-amounts")]
    public void Types_that_can_flag_financial_rows_affect_the_financial_reports(string issueType)
    {
        var reports = DataQualityCatalog.Find(issueType)!.AffectedReports;

        foreach (var key in FinancialReportKeys)
        {
            Assert.Contains(key, reports);
        }
    }

    [Theory]
    [InlineData("inconsistent-product-ids")]
    [InlineData("unexpected-quantities")]
    public void Purchase_only_types_do_not_affect_the_financial_reports(string issueType)
    {
        var reports = DataQualityCatalog.Find(issueType)!.AffectedReports;

        foreach (var key in FinancialReportKeys)
        {
            Assert.DoesNotContain(key, reports);
        }
    }

    [Fact]
    public void Every_financial_report_key_maps_to_at_least_missing_values()
    {
        foreach (var key in new[] { "vendor-financials", "vendor-financials-detail", "vendor-financials-history", "vendor-financials-outstanding" })
        {
            Assert.Contains("missing-values", DataQualityCatalog.TypesFor(key));
        }
    }
}
