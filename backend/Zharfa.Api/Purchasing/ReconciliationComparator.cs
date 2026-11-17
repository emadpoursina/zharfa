namespace Zharfa.Api.Purchasing;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Metric names of the reconciliation (research R11, contracts/cli-and-config.md §1).</summary>
public static class ReconcileMetrics
{
    public const string Net = "net";
    public const string Gross = "gross";
    public const string Returns = "returns";
    public const string Vat = "vat";

    /// <summary>Order of the results in the log and the printed line.</summary>
    public static readonly IReadOnlyList<string> All = [Net, Gross, Returns, Vat];

    /// <summary>The Overview figure whose availability rule decides the metric (MetricAvailability).</summary>
    public static string FigureOf(string metric) => metric switch
    {
        Net => MetricAvailability.FigureNet,
        Gross => MetricAvailability.FigureGross,
        Returns => MetricAvailability.FigureReturns,
        Vat => MetricAvailability.FigureVat,
        _ => throw new ArgumentOutOfRangeException(nameof(metric)),
    };
}

/// <summary>Per-metric result. <see cref="Difference"/> is system − source.</summary>
public sealed record MetricComparison(
    string Metric,
    decimal? System,
    decimal? Source,
    decimal? Difference,
    string Outcome,
    string? UnavailableReason = null);

/// <summary>Outcome words of <c>reconciliation_run.outcome</c> and the printed line.</summary>
public static class ReconcileOutcome
{
    public const string Pass = "pass";
    public const string Mismatch = "mismatch";
    public const string Unavailable = "unavailable";
    public const string Error = "error";
}

/// <summary>
/// Exact comparison of one metric (FR-017, FR-018). Pure. Equality is decimal equality, so 1.0000 and
/// 1.00 are equal and no tolerance is applied. A metric whose availability rule fails is unavailable and
/// does not fail the run.
/// </summary>
public static class ReconciliationComparator
{
    public static MetricComparison Compare(string metric, decimal? system, decimal? source, string? unavailableReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metric);

        if (unavailableReason is not null)
        {
            return new MetricComparison(metric, null, null, null, ReconcileOutcome.Unavailable, unavailableReason);
        }

        if (system is null || source is null)
        {
            return new MetricComparison(metric, system, source, null, ReconcileOutcome.Error);
        }

        var difference = system.Value - source.Value;
        return new MetricComparison(
            metric,
            system,
            source,
            difference,
            difference == 0m ? ReconcileOutcome.Pass : ReconcileOutcome.Mismatch);
    }

    /// <summary>
    /// Exit code of a run (contracts/cli-and-config.md §1): 2 when any metric could not be compared,
    /// 1 when any metric mismatches, 0 otherwise (unavailable metrics do not fail).
    /// </summary>
    public static int ExitCode(IEnumerable<MetricComparison> results)
    {
        var list = results.ToList();
        if (list.Any(r => r.Outcome == ReconcileOutcome.Error))
        {
            return 2;
        }

        return list.Any(r => r.Outcome == ReconcileOutcome.Mismatch) ? 1 : 0;
    }

    /// <summary>Formats an amount with 4 decimals, as the reconciliation log stores it.</summary>
    public static string Format(decimal value) => value.ToString("0.0000", CultureInfo.InvariantCulture);
}

/// <summary>
/// The result of one reconciliation run: the printed line, the exit code and the sign-off decision.
/// An error (no sync, map invalid, source unreachable) has no results and an <see cref="ErrorReason"/>.
/// </summary>
public sealed record ReconciliationReport(
    string? Month,
    string? FilterKind,
    string? FilterValue,
    string? Basis,
    string? DataMode,
    IReadOnlyList<MetricComparison> Results,
    string? ErrorReason = null)
{
    public string Outcome =>
        ErrorReason is not null || Results.Any(r => r.Outcome == ReconcileOutcome.Error)
            ? ReconcileOutcome.Error
            : Results.Any(r => r.Outcome == ReconcileOutcome.Mismatch)
                ? ReconcileOutcome.Mismatch
                : ReconcileOutcome.Pass;

    public bool SignOffBlocked => Outcome != ReconcileOutcome.Pass;

    public int ExitCode => Outcome switch
    {
        ReconcileOutcome.Pass => 0,
        ReconcileOutcome.Mismatch => 1,
        _ => 2,
    };

    public static ReconciliationReport Failed(
        string? month, string? filterKind, string? filterValue, string? basis, string? dataMode, string reason) =>
        new(month, filterKind, filterValue, basis, dataMode, Array.Empty<MetricComparison>(), reason);

    /// <summary>
    /// The printed JSON line. In production the amounts are omitted unless <paramref name="showAmounts"/> is
    /// set (real totals stay on the on-site screen and database; FR-017).
    /// </summary>
    public string ToJsonLine(bool showAmounts)
    {
        var isProduction = string.Equals(DataMode, "production", StringComparison.Ordinal);
        var includeAmounts = !isProduction || showAmounts;

        var results = Results.Select(r => new ResultLine(
            r.Metric,
            includeAmounts && r.System is not null ? ReconciliationComparator.Format(r.System.Value) : null,
            includeAmounts && r.Source is not null ? ReconciliationComparator.Format(r.Source.Value) : null,
            includeAmounts && r.Difference is not null ? ReconciliationComparator.Format(r.Difference.Value) : null,
            r.Outcome,
            r.UnavailableReason)).ToList();

        var line = new ReportLine(
            Month,
            FilterKind is null ? null : new FilterLine(FilterKind, FilterValue),
            Basis,
            DataMode,
            results,
            Outcome,
            SignOffBlocked,
            ErrorReason);

        return JsonSerializer.Serialize(line, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed record FilterLine(string Kind, string? Value);

    private sealed record ResultLine(
        string Metric,
        string? System,
        string? Source,
        string? Difference,
        string Outcome,
        string? Reason);

    private sealed record ReportLine(
        string? Month,
        FilterLine? Filter,
        string? Basis,
        string? DataMode,
        IReadOnlyList<ResultLine> Results,
        string Outcome,
        bool SignOffBlocked,
        string? Reason);
}
