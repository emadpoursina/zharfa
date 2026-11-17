namespace Zharfa.Api.Purchasing;

using System.Collections.Generic;
using System.Linq;
using Zharfa.Api.Mabna.Sync;

/// <summary>
/// Metric availability rules from data-model.md ("Metric availability"
/// table): which source items each figure needs, and why it is unavailable.
/// Reason codes: no-successful-sync | source-item-unverified |
/// source-item-unmapped | no-previous-data (contracts/api.md §3).
/// </summary>
public static class MetricAvailability
{
    public const string NoSuccessfulSync = "no-successful-sync";
    public const string SourceItemUnverified = "source-item-unverified";
    public const string SourceItemUnmapped = "source-item-unmapped";
    public const string NoPreviousData = "no-previous-data";

    public const string FigureNet = "net";
    public const string FigureGross = "gross";
    public const string FigureReturns = "returns";
    public const string FigureVat = "vat";
    public const string FigureActiveVendors = "activeVendors";
    public const string FigureTopVendors = "topVendors";
    public const string FigureTopProducts = "topProducts";
    public const string FigureTrend = "trend";
    public const string FigureComparison = "comparison";

    private static readonly string[] AllFigures =
    {
        FigureNet, FigureGross, FigureReturns, FigureVat,
        FigureActiveVendors, FigureTopVendors, FigureTopProducts,
        FigureTrend, FigureComparison
    };

    /// <summary>
    /// Evaluates every figure. Value <c>null</c> = available; otherwise the
    /// reason code. <paramref name="hasSucceededSync"/> is false when no
    /// <c>succeeded</c> sync job exists (then everything is unavailable).
    /// <paramref name="discountBasis"/> is the effective basis (<c>line</c> or
    /// <c>lineAndDocument</c>); under <c>lineAndDocument</c> every line-amount figure
    /// also needs the document discount (FR-014, data-model.md §4).
    /// The "no previous month" case of the comparison depends on the selected
    /// month and is decided by the Overview service (<see cref="NoPreviousData"/>).
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Evaluate(
        SourceMap? map,
        bool isProduction,
        bool hasSucceededSync,
        string discountBasis = PurchasingDiscountBasis.Line)
    {
        var result = new Dictionary<string, string?>();
        if (!hasSucceededSync)
        {
            foreach (var figure in AllFigures)
            {
                result[figure] = NoSuccessfulSync;
            }

            return result;
        }

        var lineAndDocument = string.Equals(
            discountBasis?.Trim(), PurchasingDiscountBasis.LineAndDocument, System.StringComparison.OrdinalIgnoreCase);

        // FR-001: every figure excludes cancelled documents, so both headers need a usable cancelled rule.
        var invoiceCancelled = CancelledReason(map?.PurchaseInvoice, isProduction);
        var returnCancelled = CancelledReason(map?.PurchaseReturn, isProduction);

        // Invoice side: header entity, key, date, doc-type filters, cancelled rule, and line amount items.
        var invoiceReason = First(
            EntityReason(map?.PurchaseInvoice, isProduction),
            FieldReason(map?.PurchaseInvoice?.Key, isProduction),
            FieldReason(map?.PurchaseInvoice?.Date, isProduction),
            EntityReason(map?.PurchaseLine, isProduction),
            FieldReason(map?.PurchaseLine?.DocumentKey, isProduction),
            FieldReason(map?.PurchaseLine?.GrossAmount, isProduction),
            FieldReason(map?.PurchaseLine?.LineDiscount, isProduction),
            DocDiscountReason(map?.PurchaseInvoice, lineAndDocument, isProduction),
            FilterReason(map?.PurchaseInvoice, isProduction),
            invoiceCancelled);

        // Return side: same shape on the return header.
        var returnReason = First(
            EntityReason(map?.PurchaseReturn, isProduction),
            FieldReason(map?.PurchaseReturn?.Key, isProduction),
            FieldReason(map?.PurchaseReturn?.Date, isProduction),
            EntityReason(map?.PurchaseLine, isProduction),
            FieldReason(map?.PurchaseLine?.DocumentKey, isProduction),
            FieldReason(map?.PurchaseLine?.GrossAmount, isProduction),
            FieldReason(map?.PurchaseLine?.LineDiscount, isProduction),
            DocDiscountReason(map?.PurchaseReturn, lineAndDocument, isProduction),
            FilterReason(map?.PurchaseReturn, isProduction),
            returnCancelled);

        // VAT is summed over invoices and returns, so both cancelled rules apply.
        var vatReason = First(
            FieldReason(map?.PurchaseLine?.Vat, isProduction),
            invoiceCancelled,
            returnCancelled);

        // Active vendors count vendors with a non-cancelled document in the period.
        var vendorKeyReason = First(
            FieldReason(map?.PurchaseInvoice?.VendorKey, isProduction),
            FieldReason(map?.PurchaseReturn?.VendorKey, isProduction),
            invoiceCancelled,
            returnCancelled);

        var productKeyReason = FieldReason(map?.PurchaseLine?.ProductKey, isProduction);

        var netReason = invoiceReason ?? returnReason;

        result[FigureGross] = invoiceReason;
        result[FigureReturns] = returnReason;
        result[FigureNet] = netReason;
        result[FigureVat] = vatReason;
        result[FigureActiveVendors] = vendorKeyReason;
        result[FigureTopVendors] = netReason ?? vendorKeyReason;
        result[FigureTopProducts] = netReason ?? productKeyReason;
        result[FigureTrend] = netReason;
        result[FigureComparison] = netReason;

        return result;
    }

