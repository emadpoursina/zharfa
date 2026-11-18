namespace Zharfa.Api.Audit;

using System;
using System.Globalization;
using Zharfa.Api.Analytics;

/// <summary>
/// A validated audit-log query (contracts/api.md §3). <see cref="FromUtc"/> is the start of the first Jalali day in the
/// sync time zone, and <see cref="ToUtcExclusive"/> the start of the day after the last one, both in UTC.
/// </summary>
public sealed record AuditQuery(
    string? User,
    string? Action,
    DateTime? FromUtc,
    DateTime? ToUtcExclusive,
    int Page,
    int PageSize)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    public const string EndBeforeStartMessage = "تاریخ پایان نمی‌تواند پیش از تاریخ شروع باشد.";
    private const string InvalidFromMessage = "تاریخ شروع نامعتبر است. قالب معتبر: yyyy-mm-dd.";
    private const string InvalidToMessage = "تاریخ پایان نامعتبر است. قالب معتبر: yyyy-mm-dd.";
    private const string InvalidPageMessage = "شمارهٔ صفحه باید عدد صحیح بزرگ‌تر یا برابر ۱ باشد.";
    private const string InvalidPageSizeMessage = "تعداد ردیف در هر صفحه باید عددی بین ۱ و ۱۰۰ باشد.";

    /// <summary>Parses the raw query values. On failure <paramref name="error"/> is the Persian message.</summary>
    public static bool TryParse(
        string? user,
        string? action,
        string? from,
        string? to,
        string? page,
        string? pageSize,
        TimeZoneInfo zone,
        out AuditQuery query,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(zone);
        query = new AuditQuery(null, null, null, null, 1, DefaultPageSize);
        error = null;

        if (!TryParseInt(page, 1, int.MaxValue, out var pageNumber))
        {
            error = InvalidPageMessage;
            return false;
        }

        if (!TryParseInt(pageSize, 1, MaxPageSize, out var size, defaultValue: DefaultPageSize))
        {
            error = InvalidPageSizeMessage;
            return false;
        }

        DateTime? fromUtc = null;
        JalaliDate? fromDay = null;
        if (!string.IsNullOrWhiteSpace(from))
        {
            if (!JalaliInput.TryParseDate(from, out var parsedFrom))
            {
                error = InvalidFromMessage;
                return false;
            }

            fromDay = parsedFrom;
            fromUtc = StartOfDayUtc(parsedFrom, zone);
        }

        DateTime? toExclusive = null;
        JalaliDate? toDay = null;
        if (!string.IsNullOrWhiteSpace(to))
        {
            if (!JalaliInput.TryParseDate(to, out var parsedTo))
            {
                error = InvalidToMessage;
                return false;
            }

            toDay = parsedTo;
            toExclusive = StartOfDayUtc(NextDay(parsedTo), zone);
        }

        if (fromDay is { } start && toDay is { } end && DayKey(end) < DayKey(start))
        {
            error = EndBeforeStartMessage;
            return false;
        }

        query = new AuditQuery(
            Trimmed(user),
            Trimmed(action),
            fromUtc,
            toExclusive,
            pageNumber,
            size);
        return true;
    }

    /// <summary>Start of a Jalali day as a UTC instant: local midnight in <paramref name="zone"/>.</summary>
    public static DateTime StartOfDayUtc(JalaliDate day, TimeZoneInfo zone)
    {
        var local = day.ToDateOnly();
        var midnight = new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(midnight, zone);
    }

    private static JalaliDate NextDay(JalaliDate day) => JalaliDate.FromDate(day.ToDateOnly().AddDays(1));

    private static int DayKey(JalaliDate day) => day.Year * 10000 + day.Month * 100 + day.Day;

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryParseInt(string? raw, int min, int max, out int value, int defaultValue = 1)
    {
        value = defaultValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var text = JalaliInput.ToLatinDigits(raw.Trim());
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        if (parsed < min || parsed > max)
        {
            return false;
        }

        value = parsed;
        return true;
    }
}
