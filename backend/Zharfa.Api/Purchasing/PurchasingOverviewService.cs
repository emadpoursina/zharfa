namespace Zharfa.Api.Purchasing;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Npgsql;
using Zharfa.Api.Analytics;
using Zharfa.Api.DataQuality;
using Zharfa.Api.Mabna;
using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.Platform;
using Zharfa.Api.Purchasing.Reports;

/// <summary>Last successful sync: ISO time with the sync zone offset, and Jalali text with Persian digits.</summary>
public sealed record OverviewSyncTime(string At, string AtJalali);

/// <summary>Month totals in Rial, as decimal strings with 4 decimals; null when unavailable.</summary>
public sealed record OverviewTotals(
    string? Net,
    string? Gross,
    string? Returns,
    string? Vat,
    string? VatInvoice,
    string? VatReturn);

public sealed record OverviewComparison(string PreviousMonth, string PreviousNet, string Delta, string? Percent);

public sealed record OverviewTrendPoint(string Month, string? Net, bool Available);

public sealed record OverviewRankedItem(string? Key, string Name, string Net, bool Unknown);

public sealed record OverviewUnavailable(string Figure, string Reason);

/// <summary>The Overview payload of contracts/api.md §3.</summary>
public sealed record PurchasingOverviewResult(
    string Month,
    string MonthLabel,
    string DataLabel,
    OverviewSyncTime? LastSuccessfulSync,
    string Currency,
    string CurrencyLabel,
    string DiscountBasis,
    OverviewTotals Totals,
    OverviewComparison? Comparison,
    IReadOnlyList<OverviewTrendPoint> Trend,
    int? ActiveVendors,
    IReadOnlyList<OverviewRankedItem>? TopVendors,
    IReadOnlyList<OverviewRankedItem>? TopProducts,
    IReadOnlyList<OverviewUnavailable> Unavailable,
    DataQualityNotice? DataQuality = null);

