namespace Zharfa.Api.Mabna.Sync;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Nightly purchasing sync (FR-007): wakes at <c>Sync__NightlyTime</c> in <c>Sync__TimeZone</c>
/// (default 02:00 Asia/Tehran) and asks the coordinator for a <c>scheduled</c> run. On the weekly
/// full-load day the run is a full load. An overlapping run is recorded as <c>skipped</c> by the
/// coordinator, so the scheduler never waits for a run to finish.
/// </summary>
public sealed class SyncScheduler(
    SyncCoordinator coordinator,
    IOptions<SyncOptions> options,
    SyncRuntimeSettings runtime,
    TimeProvider clock,
    ILogger<SyncScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nightly = SyncSchedule.ParseNightlyTime(options.Value.NightlyTime);
        logger.LogInformation("Purchasing sync scheduler started: nightly {NightlyTime} ({TimeZone}).",
            nightly.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), runtime.Zone.Id);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = clock.GetUtcNow();
            var next = SyncSchedule.NextRunUtc(now, nightly, runtime.Zone);
            try
            {
                await Task.Delay(next - now, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var outcome = await coordinator.RequestRunAsync(SyncTrigger.Scheduled, null, stoppingToken);
                logger.LogInformation("Scheduled purchasing sync: {Outcome}.", outcome.GetType().Name);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError("Scheduled purchasing sync could not be requested [{ExceptionType}].", ex.GetType().Name);
            }

            // A timer may wake a few milliseconds early; the pause keeps the same slot from firing twice.
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
