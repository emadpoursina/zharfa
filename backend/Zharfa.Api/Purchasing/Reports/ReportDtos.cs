namespace Zharfa.Api.Purchasing.Reports;

using System.Collections.Generic;
using System.Text.Json.Serialization;

// Response shapes of contracts/api.md §0–§7. Amounts and quantities are exact decimal strings with
// 4 decimals (Rial); an unavailable figure is null with its reason in unavailable[]. No DTO in this
// namespace has a balance, payment, credit, debt or paid property (FR-012; enforced by NoVendorFinancialsTests).

/// <summary>Last successful sync: ISO time with the sync zone offset, and Jalali text with Persian digits.</summary>
public sealed record ReportSyncTime(string At, string AtJalali);

/// <summary>A resolved period: kind, inclusive Jalali bounds, Persian label and the trim notice.</summary>
public sealed record ReportPeriodDto(string Kind, string From, string To, string Label, string? TrimmedTo);

/// <summary>One active filter with its Persian display label.</summary>
public sealed record ReportFilterEcho(string Name, string Value, string Label);

/// <summary>One issue type in a report's data-quality notice (contracts/api.md §6).</summary>
public sealed record DataQualityNoticeType(string Type, string Label, long Count);

/// <summary>
/// Server-computed data-quality notice for a report response (contracts/api.md §6, FR-027, FR-028).
/// Added as the last parameter of ReportEnvelope and response records.
/// </summary>
public sealed record DataQualityNotice(IReadOnlyList<DataQualityNoticeType> IssueTypes, long AffectedRecords, bool Unavailable = false)
{
    public static DataQualityNotice CheckUnavailable { get; } = new([], 0, true);
}

/// <summary>A figure that could not be computed, with its reason code.</summary>
public sealed record ReportUnavailable(string Figure, string Reason);

/// <summary>
/// The shared envelope of every data response (contracts/api.md §0). Built once per request by the report
/// service and copied field by field into the response records, so the JSON stays flat as the contract shows.
/// </summary>
public sealed record ReportEnvelope(
    string DataLabel,
    ReportSyncTime? LastSuccessfulSync,
    string Currency,
    string CurrencyLabel,
    string DiscountBasis,
    ReportPeriodDto Period,
    ReportPeriodDto? PreviousPeriod,
    IReadOnlyList<ReportFilterEcho> Filters,
    IReadOnlyList<ReportUnavailable> Unavailable,
    DataQualityNotice? DataQuality = null);

/// <summary>
/// Totals over the full filtered result (never the page). Fields not used by a report stay null and are
/// omitted from the JSON. Quantities are decimal strings; counts are integers.
/// </summary>
public sealed record ReportTotals(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Net = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Gross = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Returns = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Vat = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? VatInvoice = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? VatReturn = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NetQuantity = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? InvoiceAmount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReturnAmount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PreDiscount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Discount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? DocumentCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? LineCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? QuantityMissingLines = null);

/// <summary>Drill-down filters of a row (contracts/api.md §3). Unknown rows carry <c>~unknown</c>.</summary>
public sealed record ReportDrill(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Vendor = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Product = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Category = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Month = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Match = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DocType = null);

/// <summary>Grouped row of by-vendor, by-product, by-category and top-products (contracts/api.md §3).</summary>
public sealed record GroupedRow(
    string? Key,
    string Name,
    string? Code,
    bool Unknown,
    string? Net,
    string? Gross,
    string? Returns,
    string? Vat,
    string? NetQuantity,
    long DocumentCount,
    string? PreviousNet,
    string? Delta,
    string? Percent,
    ReportDrill Drill,
    int? Rank = null);

/// <summary>Trend row: one Jalali month bucket (contracts/api.md §3).</summary>
public sealed record TrendRow(
    string Month,
    string Label,
    string? Net,
    string? NetQuantity,
    bool Available,
    bool Partial,
    ReportDrill Drill,
    string? PreviousNet = null,
    string? Delta = null,
    string? Percent = null);

