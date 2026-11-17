namespace Zharfa.Api.Platform;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Zharfa.Api.Data;

/// <summary>Outcome of the startup data-mode gate (pure, unit-testable).</summary>
public sealed record DataModeGateDecision(bool Allowed, string? Error)
{
    public static readonly DataModeGateDecision Ok = new(true, null);
}

/// <summary>
/// Startup data-mode gate (FR-020, research R12):
///  - on first start the configured mode is stored in
///    <c>install_setting(key='data_mode')</c>;
///  - a later start whose configured mode differs from the stored one is
///    refused (an existing install cannot be flipped from the outside);
///  - <c>production</c> is refused when any <c>db/seed/*</c> file name is
///    recorded in <c>schema_migrations</c> (the synthetic dataset was loaded).
/// All decisions are made by the pure <see cref="Decide"/> method.
/// </summary>
public static class DataModeGate
{
    public const string SettingKey = "data_mode";

    /// <summary>
    /// Pure decision: configured mode, stored mode (null = first start) and
    /// the seed file names recorded in schema_migrations.
    /// </summary>
    public static DataModeGateDecision Decide(
        string configured,
        string? stored,
        IEnumerable<string> appliedSeedNames)
    {
        var cfg = (configured ?? string.Empty).Trim().ToLowerInvariant();
        if (cfg != DataModeOptions.Production && cfg != DataModeOptions.Demo)
        {
            return new DataModeGateDecision(false, DataModeOptions.Validate(configured));
        }

        var seeds = (appliedSeedNames ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

        if (cfg == DataModeOptions.Production && seeds.Count > 0)
        {
            var list = string.Join("، ", seeds);
            return new DataModeGateDecision(false,
                $"این نسخه در حالت production است اما داده‌های نمایشی (seed) بارگذاری شده‌اند: {list}. " +
                "از بین بردن داده‌های نمایشی یا تغییر حالت باید توسط متخصص انجام شود. " +
                $"Production mode refuses to start while demo seed files are recorded in schema_migrations: {list}.");
        }

        if (string.IsNullOrWhiteSpace(stored))
        {
            // First start: the configured mode will be stored.
            return DataModeGateDecision.Ok;
        }

        var storedMode = stored.Trim().ToLowerInvariant();
        if (storedMode != cfg)
        {
            return new DataModeGateDecision(false,
                $"حالت داده ذخیره‌شده در این پایگاه داده ({storedMode}) با مقدار پیکربندی ({cfg}) یکسان نیست؛ " +
                "تغییر حالت داده مجاز نیست. " +
                $"Stored data mode ({storedMode}) differs from the configured mode ({cfg}); switching an install's data mode is refused.");
        }

        return DataModeGateDecision.Ok;
    }

    /// <summary>
    /// Runs the gate against the database after migrations: reads the stored
    /// mode and the recorded seed file names, applies <see cref="Decide"/>,
    /// stores the mode on first start, and throws when the decision refuses
    /// startup. Never logs secrets (only mode names and file names).
    /// </summary>
    public static async Task RunAsync(
        IConfiguration configuration,
        DataModeOptions dataMode,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("Zharfa");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // MigrationRunner fails first when no connection string is set.
            return;
        }

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        string? stored = null;
        await using (var cmd = new NpgsqlCommand(
            "SELECT value FROM install_setting WHERE key = @key;", conn))
        {
            cmd.Parameters.AddWithValue("key", SettingKey);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            if (result is string s)
            {
                stored = s;
            }
        }

        var seeds = new List<string>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT name FROM schema_migrations WHERE name LIKE '%.sql' ORDER BY name;", conn))
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                seeds.Add(reader.GetString(0));
            }
        }

        // Only names that are actual db/seed/* file names count as seeds.
        var seedDir = FindSeedDirectory();
        var seedFileNames = seedDir is null
            ? new HashSet<string>(System.StringComparer.Ordinal)
            : new HashSet<string>(
                System.IO.Directory.GetFiles(seedDir, "*.sql")
                    .Select(System.IO.Path.GetFileName)
                    .Where(n => n is not null)
                    .Select(n => n!),
                System.StringComparer.Ordinal);
        var appliedSeedNames = seeds.Where(seedFileNames.Contains).ToList();

        var decision = Decide(dataMode.Mode, stored, appliedSeedNames);
        if (!decision.Allowed)
        {
            logger.LogCritical("Data mode gate refused startup: {Error}", RedactionHelper.Scrub(decision.Error));
            throw new InvalidOperationException(decision.Error);
        }

        if (string.IsNullOrWhiteSpace(stored))
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO install_setting (key, value, set_at) VALUES (@key, @value, now()) " +
                "ON CONFLICT (key) DO NOTHING;", conn);
            insert.Parameters.AddWithValue("key", SettingKey);
            insert.Parameters.AddWithValue("value", dataMode.Mode);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            logger.LogInformation("Data mode '{Mode}' recorded in install_setting.", dataMode.Mode);
        }
    }

    /// <summary>Locates db/seed via <see cref="MigrationRunner.ResolveDbRoot"/>.</summary>
    private static string? FindSeedDirectory()
    {
        try
        {
            var dbRoot = MigrationRunner.ResolveDbRoot(NullLogger.Instance);
            var dbSeed = System.IO.Path.Combine(dbRoot, "seed");
            return System.IO.Directory.Exists(dbSeed) ? dbSeed : null;
        }
        catch (System.IO.DirectoryNotFoundException)
        {
            return null;
        }
    }
}
