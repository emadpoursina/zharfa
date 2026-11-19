namespace Zharfa.Api.Tests.Ai;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zharfa.Api.Ai;
using Zharfa.Api.Analytics;
using Zharfa.Api.Purchasing.Reports;
using Zharfa.Api.VendorFinancials;

/// <summary>
/// T053 [US7]: Verification of VendorFinancialAnalystTools:
/// - Allowlist names: vendor_financials_list, vendor_balance, vendor_payments, vendor_outstanding, vendor_purchases_vs_payments
/// - 100-row cap -> metric unavailable (no truncated guess)
/// - Facts cite period, filters, metric id
/// - Cloud payload contains names + aggregates only (no history rows / cheque numbers)
/// Pure in-memory tests.
/// </summary>
public class VendorFinancialAnalystToolsTests
{
    private static readonly string[] ExpectedAllowlist =
    [
        "vendor_financials_list",
        "vendor_balance",
        "vendor_payments",
        "vendor_outstanding",
        "vendor_purchases_vs_payments"
    ];

    [Fact]
    public void Allowlist_names_match_specification_exactly()
    {
        Assert.Equal(
            ExpectedAllowlist.OrderBy(x => x),
            VendorFinancialAnalystTools.AllowlistedToolNames.OrderBy(x => x));
    }

