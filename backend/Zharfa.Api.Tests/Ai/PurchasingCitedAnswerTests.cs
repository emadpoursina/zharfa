namespace Zharfa.Api.Tests.Ai;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Zharfa.Api.Ai;
using Zharfa.Api.Analytics;
using Zharfa.Api.Controllers;
using Zharfa.Api.Data;
using Zharfa.Api.Platform;
using Zharfa.Api.Purchasing.Reports;
using Zharfa.Api.Security;

public class PurchasingCitedAnswerTests
{
    private static readonly PurchasingPeriodDto SamplePeriod = new("2026-10-23", "2026-11-21", "آبان ۱۴۰۵", "1405-08");
    private static readonly PurchasingFiltersDto SampleFilters = new();

    private class TestAiProvider : IAiProvider
    {
        public bool IsConfigured { get; set; } = true;
        public Func<CancellationToken, Task>? OnProbeLiveness { get; set; }
        public Func<IReadOnlyList<AiMessage>, IReadOnlyList<AiToolSpec>, CancellationToken, Task<AiTurn>>? OnComplete { get; set; }

        public ProviderStatus GetStatus() => new("test", "http://test/v1", "model", true);

        public Task ProbeLivenessAsync(CancellationToken ct)
        {
            if (OnProbeLiveness != null)
            {
                return OnProbeLiveness(ct);
            }
            return Task.CompletedTask;
        }

        public Task<AiTurn> CompleteAsync(
            IReadOnlyList<AiMessage> messages,
            IReadOnlyList<AiToolSpec> tools,
            CancellationToken ct)
        {
            if (OnComplete != null)
            {
                return OnComplete(messages, tools, ct);
            }
            return Task.FromResult(new AiTurn("پاسخ مدل", Array.Empty<AiToolCall>()));
        }
    }

    private class TestPurchasingAnalystTools : PurchasingAnalystTools
    {
        public Func<string, JalaliPeriod, CancellationToken, Task<ToolExecutionResult>>? OnDispatch { get; set; }

        public TestPurchasingAnalystTools()
            : base(null!, null!, null!, null!, new DataModeOptions("production"), TimeProvider.System, NullLogger<PurchasingAnalystTools>.Instance)
        {
        }

        public override Task<ToolExecutionResult> DispatchAsync(string toolName, JalaliPeriod period, CancellationToken ct)
        {
            if (OnDispatch != null)
            {
                return OnDispatch(toolName, period, ct);
            }
            return base.DispatchAsync(toolName, period, ct);
        }
    }

    private class FakeAuditLogger : IAuditLogger
    {
        public Task RecordAsync(string action, string? detail, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordAsync(AuditEntry entry, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static AiController CreateController(
        IAiProvider aiProvider,
        PurchasingAnalystTools analystTools)
    {
        var controller = new AiController(
            aiProvider,
            analystTools,
            new AiQuestionGate(),
            new FakeAuditLogger(),
            new DataModeOptions("production"),
            NullLogger<AiController>.Instance);

        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Role, Roles.Management)
        ], "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        return controller;
    }

    [Fact]
    public void When_model_invents_number_not_in_facts_answer_uses_server_template_and_interpretation_keeps_model_text()
    {
        var facts = new[]
        {
            "metric=purchasing_overview.net; period=1405-08; value=12500000000"
        };
        // 999999999 is not in facts, period, or filters
        var modelText = "خالص خرید در این ماه ۹۹۹۹۹۹۹۹۹ ریال است که رشد زیادی را نشان می‌دهد.";

        var (answer, interpretation) = PurchasingCitedAnswer.ResolveAnswer(
            facts,
            SamplePeriod,
            SampleFilters,
            view: "purchasing_overview",
            modelText: modelText);

        Assert.NotEqual(modelText, answer);
        Assert.Contains("آبان ۱۴۰۵", answer);
        Assert.Contains("ریال", answer);
        Assert.Contains("۱۲۵۰۰۰۰۰۰۰۰", answer); // Or formatted amount with ریال
        Assert.Equal(modelText.Trim(), interpretation);
    }

