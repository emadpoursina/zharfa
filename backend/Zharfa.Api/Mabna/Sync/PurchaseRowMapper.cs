namespace Zharfa.Api.Mabna.Sync;

using System;
using System.Globalization;
using Zharfa.Api.Analytics;

public sealed record MasterRecord(string SourceKey, string NameFa, bool? IsActiveSource);

public sealed record DocumentRecord(
    string SourceKey,
    string DocType,
    string? DocNumber,
    string? VendorSourceKey,
    DateOnly DocDate,
    int JalaliYear,
    int JalaliMonth,
    int JalaliDay,
    bool Cancelled,
    decimal DocDiscount);

public sealed record LineRecord(
    string DocumentSourceKey,
    string SourceKey,
    string? ProductSourceKey,
    decimal? Quantity,
    decimal GrossAmount,
    decimal LineDiscount,
    decimal? VatAmount);

/// <summary>Result of mapping one row: a record, or a reject reason code, plus an optional warning code.</summary>
public sealed record MappedRow<T>(T? Record, string? RejectReason = null, string? Warning = null) where T : class;

/// <summary>
/// Maps source rows to synced records (data-model.md §1 ingestion rules). Pure: no I/O.
/// Amounts stay <see cref="decimal"/>; text goes through <see cref="PersianText"/>; dates go
/// through <see cref="SourceDateParser"/>. Invalid rows are rejected with a reason code, never
/// thrown, and the reason carries no source values.
/// </summary>
public sealed class PurchaseRowMapper(PurchaseMapping mapping)
{
    public const string KeyMissing = "key-missing";
    public const string DateInvalid = "date-invalid";
    public const string AmountInvalid = "amount-invalid";
    public const string AmountNegative = "amount-negative";
    public const string DiscountNegative = "discount-negative";
    public const string QuantityInvalid = "quantity-invalid";
    public const string VatInvalid = "vat-invalid";
    public const string DocumentKeyMissing = "document-key-missing";
    public const string DiscountExceedsGross = "discount-exceeds-gross";

