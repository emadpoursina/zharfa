namespace Zharfa.Api.Tests.Ai;

using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using Zharfa.Api.Ai;
using Zharfa.Api.Analytics;
using Zharfa.Api.Purchasing.Reports;

public class PurchasingPeriodDefaultTests
{
    private class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Fact]
    public void Omitted_period_resolves_to_current_jalali_month()
    {
        // 2026-11-18 is Jalali 1405-08-28
        var fakeClock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var tools = new PurchasingAnalystTools(null!, null!, null!, null!, null!, fakeClock, Microsoft.Extensions.Logging.Abstractions.NullLogger<PurchasingAnalystTools>.Instance);

        using var doc = JsonDocument.Parse("{}");
        var period = tools.ResolvePeriod(doc.RootElement);

        Assert.Equal(JalaliPeriodKind.Month, period.Kind);
        Assert.Equal(1405, period.From.Year);
        Assert.Equal(8, period.From.Month);

        var defaultPeriod = tools.GetDefaultPeriod();
        Assert.Equal(period.From, defaultPeriod.From);
        Assert.Equal(period.To, defaultPeriod.To);
        Assert.Equal(period.Label, defaultPeriod.Label);
    }

    [Fact]
    public void Each_chat_is_independent_messages_contain_only_system_and_single_user_message()
    {
        var question1 = "خالص خرید این ماه چقدر است؟";
        var messages1 = new List<AiMessage>
        {
            new("system", "prompt"),
            new("user", question1)
        };

        var question2 = "بیشترین خرید از کدام تأمین‌کننده بوده است؟";
        var messages2 = new List<AiMessage>
        {
            new("system", "prompt"),
            new("user", question2)
        };

        Assert.Equal(2, messages2.Count);
        Assert.Equal("system", messages2[0].Role);
        Assert.Equal("user", messages2[1].Role);
        Assert.Equal(question2, messages2[1].Content);
        Assert.DoesNotContain(messages2, m => m.Content == question1);
    }

    [Fact]
    public void Future_empty_month_binds_next_jalali_month_after_overview_default()
    {
        // 2026-11-18 is Jalali 1405-08-28 (آبان ۱۴۰۵)
        var fakeClock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var tools = new PurchasingAnalystTools(null!, null!, null!, null!, null!, fakeClock, Microsoft.Extensions.Logging.Abstractions.NullLogger<PurchasingAnalystTools>.Instance);

        var defaultPeriod = tools.GetDefaultPeriod();
        Assert.Equal(1405, defaultPeriod.From.Year);
        Assert.Equal(8, defaultPeriod.From.Month);

        var futurePeriod = tools.BindPeriod("future-empty-month");
        Assert.Equal(JalaliPeriodKind.Month, futurePeriod.Kind);
        Assert.Equal(1405, futurePeriod.From.Year);
        Assert.Equal(9, futurePeriod.From.Month); // Next month: آذر ۱۴۰۵ (NOT current month)
        Assert.Equal(1, futurePeriod.From.Day);
        Assert.Equal(30, futurePeriod.To.Day);
        Assert.Contains("آذر ۱۴۰۵", futurePeriod.Label);
    }

    [Fact]
    public void Previous_jalali_month_matches_report_helper()
    {
        // 2026-11-18 is Jalali 1405-08-28 (آبان ۱۴۰۵)
        var fakeClock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var tools = new PurchasingAnalystTools(null!, null!, null!, null!, null!, fakeClock, Microsoft.Extensions.Logging.Abstractions.NullLogger<PurchasingAnalystTools>.Instance);

        var prevPeriod = tools.BindPeriod("previous-jalali-month");
        Assert.Equal(JalaliPeriodKind.Month, prevPeriod.Kind);
        Assert.Equal(1405, prevPeriod.From.Year);
        Assert.Equal(7, prevPeriod.From.Month); // Previous month: مهر ۱۴۰۵
        Assert.Contains("مهر ۱۴۰۵", prevPeriod.Label);
    }

    [Fact]
    public void Current_jalali_quarter_matches_report_helper()
    {
        // 2026-11-18 is Jalali 1405-08-28 (in Q3: فصل سوم ۱۴۰۵)
        var fakeClock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var tools = new PurchasingAnalystTools(null!, null!, null!, null!, null!, fakeClock, Microsoft.Extensions.Logging.Abstractions.NullLogger<PurchasingAnalystTools>.Instance);

        var quarterPeriod = tools.BindPeriod("current-jalali-quarter");
        Assert.Equal(JalaliPeriodKind.Quarter, quarterPeriod.Kind);
        Assert.Equal(1405, quarterPeriod.From.Year);
        Assert.Equal(7, quarterPeriod.From.Month); // Q3 starts month 7 (مهر)
        Assert.Contains("فصل سوم", quarterPeriod.Label);
    }
}
