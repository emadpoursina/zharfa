namespace Zharfa.Api.Platform;

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Zharfa.Api.Analytics;
using Zharfa.Api.Data;
using Zharfa.Api.Mabna.Sync;

/// <summary>
/// <c>GET /api/system/status</c> (contracts/api.md §1): the install's data mode and
/// label, the last successful sync, and which modules exist in this install. Any
/// signed-in user may read it. <c>modules.dataSync</c> is true only for the Admin
/// role; <c>demoPages</c> and <c>ai</c> are true only in demo mode.
/// </summary>
[ApiController]
[Route("api/system")]
[Authorize]
public sealed class SystemStatusController(
    DataModeOptions dataMode,
    IOptions<SyncOptions> syncOptions,
    ZharfaDbContext db) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var zone = SyncOptions.ResolveTimeZone(syncOptions.Value.TimeZone);
        var lastSucceededUtc = await LastSucceededAtUtcAsync(cancellationToken);

        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var todayJalali = JalaliDate.FromDateTime(nowLocal);

        return Ok(new
        {
            dataMode = dataMode.Mode,
            dataLabel = dataMode.DataLabel,
            lastSuccessfulSync = lastSucceededUtc is null ? null : Describe(lastSucceededUtc.Value, zone),
            modules = SystemModules.For(dataMode, User.IsInRole("admin")),
            serverJalaliToday = $"{todayJalali.Year.ToString("D4", CultureInfo.InvariantCulture)}-" +
                                $"{todayJalali.Month.ToString("D2", CultureInfo.InvariantCulture)}-" +
                                $"{todayJalali.Day.ToString("D2", CultureInfo.InvariantCulture)}",
        });
    }

    /// <summary>Finish time (UTC) of the newest <c>succeeded</c> sync job, or null when none exists.</summary>
    private async Task<DateTime?> LastSucceededAtUtcAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Database
            .SqlQueryRaw<DateTime>(
                "SELECT finished_at FROM sync_job " +
                "WHERE status = 'succeeded' AND finished_at IS NOT NULL " +
                "ORDER BY finished_at DESC LIMIT 1")
            .ToListAsync(cancellationToken);

        return rows.Count == 0 ? null : DateTime.SpecifyKind(rows[0], DateTimeKind.Utc);
    }

    /// <summary>
    /// <c>{ at, atJalali }</c>: <c>at</c> is ISO 8601 with the zone's offset; <c>atJalali</c>
    /// is the Jalali date and time in the sync time zone with Persian digits.
    /// </summary>
    private static object Describe(DateTime utc, TimeZoneInfo zone)
    {
        var offset = zone.GetUtcOffset(utc);
        var at = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(offset);
        var local = at.DateTime;
        var jalali = JalaliDate.FromDateTime(local);

        var jalaliText =
            $"{jalali.Year.ToString("D4", CultureInfo.InvariantCulture)}/" +
            $"{jalali.Month.ToString("D2", CultureInfo.InvariantCulture)}/" +
            $"{jalali.Day.ToString("D2", CultureInfo.InvariantCulture)} " +
            $"{local.Hour.ToString("D2", CultureInfo.InvariantCulture)}:" +
            $"{local.Minute.ToString("D2", CultureInfo.InvariantCulture)}";

        return new
        {
            at = at.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            atJalali = JalaliDate.ToPersianDigits(jalaliText),
        };
    }
}
