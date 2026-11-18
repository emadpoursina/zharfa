namespace Zharfa.Api.Purchasing.Reports;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Zharfa.Api.Analytics;
using Zharfa.Api.Purchasing;

/// <summary>Period kinds accepted by the purchasing reports (spec 004 FR-018).</summary>
public enum JalaliPeriodKind
{
    Month,
    Quarter,
    Year,
    Custom,
}

/// <summary>One trend bucket: a Jalali month, flagged when only part of it lies inside a custom range.</summary>
public sealed record TrendBucket(JalaliMonth Month, bool Partial);

/// <summary>
/// The selected Jalali period (research R3, R4). Pure: no database, no network. Day arithmetic goes through
/// Julian day numbers so Esfand 30 and year boundaries are exact. <see cref="From"/> and <see cref="To"/> are
/// inclusive; <see cref="To"/> is the effective end (trimmed to today when the requested end is later).
/// </summary>
public sealed class JalaliPeriod
{
    /// <summary>Longest custom range accepted (three Jalali years, FR-022).</summary>
    public const int MaxCustomDays = 1098;

    private static readonly string[] QuarterNames = ["فصل اول", "فصل دوم", "فصل سوم", "فصل چهارم"];
    private static readonly Regex DayPattern = new(@"^(\d{4})-(\d{2})-(\d{2})$", RegexOptions.CultureInvariant);
    private static readonly Regex QuarterPattern = new(@"^(\d{4})-Q([1-4])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex YearPattern = new(@"^\d{4}$", RegexOptions.CultureInvariant);

    private JalaliPeriod(JalaliPeriodKind kind, JalaliDate from, JalaliDate to, JalaliDate presetEnd, JalaliDate? trimmedTo)
    {
        Kind = kind;
        From = from;
        To = to;
        PresetEnd = presetEnd;
        TrimmedTo = trimmedTo;
    }

    public JalaliPeriodKind Kind { get; }

    /// <summary>First day, inclusive.</summary>
    public JalaliDate From { get; }

    /// <summary>Effective last day, inclusive (trimmed to today when the requested end was later).</summary>
    public JalaliDate To { get; }

    /// <summary>The unclipped end: the last day of a preset, or the requested end of a custom range.</summary>
    public JalaliDate PresetEnd { get; }

    /// <summary>The day the requested end was trimmed to, or null when nothing was trimmed.</summary>
    public JalaliDate? TrimmedTo { get; }

    /// <summary>The wire name of the kind: month, quarter, year or custom.</summary>
    public string KindName => Kind switch
    {
        JalaliPeriodKind.Month => "month",
        JalaliPeriodKind.Quarter => "quarter",
        JalaliPeriodKind.Year => "year",
        _ => "custom",
    };

    /// <summary>yyyymmdd key of <see cref="From"/>, for SQL day-key ranges.</summary>
    public int FromKey => DayKey(From);

    /// <summary>yyyymmdd key of <see cref="To"/>, for SQL day-key ranges.</summary>
    public int ToKey => DayKey(To);

    /// <summary>Number of days in the effective range, inclusive.</summary>
    public int Days => Jdn(To) - Jdn(From) + 1;

    /// <summary>The Jalali month of <see cref="From"/>.</summary>
    public JalaliMonth StartMonth => new(From.Year, From.Month);

    /// <summary>The Jalali month of the effective <see cref="To"/>.</summary>
    public JalaliMonth EndMonth => new(To.Year, To.Month);

    /// <summary>Persian label, for example «آبان ۱۴۰۵», «فصل سوم ۱۴۰۵», «سال ۱۴۰۵» or «۱۴۰۵/۰۷/۱۰ تا ۱۴۰۵/۰۸/۲۴».</summary>
    public string Label => Kind switch
    {
        JalaliPeriodKind.Month => StartMonth.Label,
        JalaliPeriodKind.Quarter => $"{QuarterNames[(From.Month - 1) / 3]} {Digits(From.Year)}",
        JalaliPeriodKind.Year => $"سال {Digits(From.Year)}",
        _ => $"{From.ToPersianDigitsString()} تا {To.ToPersianDigitsString()}",
    };

    /// <summary>
    /// The previous period of the same kind (spec clarification, research R3): a month, quarter or year preset
    /// compares with the immediately preceding Jalali month, quarter or year; a custom range compares with the
    /// same number of days ending the day before it starts. The previous period is never trimmed.
    /// </summary>
    public JalaliPeriod Previous()
    {
        switch (Kind)
        {
            case JalaliPeriodKind.Month:
            {
                var previous = StartMonth.Previous();
                return Unclipped(JalaliPeriodKind.Month, MonthStart(previous), MonthEnd(previous));
            }
            case JalaliPeriodKind.Quarter:
            {
                var quarter = (From.Month - 1) / 3;
                var year = From.Year;
                if (quarter == 0)
                {
                    quarter = 3;
                    year -= 1;
                }
                else
                {
                    quarter -= 1;
                }

                return Unclipped(JalaliPeriodKind.Quarter, QuarterStart(year, quarter), QuarterEnd(year, quarter));
            }
            case JalaliPeriodKind.Year:
            {
                var year = From.Year - 1;
                return Unclipped(JalaliPeriodKind.Year,
                    new JalaliDate(year, 1, 1),
                    new JalaliDate(year, 12, JalaliDate.DaysInMonth(year, 12)));
            }
            default:
            {
                var days = Days;
                var previousToJdn = Jdn(From) - 1;
                var previousFromJdn = previousToJdn - (days - 1);
                return Unclipped(JalaliPeriodKind.Custom, FromJdn(previousFromJdn), FromJdn(previousToJdn));
            }
        }
    }

    /// <summary>
    /// Trend buckets (research R4). A preset, and a profile trend of any range, shows the 12 Jalali months ending
    /// with the period's last month (a custom profile ends with the effective end). A custom range otherwise
    /// shows the months it touches, flagging the first and last bucket as partial when the requested range does not
    /// cover the whole month (a trim to today is not a partial month; it is stated by <see cref="TrimmedTo"/>).
    /// </summary>
    public IReadOnlyList<TrendBucket> TrendMonths(bool profile)
    {
        if (Kind != JalaliPeriodKind.Custom || profile)
        {
            var end = Kind == JalaliPeriodKind.Custom ? EndMonth : new JalaliMonth(PresetEnd.Year, PresetEnd.Month);
            var window = JalaliMonth.Window(end, 12);
            var buckets = new TrendBucket[window.Count];
            for (var i = 0; i < window.Count; i++)
            {
                buckets[i] = new TrendBucket(window[i], false);
            }

            return buckets;
        }

        var first = StartMonth;
        var last = EndMonth;
        var list = new List<TrendBucket>();
        for (var month = first; month.Index <= last.Index; month = month.AddMonths(1))
        {
            // Judged against the requested range (PresetEnd), not the trimmed To: research R4 flags a bucket
            // only when the range does not cover the whole month.
            var partialStart = month.Index == first.Index && From.Day != 1;
            var partialEnd = Jdn(PresetEnd) < Jdn(MonthEnd(month));
            list.Add(new TrendBucket(month, partialStart || partialEnd));
        }

        return list;
    }

    /// <summary>
    /// Parses a period from the query: <paramref name="period"/> is month (default), quarter, year or custom;
    /// <paramref name="value"/> is the YYYY-MM, YYYY-Qn or YYYY value for the preset (empty = current unit);
    /// <paramref name="from"/> and <paramref name="to"/> are YYYY-MM-DD for a custom range. Latin, Persian and
    /// Arabic-Indic digits are accepted. Errors carry Persian messages (contracts/api.md).
    /// </summary>
    public static bool TryParse(
        string? period,
        string? value,
        string? from,
        string? to,
        JalaliDate today,
        out JalaliPeriod? result,
        out string? error)
    {
        result = null;
        error = null;
        var kind = Normalize(period).ToLowerInvariant();
        var text = Normalize(value);

        switch (kind)
        {
            case "":
            case "month":
            {
                JalaliMonth month;
                if (text.Length == 0)
                {
                    month = new JalaliMonth(today.Year, today.Month);
                }
                else if (!JalaliMonth.TryParse(text, out month))
                {
                    error = "ماه جلالی باید به صورت YYYY-MM و با ماه بین ۱ تا ۱۲ باشد.";
                    return false;
                }

                return Build(JalaliPeriodKind.Month, MonthStart(month), MonthEnd(month), today, out result, out error);
            }
            case "quarter":
            {
                int year = today.Year;
                int quarter = 0;
                if (text.Length == 0)
                {
                    year = today.Year;
                    quarter = (today.Month - 1) / 3;
                }
                else
                {
                    var match = QuarterPattern.Match(text);
                    if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out year) ||
                        year < JalaliMonth.MinYear || year > JalaliMonth.MaxYear)
                    {
                        error = "فصل جلالی باید به صورت YYYY-Qn و با n بین ۱ تا ۴ باشد.";
                        return false;
                    }

                    quarter = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) - 1;
                }

                return Build(JalaliPeriodKind.Quarter, QuarterStart(year, quarter), QuarterEnd(year, quarter), today, out result, out error);
            }
            case "year":
            {
                int year = today.Year;
                if (text.Length > 0)
                {
                    if (!YearPattern.IsMatch(text) || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out year) ||
                        year < JalaliMonth.MinYear || year > JalaliMonth.MaxYear)
                    {
                        error = "سال جلالی باید چهار رقمی و معتبر باشد.";
                        return false;
                    }
                }

