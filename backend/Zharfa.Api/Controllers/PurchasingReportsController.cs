namespace Zharfa.Api.Controllers;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Zharfa.Api.Analytics;
using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.Purchasing.Reports;

/// <summary>
/// <c>/api/purchasing/*</c> (contracts/api.md §1–§7): the vendor directory and profile, the seven purchasing
/// reports, the documents list and lines, the filter-bar lookups and the vendor comparison, each with its print
/// twin. Manager and Admin only, enforced here on the server; not demo-only (available in production and demo).
/// Every 400/404 carries a Persian <c>message</c>; a query over its 15 s budget answers 503.
/// </summary>
[ApiController]
[Route("api/purchasing")]
[Authorize(Roles = "manager,admin")]
public sealed class PurchasingReportsController(
    PurchasingReportService reports,
    VendorDirectoryService directory,
    VendorComparisonService comparison,
    IOptions<SyncOptions> syncOptions,
    TimeProvider clock) : ControllerBase
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
    public Task<IActionResult> Report(string reportId, CancellationToken ct) => RunReport(reportId, print: false, ct);

    [HttpGet("reports/{reportId}/print")]
    public Task<IActionResult> ReportPrint(string reportId, CancellationToken ct) => RunReport(reportId, print: true, ct);

    private Task<IActionResult> RunReport(string reportId, bool print, CancellationToken ct) => Execute(async () =>
    {
        if (!ReportCatalog.TryGet(reportId, out var definition) || definition is null)
        {
            return NotFound(new { message = UnknownReportMessage });
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
                    return await reports.DocumentsPrintAsync(filter, ct);
                }

                return await reports.DocumentsAsync(filter, ct);

            default:
                throw new InvalidOperationException("report id outside the catalog: " + reportId);
        }
    }

    // ----- documents (contracts/api.md §5–§6) ----------------------------------------------------------------

    [HttpGet("documents")]
    public Task<IActionResult> Documents(CancellationToken ct) => DocumentList(print: false, ct);

    [HttpGet("documents/print")]
    public Task<IActionResult> DocumentsPrint(CancellationToken ct) => DocumentList(print: true, ct);

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

    [HttpGet("documents/{documentId:long}/lines")]
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
    public Task<IActionResult> Vendors(CancellationToken ct) => VendorList(print: false, ct);

    [HttpGet("vendors/print")]
    public Task<IActionResult> VendorsPrint(CancellationToken ct) => VendorList(print: true, ct);

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

    // ----- vendor comparison (contracts/api.md §4) -----------------------------------------------------------

    [HttpGet("vendors/compare")]
    public Task<IActionResult> VendorCompare(CancellationToken ct) => VendorCompareResponse(print: false, ct);

    [HttpGet("vendors/compare/print")]
    public Task<IActionResult> VendorComparePrint(CancellationToken ct) => VendorCompareResponse(print: true, ct);

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
