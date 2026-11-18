namespace Zharfa.Api.DataQuality;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Zharfa.Api.Analytics;
using Zharfa.Api.Audit;
using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.Platform;

/// <summary>A rejected-at-sync count of one table and reason (contracts/api.md §4). Codes and counts only.</summary>
public sealed record RejectedCount(string Table, string Reason, long Count);

/// <summary>The newest sync job (contracts/api.md §4 <c>latestSync</c>).</summary>
public sealed record LatestSyncSummary(long Id, string Status, string? FinishedAt, string? FinishedAtJalali, string? Error);

/// <summary>The check whose results are shown (<c>check</c>).</summary>
public sealed record CheckSummary(long RunId, long SyncJobId, string CheckedAt, string CheckedAtJalali);

/// <summary>One example record of an issue type. Field and product or vendor names are display values.</summary>
public sealed record IssueExample(
    string RecordKind,
    string? DocumentNumber,
    string? DateJalali,
    string? Vendor,
    string? Product,
    string Field,
    string FieldLabel);

/// <summary>A report that shows an issue type (<c>affectedReports</c>).</summary>
public sealed record AffectedReport(string Key, string Label);

/// <summary>A deferred sub-check (<c>notChecked</c>).</summary>
public sealed record NotCheckedItem(string Code, string Reason);

/// <summary>One issue type of the page. <see cref="Count"/> is null when no check has run.</summary>
public sealed record IssueTypeResult(
    string Type,
    string Label,
    string Description,
    long? Count,
    IReadOnlyList<IssueExample> Examples,
    IReadOnlyList<AffectedReport> AffectedReports,
    string? Heuristic,
    IReadOnlyList<NotCheckedItem> NotChecked);

/// <summary>The data-quality page body (contracts/api.md §4).</summary>
public sealed record DataQualityPage(
    string DataLabel,
    LatestSyncSummary? LatestSync,
    bool LatestSyncFailed,
    CheckSummary? Check,
    bool LatestCheckFailed,
    IReadOnlyList<string> Focus,
    IReadOnlyList<IssueTypeResult> IssueTypes,
    IReadOnlyList<RejectedCount> RejectedAtSync);

/// <summary>
/// Reads the current data-quality result for the Admin page (contracts/api.md §4). Reads the synced copy and the
/// check tables only; never Mabna. Every value is a bound parameter.
/// </summary>
public sealed class DataQualityService(NpgsqlDataSource db, DataModeOptions dataMode, SyncRuntimeSettings runtime)
{
    /// <summary>At most this many examples per issue type (contracts/api.md §4).</summary>
    public const int ExamplesPerType = 10;

