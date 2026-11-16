namespace Zharfa.Api.Mabna;

using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

public sealed record MabnaReadResult(
    MabnaObjectName Object,
    IReadOnlyList<string> Columns,
    IReadOnlyList<object?[]> Rows,
    bool Truncated);

/// <summary>
/// The single gateway for every Mabna read. Nothing else in the API may open
/// a connection to Mabna. Callers pass a structured <see cref="MabnaQuery"/>;
/// raw SQL is not accepted anywhere.
///
/// Defence in depth, in the order applied to each read:
///  0. Server-side read-only login (primary control, outside this code;
///     spec FR-03 <c>zharfa_ro</c>). <c>ApplicationIntent=ReadOnly</c> does
///     not enforce anything and is not relied on.
///  1. Connector enabled? (connection string configured) — else unavailable.
///  2. <see cref="MabnaObjectPolicy.Authorize"/>: identifier shape, hardcoded
///     exclusions (scuUser, UserRights, users, scu*, msg*), allowlist. Runs
///     before any connection is opened.
///  3. <see cref="MabnaSqlBuilder.Build"/>: explicit columns, parameterized
///     filters, bounded TOP; then <see cref="MabnaSqlGuard"/> re-checks the
///     text is a single SELECT.
///  4. <see cref="MabnaReadOnlyGate"/>: the login's permissions were checked
///     (once per process) and found read-only, or explicitly overridden.
///  5. Execution: guard re-run on the exact command text; command timeout;
///     row ceiling (TOP limit+1, reading stops at the limit and reports
///     truncation); the read runs inside a READ COMMITTED transaction that is
///     always rolled back. T-SQL has no <c>SET TRANSACTION READ ONLY</c>; an
///     unconditional rollback is the real mechanism that undoes any
///     transactional side effect should one ever slip through. It does not
///     cover non-transactional effects, which is why 0 and 2–3 exist.
///     SNAPSHOT isolation is not used because it requires
///     ALLOW_SNAPSHOT_ISOLATION on the production database (unknown).
/// </summary>
public interface IMabnaReader
{
    bool IsEnabled { get; }

    Task<MabnaReadResult> ReadAsync(MabnaQuery query, CancellationToken cancellationToken = default);
}

public sealed class MabnaReader(
    MabnaConnectionFactory connections,
    MabnaObjectPolicy policy,
    MabnaReadOnlyGate gate,
    IOptions<MabnaSourceOptions> options,
    ILogger<MabnaReader> logger) : IMabnaReader
{
    private readonly MabnaSourceOptions _options = options.Value;

    public bool IsEnabled => _options.IsEnabled;

    public async Task<MabnaReadResult> ReadAsync(MabnaQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!_options.IsEnabled)
        {
            throw new MabnaSourceUnavailableException("Mabna source is not configured.");
        }

        var objectName = policy.Authorize(query.Object);
        var command = MabnaSqlBuilder.Build(objectName, query, _options.EffectiveMaxRows);

        await gate.EnsureReadAllowedAsync(cancellationToken);

        // Wall-clock bound in addition to the server-side CommandTimeout.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.EffectiveCommandTimeoutSeconds + Math.Clamp(_options.ConnectTimeoutSeconds, 1, 120)));
        var token = timeout.Token;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var connection = connections.Create();
            await connection.OpenAsync(token);

            await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            try
            {
                MabnaSqlGuard.AssertSingleSelect(command.Sql);
                await using var cmd = new SqlCommand(command.Sql, connection, tx)
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = _options.EffectiveCommandTimeoutSeconds,
                };
                foreach (var (name, value) in command.Parameters)
                {
                    cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
                }

                await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult, token);
                var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
                var rows = new List<object?[]>();
                var truncated = false;
                while (await reader.ReadAsync(token))
                {
                    if (rows.Count >= command.RowLimit)
                    {
                        truncated = true;
                        break;
                    }

                    var values = new object?[reader.FieldCount];
                    reader.GetValues(values!);
                    for (var i = 0; i < values.Length; i++)
                    {
                        if (values[i] is DBNull)
                        {
                            values[i] = null;
                        }
                    }

                    rows.Add(values);
                }

                logger.LogInformation(
                    "Mabna read {Object}: {RowCount} rows{Truncated} in {ElapsedMs} ms.",
                    objectName.ToString(), rows.Count, truncated ? " (truncated at limit)" : string.Empty, stopwatch.ElapsedMilliseconds);

                return new MabnaReadResult(objectName, columns, rows, truncated);
            }
            finally
            {
                // Always roll back — reads never commit anything.
                await tx.RollbackAsync(CancellationToken.None);
            }
        }
        catch (SqlException ex)
        {
            // Number/Class only: SqlException messages can include server, login or object details.
            logger.LogWarning("Mabna read {Object} failed (SQL error {Number}, class {Class}).", objectName.ToString(), ex.Number, ex.Class);
            throw new MabnaSourceUnavailableException($"Mabna read failed (SQL error {ex.Number}).");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Mabna read {Object} timed out after {ElapsedMs} ms.", objectName.ToString(), stopwatch.ElapsedMilliseconds);
            throw new MabnaSourceUnavailableException("Mabna read timed out.");
        }
    }
}
