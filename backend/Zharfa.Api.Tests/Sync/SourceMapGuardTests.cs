namespace Zharfa.Api.Tests.Sync;

using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zharfa.Api.Mabna;
using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.Platform;

/// <summary>
/// T045 (spec 003, US3): a source map that names an object outside the connector allowlist, a
/// hard-excluded object, or a view without <c>hostFiltered: false</c> makes the map invalid, and the
/// coordinator's pre-run check refuses the run with <c>source-map-invalid</c> before any read. The
/// real <see cref="SyncPreflight"/> runs against temporary map files; the connection factory counts
/// opens and never connects. No database, no network.
/// </summary>
public class SourceMapGuardTests
{
    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    private static readonly string ExampleMapPath =
        Path.Combine(AppContext.BaseDirectory, "example", "purchase-source-map.example.json");

    private static readonly string ExampleAllowlistPath =
        Path.Combine(AppContext.BaseDirectory, "example", "purchase-allowlist.example.txt");

    // ---------------------------------------------------------------- refusals

    [Fact]
    public async Task Mapped_object_missing_from_the_allowlist_is_refused_before_any_read()
    {
        var json = ExampleJson();
        Entity(json, "purchaseInvoice")["object"] = "dbo.SimNotOnAllowlist";

        await AssertRefusedBeforeAnyRead(json);
    }

    [Theory]
    [InlineData("dbo.scuUser")]
    [InlineData("dbo.UserRights")]
    [InlineData("dbo.msgInbox")]
    public async Task Hard_excluded_mapped_object_is_refused_before_any_read(string excluded)
    {
        var json = ExampleJson();
        Entity(json, "vendor")["object"] = excluded;

        await AssertRefusedBeforeAnyRead(json);
    }

    [Fact]
    public async Task View_with_host_filtered_true_is_refused_before_any_read()
    {
        var json = ExampleJson();
        var invoice = Entity(json, "purchaseInvoice");
        invoice["kind"] = "view";
        invoice["hostFiltered"] = true;

        await AssertRefusedBeforeAnyRead(json);
    }

    [Fact]
    public async Task View_without_host_filtered_is_refused_before_any_read()
    {
        var json = ExampleJson();
        var invoice = Entity(json, "purchaseInvoice");
        invoice["kind"] = "view";
        invoice.Remove("hostFiltered");

        await AssertRefusedBeforeAnyRead(json);
    }

    // ---------------------------------------------------------------- control

    [Fact]
    public async Task Unchanged_example_map_with_its_allowlist_is_accepted_and_the_run_starts()
    {
        // Control: the refusals above come from the map, not from the test wiring.
        using var map = new TempMap(ExampleJson());
        var factory = new CountingConnectionFactory();
        var (coordinator, store, worker) = Build(map.FilePath, factory);

        var outcome = await coordinator.RequestRunAsync(SyncTrigger.Manual, "admin", CancellationToken.None);
        await coordinator.WaitForActiveRunAsync();

        Assert.IsType<SyncStarted>(outcome);
        Assert.Single(store.Started);
        Assert.Single(worker.Requests);
        Assert.Equal(0, factory.Opens);
    }

    // ---------------------------------------------------------------- helpers

    private static async Task AssertRefusedBeforeAnyRead(JsonObject json)
    {
        using var map = new TempMap(json);
        var factory = new CountingConnectionFactory();
        var (coordinator, store, worker) = Build(map.FilePath, factory);

        var outcome = await coordinator.RequestRunAsync(SyncTrigger.Manual, "admin", CancellationToken.None);

        var rejected = Assert.IsType<SyncRejected>(outcome);
        Assert.Equal("source-map-invalid", rejected.Reason);
        Assert.Empty(store.Started);
        Assert.Empty(worker.Requests); // the runner is the only caller of IMabnaReader; it never starts
        Assert.Equal(0, factory.Opens); // no connection to the source was opened
    }

    private static JsonObject ExampleJson() =>
        JsonNode.Parse(File.ReadAllText(ExampleMapPath))!.AsObject();

    private static JsonObject Entity(JsonObject json, string name) =>
        json["entities"]!.AsObject()[name]!.AsObject();

