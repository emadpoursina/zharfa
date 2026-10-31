namespace Zharfa.Api.Analytics;

using System;
using System.Globalization;

public record DateRange(DateOnly From, DateOnly To)
{
    public int LengthInDays => To.DayNumber - From.DayNumber;
}

/// <summary>
/// Single source of period truth for KPI controllers and services.
/// Resolves Gregorian ISO ranges, computes previous comparison windows, formats Jalali labels,
/// and produces category filter SQL fragments.
/// </summary>
public static class PeriodFilters
{
    /// <summary>
    /// Resolve(fromIso, toIso) → half-open Gregorian [from, to) DateOnly range.
    /// Missing/empty → default to CURRENT Jalali month-to-date: [first day of current Jalali month, tomorrow).
    /// Malformed or from >= to → throw ArgumentException (mapped to 400 Persian).
    /// </summary>
    public static DateRange Resolve(string? fromIso, string? toIso)
    {
        if (string.IsNullOrWhiteSpace(fromIso) && string.IsNullOrWhiteSpace(toIso))
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var jalaliToday = JalaliDate.FromDate(today);
            var jalaliMonthStart = new JalaliDate(jalaliToday.Year, jalaliToday.Month, 1);
            var from = jalaliMonthStart.ToDateOnly();
            var to = today.AddDays(1); // half-open: includes today, ends tomorrow
            return new DateRange(from, to);
        }

        if (string.IsNullOrWhiteSpace(fromIso) || string.IsNullOrWhiteSpace(toIso))
        {
            throw new ArgumentException("هر دو تاریخ شروع و پایان باید مشخص باشند یا هر دو خالی بمانند.");
        }

        if (!DateOnly.TryParseExact(fromIso.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDate))
        {
            throw new ArgumentException($"فرمت تاریخ شروع نامعتبر است: '{fromIso}'. فرمت معتبر YYYY-MM-DD است.", nameof(fromIso));
        }

        if (!DateOnly.TryParseExact(toIso.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var toDate))
        {
            throw new ArgumentException($"فرمت تاریخ پایان نامعتبر است: '{toIso}'. فرمت معتبر YYYY-MM-DD است.", nameof(toIso));
        }

        if (fromDate >= toDate)
        {
            throw new ArgumentException("تاریخ شروع باید قبل از تاریخ پایان باشد.");
        }

        return new DateRange(fromDate, toDate);
    }

    /// <summary>
    /// PreviousRange(range) → if the range exactly equals a whole Jalali month ([month start, month end + 1 day)),
    /// the previous range is the PREVIOUS Jalali month exactly;
    /// otherwise the immediately preceding window of the SAME LENGTH in days.
    /// Always returns the pair so the UI can label exactly which periods are compared.
    /// </summary>
    public static DateRange PreviousRange(DateRange range)
    {
        ArgumentNullException.ThrowIfNull(range);

        var jFrom = JalaliDate.FromDate(range.From);
        if (jFrom.Day == 1)
        {
            int daysInCurrentMonth = JalaliDate.DaysInMonth(jFrom.Year, jFrom.Month);
            var expectedNextMonthStart = new JalaliDate(jFrom.Year, jFrom.Month, daysInCurrentMonth).ToDateOnly().AddDays(1);

            if (range.To == expectedNextMonthStart)
            {
                int prevYear = jFrom.Year;
                int prevMonth = jFrom.Month - 1;
                if (prevMonth == 0)
                {
                    prevYear -= 1;
                    prevMonth = 12;
                }

                int daysInPrevMonth = JalaliDate.DaysInMonth(prevYear, prevMonth);
                var prevFrom = new JalaliDate(prevYear, prevMonth, 1).ToDateOnly();
                var prevTo = new JalaliDate(prevYear, prevMonth, daysInPrevMonth).ToDateOnly().AddDays(1);
                return new DateRange(prevFrom, prevTo);
            }
        }

        int days = range.To.DayNumber - range.From.DayNumber;
        var precedingTo = range.From;
        var precedingFrom = range.From.AddDays(-days);
        return new DateRange(precedingFrom, precedingTo);
    }

    /// <summary>
    /// JalaliLabel(range) → Persian label:
    /// whole month → «مهر ۱۴۰۵» style;
    /// otherwise «۱۴۰۵/۰۸/۰۱ تا ۱۴۰۵/۰۸/۰۹».
    /// </summary>
    public static string JalaliLabel(DateRange range)
    {
        ArgumentNullException.ThrowIfNull(range);

        var jFrom = JalaliDate.FromDate(range.From);
        if (jFrom.Day == 1)
        {
            int daysInCurrentMonth = JalaliDate.DaysInMonth(jFrom.Year, jFrom.Month);
            var expectedNextMonthStart = new JalaliDate(jFrom.Year, jFrom.Month, daysInCurrentMonth).ToDateOnly().AddDays(1);

            if (range.To == expectedNextMonthStart)
            {
                return $"{jFrom.MonthName} {JalaliDate.ToPersianDigits(jFrom.Year.ToString(CultureInfo.InvariantCulture))}";
            }
        }

        var inclusiveEnd = range.To.AddDays(-1);
        var jEnd = JalaliDate.FromDate(inclusiveEnd);

        var startStr = jFrom.ToPersianDigitsString();
        var endStr = jEnd.ToPersianDigitsString();
        return $"{startStr} تا {endStr}";
    }

    /// <summary>
    /// CategoryPredicate(alias, categoryId) → returns the SQL fragment "AND <alias>.category_id = @categoryId"
    /// plus binds nothing by itself (caller binds), or empty when categoryId is null; a categoryId &lt;= 0 is a 400.
    /// </summary>
    public static string CategoryPredicate(string alias, long? categoryId)
    {
        if (categoryId is null)
        {
            return string.Empty;
        }

        if (categoryId <= 0)
        {
            throw new ArgumentException("شناسه دسته‌بندی باید عددی مثبت باشد.", nameof(categoryId));
        }

        if (string.IsNullOrWhiteSpace(alias))
        {
            return "AND category_id = @categoryId";
        }

        return $"AND {alias.Trim()}.category_id = @categoryId";
    }

    public static string CategoryPredicate(string alias, int? categoryId) =>
        CategoryPredicate(alias, (long?)categoryId);

    /// <summary>
    /// Build decision (response shape, additive to contracts/api.md):
    /// every KPI response's period object is { from, to, jalaliLabel, prev: { from, to, jalaliLabel } }
    /// so the MoM comparison can state exactly which periods it compares (spec edge case).
    /// </summary>
    public record PeriodInfo(
        string From,
        string To,
        string JalaliLabel,
        PreviousPeriodInfo Prev);

    public record PreviousPeriodInfo(
        string From,
        string To,
        string JalaliLabel);

    public static PeriodInfo BuildPeriodInfo(DateRange range)
    {
        var prev = PreviousRange(range);
        return new PeriodInfo(
            range.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            range.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            JalaliLabel(range),
            new PreviousPeriodInfo(
                prev.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                prev.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                JalaliLabel(prev)));
    }

    public static PeriodInfo BuildPeriodInfo(string? fromIso, string? toIso)
    {
        var range = Resolve(fromIso, toIso);
        return BuildPeriodInfo(range);
    }
}
