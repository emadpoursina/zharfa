namespace Zharfa.Api.VendorFinancials;

using System.Collections.Generic;
using System.Linq;
using Zharfa.Api.Mabna.Sync;

/// <summary>
/// Metric availability for the vendor financials (specs/008-vendor-financials/data-model.md §3, research R1).
/// Mirrors <c>Zharfa.Api.Purchasing.MetricAvailability</c>: a value of <c>null</c> means the figure is available;
/// otherwise it is the reason code. Reason codes: no-successful-sync | source-item-unverified |
/// source-item-unmapped | no-previous-data | formula-unconfirmed.
///
/// Production treats <c>candidate</c> and <c>unknown</c> as unusable; demo/simulator installs also accept
/// <c>candidate</c> (see <see cref="SourceMap.IsStatusUsable"/>). Zero qualifying rows with a usable formula is
/// "0.0000", never an unavailable figure — that decision belongs to the service.
/// </summary>
public static class VendorFinancialAvailability
{
    public const string NoSuccessfulSync = "no-successful-sync";
    public const string SourceItemUnverified = "source-item-unverified";
    public const string SourceItemUnmapped = "source-item-unmapped";
    public const string NoPreviousData = "no-previous-data";
    public const string FormulaUnconfirmed = "formula-unconfirmed";

    public const string FigureVendorBalance = "vendorBalance";
    public const string FigureOutstandingPayments = "outstandingPayments";
    public const string FigureOutstandingInstruments = "outstandingInstruments";
    public const string FigurePayments = "payments";
    public const string FigurePaymentHistory = "paymentHistory";
    public const string FigureNetPurchases = "netPurchases";
    public const string FigurePurchasesMinusPayments = "purchasesMinusPayments";

    /// <summary>The candidate mix ids the code can evaluate (contracts/source-map.md).</summary>
    private static readonly string[] KnownBalanceMixes = { "openPayablesPlusUnpaidCheques" };

    private static readonly string[] AllFigures =
    [
        FigureVendorBalance,
        FigureOutstandingPayments,
        FigureOutstandingInstruments,
        FigurePayments,
        FigurePaymentHistory,
        FigureNetPurchases,
        FigurePurchasesMinusPayments,
    ];

    /// <summary>
    /// Evaluates every figure for one request. <paramref name="hasSucceededSync"/> is false when no
    /// <c>succeeded</c> sync job exists (then every figure, including net purchases, is no-successful-sync).
    /// The previous-period base is decided by the service (no-previous-data per comparison column).
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Evaluate(
        SourceMap? map,
        bool isProduction,
        bool hasSucceededSync)
    {
        var result = new Dictionary<string, string?>(System.StringComparer.Ordinal);
        if (!hasSucceededSync)
        {
            foreach (var figure in AllFigures)
            {
                result[figure] = NoSuccessfulSync;
            }

            return result;
        }

        var balanceReason = BalanceReason(map, isProduction);
        var outstandingReason = OutstandingReason(map, isProduction);
        var paymentsReason = PaymentsReason(map, isProduction);

        // Net purchases are the Phase 1 figure, unchanged: available as soon as a sync succeeded.
        const string? netReason = null;

        result[FigureVendorBalance] = balanceReason;
        result[FigureOutstandingPayments] = outstandingReason;
        result[FigureOutstandingInstruments] = outstandingReason;
        result[FigurePayments] = paymentsReason;
        result[FigurePaymentHistory] = paymentsReason;
        result[FigureNetPurchases] = netReason;
        result[FigurePurchasesMinusPayments] = First(netReason, paymentsReason);

        return result;
    }

    /// <summary>First non-null reason in order, or null when every item is usable.</summary>
    private static string? First(params string?[] reasons) =>
        reasons.FirstOrDefault(reason => reason is not null);

