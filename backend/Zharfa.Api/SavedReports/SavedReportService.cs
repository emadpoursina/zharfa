namespace Zharfa.Api.SavedReports;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zharfa.Api.Ai;
using Zharfa.Api.Analytics;
using Zharfa.Api.Audit;
using Zharfa.Api.Controllers;
using Zharfa.Api.Data;
using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.Platform;
using Zharfa.Api.Purchasing;
using Zharfa.Api.Purchasing.Reports;
using Zharfa.Api.Security;
using Zharfa.Api.VendorFinancials;

public sealed record SaveReportRequest(
    string Name,
    string Kind,
    JsonElement Period,
    JsonElement? Filters = null,
    JsonElement? Generated = null,
    bool Overwrite = false);

public sealed record SavedReportItemDto(
    long Id,
    string Name,
    string Kind,
    string KindLabel,
    string RequiredPermission,
    object Period,
    IReadOnlyList<object> Filters,
    string UpdatedAtJalali);

public sealed record SavedReportListEnvelope(
    string DataLabel,
    int Max,
    IReadOnlyList<SavedReportItemDto> Items);

public sealed record SaveReportResult(
    bool Success,
    int StatusCode,
    SavedReportItemDto? Item = null,
    string? Message = null,
    string? Code = null,
    long? Id = null,
    bool IsReplaced = false);

public sealed record OpenReportResult(
    bool Success,
    int StatusCode,
    object? Body = null,
    string? ErrorMessage = null,
    string? MissingPermission = null);

