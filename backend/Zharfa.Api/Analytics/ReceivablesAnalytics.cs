// ============================================================================
// Receivables KPI Service — Zharfa Demo 1 (FR-003, FR-005, FR-012)
//
// Documented financial formulas (binding, verbatim from migration 0002_kpi_views.sql):
//   1. cash_received = SUM(receipts.amount) in range
//      (period-scoped: only receipts with received_at in [p_from, p_to) are summed).
//   2. receivables_balance = SUM(posted invoice net) + opening_balance - SUM(receipts)
//      (lifetime-to-date: cumulative across all time; the period filter scopes ONLY cash_received).
//   3. sales_revenue = SUM(qty * unit_price - line_discount)
//      (separate formula, period-scoped over posted invoices; sales revenue is NEVER equated with cash received).
//
// Reads approved database views only:
//   - v_receivables(p_from, p_to)
//   - v_receivable_rows(p_from, p_to)
// All KPI calculations and balance rules are enforced server-side inside PostgreSQL.
// ============================================================================

namespace Zharfa.Api.Analytics;

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

public record ReceivablesTotalsDto(
    string Receivables,
    string PaymentsReceived);

public record TopDebtorDto(
    long CustomerId,
    string Name,
    string Balance);

public record ReceivablesSummaryResponse(
    bool DemoData,
    PeriodDto Period,
    ReceivablesTotalsDto Totals,
    IReadOnlyList<TopDebtorDto> TopDebtors,
    string View);

public record ReceivableRowDto(
    long CustomerId,
    string Name,
    string OpeningBalance,
    string InvoicedNet,
    string ReceivedTotal,
    string ReceivedInPeriod,
    string Balance);

public record ReceivablesRowsResponse(
    bool DemoData,
    PeriodDto Period,
    bool Truncated,
    IReadOnlyList<ReceivableRowDto> Rows,
    string View);

public interface IReceivablesAnalytics
{
    Task<ReceivablesSummaryResponse> GetSummaryAsync(DateRange range, CancellationToken cancellationToken = default);
    Task<ReceivablesSummaryResponse> GetSummaryAsync(string? from, string? to, CancellationToken cancellationToken = default);
    Task<ReceivablesRowsResponse> GetRowsAsync(DateRange range, CancellationToken cancellationToken = default);
    Task<ReceivablesRowsResponse> GetRowsAsync(string? from, string? to, CancellationToken cancellationToken = default);
}

/// <summary>
/// Receivables KPI service reading approved semantic database views only.
/// KPI calculations and rules are enforced server-side exclusively by the database views.
/// </summary>
public class ReceivablesAnalytics : IReceivablesAnalytics
{
    private readonly string _connectionString;
    private readonly ILogger<ReceivablesAnalytics> _logger;

    public ReceivablesAnalytics(IConfiguration configuration, ILogger<ReceivablesAnalytics> logger)
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

    public Task<ReceivablesSummaryResponse> GetSummaryAsync(string? from, string? to, CancellationToken cancellationToken = default)
    {
        var range = PeriodFilters.Resolve(from, to);
        return GetSummaryAsync(range, cancellationToken);
    }

    public async Task<ReceivablesSummaryResponse> GetSummaryAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        var periodInfo = PeriodFilters.BuildPeriodInfo(range);
        var periodDto = PeriodDto.Create(periodInfo);

