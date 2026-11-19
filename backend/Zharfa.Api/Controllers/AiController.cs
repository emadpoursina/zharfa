namespace Zharfa.Api.Controllers;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zharfa.Api.Ai;
using Zharfa.Api.Analytics;
using Zharfa.Api.Audit;
using Zharfa.Api.Data;
using Zharfa.Api.Mabna.Sync;
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
    string? DemoNotice = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] VisualDto? Visual = null);

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
    ILogger<AiController> logger,
    ExcelExportService? exportService = null,
    IOptions<SyncOptions>? syncOptions = null,
    TimeProvider? clock = null,
    VendorFinancialAnalystTools? financialTools = null) : ControllerBase
{
    private const string FinancialSystemPrompt =
        "شما تحلیل‌گر هوشمند مالی تأمین‌کنندگان در شرکت پخش دارویی «ژرفا» هستید.\n" +
        "قوانین الزامی و قطعی شما:\n" +
        "۱. پاسخ را منحصراً به زبان فارسی روان، محترمانه و دقیق ارائه دهید.\n" +
        "۲. ارقام و حقایق را صرفاً از نتایج ابزارهای مجاز مالی استخراج کنید؛ هرگز رقمی از خود نسازید، تخمین نزنید یا حدس نزنید.\n" +
        "۳. همواره دوره زمانی، فیلترها و نام نمای مرجع را در پاسخ خود ذکر نمایید.\n" +
        "۴. حقایق قطعی استخراج‌شده از ابزارها را از تحلیل و تفسیر تفکیک کنید. در متن تفسیر هیچ عددی که در حقایق نیامده اضافه نکنید.\n" +
        "۵. در صورتی که داده‌ای یافت نشد یا منبع در دسترس نبود، صریحاً پاسخ دهید: «داده در دسترس نیست».\n" +
        "۶. چهار مفهوم مالی را کاملاً تفکیک کنید: خرید خالص، پرداخت‌ها، پرداخت‌های معوق و مانده تأمین‌کننده. هرگز تفاوت خرید و پرداخت را سود یا حاشیه سود نام‌گذاری نکنید؛ تنها عنوان مجاز «خرید منهای پرداخت» است.\n" +
        "۷. مانده تأمین‌کننده دارای علامت است (مثبت بدهی، منفی بستانکاری) و هرگز سود نامیده نمی‌شود.";

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

            // 2. Scope router gate (T087, research R21, Phase 6 US7)
            var hasSuccessfulSync = await analystTools.HasSuccessfulSyncAsync(budgetCts.Token);
            var hasFinancials = User.HasPermission(Permissions.VendorFinancials);
            var route = PurchasingQuestionRouter.Classify(rawQuestion, hasSuccessfulSync, hasFinancials);

            var isFinancial = route.Scope == "vendor-financials" && route.Tool != null && financialTools != null;

            if (route.Scope != "purchasing" && !isFinancial)
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

            // 3. Server-dispatch mapped tool (T089, research R22, T070, T071)
            var boundPeriod = isFinancial ? financialTools!.BindPeriod(route.PeriodRule) : analystTools.BindPeriod(route.PeriodRule);
            var toolName = route.Tool ?? (isFinancial ? "vendor_financials_list" : "purchasing_overview");

            var periodDto = new PurchasingPeriodDto(
                From: boundPeriod.From.ToDateOnly().ToString("yyyy-MM-dd"),
                To: boundPeriod.To.ToDateOnly().ToString("yyyy-MM-dd"),
                JalaliLabel: boundPeriod.Label,
                Month: boundPeriod.Kind == JalaliPeriodKind.Month ? boundPeriod.StartMonth.Key : null);

            JsonElement? financialArgs = null;
            if (isFinancial)
            {
                var matchedVendor = await financialTools!.FindVendorInTextAsync(rawQuestion, budgetCts.Token);
                if (matchedVendor != null)
                {
                    financialArgs = JsonSerializer.SerializeToElement(new
                    {
                        vendor = matchedVendor.Name,
                        vendorKey = matchedVendor.Key
                    });
                }
            }

            ToolExecutionResult toolResult;
            try
            {
                toolResult = isFinancial
                    ? await financialTools!.DispatchAsync(toolName, boundPeriod, budgetCts.Token, financialArgs)
                    : await analystTools.DispatchAsync(toolName, boundPeriod, budgetCts.Token);
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
                var systemPrompt = isFinancial ? FinancialSystemPrompt : PurchasingSystemPrompt;
                var sourceName = isFinancial ? "سامانه مالی تأمین‌کنندگان" : "سامانه خرید";
                var promptWithFacts =
                    systemPrompt +
                    $"\n\nحقایق استخراج‌شده از {sourceName} که باید منحصراً مبنای پاسخ قرار گیرند:\n" +
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

            var normQuestion = PurchasingQuestionRouter.Normalize(rawQuestion);
            VisualDto? visual = null;
            if (toolResult.Success && VisualIntent.IsListVisual(normQuestion, toolName))
            {
                visual = VisualBuilder.Build(normQuestion, toolName, boundPeriod, toolResult, dataMode.DataLabel);
            }

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
                DemoNotice: dataMode.IsDemo ? DemoNoticeText : null,
                Visual: visual));
        }
        finally
        {
            if (userId.HasValue)
            {
                questionGate.Release(userId.Value);
            }
        }
    }

    [Authorize(Policy = Permissions.Export)]
    [HttpPost("visual/export")]
    public async Task<IActionResult> VisualExport([FromBody] VisualSaveSpec spec, CancellationToken ct)
    {
        if (spec.Generated == null || !PurchasingAnalystTools.AllowlistedToolNames.Contains(spec.Generated.Tool))
        {
            return BadRequest(new { message = "مشخصات ابزار گزارش نامعتبر است." });
        }

        var zone = SyncOptions.ResolveTimeZone(syncOptions?.Value.TimeZone ?? "Asia/Tehran");
        var nowLocal = TimeZoneInfo.ConvertTime((clock ?? TimeProvider.System).GetUtcNow(), zone).DateTime;
        var today = JalaliDate.FromDateTime(nowLocal);

        JalaliPeriod period;
        if (spec.Period.Kind == "custom" && !string.IsNullOrWhiteSpace(spec.Period.From) && !string.IsNullOrWhiteSpace(spec.Period.To))
        {
            if (!JalaliPeriod.TryParse("custom", null, spec.Period.From, spec.Period.To, today, out var parsed, out _))
            {
                return BadRequest(new { message = "بازه زمانی نامعتبر است." });
            }
            period = parsed!;
        }
        else
        {
            string? value = spec.Period.Kind switch
            {
                "quarter" => spec.Period.Quarter,
                "year" => spec.Period.Year,
                _ => spec.Period.Month
            };
            if (!JalaliPeriod.TryParse(spec.Period.Kind, value, null, null, today, out var parsed, out _))
            {
                return BadRequest(new { message = "بازه زمانی نامعتبر است." });
            }
            period = parsed!;
        }

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (spec.Filters != null)
        {
            foreach (var (k, v) in spec.Filters)
            {
                if (v != null) dict[k] = v;
            }
        }
        if (spec.Generated != null)
        {
            dict["limit"] = spec.Generated.Limit;
            if (!string.IsNullOrEmpty(spec.Generated.Sort)) dict["sort"] = spec.Generated.Sort;
            if (!string.IsNullOrEmpty(spec.Generated.Dir)) dict["dir"] = spec.Generated.Dir;
        }

        using var argsDoc = JsonDocument.Parse(JsonSerializer.Serialize(dict));
        var toolResult = await analystTools.DispatchAsync(spec.Generated!.Tool, period, ct, argsDoc.RootElement);
        if (!toolResult.Success)
        {
            return BadRequest(new { message = "جدولی برای خروجی وجود ندارد." });
        }

        var visual = VisualBuilder.Build(null, spec.Generated.Tool, period, toolResult, dataMode.DataLabel);
        if (visual == null || visual.Table.Columns.Count == 0)
        {
            return BadRequest(new { message = "جدولی برای خروجی وجود ندارد." });
        }

        var nowJalali = JalaliDate.ToPersianDigits($"{today.Year:D4}/{today.Month:D2}/{today.Day:D2} {nowLocal.Hour:D2}:{nowLocal.Minute:D2}");

        var headers = visual.Table.Columns.Select(c => c.Header).ToList();
        var rows = visual.Table.Rows
            .Select(r => (IReadOnlyList<string>)visual.Table.Columns.Select(c => r.GetValueOrDefault(c.Id) ?? "").ToList())
            .ToList();

        var exportTable = new ExportTable(
            ReportName: "گزارش تحلیلی دستیار هوشمند",
            PeriodLabel: visual.Captions.Period,
            FiltersLabel: visual.Captions.Filters,
            ExportedAtJalali: nowJalali,
            LastSyncJalali: null,
            DataLabel: dataMode.DataLabel,
            DataQualityText: visual.DataQuality?.AffectedRecords > 0 ? $"توجه کیفیت داده: {visual.DataQuality.AffectedRecords} رکورد پرچم‌دار در این بازه وجود دارد." : null,
            Headers: headers,
            Rows: rows,
            TotalRows: toolResult.TotalRows,
            Limit: 10000);

        var exporter = exportService ?? new ExcelExportService();
        var bytes = exporter.BuildWorkbook(exportTable);
        var filename = exporter.BuildFilename(exportTable.ReportName, today);

        _ = auditLogger.RecordAsync(
            new AuditEntry(AuditActions.ReportExported, $"report:{ReportKeys.Generated}", AuditDetail.Of(("period", period.Label))),
            ct);

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
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
