namespace Zharfa.Api.Controllers;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Zharfa.Api.Audit;
using Zharfa.Api.Data;
using Zharfa.Api.Purchasing.Reports;
using Zharfa.Api.SavedReports;
using Zharfa.Api.Security;

/// <summary>
/// Saved reports endpoints (contracts/api.md §2).
/// Policy: purchasing.reports on class.
/// Generated kinds also require ai.query at runtime.
/// </summary>
[ApiController]
[Route("api/saved-reports")]
[Authorize(Policy = Permissions.PurchasingReports)]
public class SavedReportsController(
    SavedReportService service,
    ExcelExportService exportService,
    IAuditLogger auditLogger,
    ILogger<SavedReportsController> logger) : ControllerBase
{
    private const string NotFoundMessage = "گزارش ذخیره شده یافت نشد.";

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var userId = User.UserId();
        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        var list = await service.ListAsync(userId.Value, ct);
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveReportRequest request, CancellationToken ct)
    {
        var userId = User.UserId();
        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        if (request.Kind == "generated" && !User.HasPermission(Permissions.AiQuery))
        {
            await RespondAccessDeniedAsync(Permissions.AiQuery, "دسترسی پرسش از دستیار هوشمند (ai.query) برای این گزارش لازم است.");
            return new EmptyResult();
        }

        var result = await service.SaveAsync(userId.Value, request, User, ct);

        if (!result.Success)
        {
            return StatusCode(result.StatusCode, new { message = result.Message, code = result.Code, id = result.Id });
        }

        var nameKey = SavedReportName.NormalizeKey(result.Item!.Name);
        var target = $"saved:{result.Item.Id}:{nameKey}";
        var action = result.IsReplaced ? AuditActions.SavedReportReplaced : AuditActions.SavedReportSaved;

        _ = auditLogger.RecordAsync(
            new AuditEntry(action, target, AuditDetail.Of(("kind", result.Item.Kind), ("name", result.Item.Name))),
            ct);

        return StatusCode(result.StatusCode, result.Item);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var userId = User.UserId();
        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        var item = await service.GetByIdAsync(id, userId.Value, ct);
        if (item == null)
        {
            return NotFound(new { message = NotFoundMessage });
        }

        return Ok(item);
    }

    [HttpPost("{id}/open")]
    public async Task<IActionResult> Open(long id, CancellationToken ct)
    {
        var userId = User.UserId();
        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        var result = await service.OpenAsync(id, userId.Value, User, ct);

        if (!result.Success)
        {
            if (result.StatusCode == 404)
            {
                return NotFound(new { message = NotFoundMessage });
            }

            if (result.StatusCode == 403)
            {
                await RespondAccessDeniedAsync(result.MissingPermission!, result.ErrorMessage!);
                return new EmptyResult();
            }

            return StatusCode(result.StatusCode, new { message = result.ErrorMessage });
        }

        _ = auditLogger.RecordAsync(
            new AuditEntry(AuditActions.SavedReportOpened, $"saved:{id}"),
            ct);

        return Ok(result.Body);
    }

    [Authorize(Policy = Permissions.Export)]
    [HttpGet("{id}/export")]
    public async Task<IActionResult> Export(long id, CancellationToken ct)
    {
        var userId = User.UserId();
        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        var report = await service.GetEntityByIdAsync(id, userId.Value, ct);
        if (report == null)
        {
            return NotFound(new { message = NotFoundMessage });
        }

        if (report.Kind == "generated" && !User.HasPermission(Permissions.AiQuery))
        {
            await RespondAccessDeniedAsync(Permissions.AiQuery, "دسترسی پرسش از دستیار هوشمند (ai.query) برای این گزارش لازم است.");
            return new EmptyResult();
        }

        if (report.Kind == "transactions" && !User.HasPermission(Permissions.PurchasingTransactions))
        {
            await RespondAccessDeniedAsync(Permissions.PurchasingTransactions, "دسترسی تاریخچه تراکنش‌ها (purchasing.transactions) برای این گزارش لازم است.");
            return new EmptyResult();
        }

        var openResult = await service.OpenAsync(id, userId.Value, User, ct, pageSize: 10000);
        if (!openResult.Success)
        {
            if (openResult.StatusCode == 404)
            {
                return NotFound(new { message = NotFoundMessage });
            }
            if (openResult.StatusCode == 403)
            {
                await RespondAccessDeniedAsync(openResult.MissingPermission!, openResult.ErrorMessage!);
                return new EmptyResult();
            }
            return StatusCode(openResult.StatusCode, new { message = openResult.ErrorMessage });
        }

        var exportTable = service.BuildExportTableFromOpen(report, openResult);
        var bytes = exportService.BuildWorkbook(exportTable);
        var today = service.Today();
        var filename = exportService.BuildFilename(report.Name, today);

        _ = auditLogger.RecordAsync(
            new AuditEntry(AuditActions.ReportExported, $"saved:{report.Id}:{report.NameKey}"),
            ct);

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var userId = User.UserId();
        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        var deleted = await service.DeleteAsync(id, userId.Value, ct);
        if (!deleted)
        {
            return NotFound(new { message = NotFoundMessage });
        }

        _ = auditLogger.RecordAsync(
            new AuditEntry(AuditActions.SavedReportDeleted, $"saved:{id}"),
            ct);

        return NoContent();
    }

    private async Task RespondAccessDeniedAsync(string permission, string message)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(Response.Body, new { message, code = "forbidden" });

        try
        {
            await auditLogger.RecordAsync(
                new AuditEntry(AuditActions.AccessDenied, AccessDenied.TargetOf(HttpContext), AuditDetail.Of(("permission", permission))));
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to record access.denied: {Message}", ex.Message);
        }
    }
}
