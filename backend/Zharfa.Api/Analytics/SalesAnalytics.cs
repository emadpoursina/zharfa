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

public record SalesTotalsDto(
    string Revenue,
    string PrevRevenue,
    string MomDelta,
    string? MomPct);

public record SalesTrendPointDto(
    string Bucket,
    string Revenue);

public record TopProductDto(
    long ProductId,
    string Name,
    string Revenue);

public record TopCustomerDto(
    long CustomerId,
    string Name,
    string Revenue);

public record SalesOverviewResponse(
    bool DemoData,
    PeriodDto Period,
    FiltersDto Filters,
    SalesTotalsDto Totals,
    IReadOnlyList<SalesTrendPointDto> Trend,
    IReadOnlyList<TopProductDto> TopProducts,
    IReadOnlyList<TopCustomerDto> TopCustomers,
    string View);

public record SalesLineDto(
    long InvoiceId,
    string Date,
    string Customer,
    string Product,
    string Qty,
    string UnitPrice,
    string LineDiscount,
    string Net);

public record SalesLinesResponse(
    bool DemoData,
    PeriodDto Period,
    FiltersDto Filters,
    bool Truncated,
    IReadOnlyList<SalesLineDto> Lines,
    string View);

public record CategoryDto(
    long CategoryId,
    string Name);

public record CategoriesResponse(
    bool DemoData,
    IReadOnlyList<CategoryDto> Categories,
    string View);

public interface ISalesAnalytics
{
    Task<SalesOverviewResponse> GetOverviewAsync(DateRange range, long? categoryId, CancellationToken cancellationToken = default);
    Task<SalesLinesResponse> GetLinesAsync(DateRange range, long? categoryId, CancellationToken cancellationToken = default);
    Task<CategoriesResponse> GetCategoriesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Sales KPI service reading approved semantic database views only.
/// KPI calculations and rules are enforced server-side exclusively by the database views.
/// </summary>
public class SalesAnalytics : ISalesAnalytics
{
    private readonly string _connectionString;
    private readonly ILogger<SalesAnalytics> _logger;

    public SalesAnalytics(IConfiguration configuration, ILogger<SalesAnalytics> logger)
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

    public async Task<SalesOverviewResponse> GetOverviewAsync(
        DateRange range,
        long? categoryId,
        CancellationToken cancellationToken = default)
    {
        var prevRange = PeriodFilters.PreviousRange(range);
        var periodInfo = PeriodFilters.BuildPeriodInfo(range);
        var periodDto = PeriodDto.Create(periodInfo);
        var filtersDto = new FiltersDto(categoryId);

        const string sql = """
            SELECT
                period_revenue,
                prev_period_revenue,
                mom_delta,
                mom_pct,
                trend,
                top_products,
                top_customers
            FROM v_sales_overview(@p_from, @p_to, @p_prev_from, @p_prev_to, @p_category_id);
            """;

        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);

        cmd.Parameters.Add(new NpgsqlParameter("p_from", NpgsqlDbType.Date) { Value = range.From });
        cmd.Parameters.Add(new NpgsqlParameter("p_to", NpgsqlDbType.Date) { Value = range.To });
        cmd.Parameters.Add(new NpgsqlParameter("p_prev_from", NpgsqlDbType.Date) { Value = prevRange.From });
        cmd.Parameters.Add(new NpgsqlParameter("p_prev_to", NpgsqlDbType.Date) { Value = prevRange.To });
        cmd.Parameters.Add(new NpgsqlParameter("p_category_id", NpgsqlDbType.Bigint)
        {
            Value = (object?)categoryId ?? DBNull.Value
        });

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return new SalesOverviewResponse(
                DemoData: true,
                Period: periodDto,
                Filters: filtersDto,
                Totals: new SalesTotalsDto("0.00", "0.00", "0.00", null),
                Trend: Array.Empty<SalesTrendPointDto>(),
                TopProducts: Array.Empty<TopProductDto>(),
                TopCustomers: Array.Empty<TopCustomerDto>(),
                View: "v_sales_overview");
        }

