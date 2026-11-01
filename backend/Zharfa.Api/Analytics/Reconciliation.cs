// ============================================================================
// Reconciliation Service — Zharfa Demo 1 (FR-013, FR-017, User Story 6)
//
// Two-Path Rationale (Line-sum vs Aggregate):
// ------------------------------------------
// The reconciliation check executes two INDEPENDENT computation paths over the
// approved semantic views with identical predicates to ensure financial integrity:
//
// 1. API side (Granular line & row grain):
//    - Sales:       SELECT COALESCE(SUM(net), 0) FROM v_sales_lines(@p_from, @p_to, NULL)
//    - Receivables: SELECT COALESCE(SUM(balance), 0) FROM v_receivable_rows(@p_from, @p_to)
//
// 2. View side (Semantic aggregate grain):
//    - Sales:       SELECT period_revenue FROM v_sales_overview(@p_from, @p_to, @p_from, @p_to, NULL)
//    - Receivables: SELECT total_receivables FROM v_receivables(@p_from, @p_to)
//
// Why this matters:
// If the semantic view definitions and the underlying line/row-grain data ever
// drift (e.g., due to join anomalies, filtering discrepancies, discount handling,
// or rounding divergences), the match comparison immediately evaluates to false,
// blocking demo sign-off (signOffBlocked = true) and triggering an audit log event.
//
// CRITICAL:
// In accordance with data-model.md and Demo 1 PRD §7, neither computation ever
// queries base tables directly (sales_invoice, receipt, customer). Both paths
// operate strictly over approved read-only views.
// ============================================================================

namespace Zharfa.Api.Analytics;

using System;
using System.Data;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

public record ReconciliationTotalsDto(
    [property: JsonPropertyName("sales")] string Sales,
    [property: JsonPropertyName("receivables")] string Receivables);

public record ReconciliationResult(
    [property: JsonPropertyName("match")] bool Match,
    [property: JsonPropertyName("apiTotals")] ReconciliationTotalsDto ApiTotals,
    [property: JsonPropertyName("viewTotals")] ReconciliationTotalsDto ViewTotals,
    [property: JsonPropertyName("at")] string At,
    [property: JsonPropertyName("signOffBlocked")] bool SignOffBlocked);

public interface IReconciliationService
{
    Task<ReconciliationResult> ReconcileAsync(DateRange range, CancellationToken cancellationToken = default);
}

public class ReconciliationService : IReconciliationService
{
    private readonly string _connectionString;
    private readonly ILogger<ReconciliationService> _logger;

    public ReconciliationService(IConfiguration configuration, ILogger<ReconciliationService> logger)
    {
        _connectionString = configuration.GetConnectionString("Zharfa")
            ?? throw new InvalidOperationException("Connection string 'Zharfa' is missing.");
        _logger = logger;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task<ReconciliationResult> ReconcileAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);

        await using var conn = await OpenConnectionAsync(cancellationToken);

        // Path 1: API side computations over granular row views
        var apiSalesRaw = await ExecuteScalarAsync(
            conn,
            "SELECT COALESCE(SUM(net), 0) FROM v_sales_lines(@p_from, @p_to, NULL);",
            range,
            cancellationToken);

        var apiReceivablesRaw = await ExecuteScalarAsync(
            conn,
            "SELECT COALESCE(SUM(balance), 0) FROM v_receivable_rows(@p_from, @p_to);",
            range,
            cancellationToken);

        // Path 2: View side computations over aggregate summary views
        // Note: prev args pass same range as MoM comparison is irrelevant to reconciliation
        var viewSalesRaw = await ExecuteScalarAsync(
            conn,
            "SELECT period_revenue FROM v_sales_overview(@p_from, @p_to, @p_from, @p_to, NULL);",
            range,
            cancellationToken);

        var viewReceivablesRaw = await ExecuteScalarAsync(
            conn,
            "SELECT total_receivables FROM v_receivables(@p_from, @p_to);",
            range,
            cancellationToken);

        // Amounts are invariant 2-decimal strings; compare after rounding to 2 decimals
        var roundedApiSales = Math.Round(apiSalesRaw, 2, MidpointRounding.AwayFromZero);
        var roundedApiReceivables = Math.Round(apiReceivablesRaw, 2, MidpointRounding.AwayFromZero);
        var roundedViewSales = Math.Round(viewSalesRaw, 2, MidpointRounding.AwayFromZero);
        var roundedViewReceivables = Math.Round(viewReceivablesRaw, 2, MidpointRounding.AwayFromZero);

        bool match = (roundedApiSales == roundedViewSales) && (roundedApiReceivables == roundedViewReceivables);
        bool signOffBlocked = !match;

        var apiTotals = new ReconciliationTotalsDto(
            Sales: roundedApiSales.ToString("0.00", CultureInfo.InvariantCulture),
            Receivables: roundedApiReceivables.ToString("0.00", CultureInfo.InvariantCulture));

        var viewTotals = new ReconciliationTotalsDto(
            Sales: roundedViewSales.ToString("0.00", CultureInfo.InvariantCulture),
            Receivables: roundedViewReceivables.ToString("0.00", CultureInfo.InvariantCulture));

        var at = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        return new ReconciliationResult(
            Match: match,
            ApiTotals: apiTotals,
            ViewTotals: viewTotals,
            At: at,
            SignOffBlocked: signOffBlocked);
    }

    private static async Task<decimal> ExecuteScalarAsync(
        NpgsqlConnection conn,
        string sql,
        DateRange range,
        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("p_from", NpgsqlDbType.Date) { Value = range.From });
        cmd.Parameters.Add(new NpgsqlParameter("p_to", NpgsqlDbType.Date) { Value = range.To });

        var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
        if (scalar == null || scalar is DBNull)
        {
            return 0.00m;
        }

        return Convert.ToDecimal(scalar, CultureInfo.InvariantCulture);
    }
}
