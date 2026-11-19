namespace Zharfa.Api.Mabna.Sync;

using System;
using System.Collections.Generic;

/// <summary>Who started a sync job (contracts/api.md §4).</summary>
public enum SyncTrigger
{
    Manual,
    Scheduled,
    StartupRecovery,
}

/// <summary>incremental = change-marker reads where possible; full = complete reload with key diff.</summary>
public enum SyncRunMode
{
    Incremental,
    Full,
}

/// <summary>Per-table change strategy from the source map (data-model.md §2).</summary>
public enum ChangeStrategy
{
    Rowversion,
    ModifiedAt,
    IdWatermark,
    Full,
}

/// <summary>Stable text values stored in sync_job, sync_log and source_record.</summary>
public static class SyncVocabulary
{
    public const string JobName = "purchasing";
    public const string SourceSystem = "mabna";

    public const string LevelInfo = "info";
    public const string LevelWarning = "warning";
    public const string LevelError = "error";

    public const string StatusRunning = "running";
    public const string StatusSucceeded = "succeeded";
    public const string StatusFailed = "failed";
    public const string StatusSkipped = "skipped";

    public static string TriggerText(SyncTrigger trigger) => trigger switch
    {
        SyncTrigger.Manual => "manual",
        SyncTrigger.Scheduled => "scheduled",
        SyncTrigger.StartupRecovery => "startup-recovery",
        _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
    };

    public static SyncTrigger ParseTrigger(string text) => text switch
    {
        "manual" => SyncTrigger.Manual,
        "scheduled" => SyncTrigger.Scheduled,
        "startup-recovery" => SyncTrigger.StartupRecovery,
        _ => throw new ArgumentOutOfRangeException(nameof(text), "unknown sync trigger"),
    };

    public static string ModeText(SyncRunMode mode) => mode == SyncRunMode.Full ? "full" : "incremental";

    public static SyncRunMode ParseMode(string text) =>
        string.Equals(text, "full", StringComparison.Ordinal) ? SyncRunMode.Full : SyncRunMode.Incremental;

    public static string StrategyText(ChangeStrategy strategy) => strategy switch
    {
        ChangeStrategy.Rowversion => "rowversion",
        ChangeStrategy.ModifiedAt => "modifiedAt",
        ChangeStrategy.IdWatermark => "idWatermark",
        ChangeStrategy.Full => "full",
        _ => throw new ArgumentOutOfRangeException(nameof(strategy)),
    };

    public static bool TryParseStrategy(string? text, out ChangeStrategy strategy)
    {
        switch (text)
        {
            case "rowversion":
                strategy = ChangeStrategy.Rowversion;
                return true;
            case "modifiedAt":
                strategy = ChangeStrategy.ModifiedAt;
                return true;
            case "idWatermark":
                strategy = ChangeStrategy.IdWatermark;
                return true;
            case "full":
                strategy = ChangeStrategy.Full;
                return true;
            default:
                strategy = ChangeStrategy.Full;
                return false;
        }
    }
}

/// <summary>Logical sync tables in run order (vendor → product → invoice → return → lines).</summary>
public static class LogicalTables
{
    public const string Vendor = "vendor";
    public const string Product = "product";
    public const string Invoice = "purchase_invoice";
    public const string Return = "purchase_return";
    public const string Line = "purchase_line";

    /// <summary>Phase 2, optional drug group master. Not in <see cref="SyncOrder"/>: installs without it stay complete.</summary>
    public const string ProductCategory = "product_category";

    // Phase 6, optional vendor-financial tables. Also not in <see cref="SyncOrder"/>: a purchasing-only install
    // (no Phase 6 entities in the map) stays complete, and a mapped financial entity that fails still fails the run.
    public const string PayableDocument = "payable_document";
    public const string PayableCheque = "payable_cheque";
    public const string VendorPayment = "vendor_payment";
    public const string PaymentAllocation = "payment_allocation";

    public static readonly IReadOnlyList<string> SyncOrder = [Vendor, Product, Invoice, Return, Line];
}

/// <summary>
/// One source row keyed by column name (case-insensitive). Values are exactly as the
/// connector returned them; DBNull has already been converted to null.
/// </summary>
public sealed class SourceRow
{
    private readonly Dictionary<string, object?> _values;

    private SourceRow(Dictionary<string, object?> values)
    {
        _values = values;
    }

    public static SourceRow Create(IReadOnlyList<string> columns, IReadOnlyList<object?> values)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(values);

        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var count = Math.Min(columns.Count, values.Count);
        for (var i = 0; i < count; i++)
        {
            map[columns[i]] = values[i];
        }

        return new SourceRow(map);
    }

    public object? Get(string column) => _values.TryGetValue(column, out var value) ? value : null;
}

/// <summary>
/// Per-table counts of one run (sync_log rows). <see cref="Detail"/> summarises rejected
/// reasons as codes and counts only; it never carries source values.
/// </summary>
public sealed record SyncTableCounts(
    string Table,
    int Read,
    int Inserted,
    int Updated,
    int Deleted,
    int Rejected,
    int Warnings = 0,
    string? Detail = null);

/// <summary>Everything the runner needs for one run. Built by the coordinator.</summary>
public sealed record SyncRunRequest(
    long JobId,
    SyncTrigger Trigger,
    SyncRunMode Mode,
    DateTimeOffset StartedAt,
    SourceMap Map,
    bool IsProduction,
    TimeZoneInfo Zone,
    int PageSize,
    int MaxRejectedRows,
    int? FailAfterTables);

public sealed record SyncRunResult(IReadOnlyList<SyncTableCounts> Tables);

/// <summary>
/// A run failed. The data transaction was rolled back; <see cref="CompletedTables"/> holds the
/// counts of the tables that finished before the failure, so the job still shows them.
/// The message is safe to store and show to an Admin (no hosts, logins or real object names).
/// </summary>
public sealed class SyncRunException(string message, IReadOnlyList<SyncTableCounts> completedTables, Exception? inner = null)
    : Exception(message, inner)
{
    public IReadOnlyList<SyncTableCounts> CompletedTables { get; } = completedTables;
}

/// <summary>Raised by the dev-only fault injection (Sync__FaultInjection__FailAfterTables).</summary>
public sealed class SyncFaultInjectedException(string message) : Exception(message);

/// <summary>A table had more rejected rows than Sync__MaxRejectedRows allows.</summary>
public sealed class SyncRowsRejectedException(string message) : Exception(message);

/// <summary>Executes one sync run inside a single PostgreSQL transaction.</summary>
public interface ISyncRunWorker
{
    Task<SyncRunResult> RunAsync(SyncRunRequest request, CancellationToken cancellationToken);
}