    /// <summary>
    /// vendorBalance needs a usable balanceFormula (production: confirmed/corrected only) plus every entity the
    /// mix names. A declared-but-unusable formula is <c>formula-unconfirmed</c>; a missing entity is
    /// <c>source-item-unmapped</c>; an unusable entity or field is <c>source-item-unverified</c>.
    /// </summary>
    private static string? BalanceReason(SourceMap? map, bool isProduction)
    {
        var formula = map?.BalanceFormula;
        if (formula is null ||
            !SourceMap.IsStatusUsable(formula.Status, isProduction) ||
            string.IsNullOrWhiteSpace(formula.Mix) ||
            !KnownBalanceMixes.Contains(formula.Mix.Trim()))
        {
            return FormulaUnconfirmed;
        }

        // openPayablesPlusUnpaidCheques: payable documents (signed remaining) plus open payable cheques.
        var documentReason = EntityReason(map!.PayableDocument, isProduction,
            entity => FieldReason(entity.Key, isProduction),
            entity => FieldReason(entity.Date, isProduction),
            entity => FieldReason(entity.RemainingAmount, isProduction),
            entity => FieldReason(entity.VendorKey, isProduction));

        var chequeReason = EntityReason(map.PayableCheque, isProduction,
            entity => FieldReason(entity.Key, isProduction),
            entity => FieldReason(entity.Date, isProduction),
            entity => FieldReason(entity.RemainingAmount, isProduction),
            entity => FieldReason(entity.VendorKey, isProduction),
            entity => FieldReason(entity.InstrumentType, isProduction),
            entity => FieldReason(entity.State, isProduction),
            entity => CancelledReason(entity, isProduction));

        return First(documentReason, chequeReason);
    }

    /// <summary>outstandingPayments / outstandingInstruments: a usable cheque source (other mapped outstanding types join it).</summary>
    private static string? OutstandingReason(SourceMap? map, bool isProduction)
    {
        var chequeReason = EntityReason(map?.PayableCheque, isProduction,
            entity => FieldReason(entity.Key, isProduction),
            entity => FieldReason(entity.Date, isProduction),
            entity => FieldReason(entity.RemainingAmount, isProduction),
            entity => FieldReason(entity.VendorKey, isProduction),
            entity => FieldReason(entity.InstrumentType, isProduction),
            entity => FieldReason(entity.State, isProduction),
            entity => CancelledReason(entity, isProduction));

        return chequeReason;
    }

    /// <summary>payments / paymentHistory: a usable vendorPayment source covering every instrument type the formula includes.</summary>
    private static string? PaymentsReason(SourceMap? map, bool isProduction)
    {
        return EntityReason(map?.VendorPayment, isProduction,
            entity => FieldReason(entity.Key, isProduction),
            entity => FieldReason(entity.Date, isProduction),
            entity => FieldReason(entity.Amount, isProduction),
            entity => FieldReason(entity.VendorKey, isProduction),
            entity => FieldReason(entity.InstrumentType, isProduction),
            entity => CancelledReason(entity, isProduction));
    }

    /// <summary>
    /// A missing optional entity is unmapped; an entity whose object/status is unusable in this data mode is
    /// unverified; then every field check runs in order.
    /// </summary>
    private static string? EntityReason(SourceMapEntity? entity, bool isProduction,
        params System.Func<SourceMapEntity, string?>[] fieldChecks)
    {
        if (entity is null || string.IsNullOrWhiteSpace(entity.Object))
        {
            return SourceItemUnmapped;
        }

        if (!SourceMap.IsStatusUsable(entity.Status, isProduction))
        {
            return SourceItemUnverified;
        }

        foreach (var check in fieldChecks)
        {
            var reason = check(entity);
            if (reason is not null)
            {
                return reason;
            }
        }

        return null;
    }

    private static string? FieldReason(SourceMapField? field, bool isProduction) =>
        field is null || string.IsNullOrWhiteSpace(field.Column)
            ? SourceItemUnmapped
            : SourceMap.IsStatusUsable(field.Status, isProduction) ? null : SourceItemUnverified;

    /// <summary>
    /// The cancelled rule is optional for the financial entities: absent means the source has no cancellation
    /// marker (rows are stored as not cancelled); a rule that is present must be mapped and usable, otherwise
    /// cancelled rows cannot be excluded.
    /// </summary>
    private static string? CancelledReason(SourceMapEntity entity, bool isProduction)
    {
        var cancelled = entity.Cancelled;
        if (cancelled is null)
        {
            return null;
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
}
