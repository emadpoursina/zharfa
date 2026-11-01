namespace Zharfa.Api.Controllers;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zharfa.Api.Ai;
using Zharfa.Api.Analytics;
using Zharfa.Api.Data;

/*
 * Verification matrix for direct-API negative authorization (T025 / check 4):
 *
 * GET /api/admin/provider        anonymous -> 401 {"message":"احراز هویت انجام نشده است یا نشست منقضی شده است."}
 * GET /api/admin/provider        manager  -> 403 {"message":"شما دسترسی لازم برای انجام این عملیات را ندارید."}
 * GET /api/admin/provider        admin    -> 200 { name, endpoint, model, configured } (never the key)
 * GET /api/admin/reconciliation  anonymous -> 401 {"message":"احراز هویت انجام نشده است یا نشست منقضی شده است."}
 * GET /api/admin/reconciliation  manager  -> 403 {"message":"شما دسترسی لازم برای انجام این عملیات را ندارید."}
 * GET /api/admin/reconciliation  admin    -> 200 { match, apiTotals, viewTotals, at, signOffBlocked }
 *
 * Both denials (401 for unauthenticated anonymous callers, 403 for authenticated non-admin roles like manager)
 * are strictly enforced server-side by ASP.NET Core cookie authentication and role-based authorization middleware,
 * completely independent of any client-side UI visibility or route guards (FR-010, check 4).
 * The same server-side authorization enforcement holds for every future admin endpoint.
 */

/// <summary>
/// Admin controller for Demo 1.
/// Admin scope for Demo 1 covers read-only provider status (GET /api/admin/provider) and
/// the financial reconciliation check (GET /api/admin/reconciliation);
/// user management, in-app provider editing, and system configuration modifications are
/// explicitly deferred to pilot (locked single-path decisions, env-only provider configuration).
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "admin")]
public class AdminController : ControllerBase
{
    private readonly IAiProvider _aiProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IReconciliationService _reconciliationService;

    public AdminController(
        IAiProvider aiProvider,
        IAuditLogger auditLogger,
        IReconciliationService reconciliationService)
    {
        _aiProvider = aiProvider;
        _auditLogger = auditLogger;
        _reconciliationService = reconciliationService;
    }

    /// <summary>
    /// Returns redacted provider configuration status for admin diagnostics.
    /// Never returns credentials or secret parameters (FR-011).
    /// </summary>
    [HttpGet("provider")]
    public async Task<IActionResult> GetProviderStatus(CancellationToken cancellationToken)
    {
        var status = _aiProvider.GetStatus();

        // Audit trail (FR-017): record access with scrubbed non-secret facts only
        await _auditLogger.RecordAsync(
            "provider.status.viewed",
            $"name={status.Name};model={status.Model};configured={status.Configured}",
            cancellationToken);

        return Ok(new
        {
            name = status.Name,
            endpoint = status.Endpoint,
            model = status.Model,
            configured = status.Configured
        });
    }

    /// <summary>
    /// Executes the financial reconciliation check between API line/row-sum totals and semantic view totals (FR-013).
    /// If there is any discrepancy, signOffBlocked is set to true and an audit mismatch is logged (FR-017).
    /// </summary>
    [HttpGet("reconciliation")]
    public async Task<IActionResult> GetReconciliation(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken cancellationToken)
    {
        var range = PeriodFilters.Resolve(from, to);
        var result = await _reconciliationService.ReconcileAsync(range, cancellationToken);

        if (result.Match)
        {
            await _auditLogger.RecordAsync(
                "reconciliation.run",
                RedactionHelper.Scrub($"from={range.From:yyyy-MM-dd}; to={range.To:yyyy-MM-dd}; match=true"),
                cancellationToken);
        }
        else
        {
            await _auditLogger.RecordAsync(
                "reconciliation.mismatch",
                RedactionHelper.Scrub($"from={range.From:yyyy-MM-dd}; to={range.To:yyyy-MM-dd}; match=false; apiSales={result.ApiTotals.Sales}; viewSales={result.ViewTotals.Sales}; apiReceivables={result.ApiTotals.Receivables}; viewReceivables={result.ViewTotals.Receivables}"),
                cancellationToken);
        }

        return Ok(result);
    }
}
