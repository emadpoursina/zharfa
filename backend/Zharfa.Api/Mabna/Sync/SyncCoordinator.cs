namespace Zharfa.Api.Mabna.Sync;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zharfa.Api.DataQuality;
using Zharfa.Api.Data;

/// <summary>Outcome of asking for a run (mapped to HTTP by <c>SyncAdminController</c> and the scheduler).</summary>
public abstract record SyncRequestOutcome;

/// <summary>A run started in the background (202).</summary>
public sealed record SyncStarted(SyncJobRecord Job) : SyncRequestOutcome;

/// <summary>A manual request while a run is active (409). The running job is returned when known.</summary>
public sealed record SyncConflict(SyncJobRecord? RunningJob) : SyncRequestOutcome;

/// <summary>A scheduled run that overlapped a running one: recorded as skipped.</summary>
public sealed record SyncSkipped(SyncJobRecord Job) : SyncRequestOutcome;

/// <summary>The run cannot start (503). Reason is one of the contract codes.</summary>
public sealed record SyncRejected(string Reason, string Message) : SyncRequestOutcome;

/// <summary>Install settings the coordinator needs, resolved once at startup.</summary>
public sealed record SyncRuntimeSettings(bool IsProduction, bool IsDemo, string EnvironmentName, TimeZoneInfo Zone);

public sealed record SyncScheduleInfo(string NightlyTime, string TimeZone, DateTimeOffset NextRunAt, string NextRunAtJalali);

public sealed record SyncOverview(
    SyncPreflightResult Source,
    SyncScheduleInfo Schedule,
    SyncJobRecord? Running,
    IReadOnlyList<SyncJobRecord> Jobs);

