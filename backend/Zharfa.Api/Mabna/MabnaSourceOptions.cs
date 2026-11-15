namespace Zharfa.Api.Mabna;

/// <summary>
/// Configuration for the read-only Mabna (SQL Server, <c>TotalSystem</c>) source.
///
/// Switching between the local simulator and the live server is configuration
/// only: point the connection string at one or the other. Nothing here is
/// hard-wired to a host.
///
/// Sources (never the tracked appsettings, never the frontend):
///  - Connection string: env <c>ConnectionStrings__Mabna</c> (preferred) or
///    <c>MabnaSource__ConnectionString</c>. If neither is set the connector is
///    disabled and the API starts normally.
///  - Allowlist: <c>MabnaSource__AllowedObjects__0..N</c> and/or a gitignored
///    file named by <c>MabnaSource__AllowlistFile</c> (one <c>schema.object</c>
///    or <c>object</c> per line, <c>#</c> comments allowed). Real accounting
///    table names are not committed (spec FR-02).
///
/// The connection string must never be logged; <see cref="ToString"/> is
/// overridden so an accidental log of the options object cannot leak it.
/// </summary>
public sealed class MabnaSourceOptions
{
    public const string SectionName = "MabnaSource";

    /// <summary>Hard ceilings that configuration cannot exceed.</summary>
    public const int MaxRowsCeiling = 100_000;
    public const int CommandTimeoutCeilingSeconds = 300;

    public string? ConnectionString { get; set; }

    /// <summary>Per-command timeout (seconds). Clamped to 1..<see cref="CommandTimeoutCeilingSeconds"/>.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Connect timeout (seconds) applied to the connection string.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>Maximum rows returned per read. Clamped to 1..<see cref="MaxRowsCeiling"/>.</summary>
    public int MaxRows { get; set; } = 10_000;

    /// <summary>Allowlisted tables/views (<c>schema.object</c> or <c>object</c>, default schema <c>dbo</c>).</summary>
    public List<string> AllowedObjects { get; set; } = new();

    /// <summary>Optional path to a gitignored allowlist file (e.g. under <c>scratch/</c> or a Docker secret).</summary>
    public string? AllowlistFile { get; set; }

    /// <summary>
    /// Check the login's write/DDL permissions before the first read
    /// (<see cref="MabnaReadOnlyGate"/>). Default on.
    /// </summary>
    public bool VerifyReadOnlyLogin { get; set; } = true;

    /// <summary>
    /// Explicit override (<c>MabnaSource__AllowWritableLogin=true</c>): let the
    /// connector run even though the permission check found the login can
    /// write. Only for a short-lived, owner-approved situation; the app-side
    /// guards still apply but are the secondary control.
    /// </summary>
    public bool AllowWritableLogin { get; set; }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(ConnectionString);

    public int EffectiveMaxRows => Math.Clamp(MaxRows, 1, MaxRowsCeiling);

    public int EffectiveCommandTimeoutSeconds => Math.Clamp(CommandTimeoutSeconds, 1, CommandTimeoutCeilingSeconds);

    public override string ToString() =>
        $"MabnaSource(enabled={IsEnabled}, maxRows={EffectiveMaxRows}, commandTimeout={EffectiveCommandTimeoutSeconds}s, " +
        $"verifyReadOnlyLogin={VerifyReadOnlyLogin}, allowWritableLogin={AllowWritableLogin})";
}
