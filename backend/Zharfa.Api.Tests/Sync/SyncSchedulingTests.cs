namespace Zharfa.Api.Tests.Sync;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zharfa.Api.Mabna;
using Zharfa.Api.Mabna.Sync;

/// <summary>
/// T032: scheduler timing, weekly full-load day, run mode, fault-injection gate, and coordinator
/// decisions (manual conflict, scheduled skip, refusal, background completion, scrubbed errors).
/// Fakes only: no database, no network.
/// </summary>
public class SyncSchedulingTests
{
    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    private static readonly string ExampleMapPath =
        Path.Combine(AppContext.BaseDirectory, "example", "purchase-source-map.example.json");

    // ---------------------------------------------------------------- schedule

    [Theory]
    [InlineData("2026-11-16T20:00:00+00:00", "2026-11-16T22:30:00+00:00")] // 23:30 Tehran → next 02:00 on the 17th
    [InlineData("2026-11-16T22:00:00+00:00", "2026-11-16T22:30:00+00:00")] // 01:30 Tehran → 02:00 later today
    [InlineData("2026-11-16T22:30:00+00:00", "2026-11-17T22:30:00+00:00")] // exactly 02:00 → strictly after
    public void Nightly_run_is_the_next_0200_in_tehran_time(string now, string expectedUtc)
    {
        var next = SyncSchedule.NextRunUtc(DateTimeOffset.Parse(now), new TimeOnly(2, 0), Tehran);

        Assert.Equal(DateTimeOffset.Parse(expectedUtc), next);
    }

    [Fact]
    public void Weekly_full_load_day_is_detected_in_the_sync_time_zone()
    {
        // 2026-11-20 is a Friday; 06:00 UTC is 09:30 in Tehran, still Friday.
        var friday = new DateTimeOffset(2026, 11, 20, 6, 0, 0, TimeSpan.Zero);
        var monday = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);

