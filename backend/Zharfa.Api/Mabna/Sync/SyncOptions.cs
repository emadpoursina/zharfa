namespace Zharfa.Api.Mabna.Sync;

using System;
using Microsoft.Extensions.Configuration;
using Zharfa.Api.Platform;

/// <summary>
/// Options for data synchronization from Mabna to PostgreSQL (<c>Sync__*</c>,
/// contracts/cli-and-config.md §2). An empty value from an env file falls back
/// to the documented default; built with <see cref="FromConfiguration"/>, which
/// reads every key through <see cref="ConfigurationKeys"/>.
/// </summary>
public sealed class SyncOptions
{
    public const string SectionName = "Sync";
    public const string DefaultNightlyTime = "02:00";
    public const string DefaultTimeZone = "Asia/Tehran";
    public const string DefaultWeeklyFullLoadDay = "Friday";
    public const int DefaultPageSize = 5000;
    public const int DefaultMaxRejectedRows = 0;

    private string _nightlyTime = DefaultNightlyTime;
    private string _timeZone = DefaultTimeZone;
    private string _weeklyFullLoadDay = DefaultWeeklyFullLoadDay;

    /// <summary>Local time of the daily incremental sync in HH:mm format (default 02:00).</summary>
    public string NightlyTime
    {
        get => _nightlyTime;
        set => _nightlyTime = string.IsNullOrWhiteSpace(value) ? DefaultNightlyTime : value.Trim();
    }

    /// <summary>IANA time zone id for the nightly schedule (default Asia/Tehran).</summary>
    public string TimeZone
    {
        get => _timeZone;
        set => _timeZone = string.IsNullOrWhiteSpace(value) ? DefaultTimeZone : value.Trim();
    }

    /// <summary>Day of week for the scheduled full load (default Friday; "None" disables).</summary>
    public string WeeklyFullLoadDay
    {
        get => _weeklyFullLoadDay;
        set => _weeklyFullLoadDay = string.IsNullOrWhiteSpace(value) ? DefaultWeeklyFullLoadDay : value.Trim();
    }

    /// <summary>Keyset page size when reading from Mabna (default 5000, must be &lt;= MabnaSource.MaxRows).</summary>
    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>Maximum rejected rows tolerated before a sync run fails (default 0).</summary>
    public int MaxRejectedRows { get; set; } = DefaultMaxRejectedRows;

    /// <summary>Fault injection settings for testing sync atomicity (dev + demo only).</summary>
    public SyncFaultInjectionOptions FaultInjection { get; set; } = new();

    /// <summary>
    /// Builds the options from <c>Sync__*</c> configuration. Blank values (an empty line in
    /// deploy/.env) take the default; a non-integer number refuses startup and names the key.
    /// Replaces the binder for this section, because the binder throws on an empty integer.
    /// </summary>
    public static SyncOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new SyncOptions
        {
            NightlyTime = ConfigurationKeys.Get(configuration, "Sync__NightlyTime") ?? DefaultNightlyTime,
            TimeZone = ConfigurationKeys.Get(configuration, "Sync__TimeZone") ?? DefaultTimeZone,
            WeeklyFullLoadDay = ConfigurationKeys.Get(configuration, "Sync__WeeklyFullLoadDay") ?? DefaultWeeklyFullLoadDay,
            PageSize = ConfigurationKeys.GetInt(configuration, "Sync__PageSize") ?? DefaultPageSize,
            MaxRejectedRows = ConfigurationKeys.GetInt(configuration, "Sync__MaxRejectedRows") ?? DefaultMaxRejectedRows,
            FaultInjection = new SyncFaultInjectionOptions
            {
                FailAfterTables = ConfigurationKeys.GetInt(configuration, "Sync__FaultInjection__FailAfterTables"),
            },
        };
    }

    /// <summary>
    /// Resolves <see cref="TimeZone"/> to a system time zone. Throws
    /// <see cref="InvalidOperationException"/> with a Persian + English message
    /// naming the key when the id is unknown, so startup can refuse fast.
    /// </summary>
    public static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                $"منطقه زمانی Sync__TimeZone ({timeZoneId}) معتبر نیست. " +
                $"Sync__TimeZone '{timeZoneId}' is not a known IANA time zone id.");
        }
    }

    /// <summary>
    /// Refuses a keyset page size outside 1..<paramref name="effectiveMaxRows"/> (the effective
    /// <c>MabnaSource__MaxRows</c>). <c>MabnaQuery.Build</c> clamps a larger <c>Top</c> without
    /// error, so a larger page would stop after its first short page and truncate a full load
    /// (contracts/cli-and-config.md §2). Throws <see cref="InvalidOperationException"/> naming both keys.
    /// </summary>
    public static void ValidatePageSize(int pageSize, int effectiveMaxRows)
    {
        if (pageSize < 1 || pageSize > effectiveMaxRows)
        {
            throw new InvalidOperationException(
                $"Sync__PageSize ({pageSize}) باید بین 1 و MabnaSource__MaxRows ({effectiveMaxRows}) باشد. " +
                $"Sync__PageSize must be between 1 and MabnaSource__MaxRows (effective {effectiveMaxRows}).");
        }
    }
}

public sealed class SyncFaultInjectionOptions
{
    /// <summary>Fail the sync run after synchronizing this many tables (null = no fault).</summary>
    public int? FailAfterTables { get; set; }
}

/// <summary>
/// Configuration for locating the Mabna source map (<c>MabnaSync__*</c>).
/// </summary>
public sealed class MabnaSyncOptions
{
    public const string SectionName = "MabnaSync";

    /// <summary>Path to the gitignored source-map JSON file. Unset disables sync with 'source-not-configured'.</summary>
    public string? SourceMapFile { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SourceMapFile);
}
