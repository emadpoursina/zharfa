namespace Zharfa.Api.Purchasing.Reports;

using System;
using System.Collections.Generic;
using Npgsql;
using NpgsqlTypes;

/// <summary>
/// The values of one report query. Keys are source keys or <c>~unknown</c>; the service has already
/// validated them against the catalog (ReportFilter). Every value is bound as a parameter.
/// </summary>
public sealed record ReportQueryScope(
    int FromKey,
    int ToKey,
    string Basis,
    string? VendorKey = null,
    string? ProductKey = null,
    string? CategoryKey = null,
    string? DocType = null,
    string? Search = null,
    string? Match = null);

/// <summary>
/// Fixed SQL templates for the purchasing reports (spec 004 T028, research R1, data-model §4). Every query
/// reads only <c>mabna.fn_purchase_lines(@fromKey, @toKey, @basis)</c> (the single line-amount definition)
/// joined to the masters. Filters and search are optional predicates driven by parameters, so the SQL text
/// never changes with user input. Sort keys map to fixed output aliases from the catalog allowlist.
/// Amounts: gross = Σ invoice line amount, returns = Σ return line amount, net = gross − returns (Phase 1).
/// </summary>
public static class ReportSql
{
    public const string UnknownKey = ReportFilter.UnknownKey;

    // Vendor, product and category filters and the document type, at line level (a vendor filter selects
    // every line of its documents). `~unknown` = the row has no master entry (NULL id).
    private const string FilterPredicate = @"
        (NOT @hasVendor::boolean OR (@vendorUnknown::boolean AND l.vendor_id IS NULL)
            OR (NOT @vendorUnknown::boolean AND l.vendor_source_key = @vendorKey::text))
    AND (NOT @hasProduct::boolean OR (@productUnknown::boolean AND l.product_id IS NULL)
            OR (NOT @productUnknown::boolean AND l.product_source_key = @productKey::text))
    AND (NOT @hasCategory::boolean OR (@categoryUnknown::boolean AND l.category_id IS NULL)
            OR (NOT @categoryUnknown::boolean AND l.category_key = @categoryKey::text))
    AND (@docType::text IS NULL OR l.doc_type = @docType::text)";

