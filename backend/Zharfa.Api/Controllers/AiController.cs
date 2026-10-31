namespace Zharfa.Api.Controllers;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Zharfa.Api.Ai;
using Zharfa.Api.Analytics;
using Zharfa.Api.Data;

public record AiChatRequest(
    string? Question,
    string? From,
    string? To,
    long? CategoryId);

public record AiChatResponse(
    bool DemoData,
    string Answer,
    PeriodDto Period,
    FiltersDto Filters,
    string? View,
    IReadOnlyList<string> Facts,
    string Interpretation,
    bool Unavailable);

public record AiChatUnavailableResponse(
    bool DemoData,
    bool Unavailable,
    string Answer,
    string Message,
    PeriodDto Period,
    FiltersDto Filters,
    IReadOnlyList<string> Facts,
    string Interpretation,
    string? View);

[ApiController]
[Route("api/ai")]
[Authorize(Roles = "manager,admin")]
public class AiController : ControllerBase
{
    private const string SystemPrompt =
        "شما تحلیل‌گر ارشد کسب‌وکار در شرکت پخش دارویی «ژرفا» هستید.\n" +
        "قوانین الزامی شما:\n" +
        "۱. پاسخ را فقط و فقط به زبان فارسی روان ارائه دهید.\n" +
        "۲. صرفاً از داده‌ها و ارقام بازگردانده شده توسط ابزارهای تاییدشده استفاده کنید؛ هرگز رقمی از خود نسازید یا حدس نزنید.\n" +
        "۳. حقایق قطعی استخراج‌شده از داده‌ها را از تحلیل و تفسیر کسب‌وکاری تفکیک کنید.\n" +
        "۴. همواره بازه زمانی، فیلترها و نام نمای مرجع (view) را در پاسخ خود ذکر نمایید.\n" +
        "۵. در صورتی که داده‌ای برای سوال کاربر وجود نداشت یا ابزاری داده‌ای نیافت، صریحاً اعلام کنید: «داده در دسترس نیست».\n" +
        "۶. قلمرو پاسخ‌گویی شما تنها به سه مجموعه شاخص فروش (sales_overview)، مطالبات و دریافتی‌ها (receivables)، و هشدارهای موجودی انبار (inventory_alerts) محدود است.";

    private readonly IAiProvider _aiProvider;
    private readonly AnalystTools _analystTools;
    private readonly ILogger<AiController> _logger;

    public AiController(
        IAiProvider aiProvider,
        AnalystTools analystTools,
        ILogger<AiController> logger)
    {
        _aiProvider = aiProvider;
        _analystTools = analystTools;
        _logger = logger;
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat(
        [FromBody] AiChatRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Question) || request.Question.Trim().Length > 2000)
        {
            return BadRequest(new { message = "متن سوال الزامی است و حداکثر می‌تواند ۲۰۰۰ کاراکتر باشد." });
        }

        if (request.CategoryId.HasValue && request.CategoryId.Value <= 0)
        {
            return BadRequest(new { message = "شناسه دسته‌بندی باید عددی مثبت باشد." });
        }

        DateRange range;
        try
        {
            range = PeriodFilters.Resolve(request.From, request.To);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var period = PeriodDto.Create(PeriodFilters.BuildPeriodInfo(range));
        var filters = new FiltersDto(request.CategoryId);

        if (!_aiProvider.IsConfigured)
        {
            _logger.LogWarning("AI provider is not configured");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new AiChatUnavailableResponse(
                DemoData: true,
                Unavailable: true,
                Answer: "پاسخی ارائه نشد؛ سرویس هوش مصنوعی در دسترس نیست.",
                Message: "سرویس هوش مصنوعی پیکربندی نشده است.",
                Period: period,
                Filters: filters,
                Facts: Array.Empty<string>(),
                Interpretation: string.Empty,
                View: null));
        }

        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budgetCts.CancelAfter(TimeSpan.FromSeconds(AnalystTools.WHOLE_TOOL_PHASE_BUDGET_SECONDS));

        var contextInfo = $"بازه زمانی پیش‌فرض تحلیل: {period.JalaliLabel} (از {period.From} تا {period.To}). " +
            (request.CategoryId.HasValue ? $"شناسه دسته‌بندی فیلتر شده: {request.CategoryId.Value}." : "بدون فیلتر دسته‌بندی خاص.");

        var messages = new List<AiMessage>
        {
            new("system", SystemPrompt),
            new("user", $"سوال کاربر: {request.Question.Trim()}\nاطلاعات زمینه: {contextInfo}")
        };

        var stopwatch = Stopwatch.StartNew();
        var executedViews = new List<string>();
        var executedFacts = new List<string>();
        var executedToolNames = new List<string>();
        int iterations = 0;
        string? finalContent = null;
        var toolSpecs = AnalystTools.GetToolSpecs();

