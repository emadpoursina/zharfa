namespace Zharfa.Api.Mabna;

using System.Text.RegularExpressions;

/// <summary>A validated, allowlisted SQL Server object reference.</summary>
public sealed record MabnaObjectName(string Schema, string Name)
{
    /// <summary>Bracket-quoted two-part name. Safe because identifiers are restricted to <c>[A-Za-z0-9_]</c>.</summary>
    public string Quoted => $"[{Schema}].[{Name}]";

    public override string ToString() => $"{Schema}.{Name}";
}

/// <summary>
/// Safeguard 1 (app side): which tables/views the connector may name at all.
///
/// Pure functions, no I/O. Order of checks:
///  1. Identifier shape — ASCII letters, digits, underscore only, ≤128 chars,
///     at most two parts (<c>schema.object</c>). This rules out quoting tricks,
///     three/four-part names (cross-database / linked server) and homoglyphs.
///  2. Hard exclusions (hardcoded, cannot be configured away): <c>scuUser</c>,
///     <c>UserRights</c>, <c>users</c>, and every <c>scu*</c> / <c>msg*</c>
///     object, compared case-insensitively. These hold user, credential and
///     messaging data (spec User Story 2, Clarifications 2026-11-14: the
///     current login CAN read <c>scuUser</c>, so this check is the only barrier).
///  3. Allowlist membership (from configuration only — real table names are
///     not committed, spec FR-02). An empty allowlist denies everything.
///
/// Exclusions win over the allowlist: an excluded name in configuration is
/// dropped and reported, never honoured.
/// </summary>
public sealed class MabnaObjectPolicy
{
    public const string DefaultSchema = "dbo";

    private static readonly Regex IdentifierRegex = new("^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant);

    private static readonly string[] ExcludedExactNames = { "scuUser", "UserRights", "users" };
    private static readonly string[] ExcludedPrefixes = { "scu", "msg" };

    private readonly Dictionary<string, MabnaObjectName> _allowed;

    public MabnaObjectPolicy(IEnumerable<string> allowedObjects)
    {
        _allowed = new Dictionary<string, MabnaObjectName>(StringComparer.OrdinalIgnoreCase);
        var rejected = new List<string>();
        foreach (var raw in allowedObjects)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (TryParse(raw.Trim(), out var name, out _) && !IsExcluded(name.Name))
            {
                _allowed[name.ToString()] = name;
            }
            else
            {
                rejected.Add(raw.Trim());
            }
        }

        RejectedConfigEntries = rejected;
    }

    /// <summary>Configured entries that were invalid or hit a hard exclusion (safe to log: names only).</summary>
    public IReadOnlyList<string> RejectedConfigEntries { get; }

    public int AllowedCount => _allowed.Count;

    /// <summary>Validated allowlist (names only; safe to log).</summary>
    public IReadOnlyCollection<MabnaObjectName> AllowedObjects => _allowed.Values;

    /// <summary>True for any hardcoded exclusion. Case-insensitive.</summary>
    public static bool IsExcluded(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
        {
            return true;
        }

        foreach (var exact in ExcludedExactNames)
        {
            if (string.Equals(objectName, exact, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var prefix in ExcludedPrefixes)
        {
            if (objectName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsValidIdentifier(string? identifier) =>
        identifier is not null && IdentifierRegex.IsMatch(identifier);

    /// <summary>Parses <c>object</c> or <c>schema.object</c> (no brackets, no quotes).</summary>
    public static bool TryParse(string raw, out MabnaObjectName name, out string? error)
    {
        name = new MabnaObjectName(DefaultSchema, string.Empty);
        var parts = raw.Split('.');
        if (parts.Length is < 1 or > 2)
        {
            error = "object name must be 'object' or 'schema.object'";
            return false;
        }

        var schema = parts.Length == 2 ? parts[0] : DefaultSchema;
        var obj = parts[^1];
        if (!IsValidIdentifier(schema) || !IsValidIdentifier(obj))
        {
            error = "object name contains characters outside [A-Za-z0-9_]";
            return false;
        }

        name = new MabnaObjectName(schema, obj);
        error = null;
        return true;
    }

    /// <summary>
    /// Resolves a requested object to an allowlisted name, or throws
    /// <see cref="MabnaAccessDeniedException"/>. Runs before any SQL is built.
    /// </summary>
    public MabnaObjectName Authorize(string requested)
    {
        if (!TryParse(requested ?? string.Empty, out var name, out var error))
        {
            throw new MabnaAccessDeniedException($"Mabna object rejected: {error}.");
        }

        if (IsExcluded(name.Name))
        {
            throw new MabnaAccessDeniedException($"Mabna object '{name}' is permanently excluded.");
        }

        if (!_allowed.TryGetValue(name.ToString(), out var allowed))
        {
            throw new MabnaAccessDeniedException($"Mabna object '{name}' is not on the allowlist.");
        }

        // Return the configured spelling so the emitted SQL matches the allowlist entry.
        return allowed;
    }

    /// <summary>Loads allowlist entries from options plus the optional allowlist file.</summary>
    public static IEnumerable<string> LoadEntries(MabnaSourceOptions options)
    {
        foreach (var entry in options.AllowedObjects)
        {
            yield return entry;
        }

        if (string.IsNullOrWhiteSpace(options.AllowlistFile) || !File.Exists(options.AllowlistFile))
        {
            yield break;
        }

        foreach (var line in File.ReadAllLines(options.AllowlistFile))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            yield return trimmed;
        }
    }
}

/// <summary>Thrown when a request names a non-allowlisted or excluded object, or fails a shape check.</summary>
public sealed class MabnaAccessDeniedException(string message) : Exception(message);

/// <summary>Thrown when the connector is disabled, blocked by the read-only check, or the server is unreachable.</summary>
public sealed class MabnaSourceUnavailableException(string message) : Exception(message);
