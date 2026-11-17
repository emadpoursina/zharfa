namespace Zharfa.Api.Mabna.Sync;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Zharfa.Api.Platform;

/// <summary>
/// Result of the pre-run checks. A refusal carries the reason code of contracts/api.md §4.1
/// (<c>source-not-configured</c>, <c>source-map-invalid</c>, <c>login-not-read-only</c>).
/// </summary>
public sealed record SyncPreflightResult(
    bool Configured,
    bool MapValid,
    IReadOnlyList<string> MapProblems,
    string? RefusalReason,
    string? RefusalMessage,
    SourceMap? Map)
{
    public bool Refused => RefusalReason is not null;
}

public interface ISyncPreflight
{
    /// <param name="probeLogin">True before a run: the read-only login is checked. Status reads pass false.</param>
    Task<SyncPreflightResult> CheckAsync(bool probeLogin, CancellationToken cancellationToken);
}

/// <summary>
/// Checks, in order: connector and map configured; the map loads and passes the validator
/// (contracts/source-map.md rules 1–6); the required items are usable in this data mode; the
/// read-only login is confirmed (the probe runs only before a run). Messages are bilingual and
/// name no connection string, host, login or real object.
/// </summary>
public sealed class SyncPreflight(
    IOptions<MabnaSourceOptions> sourceOptions,
    IOptions<MabnaSyncOptions> syncOptions,
    MabnaObjectPolicy policy,
    MabnaReadOnlyGate gate,
    DataModeOptions dataMode) : ISyncPreflight
{
    public async Task<SyncPreflightResult> CheckAsync(bool probeLogin, CancellationToken cancellationToken)
    {
        if (!sourceOptions.Value.IsEnabled || !syncOptions.Value.IsConfigured)
        {
            return Refuse(false, [], "source-not-configured",
                "همگام‌سازی پیکربندی نشده است (ConnectionStrings__Mabna و MabnaSync__SourceMapFile). " +
                "Sync is not configured (ConnectionStrings__Mabna and MabnaSync__SourceMapFile).");
        }

        var load = SourceMap.LoadFromFile(syncOptions.Value.SourceMapFile);
        var problems = load.Problems.Select(p => p.Message).ToList();
        if (load.Map is null)
        {
            return Refuse(true, problems, "source-map-invalid",
                "نقشه منبع معتبر نیست؛ همگام‌سازی آغاز نشد. Source map is invalid; sync not started.");
        }

        problems.AddRange(SourceMapValidator.Validate(load.Map, policy).Select(p => p.Message));
        problems.AddRange(SourceMapRequirements.Problems(load.Map, dataMode.IsProduction));
        if (problems.Count > 0)
        {
            return Refuse(true, problems, "source-map-invalid",
                "نقشه منبع معتبر نیست؛ همگام‌سازی آغاز نشد. Source map is invalid; sync not started.");
        }

        if (probeLogin)
        {
            try
            {
                await gate.EnsureReadAllowedAsync(cancellationToken);
            }
            catch (MabnaSourceUnavailableException) when (gate.State == MabnaGateState.WritableBlocked)
            {
                return Refuse(true, problems, "login-not-read-only",
                    "حساب خواندنی Mabna دسترسی نوشتن دارد؛ همگام‌سازی متوقف شد. " +
                    "The Mabna login can write; sync refused.");
            }
            catch (MabnaSourceUnavailableException)
            {
                // Unreachable server: the run starts and records its own failure.
            }
        }

        return new SyncPreflightResult(true, true, problems, null, null, load.Map);
    }

    private static SyncPreflightResult Refuse(bool configured, IReadOnlyList<string> problems, string reason, string message) =>
        new(configured, false, problems, reason, message, null);
}
