namespace Zharfa.Api.Mabna.Sync;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

public sealed record NewSyncJob(SyncTrigger Trigger, SyncRunMode Mode, string? RequestedBy, DateTimeOffset StartedAt);

public sealed record SyncJobRecord(
    long Id,
    SyncTrigger Trigger,
    SyncRunMode Mode,
    string Status,
    string? RequestedBy,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    long? DurationMs,
    string? Error,
    string? SkipReason,
    IReadOnlyList<SyncTableCounts> Tables);

/// <summary>One sync_log row: a table summary (with counts) or a job-level message.</summary>
public sealed record SyncLogEntry(string Level, string Message, SyncTableCounts? Table = null);

public sealed record SyncJobFinish(string Status, DateTimeOffset FinishedAt, long DurationMs, string? Error);

/// <summary>
/// Outcome of <see cref="ISyncJobStore.StartAsync"/>. When started, <see cref="RunLock"/> holds the
/// PostgreSQL session advisory lock; disposing it releases the lock and its connection.
/// </summary>
public sealed record SyncStartOutcome(bool Started, long JobId, IAsyncDisposable? RunLock)
{
    public static SyncStartOutcome AlreadyRunning { get; } = new(false, 0, null);
}

/// <summary>Bookkeeping of sync jobs and logs. Job and log writes never share the data transaction.</summary>
public interface ISyncJobStore
{
    /// <summary>Takes the run lock and inserts a running job. AlreadyRunning when another run holds the lock or the index refuses a second running row.</summary>
    Task<SyncStartOutcome> StartAsync(NewSyncJob job, CancellationToken cancellationToken);

    /// <summary>Records a scheduled run that overlapped a running one (status skipped, no lock).</summary>
    Task<SyncJobRecord> RecordSkippedAsync(NewSyncJob job, string skipReason, CancellationToken cancellationToken);

    Task FinishAsync(long jobId, SyncJobFinish finish, IReadOnlyList<SyncLogEntry> logs, CancellationToken cancellationToken);

    Task<SyncJobRecord?> GetRunningAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SyncJobRecord>> ListAsync(int limit, CancellationToken cancellationToken);

    Task<SyncJobRecord?> GetAsync(long id, CancellationToken cancellationToken);

    /// <summary>True when any logical table has never had a full load (first run promotes to full).</summary>
    Task<bool> AnyTableNeedsFullLoadAsync(CancellationToken cancellationToken);

    /// <summary>Marks jobs left 'running' by a previous process as failed. Does nothing while a run holds the lock.</summary>
    Task<int> RecoverInterruptedAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>
/// PostgreSQL implementation. A session advisory lock on its own connection serialises runs
/// (held for the whole run), and the partial unique index <c>uq_sync_job_running</c> refuses a
/// second running row even if the lock were bypassed.
/// </summary>
public sealed class PostgresSyncJobStore(NpgsqlDataSource dataSource) : ISyncJobStore
{
    /// <summary>Session advisory lock key for purchasing sync runs.</summary>
    public const long RunLockKey = 7_412_090_001L;

    public async Task<SyncStartOutcome> StartAsync(NewSyncJob job, CancellationToken cancellationToken)
    {
        // The lock lives on this connection's session. Npgsql pooling keeps the physical
        // session alive after Dispose, so the lock must be released explicitly on every path
        // that does not hand it to a SessionRunLock.
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var lockAcquired = false;
        var handedOff = false;
        try
        {
            if (!await TryLockAsync(connection, cancellationToken))
            {
                return SyncStartOutcome.AlreadyRunning;
            }

            lockAcquired = true;
            await using var insert = new NpgsqlCommand(
                "INSERT INTO sync_job (job_name, trigger, mode, status, requested_by, started_at) " +
                "VALUES (@job, @trigger, @mode, 'running', @requested_by, @started_at) RETURNING id",
                connection);
            insert.Parameters.AddWithValue("job", SyncVocabulary.JobName);
            insert.Parameters.AddWithValue("trigger", SyncVocabulary.TriggerText(job.Trigger));
            insert.Parameters.AddWithValue("mode", SyncVocabulary.ModeText(job.Mode));
            insert.Parameters.AddWithValue("requested_by", (object?)job.RequestedBy ?? DBNull.Value);
            insert.Parameters.AddWithValue("started_at", job.StartedAt);
            var id = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));

