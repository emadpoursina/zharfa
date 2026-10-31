namespace Zharfa.Api.Ai;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Zharfa.Api.Analytics;
using Zharfa.Api.Data;

/// <summary>
/// Result of executing an allowlisted analyst tool.
/// Carries aggregate JSON for the model, server-generated facts, and source view citation.
/// </summary>
public record ToolExecutionResult(
    string ToolCallId,
    string ToolName,
    string? View,
    IReadOnlyList<string> Facts,
    string ContentJson,
    bool Success);

/// <summary>
/// Execution engine for allowlisted read-only analyst tools.
/// Enforces strict timeouts, call budgets, allowlist checking, and aggregate-only serialization.
/// Calls existing analytics services exclusively; contains zero arbitrary queries or writes.
/// </summary>
public class AnalystTools
{
    // Hard limit: maximum tool calls allowed per chat conversation turn
    public const int MAX_TOOL_CALLS_PER_CHAT = 2;

    // Hard limit: per-tool call execution timeout limit in seconds
    public const int PER_CALL_TIMEOUT_SECONDS = 5;

    // Hard limit: whole tool phase budget in seconds
    public const int WHOLE_TOOL_PHASE_BUDGET_SECONDS = 20;

    // Maximum trend points allowed in aggregate response to prevent model context flooding
    public const int MAX_TREND_BUCKETS = 10;

    private static readonly HashSet<string> AllowlistedToolNames = new(StringComparer.Ordinal)
    {
        "sales_overview",
        "receivables",
        "inventory_alerts"
    };

    private static readonly AiToolSpec SalesOverviewSpec = new(
        "sales_overview",
        "دریافت آمار و شاخص‌های فروش، روند درآمد و برترین محصولات و مشتریان برای یک بازه زمانی و دسته‌بندی مشخص.",
        new
        {
            type = "object",
            properties = new
            {
                from = new
                {
                    type = "string",
                    description = "تاریخ شروع بازه به فرمت YYYY-MM-DD (میلادی)"
                },
                to = new
                {
                    type = "string",
                    description = "تاریخ پایان بازه به فرمت YYYY-MM-DD (میلادی)"
                },
                categoryId = new
                {
                    type = "integer",
                    description = "شناسه دسته‌بندی کالا (اختیاری)"
                }
            },
            required = Array.Empty<string>()
        });

    private static readonly AiToolSpec ReceivablesSpec = new(
        "receivables",
        "دریافت مطالبات، دریافتی‌های نقد در بازه زمانی مشخص و فهرست ۵ بدهکار برتر.",
        new
        {
            type = "object",
            properties = new
            {
                from = new
                {
                    type = "string",
                    description = "تاریخ شروع بازه به فرمت YYYY-MM-DD (میلادی)"
                },
                to = new
                {
                    type = "string",
                    description = "تاریخ پایان بازه به فرمت YYYY-MM-DD (میلادی)"
                }
            },
            required = Array.Empty<string>()
        });

    private static readonly AiToolSpec InventoryAlertsSpec = new(
        "inventory_alerts",
        "دریافت هشدارهای موجودی انبار شامل اقلام ناموجود و دارای کسری برای یک دسته‌بندی مشخص.",
        new
        {
            type = "object",
            properties = new
            {
                categoryId = new
                {
                    type = "integer",
                    description = "شناسه دسته‌بندی کالا (اختیاری)"
                }
            },
            required = Array.Empty<string>()
        });

    private readonly ISalesAnalytics _salesAnalytics;
    private readonly IReceivablesAnalytics _receivablesAnalytics;
    private readonly IInventoryAnalytics _inventoryAnalytics;
    private readonly ILogger<AnalystTools> _logger;

    public AnalystTools(
        ISalesAnalytics salesAnalytics,
        IReceivablesAnalytics receivablesAnalytics,
        IInventoryAnalytics inventoryAnalytics,
        ILogger<AnalystTools> logger)
    {
        _salesAnalytics = salesAnalytics;
        _receivablesAnalytics = receivablesAnalytics;
        _inventoryAnalytics = inventoryAnalytics;
        _logger = logger;
    }

    /// <summary>
    /// Returns the exact three allowlisted tool definitions for the AI model.
    /// </summary>
    public static IReadOnlyList<AiToolSpec> GetToolSpecs() =>
        new[] { SalesOverviewSpec, ReceivablesSpec, InventoryAlertsSpec };

