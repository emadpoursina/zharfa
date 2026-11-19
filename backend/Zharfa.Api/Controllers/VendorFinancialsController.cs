namespace Zharfa.Api.Controllers;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Zharfa.Api.Analytics;
using Zharfa.Api.Audit;
using Zharfa.Api.Data;
using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.Platform;
using Zharfa.Api.Purchasing.Reports;
using Zharfa.Api.Security;
using Zharfa.Api.VendorFinancials;

/// <summary>
/// Vendor financials (Phase 6, contracts/api.md §1–§5): the Financials list with its print and export twins,
/// the vendor detail header, payment history and outstanding instruments with export twins. The class policy
/// stays <c>vendor.financials</c> (Management/Finance only; Admin and Purchasing get 403 + access.denied on
/// every route) and every export additionally requires <c>export</c> — the dual check of the purchasing
/// exports. Authorization is decided here on the server; the SPA tab is navigation only.
/// </summary>
[ApiController]
[Route("api/vendor-financials")]
[Authorize(Policy = Permissions.VendorFinancials)]
public sealed class VendorFinancialsController(
    VendorFinancialsService service,
    ExcelExportService excel,
    IAuditLogger auditLogger,
    DataModeOptions dataMode,
    IOptions<SyncOptions> syncOptions,
    TimeProvider clock) : ControllerBase
{
    public const string VendorNotFoundMessage = "تأمین‌کننده یافت نشد.";

    // ----- list (contracts/api.md §1) ------------------------------------------------------------------------

    [HttpGet("summary")]
    [AuditReportOpen(ReportKeys.VendorFinancials)]
    public Task<IActionResult> Summary(CancellationToken ct) => Execute(async () =>
    {
        if (!TryRequest(VendorFinancialSurface.Summary, out var request, out var error))
        {
            return BadRequestError(error!);
        }

        return Ok(await service.SummaryAsync(request!, ct));
    });

    [HttpGet("summary/print")]
    [AuditReportOpen(ReportKeys.VendorFinancials, Print = true)]
    public Task<IActionResult> SummaryPrint(CancellationToken ct) => Execute(async () =>
    {
        if (!TryRequest(VendorFinancialSurface.Summary, out var request, out var error))
        {
            return BadRequestError(error!);
        }

        return Ok(await service.SummaryPrintAsync(request!, ct));
    });

    [Authorize(Policy = Permissions.Export)]
    [HttpGet("summary/export")]
    public Task<IActionResult> SummaryExport(CancellationToken ct) => Execute(async () =>
    {
        if (!TryRequest(VendorFinancialSurface.Summary, out var request, out var error))
        {
            return BadRequestError(error!);
        }

        var bundle = await service.SummaryExportAsync(request!, ct);
        return await ExportAsync(bundle, AuditReportTarget(ReportKeys.VendorFinancials), ct);
    });

    // ----- detail, history, outstanding (contracts/api.md §2–§4) ----------------------------------------------

    [HttpGet("vendors/{vendorKey}")]
    [AuditReportOpen(ReportKeys.VendorFinancialsDetail)]
    public Task<IActionResult> Vendor(string vendorKey, CancellationToken ct) => Execute(async () =>
    {
        if (!TryRequest(VendorFinancialSurface.Summary, out var request, out var error))
        {
            return BadRequestError(error!);
        }

        var detail = await service.DetailAsync(vendorKey, request!, ct);
        return detail is null ? NotFound(new { message = VendorNotFoundMessage }) : Ok(detail);
    });

    [HttpGet("vendors/{vendorKey}/history")]
    [AuditReportOpen(ReportKeys.VendorFinancialsHistory)]
    public Task<IActionResult> History(string vendorKey, CancellationToken ct) => Execute(async () =>
    {
        if (!TryRequest(VendorFinancialSurface.History, out var request, out var error))
        {
            return BadRequestError(error!);
        }

        return Ok(await service.HistoryAsync(vendorKey, request!, ct));
    });

    [Authorize(Policy = Permissions.Export)]
    [HttpGet("vendors/{vendorKey}/history/export")]
    public Task<IActionResult> HistoryExport(string vendorKey, CancellationToken ct) => Execute(async () =>
    {
        if (!TryRequest(VendorFinancialSurface.History, out var request, out var error))
        {
            return BadRequestError(error!);
        }

        var bundle = await service.HistoryExportAsync(vendorKey, request!, ct);
        return await ExportAsync(bundle, AuditReportTarget(ReportKeys.VendorFinancialsHistory, vendorKey), ct);
    });

    [HttpGet("vendors/{vendorKey}/outstanding")]
    [AuditReportOpen(ReportKeys.VendorFinancialsOutstanding)]
    public Task<IActionResult> Outstanding(string vendorKey, CancellationToken ct) => Execute(async () =>
    {
        if (!TryRequest(VendorFinancialSurface.Outstanding, out var request, out var error))
        {
            return BadRequestError(error!);
        }

        return Ok(await service.OutstandingAsync(vendorKey, request!, ct));
    });

    [Authorize(Policy = Permissions.Export)]
    [HttpGet("vendors/{vendorKey}/outstanding/export")]
    public Task<IActionResult> OutstandingExport(string vendorKey, CancellationToken ct) => Execute(async () =>
    {
        if (!TryRequest(VendorFinancialSurface.Outstanding, out var request, out var error))
        {
            return BadRequestError(error!);
        }

        var bundle = await service.OutstandingExportAsync(vendorKey, request!, ct);
        return await ExportAsync(bundle, AuditReportTarget(ReportKeys.VendorFinancialsOutstanding, vendorKey), ct);
    });

    [Authorize(Policy = Permissions.Export)]
    [HttpGet("vendors/{vendorKey}/purchases-vs-payments/export")]
    public Task<IActionResult> PurchasesVsPaymentsExport(string vendorKey, CancellationToken ct) => Execute(async () =>
    {
        if (!TryRequest(VendorFinancialSurface.Summary, out var request, out var error))
        {
            return BadRequestError(error!);
        }

        var bundle = await service.PurchasesVsPaymentsExportAsync(vendorKey, request!, ct);
        return await ExportAsync(bundle, AuditReportTarget(ReportKeys.VendorFinancialsDetail, vendorKey), ct);
    });

    // ----- shared -------------------------------------------------------------------------------------------

    /// <summary>Validates the query (Persian 400 on the first violation, contracts/api.md §1).</summary>
    private bool TryRequest(VendorFinancialSurface surface, out VendorFinancialRequest? request, out VendorFinancialRequestError? error)
    {
        var query = Request.Query.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString(), StringComparer.Ordinal);
        return VendorFinancialRequestParser.TryParse(query, surface, Today(), out request, out error);
    }

    private IActionResult BadRequestError(VendorFinancialRequestError error) =>
        error.Code is null
            ? BadRequest(new { message = error.Message })
            : BadRequest(new { message = error.Message, code = error.Code });

    /// <summary>The .xlsx twin: workbook, sanitized filename and one report.exported entry without amounts.</summary>
    private async Task<IActionResult> ExportAsync(VendorFinancialsService.ExportBundle bundle, string auditTarget, CancellationToken ct)
    {
        var table = new ExportTable(
            ReportName: bundle.ReportName,
            PeriodLabel: bundle.PeriodLabel,
            FiltersLabel: FormatFilters(bundle.Filters),
            ExportedAtJalali: NowJalali(),
            LastSyncJalali: bundle.LastSyncJalali,
            DataLabel: bundle.DataLabel,
            DataQualityText: bundle.DataQuality is { AffectedRecords: > 0 } notice
                ? $"توجه کیفیت داده: {notice.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد."
                : null,
            Headers: bundle.Headers,
            Rows: bundle.Rows,
            TotalRows: bundle.TotalRows,
            Limit: VendorFinancialsService.ExportRowCap);

        var bytes = excel.BuildWorkbook(table);
        var filename = excel.BuildFilename(bundle.ReportName, Today());

        _ = auditLogger.RecordAsync(
            new AuditEntry(AuditActions.ReportExported, auditTarget, AuditDetail.Of(("period", bundle.PeriodLabel))),
            ct);

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
    }

    private static string AuditReportTarget(string reportKey, string? vendorKey = null) =>
        vendorKey is null ? "report:" + reportKey : $"report:{reportKey}:{vendorKey}";

    private static string FormatFilters(IReadOnlyList<VendorFinancialFilterEcho> filters) =>
        filters.Count == 0 ? "بدون فیلتر" : string.Join("، ", filters.Select(f => $"{f.Name}: {f.Label}"));

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

    /// <summary>Maps a 15 s query timeout to 503; a 404 unknown vendor keeps the Persian message.</summary>
    private static async Task<IActionResult> Execute(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ReportTimeoutException ex)
        {
            return new ObjectResult(new { message = ex.Message }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
        }
        catch (VendorFinancialVendorNotFoundException ex)
        {
            return new ObjectResult(new { message = ex.Message }) { StatusCode = StatusCodes.Status404NotFound };
        }
    }
}
