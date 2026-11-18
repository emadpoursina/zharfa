namespace Zharfa.Api.Purchasing.Reports;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Grouping of a report's rows (data-model §4 catalog).</summary>
public enum ReportGrouping
{
    Vendor,
    Product,
    Category,
    VendorProduct,
    Month,
    Document,
}

/// <summary>Search target of a report or list (data-model §4 "Search on").</summary>
public enum SearchTarget
{
    Vendor,
    Product,
    Category,
    VendorOrProduct,
    DocumentOrVendor,
}

/// <summary>Filter names accepted by the catalog. Period and search are common to every report.</summary>
public static class ReportFilterNames
{
    public const string Vendor = "vendor";
    public const string Product = "product";
    public const string Category = "category";
    public const string Month = "month";
    public const string DocType = "docType";
}

/// <summary>
/// One allowlisted sort key. <see cref="Name"/> is a plain identifier that the SQL layer maps to a fixed
/// expression; it is never taken from user text. <see cref="Descending"/> is the key's default direction.
/// </summary>
public sealed record SortKey(string Name, bool Descending);

/// <summary>One report (or list) of the purchasing catalog. The first sort key is the default.</summary>
public sealed record ReportDefinition(
    string Id,
    ReportGrouping Grouping,
    IReadOnlyList<string> Filters,
    IReadOnlyList<SortKey> Sorts,
    SearchTarget SearchTarget,
    bool Paged,
    bool Compare,
    bool Landscape);

/// <summary>
/// The purchasing report catalog (data-model.md §4, contracts/api.md §3). Pure: no SQL, no I/O.
/// Only these ids are routable; anything else is rejected before a query is built.
/// </summary>
public static class ReportCatalog
{
    private static readonly IReadOnlyList<ReportDefinition> Reports = new[]
    {
        new ReportDefinition(
            "by-vendor", ReportGrouping.Vendor,
            new[] { ReportFilterNames.Product, ReportFilterNames.Category },
            new[] { new SortKey("net", true), new SortKey("gross", true), new SortKey("returns", true), new SortKey("vat", true), new SortKey("quantity", true), new SortKey("documents", true), new SortKey("name", false) },
            SearchTarget.Vendor, Paged: true, Compare: true, Landscape: false),
        new ReportDefinition(
            "by-product", ReportGrouping.Product,
            new[] { ReportFilterNames.Vendor, ReportFilterNames.Category },
            new[] { new SortKey("net", true), new SortKey("gross", true), new SortKey("returns", true), new SortKey("vat", true), new SortKey("quantity", true), new SortKey("name", false) },
            SearchTarget.Product, Paged: true, Compare: true, Landscape: false),
        new ReportDefinition(
            "by-category", ReportGrouping.Category,
            new[] { ReportFilterNames.Vendor },
            new[] { new SortKey("net", true), new SortKey("gross", true), new SortKey("returns", true), new SortKey("vat", true), new SortKey("quantity", true), new SortKey("name", false) },
            SearchTarget.Category, Paged: true, Compare: true, Landscape: false),
        new ReportDefinition(
            "trend", ReportGrouping.Month,
            new[] { ReportFilterNames.Vendor, ReportFilterNames.Product, ReportFilterNames.Category },
            new[] { new SortKey("month", false), new SortKey("net", true) },
            SearchTarget.VendorOrProduct, Paged: true, Compare: true, Landscape: false),
        new ReportDefinition(
            "top-products", ReportGrouping.Product,
            new[] { ReportFilterNames.Vendor, ReportFilterNames.Category },
            new[] { new SortKey("net", true), new SortKey("quantity", true) },
            SearchTarget.Product, Paged: true, Compare: true, Landscape: false),
        new ReportDefinition(
            "returns", ReportGrouping.VendorProduct,
            new[] { ReportFilterNames.Vendor, ReportFilterNames.Product, ReportFilterNames.Category },
            new[] { new SortKey("returns", true), new SortKey("quantity", false), new SortKey("vendor", false), new SortKey("product", false) },
            SearchTarget.VendorOrProduct, Paged: true, Compare: true, Landscape: false),
        new ReportDefinition(
            "transactions", ReportGrouping.Document,
            new[] { ReportFilterNames.Vendor, ReportFilterNames.Product, ReportFilterNames.Category, ReportFilterNames.DocType },
            new[] { new SortKey("date", true), new SortKey("number", false), new SortKey("vendor", false), new SortKey("net", true) },
            SearchTarget.DocumentOrVendor, Paged: true, Compare: false, Landscape: true),
    };

    /// <summary>Vendor directory (a list, not a report): search vendor name or code, sort allowlist, compare on.</summary>
    public static ReportDefinition Directory { get; } = new(
        "vendors", ReportGrouping.Vendor,
        Array.Empty<string>(),
        new[] { new SortKey("net", true), new SortKey("name", false), new SortKey("code", false), new SortKey("lastPurchase", true) },
        SearchTarget.Vendor, Paged: true, Compare: true, Landscape: false);

    /// <summary>Purchase documents list (drill-down): filters, search on document number or vendor name, no compare.</summary>
    public static ReportDefinition Documents { get; } = new(
        "documents", ReportGrouping.Document,
        new[] { ReportFilterNames.Vendor, ReportFilterNames.Product, ReportFilterNames.Category, ReportFilterNames.Month, ReportFilterNames.DocType },
        new[] { new SortKey("date", true), new SortKey("number", false), new SortKey("vendor", false), new SortKey("net", true) },
        SearchTarget.DocumentOrVendor, Paged: true, Compare: false, Landscape: false);

    /// <summary>Looks up one of the seven reports. Unknown ids return false.</summary>
    public static bool TryGet(string? id, out ReportDefinition? definition)
    {
        definition = Reports.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));
        return definition is not null;
    }
}
