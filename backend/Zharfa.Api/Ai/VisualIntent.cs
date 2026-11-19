namespace Zharfa.Api.Ai;

using System;
using System.Collections.Generic;

/// <summary>
/// Deterministic classifier for ranking and comparison list visual intent (spec 007 FR-001, research R1–R2).
/// When true, and the tool execution returns a usable row set without error, the response includes a server-built
/// table and optional chart.
/// Pure: no database, no LLM.
/// </summary>
public static class VisualIntent
{
    private static readonly HashSet<string> VisualCapableTools = new(StringComparer.Ordinal)
    {
        "purchasing_top_products",
        "purchasing_by_vendor",
        "purchasing_by_product",
        "purchasing_by_category",
        "purchasing_returns",
        "purchasing_transactions_summary",
        "purchasing_overview",
        "purchasing_trend",
        "vendor_financials_list"
    };

    private static readonly string[] RankingTokens =
    [
        "بیشترین",
        "پرمصرف",
        "برتر",
        "top",
        "رتبه",
        "۱۰",
        "10",
        "ده تا",
        "ده کالا",
        "پرخرید",
    ];

    private static readonly string[] ComparisonTokens =
    [
        "مقایسه",
        "compare",
    ];

    /// <summary>
    /// Checks whether the question asks for a ranking or comparison list visual (table/chart).
    /// </summary>
    public static bool IsListVisual(string normalizedQuestion, string? mappedTool)
    {
        if (string.IsNullOrWhiteSpace(normalizedQuestion) || mappedTool == null)
        {
            return false;
        }

        if (!VisualCapableTools.Contains(mappedTool))
        {
            return false;
        }

        // If mapped to trend, only visual if explicitly comparison
        if (mappedTool == "purchasing_trend")
        {
            return HasAnyToken(normalizedQuestion, ComparisonTokens);
        }

        // If mapped to overview, only visual if ranking top vendors/products (e.g. بیشترین / برتر / پرمصرف)
        // A single KPI comparison like "تغییر" / "نسبت به" without ranking tokens is not visual (fixture 2, 11)
        if (mappedTool == "purchasing_overview")
        {
            return HasAnyToken(normalizedQuestion, RankingTokens);
        }

        // If mapped to category or general grouped report, check if ranking or comparison tokens are present
        // (fixture 6 has "تفکیک" without ranking or comparison tokens -> not visual)
        var hasRanking = HasAnyToken(normalizedQuestion, RankingTokens);
        var hasComparison = HasAnyToken(normalizedQuestion, ComparisonTokens);

        if (hasRanking || hasComparison)
        {
            return true;
        }

        // purchasing_top_products is inherently a ranking visual tool
        if (mappedTool == "purchasing_top_products")
        {
            return true;
        }

        return false;
    }

    private static bool HasAnyToken(string text, string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