        try
        {
            while (iterations < AnalystTools.MAX_TOOL_CALLS_PER_CHAT)
            {
                iterations++;

                // If tool budget reached, do not provide tool specs so model completes text
                var activeSpecs = executedToolNames.Count >= AnalystTools.MAX_TOOL_CALLS_PER_CHAT
                    ? Array.Empty<AiToolSpec>()
                    : toolSpecs;

                var turn = await _aiProvider.CompleteAsync(messages, activeSpecs, budgetCts.Token);
                finalContent = turn.Content;

                if (turn.ToolCalls == null || turn.ToolCalls.Count == 0)
                {
                    break;
                }

                messages.Add(new AiMessage("assistant", turn.Content, ToolCalls: turn.ToolCalls));

                foreach (var call in turn.ToolCalls)
                {
                    if (executedToolNames.Count >= AnalystTools.MAX_TOOL_CALLS_PER_CHAT)
                    {
                        break;
                    }

                    executedToolNames.Add(call.Name);
                    var result = await _analystTools.ExecuteAsync(call, budgetCts.Token);

                    if (result.Success && !string.IsNullOrEmpty(result.View))
                    {
                        executedViews.Add(result.View);
                        foreach (var fact in result.Facts)
                        {
                            executedFacts.Add(fact);
                        }
                    }

                    messages.Add(new AiMessage("tool", result.ContentJson, ToolCallId: call.Id));
                }
            }
        }
        catch (NotSupportedException)
        {
            _logger.LogWarning("AI provider threw NotSupportedException (Ollama seam)");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new AiChatUnavailableResponse(
                DemoData: true,
                Unavailable: true,
                Answer: "پاسخی ارائه نشد؛ سرویس هوش مصنوعی در دسترس نیست.",
                Message: "ارائه‌دهنده انتخابی هوش مصنوعی در این نسخه فعال نیست.",
                Period: period,
                Filters: filters,
                Facts: Array.Empty<string>(),
                Interpretation: string.Empty,
                View: null));
        }
        catch (AiException ex)
        {
            _logger.LogWarning("AI provider exception: {Message}", RedactionHelper.Scrub(ex.Message));
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new AiChatUnavailableResponse(
                DemoData: true,
                Unavailable: true,
                Answer: "پاسخی ارائه نشد؛ سرویس هوش مصنوعی در دسترس نیست.",
                Message: RedactionHelper.Scrub(ex.Message),
                Period: period,
                Filters: filters,
                Facts: Array.Empty<string>(),
                Interpretation: string.Empty,
                View: null));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("AI execution phase timed out");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new AiChatUnavailableResponse(
                DemoData: true,
                Unavailable: true,
                Answer: "پاسخی ارائه نشد؛ سرویس هوش مصنوعی در دسترس نیست.",
                Message: "پاسخ سرویس هوش مصنوعی به دلیل اتمام زمان مهلت دریافت نشد (timeout).",
                Period: period,
                Filters: filters,
                Facts: Array.Empty<string>(),
                Interpretation: string.Empty,
                View: null));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("AI execution unexpected failure [{ExceptionType}]", ex.GetType().Name);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new AiChatUnavailableResponse(
                DemoData: true,
                Unavailable: true,
                Answer: "پاسخی ارائه نشد؛ سرویس هوش مصنوعی در دسترس نیست.",
                Message: "خطا در ارتباط با سرویس هوش مصنوعی.",
                Period: period,
                Filters: filters,
                Facts: Array.Empty<string>(),
                Interpretation: string.Empty,
                View: null));
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "AI chat completed: iterations={Iterations}, tools={Tools}, durationMs={DurationMs}",
            iterations,
            string.Join(",", executedToolNames),
            stopwatch.ElapsedMilliseconds);

        bool anyToolExecuted = executedViews.Count > 0;
        if (!anyToolExecuted)
        {
            return Ok(new AiChatResponse(
                DemoData: true,
                Answer: "داده‌ای در دسترس نیست؛ پاسخ بدون مرجع داده ارائه نشد.",
                Period: period,
                Filters: filters,
                View: null,
                Facts: Array.Empty<string>(),
                Interpretation: string.Empty,
                Unavailable: true));
        }

        var distinctViews = executedViews.Distinct(StringComparer.Ordinal).ToList();
        string viewSummary = string.Join(", ", distinctViews);

        string interpretation = finalContent?.Trim() ?? string.Empty;
        string answer = !string.IsNullOrWhiteSpace(finalContent)
            ? finalContent.Trim()
            : string.Join("\n", executedFacts);

        return Ok(new AiChatResponse(
            DemoData: true,
            Answer: answer,
            Period: period,
            Filters: filters,
            View: viewSummary,
            Facts: executedFacts,
            Interpretation: interpretation,
            Unavailable: false));
    }
}
