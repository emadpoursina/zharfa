namespace Zharfa.Api.VendorFinancials;

using System;
using System.Collections.Generic;
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
using Zharfa.Api.Purchasing.Reports;

/// <summary>
/// The vendor financials (specs/008-vendor-financials, research R5/R6): list, detail header, payment history
/// and outstanding instruments for one Jalali period, from the synced PostgreSQL copy only (never Mabna per
/// request). Availability comes from <see cref="VendorFinancialAvailability"/> plus the Phase 1 net rule; an
/// unavailable figure is null and listed in <c>unavailable[]</c> — zero rows with a usable formula is
/// "0.0000". When a figure is unavailable its function is never called (production does not touch
/// <c>mabna.fn_vendor_balance</c> while the formula is a candidate).
/// </summary>
public sealed class VendorFinancialsService(
    NpgsqlDataSource dataSource,
    VendorFinancialSql sql,
    DataModeOptions dataMode,
    IOptions<PurchasingOptions> purchasingOptions,
    IOptions<SyncOptions> syncOptions,
    IOptions<MabnaSyncOptions> mabnaSyncOptions,
    MabnaObjectPolicy policy,
    DataQualityNoticeService notices,
    TimeProvider clock)
{
    /// <summary>The sentence shown as demoNotice on every demo payload (quickstart B7).</summary>
    public const string DemoNotice = "این داده‌ها جنبه نمایشی (دمو) دارند و ارزش اسنادی ندارند.";

    private const string ComparisonFigure = "comparison";

    // ----- figures per surface (contracts/api.md §1–§4) -------------------------------------------------------

    private static readonly string[] ListFigures =
        [VendorFinancialAvailability.FigureVendorBalance, VendorFinancialAvailability.FigureOutstandingPayments];

    private static readonly string[] DetailFigures =
    [
        VendorFinancialAvailability.FigureVendorBalance,
        VendorFinancialAvailability.FigureOutstandingPayments,
        VendorFinancialAvailability.FigureNetPurchases,
        VendorFinancialAvailability.FigurePayments,
        VendorFinancialAvailability.FigurePurchasesMinusPayments,
    ];

    private static readonly string[] HistoryFigures = [VendorFinancialAvailability.FigurePayments];

    private static readonly string[] OutstandingFigures = [VendorFinancialAvailability.FigureOutstandingPayments];

    // ----- public entry points ------------------------------------------------------------------------------

    /// <summary>The Financials list (contracts/api.md §1).</summary>
    public async Task<VendorFinancialsSummaryResponse> SummaryAsync(VendorFinancialRequest request, CancellationToken ct) =>
        await Guard(async () =>
        {
            var context = await ResolveAsync(request, ListFigures, ReportKeys.VendorFinancials, vendorKey: null, ct);
            var totalRows = await sql.CountSummaryAsync(context.Scope, ct);
            var page = ReportFilter.EffectivePage(request.Page, request.PageSize, totalRows);
            var rows = await ReadSummaryRowsAsync(context, request, request.PageSize, (page - 1) * request.PageSize, ct);
            return Summary(request, context, totalRows, page, rows);
        });

    /// <summary>The print twin of the list: same rows, at most 1,000 (research R9).</summary>
    public async Task<VendorFinancialsPrintResponse> SummaryPrintAsync(VendorFinancialRequest request, CancellationToken ct) =>
        await Guard(async () =>
        {
            var context = await ResolveAsync(request, ListFigures, ReportKeys.VendorFinancials, vendorKey: null, ct);
            var totalRows = await sql.CountSummaryAsync(context.Scope, ct);
            var fetched = await ReadSummaryRowsAsync(context, request, PrintLimit.FetchLimit, 0, ct);
            var shaped = PrintLimit.Shape(fetched, totalRows);
            var printRows = shaped.Rows.Select(row => MapRow(context, request.Compare, row)).ToList();
            return new VendorFinancialsPrintResponse(
                context.DataLabel,
                context.DemoNotice,
                context.LastSync,
                VendorFinancialDtos.CurrencyCode,
                VendorFinancialDtos.CurrencyLabel,
                context.Period,
                context.PreviousPeriod,
                context.Filters,
                context.Unavailable,
                request.Compare,
                shaped.TotalRows,
                shaped.Truncated,
                shaped.Limit,
                Search(request),
                Sort(request),
                context.DataQuality,
                Available: true,
                printRows);
        });

    /// <summary>The detail header (contracts/api.md §2). Null = unknown vendor (the controller answers 404).</summary>
    public async Task<VendorFinancialDetailResponse?> DetailAsync(
        string vendorKey, VendorFinancialRequest request, CancellationToken ct) =>
        await Guard(async () =>
        {
            var context = await ResolveAsync(request, DetailFigures, ReportKeys.VendorFinancialsDetail, vendorKey, ct);
            var header = await sql.ReadVendorAsync(vendorKey, context.CodeUsable, ct);
            if (header is null)
            {
                return null;
            }

            var balanceUsable = context.Reason(VendorFinancialAvailability.FigureVendorBalance) is null;
            var outstandingUsable = context.Reason(VendorFinancialAvailability.FigureOutstandingPayments) is null;
            var netUsable = context.Reason(VendorFinancialAvailability.FigureNetPurchases) is null;
            var paymentsUsable = context.Reason(VendorFinancialAvailability.FigurePayments) is null;

            var data = await sql.ReadDetailAsync(
                vendorKey, context.Scope, balanceUsable, outstandingUsable, netUsable, paymentsUsable, ct);

            var compare = request.Compare && context.HasPrevious;
            var minus = netUsable && paymentsUsable && data.NetPurchases is { } net && data.Payments is { } pay
                ? ComparisonMath.Amount(ComparisonMath.Delta(net, pay))
                : null;

            var pvp = new VendorPurchasesVsPaymentsDto(
                NetPurchases: netUsable ? ComparisonMath.Amount(data.NetPurchases ?? 0m) : null,
                Payments: paymentsUsable ? ComparisonMath.Amount(data.Payments ?? 0m) : null,
                PurchasesMinusPayments: minus,
                PreviousNetPurchases: compare ? ComparisonMath.Amount(data.PreviousNetPurchases ?? 0m) : null,
                PreviousPayments: compare ? ComparisonMath.Amount(data.PreviousPayments ?? 0m) : null,
                DeltaNetPurchases: compare && data.PreviousNetPurchases is { } pn
                    ? ComparisonMath.Amount(ComparisonMath.Delta(data.NetPurchases ?? 0m, pn)) : null,
                PercentNetPurchases: compare && data.PreviousNetPurchases is { } pp
                    ? PurchasingReportService.PercentText(data.NetPurchases ?? 0m, pp) : null,
                DeltaPayments: compare && data.PreviousPayments is { } dp
                    ? ComparisonMath.Amount(ComparisonMath.Delta(data.Payments ?? 0m, dp)) : null,
                PercentPayments: compare && data.PreviousPayments is { } dpp
                    ? PurchasingReportService.PercentText(data.Payments ?? 0m, dpp) : null);

            return new VendorFinancialDetailResponse(
                context.DataLabel,
                context.DemoNotice,
                context.LastSync,
                VendorFinancialDtos.CurrencyCode,
                VendorFinancialDtos.CurrencyLabel,
                context.Period,
                context.PreviousPeriod,
                new VendorFinancialVendorDto(header.Key ?? vendorKey, header.Name ?? string.Empty, header.Code),
                context.Unavailable,
                context.DataQuality,
                Available: true,
                VendorBalance: balanceUsable ? ComparisonMath.Amount(data.VendorBalance ?? 0m) : null,
                OutstandingPayments: outstandingUsable ? ComparisonMath.Amount(data.OutstandingPayments ?? 0m) : null,
                PurchasesVsPayments: pvp);
        });

    /// <summary>Payment history (contracts/api.md §3). Empty rows when the payments figure is unavailable.</summary>
    public async Task<VendorFinancialListResponse<PaymentHistoryRow>> HistoryAsync(
        string vendorKey, VendorFinancialRequest request, CancellationToken ct) =>
        await Guard(async () =>
        {
            var context = await ResolveAsync(request, HistoryFigures, ReportKeys.VendorFinancialsHistory, vendorKey, ct);
            var header = await sql.ReadVendorAsync(vendorKey, context.CodeUsable, ct);
            if (header is null)
            {
                throw new VendorFinancialVendorNotFoundException();
            }

            var paymentsUsable = context.Reason(VendorFinancialAvailability.FigurePayments) is null;
            long totalRows = 0;
            var rows = new List<PaymentHistoryRow>();
            if (paymentsUsable)
            {
                var (count, _) = await sql.HistoryTotalsAsync(vendorKey, context.Scope.From, context.Scope.To, ct);
                totalRows = count;
                var page = ReportFilter.EffectivePage(request.Page, request.PageSize, count);
                var data = await sql.ReadHistoryAsync(
                    vendorKey, context.Scope.From, context.Scope.To,
                    request.Sort, request.Descending, request.PageSize, (page - 1) * request.PageSize, ct);
                rows = data.Select(r => new PaymentHistoryRow(
                    JalaliText(r.PaidAt),
                    r.Reference,
                    r.InstrumentType,
                    VendorFinancialLabels.InstrumentTypeLabel(r.InstrumentType),
                    ComparisonMath.Amount(r.Amount))).ToList();
            }

            return DocumentList(request, context, totalRows, rows);
        });

    /// <summary>Outstanding instruments (contracts/api.md §4), as of the period end.</summary>
    public async Task<VendorFinancialListResponse<OutstandingInstrumentRow>> OutstandingAsync(
        string vendorKey, VendorFinancialRequest request, CancellationToken ct) =>
        await Guard(async () =>
        {
            var context = await ResolveAsync(request, OutstandingFigures, ReportKeys.VendorFinancialsOutstanding, vendorKey, ct);
            var header = await sql.ReadVendorAsync(vendorKey, context.CodeUsable, ct);
            if (header is null)
            {
                throw new VendorFinancialVendorNotFoundException();
            }

            var usable = context.Reason(VendorFinancialAvailability.FigureOutstandingPayments) is null;
            long totalRows = 0;
            var rows = new List<OutstandingInstrumentRow>();
            if (usable)
            {
                var (count, _) = await sql.OutstandingTotalsAsync(vendorKey, context.Scope.AsOf, ct);
                totalRows = count;
                var page = ReportFilter.EffectivePage(request.Page, request.PageSize, count);
                var data = await sql.ReadOutstandingAsync(
                    vendorKey, context.Scope.AsOf,
                    request.Sort, request.Descending, request.PageSize, (page - 1) * request.PageSize, ct);
                rows = data.Select(r => new OutstandingInstrumentRow(
                    JalaliText(r.Date),
                    r.Reference,
                    r.InstrumentType,
                    VendorFinancialLabels.InstrumentTypeLabel(r.InstrumentType),
                    ComparisonMath.Amount(r.UnpaidAmount))).ToList();
            }

            return DocumentList(request, context, totalRows, rows);
        });

    /// <summary>Finds the vendor whose name appears inside a question text (research R9).</summary>
    public Task<FinancialVendorHeader?> FindVendorInTextAsync(string? text, CancellationToken ct) =>
        sql.FindVendorInTextAsync(text, ct);

    // ----- export bundles (contracts/xlsx.md; T055) -----------------------------------------------------------

    /// <summary>Everything an Excel twin needs besides the export time: headers, rows, meta (no amounts in audit).</summary>
    public sealed record ExportBundle(
        string ReportName,
        string PeriodLabel,
        IReadOnlyList<VendorFinancialFilterEcho> Filters,
        string? LastSyncJalali,
        string DataLabel,
        DataQualityNotice? DataQuality,
        IReadOnlyList<string> Headers,
        IReadOnlyList<IReadOnlyList<string>> Rows,
        long TotalRows);

    /// <summary>Financials list export: up to 10,000 rows (the Excel cap), Persian headers, compare columns when on.</summary>
    public async Task<ExportBundle> SummaryExportAsync(VendorFinancialRequest request, CancellationToken ct) =>
        await Guard(async () =>
        {
            var context = await ResolveAsync(request, ListFigures, ReportKeys.VendorFinancials, vendorKey: null, ct);
            var totalRows = await sql.CountSummaryAsync(context.Scope, ct);
            var data = await ReadSummaryRowsAsync(context, request, ExportRowCap, 0, ct);
            var rows = data.Select(row => MapRow(context, request.Compare, row)).ToList();

            var headers = new List<string> { "نام", "کد", "مانده تأمین‌کننده", "پرداخت‌های معوق" };
            if (request.Compare && context.HasPrevious)
            {
                headers.AddRange(["مانده دوره قبل", "تفاوت مانده", "درصد تغییر مانده", "معوق دوره قبل", "تفاوت معوق", "درصد تغییر معوق"]);
            }

            var values = rows.Select(row =>
            {
                var line = new List<string> { row.Name, row.Code ?? "-", row.VendorBalance ?? "-", row.OutstandingPayments ?? "-" };
                if (headers.Count > 4)
                {
                    line.AddRange([row.PreviousVendorBalance ?? "-", row.DeltaVendorBalance ?? "-", row.PercentVendorBalance ?? "-",
                        row.PreviousOutstandingPayments ?? "-", row.DeltaOutstanding ?? "-", row.PercentOutstanding ?? "-"]);
                }

                return (IReadOnlyList<string>)line;
            }).ToList();

            return new ExportBundle(
                "اطلاعات مالی تأمین‌کنندگان",
                context.Period.Label,
                context.Filters,
                context.LastSync?.AtJalali,
                context.DataLabel,
                context.DataQuality,
                headers,
                values,
                totalRows);
        });

    /// <summary>Payment history export (Persian headers of contracts/xlsx.md).</summary>
    public async Task<ExportBundle> HistoryExportAsync(string vendorKey, VendorFinancialRequest request, CancellationToken ct) =>
        await Guard(async () =>
        {
            var context = await ResolveAsync(request, HistoryFigures, ReportKeys.VendorFinancialsHistory, vendorKey, ct);
            var header = await sql.ReadVendorAsync(vendorKey, context.CodeUsable, ct)
                         ?? throw new VendorFinancialVendorNotFoundException();
            var usable = context.Reason(VendorFinancialAvailability.FigurePayments) is null;
            long total = 0;
            var values = new List<IReadOnlyList<string>>();
            if (usable)
            {
                var (count, _) = await sql.HistoryTotalsAsync(vendorKey, context.Scope.From, context.Scope.To, ct);
                total = count;
                var data = await sql.ReadHistoryAsync(
                    vendorKey, context.Scope.From, context.Scope.To,
                    request.Sort, request.Descending, ExportRowCap, 0, ct);
                values = data.Select(r => (IReadOnlyList<string>)new[]
                {
                    JalaliText(r.PaidAt),
                    r.Reference ?? "-",
                    VendorFinancialLabels.InstrumentTypeLabel(r.InstrumentType),
                    ComparisonMath.Amount(r.Amount),
                }).ToList();
            }

            return new ExportBundle(
                $"تاریخچه پرداخت‌ها - {header.Name ?? vendorKey}",
                context.Period.Label,
                context.Filters,
                context.LastSync?.AtJalali,
                context.DataLabel,
                context.DataQuality,
                ["تاریخ پرداخت", "مرجع", "نوع سند", "مبلغ"],
                values,
                total);
        });

    /// <summary>Outstanding instruments export.</summary>
    public async Task<ExportBundle> OutstandingExportAsync(string vendorKey, VendorFinancialRequest request, CancellationToken ct) =>
        await Guard(async () =>
        {
            var context = await ResolveAsync(request, OutstandingFigures, ReportKeys.VendorFinancialsOutstanding, vendorKey, ct);
            var header = await sql.ReadVendorAsync(vendorKey, context.CodeUsable, ct)
                         ?? throw new VendorFinancialVendorNotFoundException();
            var usable = context.Reason(VendorFinancialAvailability.FigureOutstandingPayments) is null;
            long total = 0;
            var values = new List<IReadOnlyList<string>>();
            if (usable)
            {
                var (count, _) = await sql.OutstandingTotalsAsync(vendorKey, context.Scope.AsOf, ct);
                total = count;
                var data = await sql.ReadOutstandingAsync(
                    vendorKey, context.Scope.AsOf,
                    request.Sort, request.Descending, ExportRowCap, 0, ct);
                values = data.Select(r => (IReadOnlyList<string>)new[]
                {
                    JalaliText(r.Date),
                    r.Reference ?? "-",
                    VendorFinancialLabels.InstrumentTypeLabel(r.InstrumentType),
                    ComparisonMath.Amount(r.UnpaidAmount),
                }).ToList();
            }

            return new ExportBundle(
                $"پرداخت‌های معوق - {header.Name ?? vendorKey}",
                context.Period.Label,
                context.Filters,
                context.LastSync?.AtJalali,
                context.DataLabel,
                context.DataQuality,
                ["تاریخ", "مرجع", "نوع سند", "مبلغ معوق"],
                values,
                total);
        });

    /// <summary>Purchases vs payments export: one row (two with compare) of the four separately labeled figures.</summary>
    public async Task<ExportBundle> PurchasesVsPaymentsExportAsync(string vendorKey, VendorFinancialRequest request, CancellationToken ct) =>
        await Guard(async () =>
        {
            var context = await ResolveAsync(request, DetailFigures, ReportKeys.VendorFinancialsDetail, vendorKey, ct);
            var header = await sql.ReadVendorAsync(vendorKey, context.CodeUsable, ct)
                         ?? throw new VendorFinancialVendorNotFoundException();
            var balanceUsable = context.Reason(VendorFinancialAvailability.FigureVendorBalance) is null;
            var outstandingUsable = context.Reason(VendorFinancialAvailability.FigureOutstandingPayments) is null;
            var netUsable = context.Reason(VendorFinancialAvailability.FigureNetPurchases) is null;
            var paymentsUsable = context.Reason(VendorFinancialAvailability.FigurePayments) is null;
            var data = await sql.ReadDetailAsync(
                vendorKey, context.Scope, balanceUsable, outstandingUsable, netUsable, paymentsUsable, ct);

            var headers = new List<string> { "خالص خرید", "پرداخت‌ها", "خرید منهای پرداخت" };
            var values = new List<IReadOnlyList<string>>
            {
                new[]
                {
                    netUsable ? ComparisonMath.Amount(data.NetPurchases ?? 0m) : "-",
                    paymentsUsable ? ComparisonMath.Amount(data.Payments ?? 0m) : "-",
                    netUsable && paymentsUsable
                        ? ComparisonMath.Amount(ComparisonMath.Delta(data.NetPurchases ?? 0m, data.Payments ?? 0m))
                        : "-",
                },
            };
            if (request.Compare && context.HasPrevious)
            {
                values.Add(new[]
                {
                    data.PreviousNetPurchases is { } pn ? ComparisonMath.Amount(pn) : "-",
                    data.PreviousPayments is { } pp ? ComparisonMath.Amount(pp) : "-",
                    data.PreviousNetPurchases is { } pnn && data.PreviousPayments is { } ppp
                        ? ComparisonMath.Amount(ComparisonMath.Delta(pnn, ppp)) : "-",
                });
            }

            return new ExportBundle(
                $"خرید و پرداخت‌ها - {header.Name ?? vendorKey}",
                context.Period.Label,
                context.Filters,
                context.LastSync?.AtJalali,
                context.DataLabel,
                context.DataQuality,
                headers,
                values,
                values.Count);
        });

    /// <summary>Excel row cap (contracts/xlsx.md): 10,000 rows.</summary>
    public const int ExportRowCap = 10000;

    // ----- shaping ------------------------------------------------------------------------------------------

    private VendorFinancialsSummaryResponse Summary(
        VendorFinancialRequest request, FinancialContext context, long totalRows, int page,
        IReadOnlyList<FinancialSummaryRowData> data)
    {
        var rows = data.Select(row => MapRow(context, request.Compare, row)).ToList();

        return new VendorFinancialsSummaryResponse(
            context.DataLabel,
            context.DemoNotice,
            context.LastSync,
            VendorFinancialDtos.CurrencyCode,
            VendorFinancialDtos.CurrencyLabel,
            context.Period,
            context.PreviousPeriod,
            context.Filters,
            context.Unavailable,
            request.Compare,
            page,
            request.PageSize,
            totalRows,
            Search(request),
            Sort(request),
            context.DataQuality,
            Available: true,
            rows);
    }

    /// <summary>One list row: unavailable columns are null, previous/delta/percent only with a previous base.</summary>
    private VendorFinancialSummaryRow MapRow(FinancialContext context, bool compareFlag, FinancialSummaryRowData row)
    {
        var balanceUsable = context.Reason(VendorFinancialAvailability.FigureVendorBalance) is null;
        var outstandingUsable = context.Reason(VendorFinancialAvailability.FigureOutstandingPayments) is null;
        var compare = compareFlag && context.HasPrevious;

        return new VendorFinancialSummaryRow(
            row.Key,
            row.Name,
            row.Code,
            balanceUsable && row.VendorBalance is { } balance ? ComparisonMath.Amount(balance) : null,
            outstandingUsable && row.OutstandingPayments is { } outstanding ? ComparisonMath.Amount(outstanding) : null,
            compare && balanceUsable && row.PreviousVendorBalance is { } prevBalance ? ComparisonMath.Amount(prevBalance) : null,
            compare && outstandingUsable && row.PreviousOutstandingPayments is { } prevOutstanding ? ComparisonMath.Amount(prevOutstanding) : null,
            compare && balanceUsable && row.VendorBalance is { } b && row.PreviousVendorBalance is { } pb
                ? ComparisonMath.Amount(ComparisonMath.Delta(b, pb)) : null,
            compare && balanceUsable && row.VendorBalance is { } bp && row.PreviousVendorBalance is { } bpp
                ? PurchasingReportService.PercentText(bp, bpp) : null,
            compare && outstandingUsable && row.OutstandingPayments is { } o && row.PreviousOutstandingPayments is { } po
                ? ComparisonMath.Amount(ComparisonMath.Delta(o, po)) : null,
            compare && outstandingUsable && row.OutstandingPayments is { } op && row.PreviousOutstandingPayments is { } pop
                ? PurchasingReportService.PercentText(op, pop) : null);
    }

    private VendorFinancialListResponse<T> DocumentList<T>(
        VendorFinancialRequest request, FinancialContext context, long totalRows, IReadOnlyList<T> rows) =>
        new(
            context.DataLabel,
            context.DemoNotice,
            context.LastSync,
            VendorFinancialDtos.CurrencyCode,
            VendorFinancialDtos.CurrencyLabel,
            context.Period,
            context.Filters,
            context.Unavailable,
            ReportFilter.EffectivePage(request.Page, request.PageSize, totalRows),
            request.PageSize,
            totalRows,
            request.Sort,
            request.Descending ? "desc" : "asc",
            context.DataQuality,
            Available: true,
            rows);

    private async Task<IReadOnlyList<FinancialSummaryRowData>> ReadSummaryRowsAsync(
        FinancialContext context, VendorFinancialRequest request, int limit, int offset, CancellationToken ct)
    {
        var balanceUsable = context.Reason(VendorFinancialAvailability.FigureVendorBalance) is null;
        var outstandingUsable = context.Reason(VendorFinancialAvailability.FigureOutstandingPayments) is null;
        return await sql.ReadSummaryAsync(
            context.Scope, balanceUsable, outstandingUsable,
            request.Sort, request.Descending, limit, offset, ct);
    }

    private static VendorFinancialSearchDto Search(VendorFinancialRequest request) =>
        new(request.Search ?? string.Empty, request.NormalizedSearch);

    private static VendorFinancialSortDto Sort(VendorFinancialRequest request) =>
        new(request.Sort, request.Descending ? "desc" : "asc");

    private static string JalaliText(DateOnly date)
    {
        var jalali = JalaliDate.FromDate(date);
        return $"{jalali.Year:0000}-{jalali.Month:00}-{jalali.Day:00}";
    }

    // ----- shared context -----------------------------------------------------------------------------------

    private sealed record FinancialContext(
        string DataLabel,
        string? DemoNotice,
        ReportSyncTime? LastSync,
        bool CodeUsable,
        VendorFinancialScope Scope,
        VendorFinancialPeriodDto Period,
        VendorFinancialPeriodDto? PreviousPeriod,
        bool HasPrevious,
        IReadOnlyList<VendorFinancialUnavailable> Unavailable,
        IReadOnlyList<VendorFinancialFilterEcho> Filters,
        DataQualityNotice? DataQuality,
        IReadOnlyDictionary<string, string?> Reasons)
    {
        public string? Reason(string figure) =>
            Reasons.TryGetValue(figure, out var reason) ? reason : null;
    }

    private async Task<FinancialContext> ResolveAsync(
        VendorFinancialRequest request, IReadOnlyList<string> figures, string reportKey, string? vendorKey, CancellationToken ct)
    {
        var zone = SyncOptions.ResolveTimeZone(syncOptions.Value.TimeZone);
        var basis = purchasingOptions.Value.DiscountBasis;
        var lastSynced = await sql.ReadLastSucceededAtAsync(ct);
        var map = lastSynced is null ? null : LoadStructurallyValidMap();
        var hasSync = lastSynced is not null;

        var financial = VendorFinancialAvailability.Evaluate(map, dataMode.IsProduction, hasSync);
        var purchasing = MetricAvailability.Evaluate(map, dataMode.IsProduction, hasSync, basis);

        // netPurchases keeps the Phase 1 rule (unchanged) when it refuses the figure.
        var netReason = purchasing.TryGetValue(MetricAvailability.FigureNet, out var net) && net is not null
            ? net
            : financial.TryGetValue(VendorFinancialAvailability.FigureNetPurchases, out var finNet) ? finNet : null;
        var paymentsReason = financial[VendorFinancialAvailability.FigurePayments];
        var reasons = new Dictionary<string, string?>(financial, StringComparer.Ordinal)
        {
            [VendorFinancialAvailability.FigureNetPurchases] = netReason,
            [VendorFinancialAvailability.FigurePurchasesMinusPayments] = First(netReason, paymentsReason),
        };

        VendorFinancialPeriodDto? previousPeriod = null;
        var hasPrevious = false;
        var unavailable = new List<VendorFinancialUnavailable>();
        if (request.Compare)
        {
            var firstData = await ReadFirstDataMonthAsync(ct);
            hasPrevious = VendorFinancialPeriod.HasPreviousBase(request.Period, firstData);
            previousPeriod = VendorFinancialPeriod.ToDto(request.Period.Previous());
            if (!hasPrevious)
            {
                unavailable.Add(VendorFinancialUnavailable.For(ComparisonFigure, VendorFinancialAvailability.NoPreviousData));
            }
        }

        foreach (var figure in figures)
        {
            var reason = reasons.TryGetValue(figure, out var value) ? value : null;
            if (reason is not null)
            {
                unavailable.Add(VendorFinancialUnavailable.For(figure, reason));
            }
        }

        var filters = new List<VendorFinancialFilterEcho>();
        if (request.Search is { } search)
        {
            filters.Add(new VendorFinancialFilterEcho("q", search, search));
        }
        else if (vendorKey is not null && reportKey != ReportKeys.VendorFinancials)
        {
            var header = await sql.ReadVendorAsync(vendorKey, CodeUsable(purchasing), ct);
            if (header?.Name is { } name)
            {
                filters.Add(new VendorFinancialFilterEcho("vendor", vendorKey, name));
            }
        }

        var scope = BuildScope(request, hasPrevious, basis, CodeUsable(purchasing));
        DataQualityNotice? notice = null;
        if (lastSynced is not null)
        {
            notice = await notices.ForFinancialScopeAsync(reportKey, vendorKey, scope.FromKey, scope.ToKey, ct);
        }

        return new FinancialContext(
            DataLabel: dataMode.DataLabel,
            DemoNotice: dataMode.IsDemo ? DemoNotice : null,
            LastSync: lastSynced is { } utc ? new ReportSyncTime(SyncTimeText.Iso(utc, zone), SyncTimeText.Jalali(utc, zone)) : null,
            CodeUsable: CodeUsable(purchasing),
            Scope: scope,
            Period: VendorFinancialPeriod.ToDto(request.Period),
            PreviousPeriod: previousPeriod,
            HasPrevious: hasPrevious,
            Unavailable: unavailable,
            Filters: filters,
            DataQuality: notice,
            Reasons: reasons);
    }

    private static VendorFinancialScope BuildScope(VendorFinancialRequest request, bool hasPrevious, string basis, bool codeUsable)
    {
        var period = request.Period;
        var previous = period.Previous();
        return new VendorFinancialScope(
            From: VendorFinancialPeriod.From(period),
            To: VendorFinancialPeriod.To(period),
            AsOf: VendorFinancialPeriod.AsOf(period),
            FromKey: period.FromKey,
            ToKey: period.ToKey,
            HasPrevious: hasPrevious,
            PreviousFrom: VendorFinancialPeriod.From(previous),
            PreviousTo: VendorFinancialPeriod.To(previous),
            PreviousAsOf: VendorFinancialPeriod.AsOf(previous),
            PreviousFromKey: previous.FromKey,
            PreviousToKey: previous.ToKey,
            Search: request.Search is null ? null : ReportFilter.EscapeLike(request.Search),
            CodeUsable: codeUsable,
            Basis: basis);
    }

    private static bool CodeUsable(IReadOnlyDictionary<string, string?> purchasing) =>
        !purchasing.TryGetValue(MetricAvailability.FigureVendorCode, out var reason) || reason is null;

    private async Task<JalaliMonth?> ReadFirstDataMonthAsync(CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("SELECT jalali_year, jalali_month FROM mabna.fn_first_data_month()");
        cmd.CommandTimeout = VendorFinancialSql.CommandTimeoutSeconds;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new JalaliMonth(reader.GetInt16(0), reader.GetInt16(1));
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

    private static string? First(params string?[] reasons) =>
        reasons.FirstOrDefault(reason => reason is not null);

    /// <summary>Maps a 15 s timeout to the shared 503 message (the controller handles the type).</summary>
    private static async Task<T> Guard<T>(Func<Task<T>> action)
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
}

/// <summary>The vendor key is not in the purchasing master: the controller answers 404 «تأمین‌کننده یافت نشد.».</summary>
public sealed class VendorFinancialVendorNotFoundException() : Exception("تأمین‌کننده یافت نشد.");
