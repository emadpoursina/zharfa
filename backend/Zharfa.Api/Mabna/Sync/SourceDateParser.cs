namespace Zharfa.Api.Mabna.Sync;

using System;
using System.Globalization;
using Zharfa.Api.Analytics;

/// <summary>Result of parsing a source date column (failure is a value, never an exception).</summary>
public readonly record struct SourceDate(
    DateOnly Gregorian,
    int JalaliYear,
    int JalaliMonth,
    int JalaliDay);

/// <summary>
/// Parses a source date column according to the format declared in the
/// source map (research R10): <c>jalaliText</c> (<c>1403/12/30</c> or
/// <c>1403-12-30</c>, Persian or Latin digits), <c>jalaliInt</c>
/// (<c>14031230</c>) or <c>gregorian</c> (<c>date</c>/<c>datetime</c>).
/// Invalid input (including Esfand 30 of a non-leap year) returns
/// <c>false</c> — never throws, so the caller can reject the row.
/// </summary>
public static class SourceDateParser
{
    public const string JalaliTextFormat = "jalaliText";
    public const string JalaliIntFormat = "jalaliInt";
    public const string GregorianFormat = "gregorian";

    public static bool TryParse(string? raw, string? format, out SourceDate result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.Trim();
        return format switch
        {
            JalaliTextFormat => TryParseJalaliText(text, out result),
            JalaliIntFormat => TryParseJalaliInt(text, out result),
            GregorianFormat => TryParseGregorian(text, out result),
            _ => false
        };
    }

    private static bool TryParseJalaliText(string text, out SourceDate result)
    {
        result = default;
        var normalized = ToLatinDigits(text).Replace('-', '/');
        var parts = normalized.Split('/');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var jy) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var jm) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var jd))
        {
            return false;
        }

        return TryBuild(jy, jm, jd, out result);
    }

    private static bool TryParseJalaliInt(string text, out SourceDate result)
    {
        result = default;
        var normalized = ToLatinDigits(text);
        if (normalized.Length != 8 || !normalized.All(char.IsDigit))
        {
            return false;
        }

        if (!int.TryParse(normalized[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var jy) ||
            !int.TryParse(normalized[4..6], NumberStyles.None, CultureInfo.InvariantCulture, out var jm) ||
            !int.TryParse(normalized[6..8], NumberStyles.None, CultureInfo.InvariantCulture, out var jd))
        {
            return false;
        }

        return TryBuild(jy, jm, jd, out result);
    }

    private static bool TryParseGregorian(string text, out SourceDate result)
    {
        result = default;
        var normalized = ToLatinDigits(text);

        DateOnly date;
        if (DateOnly.TryParseExact(normalized, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date))
        {
            // exact date
        }
        else if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture,
                     DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
        {
            date = DateOnly.FromDateTime(dt);
        }
        else
        {
            return false;
        }

        var jalali = JalaliDate.FromDate(date);
        if (!TryValidate(jalali.Year, jalali.Month, jalali.Day))
        {
            return false;
        }

        result = new SourceDate(date, jalali.Year, jalali.Month, jalali.Day);
        return true;
    }

    private static bool TryBuild(int jy, int jm, int jd, out SourceDate result)
    {
        result = default;
        if (!TryValidate(jy, jm, jd))
        {
            return false;
        }

        DateOnly gregorian;
        try
        {
            gregorian = new JalaliDate(jy, jm, jd).ToDateOnly();
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        result = new SourceDate(gregorian, jy, jm, jd);
        return true;
    }

    /// <summary>Month 1..12; day 1..days-in-month (Esfand 30 only in leap years).</summary>
    private static bool TryValidate(int jy, int jm, int jd)
    {
        if (jy < 1200 || jy > 1600 || jm is < 1 or > 12 || jd is < 1 or > 31)
        {
            return false;
        }

        try
        {
            return jd <= JalaliDate.DaysInMonth(jy, jm);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>Arabic-Indic and Persian digits → Latin digits; other characters unchanged.</summary>
    public static string ToLatinDigits(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var chars = input.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c >= '\u06F0' && c <= '\u06F9')       // Persian ۰-۹
            {
                chars[i] = (char)('0' + (c - '\u06F0'));
            }
            else if (c >= '\u0660' && c <= '\u0669')   // Arabic-Indic ٠-٩
            {
                chars[i] = (char)('0' + (c - '\u0660'));
            }
        }

        return new string(chars);
    }
}