            handedOff = true;
            return new SyncStartOutcome(true, id, new SessionRunLock(connection));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return SyncStartOutcome.AlreadyRunning;
        }
        finally
        {
            if (!handedOff)
            {
                if (lockAcquired)
                {
                    await UnlockQuietlyAsync(connection);
                }

                await connection.DisposeAsync();
            }
        }
    }

    private static async Task UnlockQuietlyAsync(NpgsqlConnection connection)
    {
        try
        {
            await UnlockAsync(connection, CancellationToken.None);
        }
        catch (NpgsqlException)
        {
            // The connection is disposed next; closing the session releases the lock.
        }
    }

    public async Task<SyncJobRecord> RecordSkippedAsync(NewSyncJob job, string skipReason, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var insert = new NpgsqlCommand(
            "INSERT INTO sync_job (job_name, trigger, mode, status, requested_by, started_at, finished_at, duration_ms, skip_reason) " +
            "VALUES (@job, @trigger, @mode, 'skipped', @requested_by, @started_at, @started_at, 0, @skip_reason) RETURNING id",
            connection);
        insert.Parameters.AddWithValue("job", SyncVocabulary.JobName);
        insert.Parameters.AddWithValue("trigger", SyncVocabulary.TriggerText(job.Trigger));
        insert.Parameters.AddWithValue("mode", SyncVocabulary.ModeText(job.Mode));
        insert.Parameters.AddWithValue("requested_by", (object?)job.RequestedBy ?? DBNull.Value);
        insert.Parameters.AddWithValue("started_at", job.StartedAt);
        insert.Parameters.AddWithValue("skip_reason", skipReason);
        var id = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
        return new SyncJobRecord(id, job.Trigger, job.Mode, SyncVocabulary.StatusSkipped, job.RequestedBy,
            job.StartedAt, job.StartedAt, 0, null, skipReason, []);
    }

    public async Task FinishAsync(long jobId, SyncJobFinish finish, IReadOnlyList<SyncLogEntry> logs, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        await using (var update = new NpgsqlCommand(
                         "UPDATE sync_job SET status = @status, finished_at = @finished_at, duration_ms = @duration_ms, " +
                         "error_message = @error WHERE id = @id",
                         connection, tx))
        {
            update.Parameters.AddWithValue("status", finish.Status);
            update.Parameters.AddWithValue("finished_at", finish.FinishedAt);
            update.Parameters.AddWithValue("duration_ms", finish.DurationMs);
            update.Parameters.AddWithValue("error", (object?)finish.Error ?? DBNull.Value);
            update.Parameters.AddWithValue("id", jobId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var entry in logs)
        {
            await InsertLogAsync(connection, tx, jobId, entry, finish.FinishedAt, cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    public async Task<SyncJobRecord?> GetRunningAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var jobs = await QueryJobsAsync(connection,
            "SELECT id, trigger, mode, status, requested_by, started_at, finished_at, duration_ms, error_message, skip_reason " +
            "FROM sync_job WHERE job_name = @job AND status = 'running' ORDER BY id DESC LIMIT 1",
            cmd => cmd.Parameters.AddWithValue("job", SyncVocabulary.JobName),
            cancellationToken);
        return jobs.FirstOrDefault();
    }

    public async Task<IReadOnlyList<SyncJobRecord>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var jobs = await QueryJobsAsync(connection,
            "SELECT id, trigger, mode, status, requested_by, started_at, finished_at, duration_ms, error_message, skip_reason " +
            "FROM sync_job WHERE job_name = @job ORDER BY id DESC LIMIT @limit",
            cmd =>
            {
                cmd.Parameters.AddWithValue("job", SyncVocabulary.JobName);
                cmd.Parameters.AddWithValue("limit", (long)limit);
            },
            cancellationToken);

        return await WithTablesAsync(connection, jobs, cancellationToken);
    }

    public async Task<SyncJobRecord?> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var jobs = await QueryJobsAsync(connection,
            "SELECT id, trigger, mode, status, requested_by, started_at, finished_at, duration_ms, error_message, skip_reason " +
            "FROM sync_job WHERE job_name = @job AND id = @id",
            cmd =>
            {
                cmd.Parameters.AddWithValue("job", SyncVocabulary.JobName);
                cmd.Parameters.AddWithValue("id", id);
            },
            cancellationToken);

        var withTables = await WithTablesAsync(connection, jobs, cancellationToken);
        return withTables.FirstOrDefault();
    }

    public async Task<bool> AnyTableNeedsFullLoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var count = new NpgsqlCommand(
            "SELECT count(*) FROM source_record WHERE source_system = @system AND last_full_load_at IS NOT NULL",
            connection);
        count.Parameters.AddWithValue("system", SyncVocabulary.SourceSystem);
        var loaded = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken));
        return loaded < LogicalTables.SyncOrder.Count;
    }

    public async Task<int> RecoverInterruptedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (!await TryLockAsync(connection, cancellationToken))
        {
            return 0; // a live run owns the lock: its rows are not interrupted
        }

        try
        {
            const string message = "اجرا با راه‌اندازی مجدد سرویس قطع شد. Interrupted by a service restart.";
            await using var tx = await connection.BeginTransactionAsync(cancellationToken);
            var ids = new List<long>();
            await using (var update = new NpgsqlCommand(
                             "UPDATE sync_job SET status = 'failed', finished_at = @now, error_message = @message " +
                             "WHERE job_name = @job AND status = 'running' RETURNING id",
                             connection, tx))
            {
                update.Parameters.AddWithValue("now", now);
                update.Parameters.AddWithValue("message", message);
                update.Parameters.AddWithValue("job", SyncVocabulary.JobName);
                await using var reader = await update.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    ids.Add(reader.GetInt64(0));
                }
            }

            foreach (var id in ids)
            {
                await InsertLogAsync(connection, tx, id,
                    new SyncLogEntry(SyncVocabulary.LevelWarning, message), now, cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            return ids.Count;
        }
        finally
        {
            await UnlockAsync(connection, CancellationToken.None);
        }
    }

    private static async Task<bool> TryLockAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
        cmd.Parameters.AddWithValue("key", RunLockKey);
        return (bool)(await cmd.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task UnlockAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
        cmd.Parameters.AddWithValue("key", RunLockKey);
        await cmd.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task InsertLogAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        long jobId,
        SyncLogEntry entry,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO sync_log (sync_job_id, level, message, logged_at, table_logical, rows_read, rows_inserted, " +
            "rows_updated, rows_deleted, rows_rejected) VALUES (@job, @level, @message, @at, @table, @read, @ins, @upd, @del, @rej)",
            connection, tx);
        cmd.Parameters.AddWithValue("job", jobId);
        cmd.Parameters.AddWithValue("level", entry.Level);
        cmd.Parameters.AddWithValue("message", entry.Message);
        cmd.Parameters.AddWithValue("at", at);
        cmd.Parameters.AddWithValue("table", (object?)entry.Table?.Table ?? DBNull.Value);
        cmd.Parameters.AddWithValue("read", (object?)entry.Table?.Read ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ins", (object?)entry.Table?.Inserted ?? DBNull.Value);
        cmd.Parameters.AddWithValue("upd", (object?)entry.Table?.Updated ?? DBNull.Value);
        cmd.Parameters.AddWithValue("del", (object?)entry.Table?.Deleted ?? DBNull.Value);
        cmd.Parameters.AddWithValue("rej", (object?)entry.Table?.Rejected ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<List<SyncJobRecord>> QueryJobsAsync(
        NpgsqlConnection connection,
        string sql,
        Action<NpgsqlCommand> bind,
        CancellationToken cancellationToken)
    {
        var jobs = new List<SyncJobRecord>();
        await using var cmd = new NpgsqlCommand(sql, connection);
        bind(cmd);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(new SyncJobRecord(
                reader.GetInt64(0),
                SyncVocabulary.ParseTrigger(reader.GetString(1)),
                SyncVocabulary.ParseMode(reader.GetString(2)),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                []));
        }

        return jobs;
    }

    private static async Task<IReadOnlyList<SyncJobRecord>> WithTablesAsync(
        NpgsqlConnection connection,
        IReadOnlyList<SyncJobRecord> jobs,
        CancellationToken cancellationToken)
    {
        if (jobs.Count == 0)
        {
            return jobs;
        }

        var tables = new Dictionary<long, List<SyncTableCounts>>();
        await using var cmd = new NpgsqlCommand(
            "SELECT sync_job_id, table_logical, rows_read, rows_inserted, rows_updated, rows_deleted, rows_rejected " +
            "FROM sync_log WHERE sync_job_id = ANY(@ids) AND table_logical IS NOT NULL ORDER BY id",
            connection);
        cmd.Parameters.AddWithValue("ids", jobs.Select(j => j.Id).ToArray());
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var jobId = reader.GetInt64(0);
            if (!tables.TryGetValue(jobId, out var list))
            {
                list = [];
                tables[jobId] = list;
            }

            list.Add(new SyncTableCounts(
                reader.GetString(1),
                ReadInt(reader, 2),
                ReadInt(reader, 3),
                ReadInt(reader, 4),
                ReadInt(reader, 5),
                ReadInt(reader, 6)));
        }

        return jobs
            .Select(j => j with { Tables = tables.TryGetValue(j.Id, out var t) ? t : [] })
            .ToList();
    }

    private static int ReadInt(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);

    /// <summary>Holds the session advisory lock on its connection until disposed.</summary>
    private sealed class SessionRunLock(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await UnlockQuietlyAsync(connection);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
