namespace Zharfa.Api.Analytics;

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

/// <summary>
/// Inventory alert row item.
/// Batch is a flag only (hasBatch); there is no expiry workflow anywhere in this code.
/// </summary>
public record InventoryAlertItemDto(
    long ProductId,
    string Name,
    string Category,
    string Warehouse,
    string OnHand,
    bool HasBatch);

/// <summary>
/// Inventory alerts KPI response envelope.
/// Reads approved view v_inventory_alerts with a single server-side threshold (documented default 10).
/// </summary>
public record InventoryAlertsResponse(
    bool DemoData,
    FiltersDto Filters,
    int Threshold,
    IReadOnlyList<InventoryAlertItemDto> LowStock,
    IReadOnlyList<InventoryAlertItemDto> OutOfStock,
    string View);

public interface IInventoryAnalytics
{
    Task<InventoryAlertsResponse> GetAlertsAsync(long? categoryId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Inventory KPI service reading approved semantic database view v_inventory_alerts only.
/// Single server-side threshold, documented default 10, same for every product.
/// Batch is a flag only (no expiry workflow anywhere in this code).
/// </summary>
public class InventoryAnalytics : IInventoryAnalytics
{
    private readonly string _connectionString;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InventoryAnalytics> _logger;

    public InventoryAnalytics(
        IConfiguration configuration,
        ILogger<InventoryAnalytics> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _connectionString = configuration.GetConnectionString("Zharfa")
            ?? throw new InvalidOperationException("Connection string 'Zharfa' is missing.");
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>
    /// Reads low stock and out of stock lists from approved view v_inventory_alerts.
    /// Single server-side threshold, documented default 10, same for every product.
    /// Batch is a flag only (no expiry workflow anywhere in this code).
    /// </summary>
    public async Task<InventoryAlertsResponse> GetAlertsAsync(
        long? categoryId,
        CancellationToken cancellationToken = default)
    {
        var threshold = ResolveThreshold();

        const string sql = "SELECT * FROM v_inventory_alerts(@p_category_id, @p_low_stock_threshold);";

        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);

        cmd.Parameters.Add(new NpgsqlParameter("p_category_id", NpgsqlDbType.Bigint)
        {
            Value = (object?)categoryId ?? DBNull.Value
        });
        cmd.Parameters.Add(new NpgsqlParameter("p_low_stock_threshold", NpgsqlDbType.Numeric)
        {
            Value = (decimal)threshold
        });

        var lowStock = new List<InventoryAlertItemDto>();
        var outOfStock = new List<InventoryAlertItemDto>();

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var productIdOrdinal = reader.GetOrdinal("product_id");
        var nameOrdinal = reader.GetOrdinal("name_fa");
        var categoryOrdinal = reader.GetOrdinal("category");
        var warehouseOrdinal = reader.GetOrdinal("warehouse");
        var onHandOrdinal = reader.GetOrdinal("on_hand");
        var hasBatchOrdinal = reader.GetOrdinal("has_batch");
        var stateOrdinal = reader.GetOrdinal("state");

        while (await reader.ReadAsync(cancellationToken))
        {
            var productId = reader.GetInt64(productIdOrdinal);
            var name = reader.GetString(nameOrdinal);
            var category = reader.GetString(categoryOrdinal);
            var warehouse = reader.GetString(warehouseOrdinal);
            var onHand = reader.GetDecimal(onHandOrdinal);
            var hasBatch = reader.GetBoolean(hasBatchOrdinal);
            var state = reader.GetString(stateOrdinal);

            var item = new InventoryAlertItemDto(
                productId,
                name,
                category,
                warehouse,
                KpiFormatters.FormatQty(onHand),
                hasBatch);

            if (string.Equals(state, "low", StringComparison.OrdinalIgnoreCase))
            {
                lowStock.Add(item);
            }
            else if (string.Equals(state, "out", StringComparison.OrdinalIgnoreCase))
            {
                outOfStock.Add(item);
            }
        }

        // Rows sorted by name (stable name ASC)
        var sortedLowStock = lowStock.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
        var sortedOutOfStock = outOfStock.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();

        return new InventoryAlertsResponse(
            DemoData: true,
            Filters: new FiltersDto(categoryId),
            Threshold: threshold,
            LowStock: sortedLowStock,
            OutOfStock: sortedOutOfStock,
            View: "v_inventory_alerts");
    }

    /// <summary>
    /// Reads the single server-side threshold from configuration key Inventory__LowStockThreshold.
    /// Documented default is 10 when absent or invalid; never below 0.
    /// Batch is a flag only (no expiry workflow anywhere in this code).
    /// </summary>
    private int ResolveThreshold()
    {
        // Single server-side threshold, documented default 10, same for every product.
        // Batch is a flag only (no expiry workflow anywhere in this code).
        var raw = _configuration["Inventory__LowStockThreshold"] ?? _configuration["Inventory:LowStockThreshold"];
        if (int.TryParse(raw, out var parsed) && parsed >= 0)
        {
            return parsed;
        }

        return 10;
    }
}
