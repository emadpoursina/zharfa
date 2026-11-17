namespace Zharfa.Api.Purchasing;

using System;
using System.Globalization;

/// <summary>
/// Overview comparison arithmetic (FR-025, data-model.md §4 "Comparison"). Pure: no I/O. Amounts are
/// <c>decimal</c>; the percentage is rounded to 2 decimals, half away from zero.
/// </summary>
public static class ComparisonMath
{
    /// <summary><c>current − previous</c>, exact.</summary>
    public static decimal Delta(decimal current, decimal previous) => current - previous;

    /// <summary>
    /// <c>delta / previous × 100</c> rounded to 2 decimals, or <c>null</c> when the previous net is zero
    /// (the change has no meaningful base; the Overview shows no percentage).
    /// </summary>
    public static decimal? Percent(decimal current, decimal previous)
    {
        if (previous == 0m)
        {
            return null;
        }

        return Math.Round((current - previous) * 100m / previous, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The preceding month has synced data when the first month with synced purchase documents is on or
    /// before it. No first month (empty copy) means there is no previous data.
    /// </summary>
    public static bool HasPreviousData(JalaliMonth previous, JalaliMonth? firstDataMonth) =>
        firstDataMonth is { } first && previous.Index >= first.Index;

    /// <summary>Amount as the API sends it: a decimal string with exactly 4 decimals, no grouping.</summary>
    public static string Amount(decimal value) =>
        value.ToString("0.0000", CultureInfo.InvariantCulture);

    /// <summary>Percentage as the API sends it: a decimal string with exactly 2 decimals.</summary>
    public static string Percent2(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);
}
