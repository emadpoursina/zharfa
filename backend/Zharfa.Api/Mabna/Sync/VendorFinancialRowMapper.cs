namespace Zharfa.Api.Mabna.Sync;

using System;
using Zharfa.Api.Analytics;

/// <summary>Synced payable document / credit note: <see cref="RemainingAmount"/> is SIGNED (negative = credit).</summary>
public sealed record PayableDocumentRecord(
    string SourceKey,
    string? VendorSourceKey,
    DateOnly DocDate,
    int JalaliYear,
    int JalaliMonth,
    int JalaliDay,
    string? DocNumber,
    decimal RemainingAmount,
    bool Cancelled);

/// <summary>Synced payable cheque: amounts non-negative; <see cref="ClearedAt"/> is the payment/clear date when fully cleared.</summary>
public sealed record PayableChequeRecord(
    string SourceKey,
    string? VendorSourceKey,
    DateOnly IssueDate,
    int JalaliYear,
    int JalaliMonth,
    int JalaliDay,
    string? Reference,
    string InstrumentType,
    string State,
    decimal Amount,
    decimal RemainingAmount,
    DateOnly? ClearedAt,
    bool Cancelled);

/// <summary>Synced completed payment: <see cref="Amount"/> is the paid portion only (never the unpaid remainder).</summary>
public sealed record VendorPaymentRecord(
    string SourceKey,
    string? VendorSourceKey,
    DateOnly PaidAt,
    int JalaliYear,
    int JalaliMonth,
    int JalaliDay,
    string? Reference,
    string InstrumentType,
    decimal Amount,
    string? InstrumentSourceKey,
    bool Cancelled);

/// <summary>Synced allocation of one instrument across a vendor.</summary>
public sealed record PaymentAllocationRecord(
    string SourceKey,
    string InstrumentSourceKey,
    string VendorSourceKey,
    decimal Amount);

/// <summary>
/// Maps Phase 6 financial source rows to synced records (tasks.md T016). Pure: no I/O. Reuses the purchase
/// mapper's date parser, cancelled rule and amount-sign handling; rejects invalid rows with a reason code that
/// carries no source values. Cheque/payment amounts must be non-negative; a document remaining stays signed;
/// a payment amount is the paid portion only (FR-033 — the unpaid remainder lives on the instrument).
/// </summary>
public sealed class VendorFinancialRowMapper(VendorFinancialMapping mapping)
{
    public const string KeyMissing = PurchaseRowMapper.KeyMissing;
    public const string DateInvalid = PurchaseRowMapper.DateInvalid;
    public const string AmountInvalid = PurchaseRowMapper.AmountInvalid;
    public const string AmountNegative = PurchaseRowMapper.AmountNegative;
    public const string AmountTooLarge = "amount-too-large";

    /// <summary>Storage scale of every amount (numeric(24,4)); anything beyond it cannot be stored exactly.</summary>
    private const decimal MaxAmount = 99999999999999999999.9999m;

