namespace Zharfa.Api.Purchasing.Reports;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Npgsql;
using Zharfa.Api.Analytics;
using Zharfa.Api.Audit;
using Zharfa.Api.DataQuality;
using Zharfa.Api.Mabna;
using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.Platform;
using Zharfa.Api.Purchasing;

/// <summary>A query exceeded its 15 s budget (contracts/api.md §0): the controller answers 503.</summary>
public sealed class ReportTimeoutException : Exception
{
    public const string PersianMessage = "گزارش در زمان مجاز آماده نشد؛ بازه را کوتاه‌تر کنید.";

    public ReportTimeoutException() : base(PersianMessage)
    {
    }
}

/// <summary>
/// The purchasing reports and the documents list (spec 004 T029–T030, T049–T052). Each query is a fixed template
/// in <see cref="ReportSql"/> over <c>mabna.fn_purchase_lines</c>, bound by parameters, with a 15 s budget.
/// Reads PostgreSQL only; never Mabna. Amounts are exact decimal strings (4 decimals).
/// The vendor directory, profile and comparison (VendorDirectoryService, VendorComparisonService) reuse ResolveAsync and the helpers here.
/// </summary>
public sealed class PurchasingReportService(
    NpgsqlDataSource dataSource,
    IOptions<PurchasingOptions> purchasingOptions,
    IOptions<MabnaSyncOptions> mabnaSyncOptions,
    IOptions<SyncOptions> syncOptions,
    MabnaObjectPolicy policy,
    DataModeOptions dataMode,
    DataQualityNoticeService notices)
{
    public const int CommandTimeoutSeconds = 15;
    public const string UnknownLabel = "نامشخص";

    private const string CompletedSyncSql =
        "SELECT finished_at FROM sync_job WHERE status = 'succeeded' AND finished_at IS NOT NULL " +
        "ORDER BY finished_at DESC LIMIT 1";

    private const string FirstDataMonthSql = "SELECT jalali_year, jalali_month FROM mabna.fn_first_data_month()";
    private const string VendorLabelSql = "SELECT name_fa FROM mabna.vendor WHERE source_key = @key::text";
    private const string ProductLabelSql = "SELECT name_fa FROM mabna.product WHERE source_key = @key::text";
    private const string CategoryLabelSql = "SELECT name_fa FROM mabna.product_category WHERE source_key = @key::text";

    /// <summary>Report id → the figure that gates it (MetricAvailability). Unavailable gate → 200 with no rows.</summary>
    private static readonly IReadOnlyDictionary<string, string> ReportGate = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["by-vendor"] = MetricAvailability.FigureVendorReport,
        ["by-product"] = MetricAvailability.FigureProductReport,
        ["top-products"] = MetricAvailability.FigureProductReport,
        ["by-category"] = MetricAvailability.FigureCategory,
        ["returns"] = MetricAvailability.FigureReturnsReport,
        ["trend"] = MetricAvailability.FigureNet,
        ["transactions"] = MetricAvailability.FigureTransactions,
        ["documents"] = MetricAvailability.FigureTransactions,
        ["vendors"] = MetricAvailability.FigureVendorReport,
        ["vendor-profile"] = MetricAvailability.FigureVendorReport,
        ["vendors-compare"] = MetricAvailability.FigureVendorReport,
    };

    // ----- public entry points: table (screen) and print twin -------------------------------------------------

    public Task<ReportTablePage<GroupedRow>> GroupedAsync(string report, ReportFilter filter, CancellationToken ct) =>
        Guard(async () => Table(await LoadGroupedAsync(report, filter, print: false, ct), filter.PageSize));

    public Task<ReportPrintPage<GroupedRow>> GroupedPrintAsync(string report, ReportFilter filter, CancellationToken ct) =>
        Guard(async () => Print(await LoadGroupedAsync(report, filter, print: true, ct)));

    public Task<ReportTablePage<ReturnsRow>> ReturnsAsync(ReportFilter filter, CancellationToken ct) =>
        Guard(async () => Table(await LoadReturnsAsync(filter, print: false, ct), filter.PageSize));

    public Task<ReportPrintPage<ReturnsRow>> ReturnsPrintAsync(ReportFilter filter, CancellationToken ct) =>
        Guard(async () => Print(await LoadReturnsAsync(filter, print: true, ct)));

    public Task<ReportTablePage<TrendRow>> TrendAsync(ReportFilter filter, CancellationToken ct) =>
        Guard(async () => Table(await LoadTrendAsync(filter, print: false, ct), filter.PageSize));

    public Task<ReportPrintPage<TrendRow>> TrendPrintAsync(ReportFilter filter, CancellationToken ct) =>
        Guard(async () => Print(await LoadTrendAsync(filter, print: true, ct)));

    public Task<ReportTablePage<DocumentRow>> DocumentsAsync(ReportFilter filter, CancellationToken ct, string report = "documents") =>
        Guard(async () => Table(await LoadDocumentsAsync(filter, print: false, report, ct), filter.PageSize));

    public Task<ReportPrintPage<DocumentRow>> DocumentsPrintAsync(ReportFilter filter, CancellationToken ct, string report = "documents") =>
        Guard(async () => Print(await LoadDocumentsAsync(filter, print: true, report, ct)));

    /// <summary>
    /// The unfiltered document header with its amounts, and its lines in scope (product, category and match
    /// filters; every line with <paramref name="all"/>). Null when the id is unknown or the document is cancelled.
    /// </summary>
    public Task<DocumentLinesResponse?> DocumentLinesAsync(ReportFilter filter, long documentId, bool all, CancellationToken ct) =>
        Guard(() => LoadDocumentLinesAsync(filter, documentId, all, ct));

    // ----- grouped reports (by-vendor, by-product, by-category, top-products) ---------------------------------

    private async Task<TableData<GroupedRow>> LoadGroupedAsync(string report, ReportFilter filter, bool print, CancellationToken ct)
    {
        var context = await ResolveAsync(report, filter, ct);
        if (context.Empty)
        {
            return TableData<GroupedRow>.Empty(context.Envelope, filter);
        }

        var totals = await ReadGroupedTotalsAsync(report, context.Scope, ct);
        var page = print ? 1 : ReportFilter.EffectivePage(filter.Page, filter.PageSize, totals.TotalRows);
        var (limit, offset) = print ? (PrintLimit.FetchLimit, 0) : (filter.PageSize, (page - 1) * filter.PageSize);

        Dictionary<string, decimal>? previous = null;
        if (context.PreviousScope is { } previousScope)
        {
            previous = new Dictionary<string, decimal>(StringComparer.Ordinal);
            await using var prevCmd = Command(ReportSql.Grouped(report, "net", true, paged: false), c => ReportSql.Bind(c, previousScope));
            await using var prevReader = await prevCmd.ExecuteReaderAsync(ct);
            while (await prevReader.ReadAsync(ct))
            {
                previous[Text(prevReader, "sort_key") ?? string.Empty] = Dec(prevReader, "net");
            }
        }

        ReportTotals? previousTotals = previous is null
            ? null
            : new ReportTotals(Net: Amount(previous.Values.Aggregate(0m, (sum, value) => sum + value)));

        var rows = new List<GroupedRow>();
        await using var cmd = Command(ReportSql.Grouped(report, filter.Sort, filter.Descending, paged: true), c => ReportSql.Bind(c, context.Scope, limit, offset));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rank = offset;
        while (await reader.ReadAsync(ct))
        {
            var unknown = reader.GetBoolean(reader.GetOrdinal("unknown"));
            var key = unknown ? null : Text(reader, "key");
            var net = Dec(reader, "net");
            decimal? previousNet = previous is not null && previous.TryGetValue(Text(reader, "sort_key") ?? string.Empty, out var p) ? p : null;
            rank++;
            rows.Add(new GroupedRow(
                Key: key,
                Name: unknown ? UnknownLabel : Text(reader, "name") ?? UnknownLabel,
                Code: report == "by-vendor" && !unknown && context.VendorCodeAvailable ? Text(reader, "code") : null,
                Unknown: unknown,
                Net: Amount(net),
                Gross: Amount(Dec(reader, "gross")),
                Returns: Amount(Dec(reader, "returns")),
                Vat: Amount(Dec(reader, "vat")),
                NetQuantity: context.Figures.Quantity ? Amount(Dec(reader, "net_quantity")) : null,
                DocumentCount: reader.GetInt64(reader.GetOrdinal("document_count")),
                PreviousNet: previousNet is { } pn ? Amount(pn) : null,
                Delta: previousNet is { } pd ? Amount(ComparisonMath.Delta(net, pd)) : null,
                Percent: PercentText(net, previousNet),
                Drill: DrillFor(report, unknown, key),
                Rank: report == "top-products" ? rank : null));
        }

        return new TableData<GroupedRow>(
            context.Envelope,
            totals.TotalRows,
            page,
            filter.Sort,
            Dir(filter),
            totals.Totals,
            previousTotals,
            rows);
    }

    internal async Task<(long TotalRows, ReportTotals Totals)> ReadGroupedTotalsAsync(string report, ReportQueryScope scope, CancellationToken ct)
    {
        await using var cmd = Command(ReportSql.GroupedTotals(report), c => ReportSql.Bind(c, scope));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (
            reader.GetInt64(reader.GetOrdinal("total_rows")),
            new ReportTotals(
                Gross: Amount(Dec(reader, "gross")),
                Returns: Amount(Dec(reader, "returns")),
                Net: Amount(Dec(reader, "net")),
                Vat: Amount(Dec(reader, "vat")),
                VatInvoice: Amount(Dec(reader, "vat_invoice")),
                VatReturn: Amount(Dec(reader, "vat_return")),
                NetQuantity: Amount(Dec(reader, "net_quantity")),
                DocumentCount: reader.GetInt64(reader.GetOrdinal("document_count")),
                LineCount: reader.GetInt64(reader.GetOrdinal("line_count")),
                QuantityMissingLines: reader.GetInt64(reader.GetOrdinal("quantity_missing_lines"))));
    }

    private static ReportDrill DrillFor(string report, bool unknown, string? key)
    {
        var value = unknown ? ReportFilter.UnknownKey : key;
        return report switch
        {
            "by-vendor" => new ReportDrill(Vendor: value),
            "by-category" => new ReportDrill(Category: value),
            _ => new ReportDrill(Product: value),
        };
    }

    // ----- returns report (return documents only, vendor × product) -------------------------------------------

    private async Task<TableData<ReturnsRow>> LoadReturnsAsync(ReportFilter filter, bool print, CancellationToken ct)
    {
        var context = await ResolveAsync("returns", filter, ct);
        if (context.Empty)
        {
            return TableData<ReturnsRow>.Empty(context.Envelope, filter);
        }

        await using var totalsCmd = Command(ReportSql.ReturnsTotals(), c => ReportSql.Bind(c, context.Scope));
        await using var totalsReader = await totalsCmd.ExecuteReaderAsync(ct);
        await totalsReader.ReadAsync(ct);
        var totalRows = totalsReader.GetInt64(totalsReader.GetOrdinal("total_rows"));
        var totals = new ReportTotals(
            Returns: Amount(Dec(totalsReader, "returns")),
            NetQuantity: Amount(Dec(totalsReader, "quantity")),
            DocumentCount: totalsReader.GetInt64(totalsReader.GetOrdinal("document_count")));
        await totalsReader.CloseAsync();

        var page = print ? 1 : ReportFilter.EffectivePage(filter.Page, filter.PageSize, totalRows);
        var (limit, offset) = print ? (PrintLimit.FetchLimit, 0) : (filter.PageSize, (page - 1) * filter.PageSize);

        Dictionary<string, decimal>? previous = null;
        if (context.PreviousScope is { } previousScope)
        {
            previous = new Dictionary<string, decimal>(StringComparer.Ordinal);
            await using var prevCmd = Command(ReportSql.Returns("returns", true, paged: false), c => ReportSql.Bind(c, previousScope));
            await using var prevReader = await prevCmd.ExecuteReaderAsync(ct);
            while (await prevReader.ReadAsync(ct))
            {
                previous[Text(prevReader, "sort_key") ?? string.Empty] = Dec(prevReader, "returns");
            }
        }

        var rows = new List<ReturnsRow>();
        await using var cmd = Command(ReportSql.Returns(filter.Sort, filter.Descending, paged: true), c => ReportSql.Bind(c, context.Scope, limit, offset));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var unknownVendor = reader.GetBoolean(reader.GetOrdinal("unknown_vendor"));
            var unknownProduct = reader.GetBoolean(reader.GetOrdinal("unknown_product"));
            var vendorKey = unknownVendor ? null : Text(reader, "vendor_key");
            var productKey = unknownProduct ? null : Text(reader, "product_key");
            var returns = Dec(reader, "returns");
            decimal? previousReturns = previous is not null && previous.TryGetValue(Text(reader, "sort_key") ?? string.Empty, out var p) ? p : null;
            rows.Add(new ReturnsRow(
                VendorKey: vendorKey,
                VendorName: unknownVendor ? UnknownLabel : Text(reader, "vendor_name") ?? UnknownLabel,
                ProductKey: productKey,
                ProductName: unknownProduct ? UnknownLabel : Text(reader, "product_name") ?? UnknownLabel,
                UnknownVendor: unknownVendor,
                UnknownProduct: unknownProduct,
                Returns: Amount(returns),
                Quantity: context.Figures.Quantity ? Amount(Dec(reader, "quantity")) : null,
                DocumentCount: reader.GetInt64(reader.GetOrdinal("document_count")),
                PreviousReturns: previousReturns is { } pr ? Amount(pr) : null,
                Delta: previousReturns is { } pd ? Amount(ComparisonMath.Delta(returns, pd)) : null,
                Percent: PercentText(returns, previousReturns),
                Drill: new ReportDrill(
                    Vendor: unknownVendor ? ReportFilter.UnknownKey : vendorKey,
                    Product: unknownProduct ? ReportFilter.UnknownKey : productKey,
                    DocType: "return")));
        }

        ReportTotals? previousTotals = previous is null
            ? null
            : new ReportTotals(Returns: Amount(previous.Values.Aggregate(0m, (sum, value) => sum + value)));

        return new TableData<ReturnsRow>(context.Envelope, totalRows, page, filter.Sort, Dir(filter), totals, previousTotals, rows);
    }

    // ----- trend (month buckets of the period, paged, 37 buckets at most) --------------------------------------

    private async Task<TableData<TrendRow>> LoadTrendAsync(ReportFilter filter, bool print, CancellationToken ct)
    {
        var context = await ResolveAsync("trend", filter, ct);
        if (context.Empty)
        {
            return TableData<TrendRow>.Empty(context.Envelope, filter);
        }

        var period = context.Period;
        var monthBuckets = period.TrendMonths(profile: false);
        var trendScope = TrendWindow(period, monthBuckets, context.Scope);
        var current = await ReadTrendMonthsAsync(trendScope, ct);
        var firstData = await ReadFirstDataMonthAsync(ct);

        // Comparison (T065): each row's previous value is the same Jalali month one period earlier, i.e. the
        // month offset between the two periods. Presets compare whole months; a custom range compares only
        // the days of its previous range.
        Dictionary<(int Year, int Month), (decimal Net, decimal Quantity)>? previousByMonth = null;
        var offset = 0;
        ReportQueryScope? trendPreviousScope = null;
        if (context.PreviousScope is { } previousScope)
        {
            offset = period.StartMonth.Index - period.Previous().StartMonth.Index;
            var first = monthBuckets[0].Month.AddMonths(-offset);
            var last = monthBuckets[^1].Month.AddMonths(-offset);
            var fromKey = KeyOf(first, 1);
            var toKey = KeyOf(last, 31);
            if (period.Kind == JalaliPeriodKind.Custom)
            {
                fromKey = Math.Max(fromKey, previousScope.FromKey);
                toKey = Math.Min(toKey, previousScope.ToKey);
            }

            trendPreviousScope = context.Scope with { FromKey = fromKey, ToKey = toKey };
            previousByMonth = await ReadTrendMonthsAsync(trendPreviousScope, ct);
        }

        DataQualityNotice? trendNotice = null;
        if (context.Envelope.LastSuccessfulSync is not null)
        {
            trendNotice = await notices.ForScopeAsync(
                ReportKeys.Trend,
                trendScope,
                SearchTarget.DocumentOrVendor,
                null,
                ct,
                comparisonScope: trendPreviousScope);
        }

        var buckets = new List<(TrendRow Row, decimal NetValue)>();
        decimal previousTotal = 0m;
        foreach (var bucket in monthBuckets)
        {
            var available = firstData is null || bucket.Month.Index >= firstData.Value.Index;
            var found = current.TryGetValue((bucket.Month.Year, bucket.Month.Month), out var values);
            var net = found ? values.Net : 0m;

            decimal? previousNet = null;
            if (previousByMonth is not null)
            {
                var shifted = bucket.Month.AddMonths(-offset);
                if (ComparisonMath.HasPreviousData(shifted, firstData))
                {
                    previousNet = previousByMonth.TryGetValue((shifted.Year, shifted.Month), out var pv) ? pv.Net : 0m;
                    if (available)
                    {
                        previousTotal += previousNet.Value;
                    }
                }
            }

            buckets.Add((
                new TrendRow(
                    Month: bucket.Month.Key,
                    Label: bucket.Month.Label,
                    Net: available ? Amount(net) : null,
                    NetQuantity: available && context.Figures.Quantity ? Amount(found ? values.Quantity : 0m) : null,
                    Available: available,
                    Partial: bucket.Partial,
                    Drill: new ReportDrill(Month: bucket.Month.Key, Match: filter.Search),
                    PreviousNet: previousNet is { } pn ? Amount(pn) : null,
                    Delta: available && previousNet is { } pd ? Amount(ComparisonMath.Delta(net, pd)) : null,
                    Percent: available && previousNet is { } pp ? PercentText(net, pp) : null),
                net));
        }

        var ordered = filter.Sort == "net"
            ? (filter.Descending ? buckets.OrderByDescending(b => b.NetValue) : buckets.OrderBy(b => b.NetValue)).ToList()
            : (filter.Descending ? buckets.AsEnumerable().Reverse().ToList() : buckets);

        var total = ordered.Count;
        var page = print ? 1 : ReportFilter.EffectivePage(filter.Page, filter.PageSize, total);
        var rows = print
            ? ordered.Take(PrintLimit.FetchLimit).Select(b => b.Row).ToList()
            : ordered.Skip((page - 1) * filter.PageSize).Take(filter.PageSize).Select(b => b.Row).ToList();

        return new TableData<TrendRow>(
            context.Envelope with { DataQuality = trendNotice },
            total,
            page,
            filter.Sort,
            Dir(filter),
            await ReadTrendTotalsAsync(trendScope, ct),
            previousByMonth is null ? null : new ReportTotals(Net: Amount(previousTotal)),
            rows);
    }

    /// <summary>
    /// The line window of the trend: a custom range uses its own days (partial months are clipped); a preset uses
    /// whole months, the 12 buckets of <see cref="JalaliPeriod.TrendMonths"/>, which may extend before the preset.
    /// </summary>
    internal static ReportQueryScope TrendWindow(JalaliPeriod period, IReadOnlyList<TrendBucket> buckets, ReportQueryScope scope)
    {
        if (period.Kind == JalaliPeriodKind.Custom)
        {
            return scope;
        }

        return scope with { FromKey = KeyOf(buckets[0].Month, 1), ToKey = KeyOf(buckets[^1].Month, 31) };
    }

    internal static int KeyOf(JalaliMonth month, int day) => month.Year * 10000 + month.Month * 100 + day;

    /// <summary>Net and quantity per Jalali month over a scope's window, one query.</summary>
    internal async Task<Dictionary<(int Year, int Month), (decimal Net, decimal Quantity)>> ReadTrendMonthsAsync(ReportQueryScope scope, CancellationToken ct)
    {
        var byMonth = new Dictionary<(int Year, int Month), (decimal Net, decimal Quantity)>();
        await using var cmd = Command(ReportSql.Trend, c => ReportSql.Bind(c, scope));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var key = (reader.GetInt16(reader.GetOrdinal("jalali_year")), reader.GetInt16(reader.GetOrdinal("jalali_month")));
            byMonth[key] = (Dec(reader, "net"), Dec(reader, "net_quantity"));
        }

        return byMonth;
    }

    private async Task<ReportTotals> ReadTrendTotalsAsync(ReportQueryScope scope, CancellationToken ct)
    {
        await using var cmd = Command(ReportSql.TrendTotals, c => ReportSql.Bind(c, scope));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new ReportTotals(
            Gross: Amount(Dec(reader, "gross")),
            Returns: Amount(Dec(reader, "returns")),
            Net: Amount(Dec(reader, "net")),
            Vat: Amount(Dec(reader, "vat")),
            NetQuantity: Amount(Dec(reader, "net_quantity")),
            DocumentCount: reader.GetInt64(reader.GetOrdinal("document_count")),
            LineCount: reader.GetInt64(reader.GetOrdinal("line_count")),
            QuantityMissingLines: reader.GetInt64(reader.GetOrdinal("quantity_missing_lines")));
    }

    // ----- documents (transactions report and drill-down list) -------------------------------------------------

    private async Task<TableData<DocumentRow>> LoadDocumentsAsync(ReportFilter filter, bool print, string report, CancellationToken ct)
    {
        var context = await ResolveAsync(report, filter, ct);
        if (context.Empty)
        {
            return TableData<DocumentRow>.Empty(context.Envelope, filter);
        }

        var target = SearchTarget.DocumentOrVendor;
        await using var totalsCmd = Command(ReportSql.DocumentTotals(target), c => ReportSql.Bind(c, context.Scope));
        await using var totalsReader = await totalsCmd.ExecuteReaderAsync(ct);
        await totalsReader.ReadAsync(ct);
        var totalRows = totalsReader.GetInt64(totalsReader.GetOrdinal("total_rows"));
        var totals = new ReportTotals(
            Net: Amount(Dec(totalsReader, "net")),
            InvoiceAmount: Amount(Dec(totalsReader, "invoice_amount")),
            ReturnAmount: Amount(Dec(totalsReader, "return_amount")),
            NetQuantity: Amount(Dec(totalsReader, "net_quantity")),
            LineCount: totalsReader.GetInt64(totalsReader.GetOrdinal("line_count")),
            DocumentCount: totalsReader.GetInt64(totalsReader.GetOrdinal("document_count")),
            QuantityMissingLines: totalsReader.GetInt64(totalsReader.GetOrdinal("quantity_missing_lines")));
        await totalsReader.CloseAsync();

        var page = print ? 1 : ReportFilter.EffectivePage(filter.Page, filter.PageSize, totalRows);
        var (limit, offset) = print ? (PrintLimit.FetchLimit, 0) : (filter.PageSize, (page - 1) * filter.PageSize);

        var rows = new List<DocumentRow>();
        await using var cmd = Command(ReportSql.Documents(filter.Sort, filter.Descending, paged: true, target), c => ReportSql.Bind(c, context.Scope, limit, offset));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(ReadDocumentRow(reader));
        }

        return new TableData<DocumentRow>(context.Envelope, totalRows, page, filter.Sort, Dir(filter), totals, null, rows);
    }

    private async Task<DocumentLinesResponse?> LoadDocumentLinesAsync(ReportFilter filter, long documentId, bool all, CancellationToken ct)
    {
        var basis = purchasingOptions.Value.DiscountBasis;

        DocumentRow? header = null;
        await using (var headerCmd = Command(ReportSql.DocumentHeader, c => ReportSql.BindDocument(c, documentId)))
        await using (var headerReader = await headerCmd.ExecuteReaderAsync(ct))
        {
            if (!await headerReader.ReadAsync(ct))
            {
                return null;
            }

            header = ReadDocumentRow(headerReader);
        }

        // Header amounts are over every line of the document, unfiltered.
        var amounts = new ReportQueryScope(0, 99991231, basis);
        await using (var amountCmd = Command(ReportSql.DocumentAmounts, c => ReportSql.BindDocument(c, documentId, basis)))
        await using (var amountReader = await amountCmd.ExecuteReaderAsync(ct))
        {
            await amountReader.ReadAsync(ct);
            header = header with
            {
                PreDiscount = Amount(Dec(amountReader, "pre_discount")),
                Discount = Amount(Dec(amountReader, "discount")),
                Vat = Amount(Dec(amountReader, "vat")),
                Net = Amount(Dec(amountReader, "net")),
                SignedNet = Amount(Dec(amountReader, "signed_net")),
                Quantity = Amount(Dec(amountReader, "quantity")),
                LineCount = amountReader.GetInt64(amountReader.GetOrdinal("line_count")),
            };
        }

        // Lines in scope: product, category and match only (vendor and document type do not apply to one document).
        var lineScope = all
            ? amounts
            : new ReportQueryScope(0, 99991231, basis, ProductKey: filter.ProductKey, CategoryKey: filter.CategoryKey,
                Match: filter.Match is null ? null : ReportFilter.EscapeLike(filter.Match));

        var lines = new List<LineRow>();
        decimal lineAmount = 0, signed = 0, quantity = 0;
        await using var lineCmd = Command(ReportSql.Lines, c => ReportSql.BindLines(c, lineScope, documentId));
        await using var lineReader = await lineCmd.ExecuteReaderAsync(ct);
        while (await lineReader.ReadAsync(ct))
        {
            var sign = lineReader.GetInt32(lineReader.GetOrdinal("sign"));
            var value = Dec(lineReader, "line_amount");
            var qty = Dec(lineReader, "quantity");
            lineAmount += value;
            signed += sign * value;
            quantity += sign * qty;
            var unknownProduct = lineReader.GetBoolean(lineReader.GetOrdinal("unknown_product"));
            lines.Add(new LineRow(
                LineId: lineReader.GetInt64(lineReader.GetOrdinal("line_id")),
                ProductKey: unknownProduct ? null : Text(lineReader, "product_source_key"),
                ProductName: unknownProduct ? UnknownLabel : Text(lineReader, "product_name") ?? UnknownLabel,
                CategoryName: Text(lineReader, "category_name"),
                UnknownProduct: unknownProduct,
                Quantity: lineReader.IsDBNull(lineReader.GetOrdinal("quantity")) ? null : Amount(qty),
                PreDiscount: Amount(Dec(lineReader, "gross_amount")),
                Discount: Amount(Dec(lineReader, "discount_amount")),
                LineAmount: Amount(value),
                Vat: Amount(Dec(lineReader, "vat_amount")),
                SignedAmount: Amount(sign * value)));
        }

        // Envelope without a period (contracts/api.md §6): the lines' own filters only.
        var lineFilter = filter with { VendorKey = null, DocType = null, Month = null, Search = null, Compare = false };
        var context = await ResolveAsync("documents", lineFilter, ct);
        var envelope = context.Envelope;
        return new DocumentLinesResponse(
            envelope.DataLabel,
            envelope.LastSuccessfulSync,
            envelope.Currency,
            envelope.CurrencyLabel,
            envelope.DiscountBasis,
            envelope.Filters,
            envelope.Unavailable,
            header!,
            lines,
            new ReportLineTotals(Amount(lineAmount), Amount(signed), Amount(quantity)),
            envelope.DataQuality);
    }

    // ----- shared: scope, envelope, availability --------------------------------------------------------------

    /// <summary>Reasons per figure for the sibling services (lookups, directory, comparison).</summary>
    public async Task<IReadOnlyDictionary<string, string?>> AvailabilityAsync(CancellationToken ct)
    {
        var lastSynced = await ReadLastSucceededAtAsync(ct);
        var map = lastSynced is null ? null : LoadStructurallyValidMap();
        return MetricAvailability.Evaluate(map, dataMode.IsProduction, lastSynced is not null, purchasingOptions.Value.DiscountBasis);
    }

    /// <summary>What one request resolves to: the envelope, the bound scopes and the figures that are usable.</summary>
    internal sealed record ReportContext(
        bool Empty,
        ReportEnvelope Envelope,
        ReportQueryScope Scope,
        ReportQueryScope? PreviousScope,
        ReportFigures Figures,
        bool VendorCodeAvailable,
        JalaliPeriod Period);

    internal sealed record ReportFigures(bool Quantity);

    internal async Task<ReportContext> ResolveAsync(string report, ReportFilter filter, CancellationToken ct)
    {
        var basis = purchasingOptions.Value.DiscountBasis;
        var zone = SyncOptions.ResolveTimeZone(syncOptions.Value.TimeZone);
        var lastSynced = await ReadLastSucceededAtAsync(ct);
        var map = lastSynced is null ? null : LoadStructurallyValidMap();
        var reasons = MetricAvailability.Evaluate(map, dataMode.IsProduction, lastSynced is not null, basis);

        var unavailable = new List<ReportUnavailable>();
        if (lastSynced is null)
        {
            unavailable.Add(new ReportUnavailable(MetricAvailability.FigureNet, MetricAvailability.NoSuccessfulSync));
        }

        var gate = ReportGate.TryGetValue(report, out var figure) ? figure : MetricAvailability.FigureNet;
        var gateReason = Reason(reasons, gate);
        if (gateReason is not null)
        {
            unavailable.Add(new ReportUnavailable(gate, gateReason));
        }

        // A category filter needs the product category source (FR-008): when that source is unusable the
        // filter cannot match anything, so the report is empty with the same figure marker as by-category
        // (no duplicate entry when the report's own gate is already the category figure).
        var categoryReason = Reason(reasons, MetricAvailability.FigureCategory);
        var categoryFilterUnusable = filter.CategoryKey is not null && categoryReason is not null;
        if (categoryFilterUnusable && gate != MetricAvailability.FigureCategory)
        {
            unavailable.Add(new ReportUnavailable(MetricAvailability.FigureCategory, categoryReason!));
        }

        var quantityReason = Reason(reasons, MetricAvailability.FigureQuantity);
        if (quantityReason is not null)
        {
            unavailable.Add(new ReportUnavailable(MetricAvailability.FigureQuantity, quantityReason));
        }

        var vendorCodeReason = Reason(reasons, MetricAvailability.FigureVendorCode);
        if (vendorCodeReason is not null && report is "vendors" or "vendor-profile" or "vendors-compare")
        {
            unavailable.Add(new ReportUnavailable(MetricAvailability.FigureVendorCode, vendorCodeReason));
        }

        var period = filter.Period;
        var (from, to) = MonthNarrowed(filter);
        var scope = BuildScope(filter, from, to, basis);

        ReportQueryScope? previousScope = null;
        ReportPeriodDto? previousPeriod = null;
        if (filter.Compare)
        {
            var previousValue = period.Previous();
            previousPeriod = ToDto(previousValue);
            var firstDataMonth = await ReadFirstDataMonthAsync(ct);
            if (ComparisonMath.HasPreviousData(previousValue.StartMonth, firstDataMonth))
            {
                previousScope = BuildScope(filter, previousValue.FromKey, previousValue.ToKey, basis);
            }
            else
            {
                unavailable.Add(new ReportUnavailable(MetricAvailability.FigureComparison, MetricAvailability.NoPreviousData));
            }
        }

        DataQualityNotice? notice = null;
        if (lastSynced is not null)
        {
            var reportKey = report switch
            {
                "documents" => ReportKeys.DrillDown,
                _ => report,
            };
            var searchTarget = ReportCatalog.TryGet(report, out var catDef)
                ? catDef!.SearchTarget
                : report switch
                {
                    "documents" => ReportCatalog.Documents.SearchTarget,
                    "vendors" => ReportCatalog.Directory.SearchTarget,
                    "vendor-profile" => VendorDirectoryService.ProfileDefinition.SearchTarget,
                    "vendors-compare" => VendorComparisonService.Definition.SearchTarget,
                    _ => SearchTarget.DocumentOrVendor,
                };
            notice = await notices.ForScopeAsync(reportKey, scope, searchTarget, null, ct, comparisonScope: previousScope);
        }

        var envelope = new ReportEnvelope(
            DataLabel: dataMode.DataLabel,
            LastSuccessfulSync: lastSynced is { } utc ? new ReportSyncTime(SyncTimeText.Iso(utc, zone), SyncTimeText.Jalali(utc, zone)) : null,
            Currency: PurchasingOverviewService.Currency,
            CurrencyLabel: PurchasingOverviewService.CurrencyLabel,
            DiscountBasis: basis,
            Period: ToDto(period),
            PreviousPeriod: previousPeriod,
            Filters: await FilterEchoAsync(filter, ct),
            Unavailable: unavailable,
            DataQuality: notice);

        return new ReportContext(
            Empty: lastSynced is null || gateReason is not null || categoryFilterUnusable,
            Envelope: envelope,
            Scope: scope,
            PreviousScope: previousScope,
            Figures: new ReportFigures(Quantity: quantityReason is null),
            VendorCodeAvailable: Reason(reasons, MetricAvailability.FigureVendorCode) is null,
            Period: period);
    }

    private static (int From, int To) MonthNarrowed(ReportFilter filter)
    {
        var from = filter.Period.FromKey;
        var to = filter.Period.ToKey;
        if (filter.Month is null)
        {
            return (from, to);
        }

        var parts = filter.Month.Split('-');
        var year = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var month = int.Parse(parts[1], CultureInfo.InvariantCulture);
        return (Math.Max(from, year * 10000 + month * 100 + 1), Math.Min(to, year * 10000 + month * 100 + 31));
    }

    private static ReportQueryScope BuildScope(ReportFilter filter, int fromKey, int toKey, string basis) => new(
        FromKey: fromKey,
        ToKey: toKey,
        Basis: basis,
        VendorKey: filter.VendorKey,
        ProductKey: filter.ProductKey,
        CategoryKey: filter.CategoryKey,
        DocType: filter.DocType,
        Search: filter.Search is null ? null : ReportFilter.EscapeLike(filter.Search),
        Match: filter.Match is null ? null : ReportFilter.EscapeLike(filter.Match));

    private static ReportPeriodDto ToDto(JalaliPeriod period) => new(
        period.KindName,
        JalaliText(period.From),
        JalaliText(period.To),
        period.Label,
        period.TrimmedTo is { } trimmed ? JalaliText(trimmed) : null);

    private static string JalaliText(JalaliDate date) => $"{date.Year:0000}-{date.Month:00}-{date.Day:00}";

    private async Task<IReadOnlyList<ReportFilterEcho>> FilterEchoAsync(ReportFilter filter, CancellationToken ct)
    {
        var echoes = new List<ReportFilterEcho>();
        if (filter.VendorKey is { } vendor)
        {
            echoes.Add(new ReportFilterEcho("vendor", vendor, await LabelAsync(VendorLabelSql, vendor, ct)));
        }

        if (filter.ProductKey is { } product)
        {
            echoes.Add(new ReportFilterEcho("product", product, await LabelAsync(ProductLabelSql, product, ct)));
        }

        if (filter.CategoryKey is { } category)
        {
            echoes.Add(new ReportFilterEcho("category", category, await LabelAsync(CategoryLabelSql, category, ct)));
        }

        if (filter.Month is { } month)
        {
            echoes.Add(new ReportFilterEcho("month", month, month));
        }

        if (filter.DocType is { } docType)
        {
            echoes.Add(new ReportFilterEcho("docType", docType, DocTypeLabel(docType)));
        }

        if (filter.Search is { } search)
        {
            echoes.Add(new ReportFilterEcho("q", search, search));
        }

        return echoes;
    }

    private async Task<string> LabelAsync(string sql, string key, CancellationToken ct)
    {
        if (key == ReportFilter.UnknownKey)
        {
            return UnknownLabel;
        }

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.Parameters.AddWithValue("key", key);
        return await cmd.ExecuteScalarAsync(ct) as string ?? key;
    }

    internal async Task<JalaliMonth?> ReadFirstDataMonthAsync(CancellationToken ct)
    {
        await using var cmd = Command(FirstDataMonthSql);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new JalaliMonth(reader.GetInt16(0), reader.GetInt16(1));
    }

    private async Task<DateTimeOffset?> ReadLastSucceededAtAsync(CancellationToken ct)
    {
        await using var cmd = Command(CompletedSyncSql);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is DateTime finished ? new DateTimeOffset(DateTime.SpecifyKind(finished, DateTimeKind.Utc)) : null;
    }

    /// <summary>The source map used for availability: it must load and pass the structural validator.</summary>
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

    /// <summary>A command with the 15 s budget; the caller disposes it.</summary>
    internal NpgsqlCommand Command(string sql, Action<NpgsqlCommand>? bind = null)
    {
        var cmd = dataSource.CreateCommand(sql);
        cmd.CommandTimeout = CommandTimeoutSeconds;
        bind?.Invoke(cmd);
        return cmd;
    }

    /// <summary>Maps a 15 s timeout (PostgreSQL 57014 or a client timeout) to <see cref="ReportTimeoutException"/>.</summary>
    internal static async Task<T> Guard<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (PostgresException ex) when (ex.SqlState == "57014")
        {
            throw new ReportTimeoutException();
        }
        catch (NpgsqlException ex) when (ex.InnerException is TimeoutException)
        {
            throw new ReportTimeoutException();
        }
    }

    private static ReportTablePage<TRow> Table<TRow>(TableData<TRow> data, int pageSize)
    {
        var e = data.Envelope;
        return new ReportTablePage<TRow>(
            e.DataLabel, e.LastSuccessfulSync, e.Currency, e.CurrencyLabel, e.DiscountBasis,
            e.Period, e.PreviousPeriod, e.Filters, e.Unavailable,
            data.Page, pageSize, data.TotalRows, data.Sort, data.Dir,
            data.Totals, data.PreviousTotals, data.Rows,
            e.DataQuality);
    }

    /// <summary>Print twin: the rows were fetched with LIMIT 1001 (or the trend's full list); PrintLimit keeps 1,000.</summary>
    private static ReportPrintPage<TRow> Print<TRow>(TableData<TRow> data)
    {
        var shaped = PrintLimit.Shape(data.Rows, data.TotalRows);
        var e = data.Envelope;
        return new ReportPrintPage<TRow>(
            e.DataLabel, e.LastSuccessfulSync, e.Currency, e.CurrencyLabel, e.DiscountBasis,
            e.Period, e.PreviousPeriod, e.Filters, e.Unavailable,
            shaped.TotalRows, shaped.Truncated, shaped.Limit,
            data.Totals, data.PreviousTotals, shaped.Rows,
            e.DataQuality);
    }

    private static string Dir(ReportFilter filter) => filter.Descending ? "desc" : "asc";

    internal static string? PercentText(decimal current, decimal? previous) =>
        previous is { } prev && ComparisonMath.Percent(current, prev) is { } percent ? ComparisonMath.Percent2(percent) : null;

    private static DocumentRow ReadDocumentRow(NpgsqlDataReader reader)
    {
        var docType = reader.GetString(reader.GetOrdinal("doc_type"));
        var date = reader.GetDateTime(reader.GetOrdinal("doc_date"));
        var jalali = JalaliDate.FromDateTime(date);
        var hasPreDiscount = HasColumn(reader, "pre_discount");
        return new DocumentRow(
            DocumentId: reader.GetInt64(reader.GetOrdinal("document_id")),
            Number: Text(reader, "doc_number") ?? string.Empty,
            DocType: docType,
            DocTypeLabel: DocTypeLabel(docType),
            Date: JalaliText(jalali),
            DateJalali: JalaliDate.ToPersianDigits($"{jalali.Year:0000}/{jalali.Month:00}/{jalali.Day:00}"),
            VendorKey: Text(reader, "vendor_key"),
            VendorName: Text(reader, "vendor_name") ?? UnknownLabel,
            PreDiscount: hasPreDiscount ? Amount(Dec(reader, "pre_discount")) : string.Empty,
            Discount: hasPreDiscount ? Amount(Dec(reader, "discount")) : string.Empty,
            Vat: hasPreDiscount ? Amount(Dec(reader, "vat")) : string.Empty,
            Net: hasPreDiscount ? Amount(Dec(reader, "net")) : string.Empty,
            SignedNet: hasPreDiscount ? Amount(Dec(reader, "signed_net")) : string.Empty,
            Quantity: hasPreDiscount ? Amount(Dec(reader, "quantity")) : string.Empty,
            LineCount: hasPreDiscount ? reader.GetInt64(reader.GetOrdinal("line_count")) : 0);
    }

    private static bool HasColumn(NpgsqlDataReader reader, string column)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), column, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string DocTypeLabel(string docType) => docType switch
    {
        "invoice" => "فاکتور خرید",
        "return" => "برگشت از خرید",
        _ => docType,
    };

    private static string Amount(decimal value) => ComparisonMath.Amount(value);

    internal static decimal Dec(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0m : reader.GetDecimal(ordinal);
    }

    internal static string? Text(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    /// <summary>The result of one table query before it is shaped as a table or a print response.</summary>
    private sealed record TableData<TRow>(
        ReportEnvelope Envelope,
        long TotalRows,
        int Page,
        string Sort,
        string Dir,
        ReportTotals Totals,
        ReportTotals? PreviousTotals,
        IReadOnlyList<TRow> Rows)
    {
        public static TableData<TRow> Empty(ReportEnvelope envelope, ReportFilter filter) => new(
            envelope, 0, 1, filter.Sort, filter.Descending ? "desc" : "asc", new ReportTotals(), null, Array.Empty<TRow>());
    }
}