        Assert.True(SyncSchedule.IsWeeklyFullLoadDue(friday, Tehran, DayOfWeek.Friday));
        Assert.False(SyncSchedule.IsWeeklyFullLoadDue(monday, Tehran, DayOfWeek.Friday));
        Assert.False(SyncSchedule.IsWeeklyFullLoadDue(friday, Tehran, null));
    }

    [Fact]
    public void Weekly_day_none_disables_the_weekly_full_load_and_invalid_day_is_refused()
    {
        Assert.Null(SyncSchedule.ParseWeeklyFullLoadDay("None"));
        Assert.Equal(DayOfWeek.Friday, SyncSchedule.ParseWeeklyFullLoadDay("Friday"));
        Assert.Equal(DayOfWeek.Friday, SyncSchedule.ParseWeeklyFullLoadDay(null));
        Assert.Throws<InvalidOperationException>(() => SyncSchedule.ParseWeeklyFullLoadDay("Funday"));
    }

    [Theory]
    [InlineData("02:00")]
    [InlineData("23:59")]
    public void Nightly_time_parses_hh_mm(string text)
    {
        Assert.Equal(TimeOnly.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
            SyncSchedule.ParseNightlyTime(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("2:00")]
    [InlineData("25:00")]
    [InlineData("02:00:00")]
    public void Nightly_time_that_is_not_hh_mm_refuses_startup_naming_the_key(string text)
    {
        var error = Assert.Throws<InvalidOperationException>(() => SyncSchedule.ParseNightlyTime(text));
        Assert.Contains("Sync__NightlyTime", error.Message);
    }

    // ---------------------------------------------------------------- mode

    [Theory]
    [InlineData(SyncTrigger.Scheduled, true, false, SyncRunMode.Full)]        // weekly full-load day
    [InlineData(SyncTrigger.Manual, true, false, SyncRunMode.Incremental)]    // Sync now stays incremental
    [InlineData(SyncTrigger.Scheduled, false, true, SyncRunMode.Full)]        // first run ever
    [InlineData(SyncTrigger.Manual, false, true, SyncRunMode.Full)]           // first run ever, manual
    [InlineData(SyncTrigger.Scheduled, false, false, SyncRunMode.Incremental)]
    public void Run_mode_is_full_on_first_run_or_the_weekly_scheduled_day(
        SyncTrigger trigger, bool weeklyDue, bool needsFull, SyncRunMode expected)
    {
        Assert.Equal(expected, SyncSchedule.ChooseMode(trigger, weeklyDue, needsFull));
    }

    // ---------------------------------------------------------------- fault injection

    [Fact]
    public void Fault_injection_is_honoured_only_in_development_demo()
    {
        Assert.Equal(2, SyncSchedule.EffectiveFaultInjection(2, "Development", isDemo: true, out var warning));
        Assert.Null(warning);
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    [InlineData("Staging", true)]
    public void Fault_injection_is_ignored_with_a_warning_outside_development_demo(string environment, bool isDemo)
    {
        Assert.Null(SyncSchedule.EffectiveFaultInjection(2, environment, isDemo, out var warning));
        Assert.NotNull(warning);
    }

    [Fact]
    public void Fault_injection_unset_stays_unset_without_warning()
    {
        Assert.Null(SyncSchedule.EffectiveFaultInjection(null, "Production", isDemo: false, out var warning));
        Assert.Null(warning);
    }

    // ---------------------------------------------------------------- coordinator decisions

    private sealed class FakeLock : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakePreflight(SyncPreflightResult result) : ISyncPreflight
    {
        public Task<SyncPreflightResult> CheckAsync(bool probeLogin, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class FakeStore : ISyncJobStore
    {
        public bool LockHeld { get; set; }

        public SyncJobRecord? Running { get; set; }

        public List<NewSyncJob> Started { get; } = new();

        public List<(long Id, SyncJobFinish Finish, IReadOnlyList<SyncLogEntry> Logs)> Finished { get; } = new();

        public List<SyncJobRecord> Skipped { get; } = new();

        public FakeLock? Lock { get; private set; }

        public long NextId { get; set; } = 7;

        public Task<SyncStartOutcome> StartAsync(NewSyncJob job, CancellationToken cancellationToken)
        {
            if (LockHeld)
            {
                return Task.FromResult(SyncStartOutcome.AlreadyRunning);
            }

            Started.Add(job);
            Lock = new FakeLock();
            return Task.FromResult(new SyncStartOutcome(true, NextId, Lock));
        }

        public Task<SyncJobRecord> RecordSkippedAsync(NewSyncJob job, string skipReason, CancellationToken cancellationToken)
        {
            var record = new SyncJobRecord(99, job.Trigger, job.Mode, SyncVocabulary.StatusSkipped, job.RequestedBy,
                job.StartedAt, job.StartedAt, 0, null, skipReason, []);
            Skipped.Add(record);
            return Task.FromResult(record);
        }

        public Task FinishAsync(long jobId, SyncJobFinish finish, IReadOnlyList<SyncLogEntry> logs, CancellationToken cancellationToken)
        {
            Finished.Add((jobId, finish, logs));
            return Task.CompletedTask;
        }

        public Task<SyncJobRecord?> GetRunningAsync(CancellationToken cancellationToken) => Task.FromResult(Running);

        public Task<IReadOnlyList<SyncJobRecord>> ListAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SyncJobRecord>>(Array.Empty<SyncJobRecord>());

        public Task<SyncJobRecord?> GetAsync(long id, CancellationToken cancellationToken) => Task.FromResult<SyncJobRecord?>(null);

        public Task<bool> AnyTableNeedsFullLoadAsync(CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<int> RecoverInterruptedAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class FakeWorker(Func<SyncRunRequest, Task<SyncRunResult>> run) : ISyncRunWorker
    {
        public List<SyncRunRequest> Requests { get; } = new();

        public Task<SyncRunResult> RunAsync(SyncRunRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return run(request);
        }
    }

    private static SyncPreflightResult Ready()
    {
        var map = SourceMap.LoadFromFile(ExampleMapPath).Map!;
        return new SyncPreflightResult(true, true, [], null, null, map);
    }

    private static SyncRuntimeSettings Runtime(bool production = false, bool demo = true, string environment = "Development") =>
        new(production, demo, environment, Tehran);

    private static SyncCoordinator Coordinator(
        SyncPreflightResult preflight,
        FakeStore store,
        FakeWorker worker,
        SyncRuntimeSettings? runtime = null,
        int? faultAfter = null)
    {
        var options = new SyncOptions
        {
            PageSize = 50,
            MaxRejectedRows = 0,
            FaultInjection = new SyncFaultInjectionOptions { FailAfterTables = faultAfter },
        };
        return new SyncCoordinator(
            new FakePreflight(preflight),
            store,
            worker,
            Options.Create(options),
            runtime ?? Runtime(),
            TimeProvider.System,
            NullLogger<SyncCoordinator>.Instance);
    }

    private static SyncJobRecord RunningJob(long id) =>
        new(id, SyncTrigger.Manual, SyncRunMode.Incremental, SyncVocabulary.StatusRunning, "admin",
            DateTimeOffset.UtcNow, null, null, null, null, []);

    [Fact]
    public async Task Manual_request_while_a_run_is_active_conflicts_and_starts_nothing()
    {
        var store = new FakeStore { LockHeld = true, Running = RunningJob(41) };
        var worker = new FakeWorker(_ => throw new InvalidOperationException("must not run"));
        var coordinator = Coordinator(Ready(), store, worker);

        var outcome = await coordinator.RequestRunAsync(SyncTrigger.Manual, "admin", CancellationToken.None);

        var conflict = Assert.IsType<SyncConflict>(outcome);
        Assert.Equal(41, conflict.RunningJob!.Id);
        Assert.Empty(store.Started);
        Assert.Empty(store.Skipped);
        Assert.Empty(worker.Requests);
    }

    [Fact]
    public async Task Scheduled_request_while_a_run_is_active_is_recorded_as_skipped_with_a_reason()
    {
        var store = new FakeStore { LockHeld = true, Running = RunningJob(41) };
        var worker = new FakeWorker(_ => throw new InvalidOperationException("must not run"));
        var coordinator = Coordinator(Ready(), store, worker);

        var outcome = await coordinator.RequestRunAsync(SyncTrigger.Scheduled, null, CancellationToken.None);

        var skipped = Assert.IsType<SyncSkipped>(outcome);
        Assert.Equal(SyncVocabulary.StatusSkipped, skipped.Job.Status);
        Assert.Equal("a run was already in progress", skipped.Job.SkipReason);
        Assert.Single(store.Skipped);
        Assert.Empty(worker.Requests);
    }

    [Fact]
    public async Task Refused_preflight_starts_nothing_and_returns_the_contract_reason()
    {
        var refused = new SyncPreflightResult(true, false, ["map problem"], "source-map-invalid", "invalid", null);
        var store = new FakeStore();
        var worker = new FakeWorker(_ => throw new InvalidOperationException("must not run"));
        var coordinator = Coordinator(refused, store, worker);

        var outcome = await coordinator.RequestRunAsync(SyncTrigger.Manual, "admin", CancellationToken.None);

        var rejected = Assert.IsType<SyncRejected>(outcome);
        Assert.Equal("source-map-invalid", rejected.Reason);
        Assert.Empty(store.Started);
        Assert.Empty(worker.Requests);
    }

    [Fact]
    public async Task Accepted_run_is_started_in_the_background_and_its_counts_are_stored()
    {
        var store = new FakeStore();
        var worker = new FakeWorker(_ => Task.FromResult(new SyncRunResult(new List<SyncTableCounts>
        {
            new(LogicalTables.Vendor, Read: 3, Inserted: 2, Updated: 1, Deleted: 0, Rejected: 0),
        })));
        var coordinator = Coordinator(Ready(), store, worker);

        var outcome = await coordinator.RequestRunAsync(SyncTrigger.Manual, "admin", CancellationToken.None);
        await coordinator.WaitForActiveRunAsync();

        var started = Assert.IsType<SyncStarted>(outcome);
        Assert.Equal(7, started.Job.Id);
        Assert.Equal(SyncVocabulary.StatusRunning, started.Job.Status);

        var finished = Assert.Single(store.Finished);
        Assert.Equal(7, finished.Id);
        Assert.Equal(SyncVocabulary.StatusSucceeded, finished.Finish.Status);
        Assert.Null(finished.Finish.Error);
        Assert.Contains(finished.Logs, l => l.Table is { Table: LogicalTables.Vendor, Inserted: 2 });
        Assert.True(store.Lock!.Disposed, "the run lock must be released when the run ends");
    }

    [Fact]
    public async Task Failed_run_stores_the_scrubbed_error_and_the_tables_reached()
    {
        var store = new FakeStore();
        var worker = new FakeWorker(_ => throw new SyncRunException(
            "Mabna read failed (SQL error 53). password=hunter2",
            new List<SyncTableCounts> { new(LogicalTables.Vendor, 3, 3, 0, 0, 0) }));
        var coordinator = Coordinator(Ready(), store, worker);

        await coordinator.RequestRunAsync(SyncTrigger.Manual, "admin", CancellationToken.None);
        await coordinator.WaitForActiveRunAsync();

        var finished = Assert.Single(store.Finished);
        Assert.Equal(SyncVocabulary.StatusFailed, finished.Finish.Status);
        Assert.StartsWith("همگام‌سازی ناموفق بود:", finished.Finish.Error);
        Assert.Contains("Mabna read failed", finished.Finish.Error);
        Assert.DoesNotContain("hunter2", finished.Finish.Error);
        Assert.Contains(finished.Logs, l => l.Table is { Table: LogicalTables.Vendor });
        Assert.Contains(finished.Logs, l => l.Level == SyncVocabulary.LevelError);
        Assert.True(store.Lock!.Disposed);
    }

    [Fact]
    public async Task Unexpected_exception_is_recorded_without_its_message()
    {
        var store = new FakeStore();
        var worker = new FakeWorker(_ => throw new InvalidOperationException("Server=10.0.0.5;User=zharfa_ro;Password=x"));
        var coordinator = Coordinator(Ready(), store, worker);

        await coordinator.RequestRunAsync(SyncTrigger.Manual, "admin", CancellationToken.None);
        await coordinator.WaitForActiveRunAsync();

        var finished = Assert.Single(store.Finished);
        Assert.Equal(SyncVocabulary.StatusFailed, finished.Finish.Status);
        Assert.DoesNotContain("10.0.0.5", finished.Finish.Error);
        Assert.DoesNotContain("zharfa_ro", finished.Finish.Error);
    }

    [Fact]
    public async Task Fault_injection_reaches_the_runner_only_in_development_demo()
    {
        var store = new FakeStore();
        var worker = new FakeWorker(_ => Task.FromResult(new SyncRunResult(Array.Empty<SyncTableCounts>())));
        var coordinator = Coordinator(Ready(), store, worker, Runtime(demo: true, environment: "Development"), faultAfter: 2);
        await coordinator.RequestRunAsync(SyncTrigger.Manual, "admin", CancellationToken.None);
        await coordinator.WaitForActiveRunAsync();
        Assert.Equal(2, worker.Requests.Single().FailAfterTables);

        var productionStore = new FakeStore();
        var productionWorker = new FakeWorker(_ => Task.FromResult(new SyncRunResult(Array.Empty<SyncTableCounts>())));
        var production = Coordinator(Ready(), productionStore, productionWorker, Runtime(production: true, demo: false, environment: "Production"), faultAfter: 2);
        await production.RequestRunAsync(SyncTrigger.Manual, "admin", CancellationToken.None);
        await production.WaitForActiveRunAsync();
        Assert.Null(productionWorker.Requests.Single().FailAfterTables);
    }

    [Fact]
    public void Failure_text_never_carries_connector_or_database_details()
    {
        var mabnaDenied = SyncRunner.SafeErrorMessage(new MabnaAccessDeniedException("Mabna object 'dbo.SimVendor' is not on the allowlist."));
        Assert.DoesNotContain("SimVendor", mabnaDenied);

        var pg = SyncRunner.SafeErrorMessage(new Npgsql.NpgsqlException("Failed to connect to 10.0.0.5:5432"));
        Assert.DoesNotContain("10.0.0.5", pg);
    }
}
