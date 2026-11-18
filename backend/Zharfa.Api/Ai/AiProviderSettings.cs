namespace Zharfa.Api.Ai;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Npgsql;
using Zharfa.Api.Data;

public record AiSettingsSnapshot(
    bool CloudEnabled,
    string? ApprovalNote,
    string? ApprovedBy,
    string? ApprovedAt);

public interface IAiProviderSettings
{
    Task<bool> IsCloudEnabledAsync(CancellationToken ct);
    Task<AiSettingsSnapshot> GetSnapshotAsync(CancellationToken ct);
}

/// <summary>
/// Scoped service for reading and writing AI provider install_setting keys:
/// ai.cloud.enabled, ai.cloud.approval_note, ai.cloud.approved_by, ai.cloud.approved_at.
/// </summary>
public class AiProviderSettings(
    NpgsqlDataSource dataSource,
    ILogger<AiProviderSettings> logger) : IAiProviderSettings
{
    public const string KeyCloudEnabled = "ai.cloud.enabled";
    public const string KeyApprovalNote = "ai.cloud.approval_note";
    public const string KeyApprovedBy = "ai.cloud.approved_by";
    public const string KeyApprovedAt = "ai.cloud.approved_at";

    /// <summary>
    /// Pure helper: returns true only if value is case-insensitively "true" after trim.
    /// Absent or false or any other string ⇒ false.
    /// </summary>
    public static bool ParseEnabled(string? value) =>
        string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Pure helper: returns true only if note is non-empty and non-whitespace.
    /// </summary>
    public static bool IsUsableApprovalNote(string? note) =>
        !string.IsNullOrWhiteSpace(note);

    public async Task<bool> IsCloudEnabledAsync(CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand(
            "SELECT value FROM install_setting WHERE key = @key LIMIT 1;");
        cmd.Parameters.AddWithValue("key", KeyCloudEnabled);

        var val = await cmd.ExecuteScalarAsync(ct);
        return ParseEnabled(val as string);
    }

    public async Task<AiSettingsSnapshot> GetSnapshotAsync(CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand(
            "SELECT key, value FROM install_setting WHERE key = ANY(@keys);");
        cmd.Parameters.AddWithValue("keys", new[] { KeyCloudEnabled, KeyApprovalNote, KeyApprovedBy, KeyApprovedAt });

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var k = reader.GetString(0);
            var v = reader.GetString(1);
            dict[k] = v;
        }

        var enabled = dict.TryGetValue(KeyCloudEnabled, out var ev) && ParseEnabled(ev);
        dict.TryGetValue(KeyApprovalNote, out var note);
        dict.TryGetValue(KeyApprovedBy, out var by);
        dict.TryGetValue(KeyApprovedAt, out var at);

        return new AiSettingsSnapshot(enabled, note, by, at);
    }

    public async Task<AiSettingsSnapshot> SetCloudAsync(
        bool enabled,
        string? ownerApprovalNote,
        string adminUsername,
        CancellationToken ct)
    {
        if (enabled)
        {
            if (!IsUsableApprovalNote(ownerApprovalNote))
            {
                throw new ArgumentException("یادداشت تأیید مدیرعامل الزامی است.");
            }

            var trimmedNote = ownerApprovalNote!.Trim();
            var nowIso = DateTime.UtcNow.ToString("O");

            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            const string upsertSql =
                "INSERT INTO install_setting (key, value, set_at) VALUES (@key, @value, now()) " +
                "ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, set_at = EXCLUDED.set_at;";

            await using (var cmd = new NpgsqlCommand(upsertSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("key", KeyCloudEnabled);
                cmd.Parameters.AddWithValue("value", "true");
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await using (var cmd = new NpgsqlCommand(upsertSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("key", KeyApprovalNote);
                cmd.Parameters.AddWithValue("value", trimmedNote);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await using (var cmd = new NpgsqlCommand(upsertSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("key", KeyApprovedBy);
                cmd.Parameters.AddWithValue("value", adminUsername);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await using (var cmd = new NpgsqlCommand(upsertSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("key", KeyApprovedAt);
                cmd.Parameters.AddWithValue("value", nowIso);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);

            var scrubbedNote = RedactionHelper.Scrub(trimmedNote.Length > 100 ? trimmedNote[..100] : trimmedNote);
            logger.LogInformation("Cloud AI provider enabled by {Admin}. Approval note recorded: {Note}",
                adminUsername, scrubbedNote);

            return new AiSettingsSnapshot(true, trimmedNote, adminUsername, nowIso);
        }
        else
        {
            await using var cmd = dataSource.CreateCommand(
                "INSERT INTO install_setting (key, value, set_at) VALUES (@key, 'false', now()) " +
                "ON CONFLICT (key) DO UPDATE SET value = 'false', set_at = EXCLUDED.set_at;");
            cmd.Parameters.AddWithValue("key", KeyCloudEnabled);
            await cmd.ExecuteNonQueryAsync(ct);

            logger.LogInformation("Cloud AI provider disabled by {Admin}.", adminUsername);

            return await GetSnapshotAsync(ct);
        }
    }
}
