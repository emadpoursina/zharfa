namespace Zharfa.Api.Mabna.Sync;

using System;
using System.Globalization;
using Zharfa.Api.Analytics;

/// <summary>
/// Pure schedule rules (FR-007, contracts/cli-and-config.md §2): the nightly time in the sync
/// time zone, the weekly full-load day, the run mode, and the dev-only fault injection gate.
/// </summary>
public static class SyncSchedule
{
    /// <summary>Parses <c>Sync__NightlyTime</c> (HH:mm). Refuses startup with a message naming the key.</summary>
    public static TimeOnly ParseNightlyTime(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text) &&
            TimeOnly.TryParseExact(text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return time;
        }

        throw new InvalidOperationException(
            $"مقدار Sync__NightlyTime ({text}) باید به قالب HH:mm باشد. " +
            "Sync__NightlyTime must use the HH:mm format.");
    }

    /// <summary>Parses <c>Sync__WeeklyFullLoadDay</c>. <c>None</c> returns null (weekly full load disabled).</summary>
    public static DayOfWeek? ParseWeeklyFullLoadDay(string? text)
    {
        var value = string.IsNullOrWhiteSpace(text) ? "Friday" : text.Trim();
        if (string.Equals(value, "None", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (Enum.TryParse<DayOfWeek>(value, ignoreCase: true, out var day))
        {
            return day;
        }

        throw new InvalidOperationException(
            $"مقدار Sync__WeeklyFullLoadDay ({text}) نامعتبر است؛ یک روز انگلیسی یا None. " +
            "Sync__WeeklyFullLoadDay must be an English day name or None.");
    }

    /// <summary>Next nightly run strictly after <paramref name="nowUtc"/>, as an absolute instant.</summary>
    public static DateTimeOffset NextRunUtc(DateTimeOffset nowUtc, TimeOnly nightly, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(nowUtc, zone);
        var candidate = new DateTime(local.Year, local.Month, local.Day, nightly.Hour, nightly.Minute, 0, DateTimeKind.Unspecified);
        if (candidate <= local.DateTime)
        {
            candidate = candidate.AddDays(1);
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(candidate, zone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    /// <summary>True when the local day of <paramref name="nowUtc"/> is the weekly full-load day.</summary>
    public static bool IsWeeklyFullLoadDue(DateTimeOffset nowUtc, TimeZoneInfo zone, DayOfWeek? weeklyDay) =>
        weeklyDay is { } day && TimeZoneInfo.ConvertTime(nowUtc, zone).DayOfWeek == day;

    /// <summary>
    /// Run mode. Full when the synced copy has never had a full load (first run ever), or for a
    /// scheduled run on the weekly full-load day. A manual "Sync now" stays incremental otherwise.
    /// </summary>
    public static SyncRunMode ChooseMode(SyncTrigger trigger, bool weeklyFullLoadDue, bool needsFullLoad)
    {
        if (needsFullLoad)
        {
            return SyncRunMode.Full;
        }

        return trigger == SyncTrigger.Scheduled && weeklyFullLoadDue ? SyncRunMode.Full : SyncRunMode.Incremental;
    }

    /// <summary>
    /// Fault injection is honoured only with <c>ASPNETCORE_ENVIRONMENT=Development</c> and demo mode.
    /// Anywhere else the configured value is ignored and a warning is returned.
    /// </summary>
    public static int? EffectiveFaultInjection(int? configured, string environmentName, bool isDemo, out string? warning)
    {
        warning = null;
        if (configured is null)
        {
            return null;
        }

        var isDevelopment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
        if (isDevelopment && isDemo)
        {
            return configured;
        }

        warning = "Sync__FaultInjection__FailAfterTables ignored: honoured only with ASPNETCORE_ENVIRONMENT=Development and demo mode.";
        return null;
    }
}

/// <summary>Formatting of sync timestamps for the API: ISO 8601 with offset and Jalali text in Persian digits.</summary>
public static class SyncTimeText
{
    public static string Iso(DateTimeOffset utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(utc, zone).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    public static string Jalali(DateTimeOffset utc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(utc, zone);
        var jalali = JalaliDate.FromDateTime(local.DateTime);
        var text =
            $"{jalali.Year.ToString("D4", CultureInfo.InvariantCulture)}/" +
            $"{jalali.Month.ToString("D2", CultureInfo.InvariantCulture)}/" +
            $"{jalali.Day.ToString("D2", CultureInfo.InvariantCulture)} " +
            $"{local.Hour.ToString("D2", CultureInfo.InvariantCulture)}:" +
            $"{local.Minute.ToString("D2", CultureInfo.InvariantCulture)}";
        return JalaliDate.ToPersianDigits(text);
    }
}
