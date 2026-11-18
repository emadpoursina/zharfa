namespace Zharfa.Api.Tests.Ai;

using System;
using Xunit;
using Zharfa.Api.Controllers;

public class FactsVsInterpretationTests
{
    [Fact]
    public void When_tools_return_no_fact_unavailable_is_true_and_interpretation_empty()
    {
        var response = new PurchasingChatResponse(
            Answer: "داده در دسترس نیست.",
            Period: new PurchasingPeriodDto(string.Empty, string.Empty, string.Empty),
            Filters: new PurchasingFiltersDto(),
            View: null,
            Facts: Array.Empty<string>(),
            Interpretation: string.Empty,
            Unavailable: true,
            DataLabel: "real");

        Assert.True(response.Unavailable);
        Assert.Contains("داده در دسترس نیست", response.Answer);
        Assert.Empty(response.Interpretation);
        Assert.Empty(response.Facts);
    }

    [Fact]
    public void When_facts_exist_facts_are_server_authored_and_interpretation_is_separate()
    {
        var facts = new[]
        {
            "metric=purchasing_overview.net; period=1405-08; value=12500000000",
            "metric=purchasing_overview.activeVendors; period=1405-08; count=12"
        };
        var interpretation = "خالص خرید در این ماه نسبت به میانگین رشد داشته است.";

        var response = new PurchasingChatResponse(
            Answer: interpretation,
            Period: new PurchasingPeriodDto("2026-10-23", "2026-11-21", "آبان ۱۴۰۵", "1405-08"),
            Filters: new PurchasingFiltersDto(),
            View: "purchasing_overview",
            Facts: facts,
            Interpretation: interpretation,
            Unavailable: false,
            DataLabel: "real");

        Assert.False(response.Unavailable);
        Assert.Equal(2, response.Facts.Count);
        Assert.Contains("metric=purchasing_overview.net", response.Facts[0]);
        Assert.Equal(interpretation, response.Interpretation);
        Assert.Equal("real", response.DataLabel);
        Assert.Null(response.DemoNotice);
    }

    [Fact]
    public void Production_dataLabel_is_real_without_demo_notice()
    {
        var response = new PurchasingChatResponse(
            Answer: "پاسخ خرید واقعی",
            Period: new PurchasingPeriodDto("2026-10-23", "2026-11-21", "آبان ۱۴۰۵"),
            Filters: new PurchasingFiltersDto(),
            View: "purchasing_overview",
            Facts: new[] { "fact1" },
            Interpretation: "تفسیر",
            Unavailable: false,
            DataLabel: "real",
            DemoNotice: null);

        Assert.Equal("real", response.DataLabel);
        Assert.Null(response.DemoNotice);
    }

    [Fact]
    public void Demo_dataLabel_is_demo_with_demonstration_notice()
    {
        const string notice = "این پاسخ فقط بر اساس داده‌های نمایشی (دمو) تولید شده و هیچ داده واقعی شرکت در آن استفاده نشده است.";
        var response = new PurchasingChatResponse(
            Answer: "پاسخ خرید دمو",
            Period: new PurchasingPeriodDto("2026-10-23", "2026-11-21", "آبان ۱۴۰۵"),
            Filters: new PurchasingFiltersDto(),
            View: "purchasing_overview",
            Facts: new[] { "fact1" },
            Interpretation: "تفسیر",
            Unavailable: false,
            DataLabel: "demo",
            DemoNotice: notice);

        Assert.Equal("demo", response.DataLabel);
        Assert.Equal(notice, response.DemoNotice);
        Assert.Contains("نمایشی", response.DemoNotice);
    }
}
