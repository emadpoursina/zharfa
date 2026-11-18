namespace Zharfa.Api.Data;

using System;
using System.Text.RegularExpressions;

/// <summary>
/// Redaction helper to scrub secrets and sensitive material from log messages.
/// Pattern matches api-key, password, pwd, secret, token, connection string, bearer, authorization.
/// </summary>
public static class RedactionHelper
{
    private static readonly Regex SecretRegex = new(
        @"\b(?:authorization\s*[:=]\s*)?bearer(?:\s+token)?(?:\s*[=:]\s*|\s+)\S+|(api[_-]?key|password|pwd|secret|token|connection\s*string|authorization)\s*[=:]\s*\S+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Scrub(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var scrubbed = SecretRegex.Replace(input, "[REDACTED]");
        return scrubbed.Replace("\r", " ").Replace("\n", " ").Trim();
    }
}
