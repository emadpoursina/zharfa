namespace Zharfa.Api.Mabna.Sync;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Zharfa.Api.DataQuality;
using Zharfa.Api.Platform;

/// <summary>Registers the purchasing sync (spec 003, US2). Call after <c>AddMabnaSource</c>.</summary>
public static class PurchasingSyncServiceCollectionExtensions
{
    /// <summary>
    /// Validates the schedule settings (refusing startup with the key named), then registers the
    /// store, runner, preflight, coordinator and the nightly scheduler.
    /// </summary>
    public static IServiceCollection AddPurchasingSync(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        DataModeOptions dataMode)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(dataMode);

        var syncOptions = SyncOptions.FromConfiguration(configuration);
        var zone = SyncOptions.ResolveTimeZone(syncOptions.TimeZone);
        SyncSchedule.ParseNightlyTime(syncOptions.NightlyTime);
        SyncSchedule.ParseWeeklyFullLoadDay(syncOptions.WeeklyFullLoadDay);

        var connectionString = configuration.GetConnectionString("Zharfa");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'Zharfa' is missing.");
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(NpgsqlDataSource.Create(connectionString));
        services.AddSingleton(new SyncRuntimeSettings(dataMode.IsProduction, dataMode.IsDemo, environment.EnvironmentName, zone));
        services.AddSingleton<ISyncJobStore, PostgresSyncJobStore>();
        services.AddSingleton<ISyncRunWorker, SyncRunner>();
        services.AddSingleton<ISyncPreflight, SyncPreflight>();
        services.AddSingleton<IDataQualityValidator, DataQualityValidator>();
        services.AddSingleton<SyncCoordinator>();
        services.AddHostedService<SyncScheduler>();
        return services;
    }

    /// <summary>
    /// Startup recovery: marks sync jobs left 'running' by a previous process as failed
    /// ("interrupted"). Never stops startup; a failure is logged without details.
    /// </summary>
    public static async Task RecoverInterruptedSyncRunsAsync(this IServiceProvider services, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        try
        {
            var coordinator = services.GetRequiredService<SyncCoordinator>();
            await coordinator.RecoverInterruptedAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Startup recovery of purchasing sync jobs was skipped [{ExceptionType}].", ex.GetType().Name);
        }
    }

    /// <summary>
    /// Startup warning when the configured source map is invalid (spec 003 FR-001/FR-005). Logs the
    /// count only, never the problem texts (they can name real source objects); the Data sync page
    /// lists the problems for the Admin. Never stops startup and never connects to the source.
    /// </summary>
    public static async Task WarnIfSourceMapInvalidAsync(this IServiceProvider services, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        try
        {
            var preflight = services.GetRequiredService<ISyncPreflight>();
            var check = await preflight.CheckAsync(probeLogin: false, CancellationToken.None);
            if (check.Configured && !check.MapValid)
            {
                logger.LogWarning(
                    "Purchasing sync source map is invalid ({ProblemCount} problem(s)); sync runs are refused until it is corrected. " +
                    "The Data sync page lists the problems.",
                    check.MapProblems.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Purchasing sync source map check was skipped [{ExceptionType}].", ex.GetType().Name);
        }
    }
}
