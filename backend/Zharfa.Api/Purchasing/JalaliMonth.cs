namespace Zharfa.Api.Purchasing;

using System;
using System.Collections.Generic;
using System.Globalization;
using Zharfa.Api.Analytics;

/// <summary>
/// A Jalali (Solar Hijri) calendar month, the only period the Overview selects (spec 003 clarification,
/// FR-025). Pure value type: parsing accepts Latin or Persian digits in <c>YYYY-MM</c> (contracts/api.md §3).
/// </summary>
public readonly record struct JalaliMonth(int Year, int Month)
{
    /// <summary>Years accepted from requests; the same range the frontend date parser accepts.</summary>
    public const int MinYear = 1200;
    public const int MaxYear = 1600;

    /// <summary>Monotonic index (year × 12 + month − 1) for ordering and differences.</summary>
    public int Index => Year * 12 + Month - 1;

    public bool IsValid => Year is >= MinYear and <= MaxYear && Month is >= 1 and <= 12;

    /// <summary>Canonical key sent to the client: <c>1405-08</c> (Latin digits).</summary>
    public string Key => $"{Year.ToString("D4", CultureInfo.InvariantCulture)}-{Month.ToString("D2", CultureInfo.InvariantCulture)}";

    /// <summary>Persian label, for example <c>آبان ۱۴۰۵</c>.</summary>
    public string Label => $"{JalaliDate.GetMonthName(Month)} {JalaliDate.ToPersianDigits(Year.ToString(CultureInfo.InvariantCulture))}";

    /// <summary>The immediately preceding Jalali month; <c>1405-01</c> → <c>1404-12</c>.</summary>
    public JalaliMonth Previous() => AddMonths(-1);

    public JalaliMonth AddMonths(int months)
    {
        var index = Index + months;
        return new JalaliMonth(index / 12, index % 12 + 1);
    }

    /// <summary>
    /// Parses only the <c>YYYY-MM</c> shape of contracts/api.md §3: four-digit year, <c>-</c> separator,
    /// two-digit month, Latin or Persian digits. Surrounding whitespace is not trimmed, so <c>" 1404-12 "</c>
    /// is rejected. Returns false for any other shape (for example <c>1405-1</c> or <c>1405/08</c>), a year
    /// outside 1200..1600, or a month outside 1..12 (for example <c>1405-13</c>).
    /// </summary>
    public static bool TryParse(string? text, out JalaliMonth month)
    {
        month = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = ToLatinDigits(text);
        var parts = normalized.Split('-');
        if (parts.Length != 2 ||
            parts[0].Length != 4 ||
            parts[1].Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var monthNumber))
        {
            return false;
        }

        var candidate = new JalaliMonth(year, monthNumber);
        if (!candidate.IsValid)
        {
            return false;
        }

        month = candidate;
        return true;
    }

    /// <summary>The <paramref name="count"/> months ending at <paramref name="end"/>, oldest first.</summary>
    public static IReadOnlyList<JalaliMonth> Window(JalaliMonth end, int count)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "تعداد ماه‌ها باید حداقل ۱ باشد.");
        }

        var months = new JalaliMonth[count];
        for (var i = 0; i < count; i++)
        {
            months[i] = end.AddMonths(i - (count - 1));
        }

        return months;
    }

    private static string ToLatinDigits(string input)
    {
        var chars = input.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c >= '۰' && c <= '۹')
            {
                chars[i] = (char)('0' + (c - '۰'));
            }
            else if (c >= '٠' && c <= '٩')
            {
                chars[i] = (char)('0' + (c - '٠'));
            }
        }

        return new string(chars);
    }
}