    [Fact]
    public void When_model_says_data_unavailable_while_facts_exist_answer_uses_server_template()
    {
        var facts = new[]
        {
            "metric=purchasing_overview.net; period=1405-08; value=12500000000"
        };
        var modelText = "متأسفانه داده در دسترس نیست و نمی‌توان اطلاعاتی ارائه داد.";

        var (answer, interpretation) = PurchasingCitedAnswer.ResolveAnswer(
            facts,
            SamplePeriod,
            SampleFilters,
            view: "purchasing_overview",
            modelText: modelText);

        Assert.NotEqual(modelText, answer);
        Assert.DoesNotContain("داده در دسترس نیست", answer);
        Assert.Contains("آبان ۱۴۰۵", answer);
        Assert.Contains("ریال", answer);
        Assert.Equal(modelText.Trim(), interpretation);
    }

    [Fact]
    public void When_model_text_stays_within_facts_answer_equals_model_text()
    {
        var facts = new[]
        {
            "metric=purchasing_overview.net; period=1405-08; value=12500000000"
        };
        var modelText = "خالص خرید در دوره آبان ۱۴۰۵ برابر ۱۲۵۰۰۰۰۰۰۰۰ ریال بوده است.";

        var (answer, interpretation) = PurchasingCitedAnswer.ResolveAnswer(
            facts,
            SamplePeriod,
            SampleFilters,
            view: "purchasing_overview",
            modelText: modelText);

        Assert.Equal(modelText, answer);
        Assert.Equal(modelText, interpretation);
    }

    [Fact]
    public void When_model_text_mentions_sales_or_pharmacy_sales_answer_uses_server_template()
    {
        var facts = new[]
        {
            "metric=purchasing_overview.net; period=1405-08; value=12500000000"
        };
        var modelText = "فروش داروخانه‌ها در این ماه ۱۲۵۰۰۰۰۰۰۰۰ ریال است.";

        var (answer, interpretation) = PurchasingCitedAnswer.ResolveAnswer(
            facts,
            SamplePeriod,
            SampleFilters,
            view: "purchasing_overview",
            modelText: modelText);

        Assert.NotEqual(modelText, answer);
        Assert.DoesNotContain("فروش داروخانه‌ها", answer);
        Assert.Contains("خرید", answer);
        Assert.Equal(modelText.Trim(), interpretation);
    }

    [Fact]
    public void Out_of_domain_helper_produces_unavailable_200_with_empty_facts_and_no_code()
    {
        var response = PurchasingCitedAnswer.CreateUnavailable(
            period: SamplePeriod,
            filters: SampleFilters,
            dataLabel: "real");

        Assert.True(response.Unavailable);
        Assert.Equal("داده در دسترس نیست.", response.Answer);
        Assert.Empty(response.Facts);
        Assert.Empty(response.Interpretation);
        Assert.Null(response.View);

        // Serialize and verify NO code property exists
        var json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("code", out _));
        Assert.DoesNotContain("assistant-unavailable", json);
    }

    [Fact]
    public async Task When_phrasing_times_out_after_tool_dispatch_succeeds_returns_200_server_templated_cited_answer()
    {
        var aiProvider = new TestAiProvider
        {
            OnProbeLiveness = _ => Task.CompletedTask,
            OnComplete = (_, _, _) => throw new OperationCanceledException()
        };

        var tools = new TestPurchasingAnalystTools
        {
            OnDispatch = (toolName, period, ct) => Task.FromResult(new ToolExecutionResult(
                "call_1",
                toolName,
                "purchasing_overview",
                new[] { "metric=purchasing_overview.net; period=1405-08; value=12500000000" },
                "{}",
                true,
                Period: period))
        };

        var controller = CreateController(aiProvider, tools);
        var result = await controller.Chat(new PurchasingChatRequest("خالص خرید این ماه چقدر است؟"), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode ?? StatusCodes.Status200OK);

        var response = Assert.IsType<PurchasingChatResponse>(okResult.Value);
        Assert.False(response.Unavailable);
        Assert.NotEmpty(response.Facts);
        Assert.Contains("خرید", response.Answer);
        Assert.Contains("ریال", response.Answer);
        Assert.Contains("۱۲۵۰۰۰۰۰۰۰۰", response.Answer);
        Assert.Empty(response.Interpretation);
    }

