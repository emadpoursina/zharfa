namespace Zharfa.Api.Data;

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Zharfa.Api.Security;

/// <summary>
/// One audit row to write (data-model §2.1). <paramref name="ActorOverride"/> names the actor when there is no
/// signed-in user (for example the attempted username of a failed sign-in); <paramref name="ActorUserIdOverride"/>
/// names the user id in the same cases.
/// </summary>
public sealed record AuditEntry(
    string Action,
    string? Target = null,
    string? Detail = null,
    string? ActorOverride = null,
    long? ActorUserIdOverride = null);

/// <summary>
/// Service interface for recording append-only audit trail entries (FR-017).
/// For the full audit action catalog, targets and detail payloads, see
/// <see cref="Zharfa.Api.Audit.AuditActions"/> and data-model §2.2.
/// </summary>
public interface IAuditLogger
{
    Task RecordAsync(string action, string? detail, CancellationToken ct = default);

    Task RecordAsync(AuditEntry entry, CancellationToken ct = default);
}

/// <summary>
/// Appends audit log entries to the audit_log table.
/// Captures the actor (the signed-in user, the override, or 'anonymous'), scrubs every text field for secrets,
/// and swallows any database write failures so audit logging never disrupts application responses.
/// </summary>
public class AuditLogger : IAuditLogger
{
    private const int MaxActorLength = 64;
    private const int MaxTargetLength = 200;

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

    public Task RecordAsync(string action, string? detail, CancellationToken ct = default) =>
        RecordAsync(new AuditEntry(action, Detail: detail), ct);

    public async Task RecordAsync(AuditEntry entry, CancellationToken ct = default)
    {
        try
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var (actor, actorUserId) = ResolveActor(entry, user);

            var target = RedactionHelper.Scrub(entry.Target);
            var scrubbedDetail = string.IsNullOrWhiteSpace(entry.Detail)
                ? null
                : RedactionHelper.Scrub(entry.Detail);

            var row = new AuditLog
            {
                At = DateTime.UtcNow,
                Actor = actor,
                ActorUserId = actorUserId,
                Action = entry.Action,
                Target = target.Length == 0 ? null : Cut(target, MaxTargetLength),
                Detail = scrubbedDetail
            };

            await _db.AuditLogs.AddAsync(row, ct);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to record audit log action '{Action}': {ErrorMessage}",
                RedactionHelper.Scrub(entry.Action),
                RedactionHelper.Scrub(ex.Message));
        }
    }

    /// <summary>
    /// Actor name: the override, else the signed-in user's name, else <c>anonymous</c>. Actor user id: the override,
    /// else the signed-in user's <c>NameIdentifier</c>, else null. The name is scrubbed and cut to 64 characters.
    /// </summary>
    public static (string Actor, long? ActorUserId) ResolveActor(AuditEntry entry, ClaimsPrincipal? user)
    {
        var signedIn = user?.Identity?.IsAuthenticated == true;

        string? rawActor = null;
        if (!string.IsNullOrWhiteSpace(entry.ActorOverride))
        {
            rawActor = entry.ActorOverride;
        }
        else if (signedIn)
        {
            rawActor = user!.Identity?.Name;
            if (string.IsNullOrWhiteSpace(rawActor))
            {
                rawActor = user.FindFirst(ClaimTypes.Name)?.Value;
            }
        }

        var actor = Cut(RedactionHelper.Scrub(rawActor), MaxActorLength);
        if (actor.Length == 0)
        {
            actor = "anonymous";
        }

        long? actorUserId = entry.ActorUserIdOverride;
        if (actorUserId is null && signedIn)
        {
            actorUserId = user!.UserId();
        }

        return (actor, actorUserId);
    }

    private static string Cut(string value, int maxLength) =>
        value.Length > maxLength ? value[..maxLength] : value;
}
