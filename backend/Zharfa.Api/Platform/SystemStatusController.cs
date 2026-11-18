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
using Zharfa.Api.Security;

/// <summary>
/// <c>GET /api/system/status</c> (contracts/api.md §7): the install's data mode and
/// label, the last successful sync, the caller's role and permissions, and which modules
/// exist for this caller. Any signed-in user may read it. The module flags come from the
/// caller's permission set (<see cref="SystemModules"/>); the server enforces the same rules
/// on every endpoint.
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

        var role = User.RoleId();
        var granted = User.Permissions();

        return Ok(new
        {
            dataMode = dataMode.Mode,
            dataLabel = dataMode.DataLabel,
            lastSuccessfulSync = lastSucceededUtc is null ? null : Describe(lastSucceededUtc.Value, zone),
            role,
            roleLabel = Roles.Label(role),
            permissions = Permissions.All.Where(permission => granted.Contains(permission)).ToArray(),
            modules = SystemModules.For(dataMode, granted),
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