    private static (SyncCoordinator Coordinator, RecordingStore Store, RecordingWorker Worker) Build(
        string mapPath,
        CountingConnectionFactory factory)
    {
        var sourceOptions = new MabnaSourceOptions
        {
            ConnectionString = "Server=unused.invalid;Database=unused;Integrated Security=true",
            VerifyReadOnlyLogin = false,
            AllowlistFile = ExampleAllowlistPath,
        };
        var policy = new MabnaObjectPolicy(MabnaObjectPolicy.LoadEntries(sourceOptions));
        var gate = new MabnaReadOnlyGate(factory, policy, Options.Create(sourceOptions), NullLogger<MabnaReadOnlyGate>.Instance);
        var preflight = new SyncPreflight(
            Options.Create(sourceOptions),
            Options.Create(new MabnaSyncOptions { SourceMapFile = mapPath }),
            policy,
            gate,
            new DataModeOptions(DataModeOptions.Demo));
        var store = new RecordingStore();
        var worker = new RecordingWorker();
        var coordinator = new SyncCoordinator(
            preflight,
            store,
            worker,
            Options.Create(new SyncOptions { PageSize = 50 }),
            new SyncRuntimeSettings(IsProduction: false, IsDemo: true, EnvironmentName: "Development", Tehran),
            TimeProvider.System,
            NullLogger<SyncCoordinator>.Instance);
        return (coordinator, store, worker);
    }

    /// <summary>Writes a map to a temporary file and deletes it afterwards.</summary>
    private sealed class TempMap : IDisposable
    {
        public TempMap(JsonObject json)
        {
            FilePath = Path.Combine(Path.GetTempPath(), $"zharfa-map-{Guid.NewGuid():N}.json");
            File.WriteAllText(FilePath, json.ToJsonString());
        }

        public string FilePath { get; }

        public void Dispose()
        {
            try
            {
                File.Delete(FilePath);
            }
            catch (IOException)
            {
                // A leftover temp file holds no secrets and no source data.
            }
        }
    }

    /// <summary>Counts connection attempts. Creating a connection is the only way the connector reaches the source.</summary>
    private sealed class CountingConnectionFactory : MabnaConnectionFactory
    {
        public CountingConnectionFactory()
            : base(Options.Create(new MabnaSourceOptions { ConnectionString = "Server=unused.invalid;Database=unused;Integrated Security=true" }))
        {
        }

        public int Opens { get; private set; }

        public override Microsoft.Data.SqlClient.SqlConnection Create()
        {
            Opens++;
            throw new InvalidOperationException("unit test: the source is never contacted");
        }
    }

    private sealed class NoopLock : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingStore : ISyncJobStore
    {
        public List<NewSyncJob> Started { get; } = new();

        public Task<SyncStartOutcome> StartAsync(NewSyncJob job, CancellationToken cancellationToken)
        {
            Started.Add(job);
            return Task.FromResult(new SyncStartOutcome(true, 7, new NoopLock()));
        }

        public Task<SyncJobRecord> RecordSkippedAsync(NewSyncJob job, string skipReason, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("not used in these tests");

        public Task FinishAsync(long jobId, SyncJobFinish finish, IReadOnlyList<SyncLogEntry> logs, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<SyncJobRecord?> GetRunningAsync(CancellationToken cancellationToken) => Task.FromResult<SyncJobRecord?>(null);

        public Task<IReadOnlyList<SyncJobRecord>> ListAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SyncJobRecord>>(Array.Empty<SyncJobRecord>());

        public Task<SyncJobRecord?> GetAsync(long id, CancellationToken cancellationToken) => Task.FromResult<SyncJobRecord?>(null);

        public Task<bool> AnyTableNeedsFullLoadAsync(CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<int> RecoverInterruptedAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class RecordingWorker : ISyncRunWorker
    {
        public List<SyncRunRequest> Requests { get; } = new();

        public Task<SyncRunResult> RunAsync(SyncRunRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new SyncRunResult(Array.Empty<SyncTableCounts>()));
        }
    }
}