/// <summary>
/// Service managing saved report specifications and their re-execution (spec 007 FR-007..FR-013).
/// Stores only the query specification, never result rows.
/// Reopening always runs on current synced data.
/// </summary>
public class SavedReportService(
    ZharfaDbContext db,
    PurchasingReportService reportService,
    VendorDirectoryService vendorDirectoryService,
    VendorComparisonService vendorComparisonService,
    PurchasingAnalystTools analystTools,
    DataModeOptions dataMode,
    IOptions<SyncOptions> syncOptions,
    ILogger<SavedReportService> logger,
    VendorFinancialsService? financialsService = null,
    VendorFinancialAnalystTools? financialAnalystTools = null)
{
    private static readonly HashSet<string> AllowedKinds = new(StringComparer.Ordinal)
    {
        "by-vendor",
        "by-product",
        "by-category",
        "trend",
        "top-products",
        "returns",
        "transactions",
        "vendors",
        "vendors-compare",
        "drill-down",
        "vendor-profile",
        "generated",
        "vendor-financials",
        "vendor-financials-history",
        "vendor-financials-outstanding",
        "vendor-financials-purchases-vs-payments"
    };

    private static readonly HashSet<string> FinancialKinds = new(StringComparer.Ordinal)
    {
        "vendor-financials",
        "vendor-financials-history",
        "vendor-financials-outstanding",
        "vendor-financials-purchases-vs-payments"
    };

    public async Task<SavedReportListEnvelope> ListAsync(long userId, CancellationToken ct)
    {
        var zone = SyncOptions.ResolveTimeZone(syncOptions.Value.TimeZone);
        var reports = await db.SavedReports
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync(ct);

        var items = reports.Select(r => ToDto(r, zone)).ToList();

        return new SavedReportListEnvelope(
            DataLabel: dataMode.DataLabel,
            Max: SavedReportName.MaxPerUser,
            Items: items);
    }

    public async Task<SavedReportItemDto?> GetByIdAsync(long id, long userId, CancellationToken ct)
    {
        var zone = SyncOptions.ResolveTimeZone(syncOptions.Value.TimeZone);
        var report = await db.SavedReports
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);

        return report == null ? null : ToDto(report, zone);
    }

    public Task<SavedReport?> GetEntityByIdAsync(long id, long userId, CancellationToken ct) =>
        db.SavedReports.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);

    public JalaliDate Today()
    {
        var zone = SyncOptions.ResolveTimeZone(syncOptions.Value.TimeZone);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        return JalaliDate.FromDateTime(local);
    }

    private string NowJalali()
    {
        var zone = SyncOptions.ResolveTimeZone(syncOptions.Value.TimeZone);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var jalali = JalaliDate.FromDateTime(local);
        return JalaliDate.ToPersianDigits($"{jalali.Year:D4}/{jalali.Month:D2}/{jalali.Day:D2} {local.Hour:D2}:{local.Minute:D2}");
    }

    public ExportTable BuildExportTableFromOpen(SavedReport report, OpenReportResult openResult)
    {
        var nowJalali = NowJalali();
        var today = Today();
        var period = ResolvePeriod(report.PeriodJson, today);

        if (report.Kind == "generated")
        {
            var body = openResult.Body;
            VisualDto? visual = null;
            if (body != null)
            {
                var visualProp = body.GetType().GetProperty("visual")?.GetValue(body);
                visual = visualProp as VisualDto;
            }

            var headers = visual?.Table.Columns.Select(c => c.Header).ToList() ?? new List<string> { "عنوان", "مقدار" };
            var rows = visual?.Table.Rows
                .Select(r => (IReadOnlyList<string>)visual.Table.Columns.Select(c => r.GetValueOrDefault(c.Id) ?? "").ToList())
                .ToList() ?? new List<IReadOnlyList<string>>();

            return new ExportTable(
                ReportName: report.Name,
                PeriodLabel: visual?.Captions.Period ?? period.Label,
                FiltersLabel: visual?.Captions.Filters ?? "بدون فیلتر",
                ExportedAtJalali: nowJalali,
                LastSyncJalali: null,
                DataLabel: dataMode.DataLabel,
                DataQualityText: visual?.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {visual.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
                Headers: headers,
                Rows: rows,
                TotalRows: rows.Count);
        }

        object? data = null;
        if (openResult.Body != null)
        {
            data = openResult.Body.GetType().GetProperty("data")?.GetValue(openResult.Body);
        }

        if (data is ReportTablePage<GroupedRow> g)
        {
            var headers = new List<string>
            {
                report.Kind == "by-vendor" ? "تأمین‌کننده" : (report.Kind == "by-category" ? "گروه کالا" : "کالا"),
                "خالص خرید", "ناخالص", "برگشت", "ارزش افزوده", "تعداد اسناد"
            };

            var rows = g.Rows.Select(r => (IReadOnlyList<string>)new[]
            {
                r.Name,
                r.Net ?? "0",
                r.Gross ?? "0",
                r.Returns ?? "0",
                r.Vat ?? "0",
                r.DocumentCount.ToString()
            }).ToList();

            return new ExportTable(
                ReportName: report.Name,
                PeriodLabel: period.Label,
                FiltersLabel: g.Filters != null && g.Filters.Count > 0 ? string.Join("، ", g.Filters.Select(f => $"{f.Name}: {f.Label}")) : "بدون فیلتر",
                ExportedAtJalali: nowJalali,
                LastSyncJalali: g.LastSuccessfulSync?.AtJalali,
                DataLabel: dataMode.DataLabel,
                DataQualityText: g.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {g.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
                Headers: headers,
                Rows: rows,
                TotalRows: g.TotalRows);
        }

        if (data is ReportTablePage<ReturnsRow> ret)
        {
            var headers = new List<string> { "کالا", "تأمین‌کننده", "مبلغ برگشت", "تعداد" };
            var rows = ret.Rows.Select(r => (IReadOnlyList<string>)new[]
            {
                r.ProductName,
                r.VendorName,
                r.Returns ?? "0",
                r.Quantity ?? "0"
            }).ToList();

            return new ExportTable(
                ReportName: report.Name,
                PeriodLabel: period.Label,
                FiltersLabel: ret.Filters != null && ret.Filters.Count > 0 ? string.Join("، ", ret.Filters.Select(f => $"{f.Name}: {f.Label}")) : "بدون فیلتر",
                ExportedAtJalali: nowJalali,
                LastSyncJalali: ret.LastSuccessfulSync?.AtJalali,
                DataLabel: dataMode.DataLabel,
                DataQualityText: ret.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {ret.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
                Headers: headers,
                Rows: rows,
                TotalRows: ret.TotalRows);
        }

        if (data is ReportTablePage<TrendRow> tr)
        {
            var headers = new List<string> { "ماه", "خالص خرید", "تعداد خالص" };
            var rows = tr.Rows.Select(r => (IReadOnlyList<string>)new[]
            {
                r.Label,
                r.Net ?? "0",
                r.NetQuantity ?? "0"
            }).ToList();

            return new ExportTable(
                ReportName: report.Name,
                PeriodLabel: period.Label,
                FiltersLabel: tr.Filters != null && tr.Filters.Count > 0 ? string.Join("، ", tr.Filters.Select(f => $"{f.Name}: {f.Label}")) : "بدون فیلتر",
                ExportedAtJalali: nowJalali,
                LastSyncJalali: tr.LastSuccessfulSync?.AtJalali,
                DataLabel: dataMode.DataLabel,
                DataQualityText: tr.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {tr.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
                Headers: headers,
                Rows: rows,
                TotalRows: tr.TotalRows);
        }

        if (data is ReportTablePage<DocumentRow> docs)
        {
            var headers = new List<string> { "شماره سند", "تاریخ", "نوع سند", "تأمین‌کننده", "خالص خرید", "تعداد" };
            var rows = docs.Rows.Select(r => (IReadOnlyList<string>)new[]
            {
                r.Number,
                r.DateJalali,
                r.DocTypeLabel,
                r.VendorName,
                r.SignedNet ?? "0",
                r.Quantity ?? "0"
            }).ToList();

            return new ExportTable(
                ReportName: report.Name,
                PeriodLabel: period.Label,
                FiltersLabel: docs.Filters != null && docs.Filters.Count > 0 ? string.Join("، ", docs.Filters.Select(f => $"{f.Name}: {f.Label}")) : "بدون فیلتر",
                ExportedAtJalali: nowJalali,
                LastSyncJalali: docs.LastSuccessfulSync?.AtJalali,
                DataLabel: dataMode.DataLabel,
                DataQualityText: docs.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {docs.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
                Headers: headers,
                Rows: rows,
                TotalRows: docs.TotalRows);
        }

        if (data is ReportTablePage<VendorRow> vends)
        {
            var headers = new List<string> { "نام تأمین‌کننده", "کد", "خالص خرید", "آخرین خرید" };
            var rows = vends.Rows.Select(r => (IReadOnlyList<string>)new[]
            {
                r.Name,
                r.Code ?? "-",
                r.Net ?? "0",
                r.LastPurchaseDateJalali ?? "-"
            }).ToList();

            return new ExportTable(
                ReportName: report.Name,
                PeriodLabel: period.Label,
                FiltersLabel: vends.Filters != null && vends.Filters.Count > 0 ? string.Join("، ", vends.Filters.Select(f => $"{f.Name}: {f.Label}")) : "بدون فیلتر",
                ExportedAtJalali: nowJalali,
                LastSyncJalali: vends.LastSuccessfulSync?.AtJalali,
                DataLabel: dataMode.DataLabel,
                DataQualityText: vends.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {vends.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
                Headers: headers,
                Rows: rows,
                TotalRows: vends.TotalRows);
        }

        if (data is VendorComparisonResponse cmp)
        {
            var headers = new List<string> { "تأمین‌کننده", "خالص خرید", "تعداد خالص", "تعداد اسناد", "برگشت از خرید" };
            var rows = cmp.Vendors.Select(v => (IReadOnlyList<string>)new[]
            {
                (v.Name ?? "") + (string.IsNullOrEmpty(v.Code) ? "" : $" ({v.Code})"),
                v.Net ?? "0",
                v.NetQuantity ?? "0",
                v.DocumentCount?.ToString() ?? "0",
                v.Returns ?? "0"
            }).ToList();

            return new ExportTable(
                ReportName: report.Name,
                PeriodLabel: period.Label,
                FiltersLabel: cmp.Vendors != null && cmp.Vendors.Count > 0 ? $"تأمین‌کنندگان: {string.Join("، ", cmp.Vendors.Select(v => v.Name ?? ""))}" : "بدون فیلتر",
                ExportedAtJalali: nowJalali,
                LastSyncJalali: cmp.LastSuccessfulSync?.AtJalali,
                DataLabel: dataMode.DataLabel,
                DataQualityText: cmp.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {cmp.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
                Headers: headers,
                Rows: rows,
                TotalRows: cmp.Vendors?.Count ?? 0);
        }

        if (data is VendorProfileResponse prof)
        {
            string? profileTable = null;
            try
            {
                using var fDoc = JsonDocument.Parse(report.FiltersJson);
                if (fDoc.RootElement.TryGetProperty("profileTable", out var ptProp) && ptProp.ValueKind == JsonValueKind.String)
                {
                    profileTable = ptProp.GetString();
                }
            }
            catch { }

            if (profileTable == "top-products")
            {
                var headers = new List<string> { "کالا", "خالص خرید" };
                var rows = prof.TopProducts.Select(p => (IReadOnlyList<string>)new[] { p.Name, p.Net ?? "0" }).ToList();
                return new ExportTable(
                    ReportName: report.Name,
                    PeriodLabel: period.Label,
                    FiltersLabel: $"تأمین‌کننده: {prof.Vendor.Name}",
                    ExportedAtJalali: nowJalali,
                    LastSyncJalali: prof.LastSuccessfulSync?.AtJalali,
                    DataLabel: dataMode.DataLabel,
                    DataQualityText: null,
                    Headers: headers,
                    Rows: rows,
                    TotalRows: rows.Count);
            }
            else if (profileTable == "returns")
            {
                var headers = new List<string> { "کالا", "مبلغ برگشت", "تعداد" };
                var rows = prof.Returns.Select(r => (IReadOnlyList<string>)new[] { r.ProductName, r.Returns ?? "0", r.Quantity ?? "0" }).ToList();
                return new ExportTable(
                    ReportName: report.Name,
                    PeriodLabel: period.Label,
                    FiltersLabel: $"تأمین‌کننده: {prof.Vendor.Name}",
                    ExportedAtJalali: nowJalali,
                    LastSyncJalali: prof.LastSuccessfulSync?.AtJalali,
                    DataLabel: dataMode.DataLabel,
                    DataQualityText: null,
                    Headers: headers,
                    Rows: rows,
                    TotalRows: rows.Count);
            }
            else
            {
                var headers = new List<string> { "شماره سند", "تاریخ", "نوع سند", "خالص خرید", "تعداد" };
                var rows = prof.Documents.Rows.Select(r => (IReadOnlyList<string>)new[] { r.Number, r.DateJalali, r.DocTypeLabel, r.SignedNet ?? "0", r.Quantity ?? "0" }).ToList();
                return new ExportTable(
                    ReportName: report.Name,
                    PeriodLabel: period.Label,
                    FiltersLabel: $"تأمین‌کننده: {prof.Vendor.Name}",
                    ExportedAtJalali: nowJalali,
                    LastSyncJalali: prof.LastSuccessfulSync?.AtJalali,
                    DataLabel: dataMode.DataLabel,
                    DataQualityText: prof.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {prof.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
                    Headers: headers,
                    Rows: rows,
                    TotalRows: prof.Documents.TotalRows);
            }
        }

        if (data is VendorFinancialsSummaryResponse summary)
        {
            var headers = new List<string> { "نام", "کد", "مانده تأمین‌کننده", "پرداخت‌های معوق" };
            var rows = summary.Rows.Select(r => (IReadOnlyList<string>)new[]
            {
                r.Name,
                r.Code ?? "-",
                r.VendorBalance ?? "-",
                r.OutstandingPayments ?? "-"
            }).ToList();

            return new ExportTable(
                ReportName: report.Name,
                PeriodLabel: period.Label,
                FiltersLabel: "بدون فیلتر",
                ExportedAtJalali: nowJalali,
                LastSyncJalali: summary.LastSuccessfulSync?.AtJalali,
                DataLabel: dataMode.DataLabel,
                DataQualityText: summary.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {summary.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
                Headers: headers,
                Rows: rows,
                TotalRows: summary.TotalRows);
        }

        if (data is VendorFinancialListResponse<PaymentHistoryRow> history)
        {
            var headers = new List<string> { "تاریخ پرداخت", "مرجع", "نوع سند", "مبلغ" };
            var rows = history.Rows.Select(r => (IReadOnlyList<string>)new[]
            {
                r.PaidAtJalali,
                r.Reference ?? "-",
                r.InstrumentTypeLabel,
                r.Amount
            }).ToList();

            return new ExportTable(
                ReportName: report.Name,
                PeriodLabel: period.Label,
                FiltersLabel: "بدون فیلتر",
                ExportedAtJalali: nowJalali,
                LastSyncJalali: history.LastSuccessfulSync?.AtJalali,
                DataLabel: dataMode.DataLabel,
                DataQualityText: history.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {history.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
                Headers: headers,
                Rows: rows,
                TotalRows: history.TotalRows);
        }

        if (data is VendorFinancialListResponse<OutstandingInstrumentRow> outstanding)
        {
            var headers = new List<string> { "تاریخ", "مرجع", "نوع سند", "مبلغ معوق" };
            var rows = outstanding.Rows.Select(r => (IReadOnlyList<string>)new[]
            {
                r.DateJalali,
                r.Reference ?? "-",
                r.InstrumentTypeLabel,
                r.UnpaidAmount
            }).ToList();

            return new ExportTable(
                ReportName: report.Name,
                PeriodLabel: period.Label,
                FiltersLabel: "بدون فیلتر",
                ExportedAtJalali: nowJalali,
                LastSyncJalali: outstanding.LastSuccessfulSync?.AtJalali,
                DataLabel: dataMode.DataLabel,
                DataQualityText: outstanding.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {outstanding.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
                Headers: headers,
                Rows: rows,
                TotalRows: outstanding.TotalRows);
        }

        if (data is VendorFinancialDetailResponse detail)
        {
            var headers = new List<string> { "خالص خرید", "پرداخت‌ها", "خرید منهای پرداخت" };
            var rows = new List<IReadOnlyList<string>>
            {
                new[]
                {
                    detail.PurchasesVsPayments.NetPurchases ?? "-",
                    detail.PurchasesVsPayments.Payments ?? "-",
                    detail.PurchasesVsPayments.PurchasesMinusPayments ?? "-"
                }
            };

            return new ExportTable(
                ReportName: report.Name,
                PeriodLabel: period.Label,
                FiltersLabel: $"تأمین‌کننده: {detail.Vendor.Name}",
                ExportedAtJalali: nowJalali,
                LastSyncJalali: detail.LastSuccessfulSync?.AtJalali,
                DataLabel: dataMode.DataLabel,
                DataQualityText: detail.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {detail.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
                Headers: headers,
                Rows: rows,
                TotalRows: 1);
        }

        return new ExportTable(
            ReportName: report.Name,
            PeriodLabel: period.Label,
            FiltersLabel: "بدون فیلتر",
            ExportedAtJalali: nowJalali,
            LastSyncJalali: null,
            DataLabel: dataMode.DataLabel,
            DataQualityText: null,
            Headers: ["عنوان", "مقدار"],
            Rows: [],
            TotalRows: 0);
    }

    public async Task<SaveReportResult> SaveAsync(
        long userId,
        SaveReportRequest request,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        // 1. Validate name
        if (!SavedReportName.TryValidate(request.Name, out var trimmedName, out var nameKey, out var nameError))
        {
            return new SaveReportResult(false, 400, Message: nameError);
        }

        // 2. Validate kind
        if (string.IsNullOrWhiteSpace(request.Kind) || !AllowedKinds.Contains(request.Kind))
        {
            return new SaveReportResult(false, 400, Message: "نوع گزارش نامعتبر است.");
        }

        // 3. Permission and spec checks by kind
        string requiredPermission;
        string? generatedJson = null;

        if (FinancialKinds.Contains(request.Kind))
        {
            if (!user.HasPermission(Permissions.VendorFinancials))
            {
                return new SaveReportResult(false, 403, Message: "برای مشاهده این گزارش به دسترسی «اطلاعات مالی تأمین‌کنندگان» نیاز است.");
            }

            requiredPermission = Permissions.VendorFinancials;
        }
        else if (request.Kind == "generated")
        {
            var isFinancialTool = false;
            if (request.Generated.HasValue && request.Generated.Value.ValueKind == JsonValueKind.Object)
            {
                var genTool = request.Generated.Value.TryGetProperty("tool", out var toolProp) ? toolProp.GetString() : null;
                isFinancialTool = genTool != null && VendorFinancialAnalystTools.AllowlistedToolNames.Contains(genTool);
            }

            if (isFinancialTool && !user.HasPermission(Permissions.VendorFinancials))
            {
                return new SaveReportResult(false, 403, Message: "برای مشاهده این گزارش به دسترسی «اطلاعات مالی تأمین‌کنندگان» نیاز است.");
            }

            if (!user.HasPermission(Permissions.AiQuery))
            {
                return new SaveReportResult(false, 403, Message: "شما دسترسی پرسش از دستیار هوشمند (ai.query) را ندارید.");
            }

            if (!request.Generated.HasValue || request.Generated.Value.ValueKind != JsonValueKind.Object)
            {
                return new SaveReportResult(false, 400, Message: "مشخصات گزارش تولیدشده الزامی است.");
            }

            var root = request.Generated.Value;
            var tool = root.TryGetProperty("tool", out var toolProp2) ? toolProp2.GetString() : null;
            if (string.IsNullOrWhiteSpace(tool) ||
                (!PurchasingAnalystTools.AllowlistedToolNames.Contains(tool) && !VendorFinancialAnalystTools.AllowlistedToolNames.Contains(tool)))
            {
                return new SaveReportResult(false, 400, Message: "ابزار تولید گزارش نامعتبر است.");
            }

            requiredPermission = isFinancialTool ? Permissions.VendorFinancials : Permissions.AiQuery;
            generatedJson = request.Generated.Value.GetRawText();
        }
        else
        {
            requiredPermission = Permissions.PurchasingReports;

            if (request.Kind == "vendors-compare")
            {
                if (request.Filters.HasValue && request.Filters.Value.TryGetProperty("vendors", out var vendorsProp))
                {
                    var count = vendorsProp.ValueKind == JsonValueKind.Array ? vendorsProp.GetArrayLength() : 0;
                    if (count < 2 || count > 5)
                    {
                        return new SaveReportResult(false, 400, Message: "حداقل ۲ و حداکثر ۵ تأمین‌کننده برای مقایسه مجاز است.");
                    }
                }
            }
        }

        var periodJson = request.Period.ValueKind == JsonValueKind.Object
            ? request.Period.GetRawText()
            : "{}";

        var filtersJson = request.Filters.HasValue && request.Filters.Value.ValueKind == JsonValueKind.Object
            ? request.Filters.Value.GetRawText()
            : "{}";

        var zone = SyncOptions.ResolveTimeZone(syncOptions.Value.TimeZone);

        // 4. Duplicate name_key check
        var existing = await db.SavedReports
            .FirstOrDefaultAsync(r => r.UserId == userId && r.NameKey == nameKey, ct);

        if (existing != null)
        {
            if (!request.Overwrite)
            {
                return new SaveReportResult(
                    false,
                    409,
                    Message: SavedReportName.ExistsMessage,
                    Code: "saved-report-exists",
                    Id: existing.Id);
            }

            // Overwrite existing report
            existing.Name = trimmedName;
            existing.Kind = request.Kind;
            existing.RequiredPermission = requiredPermission;
            existing.PeriodJson = periodJson;
            existing.FiltersJson = filtersJson;
            existing.GeneratedJson = generatedJson;
            existing.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);

            return new SaveReportResult(true, 200, Item: ToDto(existing, zone), IsReplaced: true);
        }

        // 5. Check 50 cap for new insert
        var userCount = await db.SavedReports.CountAsync(r => r.UserId == userId, ct);
        if (userCount >= SavedReportName.MaxPerUser)
        {
            return new SaveReportResult(
                false,
                409,
                Message: SavedReportName.CapExceededMessage,
                Code: "saved-report-cap");
        }

        var newReport = new SavedReport
        {
            UserId = userId,
            Name = trimmedName,
            NameKey = nameKey,
            Kind = request.Kind,
            RequiredPermission = requiredPermission,
            PeriodJson = periodJson,
            FiltersJson = filtersJson,
            GeneratedJson = generatedJson,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.SavedReports.Add(newReport);
        await db.SaveChangesAsync(ct);

        return new SaveReportResult(true, 201, Item: ToDto(newReport, zone), IsReplaced: false);
    }

    public async Task<bool> DeleteAsync(long id, long userId, CancellationToken ct)
    {
        var report = await db.SavedReports
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);

        if (report == null)
        {
            return false;
        }

        db.SavedReports.Remove(report);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<OpenReportResult> OpenAsync(
        long id,
        long userId,
        ClaimsPrincipal user,
        CancellationToken ct,
        int pageSize = 20)
    {
        var report = await db.SavedReports
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);

        if (report == null)
        {
            return new OpenReportResult(false, 404, ErrorMessage: "گزارش ذخیره شده یافت نشد.");
        }

        // Check required permissions
        if (FinancialKinds.Contains(report.Kind) || report.RequiredPermission == Permissions.VendorFinancials)
        {
            if (!user.HasPermission(Permissions.VendorFinancials))
            {
                return new OpenReportResult(
                    false,
                    403,
                    ErrorMessage: "برای مشاهده این گزارش به دسترسی «اطلاعات مالی تأمین‌کنندگان» نیاز است.",
                    MissingPermission: Permissions.VendorFinancials);
            }
        }

        if (report.Kind == "generated")
        {
            if (!user.HasPermission(Permissions.AiQuery))
            {
                return new OpenReportResult(
                    false,
                    403,
                    ErrorMessage: "دسترسی پرسش از دستیار هوشمند (ai.query) برای این گزارش لازم است.",
                    MissingPermission: Permissions.AiQuery);
            }

            using var genDoc = JsonDocument.Parse(report.GeneratedJson ?? "{}");
            var tool = genDoc.RootElement.TryGetProperty("tool", out var toolProp) ? toolProp.GetString() : null;
            if (tool != null && VendorFinancialAnalystTools.AllowlistedToolNames.Contains(tool))
            {
                if (!user.HasPermission(Permissions.VendorFinancials))
                {
                    return new OpenReportResult(
                        false,
                        403,
                        ErrorMessage: "برای مشاهده این گزارش به دسترسی «اطلاعات مالی تأمین‌کنندگان» نیاز است.",
                        MissingPermission: Permissions.VendorFinancials);
                }
            }
        }
        else if (report.Kind == "transactions")
        {
            if (!user.HasPermission(Permissions.PurchasingTransactions))
            {
                return new OpenReportResult(
                    false,
                    403,
                    ErrorMessage: "دسترسی تاریخچه تراکنش‌ها (purchasing.transactions) برای این گزارش لازم است.",
                    MissingPermission: Permissions.PurchasingTransactions);
            }
        }

        var zone = SyncOptions.ResolveTimeZone(syncOptions.Value.TimeZone);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var today = JalaliDate.FromDateTime(nowLocal);

        var period = ResolvePeriod(report.PeriodJson, today);
        var savedMeta = new { id = report.Id, name = report.Name, kind = report.Kind };

        if (report.Kind == "generated")
        {
            return await OpenGeneratedAsync(report, period, savedMeta, ct);
        }

        return await OpenStandardAsync(report, period, savedMeta, ct, pageSize);
    }

    private async Task<OpenReportResult> OpenGeneratedAsync(
        SavedReport report,
        JalaliPeriod period,
        object savedMeta,
        CancellationToken ct)
    {
        using var genDoc = JsonDocument.Parse(report.GeneratedJson ?? "{}");
        var genRoot = genDoc.RootElement;
        var tool = genRoot.TryGetProperty("tool", out var toolProp) ? toolProp.GetString() : "purchasing_top_products";

        // Merge filters_json and generated_json into arguments for DispatchAsync
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(report.FiltersJson))
        {
            try
            {
                using var fDoc = JsonDocument.Parse(report.FiltersJson);
                foreach (var prop in fDoc.RootElement.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.Clone();
                }
            }
            catch { }
        }
        if (!string.IsNullOrWhiteSpace(report.GeneratedJson))
        {
            try
            {
                using var gDoc = JsonDocument.Parse(report.GeneratedJson);
                foreach (var prop in gDoc.RootElement.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.Clone();
                }
            }
            catch { }
        }

        using var argsDoc = JsonDocument.Parse(JsonSerializer.Serialize(dict));
        var isFinancial = tool != null && VendorFinancialAnalystTools.AllowlistedToolNames.Contains(tool);
        var toolResult = isFinancial && financialAnalystTools != null
            ? await financialAnalystTools.DispatchAsync(tool!, period, ct, argsDoc.RootElement)
            : await analystTools.DispatchAsync(tool ?? "purchasing_top_products", period, ct, argsDoc.RootElement);
        var visual = VisualBuilder.Build(null, tool ?? (isFinancial ? "vendor_financials_list" : "purchasing_top_products"), period, toolResult, dataMode.DataLabel);

        var filtersDto = new PurchasingFiltersDto(
            Vendor: toolResult.Vendor,
            Product: toolResult.Product,
            Category: toolResult.Category);

        var periodDto = new PurchasingPeriodDto(
            From: period.FromWire,
            To: period.ToWire,
            JalaliLabel: period.Label,
            Month: period.Kind == JalaliPeriodKind.Month ? period.StartMonth.Key : null);

        var body = new
        {
            period = periodDto,
            filters = filtersDto,
            view = toolResult.View,
            visual,
            dataLabel = dataMode.DataLabel,
            unavailable = !toolResult.Success,
            saved = savedMeta
        };

        return new OpenReportResult(true, 200, Body: body);
    }

    private async Task<OpenReportResult> OpenStandardAsync(
        SavedReport report,
        JalaliPeriod period,
        object savedMeta,
        CancellationToken ct,
        int pageSize = 20)
    {
        using var filterDoc = JsonDocument.Parse(report.FiltersJson);
        var filterRoot = filterDoc.RootElement;

        string? vendor = filterRoot.TryGetProperty("vendor", out var vProp) && vProp.ValueKind == JsonValueKind.String ? vProp.GetString() : null;
        string? product = filterRoot.TryGetProperty("product", out var pProp) && pProp.ValueKind == JsonValueKind.String ? pProp.GetString() : null;
        string? category = filterRoot.TryGetProperty("category", out var cProp) && cProp.ValueKind == JsonValueKind.String ? cProp.GetString() : null;
        string? docType = filterRoot.TryGetProperty("docType", out var dProp) && dProp.ValueKind == JsonValueKind.String ? dProp.GetString() : null;
        string sort = filterRoot.TryGetProperty("sort", out var sProp) && sProp.ValueKind == JsonValueKind.String ? (sProp.GetString() ?? "net") : "net";
        bool desc = !filterRoot.TryGetProperty("dir", out var dirProp) || dirProp.GetString() != "asc";
        bool compare = filterRoot.TryGetProperty("compare", out var cmpProp) && cmpProp.GetBoolean();

        var filter = new ReportFilter(
            Period: period,
            Compare: compare,
            VendorKey: vendor,
            ProductKey: product,
            CategoryKey: category,
            Month: null,
            DocType: docType,
            Search: null,
            Match: null,
            Sort: sort,
            Descending: desc,
            Page: 1,
            PageSize: pageSize,
            Print: false);

        object resultBody;

        switch (report.Kind)
        {
            case "by-vendor":
            case "by-product":
            case "by-category":
            case "top-products":
                resultBody = await reportService.GroupedAsync(report.Kind, filter, ct);
                break;

            case "trend":
                resultBody = await reportService.TrendAsync(filter, ct);
                break;

            case "returns":
                resultBody = await reportService.ReturnsAsync(filter, ct);
                break;

            case "transactions":
            case "drill-down":
                resultBody = await reportService.DocumentsAsync(filter, ct, report.Kind == "transactions" ? "transactions" : "documents");
                break;

            case "vendors":
                resultBody = await vendorDirectoryService.DirectoryAsync(filter, ct);
                break;

            case "vendors-compare":
            {
                var keys = new List<string>();
                if (filterRoot.TryGetProperty("vendors", out var vendorsArr) && vendorsArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in vendorsArr.EnumerateArray())
                    {
                        if (elem.GetString() is { } k) keys.Add(k);
                    }
                }
                var (compareRes, _) = await vendorComparisonService.CompareAsync(filter, keys, false, ct);
                resultBody = compareRes!;
                break;
            }

            case "vendor-profile":
            {
                var profile = await vendorDirectoryService.ProfileAsync(filter, ct);
                if (profile != null && pageSize > 20)
                {
                    string? profileTable = null;
                    if (filterRoot.TryGetProperty("profileTable", out var ptProp) && ptProp.ValueKind == JsonValueKind.String)
                    {
                        profileTable = ptProp.GetString();
                    }

                    if (string.IsNullOrEmpty(profileTable) || profileTable == "documents")
                    {
                        var docFilter = new ReportFilter(
                            Period: period,
                            Compare: false,
                            VendorKey: filter.VendorKey,
                            ProductKey: null,
                            CategoryKey: null,
                            Month: null,
                            DocType: null,
                            Search: null,
                            Match: null,
                            Sort: "date",
                            Descending: true,
                            Page: 1,
                            PageSize: pageSize,
                            Print: false);
                        var docs = await reportService.DocumentsAsync(docFilter, ct);
                        var fullDocs = new ProfileDocuments(docs.Page, pageSize, docs.TotalRows, docs.Rows);
                        profile = profile with { Documents = fullDocs };
                    }
                }
                resultBody = profile ?? (object)new { message = "تأمین‌کننده یافت نشد." };
                break;
            }

            case "vendor-financials":
            {
                var finRequest = new VendorFinancialRequest(
                    Period: period,
                    Compare: compare,
                    Search: null,
                    NormalizedSearch: string.Empty,
                    Page: 1,
                    PageSize: pageSize,
                    Sort: sort ?? "vendorBalance",
                    Descending: desc);
                resultBody = financialsService != null
                    ? await financialsService.SummaryAsync(finRequest, ct)
                    : new { message = "سرویس مالی در دسترس نیست." };
                break;
            }

            case "vendor-financials-history":
            {
                var finRequest = new VendorFinancialRequest(
                    Period: period,
                    Compare: false,
                    Search: null,
                    NormalizedSearch: string.Empty,
                    Page: 1,
                    PageSize: pageSize,
                    Sort: sort ?? "paidAt",
                    Descending: desc);
                resultBody = financialsService != null && vendor != null
                    ? await financialsService.HistoryAsync(vendor, finRequest, ct)
                    : new { message = "تأمین‌کننده مشخص نشده است." };
                break;
            }

            case "vendor-financials-outstanding":
            {
                var finRequest = new VendorFinancialRequest(
                    Period: period,
                    Compare: false,
                    Search: null,
                    NormalizedSearch: string.Empty,
                    Page: 1,
                    PageSize: pageSize,
                    Sort: sort ?? "date",
                    Descending: desc);
                resultBody = financialsService != null && vendor != null
                    ? await financialsService.OutstandingAsync(vendor, finRequest, ct)
                    : new { message = "تأمین‌کننده مشخص نشده است." };
                break;
            }

            case "vendor-financials-purchases-vs-payments":
            {
                var finRequest = new VendorFinancialRequest(
                    Period: period,
                    Compare: compare,
                    Search: null,
                    NormalizedSearch: string.Empty,
                    Page: 1,
                    PageSize: pageSize,
                    Sort: "name",
                    Descending: false);
                resultBody = financialsService != null && vendor != null
                    ? await financialsService.DetailAsync(vendor, finRequest, ct) ?? (object)new { message = "تأمین‌کننده یافت نشد." }
                    : new { message = "تأمین‌کننده مشخص نشده است." };
                break;
            }

            default:
                resultBody = await reportService.GroupedAsync("by-vendor", filter, ct);
                break;
        }

        return new OpenReportResult(true, 200, Body: new { data = resultBody, saved = savedMeta });
    }

    private JalaliPeriod ResolvePeriod(string periodJson, JalaliDate today)
    {
        try
        {
            using var doc = JsonDocument.Parse(periodJson);
            var root = doc.RootElement;
            var kind = root.TryGetProperty("kind", out var kProp) ? kProp.GetString() : null;
            if (string.IsNullOrEmpty(kind) && root.TryGetProperty("period", out var pProp))
            {
                kind = pProp.GetString();
            }
            kind ??= "month";

            var month = root.TryGetProperty("month", out var mProp) ? mProp.GetString() : null;
            var quarter = root.TryGetProperty("quarter", out var qProp) ? qProp.GetString() : null;
            var year = root.TryGetProperty("year", out var yProp) ? yProp.GetString() : null;
            var from = root.TryGetProperty("from", out var fProp) ? fProp.GetString() : null;
            var to = root.TryGetProperty("to", out var tProp) ? tProp.GetString() : null;

            if (root.TryGetProperty("period", out var pValProp) && pValProp.ValueKind == JsonValueKind.String)
            {
                var pVal = pValProp.GetString();
                if (!string.IsNullOrEmpty(pVal) && pVal != "month" && pVal != "quarter" && pVal != "year" && pVal != "custom")
                {
                    if (kind == "quarter" && quarter == null) quarter = pVal;
                    else if (kind == "year" && year == null) year = pVal;
                    else if (month == null) month = pVal;
                }
            }

            string? value = kind switch
            {
                "quarter" => quarter,
                "year" => year,
                _ => month
            };

            if (JalaliPeriod.TryParse(kind, value, from, to, today, out var parsed, out _))
            {
                return parsed!;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to parse stored period_json: {Message}", ex.Message);
        }

        JalaliPeriod.TryParse("month", null, null, null, today, out var fallback, out _);
        return fallback!;
    }

    private static SavedReportItemDto ToDto(SavedReport r, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc), zone);
        var jalali = JalaliDate.FromDateTime(local);
        var jalaliText = $"{jalali.Year:D4}/{jalali.Month:D2}/{jalali.Day:D2} {local.Hour:D2}:{local.Minute:D2}";
        var updatedAtJalali = JalaliDate.ToPersianDigits(jalaliText);

        object periodObj;
        try
        {
            periodObj = JsonSerializer.Deserialize<object>(r.PeriodJson) ?? new { };
        }
        catch
        {
            periodObj = new { };
        }

        IReadOnlyList<object> filtersList = Array.Empty<object>();

        return new SavedReportItemDto(
            Id: r.Id,
            Name: r.Name,
            Kind: r.Kind,
            KindLabel: ReportKeys.Name(r.Kind),
            RequiredPermission: r.RequiredPermission,
            Period: periodObj,
            Filters: filtersList,
            UpdatedAtJalali: updatedAtJalali);
    }
}