/// <summary>Returns row: vendor by product, return documents only (contracts/api.md §3).</summary>
public sealed record ReturnsRow(
    string? VendorKey,
    string VendorName,
    string? ProductKey,
    string ProductName,
    bool UnknownVendor,
    bool UnknownProduct,
    string? Returns,
    string? Quantity,
    long DocumentCount,
    string? PreviousReturns,
    string? Delta,
    string? Percent,
    ReportDrill Drill);

/// <summary>Document row of the transactions report and the documents list (contracts/api.md §5).</summary>
public sealed record DocumentRow(
    long DocumentId,
    string Number,
    string DocType,
    string DocTypeLabel,
    string Date,
    string DateJalali,
    string? VendorKey,
    string VendorName,
    string PreDiscount,
    string Discount,
    string Vat,
    string Net,
    string SignedNet,
    string Quantity,
    long LineCount);

/// <summary>One line of a document (contracts/api.md §6).</summary>
public sealed record LineRow(
    long LineId,
    string? ProductKey,
    string ProductName,
    string? CategoryName,
    bool UnknownProduct,
    string? Quantity,
    string PreDiscount,
    string Discount,
    string LineAmount,
    string Vat,
    string SignedAmount);

/// <summary>Lookup item for the filter bar (contracts/api.md §7).</summary>
public sealed record LookupItem(string? Key, string Name, string? Code);

/// <summary>Vendor directory row (contracts/api.md §1).</summary>
public sealed record VendorRow(
    string Key,
    string Name,
    string? Code,
    string? Net,
    string? LastPurchaseDate,
    string? LastPurchaseDateJalali,
    string? PreviousNet,
    string? Delta,
    string? Percent);

/// <summary>
/// A table response: the envelope, the page, the totals over every filtered row, and the rows of the page.
/// <c>Page</c> is the effective page after clamping.
/// </summary>
public sealed record ReportTablePage<TRow>(
    string DataLabel,
    ReportSyncTime? LastSuccessfulSync,
    string Currency,
    string CurrencyLabel,
    string DiscountBasis,
    ReportPeriodDto Period,
    ReportPeriodDto? PreviousPeriod,
    IReadOnlyList<ReportFilterEcho> Filters,
    IReadOnlyList<ReportUnavailable> Unavailable,
    int Page,
    int PageSize,
    long TotalRows,
    string Sort,
    string Dir,
    ReportTotals Totals,
    ReportTotals? PreviousTotals,
    IReadOnlyList<TRow> Rows,
    DataQualityNotice? DataQuality = null);

/// <summary>
/// A print response: the envelope, the totals, and at most 1,000 rows in the current sort
/// (contracts/api.md "Print twin", research R9).
/// </summary>
public sealed record ReportPrintPage<TRow>(
    string DataLabel,
    ReportSyncTime? LastSuccessfulSync,
    string Currency,
    string CurrencyLabel,
    string DiscountBasis,
    ReportPeriodDto Period,
    ReportPeriodDto? PreviousPeriod,
    IReadOnlyList<ReportFilterEcho> Filters,
    IReadOnlyList<ReportUnavailable> Unavailable,
    long TotalRows,
    bool Truncated,
    int Limit,
    ReportTotals Totals,
    ReportTotals? PreviousTotals,
    IReadOnlyList<TRow> Rows,
    DataQualityNotice? DataQuality = null);

/// <summary>
/// Document lines response (contracts/api.md §6): the envelope without the period, the unfiltered document
/// header, the lines in scope (not paged, capped at 1,000) and their totals.
/// </summary>
public sealed record DocumentLinesResponse(
    string DataLabel,
    ReportSyncTime? LastSuccessfulSync,
    string Currency,
    string CurrencyLabel,
    string DiscountBasis,
    IReadOnlyList<ReportFilterEcho> Filters,
    IReadOnlyList<ReportUnavailable> Unavailable,
    DocumentRow Document,
    IReadOnlyList<LineRow> Lines,
    ReportLineTotals Totals,
    DataQualityNotice? DataQuality = null);

/// <summary>Totals of a document's lines.</summary>
public sealed record ReportLineTotals(string LineAmount, string SignedAmount, string Quantity);
