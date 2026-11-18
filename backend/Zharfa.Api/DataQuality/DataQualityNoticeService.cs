namespace Zharfa.Api.DataQuality;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Zharfa.Api.Purchasing.Reports;

/// <summary>
/// Scoped service that computes the data-quality notice for a report response (contracts/api.md §6, FR-027).
/// Runs one query over the report's scoped lines and the succeeded data-quality run, plus a second query over
/// the comparison scope when comparison figures are shown (FR-027, analyze I8).
/// </summary>
public sealed class DataQualityNoticeService(NpgsqlDataSource dataSource, ILogger<DataQualityNoticeService> logger)
{
    public async Task<DataQualityNotice?> ForScopeAsync(
        string reportKey,
        ReportQueryScope scope,
        SearchTarget target,
        IReadOnlyList<string>? vendorKeys,
        CancellationToken ct,
        ReportQueryScope? comparisonScope = null)
    {
        var types = DataQualityCatalog.TypesFor(reportKey);
        if (types.Count == 0)
        {
            return null;
        }

        try
        {
            var sql = ReportSql.DataQualityNotice(target);
            var typesArray = types.ToArray();
            var currentHits = await QueryScopeAsync(sql, scope, typesArray, vendorKeys, ct);
            var hits = (IEnumerable<(string Type, string RecordRef)>)currentHits;

            if (comparisonScope is not null)
            {
                var comparisonHits = await QueryScopeAsync(sql, comparisonScope, typesArray, vendorKeys, ct);
                hits = currentHits.Concat(comparisonHits);
            }

            return BuildNotice(reportKey, hits);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Data-quality notice query failed [{ExceptionType}].", ex.GetType().Name);
            return DataQualityNotice.CheckUnavailable;
        }
    }

    private async Task<List<(string Type, string RecordRef)>> QueryScopeAsync(
        string sql,
        ReportQueryScope scope,
        string[] types,
        IReadOnlyList<string>? vendorKeys,
        CancellationToken ct)
    {
        var list = new List<(string Type, string RecordRef)>();
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.CommandTimeout = 15;
        ReportSql.Bind(cmd, scope);
        cmd.Parameters.Add(new NpgsqlParameter("types", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = types });
        cmd.Parameters.Add(new NpgsqlParameter("vendorKeys", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = vendorKeys is not null ? vendorKeys.ToArray() : DBNull.Value,
        });

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var type = reader.GetString(0);
            var recordRef = reader.GetString(1);
            list.Add((type, recordRef));
        }

        return list;
    }

    /// <summary>
    /// Pure helper that aggregates flagged hits for a report: keeps only mapped types in catalog order,
    /// counts distinct record refs per type, and counts overall distinct affected records (US5 AC6, AC7).
    /// </summary>
    public static DataQualityNotice? BuildNotice(string reportKey, IEnumerable<(string Type, string RecordRef)> hits)
    {
        var allowedTypes = DataQualityCatalog.TypesFor(reportKey);
        if (allowedTypes.Count == 0)
        {
            return null;
        }

        var allowedSet = allowedTypes.ToHashSet(StringComparer.Ordinal);
        var filtered = hits.Where(h => allowedSet.Contains(h.Type)).Distinct().ToList();
        if (filtered.Count == 0)
        {
            return null;
        }

        var affectedRecords = filtered.Select(h => h.RecordRef).Distinct(StringComparer.Ordinal).LongCount();

        var byType = filtered
            .GroupBy(h => h.Type, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.RecordRef).Distinct(StringComparer.Ordinal).LongCount(),
                StringComparer.Ordinal);

        var issueTypes = DataQualityCatalog.IssueTypes
            .Where(t => byType.ContainsKey(t.Id))
            .Select(t => new DataQualityNoticeType(t.Id, t.Label, byType[t.Id]))
            .ToList();

        return new DataQualityNotice(issueTypes, affectedRecords, Unavailable: false);
    }
}
