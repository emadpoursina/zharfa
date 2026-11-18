namespace Zharfa.Api.Audit;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Primitives;
using Zharfa.Api.Data;

/// <summary>
/// Builds the <c>detail</c> text of an audit row (data-model §2.1, FR-016). Only allowlisted keys are kept, values
/// are trimmed and cut, the text is cut to 500 characters, and <see cref="RedactionHelper.Scrub"/> runs last.
/// Report rows and the report-open tracker both use this class, so a report never leaks a value it does not show.
/// </summary>
public static class AuditDetail
{
    /// <summary>Query keys that describe a report's scope (period, filters, search). Anything else is dropped.</summary>
    public static readonly IReadOnlySet<string> ReportParams = new HashSet<string>(StringComparer.Ordinal)
    {
        "period", "month", "quarter", "year", "from", "to", "vendor", "vendors", "product", "category",
        "docType", "q", "match", "compare", "all", "documentId",
    };

    /// <summary>Paging and sorting keys. They never change the scope of a report (data-model §2.3).</summary>
    private static readonly IReadOnlySet<string> ViewParams = new HashSet<string>(StringComparer.Ordinal)
    {
        "page", "pageSize", "sort", "dir",
    };

    private const int MaxValueLength = 100;
    private const int MaxDetailLength = 500;

    /// <summary>Audit detail of a report request: the allowlisted query keys plus any extra pairs.</summary>
    public static string? ForReport(
        IEnumerable<KeyValuePair<string, StringValues>> query,
        IEnumerable<(string Key, string? Value)>? extra = null)
    {
        IEnumerable<(string Key, string? Value)> pairs = Allowed(query, ReportParams)
            .Select(pair => (pair.Key, (string?)pair.Value));
        if (extra is not null)
        {
            pairs = pairs.Concat(extra);
        }

        return Format(pairs);
    }

    /// <summary>Audit detail built from explicit pairs (for example <c>("permission", "admin")</c>).</summary>
    public static string? Of(params (string Key, string? Value)[] pairs) => Format(pairs);

    /// <summary>The allowlisted scope keys, normalized (trimmed, empty dropped). Used by the report-open tracker.</summary>
    public static IReadOnlyDictionary<string, string?> ReportSignatureParams(IEnumerable<KeyValuePair<string, StringValues>> query) =>
        Allowed(query, ReportParams).ToDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.Ordinal);

    /// <summary>Only <c>page</c>, <c>pageSize</c>, <c>sort</c> and <c>dir</c>, trimmed, empty values dropped.</summary>
    public static IReadOnlyDictionary<string, string?> ReportViewParams(IEnumerable<KeyValuePair<string, StringValues>> query) =>
        Allowed(query, ViewParams).ToDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.Ordinal);

    private static IEnumerable<(string Key, string Value)> Allowed(
        IEnumerable<KeyValuePair<string, StringValues>> query,
        IReadOnlySet<string> allowed)
    {
        foreach (var pair in query)
        {
            if (!allowed.Contains(pair.Key))
            {
                continue;
            }

            var value = Clean(pair.Value.ToString());
            if (value is not null)
            {
                yield return (pair.Key, value);
            }
        }
    }

    private static string? Format(IEnumerable<(string Key, string? Value)> pairs)
    {
        var parts = pairs
            .Select(pair => (Key: (pair.Key ?? string.Empty).Trim(), Value: Clean(pair.Value)))
            .Where(pair => pair.Key.Length > 0 && pair.Value is not null)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}")
            .ToArray();

        if (parts.Length == 0)
        {
            return null;
        }

        var text = string.Join("; ", parts);
        if (text.Length > MaxDetailLength)
        {
            text = text[..MaxDetailLength];
        }

        var scrubbed = RedactionHelper.Scrub(text);
        return scrubbed.Length == 0 ? null : scrubbed;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var text = value.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return text.Length > MaxValueLength ? text[..MaxValueLength] : text;
    }
}
