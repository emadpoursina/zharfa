namespace Zharfa.Api.Data;

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

/// <summary>
/// Service interface for recording append-only audit trail entries (FR-017).
/// </summary>
/// <!--
/// Demo 1 Audit Log Coverage Map (FR-017 / T030):
/// +--------------------------+---------------------------+----------------+-------------------------------------------------------+-----------------------------+
/// | Action Name              | Written By (Endpoint/Path)| Actor Semantics| Detail Payload Contents                               | Scrubbing & Redaction       |
/// +--------------------------+---------------------------+----------------+-------------------------------------------------------+-----------------------------+
/// | provider.status.viewed   | GET /api/admin/provider   | Admin role     | Redacted name, model, configured status (never key)   | RedactionHelper.Scrub       |
/// | reconciliation.run       | GET /api/admin/reconcile  | Admin role     | Date ranges (from/to) and match=true flag             | RedactionHelper.Scrub       |
/// | reconciliation.mismatch  | GET /api/admin/reconcile  | Admin role     | Date ranges, match=false, API & View totals for both  | RedactionHelper.Scrub       |
/// |                          |                           |                | sales and receivables (blocks sign-off)               |                             |
/// +--------------------------+---------------------------+----------------+-------------------------------------------------------+-----------------------------+
/// -->
public interface IAuditLogger
{
    Task RecordAsync(string action, string? detail, CancellationToken ct = default);
}

/// <summary>
/// Appends audit log entries to the audit_log table.
/// Captures the current authenticated actor (or 'anonymous'), scrubs details for secrets,
/// and swallows any database write failures so audit logging never disrupts application responses.
/// </summary>
public class AuditLogger : IAuditLogger
{
    private readonly ZharfaDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(
        ZharfaDbContext db,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuditLogger> logger)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task RecordAsync(string action, string? detail, CancellationToken ct = default)
    {
        try
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var rawActor = user?.Identity?.IsAuthenticated == true
                ? (!string.IsNullOrWhiteSpace(user.Identity.Name)
                    ? user.Identity.Name
                    : user.FindFirst(ClaimTypes.Name)?.Value ?? user.FindFirst("name")?.Value)
                : "anonymous";

            var actor = string.IsNullOrWhiteSpace(rawActor) ? "anonymous" : rawActor;

            var scrubbedDetail = string.IsNullOrWhiteSpace(detail)
                ? null
                : RedactionHelper.Scrub(detail);

            var entry = new AuditLog
            {
                At = DateTime.UtcNow,
                Actor = actor,
                Action = action,
                Detail = scrubbedDetail
            };

            await _db.AuditLogs.AddAsync(entry, ct);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to record audit log action '{Action}': {ErrorMessage}",
                RedactionHelper.Scrub(action),
                RedactionHelper.Scrub(ex.Message));
        }
    }
}