    /// <summary>First non-null reason in order, or null when every item is usable.</summary>
    private static string? First(params string?[] reasons) =>
        reasons.FirstOrDefault(reason => reason is not null);

    private static string? EntityReason(SourceMapEntity? entity, bool isProduction) =>
        Reason(entity?.Object, entity?.Status, isProduction);

    private static string? FieldReason(SourceMapField? field, bool isProduction) =>
        Reason(field?.Column, field?.Status, isProduction);

    /// <summary>
    /// The cancelled rule (<c>rule</c> = column with a column and value, or zeroHeader) must be
    /// mapped and usable on the header; otherwise cancelled documents cannot be excluded.
    /// </summary>
    private static string? CancelledReason(SourceMapEntity? entity, bool isProduction)
    {
        var cancelled = entity?.Cancelled;
        if (cancelled is null)
        {
            return SourceItemUnmapped;
        }

        var rule = cancelled.Rule?.Trim();
        if (string.Equals(rule, "column", System.StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(cancelled.Column) || cancelled.Value is null)
            {
                return SourceItemUnmapped;
            }
        }
        else if (!string.Equals(rule, "zeroHeader", System.StringComparison.OrdinalIgnoreCase))
        {
            return SourceItemUnmapped;
        }

        return SourceMap.IsStatusUsable(cancelled.Status, isProduction) ? null : SourceItemUnverified;
    }

    /// <summary>
    /// Under basis <c>lineAndDocument</c> the line amount subtracts the header's document
    /// discount, so <c>docDiscount</c> must be mapped and usable. Other bases do not need it.
    /// </summary>
    private static string? DocDiscountReason(SourceMapEntity? entity, bool lineAndDocument, bool isProduction) =>
        lineAndDocument ? FieldReason(entity?.DocDiscount, isProduction) : null;

    private static string? FilterReason(SourceMapEntity? entity, bool isProduction) =>
        entity?.Filter
            .Select(filter => Reason(filter.Column, filter.Status, isProduction))
            .FirstOrDefault(reason => reason is not null);

    /// <summary>
    /// A dependency is unmapped when its object/column is missing, unverified
    /// when its status is not usable in this data mode, and usable otherwise.
    /// </summary>
    private static string? Reason(string? identifier, string? status, bool isProduction)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return SourceItemUnmapped;
        }

        return SourceMap.IsStatusUsable(status, isProduction) ? null : SourceItemUnverified;
    }
}
