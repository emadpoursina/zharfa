namespace Zharfa.Api.Controllers;

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Zharfa.Api.Ai;
using Zharfa.Api.Audit;
using Zharfa.Api.Data;
using Zharfa.Api.Security;

public record AiAdminTestRequest(string? Target);

public record AiAdminCloudRequest(bool Enabled, string? OwnerApprovalNote);

[ApiController]
[Route("api/admin/ai")]
[Authorize(Policy = Permissions.Admin)]
public class AiAdminController(
    OllamaAiProvider localProvider,
    CloudAiProvider cloudProvider,
    AiProviderSettings settings,
    IAuditLogger auditLogger) : ControllerBase
{
    private const string AggregatesOnlyNotice =
        "در صورت فعال بودن ارائه‌دهنده ابری، تنها نتایج تجمیعی برای آن ارسال می‌شود.";

    [HttpGet("")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var snapshot = await settings.GetSnapshotAsync(ct);
        var localStatus = localProvider.GetStatus();
        var cloudStatus = cloudProvider.GetStatus();

        var answeringProvider = snapshot.CloudEnabled ? "cloud" : "local";

        return Ok(new
        {
            answeringProvider,
            aggregatesOnlyNotice = AggregatesOnlyNotice,
            local = new
            {
                name = localStatus.Name,
                endpoint = localStatus.Endpoint,
                model = localStatus.Model,
                configured = localStatus.Configured,
                isDefault = true
            },
            cloud = new
            {
                name = cloudStatus.Name,
                endpoint = cloudStatus.Endpoint,
                model = cloudStatus.Model,
                configured = cloudStatus.Configured,
                enabled = snapshot.CloudEnabled,
                ownerApprovalRecorded = !string.IsNullOrWhiteSpace(snapshot.ApprovalNote)
            }
        });
    }

    [HttpPost("test")]
    public async Task<IActionResult> Test(
        [FromBody] AiAdminTestRequest? request,
        CancellationToken ct)
    {
        var target = (request?.Target ?? string.Empty).Trim().ToLowerInvariant();
        if (target != "local" && target != "cloud")
        {
            return BadRequest(new { message = "هدف آزمون باید 'local' یا 'cloud' باشد." });
        }

        IAiProvider provider = target == "cloud" ? cloudProvider : localProvider;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var pingMessages = new List<AiMessage>
        {
            new("user", "سلام، لطفاً یک کلمه پاسخ دهید.")
        };

        bool ok = false;
        string message;

        try
        {
            if (!provider.IsConfigured)
            {
                ok = false;
                message = "ارائه‌دهنده هوش مصنوعی پیکربندی نشده است.";
            }
            else
            {
                await provider.CompleteAsync(pingMessages, Array.Empty<AiToolSpec>(), cts.Token);
                ok = true;
                message = "اتصال برقرار شد.";
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            ok = false;
            message = "پاسخ سرویس هوش مصنوعی به دلیل اتمام زمان مهلت دریافت نشد (timeout).";
        }
        catch (AiException ex)
        {
            ok = false;
            message = RedactionHelper.Scrub(ex.Message);
        }
        catch (Exception ex)
        {
            ok = false;
            message = RedactionHelper.Scrub(ex.Message);
        }

        await auditLogger.RecordAsync(
            new AuditEntry(
                AuditActions.SettingsAiProviderChanged,
                Target: target,
                Detail: AuditDetail.Of(
                    ("op", "test"),
                    ("target", target),
                    ("ok", ok ? "true" : "false"))),
            ct);

        return Ok(new { ok, message });
    }

    [HttpPost("cloud")]
    public async Task<IActionResult> SetCloud(
        [FromBody] AiAdminCloudRequest? request,
        CancellationToken ct)
    {
        if (request == null)
        {
            return BadRequest(new { message = "داده‌های ورودی نامعتبر است." });
        }

        if (request.Enabled && !AiProviderSettings.IsUsableApprovalNote(request.OwnerApprovalNote))
        {
            return BadRequest(new { message = "یادداشت تأیید مدیرعامل الزامی است." });
        }

        var adminUsername = User.Identity?.Name ?? "admin";
        await settings.SetCloudAsync(request.Enabled, request.OwnerApprovalNote, adminUsername, ct);

        var detailPairs = request.Enabled
            ? [("op", "enable"), ("noteRecorded", "true")]
            : new (string Key, string? Value)[] { ("op", "disable") };

        await auditLogger.RecordAsync(
            new AuditEntry(
                AuditActions.SettingsAiProviderChanged,
                Target: "cloud",
                Detail: AuditDetail.Of(detailPairs)),
            ct);

        return await GetStatus(ct);
    }
}
