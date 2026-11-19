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
using Zharfa.Api.VendorFinancials;

/// <summary>
/// Execution engine for allowlisted read-only vendor financial analyst tools (Phase 6, contracts/ai-tools.md).
/// Enforces tool allowlist, 100-row cap, 10s per-tool timeout, and aggregate-only serialization.
/// Never queries Mabna directly, never writes.
/// </summary>
public class VendorFinancialAnalystTools(
    VendorFinancialsService financialsService,
    IOptions<SyncOptions> syncOptions,
    DataModeOptions dataMode,
    TimeProvider clock,
    ILogger<VendorFinancialAnalystTools> logger)
{
    public const int MAX_TOOL_CALLS_PER_CHAT = 3;
    public const int PER_CALL_TIMEOUT_SECONDS = 10;
    public const int WHOLE_ANSWER_BUDGET_SECONDS = 30;
    public const int MAX_ROWS = 100;

    public static readonly IReadOnlySet<string> AllowlistedToolNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "vendor_financials_list",
        "vendor_balance",
        "vendor_payments",
        "vendor_outstanding",
        "vendor_purchases_vs_payments"
    };

    public static IReadOnlyList<AiToolSpec> GetToolSpecs() =>
    [
        new(
            "vendor_financials_list",
            "فهرست شاخص‌های مالی تأمین‌کنندگان شامل مانده حساب تأمین‌کننده و پرداخت‌های معوق.",
            new
            {
                type = "object",
                properties = new
                {
                    period = new { type = "string", description = "نوع دوره: month, quarter, year (اختیاری)" },
                    month = new { type = "string", description = "ماه جلالی YYYY-MM (اختیاری)" },
                    quarter = new { type = "string", description = "فصل جلالی YYYY-Qn (اختیاری)" },
                    year = new { type = "string", description = "سال جلالی چهار رقمی (اختیاری)" },
                    vendor = new { type = "string", description = "نام تأمین‌کننده برای فیلتر یا جستجو (اختیاری)" },
                    limit = new { type = "integer", description = "حداکثر تعداد ردیف (حداکثر ۱۰۰)" }
                }
            }),
        new(
            "vendor_balance",
            "دریافت مانده حساب یک تأمین‌کننده مشخص (مثبت = بدهی شرکت به تأمین‌کننده، منفی = بستانکاری شرکت).",
            new
            {
                type = "object",
                properties = new
                {
                    vendor = new { type = "string", description = "نام یا کد تأمین‌کننده (الزامی)" },
                    month = new { type = "string", description = "ماه جلالی YYYY-MM (اختیاری)" }
                },
                required = new[] { "vendor" }
            }),
        new(
            "vendor_payments",
            "دریافت مجموع مبالغ پرداختی به یک تأمین‌کننده در دوره زمانی مشخص.",
            new
            {
                type = "object",
                properties = new
                {
                    vendor = new { type = "string", description = "نام یا کد تأمین‌کننده (الزامی)" },
                    period = new { type = "string", description = "نوع دوره: month, quarter, year (اختیاری)" },
                    month = new { type = "string", description = "ماه جلالی YYYY-MM (اختیاری)" }
                },
                required = new[] { "vendor" }
            }),
        new(
            "vendor_outstanding",
            "دریافت مجموع مبالغ اسناد و پرداخت‌های معوق یک تأمین‌کننده تا پایان دوره.",
            new
            {
                type = "object",
                properties = new
                {
                    vendor = new { type = "string", description = "نام یا کد تأمین‌کننده (الزامی)" },
                    month = new { type = "string", description = "ماه جلالی YYYY-MM (اختیاری)" }
                },
                required = new[] { "vendor" }
            }),
        new(
            "vendor_purchases_vs_payments",
            "گزارش خرید در برابر پرداخت برای یک تأمین‌کننده: خالص خرید، پرداخت‌ها و خرید منهای پرداخت.",
            new
            {
                type = "object",
                properties = new
                {
                    vendor = new { type = "string", description = "نام یا کد تأمین‌کننده (الزامی)" },
                    period = new { type = "string", description = "نوع دوره: month, quarter, year (اختیاری)" },
                    month = new { type = "string", description = "ماه جلالی YYYY-MM (اختیاری)" }
                },
                required = new[] { "vendor" }
            })
    ];

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

    public JalaliPeriod BindPeriod(string? periodRule)
    {
        var today = Today();
        switch (periodRule)
        {
            case "last-six-jalali-months":
            {
                var endMonth = new JalaliMonth(today.Year, today.Month);
                return JalaliPeriod.LastNMonths(6, endMonth);
            }
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

    public Task<FinancialVendorHeader?> FindVendorInTextAsync(string? text, CancellationToken ct) =>
        financialsService.FindVendorInTextAsync(text, ct);

    public async Task<ToolExecutionResult> DispatchAsync(
        string toolName,
        JalaliPeriod period,
        CancellationToken ct,
        JsonElement? args = null)
    {
        if (!AllowlistedToolNames.Contains(toolName))
        {
            return new ToolExecutionResult(
                Guid.NewGuid().ToString("N"),
                toolName,
                null,
                Array.Empty<string>(),
                "{\"unavailable\": true}",
                false,
                Period: period);
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(PER_CALL_TIMEOUT_SECONDS));

        var callId = Guid.NewGuid().ToString("N");
        try
        {
            return toolName switch
            {
                "vendor_financials_list" => await ExecuteFinancialsListAsync(callId, period, args, linkedCts.Token),
                "vendor_balance" => await ExecuteVendorBalanceAsync(callId, period, args, linkedCts.Token),
                "vendor_payments" => await ExecuteVendorPaymentsAsync(callId, period, args, linkedCts.Token),
                "vendor_outstanding" => await ExecuteVendorOutstandingAsync(callId, period, args, linkedCts.Token),
                "vendor_purchases_vs_payments" => await ExecutePurchasesVsPaymentsAsync(callId, period, args, linkedCts.Token),
                _ => new ToolExecutionResult(callId, toolName, null, Array.Empty<string>(), "{\"unavailable\": true}", false, Period: period)
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Financial tool execution timed out: {ToolName}", toolName);
            throw new AiException("پاسخ ابزار به دلیل اتمام زمان مهلت دریافت نشد (timeout).");
        }
        catch (Exception ex) when (ex is not AiException)
        {
            logger.LogWarning("Financial tool execution failed [{ExceptionType}]: {ToolName}", ex.GetType().Name, toolName);
            return new ToolExecutionResult(callId, toolName, null, Array.Empty<string>(), "{\"unavailable\": true}", false, Period: period);
        }
    }

    private async Task<ToolExecutionResult> ExecuteFinancialsListAsync(
        string callId,
        JalaliPeriod period,
        JsonElement? args,
        CancellationToken ct)
    {
        string? vendorSearch = null;
        int limit = MAX_ROWS;
        if (args.HasValue && args.Value.ValueKind == JsonValueKind.Object)
        {
            if (args.Value.TryGetProperty("vendor", out var v) && v.ValueKind == JsonValueKind.String)
                vendorSearch = v.GetString();
            if (args.Value.TryGetProperty("limit", out var l) && l.TryGetInt32(out var lim))
                limit = Math.Min(Math.Max(lim, 1), MAX_ROWS);
        }

        var request = new VendorFinancialRequest(
            Period: period,
            Compare: false,
            Search: vendorSearch,
            NormalizedSearch: VendorFinancialPeriod.SearchKey(vendorSearch),
            Page: 1,
            PageSize: limit,
            Sort: "vendorBalance",
            Descending: true);

        var summary = await financialsService.SummaryAsync(request, ct);

        // 100-row cap rule: if totalRows > 100, metric unavailable, no truncated guess
        if (summary.TotalRows > MAX_ROWS)
        {
            return new ToolExecutionResult(
                callId,
                "vendor_financials_list",
                "vendor-financials",
                ["تعداد ردیف‌های گزارش بیش از حد مجاز (۱۰۰ ردیف) است و داده قابل نمایش نیست."],
                "{\"unavailable\": true, \"reason\": \"row-cap-exceeded\"}",
                false,
                Period: period,
                TotalRows: summary.TotalRows);
        }

        var facts = new List<string>
        {
            $"در دوره {period.Label}، وضعیت مالی {summary.Rows.Count} تأمین‌کننده استخراج شد."
        };

        foreach (var r in summary.Rows.Take(10))
        {
            var bal = r.VendorBalance != null ? $"{r.VendorBalance} ریال" : "داده در دسترس نیست";
            var outst = r.OutstandingPayments != null ? $"{r.OutstandingPayments} ریال" : "داده در دسترس نیست";
            facts.Add($"تأمین‌کننده «{r.Name}»: مانده تأمین‌کننده {bal}، پرداخت‌های معوق {outst}.");
        }

        var cloudPayload = summary.Rows.Select(r => new
        {
            name = r.Name,
            vendorBalance = r.VendorBalance,
            outstandingPayments = r.OutstandingPayments
        }).ToList();

        return new ToolExecutionResult(
            callId,
            "vendor_financials_list",
            "vendor-financials",
            facts,
            JsonSerializer.Serialize(cloudPayload),
            true,
            Period: period,
            RawResult: summary,
            DataQuality: summary.DataQuality,
            TotalRows: summary.TotalRows);
    }

    private async Task<(string? VendorKey, string VendorName)> ResolveVendorAsync(
        JsonElement? args,
        JalaliPeriod period,
        CancellationToken ct)
    {
        string? vendorInput = null;
        string? vendorKey = null;
        if (args.HasValue && args.Value.ValueKind == JsonValueKind.Object)
        {
            if (args.Value.TryGetProperty("vendorKey", out var vk) && vk.ValueKind == JsonValueKind.String)
                vendorKey = vk.GetString();
            if (args.Value.TryGetProperty("vendor", out var v) && v.ValueKind == JsonValueKind.String)
                vendorInput = v.GetString();
        }

        if (!string.IsNullOrWhiteSpace(vendorKey) && !string.IsNullOrWhiteSpace(vendorInput))
        {
            return (vendorKey, vendorInput);
        }

        if (string.IsNullOrWhiteSpace(vendorInput))
        {
            return (null, "تأمین‌کننده");
        }

        var matchHeader = await financialsService.FindVendorInTextAsync(vendorInput, ct);
        if (matchHeader != null)
        {
            return (matchHeader.Key, matchHeader.Name ?? matchHeader.Key ?? vendorInput);
        }

        var request = new VendorFinancialRequest(
            Period: period,
            Compare: false,
            Search: vendorInput,
            NormalizedSearch: VendorFinancialPeriod.SearchKey(vendorInput),
            Page: 1,
            PageSize: 1,
            Sort: "name",
            Descending: false);

        var list = await financialsService.SummaryAsync(request, ct);
        var match = list.Rows.FirstOrDefault();
        if (match != null)
        {
            return (match.Key, match.Name);
        }

        return (vendorInput, vendorInput);
    }

    private async Task<ToolExecutionResult> ExecuteVendorBalanceAsync(
        string callId,
        JalaliPeriod period,
        JsonElement? args,
        CancellationToken ct)
    {
        var (vendorKey, vendorName) = await ResolveVendorAsync(args, period, ct);
        if (vendorKey == null)
        {
            return new ToolExecutionResult(callId, "vendor_balance", "vendor-financials-detail", ["نام تأمین‌کننده مشخص نشده است."], "{\"unavailable\": true}", false, Period: period);
        }

        var request = new VendorFinancialRequest(period, false, null, string.Empty, 1, 25, "name", false);
        var detail = await financialsService.DetailAsync(vendorKey, request, ct);

        if (detail == null || detail.VendorBalance == null)
        {
            return new ToolExecutionResult(callId, "vendor_balance", "vendor-financials-detail", [$"داده مانده تأمین‌کننده برای «{vendorName}» در دوره {period.Label} در دسترس نیست."], "{\"unavailable\": true}", false, Period: period, Vendor: vendorName);
        }

        var facts = new List<string>
        {
            $"در دوره {period.Label}، مانده تأمین‌کننده «{detail.Vendor.Name}» مبلغ {detail.VendorBalance} ریال است."
        };

        var content = new { vendor = detail.Vendor.Name, vendorBalance = detail.VendorBalance };

        return new ToolExecutionResult(
            callId,
            "vendor_balance",
            "vendor-financials-detail",
            facts,
            JsonSerializer.Serialize(content),
            true,
            Period: period,
            Vendor: detail.Vendor.Name,
            RawResult: detail,
            DataQuality: detail.DataQuality,
            TotalRows: 1);
    }

    private async Task<ToolExecutionResult> ExecuteVendorPaymentsAsync(
        string callId,
        JalaliPeriod period,
        JsonElement? args,
        CancellationToken ct)
    {
        var (vendorKey, vendorName) = await ResolveVendorAsync(args, period, ct);
        if (vendorKey == null)
        {
            return new ToolExecutionResult(callId, "vendor_payments", "vendor-financials-detail", ["نام تأمین‌کننده مشخص نشده است."], "{\"unavailable\": true}", false, Period: period);
        }

        var request = new VendorFinancialRequest(period, false, null, string.Empty, 1, 25, "name", false);
        var detail = await financialsService.DetailAsync(vendorKey, request, ct);

        if (detail == null || detail.PurchasesVsPayments.Payments == null)
        {
            return new ToolExecutionResult(callId, "vendor_payments", "vendor-financials-detail", [$"داده پرداخت‌ها برای «{vendorName}» در دوره {period.Label} در دسترس نیست."], "{\"unavailable\": true}", false, Period: period, Vendor: vendorName);
        }

        var facts = new List<string>
        {
            $"در دوره {period.Label}، مجموع پرداخت‌ها به تأمین‌کننده «{detail.Vendor.Name}» مبلغ {detail.PurchasesVsPayments.Payments} ریال بوده است."
        };

        var content = new { vendor = detail.Vendor.Name, payments = detail.PurchasesVsPayments.Payments };

        return new ToolExecutionResult(
            callId,
            "vendor_payments",
            "vendor-financials-detail",
            facts,
            JsonSerializer.Serialize(content),
            true,
            Period: period,
            Vendor: detail.Vendor.Name,
            RawResult: detail,
            DataQuality: detail.DataQuality,
            TotalRows: 1);
    }

    private async Task<ToolExecutionResult> ExecuteVendorOutstandingAsync(
        string callId,
        JalaliPeriod period,
        JsonElement? args,
        CancellationToken ct)
    {
        var (vendorKey, vendorName) = await ResolveVendorAsync(args, period, ct);
        if (vendorKey == null)
        {
            return new ToolExecutionResult(callId, "vendor_outstanding", "vendor-financials-detail", ["نام تأمین‌کننده مشخص نشده است."], "{\"unavailable\": true}", false, Period: period);
        }

        var request = new VendorFinancialRequest(period, false, null, string.Empty, 1, 25, "name", false);
        var detail = await financialsService.DetailAsync(vendorKey, request, ct);

        if (detail == null || detail.OutstandingPayments == null)
        {
            return new ToolExecutionResult(callId, "vendor_outstanding", "vendor-financials-detail", [$"داده پرداخت‌های معوق برای «{vendorName}» در دوره {period.Label} در دسترس نیست."], "{\"unavailable\": true}", false, Period: period, Vendor: vendorName);
        }

        var facts = new List<string>
        {
            $"در دوره {period.Label}، پرداخت‌های معوق تأمین‌کننده «{detail.Vendor.Name}» مبلغ {detail.OutstandingPayments} ریال است."
        };

        var content = new { vendor = detail.Vendor.Name, outstandingPayments = detail.OutstandingPayments };

        return new ToolExecutionResult(
            callId,
            "vendor_outstanding",
            "vendor-financials-detail",
            facts,
            JsonSerializer.Serialize(content),
            true,
            Period: period,
            Vendor: detail.Vendor.Name,
            RawResult: detail,
            DataQuality: detail.DataQuality,
            TotalRows: 1);
    }

    private async Task<ToolExecutionResult> ExecutePurchasesVsPaymentsAsync(
        string callId,
        JalaliPeriod period,
        JsonElement? args,
        CancellationToken ct)
    {
        var (vendorKey, vendorName) = await ResolveVendorAsync(args, period, ct);
        if (vendorKey == null)
        {
            return new ToolExecutionResult(callId, "vendor_purchases_vs_payments", "vendor-financials-detail", ["نام تأمین‌کننده مشخص نشده است."], "{\"unavailable\": true}", false, Period: period);
        }

        var request = new VendorFinancialRequest(period, false, null, string.Empty, 1, 25, "name", false);
        var detail = await financialsService.DetailAsync(vendorKey, request, ct);

        if (detail == null)
        {
            return new ToolExecutionResult(callId, "vendor_purchases_vs_payments", "vendor-financials-detail", [$"اطلاعات تأمین‌کننده «{vendorName}» یافت نشد."], "{\"unavailable\": true}", false, Period: period, Vendor: vendorName);
        }

        var net = detail.PurchasesVsPayments.NetPurchases != null
            ? $"مبلغ {detail.PurchasesVsPayments.NetPurchases} ریال"
            : "داده در دسترس نیست";
        var pay = detail.PurchasesVsPayments.Payments != null
            ? $"مبلغ {detail.PurchasesVsPayments.Payments} ریال"
            : "داده در دسترس نیست";

        var facts = new List<string>
        {
            $"در دوره {period.Label} برای تأمین‌کننده «{detail.Vendor.Name}»: خالص خرید {net} و پرداخت‌ها {pay} است."
        };

        if (detail.PurchasesVsPayments.PurchasesMinusPayments is { } minus)
        {
            facts.Add($"خرید منهای پرداخت: مبلغ {minus} ریال است.");
        }

        var content = new
        {
            vendor = detail.Vendor.Name,
            netPurchases = detail.PurchasesVsPayments.NetPurchases,
            payments = detail.PurchasesVsPayments.Payments,
            purchasesMinusPayments = detail.PurchasesVsPayments.PurchasesMinusPayments
        };

        return new ToolExecutionResult(
            callId,
            "vendor_purchases_vs_payments",
            "vendor-financials-detail",
            facts,
            JsonSerializer.Serialize(content),
            true,
            Period: period,
            Vendor: detail.Vendor.Name,
            RawResult: detail,
            DataQuality: detail.DataQuality,
            TotalRows: 1);
    }
}
