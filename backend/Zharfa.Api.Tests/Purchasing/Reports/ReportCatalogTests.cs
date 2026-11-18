namespace Zharfa.Api.Tests.Purchasing.Reports;

using System.Linq;
using Zharfa.Api.Purchasing.Reports;

/// <summary>
/// T022 (spec 004 data-model §4 "Report catalog", FR-016, FR-019, FR-020). Pure: no database, no network.
/// </summary>
public class ReportCatalogTests
{
    private static ReportDefinition Get(string id)
    {
        Assert.True(ReportCatalog.TryGet(id, out var definition), id);
        return definition!;
    }

    [Fact]
    public void The_seven_report_ids_exist()
    {
        foreach (var id in new[] { "by-vendor", "by-product", "by-category", "trend", "top-products", "returns", "transactions" })
        {
            Assert.Equal(id, Get(id).Id);
        }
    }

    [Fact]
    public void Unknown_report_id_is_rejected()
    {
        Assert.False(ReportCatalog.TryGet("drop-table", out var definition));
        Assert.Null(definition);
    }

    [Theory]
    [InlineData("by-vendor", ReportGrouping.Vendor)]
    [InlineData("by-product", ReportGrouping.Product)]
    [InlineData("by-category", ReportGrouping.Category)]
    [InlineData("trend", ReportGrouping.Month)]
    [InlineData("top-products", ReportGrouping.Product)]
    [InlineData("returns", ReportGrouping.VendorProduct)]
    [InlineData("transactions", ReportGrouping.Document)]
    public void Each_report_has_its_grouping(string id, ReportGrouping grouping)
    {
        Assert.Equal(grouping, Get(id).Grouping);
    }

    [Theory]
    [InlineData("by-vendor", "product", "category")]
    [InlineData("by-product", "vendor", "category")]
    [InlineData("by-category", "vendor")]
    [InlineData("trend", "vendor", "product", "category")]
    [InlineData("top-products", "vendor", "category")]
    [InlineData("returns", "vendor", "product", "category")]
    [InlineData("transactions", "vendor", "product", "category", "docType")]
    public void Each_report_declares_exactly_its_applicable_filters(string id, params string[] filters)
    {
        Assert.Equal(filters.OrderBy(f => f, StringComparer.Ordinal), Get(id).Filters.OrderBy(f => f, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("by-vendor", "net", true)]
    [InlineData("by-product", "net", true)]
    [InlineData("by-category", "net", true)]
    [InlineData("trend", "month", false)]
    [InlineData("top-products", "net", true)]
    [InlineData("returns", "returns", true)]
    [InlineData("transactions", "date", true)]
    public void Default_sort_is_the_first_key_with_its_direction(string id, string key, bool descending)
    {
        var first = Get(id).Sorts[0];

        Assert.Equal(key, first.Name);
        Assert.Equal(descending, first.Descending);
    }

    [Theory]
    [InlineData("by-vendor", "net", "gross", "returns", "vat", "quantity", "documents", "name")]
    [InlineData("by-product", "net", "gross", "returns", "vat", "quantity", "name")]
    [InlineData("by-category", "net", "gross", "returns", "vat", "quantity", "name")]
    [InlineData("trend", "month", "net")]
    [InlineData("top-products", "net", "quantity")]
    [InlineData("returns", "returns", "quantity", "vendor", "product")]
    [InlineData("transactions", "date", "number", "vendor", "net")]
    public void Sort_allowlist_is_exactly_the_catalog_keys(string id, params string[] keys)
    {
        Assert.Equal(keys, Get(id).Sorts.Select(s => s.Name).ToArray());
    }

    [Theory]
    [InlineData("by-vendor", SearchTarget.Vendor)]
    [InlineData("by-product", SearchTarget.Product)]
    [InlineData("by-category", SearchTarget.Category)]
    [InlineData("trend", SearchTarget.VendorOrProduct)]
    [InlineData("top-products", SearchTarget.Product)]
    [InlineData("returns", SearchTarget.VendorOrProduct)]
    [InlineData("transactions", SearchTarget.DocumentOrVendor)]
    public void Search_target_per_report(string id, SearchTarget target)
    {
        Assert.Equal(target, Get(id).SearchTarget);
    }

    [Fact]
    public void Every_report_is_paged_including_trend()
    {
        foreach (var id in new[] { "by-vendor", "by-product", "by-category", "trend", "top-products", "returns", "transactions" })
        {
            Assert.True(Get(id).Paged, id);
        }
    }

    [Fact]
    public void Compare_is_applicable_on_every_report_except_transactions()
    {
        foreach (var id in new[] { "by-vendor", "by-product", "by-category", "trend", "top-products", "returns" })
        {
            Assert.True(Get(id).Compare, id);
        }

        Assert.False(Get("transactions").Compare);
    }

    [Fact]
    public void Only_transactions_prints_in_landscape()
    {
        Assert.True(Get("transactions").Landscape);
        Assert.All(new[] { "by-vendor", "by-product", "by-category", "trend", "top-products", "returns" },
            id => Assert.False(Get(id).Landscape));
    }

    [Fact]
    public void Vendor_directory_lists_sort_and_compare_without_being_a_report()
    {
        var directory = ReportCatalog.Directory;

        Assert.Equal("vendors", directory.Id);
        Assert.Equal(new[] { "net", "name", "code", "lastPurchase" }, directory.Sorts.Select(s => s.Name).ToArray());
        Assert.True(directory.Sorts[0].Descending);
        Assert.True(directory.Compare);
        Assert.True(directory.Paged);
        Assert.False(ReportCatalog.TryGet("vendors", out _));
    }

    [Fact]
    public void Documents_list_sorts_and_filters_and_refuses_compare()
    {
        var documents = ReportCatalog.Documents;

        Assert.Equal("documents", documents.Id);
        Assert.Equal(new[] { "date", "number", "vendor", "net" }, documents.Sorts.Select(s => s.Name).ToArray());
        Assert.True(documents.Sorts[0].Descending);
        Assert.Equal(
            new[] { "vendor", "product", "category", "month", "docType" }.OrderBy(f => f, StringComparer.Ordinal),
            documents.Filters.OrderBy(f => f, StringComparer.Ordinal));
        Assert.Equal(SearchTarget.DocumentOrVendor, documents.SearchTarget);
        Assert.False(documents.Compare);
    }

    [Fact]
    public void Sort_keys_are_plain_identifiers_so_no_user_text_reaches_sql()
    {
        foreach (var id in new[] { "by-vendor", "by-product", "by-category", "trend", "top-products", "returns", "transactions" })
        {
            Assert.All(Get(id).Sorts, sort => Assert.Matches("^[a-z][a-zA-Z]*$", sort.Name));
        }
    }
}
