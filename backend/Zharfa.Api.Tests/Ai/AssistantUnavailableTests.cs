namespace Zharfa.Api.Tests.Ai;

using System;
using System.Text.Json;
using Xunit;
using Zharfa.Api.Controllers;

public class AssistantUnavailableTests
{
    [Fact]
    public void Unavailable_503_payload_has_persian_message_code_no_api_keys_and_no_stack_traces()
    {
        var response = new PurchasingChatUnavailableResponse(
            Unavailable: true,
            Code: "assistant-unavailable",
            Answer: "پاسخی ارائه نشد؛ دستیار هوشمند در حال حاضر در دسترس نیست.",
            Message: "سرویس هوش مصنوعی پیکربندی نشده است.",
            Period: null,
            Filters: null,
            Facts: Array.Empty<string>(),
            Interpretation: string.Empty,
            View: null,
            DataLabel: "real",
            DemoNotice: null);

        Assert.True(response.Unavailable);
        Assert.Equal("assistant-unavailable", response.Code);
        Assert.Contains("دستیار هوشمند در حال حاضر در دسترس نیست", response.Answer);
        Assert.Empty(response.Facts);
        Assert.Empty(response.Interpretation);

        var webOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(response, webOptions);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("code", out var codeProp));
        Assert.Equal("assistant-unavailable", codeProp.GetString());

        // Default serializer also preserves "code" in lowercase due to [JsonPropertyName("code")]
        var defaultJson = JsonSerializer.Serialize(response);
        using var defaultDoc = JsonDocument.Parse(defaultJson);
        Assert.True(defaultDoc.RootElement.TryGetProperty("code", out var defaultCodeProp));
        Assert.Equal("assistant-unavailable", defaultCodeProp.GetString());

        // Security checks: no secret keys, no stack traces
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("at Zharfa.", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stackTrace", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception:", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assistant_unavailable_phrasing_is_distinct_from_metric_unavailable_phrase()
    {
        var assistantUnavailableAnswer = "پاسخی ارائه نشد؛ دستیار هوشمند در حال حاضر در دسترس نیست.";
        var metricUnavailableAnswer = "داده در دسترس نیست.";

        Assert.NotEqual(assistantUnavailableAnswer, metricUnavailableAnswer);
        Assert.DoesNotContain("دستیار", metricUnavailableAnswer);
    }

    [Fact]
    public void Metric_unavailable_200_response_has_no_code_property()
    {
        var response = new PurchasingChatResponse(
            Answer: "داده در دسترس نیست.",
            Period: new PurchasingPeriodDto("2026-10-23", "2026-11-21", "آبان ۱۴۰۵"),
            Filters: new PurchasingFiltersDto(),
            View: null,
            Facts: Array.Empty<string>(),
            Interpretation: string.Empty,
            Unavailable: true,
            DataLabel: "real");

        var webOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(response, webOptions);
        using var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.TryGetProperty("code", out _));
        Assert.DoesNotContain("assistant-unavailable", json);
    }
}
