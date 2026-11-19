namespace Zharfa.Api.VendorFinancials;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

// Source-side rows of the three financial tables (specs/008-vendor-financials/data-model.md §1.5). Pure in-memory
// data: the reconciliation re-reads the guarded source into these rows and recomputes the same formulas the SQL
// functions implement, so a mismatch is a real mismatch (research R11).

/// <summary>A payable document / credit note: remaining is SIGNED (negative = credit in the vendor's favour).</summary>
public sealed record PayableDocumentRow(
    string? VendorSourceKey,
    DateOnly DocDate,
    decimal RemainingAmount,
    bool Cancelled);

/// <summary>A payable cheque: amounts non-negative; open while not fully cleared on or before the as-of date.</summary>
public sealed record PayableChequeRow(
    string? VendorSourceKey,
    DateOnly IssueDate,
    decimal RemainingAmount,
    DateOnly? ClearedAt,
    bool Cancelled);

/// <summary>A completed payment: amount is the PAID PORTION only (never the unpaid remainder).</summary>
public sealed record VendorPaymentRow(
    string? VendorSourceKey,
    DateOnly PaidAt,
    decimal Amount,
    bool Cancelled);

/// <summary>
/// The C# recompute of <c>mabna.fn_vendor_balance</c>, <c>mabna.fn_outstanding_payments</c> and
/// <c>mabna.fn_vendor_payments</c> (research R1, R4; docs/metrics/vendor-financials.md). One definition per figure,
/// implemented twice on purpose — SQL for reporting, this class for reconciliation — and compared exactly.
/// <c>discount_basis</c> is not used by any figure here; callers store '-' in reconciliation_run.
/// </summary>
public static class VendorFinancialTotals
{
    /// <summary>
    /// Signed balance as of <paramref name="asOf"/>: Σ signed document remainders (doc_date ≤ as_of, not
    /// cancelled) + Σ cheque remainders still open as of as_of (issued ≤ as_of, not cancelled, not fully cleared
    /// with cleared_at ≤ as_of). May be negative; never floored at zero; never called profit.
    /// </summary>
    public static decimal VendorBalance(
        IEnumerable<PayableDocumentRow> documents,
        IEnumerable<PayableChequeRow> cheques,
        DateOnly asOf,
        string? vendorSourceKey = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(cheques);

        var total = 0m;
        foreach (var document in documents)
        {
            if (document.Cancelled || document.DocDate > asOf || !Matches(document.VendorSourceKey, vendorSourceKey))
            {
                continue;
            }

            total += document.RemainingAmount; // signed: a credit note subtracts
        }

        foreach (var cheque in cheques)
        {
            if (!IsOpenAt(cheque, asOf) || !Matches(cheque.VendorSourceKey, vendorSourceKey))
            {
                continue;
            }

            total += cheque.RemainingAmount;
        }

        return total;
    }

    /// <summary>
    /// Outstanding payments as of <paramref name="asOf"/>: Σ unpaid remainders of instruments still open at
    /// as_of. Always non-negative; history amounts are never included (FR-033 — no double-count with payments).
    /// </summary>
    public static decimal OutstandingPayments(
        IEnumerable<PayableChequeRow> cheques,
        DateOnly asOf,
        string? vendorSourceKey = null)
    {
        ArgumentNullException.ThrowIfNull(cheques);

        var total = 0m;
        foreach (var cheque in cheques)
        {
            if (!IsOpenAt(cheque, asOf) || !Matches(cheque.VendorSourceKey, vendorSourceKey))
            {
                continue;
            }

            total += Math.Max(0m, cheque.RemainingAmount);
        }

        return total;
    }

    /// <summary>
    /// Payments inside [from, to]: Σ paid portions whose payment/clear date falls in the period, not cancelled.
    /// A partially paid instrument contributes only its paid portion here; its remainder stays outstanding.
    /// </summary>
    public static decimal Payments(
        IEnumerable<VendorPaymentRow> payments,
        DateOnly from,
        DateOnly to,
        string? vendorSourceKey = null)
    {
        ArgumentNullException.ThrowIfNull(payments);

        var total = 0m;
        foreach (var payment in payments)
        {
            if (payment.Cancelled || payment.PaidAt < from || payment.PaidAt > to || !Matches(payment.VendorSourceKey, vendorSourceKey))
            {
                continue;
            }

            total += payment.Amount;
        }

        return total;
    }

    /// <summary>A cheque counts as open at as_of when it was issued by then and not fully cleared on or before it.</summary>
    public static bool IsOpenAt(PayableChequeRow cheque, DateOnly asOf) =>
        !cheque.Cancelled &&
        cheque.IssueDate <= asOf &&
        (cheque.ClearedAt is null || cheque.ClearedAt > asOf);

    /// <summary>Formats an amount exactly as the SQL side and the API store it: 4 decimals, invariant culture.</summary>
    public static string Format(decimal value) =>
        value.ToString("0.0000", CultureInfo.InvariantCulture);

    private static bool Matches(string? rowKey, string? filter) =>
        filter is null || string.Equals(rowKey, filter, StringComparison.Ordinal);
}