    private readonly PurchaseMapping _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));

    public MappedRow<MasterRecord> MapVendor(SourceRow row) => MapMaster(row, _mapping.Vendor);

    public MappedRow<MasterRecord> MapProduct(SourceRow row) => MapMaster(row, _mapping.Product);

    public MappedRow<DocumentRecord> MapInvoice(SourceRow row) => MapDocument(row, LogicalDocType.Invoice, _mapping.Invoice);

    public MappedRow<DocumentRecord> MapReturn(SourceRow row) => MapDocument(row, LogicalDocType.Return, _mapping.Return);

    public MappedRow<LineRecord> MapLine(SourceRow row)
    {
        var line = _mapping.Line;
        var key = KeyText(row.Get(line.Key));
        if (key is null)
        {
            return Reject<LineRecord>(KeyMissing);
        }

        var documentKey = KeyText(row.Get(line.DocumentKey));
        if (documentKey is null)
        {
            return Reject<LineRecord>(DocumentKeyMissing);
        }

        if (!TryDecimal(row.Get(line.Gross), out var gross))
        {
            return Reject<LineRecord>(AmountInvalid);
        }

        gross = ApplySign(gross, line.GrossSign);
        if (gross < 0)
        {
            return Reject<LineRecord>(AmountNegative);
        }

        decimal lineDiscount = 0;
        if (line.LineDiscount is not null && row.Get(line.LineDiscount) is { } rawDiscount)
        {
            if (!TryDecimal(rawDiscount, out lineDiscount))
            {
                return Reject<LineRecord>(AmountInvalid);
            }

            if (lineDiscount < 0)
            {
                return Reject<LineRecord>(DiscountNegative);
            }
        }

        decimal? quantity = null;
        if (line.Quantity is not null && row.Get(line.Quantity) is { } rawQuantity)
        {
            if (!TryDecimal(rawQuantity, out var parsedQuantity))
            {
                return Reject<LineRecord>(QuantityInvalid);
            }

            quantity = parsedQuantity;
        }

        decimal? vat = null;
        if (line.Vat is not null && row.Get(line.Vat) is { } rawVat)
        {
            if (!TryDecimal(rawVat, out var parsedVat))
            {
                return Reject<LineRecord>(VatInvalid);
            }

            vat = parsedVat;
        }

        string? productKey = line.ProductKey is null ? null : KeyText(row.Get(line.ProductKey));
        var record = new LineRecord(documentKey, key, productKey, quantity, gross, lineDiscount, vat);
        return new MappedRow<LineRecord>(record, null, gross < lineDiscount ? DiscountExceedsGross : null);
    }

    private MappedRow<MasterRecord> MapMaster(SourceRow row, MasterSource source)
    {
        var key = KeyText(row.Get(source.Key));
        if (key is null)
        {
            return Reject<MasterRecord>(KeyMissing);
        }

        var name = PersianText.NormalizeName(source.Name is null ? null : TextOf(row.Get(source.Name)));
        bool? active = null;
        if (source.Active is not null && TryBool(row.Get(source.Active), out var flag))
        {
            active = flag;
        }

        return new MappedRow<MasterRecord>(new MasterRecord(key, name, active));
    }

    private MappedRow<DocumentRecord> MapDocument(SourceRow row, string docType, DocumentSource source)
    {
        var key = KeyText(row.Get(source.Key));
        if (key is null)
        {
            return Reject<DocumentRecord>(KeyMissing);
        }

        if (!SourceDateParser.TryParse(DateText(row.Get(source.Date)), source.DateFormat, out var date))
        {
            return Reject<DocumentRecord>(DateInvalid);
        }

        decimal discount = 0;
        if (source.DocDiscount is not null && row.Get(source.DocDiscount) is { } rawDiscount)
        {
            if (!TryDecimal(rawDiscount, out discount))
            {
                return Reject<DocumentRecord>(AmountInvalid);
            }

            if (discount < 0)
            {
                return Reject<DocumentRecord>(DiscountNegative);
            }
        }

        var cancelled = source.CancelledColumn is not null &&
                        ValueEquals(row.Get(source.CancelledColumn), source.CancelledValue);

        var record = new DocumentRecord(
            key,
            docType,
            source.Number is null ? null : TextOrNull(row.Get(source.Number)),
            source.VendorKey is null ? null : KeyText(row.Get(source.VendorKey)),
            date.Gregorian,
            date.JalaliYear,
            date.JalaliMonth,
            date.JalaliDay,
            cancelled,
            discount);
        return new MappedRow<DocumentRecord>(record);
    }

    private static MappedRow<T> Reject<T>(string reason) where T : class => new(null, reason);

    /// <summary>Applies the map's amountSign: asIs keeps the value, negate flips it, abs makes it positive.</summary>
    public static decimal ApplySign(decimal value, string sign) => sign switch
    {
        "negate" => -value,
        "abs" => Math.Abs(value),
        _ => value,
    };

    /// <summary>Source key as text: trimmed; integral numbers without a fraction; byte arrays as hex. Null when empty.</summary>
    public static string? KeyText(object? value) => TextOrNull(value);

    /// <summary>Text value: trimmed string, or the invariant text of a number or byte array. Null when empty.</summary>
    public static string? TextOrNull(object? value)
    {
        var text = TextOf(value).Trim();
        return text.Length == 0 ? null : text;
    }

    private static string TextOf(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        byte[] bytes => "0x" + Convert.ToHexString(bytes),
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        bool b => b ? "1" : "0",
        decimal d => d == decimal.Truncate(d)
            ? decimal.Truncate(d).ToString("0", CultureInfo.InvariantCulture)
            : d.ToString(CultureInfo.InvariantCulture),
        double or float => Convert.ToDecimal(value, CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    /// <summary>Date text for <see cref="SourceDateParser"/>: DateTime as yyyy-MM-dd, other values as their text.</summary>
    private static string? DateText(object? value) => value is null ? null : TextOf(value).Trim();

    /// <summary>Exact decimal from a numeric value or a numeric string (Persian digits and separators allowed).</summary>
    public static bool TryDecimal(object? value, out decimal result)
    {
        result = 0;
        switch (value)
        {
            case null:
                return false;
            case decimal d:
                result = d;
                return true;
            case long or int or short or byte or sbyte or ushort or uint or ulong:
                result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            case double or float:
                return decimal.TryParse(
                    Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out result);
            case string s:
                var normalized = SourceDateParser.ToLatinDigits(s)
                    .Replace(",", string.Empty, StringComparison.Ordinal)
                    .Replace("٬", string.Empty, StringComparison.Ordinal)
                    .Replace('٫', '.')
                    .Trim();
                return normalized.Length > 0 && decimal.TryParse(
                    normalized,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out result);
            default:
                return false;
        }
    }

    private static bool TryBool(object? value, out bool result)
    {
        result = false;
        switch (value)
        {
            case bool b:
                result = b;
                return true;
            case string s when s.Trim() is "1" or "true" or "True" or "TRUE":
                result = true;
                return true;
            case string s when s.Trim() is "0" or "false" or "False" or "FALSE":
                return true;
            default:
                if (TryDecimal(value, out var number) && number is 0 or 1)
                {
                    result = number == 1;
                    return true;
                }

                return false;
        }
    }

    /// <summary>Cancelled rule: the header value equals the verified cancelled value (numeric or text).</summary>
    public static bool ValueEquals(object? actual, object? expected)
    {
        if (actual is null || expected is null)
        {
            return false;
        }

        if (TryDecimal(actual, out var a) && TryDecimal(expected, out var e))
        {
            return a == e;
        }

        return string.Equals(TextOf(actual).Trim(), TextOf(expected).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static class LogicalDocType
    {
        public const string Invoice = "invoice";
        public const string Return = "return";
    }
}