    /// <summary>
    /// Executes an allowlisted tool with strict limits and returns aggregate-only results.
    /// Never throws out of the tool: errors return Persian diagnostic strings.
    /// </summary>
    public async Task<ToolExecutionResult> ExecuteAsync(AiToolCall toolCall, CancellationToken cancellationToken)
    {
        if (!AllowlistedToolNames.Contains(toolCall.Name))
        {
            _logger.LogWarning("AI requested disallowed tool '{ToolName}'", RedactionHelper.Scrub(toolCall.Name));
            var refusedJson = JsonSerializer.Serialize(new
            {
                error = "ابزار درخواستی مجاز نیست یا در دسترس نمی‌باشد."
            });
            return new ToolExecutionResult(toolCall.Id, toolCall.Name, null, Array.Empty<string>(), refusedJson, false);
        }

        using var callCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        callCts.CancelAfter(TimeSpan.FromSeconds(PER_CALL_TIMEOUT_SECONDS));

        try
        {
            return toolCall.Name switch
            {
                "sales_overview" => await ExecuteSalesOverviewAsync(toolCall, callCts.Token),
                "receivables" => await ExecuteReceivablesAsync(toolCall, callCts.Token),
                "inventory_alerts" => await ExecuteInventoryAlertsAsync(toolCall, callCts.Token),
                _ => new ToolExecutionResult(toolCall.Id, toolCall.Name, null, Array.Empty<string>(), JsonSerializer.Serialize(new { error = "ابزار نامعتبر است." }), false)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Execution timed out for tool '{ToolName}'", toolCall.Name);
            var timeoutJson = JsonSerializer.Serialize(new { error = "زمان اجرای ابزار به پایان رسید (timeout)." });
            return new ToolExecutionResult(toolCall.Id, toolCall.Name, null, Array.Empty<string>(), timeoutJson, false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Execution failed for tool '{ToolName}' [{ExceptionType}]", toolCall.Name, ex.GetType().Name);
            var errJson = JsonSerializer.Serialize(new { error = "خطا در اجرای ابزار." });
            return new ToolExecutionResult(toolCall.Id, toolCall.Name, null, Array.Empty<string>(), errJson, false);
        }
    }

    private async Task<ToolExecutionResult> ExecuteSalesOverviewAsync(AiToolCall toolCall, CancellationToken ct)
    {
        string? fromStr = null;
        string? toStr = null;
        long? categoryId = null;

        if (!string.IsNullOrWhiteSpace(toolCall.ArgumentsJson))
        {
            try
            {
                using var argDoc = JsonDocument.Parse(toolCall.ArgumentsJson);
                var root = argDoc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("from", out var pFrom) && pFrom.ValueKind == JsonValueKind.String)
                        fromStr = pFrom.GetString();
                    if (root.TryGetProperty("to", out var pTo) && pTo.ValueKind == JsonValueKind.String)
                        toStr = pTo.GetString();
                    if (root.TryGetProperty("categoryId", out var pCat) && pCat.ValueKind == JsonValueKind.Number && pCat.TryGetInt64(out var catVal))
                        categoryId = catVal;
                }
            }
            catch (JsonException) { }
        }

        if (categoryId.HasValue && categoryId.Value <= 0)
        {
            var errJson = JsonSerializer.Serialize(new { error = "شناسه دسته‌بندی باید عددی مثبت باشد." });
            return new ToolExecutionResult(toolCall.Id, toolCall.Name, null, Array.Empty<string>(), errJson, false);
        }

        DateRange range;
        try
        {
            range = PeriodFilters.Resolve(fromStr, toStr);
        }
        catch (ArgumentException ex)
        {
            var errJson = JsonSerializer.Serialize(new { error = RedactionHelper.Scrub(ex.Message) });
            return new ToolExecutionResult(toolCall.Id, toolCall.Name, null, Array.Empty<string>(), errJson, false);
        }

        var overview = await _salesAnalytics.GetOverviewAsync(range, categoryId, ct);

        var trendList = new List<object>();
        int trendCount = 0;
        foreach (var t in overview.Trend)
        {
            if (trendCount >= MAX_TREND_BUCKETS) break;
            trendList.Add(new { bucket = t.Bucket, revenue = t.Revenue });
            trendCount++;
        }

        var payload = new
        {
            citation = new
            {
                period = new
                {
                    from = overview.Period.From,
                    to = overview.Period.To,
                    jalaliLabel = overview.Period.JalaliLabel
                },
                filters = new
                {
                    categoryId = overview.Filters.CategoryId
                },
                view = overview.View
            },
            totals = new
            {
                revenue = overview.Totals.Revenue,
                prevRevenue = overview.Totals.PrevRevenue,
                momDelta = overview.Totals.MomDelta,
                momPct = overview.Totals.MomPct
            },
            topProducts = overview.TopProducts,
            topCustomers = overview.TopCustomers,
            trend = trendList
        };

        var facts = new List<string>
        {
            $"درآمد فروش {overview.Period.JalaliLabel}: {FormatPersianAmount(overview.Totals.Revenue)} ریال (مرجع: {overview.View})",
            $"درآمد دوره قبل ({overview.Period.Prev.JalaliLabel}): {FormatPersianAmount(overview.Totals.PrevRevenue)} ریال (مرجع: {overview.View})"
        };

        if (!string.IsNullOrEmpty(overview.Totals.MomDelta))
        {
            var deltaFact = $"تغییر ماه به ماه: {FormatPersianAmount(overview.Totals.MomDelta)} ریال";
            if (!string.IsNullOrEmpty(overview.Totals.MomPct))
            {
                deltaFact += $" ({JalaliDate.ToPersianDigits(overview.Totals.MomPct)}٪)";
            }
            deltaFact += $" (مرجع: {overview.View})";
            facts.Add(deltaFact);
        }

        foreach (var p in overview.TopProducts)
        {
            facts.Add($"محصول برتر: {p.Name} با فروش {FormatPersianAmount(p.Revenue)} ریال (مرجع: {overview.View})");
        }

        foreach (var c in overview.TopCustomers)
        {
            facts.Add($"مشتری برتر: {c.Name} با خرید {FormatPersianAmount(c.Revenue)} ریال (مرجع: {overview.View})");
        }

        return new ToolExecutionResult(
            toolCall.Id,
            toolCall.Name,
            overview.View,
            facts,
            JsonSerializer.Serialize(payload),
            true);
    }

