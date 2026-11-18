namespace Zharfa.Api.Analytics;

using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Reads a typed Jalali date (<c>yyyy-mm-dd</c>, Latin or Persian/Arabic-Indic digits) for query strings and settings.
/// Pure: no clock and no time zone. Only this exact shape is accepted.
/// </summary>
public static class JalaliInput
{
    private static readonly Regex DatePattern = new(@"^(\d{4})-(\d{2})-(\d{2})$", RegexOptions.CultureInvariant);

    /// <summary>Maps Persian (U+06F0–U+06F9) and Arabic-Indic (U+0660–U+0669) digits to Latin digits.</summary>
    public static string ToLatinDigits(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (ch is >= '۰' and <= '۹')
            {
                builder.Append((char)('0' + (ch - '۰')));
            }
            else if (ch is >= '٠' and <= '٩')
            {
                builder.Append((char)('0' + (ch - '٠')));
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Parses <c>yyyy-mm-dd</c> with a real Jalali month (1–12) and day (1 to the month's length). Returns false for
    /// any other text, for an out-of-range year, and for a day that does not exist in that month.
    /// </summary>
    public static bool TryParseDate(string? raw, out JalaliDate date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = ToLatinDigits(raw.Trim());
        var match = DatePattern.Match(text);
        if (!match.Success)
        {
            return false;
        }

        var year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var day = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        if (month is < 1 or > 12 || day < 1)
        {
            return false;
        }

        try
        {
            if (day > JalaliDate.DaysInMonth(year, month))
            {
                return false;
            }

            // Converting checks the year range as well; an out-of-range year throws here.
            _ = new JalaliDate(year, month, day).ToDateOnly();
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        date = new JalaliDate(year, month, day);
        return true;
    }
}
