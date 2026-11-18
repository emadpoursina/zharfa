namespace Zharfa.Api.Ai;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zharfa.Api.Analytics;
using Zharfa.Api.Data;
using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.Platform;
using Zharfa.Api.Purchasing;
using Zharfa.Api.Purchasing.Reports;

/// <summary>
/// Execution engine for allowlisted read-only purchasing analyst tools (Phase 4).
/// Enforces tool allowlist, 100-row cap, 10s per-tool timeout, and aggregate-only serialization.
/// Never queries Mabna directly, never writes, and never contains vendor financials.
/// </summary>
public class PurchasingAnalystTools(
    PurchasingOverviewService overviewService,
    PurchasingReportService reportService,
    VendorDirectoryService vendorDirectoryService,
    IOptions<SyncOptions> syncOptions,
    DataModeOptions dataMode,
    TimeProvider clock,
    ILogger<PurchasingAnalystTools> logger)
{
    public const int MAX_TOOL_CALLS_PER_CHAT = 3;
    public const int PER_CALL_TIMEOUT_SECONDS = 10;
    public const int WHOLE_ANSWER_BUDGET_SECONDS = 30;
    public const int MAX_ROWS = 100;

    public static readonly IReadOnlySet<string> AllowlistedToolNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "purchasing_overview",
        "purchasing_by_vendor",
        "purchasing_by_product",
        "purchasing_by_category",
        "purchasing_trend",
        "purchasing_top_products",
        "purchasing_returns",
        "purchasing_transactions_summary"
    };

    public static IReadOnlyList<AiToolSpec> GetToolSpecs() =>
    [
        new(
            "purchasing_overview",
            "دریافت شاخص‌های کلی خرید (خالص، ناخالص، برگشتی، مالیات ارزش افزوده، مقایسه با ماه قبل، روند ۱۲ ماهه، تعداد تأمین‌کنندگان فعال و ۵ کالا/تأمین‌کننده برتر).",
            new
            {
                type = "object",
                properties = new
                {
                    month = new { type = "string", description = "ماه جلالی به فرمت YYYY-MM (اختیاری؛ پیش‌فرض: ماه جاری)" }
                }
            }),
        new(
            "purchasing_by_vendor",
            "گزارش تجمیعی خرید به تفکیک تأمین‌کننده به همراه مبالغ، تعداد فاکتور و مقایسه با دوره قبل.",
            new
            {
                type = "object",
                properties = new
                {
                    period = new { type = "string", description = "نوع دوره: month, quarter, year (اختیاری)" },
                    month = new { type = "string", description = "ماه جلالی YYYY-MM (اختیاری)" },
                    quarter = new { type = "string", description = "فصل جلالی YYYY-Qn (اختیاری)" },
                    year = new { type = "string", description = "سال جلالی چهار رقمی (اختیاری)" },
                    vendor = new { type = "string", description = "نام تأمین‌کننده برای فیلتر (اختیاری)" },
                    limit = new { type = "integer", description = "حداکثر تعداد ردیف (حداکثر ۱۰۰)" }
                }
            }),
        new(
            "purchasing_by_product",
            "گزارش تجمیعی خرید به تفکیک کالا با مبالغ و تعداد.",
            new
            {
                type = "object",
                properties = new
                {
                    period = new { type = "string", description = "نوع دوره: month, quarter, year (اختیاری)" },
                    month = new { type = "string", description = "ماه جلالی YYYY-MM (اختیاری)" },
                    quarter = new { type = "string", description = "فصل جلالی YYYY-Qn (اختیاری)" },
                    year = new { type = "string", description = "سال جلالی چهار رقمی (اختیاری)" },
                    product = new { type = "string", description = "نام کالا برای فیلتر (اختیاری)" },
                    limit = new { type = "integer", description = "حداکثر تعداد ردیف (حداکثر ۱۰۰)" }
                }
            }),
        new(
            "purchasing_by_category",
            "گزارش تجمیعی خرید به تفکیک گروه کالا.",
            new
            {
                type = "object",
                properties = new
                {
                    period = new { type = "string", description = "نوع دوره: month, quarter, year (اختیاری)" },
                    month = new { type = "string", description = "ماه جلالی YYYY-MM (اختیاری)" },
                    quarter = new { type = "string", description = "فصل جلالی YYYY-Qn (اختیاری)" },
                    year = new { type = "string", description = "سال جلالی چهار رقمی (اختیاری)" },
                    category = new { type = "string", description = "نام گروه کالا برای فیلتر (اختیاری)" },
                    limit = new { type = "integer", description = "حداکثر تعداد ردیف (حداکثر ۱۰۰)" }
                }
            }),
        new(
            "purchasing_trend",
            "روند ماهانه مبالغ خرید در ماه‌های گذشته.",
            new
            {
                type = "object",
                properties = new
                {
                    period = new { type = "string", description = "نوع دوره: month, quarter, year (اختیاری)" },
                    month = new { type = "string", description = "ماه جلالی مبنا YYYY-MM (اختیاری)" },
                    year = new { type = "string", description = "سال جلالی چهار رقمی (اختیاری)" }
                }
            }),
        new(
            "purchasing_top_products",
            "پرمصرف‌ترین و بیشترین کالاهای خریداری شده از نظر مبلغ خالص.",
            new
            {
                type = "object",
                properties = new
                {
                    period = new { type = "string", description = "نوع دوره: month, quarter, year (اختیاری)" },
                    month = new { type = "string", description = "ماه جلالی YYYY-MM (اختیاری)" },
                    quarter = new { type = "string", description = "فصل جلالی YYYY-Qn (اختیاری)" },
                    limit = new { type = "integer", description = "تعداد برترین‌ها (پیش‌فرض ۵، حداکثر ۱۰۰)" }
                }
            }),
        new(
            "purchasing_returns",
            "گزارش اقلام برگشتی از خرید به تفکیک کالا و تأمین‌کننده.",
            new
            {
                type = "object",
                properties = new
                {
                    period = new { type = "string", description = "نوع دوره: month, quarter, year (اختیاری)" },
                    month = new { type = "string", description = "ماه جلالی YYYY-MM (اختیاری)" },
                    limit = new { type = "integer", description = "حداکثر تعداد ردیف (حداکثر ۱۰۰)" }
                }
            }),
        new(
            "purchasing_transactions_summary",
            "خلاصه اسناد و فاکتورهای خرید (فقط سربرگ و مبالغ کل، بدون ریز اقلام سند).",
            new
            {
                type = "object",
                properties = new
                {
                    period = new { type = "string", description = "نوع دوره: month, quarter, year (اختیاری)" },
                    month = new { type = "string", description = "ماه جلالی YYYY-MM (اختیاری)" },
                    vendor = new { type = "string", description = "نام تأمین‌کننده (اختیاری)" },
                    limit = new { type = "integer", description = "حداکثر تعداد سند (حداکثر ۱۰۰)" }
                }
            })
    ];

    public async Task<ToolExecutionResult> ExecuteAsync(AiToolCall call, CancellationToken ct)
    {
        if (!AllowlistedToolNames.Contains(call.Name))
        {
            logger.LogWarning("Refused unapproved or non-existent tool call: {ToolName}", call.Name);
            return new ToolExecutionResult(
                call.Id,
                call.Name,
                null,
                Array.Empty<string>(),
                "{\"error\": \"ابزار درخواستی معتبر نیست یا مجاز نمی‌باشد.\", \"unavailable\": true}",
                false);
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(PER_CALL_TIMEOUT_SECONDS));

        JalaliPeriod? period = null;
        try
        {
            using var argsDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson);
            var root = argsDoc.RootElement;

            period = ResolvePeriod(root);

            return call.Name switch
            {
                "purchasing_overview" => await ExecuteOverviewAsync(call.Id, period, linkedCts.Token),
                "purchasing_by_vendor" => await ExecuteGroupedAsync(call.Id, "by-vendor", period, root, linkedCts.Token),
                "purchasing_by_product" => await ExecuteGroupedAsync(call.Id, "by-product", period, root, linkedCts.Token),
                "purchasing_by_category" => await ExecuteGroupedAsync(call.Id, "by-category", period, root, linkedCts.Token),
                "purchasing_top_products" => await ExecuteGroupedAsync(call.Id, "top-products", period, root, linkedCts.Token),
                "purchasing_trend" => await ExecuteTrendAsync(call.Id, period, root, linkedCts.Token),
                "purchasing_returns" => await ExecuteReturnsAsync(call.Id, period, root, linkedCts.Token),
                "purchasing_transactions_summary" => await ExecuteTransactionsSummaryAsync(call.Id, period, root, linkedCts.Token),
                _ => new ToolExecutionResult(call.Id, call.Name, null, Array.Empty<string>(), "{\"unavailable\": true}", false, Period: period)
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Tool execution timed out: {ToolName}", call.Name);
            throw new AiException("پاسخ ابزار به دلیل اتمام زمان مهلت دریافت نشد (timeout).");
        }
        catch (Exception ex) when (ex is not AiException)
        {
            logger.LogWarning("Tool execution failed [{ExceptionType}]: {ToolName}", ex.GetType().Name, call.Name);
            return new ToolExecutionResult(call.Id, call.Name, null, Array.Empty<string>(), "{\"unavailable\": true}", false, Period: period);
        }
    }

    private JalaliDate Today()
    {
        var zone = SyncOptions.ResolveTimeZone(syncOptions?.Value?.TimeZone ?? "Asia/Tehran");
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);
        return JalaliDate.FromDateTime(local.DateTime);
    }

    public JalaliPeriod GetDefaultPeriod()
    {
        var today = Today();
        JalaliPeriod.TryParse("month", null, null, null, today, out var defaultMonth, out _);
        return defaultMonth!;
    }

    public async Task<bool> HasSuccessfulSyncAsync(CancellationToken ct)
    {
        if (overviewService == null)
        {
            return true;
        }
        return await overviewService.HasSuccessfulSyncAsync(ct);
    }

    public JalaliPeriod BindPeriod(string? periodRule)
    {
        var today = Today();
        switch (periodRule)
        {
            case "previous-jalali-month":
            {
                JalaliPeriod.TryParse("month", null, null, null, today, out var currentM, out _);
                return currentM!.Previous();
            }
            case "current-jalali-quarter":
            {
                JalaliPeriod.TryParse("quarter", null, null, null, today, out var qPeriod, out _);
                return qPeriod!;
            }
            case "future-empty-month":
            {
                var currentMonth = new JalaliMonth(today.Year, today.Month);
                var nextMonth = currentMonth.AddMonths(1);
                var endOfNextMonth = new JalaliDate(nextMonth.Year, nextMonth.Month, JalaliDate.DaysInMonth(nextMonth.Year, nextMonth.Month));
                JalaliPeriod.TryParse("month", nextMonth.Key, null, null, endOfNextMonth, out var futurePeriod, out _);
                return futurePeriod!;
            }
            default:
                return GetDefaultPeriod();
        }
    }

    public virtual async Task<ToolExecutionResult> DispatchAsync(string toolName, JalaliPeriod period, CancellationToken ct)
    {
        if (!AllowlistedToolNames.Contains(toolName))
        {
            logger.LogWarning("Refused execution of unlisted purchasing tool: {ToolName}", toolName);
            return new ToolExecutionResult("dispatch", toolName, null, Array.Empty<string>(), "{\"unavailable\": true, \"reason\": \"unlisted_tool\"}", false, Period: period);
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(PER_CALL_TIMEOUT_SECONDS));

        using var emptyDoc = JsonDocument.Parse("{}");
        var root = emptyDoc.RootElement;
        var callId = "dispatch_" + toolName;

        try
        {
            return toolName switch
            {
                "purchasing_overview" => await ExecuteOverviewAsync(callId, period, linkedCts.Token),
                "purchasing_by_vendor" => await ExecuteGroupedAsync(callId, "by-vendor", period, root, linkedCts.Token),
                "purchasing_by_product" => await ExecuteGroupedAsync(callId, "by-product", period, root, linkedCts.Token),
                "purchasing_by_category" => await ExecuteGroupedAsync(callId, "by-category", period, root, linkedCts.Token),
                "purchasing_top_products" => await ExecuteGroupedAsync(callId, "top-products", period, root, linkedCts.Token),
                "purchasing_trend" => await ExecuteTrendAsync(callId, period, root, linkedCts.Token),
                "purchasing_returns" => await ExecuteReturnsAsync(callId, period, root, linkedCts.Token),
                "purchasing_transactions_summary" => await ExecuteTransactionsSummaryAsync(callId, period, root, linkedCts.Token),
                _ => new ToolExecutionResult(callId, toolName, null, Array.Empty<string>(), "{\"unavailable\": true}", false, Period: period)
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Dispatched tool execution timed out: {ToolName}", toolName);
            throw new AiException($"اجرای ابزار {toolName} به دلیل اتمام زمان مهلت متوقف شد.");
        }
        catch (Exception ex) when (ex is not AiException)
        {
            logger.LogWarning("Dispatched tool execution failed [{ExceptionType}]: {ToolName}", ex.GetType().Name, toolName);
            return new ToolExecutionResult(callId, toolName, null, Array.Empty<string>(), "{\"unavailable\": true}", false, Period: period);
        }
    }

    public JalaliPeriod ResolvePeriod(JsonElement root)
    {
        var today = Today();

        string? periodKind = GetStringProp(root, "period");
        string? month = GetStringProp(root, "month");
        string? quarter = GetStringProp(root, "quarter");
        string? year = GetStringProp(root, "year");
        string? from = GetStringProp(root, "from");
        string? to = GetStringProp(root, "to");

        if (periodKind == "current-jalali-quarter" || quarter != null || periodKind == "quarter")
        {
            var qVal = quarter != null && quarter != "current" ? quarter : null;
            if (JalaliPeriod.TryParse("quarter", qVal, null, null, today, out var qPeriod, out _))
            {
                return qPeriod!;
            }
        }

        if (periodKind == "previous-jalali-month")
        {
            if (JalaliPeriod.TryParse("month", null, null, null, today, out var currentM, out _))
            {
                return currentM!.Previous();
            }
        }

        if (!string.IsNullOrEmpty(month))
        {
            if (JalaliPeriod.TryParse("month", month, null, null, today, out var mPeriod, out _))
            {
                return mPeriod!;
            }
        }

        if (!string.IsNullOrEmpty(year))
        {
            if (JalaliPeriod.TryParse("year", year, null, null, today, out var yPeriod, out _))
            {
                return yPeriod!;
            }
        }

        if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
        {
            if (JalaliPeriod.TryParse("custom", null, from, to, today, out var cPeriod, out _))
            {
                return cPeriod!;
            }
        }

        // Overview default: current Jalali month
        JalaliPeriod.TryParse("month", null, null, null, today, out var defaultMonth, out _);
        return defaultMonth!;
    }

    private static string? GetStringProp(JsonElement elem, string propName)
    {
        if (elem.ValueKind == JsonValueKind.Object && elem.TryGetProperty(propName, out var p))
        {
            return p.ValueKind == JsonValueKind.String ? p.GetString() : p.GetRawText();
        }
        return null;
    }

    private static int? GetIntProp(JsonElement elem, string propName)
    {
        if (elem.ValueKind == JsonValueKind.Object && elem.TryGetProperty(propName, out var p))
        {
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v))
            {
                return v;
            }
        }
        return null;
    }

    private async Task<ToolExecutionResult> ExecuteOverviewAsync(string callId, JalaliPeriod period, CancellationToken ct)
    {
        var jalaliMonth = new JalaliMonth(period.From.Year, period.From.Month);
        var res = await overviewService.GetAsync(jalaliMonth, ct);

        if (res.Unavailable.Count > 0 && res.Totals == null)
        {
            return new ToolExecutionResult(
                callId,
                "purchasing_overview",
                "purchasing_overview",
                Array.Empty<string>(),
                "{\"unavailable\": true, \"message\": \"داده در دسترس نیست\"}",
                false,
                Period: period);
        }

        var facts = new List<string>();
        var monthKey = jalaliMonth.Key;

        if (res.Totals != null)
        {
            facts.Add($"metric=purchasing_overview.net; period={monthKey}; value={res.Totals.Net}");
            facts.Add($"metric=purchasing_overview.gross; period={monthKey}; value={res.Totals.Gross}");
            facts.Add($"metric=purchasing_overview.returns; period={monthKey}; value={res.Totals.Returns}");
            facts.Add($"metric=purchasing_overview.vat; period={monthKey}; value={res.Totals.Vat}");
        }

        if (res.Comparison != null)
        {
            facts.Add($"metric=purchasing_overview.comparison; period={monthKey}; delta={res.Comparison.Delta}; percent={res.Comparison.Percent}");
        }

        if (res.ActiveVendors.HasValue)
        {
            facts.Add($"metric=purchasing_overview.activeVendors; period={monthKey}; count={res.ActiveVendors.Value}");
        }

        if (res.LastSuccessfulSync != null)
        {
            facts.Add($"metric=purchasing_overview.sync; lastSync={res.LastSuccessfulSync.AtJalali}; dataMode={dataMode.DataLabel}");
        }

        if (res.TopVendors is { Count: > 0 })
        {
            var top = res.TopVendors[0];
            facts.Add($"metric=purchasing_overview.topVendors; name={top.Name}; net={top.Net}");
        }

        if (res.TopProducts is { Count: > 0 })
        {
            var top = res.TopProducts[0];
            facts.Add($"metric=purchasing_overview.topProducts; name={top.Name}; net={top.Net}");
        }

        var payload = new
        {
            view = "purchasing_overview",
            period = monthKey,
            totals = res.Totals,
            comparison = res.Comparison,
            activeVendors = res.ActiveVendors,
            lastSync = res.LastSuccessfulSync?.AtJalali,
            dataLabel = dataMode.DataLabel,
            topVendors = res.TopVendors?.Take(5).Select(v => new { name = v.Name, net = v.Net }).ToList(),
            topProducts = res.TopProducts?.Take(5).Select(p => new { name = p.Name, net = p.Net }).ToList()
        };

        var contentJson = JsonSerializer.Serialize(payload);
        return new ToolExecutionResult(callId, "purchasing_overview", "purchasing_overview", facts, contentJson, true, Period: period);
    }

    private async Task<ToolExecutionResult> ExecuteGroupedAsync(
        string callId,
        string reportName,
        JalaliPeriod period,
        JsonElement root,
        CancellationToken ct)
    {
        string? vendorName = GetStringProp(root, "vendor");
        string? productName = GetStringProp(root, "product");
        string? categoryName = GetStringProp(root, "category");
        int limit = Math.Clamp(GetIntProp(root, "limit") ?? MAX_ROWS, 1, MAX_ROWS);

        string? vendorKey = null;
        if (!string.IsNullOrWhiteSpace(vendorName))
        {
            var lookup = await vendorDirectoryService.LookupAsync("vendors", vendorName.Trim(), 10, ct);
            var item = lookup?.Items?.FirstOrDefault();
            if (item == null)
            {
                return UnavailableResult(callId, reportName, "تأمین‌کننده مشخص شده یافت نشد.", period, vendor: vendorName);
            }
            vendorKey = item.Key;
        }

        string? productKey = null;
        if (!string.IsNullOrWhiteSpace(productName))
        {
            var lookup = await vendorDirectoryService.LookupAsync("products", productName.Trim(), 10, ct);
            var item = lookup?.Items?.FirstOrDefault();
            if (item == null)
            {
                return UnavailableResult(callId, reportName, "کالای مشخص شده یافت نشد.", period, vendor: vendorName, product: productName);
            }
            productKey = item.Key;
        }

        string? categoryKey = null;
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            var lookup = await vendorDirectoryService.LookupAsync("categories", categoryName.Trim(), 10, ct);
            var item = lookup?.Items?.FirstOrDefault();
            if (item == null)
            {
                return UnavailableResult(callId, reportName, "دسته‌بندی مشخص شده یافت نشد.", period, vendor: vendorName, product: productName, category: categoryName);
            }
            categoryKey = item.Key;
        }

        var filter = new ReportFilter(
            Period: period,
            Compare: true,
            VendorKey: vendorKey,
            ProductKey: productKey,
            CategoryKey: categoryKey,
            Month: null,
            DocType: null,
            Search: null,
            Match: null,
            Sort: reportName == "trend" ? "month" : "net",
            Descending: reportName != "trend",
            Page: 1,
            PageSize: limit,
            Print: false);

        var table = await reportService.GroupedAsync(reportName, filter, ct);

        if (table.TotalRows > MAX_ROWS)
        {
            logger.LogWarning("Report {Report} exceeded max row cap ({Total} > {Max})", reportName, table.TotalRows, MAX_ROWS);
            return UnavailableResult(callId, reportName, "تعداد رکوردهای نتیجه بیش از حد مجاز (۱۰۰ سطر) است.", period, vendor: vendorName, product: productName, category: categoryName);
        }

        var facts = new List<string>();
        foreach (var row in table.Rows.Take(10))
        {
            var fact = $"metric=purchasing_{reportName.Replace('-', '_')}; period={period.Label}; name={row.Name}; net={row.Net}";
            if (row.Delta != null)
            {
                fact += $"; delta={row.Delta}; percent={row.Percent}";
            }
            facts.Add(fact);
        }

        var payload = new
        {
            view = $"purchasing_{reportName.Replace('-', '_')}",
            period = period.Label,
            totalRows = table.TotalRows,
            totals = table.Totals,
            previousTotals = table.PreviousTotals,
            items = table.Rows.Select(r => new
            {
                name = r.Name,
                net = r.Net,
                gross = r.Gross,
                returns = r.Returns,
                delta = r.Delta,
                percent = r.Percent,
                count = r.DocumentCount
            }).ToList()
        };

        return new ToolExecutionResult(callId, $"purchasing_{reportName.Replace('-', '_')}", $"purchasing_{reportName.Replace('-', '_')}", facts, JsonSerializer.Serialize(payload), true, Period: period, Vendor: vendorName, Product: productName, Category: categoryName);
    }

    private async Task<ToolExecutionResult> ExecuteTrendAsync(string callId, JalaliPeriod period, JsonElement root, CancellationToken ct)
    {
        var filter = new ReportFilter(period, Compare: false, VendorKey: null, ProductKey: null, CategoryKey: null, Month: null, DocType: null, Search: null, Match: null, Sort: "month", Descending: false, Page: 1, PageSize: MAX_ROWS, Print: false);
        var table = await reportService.TrendAsync(filter, ct);

        if (table.TotalRows > MAX_ROWS)
        {
            return UnavailableResult(callId, "purchasing_trend", "تعداد سطرهای روند بیش از ۱۰۰ است.", period);
        }

        var facts = new List<string>();
        foreach (var row in table.Rows)
        {
            facts.Add($"metric=purchasing_trend; month={row.Month}; net={row.Net}; available={row.Available}");
        }

        var payload = new
        {
            view = "purchasing_trend",
            period = period.Label,
            totalRows = table.TotalRows,
            items = table.Rows.Select(r => new { month = r.Month, net = r.Net, available = r.Available }).ToList()
        };

        return new ToolExecutionResult(callId, "purchasing_trend", "purchasing_trend", facts, JsonSerializer.Serialize(payload), true, Period: period);
    }

    private async Task<ToolExecutionResult> ExecuteReturnsAsync(string callId, JalaliPeriod period, JsonElement root, CancellationToken ct)
    {
        int limit = Math.Clamp(GetIntProp(root, "limit") ?? MAX_ROWS, 1, MAX_ROWS);
        var filter = new ReportFilter(period, Compare: false, VendorKey: null, ProductKey: null, CategoryKey: null, Month: null, DocType: null, Search: null, Match: null, Sort: "returns", Descending: true, Page: 1, PageSize: limit, Print: false);
        var table = await reportService.ReturnsAsync(filter, ct);

        if (table.TotalRows > MAX_ROWS)
        {
            return UnavailableResult(callId, "purchasing_returns", "تعداد سطرهای برگشتی بیش از ۱۰۰ است.", period);
        }

        var facts = new List<string>();
        foreach (var row in table.Rows.Take(10))
        {
            facts.Add($"metric=purchasing_returns; product={row.ProductName}; vendor={row.VendorName}; returns={row.Returns}");
        }

        var payload = new
        {
            view = "purchasing_returns",
            period = period.Label,
            totalRows = table.TotalRows,
            totals = table.Totals,
            items = table.Rows.Select(r => new { product = r.ProductName, vendor = r.VendorName, returns = r.Returns, count = r.DocumentCount }).ToList()
        };

        return new ToolExecutionResult(callId, "purchasing_returns", "purchasing_returns", facts, JsonSerializer.Serialize(payload), true, Period: period);
    }

    private async Task<ToolExecutionResult> ExecuteTransactionsSummaryAsync(string callId, JalaliPeriod period, JsonElement root, CancellationToken ct)
    {
        string? vendorName = GetStringProp(root, "vendor");
        int limit = Math.Clamp(GetIntProp(root, "limit") ?? MAX_ROWS, 1, MAX_ROWS);

        string? vendorKey = null;
        if (!string.IsNullOrWhiteSpace(vendorName))
        {
            var lookup = await vendorDirectoryService.LookupAsync("vendors", vendorName.Trim(), 10, ct);
            var item = lookup?.Items?.FirstOrDefault();
            if (item == null)
            {
                return UnavailableResult(callId, "purchasing_transactions_summary", "تأمین‌کننده مشخص شده یافت نشد.", period, vendor: vendorName);
            }
            vendorKey = item.Key;
        }

        var filter = new ReportFilter(period, Compare: false, VendorKey: vendorKey, ProductKey: null, CategoryKey: null, Month: null, DocType: null, Search: null, Match: null, Sort: "date", Descending: true, Page: 1, PageSize: limit, Print: false);
        var table = await reportService.DocumentsAsync(filter, ct);

        if (table.TotalRows > MAX_ROWS)
        {
            return UnavailableResult(callId, "purchasing_transactions_summary", "تعداد اسناد بیش از ۱۰۰ است.", period, vendor: vendorName);
        }

        var facts = new List<string>();
        if (table.Totals != null)
        {
            facts.Add($"metric=purchasing_transactions_summary; totalRows={table.TotalRows}; net={table.Totals.Net}; invoiceAmount={table.Totals.InvoiceAmount}; returnAmount={table.Totals.ReturnAmount}; documentCount={table.Totals.DocumentCount}");
        }
        else
        {
            facts.Add($"metric=purchasing_transactions_summary; totalRows={table.TotalRows}");
        }

        // Aggregates only (totals and counts). Never document headers, raw rows, or document lines.
        var payload = new
        {
            view = "purchasing_transactions_summary",
            period = period.Label,
            totalRows = table.TotalRows,
            totals = table.Totals
        };

        return new ToolExecutionResult(callId, "purchasing_transactions_summary", "purchasing_transactions_summary", facts, JsonSerializer.Serialize(payload), true, Period: period, Vendor: vendorName);
    }

    private static ToolExecutionResult UnavailableResult(
        string callId,
        string toolName,
        string message,
        JalaliPeriod? period = null,
        string? vendor = null,
        string? product = null,
        string? category = null) =>
        new(callId, toolName, null, Array.Empty<string>(), $"{{\"unavailable\": true, \"message\": \"{message}\"}}", false, period, vendor, product, category);
}