    // base: the lines in scope with the joined masters and their search keys.
    // scoped: adds the free-text search (q) and the trend drill match, both line-level.
    private const string BaseCte = @"
WITH base AS (
    SELECT l.doc_type, l.document_id, l.line_id, l.jalali_year, l.jalali_month, l.jalali_key,
           l.vendor_id, l.vendor_source_key, l.product_id, l.product_source_key,
           l.category_id, l.category_key, l.quantity, l.gross_amount, l.discount_amount,
           l.line_amount, l.vat_amount, l.sign,
           d.doc_number, d.doc_date,
           v.name_fa AS vendor_name, v.code AS vendor_code, v.search_key AS vendor_search,
           p.name_fa AS product_name, p.search_key AS product_search,
           c.name_fa AS category_name, c.source_key AS category_source_key, c.search_key AS category_search,
           mabna.fn_search_key(d.doc_number) AS doc_search
    FROM mabna.fn_purchase_lines(@fromKey::int, @toKey::int, @basis::text) l
    JOIN mabna.purchase_document d ON d.document_id = l.document_id
    LEFT JOIN mabna.vendor v ON v.vendor_id = l.vendor_id
    LEFT JOIN mabna.product p ON p.product_id = l.product_id
    LEFT JOIN mabna.product_category c ON c.category_id = l.category_id
    WHERE " + FilterPredicate + @"
),
scoped AS (
    SELECT * FROM base
    WHERE (@q::text IS NULL OR {SEARCH})
      AND (@match::text IS NULL OR (base.vendor_search LIKE '%' || mabna.fn_search_key(@match::text) || '%' ESCAPE '\'
           OR base.product_search LIKE '%' || mabna.fn_search_key(@match::text) || '%' ESCAPE '\'))
)";

    // Search predicates by catalog target. `@q` is the LIKE-escaped, trimmed search text.
    private const string VendorSearch = @"base.vendor_search LIKE '%' || mabna.fn_search_key(@q::text) || '%' ESCAPE '\'";
    private const string ProductSearch = @"base.product_search LIKE '%' || mabna.fn_search_key(@q::text) || '%' ESCAPE '\'";
    private const string CategorySearch = @"base.category_search LIKE '%' || mabna.fn_search_key(@q::text) || '%' ESCAPE '\'";
    private const string VendorOrProductSearch = "(" + VendorSearch + " OR " + ProductSearch + ")";
    private const string DocumentOrVendorSearch =
        @"(base.doc_search LIKE '%' || mabna.fn_search_key(@q::text) || '%' ESCAPE '\' OR " + VendorSearch + ")";

    /// <summary>The search predicate of a catalog search target (fixed text, never user input).</summary>
    public static string SearchPredicate(SearchTarget target) => target switch
    {
        SearchTarget.Vendor => VendorSearch,
        SearchTarget.Product => ProductSearch,
        SearchTarget.Category => CategorySearch,
        SearchTarget.VendorOrProduct => VendorOrProductSearch,
        SearchTarget.DocumentOrVendor => DocumentOrVendorSearch,
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    // Measures over the scoped lines (the same figures as Phase 1: gross = invoice lines, returns = return lines).
    private const string GroupMeasures = @"
    SUM(CASE WHEN doc_type = 'invoice' THEN line_amount ELSE 0 END) AS gross,
    SUM(CASE WHEN doc_type = 'return' THEN line_amount ELSE 0 END) AS returns,
    SUM(CASE WHEN doc_type = 'invoice' THEN line_amount ELSE 0 END)
      - SUM(CASE WHEN doc_type = 'return' THEN line_amount ELSE 0 END) AS net,
    SUM(CASE WHEN doc_type = 'invoice' THEN vat_amount ELSE 0 END)
      - SUM(CASE WHEN doc_type = 'return' THEN vat_amount ELSE 0 END) AS vat,
    SUM(CASE WHEN doc_type = 'invoice' THEN vat_amount ELSE 0 END) AS vat_invoice,
    SUM(CASE WHEN doc_type = 'return' THEN vat_amount ELSE 0 END) AS vat_return,
    SUM(sign * quantity) AS net_quantity,
    COUNT(*) FILTER (WHERE quantity IS NULL) AS quantity_missing_lines,
    COUNT(DISTINCT document_id) AS document_count,
    COUNT(*) AS line_count";

    /// <summary>Grouped report: the group columns, the key expressions and the sort allowlist (alias per key).</summary>
    private sealed record GroupDef(string GroupBy, string KeyExpr, string NameExpr, string? CodeExpr, string UnknownExpr, string SortKeyExpr, string Table);

    private static readonly Dictionary<string, GroupDef> Groups = new(StringComparer.Ordinal)
    {
        ["vendor"] = new(
            GroupBy: "vendor_id",
            KeyExpr: "CASE WHEN vendor_id IS NULL THEN NULL ELSE MAX(vendor_source_key) END",
            NameExpr: "MAX(vendor_name)",
            CodeExpr: "MAX(vendor_code)",
            UnknownExpr: "vendor_id IS NULL",
            SortKeyExpr: "COALESCE(CASE WHEN vendor_id IS NULL THEN NULL ELSE MAX(vendor_source_key) END, '')",
            Table: "scoped"),
        ["product"] = new(
            GroupBy: "product_id",
            KeyExpr: "CASE WHEN product_id IS NULL THEN NULL ELSE MAX(product_source_key) END",
            NameExpr: "MAX(product_name)",
            CodeExpr: null,
            UnknownExpr: "product_id IS NULL",
            SortKeyExpr: "COALESCE(CASE WHEN product_id IS NULL THEN NULL ELSE MAX(product_source_key) END, '')",
            Table: "scoped"),
        ["category"] = new(
            GroupBy: "category_id",
            KeyExpr: "CASE WHEN category_id IS NULL THEN NULL ELSE MAX(category_source_key) END",
            NameExpr: "MAX(category_name)",
            CodeExpr: null,
            UnknownExpr: "category_id IS NULL",
            SortKeyExpr: "COALESCE(CASE WHEN category_id IS NULL THEN NULL ELSE MAX(category_source_key) END, '')",
            Table: "scoped"),
    };

    /// <summary>Output alias of each sort key, per grouped report (catalog allowlist only).</summary>
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> GroupedSortAliases = new(StringComparer.Ordinal)
    {
        ["by-vendor"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["net"] = "net", ["gross"] = "gross", ["returns"] = "returns", ["vat"] = "vat",
            ["quantity"] = "net_quantity", ["documents"] = "document_count", ["name"] = "name",
        },
        ["by-product"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["net"] = "net", ["gross"] = "gross", ["returns"] = "returns", ["vat"] = "vat",
            ["quantity"] = "net_quantity", ["name"] = "name",
        },
        ["by-category"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["net"] = "net", ["gross"] = "gross", ["returns"] = "returns", ["vat"] = "vat",
            ["quantity"] = "net_quantity", ["name"] = "name",
        },
        ["top-products"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["net"] = "net", ["quantity"] = "net_quantity",
        },
    };

    /// <summary>Grouped SELECT of by-vendor, by-product, by-category or top-products (page or all groups).</summary>
    /// <param name="report">Catalog id.</param>
    /// <param name="paged">True: ordered, LIMIT/OFFSET bound. False: every group (used for the previous-period map).</param>
    public static string Grouped(string report, string sort, bool descending, bool paged)
    {
        var def = Groups[GroupFor(report)];
        var alias = GroupedSortAliases[report][sort];
        var direction = descending ? "DESC" : "ASC";
        var page = paged ? "LIMIT @limit::int OFFSET @offset::int" : string.Empty;
        return BaseCte.Replace("{SEARCH}", SearchPredicate(ReportCatalog.TryGet(report, out var def0) ? def0!.SearchTarget : SearchTarget.Vendor)) + @"
SELECT * FROM (
    SELECT " + def.KeyExpr + @" AS key,
           " + def.SortKeyExpr + @" AS sort_key,
           " + def.UnknownExpr + @" AS unknown,
           " + def.NameExpr + @" AS name," +
           (def.CodeExpr is null ? " NULL::text AS code," : " " + def.CodeExpr + " AS code,") + GroupMeasures + @"
    FROM " + def.Table + @"
    GROUP BY " + def.GroupBy + @"
) g
ORDER BY " + alias + " " + direction + @" NULLS LAST, sort_key COLLATE ""C""" + (paged ? "\n" + page : string.Empty);
    }

    // The grouped-report kind of each catalog id.
    private static string GroupFor(string report) => report switch
    {
        "by-vendor" => "vendor",
        "by-product" or "top-products" => "product",
        "by-category" => "category",
        _ => throw new ArgumentOutOfRangeException(nameof(report)),
    };

    private static string GroupColumn(string report) => GroupFor(report) switch
    {
        "vendor" => "vendor_id",
        "product" => "product_id",
        _ => "category_id",
    };

    /// <summary>
    /// Totals and group count over the scoped lines in one statement: <c>total_rows</c> is the number of groups
    /// (an unknown group counts as one), the measures are over every line, so they equal the sum of all groups.
    /// </summary>
    public static string GroupedTotals(string report) => BaseCte.Replace("{SEARCH}", SearchPredicate(ReportCatalog.TryGet(report, out var d) ? d!.SearchTarget : SearchTarget.Vendor)) + @"
SELECT (SELECT count(*) FROM (SELECT 1 FROM scoped GROUP BY " + GroupColumn(report) + @") g) AS total_rows, " + GroupMeasures.Trim() + @"
FROM scoped";

    /// <summary>Returns report: return lines only, vendor × product groups (dated by the return document).</summary>
    public static string Returns(string sort, bool descending, bool paged)
    {
        var alias = sort switch
        {
            "returns" => "returns",
            "quantity" => "quantity",
            "vendor" => "vendor_name",
            "product" => "product_name",
            _ => throw new ArgumentOutOfRangeException(nameof(sort)),
        };
        var direction = descending ? "DESC" : "ASC";
        var page = paged ? "LIMIT @limit::int OFFSET @offset::int" : string.Empty;
        return BaseCte.Replace("{SEARCH}", SearchPredicate(SearchTarget.VendorOrProduct)) + @"
SELECT * FROM (
    SELECT CASE WHEN vendor_id IS NULL THEN NULL ELSE MAX(vendor_source_key) END AS vendor_key,
           COALESCE(CASE WHEN vendor_id IS NULL THEN NULL ELSE MAX(vendor_source_key) END, '')
             || '|' || COALESCE(CASE WHEN product_id IS NULL THEN NULL ELSE MAX(product_source_key) END, '') AS sort_key,
           vendor_id IS NULL AS unknown_vendor,
           product_id IS NULL AS unknown_product,
           MAX(vendor_name) AS vendor_name,
           CASE WHEN product_id IS NULL THEN NULL ELSE MAX(product_source_key) END AS product_key,
           MAX(product_name) AS product_name,
           SUM(line_amount) AS returns,
           SUM(quantity) AS quantity,
           COUNT(DISTINCT document_id) AS document_count
    FROM scoped
    WHERE doc_type = 'return'
    GROUP BY vendor_id, product_id
) g
ORDER BY " + alias + " " + direction + @" NULLS LAST, sort_key COLLATE ""C""" + (paged ? "\n" + page : string.Empty);
    }

    /// <summary>Totals of the returns report: return amount, quantity and document count over every return line.</summary>
    public static string ReturnsTotals() => BaseCte.Replace("{SEARCH}", SearchPredicate(SearchTarget.VendorOrProduct)) + @"
SELECT (SELECT count(*) FROM (SELECT 1 FROM scoped WHERE doc_type = 'return' GROUP BY vendor_id, product_id) g) AS total_rows,
       COALESCE(SUM(line_amount) FILTER (WHERE doc_type = 'return'), 0) AS returns,
       COALESCE(SUM(quantity) FILTER (WHERE doc_type = 'return'), 0) AS quantity,
       COUNT(DISTINCT document_id) FILTER (WHERE doc_type = 'return') AS document_count
FROM scoped";

    /// <summary>Trend buckets: scoped lines grouped by Jalali month (the service fills the window and gaps).</summary>
    public static string Trend => BaseCte.Replace("{SEARCH}", SearchPredicate(SearchTarget.VendorOrProduct)) + @"
SELECT jalali_year, jalali_month, " + GroupMeasures.Trim() + @"
FROM scoped
GROUP BY jalali_year, jalali_month";

    /// <summary>Totals of the trend (every bucket): same measures over the scoped lines.</summary>
    public static string TrendTotals => BaseCte.Replace("{SEARCH}", SearchPredicate(SearchTarget.VendorOrProduct)) + @"
SELECT " + GroupMeasures.Trim() + @"
FROM scoped";

    /// <summary>Document list (transactions report and the documents list): in-scope sums per document.</summary>
    public static string Documents(string sort, bool descending, bool paged, SearchTarget target)
    {
        var alias = sort switch
        {
            "date" => "doc_date",
            "number" => "doc_number",
            "vendor" => "vendor_name",
            "net" => "net",
            _ => throw new ArgumentOutOfRangeException(nameof(sort)),
        };
        var direction = descending ? "DESC" : "ASC";
        return BaseCte.Replace("{SEARCH}", SearchPredicate(target)) + @"
SELECT * FROM (
    SELECT document_id,
           MAX(doc_number) AS doc_number,
           MAX(doc_date) AS doc_date,
           MAX(doc_type) AS doc_type,
           CASE WHEN MAX(vendor_id) IS NULL THEN NULL ELSE MAX(vendor_source_key) END AS vendor_key,
           MAX(vendor_name) AS vendor_name,
           SUM(gross_amount) AS pre_discount,
           SUM(discount_amount) AS discount,
           SUM(vat_amount) AS vat,
           SUM(line_amount) AS net,
           SUM(sign * line_amount) AS signed_net,
           SUM(sign * quantity) AS quantity,
           COUNT(*) AS line_count
    FROM scoped
    GROUP BY document_id
) g
ORDER BY " + alias + " " + direction + @" NULLS LAST, doc_number COLLATE ""C"", document_id" +
    (paged ? "\nLIMIT @limit::int OFFSET @offset::int" : string.Empty);
    }

    /// <summary>Totals of the document list over every in-scope line (same statement shape as the other totals).</summary>
    public static string DocumentTotals(SearchTarget target) => BaseCte.Replace("{SEARCH}", SearchPredicate(target)) + @"
SELECT (SELECT count(*) FROM (SELECT 1 FROM scoped GROUP BY document_id) g) AS total_rows,
       COALESCE(SUM(sign * line_amount), 0) AS net,
       COALESCE(SUM(line_amount) FILTER (WHERE doc_type = 'invoice'), 0) AS invoice_amount,
       COALESCE(SUM(line_amount) FILTER (WHERE doc_type = 'return'), 0) AS return_amount,
       COALESCE(SUM(sign * quantity), 0) AS net_quantity,
       COUNT(*) AS line_count,
       COUNT(DISTINCT document_id) AS document_count,
       COUNT(*) FILTER (WHERE quantity IS NULL) AS quantity_missing_lines
FROM scoped";

    /// <summary>The lines of one document in scope (not paged; the service caps them at 1,000).</summary>
    public const string Lines = BaseCteLines + @"
SELECT line_id, product_source_key, product_name, category_name, product_id IS NULL AS unknown_product,
       quantity, gross_amount, discount_amount, line_amount, vat_amount, sign
FROM scoped
WHERE document_id = @documentId::bigint
ORDER BY line_id
LIMIT 1000";

    // Lines of one document: product, category and match filters only (document-level filters do not apply).
    private const string BaseCteLines = @"
WITH base AS (
    SELECT l.doc_type, l.document_id, l.line_id, l.jalali_year, l.jalali_month, l.jalali_key,
           l.vendor_id, l.vendor_source_key, l.product_id, l.product_source_key,
           l.category_id, l.category_key, l.quantity, l.gross_amount, l.discount_amount,
           l.line_amount, l.vat_amount, l.sign,
           d.doc_number, d.doc_date,
           v.name_fa AS vendor_name, v.code AS vendor_code, v.search_key AS vendor_search,
           p.name_fa AS product_name, p.search_key AS product_search,
           c.name_fa AS category_name, c.source_key AS category_source_key, c.search_key AS category_search,
           mabna.fn_search_key(d.doc_number) AS doc_search
    FROM mabna.fn_purchase_lines(0, 99991231, @basis::text) l
    JOIN mabna.purchase_document d ON d.document_id = l.document_id
    LEFT JOIN mabna.vendor v ON v.vendor_id = l.vendor_id
    LEFT JOIN mabna.product p ON p.product_id = l.product_id
    LEFT JOIN mabna.product_category c ON c.category_id = l.category_id
    WHERE (NOT @hasProduct::boolean OR (@productUnknown::boolean AND l.product_id IS NULL)
            OR (NOT @productUnknown::boolean AND l.product_source_key = @productKey::text))
      AND (NOT @hasCategory::boolean OR (@categoryUnknown::boolean AND l.category_id IS NULL)
            OR (NOT @categoryUnknown::boolean AND l.category_key = @categoryKey::text))
),
scoped AS (
    SELECT * FROM base
    WHERE (@match::text IS NULL OR (base.vendor_search LIKE '%' || mabna.fn_search_key(@match::text) || '%' ESCAPE '\'
           OR base.product_search LIKE '%' || mabna.fn_search_key(@match::text) || '%' ESCAPE '\'))
)";

    /// <summary>
    /// The unfiltered header of one non-cancelled document, or no row. Used for the lines response and to
    /// look up a document id (404 when missing).
    /// </summary>
    public const string DocumentHeader = @"
SELECT d.document_id, d.doc_number, d.doc_date, d.doc_type,
       CASE WHEN d.vendor_id IS NULL THEN NULL ELSE d.vendor_source_key END AS vendor_key,
       v.name_fa AS vendor_name
FROM mabna.purchase_document d
LEFT JOIN mabna.vendor v ON v.vendor_id = d.vendor_id
WHERE d.document_id = @documentId::bigint AND NOT d.cancelled";

    /// <summary>Unfiltered amounts of one document, over every line (the header of the lines response).</summary>
    public const string DocumentAmounts = @"
SELECT COALESCE(SUM(l.gross_amount), 0) AS pre_discount,
       COALESCE(SUM(l.discount_amount), 0) AS discount,
       COALESCE(SUM(l.vat_amount), 0) AS vat,
       COALESCE(SUM(l.line_amount), 0) AS net,
       COALESCE(SUM(l.sign * l.line_amount), 0) AS signed_net,
       COALESCE(SUM(l.sign * l.quantity), 0) AS quantity,
       COUNT(*) AS line_count
FROM mabna.fn_purchase_lines(0, 99991231, @basis::text) l
WHERE l.document_id = @documentId::bigint";

    /// <summary>Vendor profile: return lines of the scope grouped by product (contracts/api.md §2, returns list).</summary>
    public static string ProfileReturnsByProduct => BaseCte.Replace("{SEARCH}", SearchPredicate(SearchTarget.VendorOrProduct)) + @"
SELECT * FROM (
    SELECT CASE WHEN product_id IS NULL THEN NULL ELSE MAX(product_source_key) END AS product_key,
           COALESCE(CASE WHEN product_id IS NULL THEN NULL ELSE MAX(product_source_key) END, '') AS sort_key,
           MAX(product_name) AS product_name,
           product_id IS NULL AS unknown,
           SUM(line_amount) AS returns,
           SUM(quantity) AS quantity
    FROM scoped
    WHERE doc_type = 'return'
    GROUP BY product_id
) g
ORDER BY returns DESC NULLS LAST, sort_key COLLATE ""C""";

    /// <summary>
    /// Vendor comparison, per vendor over the period (vendor source keys bound as a text array; period and basis
    /// bound as usual). Reads <c>fn_purchase_lines</c> directly, the same definition as every other report.
    /// </summary>
    public const string ComparisonTotals = @"
SELECT l.vendor_source_key AS key,
       SUM(CASE WHEN l.doc_type = 'invoice' THEN l.line_amount ELSE 0 END)
         - SUM(CASE WHEN l.doc_type = 'return' THEN l.line_amount ELSE 0 END) AS net,
       SUM(CASE WHEN l.doc_type = 'return' THEN l.line_amount ELSE 0 END) AS returns,
       SUM(l.sign * l.quantity) AS net_quantity,
       COUNT(DISTINCT l.document_id) AS document_count
FROM mabna.fn_purchase_lines(@fromKey::int, @toKey::int, @basis::text) l
WHERE l.vendor_source_key = ANY(@vendorKeys::text[])
GROUP BY l.vendor_source_key";

    /// <summary>Vendor comparison: net per vendor and Jalali month (the service fills the month window).</summary>
    public const string ComparisonTrend = @"
SELECT l.vendor_source_key AS key, l.jalali_year, l.jalali_month,
       SUM(CASE WHEN l.doc_type = 'invoice' THEN l.line_amount ELSE 0 END)
         - SUM(CASE WHEN l.doc_type = 'return' THEN l.line_amount ELSE 0 END) AS net
FROM mabna.fn_purchase_lines(@fromKey::int, @toKey::int, @basis::text) l
WHERE l.vendor_source_key = ANY(@vendorKeys::text[])
GROUP BY l.vendor_source_key, l.jalali_year, l.jalali_month";

    /// <summary>Binds the scope's parameters for a query template, with NULLs for absent values.</summary>
    public static void Bind(NpgsqlCommand cmd, ReportQueryScope scope, int? limit = null, int? offset = null)
    {
        cmd.Parameters.Add(Int("fromKey", scope.FromKey));
        cmd.Parameters.Add(Int("toKey", scope.ToKey));
        cmd.Parameters.Add(Text("basis", scope.Basis));
        Key(cmd, "Vendor", scope.VendorKey);
        Key(cmd, "Product", scope.ProductKey);
        Key(cmd, "Category", scope.CategoryKey);
        cmd.Parameters.Add(Text("docType", scope.DocType));
        cmd.Parameters.Add(Text("q", scope.Search));
        cmd.Parameters.Add(Text("match", scope.Match));
        if (limit is { } l)
        {
            cmd.Parameters.Add(Int("limit", l));
        }

        if (offset is { } o)
        {
            cmd.Parameters.Add(Int("offset", o));
        }
    }

    /// <summary>Binds the product, category and match filters of the document-lines query.</summary>
    public static void BindLines(NpgsqlCommand cmd, ReportQueryScope scope, long documentId)
    {
        cmd.Parameters.Add(Text("basis", scope.Basis));
        Key(cmd, "Product", scope.ProductKey);
        Key(cmd, "Category", scope.CategoryKey);
        cmd.Parameters.Add(Text("match", scope.Match));
        cmd.Parameters.Add(new NpgsqlParameter("documentId", NpgsqlDbType.Bigint) { Value = documentId });
    }

    /// <summary>Binds the document id, and the discount basis when the template needs it.</summary>
    public static void BindDocument(NpgsqlCommand cmd, long documentId, string? basis = null)
    {
        cmd.Parameters.Add(new NpgsqlParameter("documentId", NpgsqlDbType.Bigint) { Value = documentId });
        if (basis is not null)
        {
            cmd.Parameters.Add(Text("basis", basis));
        }
    }

    private static void Key(NpgsqlCommand cmd, string name, string? key)
    {
        var unknown = key == UnknownKey;
        cmd.Parameters.Add(new NpgsqlParameter($"has{name}", NpgsqlDbType.Boolean) { Value = key is not null });
        cmd.Parameters.Add(new NpgsqlParameter($"{char.ToLowerInvariant(name[0])}{name[1..]}Unknown", NpgsqlDbType.Boolean) { Value = unknown });
        cmd.Parameters.Add(Text($"{char.ToLowerInvariant(name[0])}{name[1..]}Key", unknown ? null : key));
    }

    private static NpgsqlParameter Int(string name, int value) =>
        new(name, NpgsqlDbType.Integer) { Value = value };

    private static NpgsqlParameter Text(string name, string? value) =>
        new(name, NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value };
}
