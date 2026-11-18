namespace Zharfa.Api.Tests.Purchasing.Reports;

using System;
using System.Collections.Generic;
using System.Linq;
using Zharfa.Api.Analytics;
using Zharfa.Api.Purchasing.Reports;

/// <summary>
/// T023 (spec 004 contracts/api.md §0, data-model §4 "ReportFilter", FR-019, FR-020): request validation
/// for the report endpoints. Pure: no database, no network. The SQL text itself is covered by the
/// ReportSql tests (catalog templates only, no user text).
/// </summary>
public class ReportFilterTests
{
    // Today is in Aban 1405, so presets ending later are trimmed.
    private static readonly JalaliDate Today = new(1405, 8, 25);

    private const string NotApplicable = "این فیلتر برای این گزارش معتبر نیست";

    private static ReportDefinition Resolve(string id) => id switch
    {
        "vendors" => ReportCatalog.Directory,
        "documents" => ReportCatalog.Documents,
        _ => ReportCatalog.TryGet(id, out var def) ? def! : throw new InvalidOperationException(id),
    };

    private static Dictionary<string, string?> Query(params (string Key, string? Value)[] pairs)
    {
        var query = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            query[key] = value;
        }

        return query;
    }

    private static ReportFilter Parse(string reportId, params (string Key, string? Value)[] pairs)
    {
        var ok = ReportFilter.TryParse(Resolve(reportId), Query(pairs), Today, print: false, out var filter, out var error);
        Assert.True(ok, error);
        Assert.Null(error);
        return filter!;
    }

    private static string Reject(string reportId, params (string Key, string? Value)[] pairs)
    {
        var ok = ReportFilter.TryParse(Resolve(reportId), Query(pairs), Today, print: false, out var filter, out var error);
        Assert.False(ok);
        Assert.Null(filter);
        Assert.False(string.IsNullOrWhiteSpace(error));
        return error!;
    }

    [Theory]
    [InlineData("by-vendor", "vendor", "V-102")]
    [InlineData("by-product", "product", "P-9")]
    [InlineData("by-category", "category", "C-1")]
    [InlineData("by-category", "product", "P-9")]
    [InlineData("top-products", "product", "P-9")]
    [InlineData("returns", "docType", "return")]
    [InlineData("vendors", "vendor", "V-102")]
    [InlineData("trend", "docType", "invoice")]
    public void A_filter_not_declared_by_the_report_is_refused(string reportId, string key, string value)
    {
        Assert.Equal(NotApplicable, Reject(reportId, (key, value)));
    }

    [Fact]
    public void A_declared_filter_is_accepted_and_empty_values_are_absent()
    {
        var filter = Parse("by-product", ("vendor", "V-102"), ("category", ""));

        Assert.Equal("V-102", filter.VendorKey);
        Assert.Null(filter.CategoryKey);
    }

    [Fact]
    public void Unknown_sort_key_is_refused()
    {
        var error = Reject("by-vendor", ("sort", "drop_table"));

        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void A_sort_key_outside_the_reports_allowlist_is_refused()
    {
        Reject("vendors", ("sort", "documents"));
        Reject("trend", ("sort", "name"));
    }

    [Fact]
    public void Default_sort_is_the_first_allowlist_key_in_its_default_direction()
    {
        var filter = Parse("by-vendor");

        Assert.Equal("net", filter.Sort);
        Assert.True(filter.Descending);
    }

    [Fact]
    public void A_text_sort_key_defaults_to_ascending_and_dir_overrides_the_default()
    {
        var byName = Parse("by-vendor", ("sort", "name"));
        Assert.Equal("name", byName.Sort);
        Assert.False(byName.Descending);

        var reversed = Parse("by-vendor", ("sort", "net"), ("dir", "asc"));
        Assert.False(reversed.Descending);
    }

    [Theory]
    [InlineData("sideways")]
    [InlineData("up")]
    public void Dir_accepts_only_asc_or_desc(string dir)
    {
        Reject("by-vendor", ("dir", dir));
    }

    [Fact]
    public void Dir_asc_and_desc_are_accepted()
    {
        Assert.False(Parse("by-vendor", ("dir", "asc")).Descending);
        Assert.True(Parse("by-vendor", ("dir", "desc")).Descending);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    public void Page_must_be_a_whole_number_of_at_least_one(string page)
    {
        Reject("by-vendor", ("page", page));
    }

    [Fact]
    public void Page_defaults_to_one()
    {
        Assert.Equal(1, Parse("by-vendor").Page);
    }

    [Theory]
    [InlineData("10")]
    [InlineData("75")]
    [InlineData("1000")]
    public void Page_size_must_be_25_50_or_100(string pageSize)
    {
        Reject("by-vendor", ("pageSize", pageSize));
    }

    [Theory]
    [InlineData("25", 25)]
    [InlineData("50", 50)]
    [InlineData("100", 100)]
    public void Page_size_accepts_the_three_allowed_values(string pageSize, int expected)
    {
        Assert.Equal(expected, Parse("by-vendor", ("pageSize", pageSize)).PageSize);
    }

    [Fact]
    public void Page_size_defaults_to_25()
    {
        Assert.Equal(25, Parse("by-vendor").PageSize);
    }

    [Fact]
    public void Effective_page_is_clamped_to_the_last_page()
    {
        // 312 rows at 25 per page = 13 pages.
        Assert.Equal(13, ReportFilter.EffectivePage(99, 25, 312));
        Assert.Equal(13, ReportFilter.EffectivePage(13, 25, 312));
        Assert.Equal(1, ReportFilter.EffectivePage(1, 25, 312));
        Assert.Equal(1, ReportFilter.EffectivePage(1, 25, 1));
    }

    [Fact]
    public void Zero_rows_always_show_page_one()
    {
        Assert.Equal(1, ReportFilter.EffectivePage(5, 25, 0));
        Assert.Equal(1, ReportFilter.EffectivePage(1, 100, 0));
    }

    [Fact]
    public void Trend_with_37_buckets_pages_at_size_25_and_clamps_page_three_to_two()
    {
        Assert.Equal(1, ReportFilter.EffectivePage(1, 25, 37));
        Assert.Equal(2, ReportFilter.EffectivePage(2, 25, 37));
        Assert.Equal(2, ReportFilter.EffectivePage(3, 25, 37));
    }

    [Fact]
    public void Trend_accepts_page_and_page_size_like_every_report()
    {
        var filter = Parse("trend", ("page", "2"), ("pageSize", "25"));

        Assert.Equal(2, filter.Page);
        Assert.Equal(25, filter.PageSize);
    }

    [Fact]
    public void Unknown_key_placeholder_is_accepted_for_vendor_product_and_category_only()
    {
        Assert.Equal("~unknown", Parse("by-product", ("vendor", "~unknown")).VendorKey);
        Assert.Equal("~unknown", Parse("by-vendor", ("product", "~unknown")).ProductKey);
        Assert.Equal("~unknown", Parse("by-vendor", ("category", "~unknown")).CategoryKey);
        Assert.Equal("~unknown", Parse("documents", ("vendor", "~unknown")).VendorKey);
    }

    [Fact]
    public void Unknown_key_placeholder_is_not_a_document_type()
    {
        Reject("transactions", ("docType", "~unknown"));
    }

    [Fact]
    public void Month_is_the_period_value_when_the_period_is_a_month()
    {
        var filter = Parse("documents", ("month", "1405-07"));

        Assert.Equal(JalaliPeriodKind.Month, filter.Period.Kind);
        Assert.Equal(14050701, filter.Period.FromKey);
        Assert.Null(filter.Month);
    }

    [Fact]
    public void Month_drill_filter_is_accepted_inside_a_year_and_normalized_to_latin_digits()
    {
        var filter = Parse("documents", ("period", "year"), ("year", "1405"), ("month", "۱۴۰۵-۰۳"));

        Assert.Equal("1405-03", filter.Month);
        Assert.Equal(JalaliPeriodKind.Year, filter.Period.Kind);
    }

    [Fact]
    public void Month_drill_filter_must_lie_within_the_period()
    {
        var error = Reject("documents", ("period", "quarter"), ("quarter", "1405-Q1"), ("month", "1405-04"));

        Assert.Equal("ماه انتخاب‌شده خارج از بازه گزارش است", error);
    }

    [Theory]
    [InlineData("1405-13")]
    [InlineData("1405/02")]
    [InlineData("1405")]
    public void Month_drill_filter_must_be_a_valid_yyyy_mm(string month)
    {
        Reject("documents", ("period", "year"), ("year", "1405"), ("month", month));
    }

    [Fact]
    public void Month_is_not_a_filter_on_reports_that_do_not_declare_it()
    {
        Reject("by-vendor", ("period", "year"), ("year", "1405"), ("month", "1405-03"));
    }

    [Theory]
    [InlineData("invoice")]
    [InlineData("return")]
    public void Document_type_accepts_invoice_or_return(string docType)
    {
        Assert.Equal(docType, Parse("transactions", ("docType", docType)).DocType);
    }

    [Fact]
    public void Document_type_rejects_other_values_and_undeclared_reports()
    {
        Reject("transactions", ("docType", "sale"));
        Reject("by-vendor", ("docType", "invoice"));
    }

    [Fact]
    public void Search_is_trimmed_and_whitespace_only_means_no_search()
    {
        Assert.Equal("دارو", Parse("by-vendor", ("q", "  دارو  ")).Search);
        Assert.Null(Parse("by-vendor", ("q", "   ")).Search);
        Assert.Null(Parse("by-vendor", ("q", null)).Search);
    }

    [Fact]
    public void Search_accepts_up_to_100_characters_and_refuses_more()
    {
        var hundred = new string('ا', 100);

        Assert.Equal(hundred, Parse("by-vendor", ("q", hundred)).Search);
        Reject("by-vendor", ("q", new string('ا', 101)));
    }

    [Fact]
    public void Search_is_accepted_on_all_seven_reports_and_both_lists()
    {
        foreach (var id in new[] { "by-vendor", "by-product", "by-category", "trend", "top-products", "returns", "transactions", "vendors", "documents" })
        {
            Assert.Equal("x", Parse(id, ("q", "x")).Search);
        }
    }

    [Fact]
    public void Trend_search_is_accepted_and_paged()
    {
        var filter = Parse("trend", ("q", "ی"), ("page", "1"));

        Assert.Equal("ی", filter.Search);
    }

    [Fact]
    public void Like_escaping_protects_percent_underscore_and_backslash()
    {
        Assert.Equal(@"50\%\_\\x", ReportFilter.EscapeLike(@"50%_\x"));
        Assert.Equal("plain", ReportFilter.EscapeLike("plain"));
    }

    [Fact]
    public void Match_is_accepted_only_on_documents()
    {
        Assert.Equal("ماهان", Parse("documents", ("match", "ماهان")).Match);

        Reject("trend", ("match", "ماهان"));
        Reject("transactions", ("match", "ماهان"));
    }

    [Fact]
    public void Compare_defaults_to_false_and_is_accepted_on_the_comparable_reports()
    {
        Assert.False(Parse("by-vendor").Compare);

        foreach (var id in new[] { "by-vendor", "by-product", "by-category", "trend", "top-products", "returns", "vendors" })
        {
            Assert.True(Parse(id, ("compare", "true")).Compare, id);
        }
    }

    [Theory]
    [InlineData("transactions")]
    [InlineData("documents")]
    public void Compare_is_refused_on_transactions_and_documents(string reportId)
    {
        Assert.Equal(NotApplicable, Reject(reportId, ("compare", "true")));
    }

    [Fact]
    public void Compare_must_be_true_or_false()
    {
        Reject("by-vendor", ("compare", "yes"));
    }

    [Fact]
    public void Period_errors_are_refused_with_the_period_message()
    {
        Reject("by-vendor", ("period", "week"));
        Reject("by-vendor", ("period", "custom"), ("from", "1405-08-24"), ("to", "1405-08-10"));
    }

    [Fact]
    public void Print_mode_ignores_paging_parameters_and_still_validates_the_rest()
    {
        var ok = ReportFilter.TryParse(
            ReportCatalog.TryGet("transactions", out var def) ? def! : throw new InvalidOperationException(),
            Query(("page", "abc"), ("pageSize", "7"), ("vendor", "V-102")),
            Today,
            print: true,
            out var filter,
            out var error);

        Assert.True(ok, error);
        Assert.True(filter!.Print);
        Assert.Equal("V-102", filter.VendorKey);

        var rejected = ReportFilter.TryParse(
            ReportCatalog.TryGet("by-vendor", out var byVendor) ? byVendor! : throw new InvalidOperationException(),
            Query(("sort", "drop_table")),
            Today,
            print: true,
            out _,
            out var printError);

        Assert.False(rejected);
        Assert.False(string.IsNullOrWhiteSpace(printError));
    }

    [Fact]
    public void Every_report_id_resolves_to_its_catalog_entry()
    {
        var ids = new[] { "by-vendor", "by-product", "by-category", "trend", "top-products", "returns", "transactions" };

        Assert.All(ids, id => Assert.True(ReportCatalog.TryGet(id, out var def) && def!.Id == id));
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }
}
