namespace Zharfa.Api.Controllers;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Zharfa.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Zharfa.Api.Analytics;
using Zharfa.Api.Audit;
using Zharfa.Api.Data;
using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.Platform;
using Zharfa.Api.Purchasing.Reports;

/// <summary>
/// <c>/api/purchasing/*</c> (contracts/api.md §1–§7): the vendor directory and profile, the seven purchasing
/// reports, the documents list and lines, the filter-bar lookups and the vendor comparison, each with its print
/// twin. Manager and Admin only, enforced here on the server; not demo-only (available in production and demo).
/// Every 400/404 carries a Persian <c>message</c>; a query over its 15 s budget answers 503.
/// </summary>
[ApiController]
[Route("api/purchasing")]
[Authorize(Policy = Permissions.PurchasingReports)]
public sealed class PurchasingReportsController(
    PurchasingReportService reports,
    VendorDirectoryService directory,
    VendorComparisonService comparison,
    ExcelExportService exportService,
    IAuditLogger auditLogger,
    DataModeOptions dataMode,
    IOptions<SyncOptions> syncOptions,
    TimeProvider clock,
    IAuthorizationService authorization) : ControllerBase
{
    private const string UnknownReportMessage = "گزارش موردنظر یافت نشد.";
    private const string UnknownLookupMessage = "فهرست جستجو معتبر نیست؛ یکی از vendors، products یا categories را انتخاب کنید.";
    private const string LookupLimitMessage = "تعداد نتایج جستجو باید عددی بین ۱ و ۵۰ باشد.";
    private const string AllFlagMessage = "مقدار all باید true یا false باشد.";
    private const string DocumentNotFoundMessage = "سند موردنظر یافت نشد یا لغو شده است.";
    private const string VendorRequiredMessage = "کلید تأمین‌کننده لازم است.";
    private const string UnknownVendorMessage = "تأمین‌کننده نامشخص را نمی‌توان نمایش داد.";
    private const string VendorNotFoundMessage = "تأمین‌کننده موردنظر در فهرست تأمین‌کنندگان یافت نشد.";
    private const int DefaultLookupLimit = 20;
    private const int MaxLookupLimit = 50;

    // ----- reports (contracts/api.md §3) ---------------------------------------------------------------------

    [HttpGet("reports/{reportId}")]
    [AuditReportOpen("{reportId}")]
    public Task<IActionResult> Report(string reportId, CancellationToken ct) => RunReport(reportId, print: false, ct);

    [HttpGet("reports/{reportId}/print")]
    [AuditReportOpen("{reportId}", Print = true)]
    public Task<IActionResult> ReportPrint(string reportId, CancellationToken ct) => RunReport(reportId, print: true, ct);

    [Authorize(Policy = Permissions.Export)]
    [HttpGet("reports/{reportId}/export")]
    public Task<IActionResult> ReportExport(string reportId, CancellationToken ct) => Execute(async () =>
    {
        if (!ReportCatalog.TryGet(reportId, out var definition) || definition is null)
        {
            return NotFound(new { message = UnknownReportMessage });
        }

        if (reportId == "transactions")
        {
            var transactionsAllowed = await authorization.AuthorizeAsync(User, Permissions.PurchasingTransactions);
            if (!transactionsAllowed.Succeeded)
            {
                await AccessDenied.RespondAsync(HttpContext, Permissions.PurchasingTransactions);
                return new EmptyResult();
            }
        }

        if (!TryFilter(definition, print: false, out var filter, out var error))
        {
            return BadRequest(new { message = error });
        }

        var exportFilter = new ReportFilter(
            Period: filter.Period,
            Compare: filter.Compare,
            VendorKey: filter.VendorKey,
            ProductKey: filter.ProductKey,
            CategoryKey: filter.CategoryKey,
            Month: filter.Month,
            DocType: filter.DocType,
            Search: filter.Search,
            Match: filter.Match,
            Sort: filter.Sort,
            Descending: filter.Descending,
            Page: 1,
            PageSize: 10000,
            Print: false);

        var data = await ReportDataAsync(definition.Id, exportFilter, print: false, ct);
        var exportTable = BuildExportTableFromReport(definition.Id, data, exportFilter);

        var bytes = exportService.BuildWorkbook(exportTable);
        var filename = exportService.BuildFilename(exportTable.ReportName, Today());

        _ = auditLogger.RecordAsync(
            new AuditEntry(AuditActions.ReportExported, $"report:{reportId}", AuditDetail.Of(("period", exportFilter.Period.Label))),
            ct);

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
    });

    private Task<IActionResult> RunReport(string reportId, bool print, CancellationToken ct) => Execute(async () =>
    {
        if (!ReportCatalog.TryGet(reportId, out var definition) || definition is null)
        {
            return NotFound(new { message = UnknownReportMessage });
        }

        // The class policy is purchasing.reports; the transactions report also needs purchasing.transactions. The
        // check runs here, before any query, so a refused caller never reaches the data (contracts/api.md §0).
        if (reportId == "transactions")
        {
            var transactionsAllowed = await authorization.AuthorizeAsync(User, Permissions.PurchasingTransactions);
            if (!transactionsAllowed.Succeeded)
            {
                await AccessDenied.RespondAsync(HttpContext, Permissions.PurchasingTransactions);
                return new EmptyResult();
            }
        }

        if (!TryFilter(definition, print, out var filter, out var error))
        {
            return BadRequest(new { message = error });
        }

        return Ok(await ReportDataAsync(definition.Id, filter, print, ct));
    });

    /// <summary>The report's page or print response; <c>transactions</c> is the documents list.</summary>
    private async Task<object> ReportDataAsync(string reportId, ReportFilter filter, bool print, CancellationToken ct)
    {
        switch (reportId)
        {
            case "by-vendor":
            case "by-product":
            case "by-category":
            case "top-products":
                if (print)
                {
                    return await reports.GroupedPrintAsync(reportId, filter, ct);
                }

                return await reports.GroupedAsync(reportId, filter, ct);

            case "returns":
                if (print)
                {
                    return await reports.ReturnsPrintAsync(filter, ct);
                }

                return await reports.ReturnsAsync(filter, ct);

            case "trend":
                if (print)
                {
                    return await reports.TrendPrintAsync(filter, ct);
                }

                return await reports.TrendAsync(filter, ct);

            case "transactions":
                if (print)
                {
                    return await reports.DocumentsPrintAsync(filter, ct, ReportKeys.Transactions);
                }

                return await reports.DocumentsAsync(filter, ct, ReportKeys.Transactions);

            default:
                throw new InvalidOperationException("report id outside the catalog: " + reportId);
        }
    }

    // ----- documents (contracts/api.md §5–§6) ----------------------------------------------------------------

    [Authorize(Policy = Permissions.PurchasingTransactions)]
    [HttpGet("documents")]
    [AuditReportOpen(ReportKeys.DrillDown)]
    public Task<IActionResult> Documents(CancellationToken ct) => DocumentList(print: false, ct);

    [Authorize(Policy = Permissions.PurchasingTransactions)]
    [HttpGet("documents/print")]
    [AuditReportOpen(ReportKeys.DrillDown, Print = true)]
    public Task<IActionResult> DocumentsPrint(CancellationToken ct) => DocumentList(print: true, ct);

    [Authorize(Policy = Permissions.PurchasingTransactions)]
    [Authorize(Policy = Permissions.Export)]
    [HttpGet("documents/export")]
    public Task<IActionResult> DocumentsExport(CancellationToken ct) => Execute(async () =>
    {
        if (!TryFilter(ReportCatalog.Documents, print: false, out var filter, out var error))
        {
            return BadRequest(new { message = error });
        }

        var exportFilter = new ReportFilter(
            Period: filter.Period,
            Compare: false,
            VendorKey: filter.VendorKey,
            ProductKey: filter.ProductKey,
            CategoryKey: filter.CategoryKey,
            Month: filter.Month,
            DocType: filter.DocType,
            Search: filter.Search,
            Match: filter.Match,
            Sort: filter.Sort,
            Descending: filter.Descending,
            Page: 1,
            PageSize: 10000,
            Print: false);

        var data = await reports.DocumentsAsync(exportFilter, ct);
        var headers = new List<string> { "شماره سند", "تاریخ", "نوع سند", "تأمین‌کننده", "خالص خرید", "تعداد" };
        var rows = data.Rows.Select(r => (IReadOnlyList<string>)new[]
        {
            r.Number,
            r.DateJalali,
            r.DocTypeLabel,
            r.VendorName,
            r.SignedNet ?? "0",
            r.Quantity ?? "0"
        }).ToList();

        var exportTable = new ExportTable(
            ReportName: "جزئیات اسناد",
            PeriodLabel: exportFilter.Period.Label,
            FiltersLabel: FormatFiltersEcho(data.Filters),
            ExportedAtJalali: NowJalali(),
            LastSyncJalali: data.LastSuccessfulSync?.AtJalali,
            DataLabel: dataMode.DataLabel,
            DataQualityText: data.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {data.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
            Headers: headers,
            Rows: rows,
            TotalRows: data.TotalRows,
            Limit: 10000);

        var bytes = exportService.BuildWorkbook(exportTable);
        var filename = exportService.BuildFilename(exportTable.ReportName, Today());

        _ = auditLogger.RecordAsync(
            new AuditEntry(AuditActions.ReportExported, "report:drill-down", AuditDetail.Of(("period", exportFilter.Period.Label))),
            ct);

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
    });

    private Task<IActionResult> DocumentList(bool print, CancellationToken ct) => Execute(async () =>
    {
        if (!TryFilter(ReportCatalog.Documents, print, out var filter, out var error))
        {
            return BadRequest(new { message = error });
        }

        if (print)
        {
            return Ok(await reports.DocumentsPrintAsync(filter, ct));
        }

        return Ok(await reports.DocumentsAsync(filter, ct));
    });

    [Authorize(Policy = Permissions.PurchasingTransactions)]
    [HttpGet("documents/{documentId:long}/lines")]
    [AuditReportOpen(ReportKeys.DrillDownLines)]
    public Task<IActionResult> DocumentLines(long documentId, CancellationToken ct) => Execute(async () =>
    {
        if (!TryFilter(ReportCatalog.Documents, print: false, out var filter, out var error))
        {
            return BadRequest(new { message = error });
        }

        var allText = Request.Query["all"].ToString();
        if (allText.Length > 0 && allText is not ("true" or "false"))
        {
            return BadRequest(new { message = AllFlagMessage });
        }

        var lines = await reports.DocumentLinesAsync(filter, documentId, allText == "true", ct);
        return lines is null ? NotFound(new { message = DocumentNotFoundMessage }) : Ok(lines);
    });

    // ----- filter-bar lookups (contracts/api.md §7) ----------------------------------------------------------

    [HttpGet("lookup/{kind}")]
    public Task<IActionResult> Lookup(string kind, string? q, string? limit, CancellationToken ct) => Execute(async () =>
    {
        var text = q?.Trim();
        if (text is { Length: > ReportFilter.MaxSearchLength })
        {
            return BadRequest(new { message = "متن جستجو نمی‌تواند بیش از ۱۰۰ نویسه باشد." });
        }

        var size = DefaultLookupLimit;
        if (!string.IsNullOrEmpty(limit))
        {
            if (!int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size < 1)
            {
                return BadRequest(new { message = LookupLimitMessage });
            }

            size = Math.Min(size, MaxLookupLimit);
        }

        var response = await directory.LookupAsync(kind, string.IsNullOrEmpty(text) ? null : text, size, ct);
        return response is null ? NotFound(new { message = UnknownLookupMessage }) : Ok(response);
    });

    // ----- vendor directory and profile (contracts/api.md §1–§2) ---------------------------------------------

    [HttpGet("vendors")]
    [AuditReportOpen(ReportKeys.Vendors)]
    public Task<IActionResult> Vendors(CancellationToken ct) => VendorList(print: false, ct);

    [HttpGet("vendors/print")]
    [AuditReportOpen(ReportKeys.Vendors, Print = true)]
    public Task<IActionResult> VendorsPrint(CancellationToken ct) => VendorList(print: true, ct);

    [Authorize(Policy = Permissions.Export)]
    [HttpGet("vendors/export")]
    public Task<IActionResult> VendorsExport(CancellationToken ct) => Execute(async () =>
    {
        if (!TryFilter(ReportCatalog.Directory, print: false, out var filter, out var error))
        {
            return BadRequest(new { message = error });
        }

        var exportFilter = new ReportFilter(
            Period: filter.Period,
            Compare: filter.Compare,
            VendorKey: filter.VendorKey,
            ProductKey: filter.ProductKey,
            CategoryKey: filter.CategoryKey,
            Month: filter.Month,
            DocType: filter.DocType,
            Search: filter.Search,
            Match: filter.Match,
            Sort: filter.Sort,
            Descending: filter.Descending,
            Page: 1,
            PageSize: 10000,
            Print: false);

        var data = await directory.DirectoryAsync(exportFilter, ct);
        var headers = new List<string> { "نام تأمین‌کننده", "کد", "خالص خرید", "آخرین تاریخ خرید" };
        if (exportFilter.Compare)
        {
            headers.AddRange(["دوره قبل", "تفاوت", "درصد تغییر"]);
        }

        var rows = data.Rows.Select(r =>
        {
            var row = new List<string> { r.Name, r.Code ?? "-", r.Net ?? "0", r.LastPurchaseDateJalali ?? "-" };
            if (exportFilter.Compare)
            {
                row.AddRange([r.PreviousNet ?? "-", r.Delta ?? "-", r.Percent ?? "-"]);
            }
            return (IReadOnlyList<string>)row;
        }).ToList();

        var exportTable = new ExportTable(
            ReportName: "فهرست تأمین‌کنندگان",
            PeriodLabel: exportFilter.Period.Label,
            FiltersLabel: FormatFiltersEcho(data.Filters),
            ExportedAtJalali: NowJalali(),
            LastSyncJalali: data.LastSuccessfulSync?.AtJalali,
            DataLabel: dataMode.DataLabel,
            DataQualityText: data.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {data.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
            Headers: headers,
            Rows: rows,
            TotalRows: data.TotalRows,
            Limit: 10000);

        var bytes = exportService.BuildWorkbook(exportTable);
        var filename = exportService.BuildFilename(exportTable.ReportName, Today());

        _ = auditLogger.RecordAsync(
            new AuditEntry(AuditActions.ReportExported, "report:vendors", AuditDetail.Of(("period", exportFilter.Period.Label))),
            ct);

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
    });

    private Task<IActionResult> VendorList(bool print, CancellationToken ct) => Execute(async () =>
    {
        if (!TryFilter(ReportCatalog.Directory, print, out var filter, out var error))
        {
            return BadRequest(new { message = error });
        }

        if (print)
        {
            return Ok(await directory.DirectoryPrintAsync(filter, ct));
        }

        return Ok(await directory.DirectoryAsync(filter, ct));
    });

    [HttpGet("vendor-profile")]
    [AuditReportOpen(ReportKeys.VendorProfile)]
    public Task<IActionResult> VendorProfile(CancellationToken ct) => Execute(async () =>
    {
        if (!TryFilter(VendorDirectoryService.ProfileDefinition, print: false, out var filter, out var error))
        {
            return BadRequest(new { message = error });
        }

        if (filter.VendorKey is null)
        {
            return BadRequest(new { message = VendorRequiredMessage });
        }

        if (filter.VendorKey == ReportFilter.UnknownKey)
        {
            return BadRequest(new { message = UnknownVendorMessage });
        }

        var profile = await directory.ProfileAsync(filter, ct);
        return profile is null ? NotFound(new { message = VendorNotFoundMessage }) : Ok(profile);
    });

    [Authorize(Policy = Permissions.Export)]
    [HttpGet("vendor-profile/export")]
    public Task<IActionResult> VendorProfileExport(CancellationToken ct) => Execute(async () =>
    {
        if (!TryFilter(VendorDirectoryService.ProfileDefinition, print: false, out var filter, out var error))
        {
            return BadRequest(new { message = error });
        }

        if (filter.VendorKey is null)
        {
            return BadRequest(new { message = VendorRequiredMessage });
        }

        var tableParam = Request.Query["table"].ToString().ToLowerInvariant();
        var profile = await directory.ProfileAsync(filter, ct);
        if (profile == null)
        {
            return NotFound(new { message = VendorNotFoundMessage });
        }

        ExportTable exportTable;
        if (tableParam == "top-products")
        {
            var headers = new List<string> { "کالا", "خالص خرید" };
            var rows = profile.TopProducts.Select(p => (IReadOnlyList<string>)new[] { p.Name, p.Net ?? "0" }).ToList();
            exportTable = new ExportTable(
                ReportName: $"کالاهای برتر - {profile.Vendor.Name}",
                PeriodLabel: filter.Period.Label,
                FiltersLabel: $"تأمین‌کننده: {profile.Vendor.Name}",
                ExportedAtJalali: NowJalali(),
                LastSyncJalali: profile.LastSuccessfulSync?.AtJalali,
                DataLabel: dataMode.DataLabel,
                DataQualityText: null,
                Headers: headers,
                Rows: rows,
                TotalRows: rows.Count);
        }
        else if (tableParam == "returns")
        {
            var headers = new List<string> { "کالا", "مبلغ برگشت", "تعداد" };
            var rows = profile.Returns.Select(r => (IReadOnlyList<string>)new[] { r.ProductName, r.Returns ?? "0", r.Quantity ?? "0" }).ToList();
            exportTable = new ExportTable(
                ReportName: $"برگشت از خرید - {profile.Vendor.Name}",
                PeriodLabel: filter.Period.Label,
                FiltersLabel: $"تأمین‌کننده: {profile.Vendor.Name}",
                ExportedAtJalali: NowJalali(),
                LastSyncJalali: profile.LastSuccessfulSync?.AtJalali,
                DataLabel: dataMode.DataLabel,
                DataQualityText: null,
                Headers: headers,
                Rows: rows,
                TotalRows: rows.Count);
        }
        else
        {
            var docFilter = new ReportFilter(
                Period: filter.Period,
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
                PageSize: 10000,
                Print: false);

            var docs = await reports.DocumentsAsync(docFilter, ct);
            var headers = new List<string> { "شماره سند", "تاریخ", "نوع سند", "خالص خرید", "تعداد" };
            var rows = docs.Rows.Select(r => (IReadOnlyList<string>)new[] { r.Number, r.DateJalali, r.DocTypeLabel, r.SignedNet ?? "0", r.Quantity ?? "0" }).ToList();

            exportTable = new ExportTable(
                ReportName: $"اسناد {profile.Vendor.Name}",
                PeriodLabel: filter.Period.Label,
                FiltersLabel: $"تأمین‌کننده: {profile.Vendor.Name}",
                ExportedAtJalali: NowJalali(),
                LastSyncJalali: docs.LastSuccessfulSync?.AtJalali,
                DataLabel: dataMode.DataLabel,
                DataQualityText: docs.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {docs.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
                Headers: headers,
                Rows: rows,
                TotalRows: docs.TotalRows);
        }

        var bytes = exportService.BuildWorkbook(exportTable);
        var filename = exportService.BuildFilename(exportTable.ReportName, Today());

        _ = auditLogger.RecordAsync(
            new AuditEntry(AuditActions.ReportExported, "report:vendor-profile", AuditDetail.Of(("vendor", filter.VendorKey), ("period", filter.Period.Label))),
            ct);

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
    });

    // ----- vendor comparison (contracts/api.md §4) -----------------------------------------------------------

    [HttpGet("vendors/compare")]
    [AuditReportOpen(ReportKeys.VendorsCompare)]
    public Task<IActionResult> VendorCompare(CancellationToken ct) => VendorCompareResponse(print: false, ct);

    [HttpGet("vendors/compare/print")]
    [AuditReportOpen(ReportKeys.VendorsCompare, Print = true)]
    public Task<IActionResult> VendorComparePrint(CancellationToken ct) => VendorCompareResponse(print: true, ct);

    [Authorize(Policy = Permissions.Export)]
    [HttpGet("vendors/compare/export")]
    public Task<IActionResult> VendorCompareExport(CancellationToken ct) => Execute(async () =>
    {
        if (!TryFilter(VendorComparisonService.Definition, print: false, out var filter, out var error))
        {
            return BadRequest(new { message = error });
        }

        if (!VendorComparisonService.TryParseKeys(Request.Query["vendors"].ToString(), out var keys, out var keyError))
        {
            return BadRequest(new { message = keyError });
        }

        var (response, failure) = await comparison.CompareAsync(filter, keys, false, ct);
        if (failure is not null)
        {
            return BadRequest(new { message = failure });
        }

        var headers = new List<string> { "تأمین‌کننده", "خالص خرید", "تعداد خالص", "تعداد اسناد", "برگشت از خرید" };
        var rows = response!.Vendors.Select(v => (IReadOnlyList<string>)new[]
        {
            v.Name + (string.IsNullOrEmpty(v.Code) ? "" : $" ({v.Code})"),
            v.Net ?? "0",
            v.NetQuantity ?? "0",
            v.DocumentCount?.ToString() ?? "0",
            v.Returns ?? "0"
        }).ToList();

        var exportTable = new ExportTable(
            ReportName: "مقایسه تأمین‌کنندگان",
            PeriodLabel: filter.Period.Label,
            FiltersLabel: $"تأمین‌کنندگان: {string.Join("، ", response.Vendors.Select(v => v.Name))}",
            ExportedAtJalali: NowJalali(),
            LastSyncJalali: response.LastSuccessfulSync?.AtJalali,
            DataLabel: dataMode.DataLabel,
            DataQualityText: response.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {response.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
            Headers: headers,
            Rows: rows,
            TotalRows: response.Vendors.Count,
            Limit: 10000);

        var bytes = exportService.BuildWorkbook(exportTable);
        var filename = exportService.BuildFilename(exportTable.ReportName, Today());

        _ = auditLogger.RecordAsync(
            new AuditEntry(AuditActions.ReportExported, "report:vendors-compare", AuditDetail.Of(("period", filter.Period.Label))),
            ct);

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
    });

    private Task<IActionResult> VendorCompareResponse(bool print, CancellationToken ct) => Execute(async () =>
    {
        if (!TryFilter(VendorComparisonService.Definition, print, out var filter, out var error))
        {
            return BadRequest(new { message = error });
        }

        if (!VendorComparisonService.TryParseKeys(Request.Query["vendors"].ToString(), out var keys, out var keyError))
        {
            return BadRequest(new { message = keyError });
        }

        var (response, failure) = await comparison.CompareAsync(filter, keys, print, ct);
        if (failure is not null)
        {
            return BadRequest(new { message = failure });
        }

        return Ok(response);
    });

    // ----- shared ---------------------------------------------------------------------------------------------

    /// <summary>Validates the query against a declaration; on failure <paramref name="error"/> is the Persian message.</summary>
    private bool TryFilter(ReportDefinition definition, bool print, out ReportFilter filter, out string error)
    {
        var ok = ReportFilter.TryParse(definition, QueryValues(), Today(), print, out var parsed, out var message);
        filter = parsed!;
        error = message ?? string.Empty;
        return ok;
    }

    private IReadOnlyDictionary<string, string?> QueryValues() =>
        Request.Query.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString(), StringComparer.Ordinal);

    /// <summary>The Jalali date of today in the sync zone (the period default).</summary>
    private JalaliDate Today()
    {
        var zone = SyncOptions.ResolveTimeZone(syncOptions.Value.TimeZone);
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);
        return JalaliDate.FromDateTime(local.DateTime);
    }

    private string NowJalali()
    {
        var zone = SyncOptions.ResolveTimeZone(syncOptions.Value.TimeZone);
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime;
        var jalali = JalaliDate.FromDateTime(local);
        return JalaliDate.ToPersianDigits($"{jalali.Year:D4}/{jalali.Month:D2}/{jalali.Day:D2} {local.Hour:D2}:{local.Minute:D2}");
    }

    private static string FormatFiltersEcho(IReadOnlyList<ReportFilterEcho>? echoes)
    {
        if (echoes == null || echoes.Count == 0) return "بدون فیلتر";
        return string.Join("، ", echoes.Select(e => $"{e.Name}: {e.Label}"));
    }

    private ExportTable BuildExportTableFromReport(string reportId, object data, ReportFilter filter)
    {
        string reportName = ReportKeys.Name(reportId);
        var nowJalali = NowJalali();

        if (data is ReportTablePage<GroupedRow> g)
        {
            var headers = new List<string>
            {
                reportId == "by-vendor" ? "تأمین‌کننده" : (reportId == "by-category" ? "گروه کالا" : "کالا"),
                "خالص خرید", "ناخالص", "برگشت", "ارزش افزوده", "تعداد اسناد"
            };

            if (filter.Compare)
            {
                headers.AddRange(["دوره قبل", "تفاوت", "درصد تغییر"]);
            }

            var rows = g.Rows.Select(r =>
            {
                var row = new List<string>
                {
                    r.Name,
                    r.Net ?? "0",
                    r.Gross ?? "0",
                    r.Returns ?? "0",
                    r.Vat ?? "0",
                    r.DocumentCount.ToString()
                };
                if (filter.Compare)
                {
                    row.AddRange([r.PreviousNet ?? "-", r.Delta ?? "-", r.Percent ?? "-"]);
                }
                return (IReadOnlyList<string>)row;
            }).ToList();

            return new ExportTable(
                ReportName: reportName,
                PeriodLabel: filter.Period.Label,
                FiltersLabel: FormatFiltersEcho(g.Filters),
                ExportedAtJalali: nowJalali,
                LastSyncJalali: g.LastSuccessfulSync?.AtJalali,
                DataLabel: dataMode.DataLabel,
                DataQualityText: g.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {g.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
                Headers: headers,
                Rows: rows,
                TotalRows: g.TotalRows,
                Limit: 10000);
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
                ReportName: reportName,
                PeriodLabel: filter.Period.Label,
                FiltersLabel: FormatFiltersEcho(ret.Filters),
                ExportedAtJalali: nowJalali,
                LastSyncJalali: ret.LastSuccessfulSync?.AtJalali,
                DataLabel: dataMode.DataLabel,
                DataQualityText: ret.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {ret.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
                Headers: headers,
                Rows: rows,
                TotalRows: ret.TotalRows,
                Limit: 10000);
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
                ReportName: reportName,
                PeriodLabel: filter.Period.Label,
                FiltersLabel: FormatFiltersEcho(tr.Filters),
                ExportedAtJalali: nowJalali,
                LastSyncJalali: tr.LastSuccessfulSync?.AtJalali,
                DataLabel: dataMode.DataLabel,
                DataQualityText: tr.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {tr.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
                Headers: headers,
                Rows: rows,
                TotalRows: tr.TotalRows,
                Limit: 10000);
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
                ReportName: reportName,
                PeriodLabel: filter.Period.Label,
                FiltersLabel: FormatFiltersEcho(docs.Filters),
                ExportedAtJalali: nowJalali,
                LastSyncJalali: docs.LastSuccessfulSync?.AtJalali,
                DataLabel: dataMode.DataLabel,
                DataQualityText: docs.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {docs.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
                Headers: headers,
                Rows: rows,
                TotalRows: docs.TotalRows,
                Limit: 10000);
        }

        return new ExportTable(
            ReportName: reportName,
            PeriodLabel: filter.Period.Label,
            FiltersLabel: "بدون فیلتر",
            ExportedAtJalali: nowJalali,
            LastSyncJalali: null,
            DataLabel: dataMode.DataLabel,
            DataQualityText: null,
            Headers: ["عنوان", "مقدار"],
            Rows: [],
            TotalRows: 0,
            Limit: 10000);
    }

    /// <summary>Maps a 15 s query timeout to 503 with the Persian message (contracts/api.md, status codes).</summary>
    private static async Task<IActionResult> Execute(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ReportTimeoutException ex)
        {
            return new ObjectResult(new { message = ex.Message }) { StatusCode = 503 };
        }
    }
}
