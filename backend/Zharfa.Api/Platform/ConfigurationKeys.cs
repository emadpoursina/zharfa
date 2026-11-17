namespace Zharfa.Api.Platform;

using System;
using System.Globalization;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Shared reader for Zharfa configuration keys written in environment-variable
/// form (<c>Zharfa__DataMode</c>, <c>Bootstrap__AdminUsername</c>, <c>Sync__PageSize</c>).
/// The .NET environment provider stores such a key under the colon form
/// (<c>Zharfa:DataMode</c>), so a literal indexer lookup with "__" never matches.
/// Every reader goes through here so the two spellings behave the same (FR-020).
/// </summary>
public static class ConfigurationKeys
{
    /// <summary>
    /// Returns the raw value of an environment-style key, or <c>null</c> when it is
    /// unset, empty or whitespace. The colon form wins; the literal form is a fallback
    /// for configuration sources that keep the "__" spelling. Not trimmed: callers
    /// decide (passwords keep their exact value).
    /// </summary>
    public static string? Get(IConfiguration configuration, string environmentKey)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentKey);

        var colonKey = environmentKey.Replace("__", ":", StringComparison.Ordinal);
        return NonBlank(configuration[colonKey]) ?? NonBlank(configuration[environmentKey]);
    }

    /// <summary>
    /// Integer form of <see cref="Get"/>. A blank value means "not set" (<c>null</c>), so the
    /// caller falls back to its default; a non-blank, non-integer value refuses startup with
    /// a message that names the key.
    /// </summary>
    public static int? GetInt(IConfiguration configuration, string environmentKey)
    {
        var raw = Get(configuration, environmentKey);
        if (raw is null)
        {
            return null;
        }

        if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException(
                $"مقدار کلید پیکربندی {environmentKey} باید عدد صحیح باشد. " +
                $"Configuration key {environmentKey} must be an integer.");
        }

        return value;
    }

    private static string? NonBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