        decimal periodRev = reader.IsDBNull(0) ? 0.00m : reader.GetDecimal(0);
        decimal prevRev = reader.IsDBNull(1) ? 0.00m : reader.GetDecimal(1);
        decimal momDelta = reader.IsDBNull(2) ? 0.00m : reader.GetDecimal(2);
        decimal? momPct = reader.IsDBNull(3) ? null : reader.GetDecimal(3);

        string trendJson = reader.IsDBNull(4) ? "[]" : reader.GetString(4);
        string topProductsJson = reader.IsDBNull(5) ? "[]" : reader.GetString(5);
        string topCustomersJson = reader.IsDBNull(6) ? "[]" : reader.GetString(6);

        var totals = new SalesTotalsDto(
            Revenue: KpiFormatters.FormatAmount(periodRev),
            PrevRevenue: KpiFormatters.FormatAmount(prevRev),
            MomDelta: KpiFormatters.FormatAmount(momDelta),
            MomPct: KpiFormatters.FormatPercent(momPct));

        var trend = ParseTrend(trendJson);
        var topProducts = ParseTopProducts(topProductsJson);
        var topCustomers = ParseTopCustomers(topCustomersJson);

        return new SalesOverviewResponse(
            DemoData: true,
            Period: periodDto,
            Filters: filtersDto,
            Totals: totals,
            Trend: trend,
            TopProducts: topProducts,
            TopCustomers: topCustomers,
            View: "v_sales_overview");
    }

    public async Task<SalesLinesResponse> GetLinesAsync(
        DateRange range,
        long? categoryId,
        CancellationToken cancellationToken = default)
    {
        var periodInfo = PeriodFilters.BuildPeriodInfo(range);
        var periodDto = PeriodDto.Create(periodInfo);
        var filtersDto = new FiltersDto(categoryId);

        const string sql = """
            SELECT
                invoice_id,
                customer_id,
                product_id,
                date,
                customer,
                product,
                qty,
                unit_price,
                line_discount,
                net
            FROM v_sales_lines(@p_from, @p_to, @p_category_id)
            LIMIT 1000;
            """;

        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);

        cmd.Parameters.Add(new NpgsqlParameter("p_from", NpgsqlDbType.Date) { Value = range.From });
        cmd.Parameters.Add(new NpgsqlParameter("p_to", NpgsqlDbType.Date) { Value = range.To });
        cmd.Parameters.Add(new NpgsqlParameter("p_category_id", NpgsqlDbType.Bigint)
        {
            Value = (object?)categoryId ?? DBNull.Value
        });

        var lines = new List<SalesLineDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            long invoiceId = reader.GetInt64(0);

            string dateStr;
            if (reader.GetFieldType(3) == typeof(DateOnly))
            {
                dateStr = reader.GetFieldValue<DateOnly>(3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else if (reader.GetFieldType(3) == typeof(DateTime))
            {
                dateStr = reader.GetDateTime(3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else
            {
                dateStr = reader.GetValue(3)?.ToString() ?? string.Empty;
            }

            string customer = reader.GetString(4);
            string product = reader.GetString(5);
            decimal qty = reader.GetDecimal(6);
            decimal unitPrice = reader.GetDecimal(7);
            decimal lineDiscount = reader.GetDecimal(8);
            decimal net = reader.GetDecimal(9);

            lines.Add(new SalesLineDto(
                InvoiceId: invoiceId,
                Date: dateStr,
                Customer: customer,
                Product: product,
                Qty: KpiFormatters.FormatQty(qty),
                UnitPrice: KpiFormatters.FormatAmount(unitPrice),
                LineDiscount: KpiFormatters.FormatAmount(lineDiscount),
                Net: KpiFormatters.FormatAmount(net)));
        }

        bool truncated = lines.Count >= 1000;

        return new SalesLinesResponse(
            DemoData: true,
            Period: periodDto,
            Filters: filtersDto,
            Truncated: truncated,
            Lines: lines,
            View: "v_sales_lines");
    }

    public async Task<CategoriesResponse> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT category_id, name_fa
            FROM v_categories();
            """;

        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);

        var categories = new List<CategoryDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            long categoryId = reader.GetInt64(0);
            string name = reader.GetString(1);
            categories.Add(new CategoryDto(categoryId, name));
        }

        return new CategoriesResponse(
            DemoData: true,
            Categories: categories,
            View: "v_categories");
    }

    private static IReadOnlyList<SalesTrendPointDto> ParseTrend(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return Array.Empty<SalesTrendPointDto>();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var list = new List<SalesTrendPointDto>();

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                string bucket = element.TryGetProperty("bucket", out var bProp) ? bProp.GetString() ?? "" : "";
                string revenue = "0.00";
                if (element.TryGetProperty("revenue", out var rProp))
                {
                    if (rProp.ValueKind == JsonValueKind.Number)
                    {
                        revenue = KpiFormatters.FormatAmount(rProp.GetDecimal());
                    }
                    else if (rProp.ValueKind == JsonValueKind.String && decimal.TryParse(rProp.GetString(), CultureInfo.InvariantCulture, out var parsed))
                    {
                        revenue = KpiFormatters.FormatAmount(parsed);
                    }
                    else if (rProp.ValueKind == JsonValueKind.String)
                    {
                        revenue = rProp.GetString() ?? "0.00";
                    }
                }

                list.Add(new SalesTrendPointDto(bucket, revenue));
            }

            return list;
        }
        catch
        {
            return Array.Empty<SalesTrendPointDto>();
        }
    }

    private static IReadOnlyList<TopProductDto> ParseTopProducts(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return Array.Empty<TopProductDto>();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var list = new List<TopProductDto>();

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                long productId = element.TryGetProperty("productId", out var pProp) ? pProp.GetInt64() : 0;
                string name = element.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
                string revenue = "0.00";
                if (element.TryGetProperty("revenue", out var rProp))
                {
                    if (rProp.ValueKind == JsonValueKind.Number)
                    {
                        revenue = KpiFormatters.FormatAmount(rProp.GetDecimal());
                    }
                    else if (rProp.ValueKind == JsonValueKind.String && decimal.TryParse(rProp.GetString(), CultureInfo.InvariantCulture, out var parsed))
                    {
                        revenue = KpiFormatters.FormatAmount(parsed);
                    }
                    else if (rProp.ValueKind == JsonValueKind.String)
                    {
                        revenue = rProp.GetString() ?? "0.00";
                    }
                }

                list.Add(new TopProductDto(productId, name, revenue));
            }

            return list;
        }
        catch
        {
            return Array.Empty<TopProductDto>();
        }
    }

    private static IReadOnlyList<TopCustomerDto> ParseTopCustomers(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return Array.Empty<TopCustomerDto>();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var list = new List<TopCustomerDto>();

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                long customerId = element.TryGetProperty("customerId", out var cProp) ? cProp.GetInt64() : 0;
                string name = element.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
                string revenue = "0.00";
                if (element.TryGetProperty("revenue", out var rProp))
                {
                    if (rProp.ValueKind == JsonValueKind.Number)
                    {
                        revenue = KpiFormatters.FormatAmount(rProp.GetDecimal());
                    }
                    else if (rProp.ValueKind == JsonValueKind.String && decimal.TryParse(rProp.GetString(), CultureInfo.InvariantCulture, out var parsed))
                    {
                        revenue = KpiFormatters.FormatAmount(parsed);
                    }
                    else if (rProp.ValueKind == JsonValueKind.String)
                    {
                        revenue = rProp.GetString() ?? "0.00";
                    }
                }

                list.Add(new TopCustomerDto(customerId, name, revenue));
            }

            return list;
        }
        catch
        {
            return Array.Empty<TopCustomerDto>();
        }
    }
}