/// <summary>
/// Builds the Overview (FR-025..FR-029) from the server-side purchasing functions in
/// <c>db/migrations/0006_purchasing_kpis.sql</c>. Every figure follows the single definitions in
/// <c>docs/metrics/purchasing.md</c>; the same functions feed the reconciliation, so the Overview
/// net equals the reconciled net. A figure whose source items are unavailable (MetricAvailability)
/// is returned as <c>null</c> with its reason and its query is not run.
/// </summary>
public sealed class PurchasingOverviewService(
    NpgsqlDataSource dataSource,
    IOptions<PurchasingOptions> purchasingOptions,
    IOptions<MabnaSyncOptions> mabnaSyncOptions,
    IOptions<SyncOptions> syncOptions,
    MabnaObjectPolicy policy,
    DataModeOptions dataMode,
    TimeProvider clock,
    DataQualityNoticeService notices)
{
    public const int TrendMonths = 12;
    public const int TopLimit = 5;
    public const string Currency = "IRR";
    public const string CurrencyLabel = "ریال";

    private const string CompletedSyncSql =
        "SELECT finished_at FROM sync_job WHERE status = 'succeeded' AND finished_at IS NOT NULL " +
        "ORDER BY finished_at DESC LIMIT 1";

    private const string TotalsSql =
        "SELECT gross, returns, net, vat_invoice, vat_return, vat " +
        "FROM mabna.fn_purchase_totals(@year::smallint, @month::smallint, NULL::text, NULL::text, @basis::text)";

    private const string TrendSql =
        "SELECT jalali_year, jalali_month, net, has_data " +
        "FROM mabna.fn_purchase_trend(@year::smallint, @month::smallint, @months::int, @basis::text)";

    private const string FirstDataMonthSql =
        "SELECT jalali_year, jalali_month FROM mabna.fn_first_data_month()";

    private const string ActiveVendorsSql =
        "SELECT mabna.fn_active_vendors(@year::smallint, @month::smallint)";

    private const string TopVendorsSql =
        "SELECT key, name_fa, net, is_unknown " +
        "FROM mabna.fn_top_vendors(@year::smallint, @month::smallint, @limit::int, @basis::text)";

    private const string TopProductsSql =
        "SELECT key, name_fa, net, is_unknown " +
        "FROM mabna.fn_top_products(@year::smallint, @month::smallint, @limit::int, @basis::text)";

    /// <summary>The Jalali month of "today" in the sync time zone; the default when no month is requested.</summary>
    public JalaliMonth CurrentMonth()
    {
        var zone = SyncOptions.ResolveTimeZone(syncOptions.Value.TimeZone);
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);
        var today = JalaliDate.FromDateTime(local.DateTime);
        return new JalaliMonth(today.Year, today.Month);
    }

    public async Task<bool> HasSuccessfulSyncAsync(CancellationToken cancellationToken)
    {
        var lastSynced = await ReadLastSucceededAtAsync(cancellationToken);
        return lastSynced is not null;
    }

    public async Task<PurchasingOverviewResult> GetAsync(JalaliMonth month, CancellationToken cancellationToken)
    {
        var basis = purchasingOptions.Value.DiscountBasis;
        var zone = SyncOptions.ResolveTimeZone(syncOptions.Value.TimeZone);

        var lastSynced = await ReadLastSucceededAtAsync(cancellationToken);
        var map = lastSynced is null ? null : LoadStructurallyValidMap();
        var reasons = MetricAvailability.Evaluate(map, dataMode.IsProduction, lastSynced is not null, basis);

        var current = AnyAvailable(reasons, MetricAvailability.FigureNet, MetricAvailability.FigureGross,
                MetricAvailability.FigureReturns, MetricAvailability.FigureVat)
            ? await ReadTotalsAsync(month, basis, cancellationToken)
            : null;

        var totals = new OverviewTotals(
            Net: Money(reasons, MetricAvailability.FigureNet, current?.Net),
            Gross: Money(reasons, MetricAvailability.FigureGross, current?.Gross),
            Returns: Money(reasons, MetricAvailability.FigureReturns, current?.Returns),
            Vat: Money(reasons, MetricAvailability.FigureVat, current?.Vat),
            VatInvoice: Money(reasons, MetricAvailability.FigureVat, current?.VatInvoice),
            VatReturn: Money(reasons, MetricAvailability.FigureVat, current?.VatReturn));

        var comparisonReason = Reason(reasons, MetricAvailability.FigureComparison);
        var previous = month.Previous();
        OverviewComparison? comparison = null;
        if (comparisonReason is null)
        {
            var firstDataMonth = await ReadFirstDataMonthAsync(cancellationToken);
            if (!ComparisonMath.HasPreviousData(previous, firstDataMonth))
            {
                comparisonReason = MetricAvailability.NoPreviousData;
            }
            else
            {
                var previousNet = (await ReadTotalsAsync(previous, basis, cancellationToken)).Net;
                var currentNet = current!.Net;
                var percent = ComparisonMath.Percent(currentNet, previousNet);
                comparison = new OverviewComparison(
                    PreviousMonth: previous.Key,
                    PreviousNet: ComparisonMath.Amount(previousNet),
                    Delta: ComparisonMath.Amount(ComparisonMath.Delta(currentNet, previousNet)),
                    Percent: percent is { } p ? ComparisonMath.Percent2(p) : null);
            }
        }

        var window = JalaliMonth.Window(month, TrendMonths);
        var trendReason = Reason(reasons, MetricAvailability.FigureTrend);
        var trend = new List<OverviewTrendPoint>(window.Count);
        if (trendReason is null)
        {
            var rows = await ReadTrendAsync(month, basis, cancellationToken);
            foreach (var point in window)
            {
                trend.Add(rows.TryGetValue(point, out var row) && row.Net is { } net
                    ? new OverviewTrendPoint(point.Key, ComparisonMath.Amount(net), true)
                    : new OverviewTrendPoint(point.Key, null, false));
            }
        }
        else
        {
            trend.AddRange(window.Select(point => new OverviewTrendPoint(point.Key, null, false)));
        }

        int? activeVendors = Reason(reasons, MetricAvailability.FigureActiveVendors) is null
            ? await ReadActiveVendorsAsync(month, cancellationToken)
            : null;

        var topVendors = Reason(reasons, MetricAvailability.FigureTopVendors) is null
            ? await ReadTopAsync(TopVendorsSql, month, basis, cancellationToken)
            : null;

        var topProducts = Reason(reasons, MetricAvailability.FigureTopProducts) is null
            ? await ReadTopAsync(TopProductsSql, month, basis, cancellationToken)
            : null;

        var unavailable = new List<OverviewUnavailable>();
        AddUnavailable(unavailable, MetricAvailability.FigureNet, Reason(reasons, MetricAvailability.FigureNet));
        AddUnavailable(unavailable, MetricAvailability.FigureGross, Reason(reasons, MetricAvailability.FigureGross));
        AddUnavailable(unavailable, MetricAvailability.FigureReturns, Reason(reasons, MetricAvailability.FigureReturns));
        AddUnavailable(unavailable, MetricAvailability.FigureVat, Reason(reasons, MetricAvailability.FigureVat));
        AddUnavailable(unavailable, MetricAvailability.FigureComparison, comparisonReason);
        AddUnavailable(unavailable, MetricAvailability.FigureTrend, trendReason);
        AddUnavailable(unavailable, MetricAvailability.FigureActiveVendors, Reason(reasons, MetricAvailability.FigureActiveVendors));
        AddUnavailable(unavailable, MetricAvailability.FigureTopVendors, Reason(reasons, MetricAvailability.FigureTopVendors));
        AddUnavailable(unavailable, MetricAvailability.FigureTopProducts, Reason(reasons, MetricAvailability.FigureTopProducts));

        DataQualityNotice? dqNotice = null;
        if (lastSynced is not null)
        {
            var fromKey = month.Year * 10000 + month.Month * 100 + 1;
            var toKey = month.Year * 10000 + month.Month * 100 + JalaliDate.DaysInMonth(month.Year, month.Month);
            var scope = new ReportQueryScope(fromKey, toKey, basis);

            ReportQueryScope? previousScope = null;
            if (comparison is not null)
            {
                var prevMonth = month.Previous();
                var prevFromKey = prevMonth.Year * 10000 + prevMonth.Month * 100 + 1;
                var prevToKey = prevMonth.Year * 10000 + prevMonth.Month * 100 + JalaliDate.DaysInMonth(prevMonth.Year, prevMonth.Month);
                previousScope = new ReportQueryScope(prevFromKey, prevToKey, basis);
            }

            dqNotice = await notices.ForScopeAsync("overview", scope, SearchTarget.Vendor, null, cancellationToken, comparisonScope: previousScope);
        }

        return new PurchasingOverviewResult(
            Month: month.Key,
            MonthLabel: month.Label,
            DataLabel: dataMode.DataLabel,
            LastSuccessfulSync: lastSynced is { } utc
                ? new OverviewSyncTime(SyncTimeText.Iso(utc, zone), SyncTimeText.Jalali(utc, zone))
                : null,
            Currency: Currency,
            CurrencyLabel: CurrencyLabel,
            DiscountBasis: basis,
            Totals: totals,
            Comparison: comparison,
            Trend: trend,
            ActiveVendors: activeVendors,
            TopVendors: topVendors,
            TopProducts: topProducts,
            Unavailable: unavailable,
            DataQuality: dqNotice);
    }

    /// <summary>
    /// The source map used for availability: it must load and pass the structural validator (rules 1–6).
    /// Requirements for the sync run are not applied here: an unverified item must show as its own
    /// figure reason, not void the whole map. A missing or invalid map leaves every figure unmapped.
    /// </summary>
    private SourceMap? LoadStructurallyValidMap()
    {
        var load = SourceMap.LoadFromFile(mabnaSyncOptions.Value.SourceMapFile);
        if (load.Map is null)
        {
            return null;
        }

        return SourceMapValidator.Validate(load.Map, policy).Count == 0 ? load.Map : null;
    }

    private static string? Reason(IReadOnlyDictionary<string, string?> reasons, string figure) =>
        reasons.TryGetValue(figure, out var reason) ? reason : null;

    private static bool AnyAvailable(IReadOnlyDictionary<string, string?> reasons, params string[] figures) =>
        figures.Any(figure => Reason(reasons, figure) is null);

    private static string? Money(IReadOnlyDictionary<string, string?> reasons, string figure, decimal? value) =>
        Reason(reasons, figure) is null && value is { } amount ? ComparisonMath.Amount(amount) : null;

    private static void AddUnavailable(List<OverviewUnavailable> list, string figure, string? reason)
    {
        if (reason is not null)
        {
            list.Add(new OverviewUnavailable(figure, reason));
        }
    }

    private async Task<DateTimeOffset?> ReadLastSucceededAtAsync(CancellationToken cancellationToken)
    {
        await using var cmd = dataSource.CreateCommand(CompletedSyncSql);
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value is DateTime finished
            ? new DateTimeOffset(DateTime.SpecifyKind(finished, DateTimeKind.Utc))
            : null;
    }

    private async Task<MonthTotals> ReadTotalsAsync(JalaliMonth month, string basis, CancellationToken cancellationToken)
    {
        await using var cmd = dataSource.CreateCommand(TotalsSql);
        cmd.Parameters.AddWithValue("year", month.Year);
        cmd.Parameters.AddWithValue("month", month.Month);
        cmd.Parameters.AddWithValue("basis", basis);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("fn_purchase_totals returned no row.");
        }

        return new MonthTotals(
            Gross: reader.GetDecimal(0),
            Returns: reader.GetDecimal(1),
            Net: reader.GetDecimal(2),
            VatInvoice: reader.GetDecimal(3),
            VatReturn: reader.GetDecimal(4),
            Vat: reader.GetDecimal(5));
    }

    private async Task<Dictionary<JalaliMonth, TrendRow>> ReadTrendAsync(JalaliMonth month, string basis, CancellationToken cancellationToken)
    {
        await using var cmd = dataSource.CreateCommand(TrendSql);
        cmd.Parameters.AddWithValue("year", month.Year);
        cmd.Parameters.AddWithValue("month", month.Month);
        cmd.Parameters.AddWithValue("months", TrendMonths);
        cmd.Parameters.AddWithValue("basis", basis);

        var rows = new Dictionary<JalaliMonth, TrendRow>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var point = new JalaliMonth(reader.GetInt16(0), reader.GetInt16(1));
            var net = reader.IsDBNull(2) ? (decimal?)null : reader.GetDecimal(2);
            rows[point] = new TrendRow(net, reader.GetBoolean(3));
        }

        return rows;
    }

    private async Task<JalaliMonth?> ReadFirstDataMonthAsync(CancellationToken cancellationToken)
    {
        await using var cmd = dataSource.CreateCommand(FirstDataMonthSql);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new JalaliMonth(reader.GetInt16(0), reader.GetInt16(1));
    }

    private async Task<int> ReadActiveVendorsAsync(JalaliMonth month, CancellationToken cancellationToken)
    {
        await using var cmd = dataSource.CreateCommand(ActiveVendorsSql);
        cmd.Parameters.AddWithValue("year", month.Year);
        cmd.Parameters.AddWithValue("month", month.Month);
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<IReadOnlyList<OverviewRankedItem>> ReadTopAsync(
        string sql, JalaliMonth month, string basis, CancellationToken cancellationToken)
    {
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("year", month.Year);
        cmd.Parameters.AddWithValue("month", month.Month);
        cmd.Parameters.AddWithValue("limit", TopLimit);
        cmd.Parameters.AddWithValue("basis", basis);

        var items = new List<OverviewRankedItem>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new OverviewRankedItem(
                Key: reader.IsDBNull(0) ? null : reader.GetString(0),
                Name: reader.GetString(1),
                Net: ComparisonMath.Amount(reader.GetDecimal(2)),
                Unknown: reader.GetBoolean(3)));
        }

        return items;
    }

    private sealed record MonthTotals(
        decimal Gross,
        decimal Returns,
        decimal Net,
        decimal VatInvoice,
        decimal VatReturn,
        decimal Vat);

    private sealed record TrendRow(decimal? Net, bool HasData);
}