    [Fact]
    public async Task When_phrasing_fails_with_ai_exception_after_tool_dispatch_succeeds_returns_200_server_templated_cited_answer()
    {
        var aiProvider = new TestAiProvider
        {
            OnProbeLiveness = _ => Task.CompletedTask,
            OnComplete = (_, _, _) => throw new AiException("AI provider timeout or failure")
        };

        var tools = new TestPurchasingAnalystTools
        {
            OnDispatch = (toolName, period, ct) => Task.FromResult(new ToolExecutionResult(
                "call_1",
                toolName,
                "purchasing_overview",
                new[] { "metric=purchasing_overview.net; period=1405-08; value=12500000000" },
                "{}",
                true,
                Period: period))
        };

        var controller = CreateController(aiProvider, tools);
        var result = await controller.Chat(new PurchasingChatRequest("خالص خرید این ماه چقدر است؟"), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode ?? StatusCodes.Status200OK);

        var response = Assert.IsType<PurchasingChatResponse>(okResult.Value);
        Assert.False(response.Unavailable);
        Assert.NotEmpty(response.Facts);
        Assert.Contains("۱۲۵۰۰۰۰۰۰۰۰", response.Answer);
        Assert.Empty(response.Interpretation);
    }

    [Fact]
    public async Task When_liveness_probe_fails_returns_503_assistant_unavailable()
    {
        var aiProvider = new TestAiProvider
        {
            OnProbeLiveness = _ => throw new AiException("Liveness probe failed")
        };

        var tools = new TestPurchasingAnalystTools();
        var controller = CreateController(aiProvider, tools);
        var result = await controller.Chat(new PurchasingChatRequest("خالص خرید این ماه چقدر است؟"), CancellationToken.None);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objResult.StatusCode);

        var response = Assert.IsType<PurchasingChatUnavailableResponse>(objResult.Value);
        Assert.True(response.Unavailable);
        Assert.Equal("assistant-unavailable", response.Code);
        Assert.Empty(response.Facts);
    }

    [Fact]
    public async Task When_liveness_probe_times_out_returns_503_assistant_unavailable()
    {
        var aiProvider = new TestAiProvider
        {
            OnProbeLiveness = _ => throw new OperationCanceledException()
        };

        var tools = new TestPurchasingAnalystTools();
        var controller = CreateController(aiProvider, tools);
        var result = await controller.Chat(new PurchasingChatRequest("خالص خرید این ماه چقدر است؟"), CancellationToken.None);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objResult.StatusCode);

        var response = Assert.IsType<PurchasingChatUnavailableResponse>(objResult.Value);
        Assert.True(response.Unavailable);
        Assert.Equal("assistant-unavailable", response.Code);
        Assert.Empty(response.Facts);
    }

    [Fact]
    public async Task When_dispatch_fails_with_ai_exception_returns_503_assistant_unavailable()
    {
        var aiProvider = new TestAiProvider
        {
            OnProbeLiveness = _ => Task.CompletedTask
        };

        var tools = new TestPurchasingAnalystTools
        {
            OnDispatch = (_, _, _) => throw new AiException("Tool dispatch timeout or error")
        };

        var controller = CreateController(aiProvider, tools);
        var result = await controller.Chat(new PurchasingChatRequest("خالص خرید این ماه چقدر است؟"), CancellationToken.None);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objResult.StatusCode);

        var response = Assert.IsType<PurchasingChatUnavailableResponse>(objResult.Value);
        Assert.True(response.Unavailable);
        Assert.Equal("assistant-unavailable", response.Code);
        Assert.Empty(response.Facts);
    }

    [Fact]
    public async Task When_dispatch_times_out_returns_503_assistant_unavailable()
    {
        var aiProvider = new TestAiProvider
        {
            OnProbeLiveness = _ => Task.CompletedTask
        };

        var tools = new TestPurchasingAnalystTools
        {
            OnDispatch = (_, _, _) => throw new OperationCanceledException()
        };

        var controller = CreateController(aiProvider, tools);
        var result = await controller.Chat(new PurchasingChatRequest("خالص خرید این ماه چقدر است؟"), CancellationToken.None);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objResult.StatusCode);

        var response = Assert.IsType<PurchasingChatUnavailableResponse>(objResult.Value);
        Assert.True(response.Unavailable);
        Assert.Equal("assistant-unavailable", response.Code);
        Assert.Empty(response.Facts);
    }
}