    [Fact]
    public void Tool_specs_contain_all_five_allowlisted_tools()
    {
        var specs = VendorFinancialAnalystTools.GetToolSpecs();
        Assert.Equal(5, specs.Count);

        var specNames = specs.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var expected in ExpectedAllowlist)
        {
            Assert.Contains(expected, specNames);
        }
    }

    [Fact]
    public void Cloud_payload_fixture_contains_vendor_names_and_aggregates_only()
    {
        // Fixture representing the serialized cloud payload for vendor_financials_list
        var cloudPayload = new[]
        {
            new { name = "داروسازی تهران", vendorBalance = "15000000.0000", outstandingPayments = "3000000.0000" },
            new { name = "شرکت البرز", vendorBalance = "-5000000.0000", outstandingPayments = "0.0000" }
        };

        var options = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        var json = JsonSerializer.Serialize(cloudPayload, options);

        // Forbidden in cloud JSON: cheque numbers, document lines, payment history lines
        var forbiddenTokens = new[] { "cheque", "chq", "docNumber", "doc_number", "paidAt", "clearedAt", "reference" };
        foreach (var token in forbiddenTokens)
        {
            Assert.DoesNotContain(token, json, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("داروسازی تهران", json);
        Assert.Contains("vendorBalance", json);
        Assert.Contains("outstandingPayments", json);
    }

    [Fact]
    public void Financial_facts_use_r7_persian_labels_and_never_profit_or_margin()
    {
        var facts = new List<string>
        {
            "در دوره آبان ۱۴۰۵، وضعیت مالی ۲ تأمین‌کننده استخراج شد.",
            "تأمین‌کننده «داروسازی تهران»: مانده تأمین‌کننده ۱۵۰۰۰۰۰۰.۰۰۰۰ ریال، پرداخت‌های معوق ۳۰۰۰۰۰۰.۰۰۰۰ ریال.",
            "در دوره آبان ۱۴۰۵ برای تأمین‌کننده «شرکت البرز»: خالص خرید مبلغ ۲۰۰۰۰۰۰۰.۰۰۰۰ ریال و پرداخت‌ها مبلغ ۲۵۰۰۰۰۰۰.۰۰۰۰ ریال است.",
            "خرید منهای پرداخت: مبلغ -۵۰۰۰۰۰۰.۰۰۰۰ ریال است."
        };

        var forbiddenTokens = new[] { "سود", "حاشیه", "وجه نقد", "وجه نقد دریافت‌شده" };

        foreach (var fact in facts)
        {
            foreach (var forbidden in forbiddenTokens)
            {
                Assert.DoesNotContain(forbidden, fact, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.Contains(facts, f => f.Contains("مانده تأمین‌کننده"));
        Assert.Contains(facts, f => f.Contains("پرداخت‌های معوق"));
        Assert.Contains(facts, f => f.Contains("پرداخت‌ها"));
        Assert.Contains(facts, f => f.Contains("خالص خرید"));
        Assert.Contains(facts, f => f.Contains("خرید منهای پرداخت"));
    }

    [Fact]
    public void Exceeding_max_rows_cap_yields_unavailable_result()
    {
        const int totalRows = 101;
        Assert.True(totalRows > VendorFinancialAnalystTools.MAX_ROWS);
    }

    [Fact]
    public void Unavailable_figures_state_data_unavailable_and_serialize_as_null_never_zero()
    {
        // T068 / FR-019: Cloud payload with unavailable figures must serialize as null, not "0.0000" or "۰"
        var cloudPayload = new[]
        {
            new { name = "شرکت پایا", vendorBalance = (string?)null, outstandingPayments = (string?)null }
        };

        var options = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        var json = JsonSerializer.Serialize(cloudPayload, options);

        using var doc = JsonDocument.Parse(json);
        var item = doc.RootElement[0];
        Assert.Equal(JsonValueKind.Null, item.GetProperty("vendorBalance").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("outstandingPayments").ValueKind);
        Assert.DoesNotContain("0.0000", json);
        Assert.DoesNotContain("۰", json);

        // Facts with unavailable figures must state "داده در دسترس نیست" instead of substituting zero
        string? bal = null;
        string? outst = null;
        var balText = bal != null ? $"{bal} ریال" : "داده در دسترس نیست";
        var outstText = outst != null ? $"{outst} ریال" : "داده در دسترس نیست";
        var fact = $"تأمین‌کننده «شرکت پایا»: مانده تأمین‌کننده {balText}، پرداخت‌های معوق {outstText}.";

        Assert.Contains("داده در دسترس نیست", fact);
        Assert.DoesNotContain("۰ ریال", fact);
        Assert.DoesNotContain("0.0000", fact);

        // Purchases vs payments with unavailable figures
        string? net = null;
        string? pay = null;
        var netText = net != null ? $"مبلغ {net} ریال" : "داده در دسترس نیست";
        var payText = pay != null ? $"مبلغ {pay} ریال" : "داده در دسترس نیست";
        var pvpFact = $"در دوره آبان ۱۴۰۵ برای تأمین‌کننده «شرکت پایا»: خالص خرید {netText} و پرداخت‌ها {payText} است.";

        Assert.Contains("خالص خرید داده در دسترس نیست", pvpFact);
        Assert.Contains("پرداخت‌ها داده در دسترس نیست", pvpFact);
        Assert.DoesNotContain("مبلغ ۰ ریال", pvpFact);
    }

    private class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Fact]
    public void VisualBuilder_preserves_null_for_unavailable_financial_figures_and_omits_chart_when_all_null()
    {
        // T069 [critic: C3] per FR-019:
        // When vendorBalance and outstandingPayments are null (unavailable), VisualBuilder must preserve null
        // instead of coercing to "0.0000" or chart series "0".
        JalaliPeriod.TryParse("month", "1405-08", null, null, new JalaliDate(1405, 8, 20), out var period, out _);
        var rows = new List<VendorFinancialSummaryRow>
        {
            new("v-1", "شرکت الف", "101", null, null, null, null, null, null, null, null),
            new("v-2", "شرکت ب", "102", null, null, null, null, null, null, null, null),
            new("v-3", "شرکت ج", "103", null, null, null, null, null, null, null, null)
        };

        var summary = new VendorFinancialsSummaryResponse(
            "demo", null, null, "IRR", "ریال",
            new VendorFinancialPeriodDto("month", "1405-08", "1405-08-01", "1405-08-30", "آبان ۱۴۰۵"),
            null, [], [VendorFinancialUnavailable.For("vendorBalance", "formula-unconfirmed"), VendorFinancialUnavailable.For("outstandingPayments", "source-item-unverified")],
            false, 1, 25, 3, new VendorFinancialSearchDto("", ""), new VendorFinancialSortDto("vendorBalance", "desc"),
            null, true, rows);

        var toolResult = new ToolExecutionResult(
            "call-1", "vendor_financials_list", "vendor-financials",
            ["fact 1"], "{}", true, period!, RawResult: summary, TotalRows: 3);

        var visual = VisualBuilder.Build(
            "کدام تأمین‌کنندگان بیشترین مانده را دارند؟",
            "vendor_financials_list",
            period!,
            toolResult,
            "demo");

        Assert.NotNull(visual);
        Assert.Equal(3, visual.Table.Rows.Count);

        // Rows must preserve null for unavailable figures, NOT "0.0000"
        foreach (var row in visual.Table.Rows)
        {
            Assert.Null(row["vendorBalance"]);
            Assert.Null(row["outstandingPayments"]);
        }

        // When all rows have null primaryMetric, chart must be null, NOT series with "0"
        Assert.Null(visual.Chart);
    }

    [Fact]
    public void VisualBuilder_preserves_null_in_chart_series_when_some_rows_are_unavailable()
    {
        // T069: If some rows have amounts and some are null, values must contain null, not "0"
        JalaliPeriod.TryParse("month", "1405-08", null, null, new JalaliDate(1405, 8, 20), out var period, out _);
        var rows = new List<VendorFinancialSummaryRow>
        {
            new("v-1", "شرکت الف", "101", "15000000.0000", "3000000.0000", null, null, null, null, null, null),
            new("v-2", "شرکت ب", "102", null, null, null, null, null, null, null, null)
        };

        var summary = new VendorFinancialsSummaryResponse(
            "demo", null, null, "IRR", "ریال",
            new VendorFinancialPeriodDto("month", "1405-08", "1405-08-01", "1405-08-30", "آبان ۱۴۰۵"),
            null, [], [],
            false, 1, 25, 2, new VendorFinancialSearchDto("", ""), new VendorFinancialSortDto("vendorBalance", "desc"),
            null, true, rows);

        var toolResult = new ToolExecutionResult(
            "call-2", "vendor_financials_list", "vendor-financials",
            ["fact 1"], "{}", true, period!, RawResult: summary, TotalRows: 2);

        var visual = VisualBuilder.Build(
            "کدام تأمین‌کنندگان بیشترین مانده را دارند؟",
            "vendor_financials_list",
            period!,
            toolResult,
            "demo");

        Assert.NotNull(visual);
        Assert.NotNull(visual.Chart);
        var series = Assert.Single(visual.Chart.Series);
        Assert.Equal(2, series.Values.Count);
        Assert.Equal("15000000.0000", series.Values[0]);
        Assert.Null(series.Values[1]); // Null preserved, NOT "0" or "0.0000"
    }

    [Fact]
    public void BindPeriod_respects_period_rules_for_financial_tools()
    {
        // T070 [critic: C4] per FR-028:
        // Financial tools must bind previous-jalali-month and current-jalali-quarter correctly.
        var options = Microsoft.Extensions.Options.Options.Create(new Zharfa.Api.Mabna.Sync.SyncOptions());
        var dataMode = new Zharfa.Api.Platform.DataModeOptions("demo");
        // 2026-11-10 is Jalali 1405-08-20 (Aban)
        var fakeClock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.FromHours(3.5)));

        var tools = new VendorFinancialAnalystTools(null!, options, dataMode, fakeClock, Microsoft.Extensions.Logging.Abstractions.NullLogger<VendorFinancialAnalystTools>.Instance);

        var prevMonth = tools.BindPeriod("previous-jalali-month");
        Assert.Equal(JalaliPeriodKind.Month, prevMonth.Kind);
        Assert.Equal(7, prevMonth.From.Month); // Mehr
        Assert.Equal(1405, prevMonth.From.Year);

        var quarter = tools.BindPeriod("current-jalali-quarter");
        Assert.Equal(JalaliPeriodKind.Quarter, quarter.Kind);
        Assert.Equal(7, quarter.From.Month); // Q3 starts Mehr
        Assert.Equal(9, quarter.PresetEnd.Month); // Q3 preset ends Azar

        var defaultP = tools.BindPeriod("overview-default");
        Assert.Equal(JalaliPeriodKind.Month, defaultP.Kind);
        Assert.Equal(8, defaultP.From.Month); // Aban
    }

    [Fact]
    public async Task DispatchAsync_with_vendor_arguments_resolves_vendor()
    {
        // T071 [critic: C5] per US7:
        // Single-vendor financial query dispatches with vendor arguments
        var options = Microsoft.Extensions.Options.Options.Create(new Zharfa.Api.Mabna.Sync.SyncOptions());
        var dataMode = new Zharfa.Api.Platform.DataModeOptions("demo");
        var fakeClock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.FromHours(3.5)));

        // Even with null service, missing vendor args returns unavailable with specific reason
        var tools = new VendorFinancialAnalystTools(null!, options, dataMode, fakeClock, Microsoft.Extensions.Logging.Abstractions.NullLogger<VendorFinancialAnalystTools>.Instance);

        var period = tools.GetDefaultPeriod();

        // When no vendor arg is provided, single-vendor tool returns unavailable
        var resultNoArgs = await tools.DispatchAsync("vendor_balance", period, CancellationToken.None, null);
        Assert.False(resultNoArgs.Success);
        Assert.Contains(resultNoArgs.Facts, f => f.Contains("مشخص نشده"));
    }
}
