namespace Zharfa.Api.VendorFinancials;

using System;
using System.Globalization;
using System.Text;
using Zharfa.Api.Analytics;
using Zharfa.Api.Purchasing;
using Zharfa.Api.Purchasing.Reports;

/// <summary>
/// Period rules of the vendor financials (specs/008-vendor-financials/research.md R2, tasks.md T029). Pure.
/// Balance and outstanding are evaluated as of the period end: the last Jalali day of a month/quarter/year
/// preset (leap Esfand included) or the `to` of a custom range. The payments window and the purchases window
/// use the effective (trimmed) range, exactly like the Phase 2 purchasing reports, so net purchases equal the
/// vendor-profile net for the same period.
/// </summary>
public static class VendorFinancialPeriod
{
    /// <summary>As-of date of the balance and outstanding figures: the untrimmed period end.</summary>
    public static DateOnly AsOf(JalaliPeriod period) => period.PresetEnd.ToDateOnly();

    /// <summary>First day of the payments/purchases window (effective range).</summary>
    public static DateOnly From(JalaliPeriod period) => period.From.ToDateOnly();

    /// <summary>Last day of the payments/purchases window (effective range, trimmed to today).</summary>
    public static DateOnly To(JalaliPeriod period) => period.To.ToDateOnly();

    /// <summary>The period DTO of the financial responses (kind + preset value + inclusive bounds + label).</summary>
    public static VendorFinancialPeriodDto ToDto(JalaliPeriod period) => new(
        period.KindName,
        period.Kind == JalaliPeriodKind.Month ? period.StartMonth.Key : null,
        period.FromWire,
        period.ToWire,
        period.Label);

    /// <summary>
    /// True when the previous period has a data base (the previous comparison columns may show numbers).
    /// The base follows the Phase 2 rule: the first month with synced purchase documents is on or before the
    /// previous period. False when compare is off or the previous base is missing (no-previous-data) — the
    /// current figures stay zeros in that case, never unavailable.
    /// </summary>
    public static bool HasPreviousBase(JalaliPeriod period, JalaliMonth? firstDataMonth) =>
        firstDataMonth is { } first && ComparisonMath.HasPreviousData(period.Previous().StartMonth, first);

    /// <summary>
    /// The server's search key (the C# twin of <c>mabna.fn_search_key</c>): Arabic yeh/kaf become Persian,
    /// Persian and Arabic-Indic digits become Latin, whitespace, ZWNJ, tatweel and '|' are removed, lower-cased.
    /// </summary>
    public static string SearchKey(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        foreach (var raw in text)
        {
            var ch = raw switch
            {
                'ي' => 'ی',
                'ى' => 'ی',
                'ك' => 'ک',
                >= '۰' and <= '۹' => (char)('0' + (raw - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (raw - '٠')),
                _ => raw,
            };

            if (char.IsWhiteSpace(ch) || ch is '‌' or 'ـ' or '|')
            {
                continue;
            }

            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }

    /// <summary>Persian error message of an invalid period query (the shared JalaliPeriod messages).</summary>
    public const string PeriodErrorFallback = "بازه زمانی نامعتبر است.";
}