                return Build(JalaliPeriodKind.Year,
                    new JalaliDate(year, 1, 1),
                    new JalaliDate(year, 12, JalaliDate.DaysInMonth(year, 12)),
                    today,
                    out result,
                    out error);
            }
            case "custom":
            {
                if (!TryParseDay(from, out var start))
                {
                    error = "تاریخ آغاز بازه باید به صورت YYYY-MM-DD و یک روز معتبر تقویمی باشد.";
                    return false;
                }

                if (!TryParseDay(to, out var end))
                {
                    error = "تاریخ پایان بازه باید به صورت YYYY-MM-DD و یک روز معتبر تقویمی باشد.";
                    return false;
                }

                if (Jdn(end) < Jdn(start))
                {
                    error = "تاریخ پایان بازه نمی‌تواند پیش از تاریخ آغاز باشد.";
                    return false;
                }

                if (Jdn(end) - Jdn(start) + 1 > MaxCustomDays)
                {
                    error = "حداکثر طول بازه ۱٬۰۹۸ روز است؛ بازه را کوتاه‌تر کنید.";
                    return false;
                }

                return Build(JalaliPeriodKind.Custom, start, end, today, out result, out error);
            }
            default:
                error = "نوع بازه باید month، quarter، year یا custom باشد.";
                return false;
        }
    }

    /// <summary>The sync-zone day key of a date (yyyymmdd), as stored in purchase_document.jalali_key.</summary>
    public static int DayKey(JalaliDate date) => date.Year * 10000 + date.Month * 100 + date.Day;

    private static bool Build(
        JalaliPeriodKind kind,
        JalaliDate from,
        JalaliDate end,
        JalaliDate today,
        out JalaliPeriod? result,
        out string? error)
    {
        result = null;
        error = null;
        var todayJdn = Jdn(today);
        if (Jdn(from) > todayJdn)
        {
            error = "این بازه کاملاً در آینده است.";
            return false;
        }

        var trimmed = Jdn(end) > todayJdn ? today : (JalaliDate?)null;
        var effectiveTo = trimmed ?? end;
        result = new JalaliPeriod(kind, from, effectiveTo, end, trimmed);
        return true;
    }

    private static JalaliPeriod Unclipped(JalaliPeriodKind kind, JalaliDate from, JalaliDate to) =>
        new(kind, from, to, to, null);

    private static JalaliDate MonthStart(JalaliMonth month) => new(month.Year, month.Month, 1);

    private static JalaliDate MonthEnd(JalaliMonth month) =>
        new(month.Year, month.Month, JalaliDate.DaysInMonth(month.Year, month.Month));

    private static JalaliDate QuarterStart(int year, int quarter) => new(year, quarter * 3 + 1, 1);

    private static JalaliDate QuarterEnd(int year, int quarter)
    {
        var lastMonth = quarter * 3 + 3;
        return new JalaliDate(year, lastMonth, JalaliDate.DaysInMonth(year, lastMonth));
    }

    private static bool TryParseDay(string? text, out JalaliDate date)
    {
        date = default;
        var match = DayPattern.Match(Normalize(text));
        if (!match.Success)
        {
            return false;
        }

        var year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var day = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        if (year < JalaliMonth.MinYear || year > JalaliMonth.MaxYear || month is < 1 or > 12)
        {
            return false;
        }

        if (day < 1 || day > JalaliDate.DaysInMonth(year, month))
        {
            return false;
        }

        date = new JalaliDate(year, month, day);
        return true;
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var chars = text.Trim().ToCharArray();
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

    private static string Digits(int value) =>
        JalaliDate.ToPersianDigits(value.ToString(CultureInfo.InvariantCulture));

    private static int Jdn(JalaliDate date) => JalaliDate.JalaliToJdn(date.Year, date.Month, date.Day);

    private static JalaliDate FromJdn(int jdn)
    {
        var (year, month, day) = JalaliDate.JdnToJalali(jdn);
        return new JalaliDate(year, month, day);
    }
}