    private readonly VendorFinancialMapping _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));

    // ----- payable documents -------------------------------------------------------------------------------

    public MappedRow<PayableDocumentRecord> MapPayableDocument(SourceRow row)
    {
        var source = _mapping.PayableDocument ??
                     throw new InvalidOperationException("payableDocument is not usable in this data mode.");

        var key = PurchaseRowMapper.KeyText(row.Get(source.Key));
        if (key is null)
        {
            return Reject<PayableDocumentRecord>(KeyMissing);
        }

        if (!TryDate(row, source.Date, source.DateFormat, out var date))
        {
            return Reject<PayableDocumentRecord>(DateInvalid);
        }

        // Signed: the map's amountSign decides; a credit note keeps its negative remainder.
        if (!PurchaseRowMapper.TryDecimal(row.Get(source.Remaining), out var remaining))
        {
            return Reject<PayableDocumentRecord>(AmountInvalid);
        }

        remaining = PurchaseRowMapper.ApplySign(remaining, source.RemainingSign);
        if (Math.Abs(remaining) > MaxAmount)
        {
            return Reject<PayableDocumentRecord>(AmountTooLarge);
        }

        var record = new PayableDocumentRecord(
            key,
            source.VendorKey is null ? null : PurchaseRowMapper.KeyText(row.Get(source.VendorKey)),
            date.Gregorian,
            date.JalaliYear,
            date.JalaliMonth,
            date.JalaliDay,
            source.Number is null ? null : PurchaseRowMapper.TextOrNull(row.Get(source.Number)),
            remaining,
            IsCancelled(row, source.CancelledColumn, source.CancelledValue));
        return new MappedRow<PayableDocumentRecord>(record);
    }

    // ----- payable cheques ---------------------------------------------------------------------------------

    public MappedRow<PayableChequeRecord> MapPayableCheque(SourceRow row)
    {
        var source = _mapping.PayableCheque ??
                     throw new InvalidOperationException("payableCheque is not usable in this data mode.");

        var key = PurchaseRowMapper.KeyText(row.Get(source.Key));
        if (key is null)
        {
            return Reject<PayableChequeRecord>(KeyMissing);
        }

        if (!TryDate(row, source.IssueDate, source.IssueDateFormat, out var issue))
        {
            return Reject<PayableChequeRecord>(DateInvalid);
        }

        if (!TryAmount(row, source.Amount, source.AmountSign, allowNegative: false, out var amount))
        {
            return Reject<PayableChequeRecord>(AmountInvalid);
        }

        if (!TryAmount(row, source.Remaining, source.RemainingSign, allowNegative: false, out var remaining))
        {
            return Reject<PayableChequeRecord>(AmountInvalid);
        }

        DateOnly? clearedAt = null;
        if (source.ClearedAt is not null && row.Get(source.ClearedAt) is { } rawCleared)
        {
            var clearedText = PurchaseRowMapper.TextOrNull(rawCleared);
            if (clearedText is not null)
            {
                if (!SourceDateParser.TryParse(clearedText, source.ClearedAtFormat ?? SourceDateParser.JalaliTextFormat, out var cleared))
                {
                    return Reject<PayableChequeRecord>(DateInvalid);
                }

                clearedAt = cleared.Gregorian;
            }
        }

        var record = new PayableChequeRecord(
            key,
            source.VendorKey is null ? null : PurchaseRowMapper.KeyText(row.Get(source.VendorKey)),
            issue.Gregorian,
            issue.JalaliYear,
            issue.JalaliMonth,
            issue.JalaliDay,
            source.Reference is null ? null : PurchaseRowMapper.TextOrNull(row.Get(source.Reference)),
            TextOrUnknown(row, source.InstrumentType),
            TextOrUnknown(row, source.State),
            amount,
            remaining,
            clearedAt,
            IsCancelled(row, source.CancelledColumn, source.CancelledValue));
        return new MappedRow<PayableChequeRecord>(record);
    }

    // ----- vendor payments ---------------------------------------------------------------------------------

    public MappedRow<VendorPaymentRecord> MapVendorPayment(SourceRow row)
    {
        var source = _mapping.VendorPayment ??
                     throw new InvalidOperationException("vendorPayment is not usable in this data mode.");

        var key = PurchaseRowMapper.KeyText(row.Get(source.Key));
        if (key is null)
        {
            return Reject<VendorPaymentRecord>(KeyMissing);
        }

        if (!TryDate(row, source.PaidAt, source.PaidAtFormat, out var paid))
        {
            return Reject<VendorPaymentRecord>(DateInvalid);
        }

        // The paid portion only; a negative or unparseable amount is rejected, never floored.
        if (!TryAmount(row, source.Amount, source.AmountSign, allowNegative: false, out var amount))
        {
            return Reject<VendorPaymentRecord>(AmountInvalid);
        }

        var record = new VendorPaymentRecord(
            key,
            source.VendorKey is null ? null : PurchaseRowMapper.KeyText(row.Get(source.VendorKey)),
            paid.Gregorian,
            paid.JalaliYear,
            paid.JalaliMonth,
            paid.JalaliDay,
            source.Reference is null ? null : PurchaseRowMapper.TextOrNull(row.Get(source.Reference)),
            TextOrUnknown(row, source.InstrumentType),
            amount,
            source.InstrumentKey is null ? null : PurchaseRowMapper.KeyText(row.Get(source.InstrumentKey)),
            IsCancelled(row, source.CancelledColumn, source.CancelledValue));
        return new MappedRow<VendorPaymentRecord>(record);
    }

    // ----- allocations -------------------------------------------------------------------------------------

    public MappedRow<PaymentAllocationRecord> MapPaymentAllocation(SourceRow row)
    {
        var source = _mapping.PaymentAllocation ??
                     throw new InvalidOperationException("paymentAllocation is not usable in this data mode.");

        var key = PurchaseRowMapper.KeyText(row.Get(source.Key));
        if (key is null)
        {
            return Reject<PaymentAllocationRecord>(KeyMissing);
        }

        var instrumentKey = PurchaseRowMapper.KeyText(row.Get(source.InstrumentKey));
        var vendorKey = PurchaseRowMapper.KeyText(row.Get(source.VendorKey));
        if (instrumentKey is null || vendorKey is null)
        {
            return Reject<PaymentAllocationRecord>(KeyMissing);
        }

        if (!PurchaseRowMapper.TryDecimal(row.Get(source.Amount), out var amount))
        {
            return Reject<PaymentAllocationRecord>(AmountInvalid);
        }

        amount = PurchaseRowMapper.ApplySign(amount, source.AmountSign);
        var record = new PaymentAllocationRecord(key, instrumentKey, vendorKey, amount);
        return new MappedRow<PaymentAllocationRecord>(record);
    }

    // ----- shared ------------------------------------------------------------------------------------------

    private static bool TryDate(SourceRow row, string column, string format, out SourceDate date)
    {
        date = default;
        var text = PurchaseRowMapper.TextOrNull(row.Get(column));
        return text is not null && SourceDateParser.TryParse(text, format, out date);
    }

    private static bool TryAmount(SourceRow row, string column, string sign, bool allowNegative, out decimal value)
    {
        value = 0;
        if (!PurchaseRowMapper.TryDecimal(row.Get(column), out var parsed))
        {
            return false;
        }

        value = PurchaseRowMapper.ApplySign(parsed, sign);
        if (!allowNegative && value < 0)
        {
            return false;
        }

        return Math.Abs(value) <= MaxAmount;
    }

    /// <summary>Mapped enum-like text (type/state): trimmed source value, or "unknown" when the column is absent or null.</summary>
    private static string TextOrUnknown(SourceRow row, string? column)
    {
        if (column is null)
        {
            return "unknown";
        }

        var text = PurchaseRowMapper.TextOrNull(row.Get(column));
        return text ?? "unknown";
    }

    /// <summary>Cancelled rule: the header value equals the verified cancelled value (same rule as purchases).</summary>
    private static bool IsCancelled(SourceRow row, string? column, object? value) =>
        column is not null && PurchaseRowMapper.ValueEquals(row.Get(column), value);

    private static MappedRow<T> Reject<T>(string reason) where T : class => new(null, reason);
}
