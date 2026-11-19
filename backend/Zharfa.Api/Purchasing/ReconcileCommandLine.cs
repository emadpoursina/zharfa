namespace Zharfa.Api.Purchasing;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Zharfa.Api.Mabna.Sync;

/// <summary>A parsed <c>reconcile purchasing</c> command.</summary>
/// <param name="Year">Jalali year.</param>
/// <param name="Month">Jalali month, 1–12.</param>
/// <param name="FilterKind">none | vendor | product.</param>
/// <param name="FilterValue">Source key of the vendor or product, or null.</param>
/// <param name="Basis">line | lineAndDocument, or null to use the configured basis.</param>
/// <param name="ShowAmounts">Print the amounts in production mode too.</param>
public sealed record ReconcileCommand(
    int Year,
    int Month,
    string FilterKind,
    string? FilterValue,
    string? Basis,
    bool ShowAmounts)
{
    /// <summary>The period as <c>YYYY-MM</c>.</summary>
    public string MonthText => $"{Year.ToString("0000", CultureInfo.InvariantCulture)}-{Month.ToString("00", CultureInfo.InvariantCulture)}";
}

/// <summary>Result of parsing: a command, or a usage error (exit 2).</summary>
public sealed record ReconcileParseResult(ReconcileCommand? Command, string? Error);

/// <summary>
/// Parses <c>reconcile purchasing --month YYYY-MM [--vendor key | --product key] [--basis line|lineAndDocument] [--show-amounts]</c>
/// (contracts/cli-and-config.md §1). Pure. The month accepts Latin or Persian digits.
/// </summary>
public static class ReconcileCommandLine
{
    public const string Usage =
        "استفاده: reconcile purchasing --month YYYY-MM [--vendor <key> | --product <key>] [--basis line|lineAndDocument] [--show-amounts]. " +
        "Usage: reconcile purchasing --month YYYY-MM [--vendor <key> | --product <key>] [--basis line|lineAndDocument] [--show-amounts].";

    public const string VendorBalanceUsage =
        "استفاده: reconcile vendor-balance --month YYYY-MM [--vendor <key>] [--show-amounts]. " +
        "Usage: reconcile vendor-balance --month YYYY-MM [--vendor <key>] [--show-amounts].";

    private static readonly Regex MonthPattern = new(@"^(\d{4})-(\d{1,2})$", RegexOptions.CultureInvariant);

    /// <summary>True when the process was started for the reconciliation command.</summary>
    public static bool IsReconcile(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[0], "reconcile", StringComparison.Ordinal);

    /// <summary>True for <c>reconcile vendor-balance</c> (the purchasing parse refuses it and vice versa).</summary>
    public static bool IsVendorBalance(IReadOnlyList<string> args) =>
        args.Count > 1 && string.Equals(args[1], "vendor-balance", StringComparison.Ordinal);

    /// <summary>
    /// Parses <c>reconcile vendor-balance --month YYYY-MM [--vendor key] [--show-amounts]</c>
    /// (specs/008-vendor-financials/contracts/cli-and-config.md). Pure. `--product` and `--basis` do not
    /// belong to this command and are refused as unknown options; the basis of the result stays '-'.
    /// </summary>
    public static ReconcileParseResult ParseVendorBalance(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count < 2 || !string.Equals(args[0], "reconcile", StringComparison.Ordinal) ||
            !string.Equals(args[1], "vendor-balance", StringComparison.Ordinal))
        {
            return Error(VendorBalanceUsage);
        }

