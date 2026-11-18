namespace Zharfa.Api.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Zharfa.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Zharfa.Api.Data;
using Zharfa.Api.Mabna.Sync;

/// <summary>
/// Data sync for the Admin (contracts/api.md §4). Every action requires the <c>admin</c> role on
/// the server; the UI only hides the page. Runs are background jobs: POST returns at once.
/// Start and refusal decisions are audited (<c>sync.requested</c>, <c>sync.rejected</c>).
/// </summary>
[ApiController]
[Route("api/admin/sync")]
[Authorize(Policy = Permissions.Admin)]
public sealed class SyncAdminController(
    SyncCoordinator coordinator,
    SyncRuntimeSettings runtime,
    IAuditLogger auditLogger) : ControllerBase
{
    /// <summary>POST /api/admin/sync/run: 202 started, 409 a run is active, 503 cannot start.</summary>
    [HttpPost("run")]
    public async Task<IActionResult> Run(CancellationToken cancellationToken)
    {
        var outcome = await coordinator.RequestRunAsync(SyncTrigger.Manual, User.Identity?.Name, cancellationToken);
        switch (outcome)
        {
            case SyncStarted started:
                await auditLogger.RecordAsync("sync.requested", $"job={started.Job.Id}", cancellationToken);
                return Accepted(new { job = JobView(started.Job) });

            case SyncConflict conflict:
                await auditLogger.RecordAsync("sync.rejected", "reason=already-running", cancellationToken);
                return Conflict(new
                {
                    message = "یک همگام‌سازی در حال اجراست.",
                    job = conflict.RunningJob is null ? null : JobView(conflict.RunningJob),
                });

            case SyncRejected rejected:
                await auditLogger.RecordAsync("sync.rejected", $"reason={rejected.Reason}", cancellationToken);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = rejected.Message,
                    reason = rejected.Reason,
                });

            case SyncSkipped skipped:
                // Not reachable for a manual request; answered as a conflict with the skipped job.
                return Conflict(new { message = "یک همگام‌سازی در حال اجراست.", job = JobView(skipped.Job) });

            default:
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "خطایی در پردازش درخواست رخ داد." });
        }
    }

    /// <summary>GET /api/admin/sync/jobs?limit=50: newest first, the running job, source status and schedule.</summary>
    [HttpGet("jobs")]
    public async Task<IActionResult> Jobs([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        if (limit < 1 || limit > 200)
        {
            return BadRequest(new { message = "مقدار limit باید بین ۱ و ۲۰۰ باشد." });
        }

        var overview = await coordinator.GetOverviewAsync(limit, cancellationToken);
        var schedule = overview.Schedule;
        return Ok(new
        {
            jobs = overview.Jobs.Select(JobView).ToList(),
            running = overview.Running is null ? null : JobView(overview.Running),
            source = new
            {
                configured = overview.Source.Configured,
                mapValid = overview.Source.MapValid,
                mapProblems = overview.Source.MapProblems,
            },
            schedule = new
            {
                nightlyTime = schedule.NightlyTime,
                timeZone = schedule.TimeZone,
                nextRunAt = SyncTimeText.Iso(schedule.NextRunAt, runtime.Zone),
                nextRunAtJalali = schedule.NextRunAtJalali,
            },
        });
    }

    /// <summary>GET /api/admin/sync/jobs/{id}: one job with its per-table counts; 404 when unknown.</summary>
    [HttpGet("jobs/{id:long}")]
    public async Task<IActionResult> Job(long id, CancellationToken cancellationToken)
    {
        var job = await coordinator.GetJobAsync(id, cancellationToken);
        if (job is null)
        {
            return NotFound(new { message = "همگام‌سازی با این شناسه یافت نشد." });
        }

        return Ok(JobView(job));
    }

    /// <summary>The SyncJob shape of contracts/api.md §4 (timestamps as ISO with offset and Jalali text).</summary>
    private object JobView(SyncJobRecord job) => new
    {
        id = job.Id,
        trigger = SyncVocabulary.TriggerText(job.Trigger),
        mode = SyncVocabulary.ModeText(job.Mode),
        status = job.Status,
        requestedBy = job.RequestedBy,
        startedAt = SyncTimeText.Iso(job.StartedAt, runtime.Zone),
        startedAtJalali = SyncTimeText.Jalali(job.StartedAt, runtime.Zone),
        finishedAt = job.FinishedAt is { } finished ? SyncTimeText.Iso(finished, runtime.Zone) : null,
        finishedAtJalali = job.FinishedAt is { } finishedJalali ? SyncTimeText.Jalali(finishedJalali, runtime.Zone) : null,
        durationMs = job.DurationMs,
        error = job.Error,
        skipReason = job.SkipReason,
        tables = job.Tables.Select(t => new
        {
            table = t.Table,
            read = t.Read,
            inserted = t.Inserted,
            updated = t.Updated,
            deleted = t.Deleted,
            rejected = t.Rejected,
        }).ToList(),
    };
}
