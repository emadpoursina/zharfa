namespace Zharfa.Api.Mabna;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>Comparison operators the gateway can emit. Nothing else is expressible.</summary>
public enum MabnaFilterOp
{
    Eq,
    Ne,
    Lt,
    Le,
    Gt,
    Ge,
    IsNull,
    IsNotNull,
}

/// <summary>One parameterized predicate. <see cref="Value"/> is always sent as a parameter, never inlined.</summary>
public sealed record MabnaFilter(string Column, MabnaFilterOp Op, object? Value = null);

public sealed record MabnaOrder(string Column, bool Descending = false);

/// <summary>
/// Structured read request. Callers describe WHAT to read (object, columns,
/// filters, order); only <see cref="MabnaSqlBuilder"/> turns it into SQL.
/// There is deliberately no way to pass raw SQL text.
/// </summary>
public sealed record MabnaQuery(
    string Object,
    IReadOnlyList<string> Columns,
    IReadOnlyList<MabnaFilter>? Filters = null,
    IReadOnlyList<MabnaOrder>? OrderBy = null,
    int? Top = null);

public sealed record MabnaBuiltCommand(string Sql, IReadOnlyList<KeyValuePair<string, object?>> Parameters, int RowLimit);

/// <summary>
/// Safeguard 2 (app side): build a single SELECT from structured input, then
/// independently re-check the generated text (<see cref="MabnaSqlGuard"/>).
/// Pure functions, no I/O.
/// </summary>
public static class MabnaSqlBuilder
{
    public const int MaxColumns = 64;
    public const int MaxFilters = 16;

    /// <param name="objectName">Already authorized by <see cref="MabnaObjectPolicy.Authorize"/>.</param>
    /// <param name="maxRows">Effective row ceiling from options.</param>
    public static MabnaBuiltCommand Build(MabnaObjectName objectName, MabnaQuery query, int maxRows)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Columns is null || query.Columns.Count == 0)
        {
            throw new MabnaAccessDeniedException("Mabna query must name explicit columns ('*' is not allowed).");
        }

        if (query.Columns.Count > MaxColumns)
        {
            throw new MabnaAccessDeniedException($"Mabna query names more than {MaxColumns} columns.");
        }

        var filters = query.Filters ?? Array.Empty<MabnaFilter>();
        if (filters.Count > MaxFilters)
        {
            throw new MabnaAccessDeniedException($"Mabna query has more than {MaxFilters} filters.");
        }

        // Row limit: never above the configured ceiling. TOP (limit + 1) lets the
        // reader detect truncation without reading an unbounded result.
        var limit = Math.Clamp(query.Top ?? maxRows, 1, maxRows);

        var sql = new StringBuilder();
        sql.Append("SELECT TOP (").Append(limit + 1).Append(") ");
        sql.Append(string.Join(", ", query.Columns.Select(QuoteColumn)));
        sql.Append(" FROM ").Append(objectName.Quoted);

        var parameters = new List<KeyValuePair<string, object?>>();
        if (filters.Count > 0)
        {
            var predicates = new List<string>();
            for (var i = 0; i < filters.Count; i++)
            {
                var f = filters[i];
                var column = QuoteColumn(f.Column);
                switch (f.Op)
                {
                    case MabnaFilterOp.IsNull:
                        predicates.Add($"{column} IS NULL");
                        break;
                    case MabnaFilterOp.IsNotNull:
                        predicates.Add($"{column} IS NOT NULL");
                        break;
                    default:
                        var p = $"@p{i}";
                        predicates.Add($"{column} {OperatorText(f.Op)} {p}");
                        parameters.Add(new KeyValuePair<string, object?>(p, f.Value));
                        break;
                }
            }

            sql.Append(" WHERE ").Append(string.Join(" AND ", predicates));
        }

        if (query.OrderBy is { Count: > 0 })
        {
            sql.Append(" ORDER BY ").Append(string.Join(", ",
                query.OrderBy.Select(o => QuoteColumn(o.Column) + (o.Descending ? " DESC" : " ASC"))));
        }

        var text = sql.ToString();
        MabnaSqlGuard.AssertSingleSelect(text);
        return new MabnaBuiltCommand(text, parameters, limit);
    }

    private static string QuoteColumn(string column)
    {
        if (!MabnaObjectPolicy.IsValidIdentifier(column))
        {
            throw new MabnaAccessDeniedException("Mabna column name contains characters outside [A-Za-z0-9_].");
        }

        return $"[{column}]";
    }

    private static string OperatorText(MabnaFilterOp op) => op switch
    {
        MabnaFilterOp.Eq => "=",
        MabnaFilterOp.Ne => "<>",
        MabnaFilterOp.Lt => "<",
        MabnaFilterOp.Le => "<=",
        MabnaFilterOp.Gt => ">",
        MabnaFilterOp.Ge => ">=",
        _ => throw new MabnaAccessDeniedException("Unsupported Mabna filter operator."),
    };
}