        const string sql = "SELECT * FROM v_receivables(@p_from, @p_to);";

        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);

        cmd.Parameters.Add(new NpgsqlParameter("p_from", NpgsqlDbType.Date) { Value = range.From });
        cmd.Parameters.Add(new NpgsqlParameter("p_to", NpgsqlDbType.Date) { Value = range.To });

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return new ReceivablesSummaryResponse(
                DemoData: true,
                Period: periodDto,
                Totals: new ReceivablesTotalsDto("0.00", "0.00"),
                TopDebtors: Array.Empty<TopDebtorDto>(),
                View: "v_receivables");
        }

        decimal totalReceivables = reader.IsDBNull(0) ? 0.00m : reader.GetDecimal(0);
        decimal paymentsReceived = reader.IsDBNull(1) ? 0.00m : reader.GetDecimal(1);
        string topDebtorsJson = reader.IsDBNull(2) ? "[]" : reader.GetString(2);

        var totals = new ReceivablesTotalsDto(
            Receivables: KpiFormatters.FormatAmount(totalReceivables),
            PaymentsReceived: KpiFormatters.FormatAmount(paymentsReceived));

        var topDebtors = ParseTopDebtors(topDebtorsJson);

        return new ReceivablesSummaryResponse(
            DemoData: true,
            Period: periodDto,
            Totals: totals,
            TopDebtors: topDebtors,
            View: "v_receivables");
    }

    public Task<ReceivablesRowsResponse> GetRowsAsync(string? from, string? to, CancellationToken cancellationToken = default)
    {
        var range = PeriodFilters.Resolve(from, to);
        return GetRowsAsync(range, cancellationToken);
    }

    public async Task<ReceivablesRowsResponse> GetRowsAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        var periodInfo = PeriodFilters.BuildPeriodInfo(range);
        var periodDto = PeriodDto.Create(periodInfo);

        const string sql = "SELECT * FROM v_receivable_rows(@p_from, @p_to) LIMIT 1000;";

        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);

        cmd.Parameters.Add(new NpgsqlParameter("p_from", NpgsqlDbType.Date) { Value = range.From });
        cmd.Parameters.Add(new NpgsqlParameter("p_to", NpgsqlDbType.Date) { Value = range.To });

        var rows = new List<ReceivableRowDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            long customerId = reader.GetInt64(0);
            string name = reader.GetString(1);
            decimal openingBalance = reader.IsDBNull(2) ? 0.00m : reader.GetDecimal(2);
            decimal invoicedNet = reader.IsDBNull(3) ? 0.00m : reader.GetDecimal(3);
            decimal receivedTotal = reader.IsDBNull(4) ? 0.00m : reader.GetDecimal(4);
            decimal receivedInPeriod = reader.IsDBNull(5) ? 0.00m : reader.GetDecimal(5);
            decimal balance = reader.IsDBNull(6) ? 0.00m : reader.GetDecimal(6);

            rows.Add(new ReceivableRowDto(
                CustomerId: customerId,
                Name: name,
                OpeningBalance: KpiFormatters.FormatAmount(openingBalance),
                InvoicedNet: KpiFormatters.FormatAmount(invoicedNet),
                ReceivedTotal: KpiFormatters.FormatAmount(receivedTotal),
                ReceivedInPeriod: KpiFormatters.FormatAmount(receivedInPeriod),
                Balance: KpiFormatters.FormatAmount(balance)));
        }

        bool truncated = rows.Count >= 1000;

        return new ReceivablesRowsResponse(
            DemoData: true,
            Period: periodDto,
            Truncated: truncated,
            Rows: rows,
            View: "v_receivable_rows");
    }

    private static IReadOnlyList<TopDebtorDto> ParseTopDebtors(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return Array.Empty<TopDebtorDto>();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var list = new List<TopDebtorDto>();

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                long customerId = element.TryGetProperty("customerId", out var cProp) ? cProp.GetInt64() : 0;
                string name = element.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
                string balance = "0.00";
                if (element.TryGetProperty("balance", out var bProp))
                {
                    if (bProp.ValueKind == JsonValueKind.Number)
                    {
                        balance = KpiFormatters.FormatAmount(bProp.GetDecimal());
                    }
                    else if (bProp.ValueKind == JsonValueKind.String && decimal.TryParse(bProp.GetString(), CultureInfo.InvariantCulture, out var parsed))
                    {
                        balance = KpiFormatters.FormatAmount(parsed);
                    }
                    else if (bProp.ValueKind == JsonValueKind.String)
                    {
                        balance = bProp.GetString() ?? "0.00";
                    }
                }

                list.Add(new TopDebtorDto(customerId, name, balance));
            }

            return list;
        }
        catch
        {
            return Array.Empty<TopDebtorDto>();
        }
    }
}