        string? month = null;
        string? vendor = null;
        var showAmounts = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 2; i < args.Count; i++)
        {
            var option = args[i];
            if (!seen.Add(option) && option != "--show-amounts")
            {
                return Error($"گزینه {option} بیش از یک بار آمده است. Option {option} is repeated.");
            }

            switch (option)
            {
                case "--show-amounts":
                    showAmounts = true;
                    break;
                case "--month":
                    if (!TryValue(args, ref i, out month))
                    {
                        return Error($"مقدار {option} لازم است. A value for {option} is required.");
                    }

                    break;
                case "--vendor":
                    if (!TryValue(args, ref i, out vendor))
                    {
                        return Error($"مقدار {option} لازم است. A value for {option} is required.");
                    }

                    break;
                default:
                    return Error($"گزینه ناشناخته {option}. Unknown option {option}. " + VendorBalanceUsage);
            }
        }

        if (month is null)
        {
            return Error("--month لازم است. --month YYYY-MM is required.");
        }

        var parsedMonth = ParseMonth(month);
        if (parsedMonth is null)
        {
            return Error("ماه باید به شکل YYYY-MM و بین ۱ تا ۱۲ باشد. --month must be YYYY-MM with a month from 1 to 12.");
        }

        var command = new ReconcileCommand(
            parsedMonth.Value.Year,
            parsedMonth.Value.Month,
            vendor is not null ? "vendor" : "none",
            vendor,
            Basis: null,
            ShowAmounts: showAmounts);
        return new ReconcileParseResult(command, null);
    }

    public static ReconcileParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count < 2 || !string.Equals(args[0], "reconcile", StringComparison.Ordinal) ||
            !string.Equals(args[1], "purchasing", StringComparison.Ordinal))
        {
            return Error(Usage);
        }

        string? month = null;
        string? vendor = null;
        string? product = null;
        string? basis = null;
        var showAmounts = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 2; i < args.Count; i++)
        {
            var option = args[i];
            if (!seen.Add(option) && option != "--show-amounts")
            {
                return Error($"گزینه {option} بیش از یک بار آمده است. Option {option} is repeated.");
            }

            switch (option)
            {
                case "--show-amounts":
                    showAmounts = true;
                    break;
                case "--month":
                    if (!TryValue(args, ref i, out month))
                    {
                        return Error($"مقدار {option} لازم است. A value for {option} is required.");
                    }

                    break;
                case "--vendor":
                    if (!TryValue(args, ref i, out vendor))
                    {
                        return Error($"مقدار {option} لازم است. A value for {option} is required.");
                    }

                    break;
                case "--product":
                    if (!TryValue(args, ref i, out product))
                    {
                        return Error($"مقدار {option} لازم است. A value for {option} is required.");
                    }

                    break;
                case "--basis":
                    if (!TryValue(args, ref i, out var rawBasis) || !PurchasingDiscountBasis.IsKnown(rawBasis))
                    {
                        return Error("پایه تخفیف باید line یا lineAndDocument باشد. --basis must be line or lineAndDocument.");
                    }

                    basis = string.Equals(rawBasis, PurchasingDiscountBasis.Line, StringComparison.OrdinalIgnoreCase)
                        ? PurchasingDiscountBasis.Line
                        : PurchasingDiscountBasis.LineAndDocument;
                    break;
                default:
                    return Error($"گزینه ناشناخته {option}. Unknown option {option}. " + Usage);
            }
        }

        if (vendor is not null && product is not null)
        {
            return Error("--vendor و --product با هم قابل استفاده نیستند. --vendor and --product are mutually exclusive.");
        }

        if (month is null)
        {
            return Error("--month لازم است. --month YYYY-MM is required.");
        }

        var parsedMonth = ParseMonth(month);
        if (parsedMonth is null)
        {
            return Error("ماه باید به شکل YYYY-MM و بین ۱ تا ۱۲ باشد. --month must be YYYY-MM with a month from 1 to 12.");
        }

        var filterKind = vendor is not null ? "vendor" : product is not null ? "product" : "none";
        var filterValue = vendor ?? product;

        var command = new ReconcileCommand(
            parsedMonth.Value.Year,
            parsedMonth.Value.Month,
            filterKind,
            filterValue,
            basis,
            showAmounts);
        return new ReconcileParseResult(command, null);
    }

    /// <summary>Parses <c>YYYY-MM</c> with Latin or Persian digits. Null when invalid.</summary>
    public static (int Year, int Month)? ParseMonth(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = SourceDateParser.ToLatinDigits(text.Trim());
        var match = MonthPattern.Match(normalized);
        if (!match.Success)
        {
            return null;
        }

        var year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        if (year < 1300 || year > 1500 || month < 1 || month > 12)
        {
            return null;
        }

        return (year, month);
    }

    private static bool TryValue(IReadOnlyList<string> args, ref int index, out string? value)
    {
        value = null;
        if (index + 1 >= args.Count)
        {
            return false;
        }

        var next = args[index + 1];
        if (string.IsNullOrWhiteSpace(next) || next.StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        index++;
        value = next.Trim();
        return true;
    }

    private static ReconcileParseResult Error(string message) => new(null, message);
}