/// <summary>
/// Independent shape check on the final SQL text, run after building and
/// again immediately before execution. It is redundant with the builder on
/// purpose (defence in depth): if a future change to the builder lets
/// something else through, this still refuses it.
///
/// Accepts only: one statement, starting with <c>SELECT</c>, no statement
/// separators, no comments, no string literals (values must be parameters),
/// and none of the write/DDL/exec/side-channel keywords outside bracketed
/// identifiers.
/// </summary>
public static class MabnaSqlGuard
{
    private static readonly Regex BracketedIdentifier = new(@"\[[A-Za-z0-9_]+\]", RegexOptions.CultureInvariant);

    private static readonly Regex ForbiddenKeyword = new(
        @"\b(INSERT|UPDATE|DELETE|MERGE|INTO|EXEC|EXECUTE|SP_\w*|XP_\w*|DROP|ALTER|CREATE|TRUNCATE|GRANT|REVOKE|DENY|" +
        @"OPENROWSET|OPENQUERY|OPENDATASOURCE|OPENXML|BULK|WAITFOR|DECLARE|SET|USE|BACKUP|RESTORE|DBCC|SHUTDOWN|KILL|" +
        @"RECONFIGURE|CHECKPOINT|READTEXT|WRITETEXT|UPDATETEXT|UNION|GO)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SelectKeyword = new(@"\bSELECT\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsSingleSelect(string? sql, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(sql))
        {
            reason = "empty statement";
            return false;
        }

        if (sql.Contains(';') || sql.Contains("--") || sql.Contains("/*") || sql.Contains("*/"))
        {
            reason = "statement separators and comments are not allowed";
            return false;
        }

        if (sql.Contains('\'') || sql.Contains('"') || sql.Contains('\0') || sql.Contains('\r') || sql.Contains('\n'))
        {
            reason = "literals, quoted identifiers and line breaks are not allowed";
            return false;
        }

        // Inspect keywords only outside [identifier] spans, so an allowlisted
        // column literally named e.g. [Update] does not trip the check, while
        // bare keywords still do.
        var bare = BracketedIdentifier.Replace(sql, "[]");
        if (bare.Replace("[]", string.Empty).IndexOfAny(['[', ']']) >= 0)
        {
            reason = "malformed bracketed identifier";
            return false;
        }

        if (!bare.TrimStart().StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase))
        {
            reason = "statement must start with SELECT";
            return false;
        }

        if (SelectKeyword.Matches(bare).Count != 1)
        {
            reason = "exactly one SELECT is allowed (no subqueries)";
            return false;
        }

        var forbidden = ForbiddenKeyword.Match(bare);
        if (forbidden.Success)
        {
            reason = $"keyword '{forbidden.Value.ToUpperInvariant()}' is not allowed";
            return false;
        }

        return true;
    }

    public static void AssertSingleSelect(string sql)
    {
        if (!IsSingleSelect(sql, out var reason))
        {
            throw new MabnaAccessDeniedException($"Mabna SQL rejected: {reason}.");
        }
    }
}
