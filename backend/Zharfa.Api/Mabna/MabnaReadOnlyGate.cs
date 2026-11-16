namespace Zharfa.Api.Mabna;

using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

public enum MabnaGateState
{
    /// <summary>Not checked yet, or the last check could not reach the server.</summary>
    NotChecked,
    /// <summary>Check ran: no write/DDL/execute capability found.</summary>
    ReadOnlyConfirmed,
    /// <summary>Check ran: login can write; connector refused (no override).</summary>
    WritableBlocked,
    /// <summary>Check ran: login can write; <c>MabnaSource__AllowWritableLogin=true</c> lets it run.</summary>
    WritableOverridden,
    /// <summary><c>MabnaSource__VerifyReadOnlyLogin=false</c>: check skipped (warned once).</summary>
    CheckDisabled,
}

/// <summary>
/// Safeguard 3: verify, from permission metadata only, that the configured
/// login cannot write. Nothing is ever written to test it (spec SC-02).
///
/// Checks, in the connected database:
///  - server role <c>sysadmin</c>; database roles <c>db_owner</c>,
///    <c>db_datawriter</c>, <c>db_ddladmin</c>;
///  - database-level <c>HAS_PERMS_BY_NAME</c> for INSERT, UPDATE, DELETE,
///    ALTER, CREATE TABLE, EXECUTE (EXECUTE counts because stored procedures
///    can write);
///  - object-level INSERT/UPDATE/DELETE/ALTER on every allowlisted object
///    (catches table-level GRANTs that the database-level check misses).
///
/// If any is granted the connector refuses to read unless the explicit
/// override <c>MabnaSource__AllowWritableLogin=true</c> is set; either way a
/// warning naming only the permission (never the login, server or
/// connection string) is logged. The result is cached for the process
/// lifetime; a connectivity failure is not cached, so the next read retries.
/// </summary>
public sealed class MabnaReadOnlyGate(
    MabnaConnectionFactory connections,
    MabnaObjectPolicy policy,
    IOptions<MabnaSourceOptions> options,
    ILogger<MabnaReadOnlyGate> logger)
{
    // Fixed probe text; contains only catalog functions, no user input.
    private const string DatabaseProbeSql =
        "SELECT " +
        "ISNULL(IS_SRVROLEMEMBER('sysadmin'), 0), " +
        "ISNULL(IS_MEMBER('db_owner'), 0), " +
        "ISNULL(IS_MEMBER('db_datawriter'), 0), " +
        "ISNULL(IS_MEMBER('db_ddladmin'), 0), " +
        "ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'INSERT'), 0), " +
        "ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'UPDATE'), 0), " +
        "ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'DELETE'), 0), " +
        "ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER'), 0), " +
        "ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE'), 0), " +
        "ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'EXECUTE'), 0)";

    private static readonly string[] DatabaseProbeLabels =
    {
        "server role sysadmin", "db_owner", "db_datawriter", "db_ddladmin",
        "INSERT", "UPDATE", "DELETE", "ALTER", "CREATE TABLE", "EXECUTE",
    };

    private const string ObjectProbeSql =
        "SELECT " +
        "ISNULL(HAS_PERMS_BY_NAME(@obj, 'OBJECT', 'INSERT'), 0), " +
        "ISNULL(HAS_PERMS_BY_NAME(@obj, 'OBJECT', 'UPDATE'), 0), " +
        "ISNULL(HAS_PERMS_BY_NAME(@obj, 'OBJECT', 'DELETE'), 0), " +
        "ISNULL(HAS_PERMS_BY_NAME(@obj, 'OBJECT', 'ALTER'), 0)";

    private static readonly string[] ObjectProbeLabels = { "INSERT", "UPDATE", "DELETE", "ALTER" };

    private readonly MabnaSourceOptions _options = options.Value;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public MabnaGateState State { get; private set; } = MabnaGateState.NotChecked;

    /// <summary>Throws <see cref="MabnaSourceUnavailableException"/> unless reads are allowed.</summary>
    public async Task EnsureReadAllowedAsync(CancellationToken cancellationToken)
    {
        if (State is MabnaGateState.ReadOnlyConfirmed or MabnaGateState.WritableOverridden or MabnaGateState.CheckDisabled)
        {
            return;
        }

        if (State == MabnaGateState.WritableBlocked)
        {
            throw new MabnaSourceUnavailableException(
                "Mabna connector is disabled: the configured login can write (see startup warning).");
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (State == MabnaGateState.NotChecked)
            {
                State = await CheckAsync(cancellationToken);
            }
        }
        finally
        {
            _lock.Release();
        }

        if (State == MabnaGateState.WritableBlocked)
        {
            throw new MabnaSourceUnavailableException(
                "Mabna connector is disabled: the configured login can write (see startup warning).");
        }
    }

    private async Task<MabnaGateState> CheckAsync(CancellationToken cancellationToken)
    {
        if (!_options.VerifyReadOnlyLogin)
        {
            logger.LogWarning(
                "Mabna read-only login check is disabled (MabnaSource__VerifyReadOnlyLogin=false). " +
                "Only app-side guards apply; server-side read-only login remains required.");
            return MabnaGateState.CheckDisabled;
        }

        var findings = new List<string>();
        try
        {
            await using var connection = connections.Create();
            await connection.OpenAsync(cancellationToken);

            // Probes also run in an always-rolled-back transaction, like reads.
            await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                await using (var cmd = new SqlCommand(DatabaseProbeSql, connection, tx))
                {
                    cmd.CommandTimeout = _options.EffectiveCommandTimeoutSeconds;
                    await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        for (var i = 0; i < DatabaseProbeLabels.Length; i++)
                        {
                            if (Convert.ToInt32(reader.GetValue(i)) == 1)
                            {
                                findings.Add($"database: {DatabaseProbeLabels[i]}");
                            }
                        }
                    }
                }

                foreach (var obj in policy.AllowedObjects)
                {
                    await using var cmd = new SqlCommand(ObjectProbeSql, connection, tx);
                    cmd.CommandTimeout = _options.EffectiveCommandTimeoutSeconds;
                    cmd.Parameters.Add(new SqlParameter("@obj", SqlDbType.NVarChar, 300) { Value = obj.Quoted });
                    await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        for (var i = 0; i < ObjectProbeLabels.Length; i++)
                        {
                            if (Convert.ToInt32(reader.GetValue(i)) == 1)
                            {
                                findings.Add($"{obj}: {ObjectProbeLabels[i]}");
                            }
                        }
                    }
                }
            }
            finally
            {
                await tx.RollbackAsync(CancellationToken.None);
            }
        }
        catch (SqlException ex)
        {
            // Number/Class only: SqlException messages can include server or login names.
            logger.LogWarning("Mabna read-only check could not run (SQL error {Number}, class {Class}); will retry on next read.", ex.Number, ex.Class);
            throw new MabnaSourceUnavailableException("Mabna source is unreachable.");
        }

        if (findings.Count == 0)
        {
            logger.LogInformation("Mabna read-only check passed: no write, DDL or execute permission found.");
            return MabnaGateState.ReadOnlyConfirmed;
        }

        if (_options.AllowWritableLogin)
        {
            logger.LogWarning(
                "Mabna login has write-capable permissions ({Findings}); running anyway because MabnaSource__AllowWritableLogin=true. " +
                "Replace it with a read-only login (spec FR-03, PRD NFR-07).",
                string.Join(", ", findings));
            return MabnaGateState.WritableOverridden;
        }

        logger.LogWarning(
            "Mabna connector refused: login has write-capable permissions ({Findings}). " +
            "Use a read-only login, or set MabnaSource__AllowWritableLogin=true to override explicitly.",
            string.Join(", ", findings));
        return MabnaGateState.WritableBlocked;
    }
}
