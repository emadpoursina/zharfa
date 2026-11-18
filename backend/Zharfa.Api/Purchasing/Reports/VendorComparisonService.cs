namespace Zharfa.Api.Purchasing.Reports;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using Zharfa.Api.Analytics;
using Zharfa.Api.Purchasing;

/// <summary>One vendor of the comparison, in request order (contracts/api.md §4).</summary>
public sealed record VendorComparisonVendor(
    string Key,
    string Name,
    string? Code,
    string? Net,
    string? NetQuantity,
    long? DocumentCount,
    string? Returns,
    IReadOnlyList<ProfileTrendRow> Trend);

/// <summary>
/// The vendor comparison response (contracts/api.md §4): the envelope (period only, no previous period), the month
/// header and the vendors. The print twin adds <c>truncated: false</c> and <c>limit: 1000</c> (FR-021).
/// </summary>
public sealed record VendorComparisonResponse(
    string DataLabel,
    ReportSyncTime? LastSuccessfulSync,
    string Currency,
    string CurrencyLabel,
    string DiscountBasis,
    ReportPeriodDto Period,
    IReadOnlyList<ReportFilterEcho> Filters,
    IReadOnlyList<ReportUnavailable> Unavailable,
    IReadOnlyList<string> Months,
    IReadOnlyList<VendorComparisonVendor> Vendors,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    bool? Truncated,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    int? Limit);

/// <summary>
/// Side-by-side comparison of two to five vendors over the selected period (spec 004 T060, US4). One grouped query
/// over <c>mabna.fn_purchase_lines</c> for the measures and one for the monthly net, so each vendor equals its
/// profile totals for the same period (SC-005). Reads PostgreSQL only.
/// </summary>
public sealed class VendorComparisonService(NpgsqlDataSource dataSource, PurchasingReportService reports)
{
    public const int MinVendors = 2;
    public const int MaxVendors = 5;
    public const string ReportId = "vendors-compare";
    public const string TooFewMessage = "برای مقایسه دست‌کم دو تأمین‌کننده لازم است";
    public const string TooManyMessage = "حداکثر پنج تأمین‌کننده را می‌توان مقایسه کرد";
    public const string UnknownVendorMessage = "تأمین‌کننده نامشخص را نمی‌توان مقایسه کرد";

    /// <summary>Filter declaration of the comparison: period only (no vendor filter, comparison refused).</summary>
    public static readonly ReportDefinition Definition = new(
        ReportId,
        ReportGrouping.Vendor,
        Array.Empty<string>(),
        new[] { new SortKey("net", true) },
        SearchTarget.Vendor,
        true,
        false,
        false);

    /// <summary>
    /// Parses <c>vendors=K1,K2,…</c>: trimmed, empty parts ignored, de-duplicated in request order. Fewer than two or
    /// more than five distinct keys, or <c>~unknown</c>, is refused with the Persian message of contracts/api.md §4.
    /// </summary>
    public static bool TryParseKeys(string? raw, out IReadOnlyList<string> keys, out string? error)
    {
        var list = new List<string>();
        keys = list;
        error = null;

        foreach (var part in (raw ?? string.Empty).Split(','))
        {
            var key = part.Trim();
            if (key.Length == 0)
            {
                continue;
            }

            if (key == ReportFilter.UnknownKey)
            {
                error = UnknownVendorMessage;
                return false;
            }

            if (!list.Contains(key, StringComparer.Ordinal))
            {
                list.Add(key);
            }
        }

        if (list.Count < MinVendors)
        {
            error = TooFewMessage;
            return false;
        }

        if (list.Count > MaxVendors)
        {
            error = TooManyMessage;
            return false;
        }

        return true;
    }

    /// <summary>
    /// The comparison, or a Persian error (a key not in the vendor master, or the print/screen rules). The filter
    /// carries the period only; <paramref name="print"/> adds the print envelope fields.
    /// </summary>
    public Task<(VendorComparisonResponse? Response, string? Error)> CompareAsync(
        ReportFilter filter,
        IReadOnlyList<string> keys,
        bool print,
        CancellationToken ct) =>
        PurchasingReportService.Guard(() => LoadAsync(filter, keys, print, ct));

