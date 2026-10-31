namespace Zharfa.Api.Analytics;

using System.Globalization;

/// <summary>
/// Common period representation for all KPI responses.
/// Always indicates both the primary range and its comparative previous window.
/// </summary>
public record PrevPeriodDto(
    string From,
    string To,
    string JalaliLabel);

public record PeriodDto(
    string From,
    string To,
    string JalaliLabel,
    PrevPeriodDto Prev)
{
    public static PeriodDto Create(PeriodFilters.PeriodInfo info) =>
        new(
            info.From,
            info.To,
            info.JalaliLabel,
            new PrevPeriodDto(info.Prev.From, info.Prev.To, info.Prev.JalaliLabel));
}

/// <summary>
/// Generic filter criteria applied across KPI endpoints.
/// </summary>
public record FiltersDto(long? CategoryId);

/// <summary>
/// Invariant culture number and currency formatters for KPI DTOs.
/// All amounts are formatted to 2 decimal places ("0.00"), quantities to up to 3 decimals with trimmed zeros ("0.###").
/// </summary>
public static class KpiFormatters
{
    public static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static string FormatQty(decimal qty) =>
        qty.ToString("0.###", CultureInfo.InvariantCulture);

    public static string? FormatPercent(decimal? pct) =>
        pct.HasValue ? pct.Value.ToString("0.00", CultureInfo.InvariantCulture) : null;
}