/// <summary>
/// Decides whether a purchasing sync run starts, is skipped, conflicts or is refused, and runs
/// accepted runs in the background. One run at a time: the store's advisory lock and partial
/// unique index refuse a second running job. A failed run records the scrubbed error and the
/// per-table counts that were reached; the data transaction is rolled back by the runner.
/// </summary>
public sealed class SyncCoordinator(
    ISyncPreflight preflight,
    ISyncJobStore store,
    ISyncRunWorker worker,
    IOptions<SyncOptions> options,
    SyncRuntimeSettings runtime,
    TimeProvider clock,
    ILogger<SyncCoordinator> logger,
    IDataQualityValidator? dataQuality = null)
{
    private readonly object _gate = new();
    private Task _active = Task.CompletedTask;

    /// <summary>Completes when the most recently started background run has finished (tests and shutdown).</summary>
    public Task WaitForActiveRunAsync()
    {
        lock (_gate)
        {
            return _active;
        }
    }

    public async Task<SyncRequestOutcome> RequestRunAsync(SyncTrigger trigger, string? requestedBy, CancellationToken cancellationToken)
    {
        var check = await preflight.CheckAsync(probeLogin: true, cancellationToken);
        if (check.Refused)
        {
            return new SyncRejected(check.RefusalReason!, check.RefusalMessage!);
        }

        var now = clock.GetUtcNow();
        var weeklyDay = SyncSchedule.ParseWeeklyFullLoadDay(options.Value.WeeklyFullLoadDay);
        var weeklyDue = SyncSchedule.IsWeeklyFullLoadDue(now, runtime.Zone, weeklyDay);
        var needsFullLoad = await store.AnyTableNeedsFullLoadAsync(cancellationToken);
        var mode = SyncSchedule.ChooseMode(trigger, weeklyDue, needsFullLoad);
        var newJob = new NewSyncJob(trigger, mode, requestedBy, now);

        var start = await store.StartAsync(newJob, cancellationToken);
        if (!start.Started)
        {
            if (trigger == SyncTrigger.Scheduled)
            {
                var skipped = await store.RecordSkippedAsync(newJob, "a run was already in progress", cancellationToken);
                logger.LogInformation("Scheduled purchasing sync skipped: a run was already in progress.");
                return new SyncSkipped(skipped);
            }

            var running = await store.GetRunningAsync(cancellationToken);
            return new SyncConflict(running);
        }

        var faultAfter = SyncSchedule.EffectiveFaultInjection(
            options.Value.FaultInjection.FailAfterTables,
            runtime.EnvironmentName,
            runtime.IsDemo,
            out var faultWarning);
        if (faultWarning is not null)
        {
            logger.LogWarning("{Warning}", faultWarning);
        }

        var request = new SyncRunRequest(
            start.JobId,
            trigger,
            mode,
            now,
            check.Map!,
            runtime.IsProduction,
            runtime.Zone,
            options.Value.PageSize,
            options.Value.MaxRejectedRows,
            faultAfter);

        var job = new SyncJobRecord(
            start.JobId, trigger, mode, SyncVocabulary.StatusRunning, requestedBy, now, null, null, null, null, []);

        var runLock = start.RunLock!;
        lock (_gate)
        {
            _active = Task.Run(() => ExecuteAsync(runLock, request));
        }

        return new SyncStarted(job);
    }

    public async Task<SyncOverview> GetOverviewAsync(int limit, CancellationToken cancellationToken)
    {
        var source = await preflight.CheckAsync(probeLogin: false, cancellationToken);
        var running = await store.GetRunningAsync(cancellationToken);
        var jobs = await store.ListAsync(limit, cancellationToken);
        return new SyncOverview(source, GetScheduleInfo(), running, jobs);
    }

    public Task<SyncJobRecord?> GetJobAsync(long id, CancellationToken cancellationToken) =>
        store.GetAsync(id, cancellationToken);

    public SyncScheduleInfo GetScheduleInfo()
    {
        var nightly = SyncSchedule.ParseNightlyTime(options.Value.NightlyTime);
        var next = SyncSchedule.NextRunUtc(clock.GetUtcNow(), nightly, runtime.Zone);
        return new SyncScheduleInfo(
            nightly.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            runtime.Zone.Id,
            next,
            SyncTimeText.Jalali(next, runtime.Zone));
    }

    /// <summary>Marks runs left 'running' by a previous process as failed (startup recovery).</summary>
    public async Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken)
    {
        var count = await store.RecoverInterruptedAsync(clock.GetUtcNow(), cancellationToken);
        if (count > 0)
        {
            logger.LogWarning("Marked {Count} interrupted purchasing sync job(s) as failed.", count);
        }

        return count;
    }

    private async Task ExecuteAsync(IAsyncDisposable runLock, SyncRunRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            try
            {
                var result = await worker.RunAsync(request, CancellationToken.None);
                await FinishAsync(request.JobId, SyncVocabulary.StatusSucceeded, stopwatch, null,
                    LogsFor(result.Tables, null));
                logger.LogInformation("Purchasing sync job {JobId} succeeded in {ElapsedMs} ms.",
                    request.JobId, stopwatch.ElapsedMilliseconds);
                await CheckDataQualityAsync(request.JobId);
            }
            catch (SyncRunException ex)
            {
                var error = FailureText(ex);
                await FinishAsync(request.JobId, SyncVocabulary.StatusFailed, stopwatch, error,
                    LogsFor(ex.CompletedTables, error));
                logger.LogWarning("Purchasing sync job {JobId} failed: {Error}", request.JobId, error);
            }
            catch (Exception ex)
            {
                var error = FailureText(ex);
                await FinishAsync(request.JobId, SyncVocabulary.StatusFailed, stopwatch, error,
                    LogsFor([], error));
                logger.LogWarning("Purchasing sync job {JobId} failed [{ExceptionType}].", request.JobId, ex.GetType().Name);
            }
        }
        finally
        {
            await runLock.DisposeAsync();
        }
    }

    /// <summary>
    /// The data-quality check of a succeeded run (research R12). A failure is logged and never changes the job's status.
    /// A failed run never reaches this method, so the previous check result stays current.
    /// </summary>
    private async Task CheckDataQualityAsync(long jobId)
    {
        if (dataQuality is null)
        {
            return;
        }

        try
        {
            await dataQuality.RunAsync(jobId, clock.GetUtcNow(), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Data-quality check after sync job {JobId} failed [{ExceptionType}].", jobId, ex.GetType().Name);
        }
    }

    private async Task FinishAsync(
        long jobId,
        string status,
        Stopwatch stopwatch,
        string? error,
        IReadOnlyList<SyncLogEntry> logs)
    {
        try
        {
            stopwatch.Stop();
            var finish = new SyncJobFinish(status, clock.GetUtcNow(), stopwatch.ElapsedMilliseconds, error);
            await store.FinishAsync(jobId, finish, logs, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError("Could not record the result of purchasing sync job {JobId} [{ExceptionType}].",
                jobId, ex.GetType().Name);
        }
    }

    private static string FailureText(Exception ex) =>
        "همگام‌سازی ناموفق بود: " + RedactionHelper.Scrub(SyncRunner.SafeErrorMessage(ex));

    private static IReadOnlyList<SyncLogEntry> LogsFor(IReadOnlyList<SyncTableCounts> tables, string? error)
    {
        var logs = new List<SyncLogEntry>();
        foreach (var table in tables)
        {
            var hasIssues = table.Rejected > 0 || table.Warnings > 0;
            var message = $"warnings={table.Warnings}" + (table.Detail is null ? string.Empty : $"; {table.Detail}");
            logs.Add(new SyncLogEntry(hasIssues ? SyncVocabulary.LevelWarning : SyncVocabulary.LevelInfo, message, table));
        }

        if (error is not null)
        {
            logs.Add(new SyncLogEntry(SyncVocabulary.LevelError, error));
        }

        return logs;
    }
}
