namespace Zharfa.Api.Controllers;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zharfa.Api.Audit;
using Zharfa.Api.Data;
using Zharfa.Api.DataQuality;
using Zharfa.Api.Security;

/// <summary>
/// Data quality for the Admin (contracts/api.md §4, FR-021–FR-025). <c>types</c> only narrows the focus for the page;
/// every issue type is always returned. Each load writes <c>data_quality.viewed</c>.
/// </summary>
[ApiController]
[Route("api/admin/data-quality")]
[Authorize(Policy = Permissions.Admin)]
public sealed class DataQualityController(DataQualityService service, IAuditLogger auditLogger) : ControllerBase
{
    private const string AuditTarget = "data-quality";

    [HttpGet("")]
    public async Task<IActionResult> Get([FromQuery] string? types, CancellationToken cancellationToken)
    {
        var focus = (types ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(DataQualityCatalog.IsIssueType)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var page = await service.GetAsync(focus, cancellationToken);

        await auditLogger.RecordAsync(
            new AuditEntry(
                AuditActions.DataQualityViewed,
                AuditTarget,
                AuditDetail.Of(("types", focus.Length == 0 ? null : string.Join(",", focus)))),
            cancellationToken);

        return Ok(page);
    }
}
