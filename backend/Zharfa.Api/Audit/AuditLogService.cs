namespace Zharfa.Api.Audit;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Zharfa.Api.Mabna.Sync;

/// <summary>One audit row as the admin screen shows it (contracts/api.md §3).</summary>
public sealed record AuditRowDto(
    long Id,
    string At,
    string AtJalali,
    string? Actor,
    string Action,
    string ActionLabel,
    string? Target,
    string? Detail);

/// <summary>One page of the audit log with the total of the whole filter.</summary>
public sealed record AuditPageResult(long Total, int Page, int PageSize, IReadOnlyList<AuditRowDto> Rows);

/// <summary>
/// Reads the audit log for the admin screen (research R10, FR-017). Fixed SQL text: every filter is a bound parameter
/// that is ignored when null. Read only: this service never updates or deletes an audit row.
/// </summary>
public sealed class AuditLogService(NpgsqlDataSource db, SyncRuntimeSettings runtime)
{
    private const string Filter = """
        WHERE (@actor::text IS NULL OR actor = @actor)
          AND (@action::text IS NULL OR action = @action)
          AND (@from::timestamptz IS NULL OR at >= @from)
          AND (@to::timestamptz IS NULL OR at < @to)
        """;

    private const string PageSql = "SELECT audit_id, at, actor, action, target, detail FROM audit_log " + Filter +
        " ORDER BY at DESC, audit_id DESC LIMIT @limit OFFSET @offset";

    private const string CountSql = "SELECT count(*) FROM audit_log " + Filter;

    public async Task<AuditPageResult> ListAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        long total;
        await using (var count = db.CreateCommand(CountSql))
        {
            BindFilter(count, query);
            total = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken));
        }

        var rows = new List<AuditRowDto>();
        if (total > 0)
        {
            await using var page = db.CreateCommand(PageSql);
            BindFilter(page, query);
            page.Parameters.AddWithValue("limit", query.PageSize);
            page.Parameters.AddWithValue("offset", (long)(query.Page - 1) * query.PageSize);

            await using var reader = await page.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(MapRow(reader));
            }
        }

        return new AuditPageResult(total, query.Page, query.PageSize, rows);
    }

    private static void BindFilter(NpgsqlCommand command, AuditQuery query)
    {
        command.Parameters.AddWithValue("actor", (object?)query.User ?? DBNull.Value);
        command.Parameters.AddWithValue("action", (object?)query.Action ?? DBNull.Value);
        command.Parameters.AddWithValue("from", (object?)query.FromUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("to", (object?)query.ToUtcExclusive ?? DBNull.Value);
    }

    private AuditRowDto MapRow(NpgsqlDataReader reader)
    {
        var at = reader.GetFieldValue<DateTime>(1);
        var utc = new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc));
        var action = reader.GetString(3);

        return new AuditRowDto(
            Id: reader.GetInt64(0),
            At: SyncTimeText.Iso(utc, runtime.Zone),
            AtJalali: SyncTimeText.Jalali(utc, runtime.Zone),
            Actor: reader.IsDBNull(2) ? null : reader.GetString(2),
            Action: action,
            ActionLabel: AuditActions.Label(action),
            Target: reader.IsDBNull(4) ? null : reader.GetString(4),
            Detail: reader.IsDBNull(5) ? null : reader.GetString(5));
    }
}
