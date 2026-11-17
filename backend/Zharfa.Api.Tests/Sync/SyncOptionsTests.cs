namespace Zharfa.Api.Tests.Sync;

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Zharfa.Api.Mabna.Sync;

/// <summary>
/// T082: blank Sync__* values fall back to their defaults instead of throwing at startup
/// (contracts/cli-and-config.md §2). No database or network access.
/// </summary>
public class SyncOptionsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values)
    {
        var map = new Dictionary<string, string?>();
        foreach (var (key, value) in values)
        {
            map[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(map).Build();
    }

    [Fact]
    public void Empty_configuration_uses_every_default()
    {
        var options = SyncOptions.FromConfiguration(Config());

        Assert.Equal(SyncOptions.DefaultNightlyTime, options.NightlyTime);
        Assert.Equal(SyncOptions.DefaultTimeZone, options.TimeZone);
        Assert.Equal(SyncOptions.DefaultWeeklyFullLoadDay, options.WeeklyFullLoadDay);
        Assert.Equal(5000, options.PageSize);
        Assert.Equal(0, options.MaxRejectedRows);
        Assert.Null(options.FaultInjection.FailAfterTables);
    }

    [Fact]
    public void Blank_env_file_values_fall_back_to_defaults_without_throwing()
    {
        var options = SyncOptions.FromConfiguration(Config(
            ("Sync:NightlyTime", ""),
            ("Sync:TimeZone", "   "),
            ("Sync:WeeklyFullLoadDay", ""),
            ("Sync:PageSize", ""),
            ("Sync:MaxRejectedRows", "  "),
            ("Sync:FaultInjection:FailAfterTables", "")));

        Assert.Equal(SyncOptions.DefaultNightlyTime, options.NightlyTime);
        Assert.Equal(SyncOptions.DefaultTimeZone, options.TimeZone);
        Assert.Equal(SyncOptions.DefaultWeeklyFullLoadDay, options.WeeklyFullLoadDay);
        Assert.Equal(SyncOptions.DefaultPageSize, options.PageSize);
        Assert.Equal(SyncOptions.DefaultMaxRejectedRows, options.MaxRejectedRows);
        Assert.Null(options.FaultInjection.FailAfterTables);
    }

    [Fact]
    public void Blank_double_underscore_keys_fall_back_to_defaults()
    {
        var options = SyncOptions.FromConfiguration(Config(
            ("Sync__PageSize", ""),
            ("Sync__MaxRejectedRows", ""),
            ("Sync__FaultInjection__FailAfterTables", "")));

        Assert.Equal(SyncOptions.DefaultPageSize, options.PageSize);
        Assert.Equal(SyncOptions.DefaultMaxRejectedRows, options.MaxRejectedRows);
        Assert.Null(options.FaultInjection.FailAfterTables);
    }

    [Fact]
    public void Numeric_values_are_honoured_in_both_key_forms()
    {
        var options = SyncOptions.FromConfiguration(Config(
            ("Sync:PageSize", "1200"),
            ("Sync__MaxRejectedRows", "7"),
            ("Sync__FaultInjection__FailAfterTables", "3")));

        Assert.Equal(1200, options.PageSize);
        Assert.Equal(7, options.MaxRejectedRows);
        Assert.Equal(3, options.FaultInjection.FailAfterTables);
    }

    [Fact]
    public void Non_integer_page_size_refuses_and_names_the_key()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            SyncOptions.FromConfiguration(Config(("Sync__PageSize", "five-thousand"))));

        Assert.Contains("Sync__PageSize", error.Message);
    }

    [Fact]
    public void Page_size_within_the_effective_ceiling_is_accepted()
    {
        SyncOptions.ValidatePageSize(1, 10_000);
        SyncOptions.ValidatePageSize(SyncOptions.DefaultPageSize, 10_000);
        SyncOptions.ValidatePageSize(10_000, 10_000);
    }

    [Fact]
    public void Zero_or_negative_page_size_is_refused_and_names_both_keys()
    {
        foreach (var pageSize in new[] { 0, -1 })
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                SyncOptions.ValidatePageSize(pageSize, 10_000));

            Assert.Contains("Sync__PageSize", error.Message);
            Assert.Contains("MabnaSource__MaxRows", error.Message);
        }
    }

    [Fact]
    public void Page_size_above_the_effective_max_rows_is_refused_and_names_both_keys()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            SyncOptions.ValidatePageSize(10_001, 10_000));

        Assert.Contains("Sync__PageSize", error.Message);
        Assert.Contains("MabnaSource__MaxRows", error.Message);
        Assert.Contains("10000", error.Message);
    }

    [Fact]
    public void Blank_time_zone_takes_the_default_time_zone()
    {
        var options = SyncOptions.FromConfiguration(Config(("Sync__TimeZone", "")));

        Assert.Equal("Asia/Tehran", options.TimeZone);
        Assert.NotNull(SyncOptions.ResolveTimeZone(options.TimeZone));
    }
}