    private async Task<ToolExecutionResult> ExecuteReceivablesAsync(AiToolCall toolCall, CancellationToken ct)
    {
        string? fromStr = null;
        string? toStr = null;

        if (!string.IsNullOrWhiteSpace(toolCall.ArgumentsJson))
        {
            try
            {
                using var argDoc = JsonDocument.Parse(toolCall.ArgumentsJson);
                var root = argDoc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("from", out var pFrom) && pFrom.ValueKind == JsonValueKind.String)
                        fromStr = pFrom.GetString();
                    if (root.TryGetProperty("to", out var pTo) && pTo.ValueKind == JsonValueKind.String)
                        toStr = pTo.GetString();
                }
            }
            catch (JsonException) { }
        }

        DateRange range;
        try
        {
            range = PeriodFilters.Resolve(fromStr, toStr);
        }
        catch (ArgumentException ex)
        {
            var errJson = JsonSerializer.Serialize(new { error = RedactionHelper.Scrub(ex.Message) });
            return new ToolExecutionResult(toolCall.Id, toolCall.Name, null, Array.Empty<string>(), errJson, false);
        }

        var summary = await _receivablesAnalytics.GetSummaryAsync(range, ct);

        var payload = new
        {
            citation = new
            {
                period = new
                {
                    from = summary.Period.From,
                    to = summary.Period.To,
                    jalaliLabel = summary.Period.JalaliLabel
                },
                filters = new
                {
                    categoryId = (long?)null
                },
                view = summary.View
            },
            totals = new
            {
                receivables = summary.Totals.Receivables,
                paymentsReceived = summary.Totals.PaymentsReceived
            },
            topDebtors = summary.TopDebtors
        };

        var facts = new List<string>
        {
            $"مانده مطالبات: {FormatPersianAmount(summary.Totals.Receivables)} ریال (مرجع: {summary.View})",
            $"دریافتی‌های نقد {summary.Period.JalaliLabel}: {FormatPersianAmount(summary.Totals.PaymentsReceived)} ریال (مرجع: {summary.View})"
        };

        foreach (var d in summary.TopDebtors)
        {
            facts.Add($"بدهکار برتر: {d.Name} با مانده بدهی {FormatPersianAmount(d.Balance)} ریال (مرجع: {summary.View})");
        }

        return new ToolExecutionResult(
            toolCall.Id,
            toolCall.Name,
            summary.View,
            facts,
            JsonSerializer.Serialize(payload),
            true);
    }

    private async Task<ToolExecutionResult> ExecuteInventoryAlertsAsync(AiToolCall toolCall, CancellationToken ct)
    {
        long? categoryId = null;

        if (!string.IsNullOrWhiteSpace(toolCall.ArgumentsJson))
        {
            try
            {
                using var argDoc = JsonDocument.Parse(toolCall.ArgumentsJson);
                var root = argDoc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("categoryId", out var pCat) && pCat.ValueKind == JsonValueKind.Number && pCat.TryGetInt64(out var catVal))
                        categoryId = catVal;
                }
            }
            catch (JsonException) { }
        }

        if (categoryId.HasValue && categoryId.Value <= 0)
        {
            var errJson = JsonSerializer.Serialize(new { error = "شناسه دسته‌بندی باید عددی مثبت باشد." });
            return new ToolExecutionResult(toolCall.Id, toolCall.Name, null, Array.Empty<string>(), errJson, false);
        }

        var alerts = await _inventoryAnalytics.GetAlertsAsync(categoryId, ct);

        var lowStockList = new List<object>();
        foreach (var item in alerts.LowStock)
        {
            lowStockList.Add(new
            {
                productId = item.ProductId,
                name = item.Name,
                category = item.Category,
                warehouse = item.Warehouse,
                onHand = item.OnHand,
                hasBatch = item.HasBatch
            });
        }

        var outOfStockList = new List<object>();
        foreach (var item in alerts.OutOfStock)
        {
            outOfStockList.Add(new
            {
                productId = item.ProductId,
                name = item.Name,
                category = item.Category,
                warehouse = item.Warehouse,
                onHand = item.OnHand,
                hasBatch = item.HasBatch
            });
        }

        var payload = new
        {
            citation = new
            {
                period = (object?)null,
                filters = new
                {
                    categoryId = alerts.Filters.CategoryId
                },
                view = alerts.View
            },
            threshold = alerts.Threshold,
            outOfStock = outOfStockList,
            lowStock = lowStockList,
            outOfStockCount = alerts.OutOfStock.Count,
            lowStockCount = alerts.LowStock.Count
        };

        var facts = new List<string>
        {
            $"تعداد اقلام ناموجود: {JalaliDate.ToPersianDigits(alerts.OutOfStock.Count.ToString(CultureInfo.InvariantCulture))} قلم (مرجع: {alerts.View})",
            $"تعداد اقلام دارای کسری (آستانه {JalaliDate.ToPersianDigits(alerts.Threshold.ToString(CultureInfo.InvariantCulture))}): {JalaliDate.ToPersianDigits(alerts.LowStock.Count.ToString(CultureInfo.InvariantCulture))} قلم (مرجع: {alerts.View})"
        };

        foreach (var item in alerts.OutOfStock)
        {
            facts.Add($"کالای ناموجود: {item.Name} در انبار {item.Warehouse} (مرجع: {alerts.View})");
        }

        foreach (var item in alerts.LowStock)
        {
            facts.Add($"کسری موجودی: {item.Name} با موجودی {FormatPersianAmount(item.OnHand)} در انبار {item.Warehouse} (مرجع: {alerts.View})");
        }

        return new ToolExecutionResult(
            toolCall.Id,
            toolCall.Name,
            alerts.View,
            facts,
            JsonSerializer.Serialize(payload),
            true);
    }

    /// <summary>
    /// Formats decimal amount string into Persian digits with thousand separators.
    /// </summary>
    public static string FormatPersianAmount(string? amountStr)
    {
        if (string.IsNullOrWhiteSpace(amountStr))
        {
            return "۰";
        }

        if (decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
        {
            var frac = val - Math.Truncate(val);
            string formatted = frac == 0
                ? Math.Truncate(val).ToString("#,##0", CultureInfo.InvariantCulture)
                : val.ToString("#,##0.00", CultureInfo.InvariantCulture);
            formatted = formatted.Replace(',', '٬');
            return JalaliDate.ToPersianDigits(formatted);
        }

        return JalaliDate.ToPersianDigits(amountStr);
    }
}