    private async Task<(VendorComparisonResponse? Response, string? Error)> LoadAsync(
        ReportFilter filter,
        IReadOnlyList<string> keys,
        bool print,
        CancellationToken ct)
    {
        var context = await reports.ResolveAsync(ReportId, filter, ct);
        var envelope = context.Envelope;
        var scope = context.Scope;
        var quantityUsable = context.Figures.Quantity;

        var masters = await ReadMastersAsync(keys, context.VendorCodeAvailable, ct);
        if (masters.Count < keys.Count)
        {
            var missing = JalaliDate.ToPersianDigits((keys.Count - masters.Count).ToString(CultureInfo.InvariantCulture));
            return (null, missing + " تأمین‌کننده در فهرست یافت نشد.");
        }

        var buckets = filter.Period.TrendMonths(profile: false);
        var window = PurchasingReportService.TrendWindow(filter.Period, buckets, scope);

        var measures = new Dictionary<string, (decimal Net, decimal NetQuantity, long Documents, decimal Returns)>(StringComparer.Ordinal);
        var trendNet = new Dictionary<(string Key, int Year, int Month), decimal>();
        if (!context.Empty)
        {
            await using (var cmd = Command(ReportSql.ComparisonTotals))
            {
                BindVendorKeys(cmd, scope.FromKey, scope.ToKey, scope.Basis, keys);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    measures[Text(reader, "key") ?? string.Empty] = (
                        Dec(reader, "net"),
                        Dec(reader, "net_quantity"),
                        reader.GetInt64(reader.GetOrdinal("document_count")),
                        Dec(reader, "returns"));
                }
            }

            await using (var cmd = Command(ReportSql.ComparisonTrend))
            {
                BindVendorKeys(cmd, window.FromKey, window.ToKey, window.Basis, keys);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var key = Text(reader, "key") ?? string.Empty;
                    var year = (int)reader.GetInt16(reader.GetOrdinal("jalali_year"));
                    var month = (int)reader.GetInt16(reader.GetOrdinal("jalali_month"));
                    trendNet[(key, year, month)] = Dec(reader, "net");
                }
            }
        }

        var firstData = await reports.ReadFirstDataMonthAsync(ct);
        var vendors = new List<VendorComparisonVendor>(keys.Count);
        foreach (var key in keys)
        {
            var master = masters[key];
            var trend = new List<ProfileTrendRow>(buckets.Count);
            foreach (var bucket in buckets)
            {
                var available = firstData is null || bucket.Month.Index >= firstData.Value.Index;
                var net = context.Empty || !available
                    ? (decimal?)null
                    : trendNet.TryGetValue((key, bucket.Month.Year, bucket.Month.Month), out var value) ? value : 0m;
                trend.Add(new ProfileTrendRow(bucket.Month.Key, net is { } n ? ComparisonMath.Amount(n) : null, available, bucket.Partial));
            }

            var measure = measures.TryGetValue(key, out var m) ? m : default;
            vendors.Add(new VendorComparisonVendor(
                Key: key,
                Name: master.Name,
                Code: master.Code,
                Net: context.Empty ? null : ComparisonMath.Amount(measure.Net),
                NetQuantity: context.Empty || !quantityUsable ? null : ComparisonMath.Amount(measure.NetQuantity),
                DocumentCount: context.Empty ? null : measure.Documents,
                Returns: context.Empty ? null : ComparisonMath.Amount(measure.Returns),
                Trend: trend));
        }

        var response = new VendorComparisonResponse(
            envelope.DataLabel,
            envelope.LastSuccessfulSync,
            envelope.Currency,
            envelope.CurrencyLabel,
            envelope.DiscountBasis,
            envelope.Period,
            envelope.Filters,
            envelope.Unavailable,
            buckets.Select(b => b.Month.Key).ToList(),
            vendors,
            print ? false : null,
            print ? PrintLimit.Limit : null);
        return (response, null);
    }

    /// <summary>Name and code of every requested vendor in the master (the code only when its item is usable).</summary>
    private async Task<Dictionary<string, (string Name, string? Code)>> ReadMastersAsync(IReadOnlyList<string> keys, bool codeUsable, CancellationToken ct)
    {
        var masters = new Dictionary<string, (string Name, string? Code)>(StringComparer.Ordinal);
        await using var cmd = Command("SELECT source_key, name_fa, code FROM mabna.vendor WHERE source_key = ANY(@keys::text[])");
        cmd.Parameters.Add(new NpgsqlParameter("keys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = keys.ToArray() });
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var key = Text(reader, "source_key") ?? string.Empty;
            masters[key] = (Text(reader, "name_fa") ?? string.Empty, codeUsable ? Text(reader, "code") : null);
        }

        return masters;
    }

    private static void BindVendorKeys(NpgsqlCommand cmd, int fromKey, int toKey, string basis, IReadOnlyList<string> keys)
    {
        cmd.Parameters.Add(new NpgsqlParameter("fromKey", NpgsqlDbType.Integer) { Value = fromKey });
        cmd.Parameters.Add(new NpgsqlParameter("toKey", NpgsqlDbType.Integer) { Value = toKey });
        cmd.Parameters.Add(new NpgsqlParameter("basis", NpgsqlDbType.Text) { Value = basis });
        cmd.Parameters.Add(new NpgsqlParameter("vendorKeys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = keys.ToArray() });
    }

    private NpgsqlCommand Command(string sql)
    {
        var cmd = dataSource.CreateCommand(sql);
        cmd.CommandTimeout = PurchasingReportService.CommandTimeoutSeconds;
        return cmd;
    }

    private static decimal Dec(NpgsqlDataReader reader, string column) => PurchasingReportService.Dec(reader, column);

    private static string? Text(NpgsqlDataReader reader, string column) => PurchasingReportService.Text(reader, column);
}
