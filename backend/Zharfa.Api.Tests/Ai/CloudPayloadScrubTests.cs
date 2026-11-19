namespace Zharfa.Api.Tests.Ai;

using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using Zharfa.Api.Ai;

public class CloudPayloadScrubTests
{
    private static readonly string[] ForbiddenTokens =
    [
        "\"lines\":",
        "\"password\":",
        "\"apiKey\":",
        "\"token\":",
        "national_id",
        "nationalId",
        "phone_number",
        "phoneNumber",
        "document_lines"
    ];

    [Fact]
    public void Cloud_bound_tool_result_payload_contains_only_aggregates_and_no_forbidden_fields()
    {
        // Snapshot of representative tool results and messages sent to cloud provider
        var toolPayload = new
        {
            view = "purchasing_overview",
            period = "1405-08",
            totals = new
            {
                net = "12500000000",
                gross = "13000000000",
                returns = "500000000",
                vat = "1125000000"
            },
            activeVendors = 15,
            topVendors = new[]
            {
                new { name = "تأمین‌کننده البرز", net = "4500000000" },
                new { name = "داروسازی تهران", net = "3200000000" }
            }
        };

        var toolJson = JsonSerializer.Serialize(toolPayload);

        var messages = new List<AiMessage>
        {
            new("system", "شما تحلیل‌گر هوشمند خرید هستید."),
            new("user", "خالص خرید این ماه چقدر است؟"),
            new("tool", toolJson, ToolCallId: "call_123")
        };

        var cloudPayloadDict = new Dictionary<string, object>
        {
            ["model"] = "gpt-4o-mini",
            ["messages"] = messages,
            ["tools"] = PurchasingAnalystTools.GetToolSpecs()
        };

        var serializedJson = JsonSerializer.Serialize(cloudPayloadDict);

        // Assert forbidden fields/tokens are absent
        foreach (var token in ForbiddenTokens)
        {
            Assert.DoesNotContain(token, serializedJson, StringComparison.OrdinalIgnoreCase);
        }

        // Assert allowed aggregate fields are present
        Assert.Contains("purchasing_overview", serializedJson);
        Assert.Contains("1405-08", serializedJson);
        Assert.Contains("topVendors", serializedJson);
        Assert.Contains("activeVendors", serializedJson);
    }

    [Fact]
    public void Purchasing_transactions_summary_payload_is_aggregates_only_and_omits_document_headers()
    {
        // Tool payload as produced by ExecuteTransactionsSummaryAsync
        var payload = new
        {
            view = "purchasing_transactions_summary",
            period = "آبان ۱۴۰۵",
            totalRows = 42,
            totals = new
            {
                net = 5000000000m,
                invoiceAmount = 5200000000m,
                returnAmount = 200000000m,
                documentCount = 42
            }
        };

        var toolJson = JsonSerializer.Serialize(payload);

        // Assert document headers and lines are strictly absent
        Assert.DoesNotContain("\"documents\":", toolJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"document_lines\":", toolJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"number\":", toolJson, StringComparison.OrdinalIgnoreCase);

        // Assert aggregate totals and counts are present
        Assert.Contains("\"totalRows\":42", toolJson);
        Assert.Contains("\"totals\":", toolJson);
        Assert.Contains("\"net\":5000000000", toolJson);
    }
}
