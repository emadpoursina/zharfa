namespace Zharfa.Api.VendorFinancials;

using System.Text.Json.Serialization;
using Zharfa.Api.Purchasing.Reports;

// Response shapes of specs/008-vendor-financials/contracts/api.md §1–§5. Amounts are exact decimal strings with
// 4 places; the signed balance may start with '-'. An unavailable figure is null on its column and listed in
// `unavailable[]` with its reason and the Persian message — never "0". Nothing here is named profit, margin or
// cash received: the purchases/payments difference is purchasesMinusPayments («خرید منهای پرداخت») only.
// Every type lives in Zharfa.Api.VendorFinancials, never in Zharfa.Api.Purchasing.Reports (FR-012).

/// <summary>A resolved Jalali period for the financial responses (kind + preset value + inclusive bounds).</summary>
public sealed record VendorFinancialPeriodDto(
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Month,
    string From,
    string To,
    string Label);

/// <summary>Active filter echo of a financial request (vendor or search).</summary>
public sealed record VendorFinancialFilterEcho(string Name, string Value, string Label);

/// <summary>A figure that could not be computed, with its reason and the Persian message shown to the user.</summary>
public sealed record VendorFinancialUnavailable(string Figure, string Reason, string Message)
{
    public const string PersianMessage = "داده در دسترس نیست.";

    public static VendorFinancialUnavailable For(string figure, string reason) => new(figure, reason, PersianMessage);
}

/// <summary>Search state of the list (q as typed, normalized with the server's ی/ک + digits key).</summary>
public sealed record VendorFinancialSearchDto(string Q, string Normalized);

/// <summary>Sort state of the list: an allowlisted key and direction.</summary>
public sealed record VendorFinancialSortDto(string Key, string Dir);

/// <summary>One vendor of the Financials list (contracts/api.md §1).</summary>
public sealed record VendorFinancialSummaryRow(
    string Key,
    string Name,
    string? Code,
    string? VendorBalance,
    string? OutstandingPayments,
    string? PreviousVendorBalance,
    string? PreviousOutstandingPayments,
    string? DeltaVendorBalance,
    string? PercentVendorBalance,
    string? DeltaOutstanding,
    string? PercentOutstanding);

/// <summary>The Financials list envelope (contracts/api.md §1). <see cref="Available"/> is true: the endpoint is implemented.</summary>
public sealed record VendorFinancialsSummaryResponse(
    string DataLabel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DemoNotice,
    ReportSyncTime? LastSuccessfulSync,
    string Currency,
    string CurrencyLabel,
    VendorFinancialPeriodDto Period,
    VendorFinancialPeriodDto? PreviousPeriod,
    IReadOnlyList<VendorFinancialFilterEcho> Filters,
    IReadOnlyList<VendorFinancialUnavailable> Unavailable,
    bool Compare,
    int Page,
    int PageSize,
    long TotalRows,
    VendorFinancialSearchDto Search,
    VendorFinancialSortDto Sort,
    DataQualityNotice? DataQuality,
    bool Available,
    IReadOnlyList<VendorFinancialSummaryRow> Rows);

/// <summary>The print twin of the list: the same rows, capped at 1,000 (research R9).</summary>
public sealed record VendorFinancialsPrintResponse(
    string DataLabel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DemoNotice,
    ReportSyncTime? LastSuccessfulSync,
    string Currency,
    string CurrencyLabel,
    VendorFinancialPeriodDto Period,
    VendorFinancialPeriodDto? PreviousPeriod,
    IReadOnlyList<VendorFinancialFilterEcho> Filters,
    IReadOnlyList<VendorFinancialUnavailable> Unavailable,
    bool Compare,
    long TotalRows,
    bool Truncated,
    int Limit,
    VendorFinancialSearchDto Search,
    VendorFinancialSortDto Sort,
    DataQualityNotice? DataQuality,
    bool Available,
    IReadOnlyList<VendorFinancialSummaryRow> Rows);

/// <summary>Vendor header of the detail (contracts/api.md §2).</summary>
public sealed record VendorFinancialVendorDto(string Key, string Name, string? Code);

/// <summary>
/// Purchases vs payments (contracts/api.md §2): four separately documented figures.
/// <c>PurchasesMinusPayments</c> is omitted from the JSON when either side is unavailable and is never called profit.
/// </summary>
public sealed record VendorPurchasesVsPaymentsDto(
    string? NetPurchases,
    string? Payments,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PurchasesMinusPayments,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PreviousNetPurchases,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PreviousPayments,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DeltaNetPurchases,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PercentNetPurchases,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DeltaPayments,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PercentPayments);

/// <summary>The vendor financial detail header (contracts/api.md §2); the profile uses it for F5 AC3.</summary>
public sealed record VendorFinancialDetailResponse(
    string DataLabel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DemoNotice,
    ReportSyncTime? LastSuccessfulSync,
    string Currency,
    string CurrencyLabel,
    VendorFinancialPeriodDto Period,
    VendorFinancialPeriodDto? PreviousPeriod,
    VendorFinancialVendorDto Vendor,
    IReadOnlyList<VendorFinancialUnavailable> Unavailable,
    DataQualityNotice? DataQuality,
    bool Available,
    string? VendorBalance,
    string? OutstandingPayments,
    VendorPurchasesVsPaymentsDto PurchasesVsPayments);

/// <summary>One payment-history row (contracts/api.md §3).</summary>
public sealed record PaymentHistoryRow(
    string PaidAtJalali,
    string? Reference,
    string InstrumentType,
    string InstrumentTypeLabel,
    string Amount);

/// <summary>One outstanding-instrument row (contracts/api.md §4).</summary>
public sealed record OutstandingInstrumentRow(
    string DateJalali,
    string? Reference,
    string InstrumentType,
    string InstrumentTypeLabel,
    string UnpaidAmount);

/// <summary>A paged financial document list (history / outstanding): the envelope plus the page.</summary>
public sealed record VendorFinancialListResponse<T>(
    string DataLabel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DemoNotice,
    ReportSyncTime? LastSuccessfulSync,
    string Currency,
    string CurrencyLabel,
    VendorFinancialPeriodDto Period,
    IReadOnlyList<VendorFinancialFilterEcho> Filters,
    IReadOnlyList<VendorFinancialUnavailable> Unavailable,
    int Page,
    int PageSize,
    long TotalRows,
    string Sort,
    string Dir,
    DataQualityNotice? DataQuality,
    bool Available,
    IReadOnlyList<T> Rows);

/// <summary>Standard constants shared by the financial responses.</summary>
public static class VendorFinancialDtos
{
    public const string CurrencyCode = "IRR";
    public const string CurrencyLabel = "ریال";
    public const string DataMessage = "داده در دسترس نیست.";
}
