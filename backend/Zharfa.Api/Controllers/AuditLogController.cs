namespace Zharfa.Api.Controllers;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zharfa.Api.Audit;
using Zharfa.Api.Data;
using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.Platform;
using Zharfa.Api.Security;

/// <summary>
/// The audit log for the Admin (contracts/api.md §3, FR-016, FR-017). Read only: there is no route that changes or
/// removes an entry. The first page of a query writes <c>audit.viewed</c> with the filter values.
/// </summary>
[ApiController]
[Route("api/admin/audit")]
[Authorize(Policy = Permissions.Admin)]
public sealed class AuditLogController(
    AuditLogService log,
    SyncRuntimeSettings runtime,
    DataModeOptions dataMode,
    IAuditLogger auditLogger) : ControllerBase
{
    private const string AuditTarget = "audit-log";

    /// <summary>GET /api/admin/audit/actions: the action catalog with Persian labels.</summary>
    [HttpGet("actions")]
    public IActionResult Actions() => Ok(new
    {
        actions = AuditActions.All.Select(action => new { code = action.Code, label = action.Label }).ToArray(),
    });

    /// <summary>GET /api/admin/audit: newest first, server-paged with the total of the filter.</summary>
    [HttpGet("")]
    public async Task<IActionResult> List(
        [FromQuery] string? user,
        [FromQuery] string? action,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken)
    {
        if (!AuditQuery.TryParse(user, action, from, to, page, pageSize, runtime.Zone, out var query, out var error))
        {
            return BadRequest(new { message = error });
        }

        var result = await log.ListAsync(query, cancellationToken);

        if (query.Page == 1)
        {
            await auditLogger.RecordAsync(
                new AuditEntry(
                    AuditActions.AuditViewed,
                    AuditTarget,
                    AuditDetail.Of(
                        ("user", query.User),
                        ("action", query.Action),
                        ("from", from?.Trim()),
                        ("to", to?.Trim()))),
                cancellationToken);
        }

        return Ok(new
        {
            dataLabel = dataMode.DataLabel,
            total = result.Total,
            page = result.Page,
            pageSize = result.PageSize,
            rows = result.Rows,
        });
    }
}
