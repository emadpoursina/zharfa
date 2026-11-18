namespace Zharfa.Api.Controllers;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Zharfa.Api.Ai;
using Zharfa.Api.Audit;
using Zharfa.Api.Data;
using Zharfa.Api.Platform;
using Zharfa.Api.Purchasing.Reports;
using Zharfa.Api.Security;

public record PurchasingChatRequest(string? Question);

public record PurchasingPeriodDto(string From, string To, string JalaliLabel, string? Month = null);

public record PurchasingFiltersDto(string? Vendor = null, string? Product = null, string? Category = null);

public record PurchasingChatResponse(
    string Answer,
    PurchasingPeriodDto Period,
    PurchasingFiltersDto Filters,
    string? View,
    IReadOnlyList<string> Facts,
    string Interpretation,
    bool Unavailable,
    string DataLabel,
    string? DemoNotice = null);

public record PurchasingChatUnavailableResponse(
    bool Unavailable,
    string Answer,
    string Message,
    PurchasingPeriodDto? Period,
    PurchasingFiltersDto? Filters,
    IReadOnlyList<string> Facts,
    string Interpretation,
    string? View,
    string DataLabel,
    string? DemoNotice = null,
    [property: JsonPropertyName("code")] string Code = "assistant-unavailable");

[ApiController]
[Route("api/ai")]
[Authorize(Policy = Permissions.AiQuery)]
public class AiController(
    IAiProvider aiProvider,
    PurchasingAnalystTools analystTools,
    AiQuestionGate questionGate,
    IAuditLogger auditLogger,
    DataModeOptions dataMode,
    ILogger<AiController> logger) : ControllerBase
{
    private const string PurchasingSystemPrompt =
        "شما تحلیل‌گر هوشمند خرید در شرکت پخش دارویی «ژرفا» هستید.\n" +
        "قوانین الزامی و قطعی شما:\n" +
        "۱. پاسخ را منحصراً به زبان فارسی روان، محترمانه و دقیق ارائه دهید.\n" +
        "۲. ارقام و حقایق را صرفاً از نتایج ابزارهای مجاز خرید استخراج کنید؛ هرگز رقمی از خود نسازید، تخمین نزنید یا حدس نزنید.\n" +
        "۳. همواره دوره زمانی (مثلاً ماه یا فصل جلالی)، فیلترها و نام نمای مرجع (view) را در پاسخ خود ذکر نمایید.\n" +
        "۴. حقایق قطعی استخراج‌شده از ابزارها را از تحلیل و تفسیر کسب‌وکاری تفکیک کنید. در متن تفسیر هیچ عدد یا رقمی که در حقایق نیامده اضافه نکنید.\n" +
        "۵. در صورتی که داده‌ای یافت نشد، منبع در دسترس نبود یا سوال خارج از قلمرو خرید بود، صریحاً پاسخ دهید: «داده در دسترس نیست».\n" +
        "۶. اگر برای یک دوره زمانی معتبر خریدی ثبت نشده بود، مبلغ آن صفر است (نه داده در دسترس نیست).\n" +
        "۷. فرمول‌های مالی را کاملاً تفکیک کنید: خرید، پرداخت و مانده حساب تأمین‌کننده سه مفهوم متفاوتند. هرگز تفاوت قیمت را به عنوان سود معرفی نکنید.\n" +
        "۸. از جداول، نمودارها یا کدهای ساختگی استفاده نکنید.\n" +
        "۹. قلمرو پاسخ‌گویی شما منحصراً ابزارهای تحلیل خرید است و هیچ‌گونه دسترسی به مانده حساب، اسناد پرداختنی، چک‌ها یا فروش و انبار ندارید.\n" +
        "۱۰. هرگز خالص خرید را به عنوان فروش داروخانه‌ها معرفی نکنید.";

    private const string DemoNoticeText =
        "این پاسخ فقط بر اساس داده‌های نمایشی (دمو) تولید شده و هیچ داده واقعی شرکت در آن استفاده نشده است.";

    private const string WaitMessage =
        "لطفاً صبر کنید تا پاسخ پرسش فعلی آماده شود.";

    private const string AssistantUnavailableAnswer =
        "پاسخی ارائه نشد؛ دستیار هوشمند در حال حاضر در دسترس نیست.";

    private IActionResult AssistantUnavailableResult(string message)
    {
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new PurchasingChatUnavailableResponse(
            Unavailable: true,
            Code: "assistant-unavailable",
            Answer: AssistantUnavailableAnswer,
            Message: message,
            Period: null,
            Filters: null,
            Facts: Array.Empty<string>(),
            Interpretation: string.Empty,
            View: null,
            DataLabel: dataMode.DataLabel,
            DemoNotice: dataMode.IsDemo ? DemoNoticeText : null));
    }

    // Phase 3 FR-018: Authentication (401) and Authorization (403 missing ai.query) are enforced
    // by framework middleware and AuditingAuthorizationResultHandler. AiController does not catch
    // or handle 401/403 directly. Audit failure must never change the HTTP outcome.
    [HttpPost("chat")]
    public async Task<IActionResult> Chat(
        [FromBody] PurchasingChatRequest? request,
        CancellationToken cancellationToken)
    {
        var rawQuestion = request?.Question?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawQuestion) || rawQuestion.Length > 2000)
        {
            return BadRequest(new { message = "متن سوال الزامی است و حداکثر می‌تواند ۲۰۰۰ کاراکتر باشد." });
        }

        var userId = User.UserId();
        if (userId.HasValue && !questionGate.TryEnter(userId.Value))
        {
            _ = RecordAuditSafeAsync(rawQuestion, "busy", Array.Empty<string>());
            return Conflict(new { message = WaitMessage, code = "ai-busy" });
        }

        try
        {
            using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budgetCts.CancelAfter(TimeSpan.FromSeconds(PurchasingAnalystTools.WHOLE_ANSWER_BUDGET_SECONDS));

            // 1. Answering provider configuration & liveness check (contracts/api.md §1)
            try
            {
                var isConfigured = aiProvider is ActiveAiProvider activeProvider
                    ? await activeProvider.IsConfiguredAsync(budgetCts.Token)
                    : aiProvider.IsConfigured;

                if (!isConfigured)
                {
                    logger.LogWarning("Purchasing AI provider is not configured");
                    _ = RecordAuditSafeAsync(rawQuestion, "provider", Array.Empty<string>());
                    return AssistantUnavailableResult("سرویس هوش مصنوعی پیکربندی نشده است.");
                }

                await aiProvider.ProbeLivenessAsync(budgetCts.Token);
            }
            catch (AiException ex)
            {
                logger.LogWarning("Purchasing AI liveness probe failed: {Message}", RedactionHelper.Scrub(ex.Message));
                _ = RecordAuditSafeAsync(rawQuestion, "provider", Array.Empty<string>());
                return AssistantUnavailableResult(RedactionHelper.Scrub(ex.Message));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Purchasing AI liveness probe timed out");
                _ = RecordAuditSafeAsync(rawQuestion, "provider", Array.Empty<string>());
                return AssistantUnavailableResult("پاسخ سرویس هوش مصنوعی به دلیل اتمام زمان مهلت دریافت نشد (timeout).");
            }
            catch (Exception ex)
            {
                logger.LogWarning("Purchasing AI liveness probe unexpected failure [{Type}]", ex.GetType().Name);
                _ = RecordAuditSafeAsync(rawQuestion, "provider", Array.Empty<string>());
                return AssistantUnavailableResult("خطا در برقراری ارتباط با سرویس هوش مصنوعی.");
            }

            // 2. Scope router gate (T087, research R21)
            var hasSuccessfulSync = await analystTools.HasSuccessfulSyncAsync(budgetCts.Token);
            var route = PurchasingQuestionRouter.Classify(rawQuestion, hasSuccessfulSync);

            if (route.Scope != "purchasing")
            {
                // Metric-unavailable: 200 OK, empty facts, view=null, «داده در دسترس نیست»
                // Tools are NEVER executed and CompleteAsync is NEVER called (Q13 safe).
                var defaultPeriod = analystTools.GetDefaultPeriod();
                var periodInfo = new PurchasingPeriodDto(
                    From: defaultPeriod.From.ToDateOnly().ToString("yyyy-MM-dd"),
                    To: defaultPeriod.To.ToDateOnly().ToString("yyyy-MM-dd"),
                    JalaliLabel: defaultPeriod.Label,
                    Month: defaultPeriod.Kind == JalaliPeriodKind.Month ? defaultPeriod.StartMonth.Key : null);
                var filtersInfo = new PurchasingFiltersDto();

                _ = RecordAuditSafeAsync(rawQuestion, "unavailable", Array.Empty<string>(), defaultPeriod);
                return Ok(PurchasingCitedAnswer.CreateUnavailable(periodInfo, filtersInfo, dataMode.DataLabel, dataMode.IsDemo ? DemoNoticeText : null));
            }

            // 3. Server-dispatch mapped tool (T089, research R22)
            var boundPeriod = analystTools.BindPeriod(route.PeriodRule);
            var toolName = route.Tool ?? "purchasing_overview";

            var periodDto = new PurchasingPeriodDto(
                From: boundPeriod.From.ToDateOnly().ToString("yyyy-MM-dd"),
                To: boundPeriod.To.ToDateOnly().ToString("yyyy-MM-dd"),
                JalaliLabel: boundPeriod.Label,
                Month: boundPeriod.Kind == JalaliPeriodKind.Month ? boundPeriod.StartMonth.Key : null);

            ToolExecutionResult toolResult;
            try
            {
                toolResult = await analystTools.DispatchAsync(toolName, boundPeriod, budgetCts.Token);
            }
            catch (AiException ex)
            {
                logger.LogWarning("Dispatched tool execution error: {Message}", RedactionHelper.Scrub(ex.Message));
                _ = RecordAuditSafeAsync(rawQuestion, "provider", new[] { toolName }, boundPeriod);
                return AssistantUnavailableResult(RedactionHelper.Scrub(ex.Message));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Dispatched tool execution timed out");
                _ = RecordAuditSafeAsync(rawQuestion, "provider", new[] { toolName }, boundPeriod);
                return AssistantUnavailableResult("پاسخ سرویس هوش مصنوعی به دلیل اتمام زمان مهلت دریافت نشد (timeout).");
            }

            var filtersDto = new PurchasingFiltersDto(
                Vendor: toolResult.Vendor,
                Product: toolResult.Product,
                Category: toolResult.Category);

            if (!toolResult.Success || toolResult.Facts == null || toolResult.Facts.Count == 0)
            {
                _ = RecordAuditSafeAsync(rawQuestion, "unavailable", new[] { toolName }, boundPeriod, toolResult.Vendor, toolResult.Product, toolResult.Category);
                return Ok(PurchasingCitedAnswer.CreateUnavailable(periodDto, filtersDto, dataMode.DataLabel, dataMode.IsDemo ? DemoNoticeText : null));
            }

            // 4. Single CompleteAsync with empty tool specs for phrasing (model gets no tool specs)
            string modelContent;
            try
            {
                var promptWithFacts =
                    PurchasingSystemPrompt +
                    "\n\nحقایق استخراج‌شده از سامانه خرید که باید منحصراً مبنای پاسخ قرار گیرند:\n" +
                    string.Join("\n", toolResult.Facts);

                var phrasingMessages = new List<AiMessage>
                {
                    new("system", promptWithFacts),
                    new("user", rawQuestion)
                };

                var turn = await aiProvider.CompleteAsync(phrasingMessages, Array.Empty<AiToolSpec>(), budgetCts.Token);
                modelContent = turn.Content ?? string.Empty;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Phrasing CompleteAsync timed out; falling back to server-templated facts");
                modelContent = string.Empty;
            }
            catch (AiException ex)
            {
                logger.LogWarning("Phrasing CompleteAsync error: {Message}; falling back to server-templated facts", RedactionHelper.Scrub(ex.Message));
                modelContent = string.Empty;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Phrasing CompleteAsync unexpected failure [{Type}]; falling back to server-templated facts", ex.GetType().Name);
                modelContent = string.Empty;
            }

            // 5. Answer resolution via PurchasingCitedAnswer (facts enforcement)
            var (answer, interpretation) = PurchasingCitedAnswer.ResolveAnswer(
                toolResult.Facts,
                periodDto,
                filtersDto,
                toolResult.View,
                modelContent);

            _ = RecordAuditSafeAsync(rawQuestion, "ok", new[] { toolName }, boundPeriod, toolResult.Vendor, toolResult.Product, toolResult.Category);

            return Ok(new PurchasingChatResponse(
                Answer: answer,
                Period: periodDto,
                Filters: filtersDto,
                View: toolResult.View,
                Facts: toolResult.Facts,
                Interpretation: interpretation,
                Unavailable: false,
                DataLabel: dataMode.DataLabel,
                DemoNotice: dataMode.IsDemo ? DemoNoticeText : null));
        }
        finally
        {
            if (userId.HasValue)
            {
                questionGate.Release(userId.Value);
            }
        }
    }

    private async Task RecordAuditSafeAsync(
        string question,
        string outcome,
        IReadOnlyList<string> tools,
        JalaliPeriod? period = null,
        string? vendor = null,
        string? product = null,
        string? category = null)
    {
        try
        {
            var targetText = question.Length > 200 ? question[..200] : question;
            var periodLabel = period?.Kind == JalaliPeriodKind.Month ? period.StartMonth.Key : period?.Label;
            var fromDate = period != null ? period.From.ToDateOnly().ToString("yyyy-MM-dd") : null;
            var toDate = period != null ? period.To.ToDateOnly().ToString("yyyy-MM-dd") : null;

            var detailPairs = new List<(string Key, string? Value)>
            {
                ("period", periodLabel),
                ("from", fromDate),
                ("to", toDate),
                ("vendor", vendor),
                ("product", product),
                ("category", category),
                ("tools", tools.Count > 0 ? string.Join(",", tools.Distinct(StringComparer.Ordinal)) : null),
                ("outcome", outcome)
            };

            await auditLogger.RecordAsync(
                new AuditEntry(
                    AuditActions.Question,
                    Target: targetText,
                    Detail: AuditDetail.Of(detailPairs.ToArray())),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to record AI question audit entry: {Message}", ex.Message);
        }
    }
}
