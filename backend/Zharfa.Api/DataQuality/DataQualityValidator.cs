namespace Zharfa.Api.DataQuality;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Npgsql;
using Zharfa.Api.Analytics;
using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.Platform;

/// <summary>Runs the data-quality check after a succeeded sync (research R11, R12). Never changes a sync job's status.</summary>
public interface IDataQualityValidator
{
    Task RunAsync(long syncJobId, DateTimeOffset syncFinishedUtc, CancellationToken cancellationToken);
}

/// <summary>
/// Validates the synced copy in one transaction: a new <c>data_quality_run</c>, every rule of
/// <see cref="DataQualitySql.Rules"/>, the run marked succeeded, and the issues of older runs deleted. A failure rolls
/// back and records a failed run with a fixed code; the log holds the exception type only, never a value.
/// </summary>
public sealed class DataQualityValidator(
    NpgsqlDataSource db,
    DataQualityOptions options,
    ISyncPreflight preflight,
    SyncRuntimeSettings runtime,
    ILogger<DataQualityValidator> logger) : IDataQualityValidator
{
    private const int RuleTimeoutSeconds = 120;
    private const string ValidationFailedCode = "validation-failed";

    public async Task RunAsync(long syncJobId, DateTimeOffset syncFinishedUtc, CancellationToken cancellationToken)
    {
        try
        {
            await ValidateAsync(syncJobId, syncFinishedUtc, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Data-quality validation for sync job {JobId} failed [{ExceptionType}].", syncJobId, ex.GetType().Name);
            await RecordFailedRunAsync(syncJobId);
        }
    }

    private async Task ValidateAsync(long syncJobId, DateTimeOffset syncFinishedUtc, CancellationToken cancellationToken)
    {
        var check = await preflight.CheckAsync(probeLogin: false, cancellationToken);
        if (check.Refused || check.Map is null)
        {
            throw new InvalidOperationException("The source map is not usable for the data-quality check.");
        }

        var mapping = new PurchaseMapping(check.Map, runtime.IsProduction);
        var values = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["todayKey"] = TodayKey(syncFinishedUtc),
            ["earliestKey"] = options.EarliestKey,
            ["numberMapped"] = mapping.Invoice.Number is not null && mapping.Return.Number is not null,
            ["vendorMapped"] = mapping.Invoice.VendorKey is not null && mapping.Return.VendorKey is not null,
            ["productMapped"] = mapping.Line.ProductKey is not null,
            ["quantityMapped"] = mapping.Line.Quantity is not null,
            ["categoryMapped"] = mapping.ProductCategoryKey is not null && mapping.ProductCategory is not null,
        };

        await using var connection = await db.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        long runId;
        await using (var insertRun = new NpgsqlCommand(DataQualitySql.InsertRun, connection, tx))
        {
            insertRun.Parameters.AddWithValue("syncJobId", syncJobId);
            runId = (long)(await insertRun.ExecuteScalarAsync(cancellationToken))!;
        }

        values["runId"] = runId;
        foreach (var rule in DataQualitySql.Rules)
        {
            await using var command = new NpgsqlCommand(rule.Sql, connection, tx) { CommandTimeout = RuleTimeoutSeconds };
            Bind(command, rule.Sql, values);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var complete = new NpgsqlCommand(DataQualitySql.CompleteRun, connection, tx))
        {
            complete.Parameters.AddWithValue("runId", runId);
            await complete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var prune = new NpgsqlCommand(DataQualitySql.DeleteOlderIssues, connection, tx))
        {
            prune.Parameters.AddWithValue("runId", runId);
            await prune.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    /// <summary>Records a failed run in its own connection (the validation transaction is already rolled back).</summary>
    private async Task RecordFailedRunAsync(long syncJobId)
    {
        try
        {
            await using var connection = await db.OpenConnectionAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand(DataQualitySql.InsertFailedRun, connection);
            command.Parameters.AddWithValue("syncJobId", syncJobId);
            command.Parameters.AddWithValue("errorCode", ValidationFailedCode);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not record the failed data-quality run [{ExceptionType}].", ex.GetType().Name);
        }
    }

    /// <summary>The Jalali day key of the sync's finish time in the sync zone (yyyymmdd).</summary>
    private int TodayKey(DateTimeOffset syncFinishedUtc)
    {
        var local = TimeZoneInfo.ConvertTime(syncFinishedUtc, runtime.Zone);
        var jalali = JalaliDate.FromDateTime(local.DateTime);
        return jalali.Year * 10000 + jalali.Month * 100 + jalali.Day;
    }

    /// <summary>Adds only the parameters that the statement names, so every named placeholder is bound.</summary>
    private static void Bind(NpgsqlCommand command, string sql, IReadOnlyDictionary<string, object> values)
    {
        foreach (var (name, value) in values)
        {
            if (sql.Contains("@" + name, StringComparison.Ordinal))
            {
                command.Parameters.AddWithValue(name, value);
            }
        }
    }
}