    private const string UnknownName = "نامشخص";
    private static readonly Regex ReasonPattern = new(@"^[a-z][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant);

    private const string LatestSyncSql =
        "SELECT id, status, finished_at, error_message FROM sync_job ORDER BY id DESC LIMIT 1";

    private const string LatestRunSql =
        "SELECT status FROM data_quality_run ORDER BY id DESC LIMIT 1";

    private const string CurrentCheckSql =
        "SELECT id, sync_job_id, checked_at FROM data_quality_run WHERE status = 'succeeded' ORDER BY id DESC LIMIT 1";

    private const string CountsSql =
        "SELECT issue_type, count(DISTINCT record_ref) FROM data_quality_issue WHERE run_id = @runId GROUP BY issue_type";

    private const string ExamplesSql = """
        WITH flagged AS (
            SELECT DISTINCT ON (i.issue_type, i.record_ref)
                   i.issue_type, i.record_kind, i.record_ref, i.field, i.document_id, i.product_id
            FROM data_quality_issue i
            WHERE i.run_id = @runId
            ORDER BY i.issue_type, i.record_ref, i.field
        ),
        ranked AS (
            SELECT f.issue_type, f.record_kind, f.record_ref, f.field,
                   d.doc_number, d.jalali_key, vn.name_fa AS vendor_name, pn.name_fa AS product_name,
                   row_number() OVER (PARTITION BY f.issue_type ORDER BY d.jalali_key DESC NULLS LAST, f.record_ref) AS rn
            FROM flagged f
            LEFT JOIN mabna.purchase_document d ON d.document_id = f.document_id
            LEFT JOIN mabna.vendor vn ON vn.vendor_id = d.vendor_id
            LEFT JOIN mabna.product pn ON pn.product_id = f.product_id
        )
        SELECT issue_type, record_kind, field, doc_number, jalali_key, vendor_name, product_name
        FROM ranked
        WHERE rn <= @perType
        ORDER BY issue_type, rn
        """;

    private const string RejectedSql =
        "SELECT table_logical, message FROM sync_log " +
        "WHERE sync_job_id = @jobId AND table_logical IS NOT NULL ORDER BY id";

    public async Task<DataQualityPage> GetAsync(IReadOnlyList<string> focus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(focus);

        var latest = await ReadLatestSyncAsync(cancellationToken);
        var latestRunStatus = await ReadLatestRunStatusAsync(cancellationToken);
        var check = await ReadCurrentCheckAsync(cancellationToken);

        Dictionary<string, long> counts = [];
        var examples = new Dictionary<string, List<IssueExample>>(StringComparer.Ordinal);
        var rejected = new List<RejectedCount>();

        if (check is not null)
        {
            counts = await ReadCountsAsync(check.RunId, cancellationToken);
            examples = await ReadExamplesAsync(check.RunId, cancellationToken);
            rejected = await ReadRejectedAsync(check.SyncJobId, cancellationToken);
        }

        var types = DataQualityCatalog.IssueTypes.Select(type => new IssueTypeResult(
            type.Id,
            type.Label,
            type.Description,
            check is null ? null : counts.GetValueOrDefault(type.Id, 0L),
            check is null || !examples.TryGetValue(type.Id, out var list) ? [] : list,
            type.AffectedReports.Select(key => new AffectedReport(key, ReportKeys.Name(key))).ToArray(),
            type.Heuristic,
            type.NotChecked.Select(item => new NotCheckedItem(item.Code, item.Reason)).ToArray())).ToArray();

        return new DataQualityPage(
            dataMode.DataLabel,
            latest,
            latest?.Status == "failed",
            check,
            latestRunStatus == "failed",
            focus,
            types,
            rejected);
    }

    /// <summary>
    /// The rejected-at-sync counts of one sync job, from its <c>rejected:&lt;reason&gt;=&lt;n&gt;</c> parts. Other parts
    /// (reads, warnings, samples) are ignored; a malformed part is skipped; counts of one reason are summed.
    /// </summary>
    public static IReadOnlyList<RejectedCount> ParseRejected(string table, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return [];
        }

        const string prefix = "rejected:";
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var part in message.Split(';'))
        {
            var text = part.Trim();
            if (!text.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var body = text[prefix.Length..];
            var separator = body.LastIndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var reason = body[..separator].Trim();
            if (!ReasonPattern.IsMatch(reason))
            {
                continue;
            }

            if (!long.TryParse(body[(separator + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count <= 0)
            {
                continue;
            }

            if (!totals.ContainsKey(reason))
            {
                totals[reason] = 0;
                order.Add(reason);
            }

            totals[reason] += count;
        }

        return order.Select(reason => new RejectedCount(table, reason, totals[reason])).ToArray();
    }

    private async Task<LatestSyncSummary?> ReadLatestSyncAsync(CancellationToken cancellationToken)
    {
        await using var command = db.CreateCommand(LatestSyncSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var finished = reader.IsDBNull(2) ? (DateTimeOffset?)null : Utc(reader.GetFieldValue<DateTime>(2));
        return new LatestSyncSummary(
            Id: reader.GetInt64(0),
            Status: reader.GetString(1),
            FinishedAt: finished is null ? null : SyncTimeText.Iso(finished.Value, runtime.Zone),
            FinishedAtJalali: finished is null ? null : SyncTimeText.Jalali(finished.Value, runtime.Zone),
            Error: reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private async Task<string?> ReadLatestRunStatusAsync(CancellationToken cancellationToken)
    {
        await using var command = db.CreateCommand(LatestRunSql);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value as string;
    }

    private async Task<CheckSummary?> ReadCurrentCheckAsync(CancellationToken cancellationToken)
    {
        await using var command = db.CreateCommand(CurrentCheckSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var checkedAt = Utc(reader.GetFieldValue<DateTime>(2));
        return new CheckSummary(
            RunId: reader.GetInt64(0),
            SyncJobId: reader.GetInt64(1),
            CheckedAt: SyncTimeText.Iso(checkedAt, runtime.Zone),
            CheckedAtJalali: SyncTimeText.Jalali(checkedAt, runtime.Zone));
    }

    private async Task<Dictionary<string, long>> ReadCountsAsync(long runId, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        await using var command = db.CreateCommand(CountsSql);
        command.Parameters.AddWithValue("runId", runId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts[reader.GetString(0)] = reader.GetInt64(1);
        }

        return counts;
    }

    private async Task<Dictionary<string, List<IssueExample>>> ReadExamplesAsync(long runId, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, List<IssueExample>>(StringComparer.Ordinal);
        await using var command = db.CreateCommand(ExamplesSql);
        command.Parameters.AddWithValue("runId", runId);
        command.Parameters.AddWithValue("perType", ExamplesPerType);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var issueType = reader.GetString(0);
            var kind = reader.GetString(1);
            var field = reader.GetString(2);
            var number = reader.IsDBNull(3) ? null : reader.GetString(3);
            var jalaliKey = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
            var vendorName = reader.IsDBNull(5) ? null : reader.GetString(5);
            var productName = reader.IsDBNull(6) ? null : reader.GetString(6);

            var example = new IssueExample(
                RecordKind: kind,
                DocumentNumber: kind == "product" ? null : number,
                DateJalali: kind == "product" || jalaliKey is null ? null : JalaliText(jalaliKey.Value),
                Vendor: kind == "product" ? null : vendorName ?? UnknownName,
                Product: kind == "document" ? null : productName ?? UnknownName,
                Field: field,
                FieldLabel: DataQualityCatalog.FieldLabel(field));

            if (!result.TryGetValue(issueType, out var list))
            {
                list = [];
                result[issueType] = list;
            }

            list.Add(example);
        }

        return result;
    }

    private async Task<List<RejectedCount>> ReadRejectedAsync(long syncJobId, CancellationToken cancellationToken)
    {
        var rows = new List<(string Table, string? Message)>();
        await using (var command = db.CreateCommand(RejectedSql))
        {
            command.Parameters.AddWithValue("jobId", syncJobId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
            }
        }

        return rows.SelectMany(row => ParseRejected(row.Table, row.Message)).ToList();
    }

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    /// <summary>A Jalali day key as <c>yyyy/mm/dd</c> with Persian digits.</summary>
    private static string JalaliText(int key)
    {
        var year = key / 10000;
        var month = key / 100 % 100;
        var day = key % 100;
        return JalaliDate.ToPersianDigits($"{year:D4}/{month:D2}/{day:D2}");
    }
}
