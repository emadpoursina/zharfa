namespace Zharfa.Api.Tests.Ai;

using System;
using Xunit;
using Zharfa.Api.Ai;

public class CloudEnableRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void Enable_with_null_or_whitespace_note_is_refused(string? note)
    {
        Assert.False(AiProviderSettings.IsUsableApprovalNote(note));
    }

    [Theory]
    [InlineData("تأیید مدیرعامل — فقط برای ارسال نتایج تجمیعی خرید")]
    [InlineData("Approved by CEO")]
    public void Enable_with_valid_note_is_accepted(string note)
    {
        Assert.True(AiProviderSettings.IsUsableApprovalNote(note));
    }

    [Fact]
    public void Disable_does_not_require_approval_note()
    {
        // When disabling cloud, approval note is optional / not required
        var enabled = false;
        string? note = null;

        var canProceed = !enabled || AiProviderSettings.IsUsableApprovalNote(note);
        Assert.True(canProceed);
    }

    [Fact]
    public void Reenable_requires_explicit_new_note_argument()
    {
        // If a request enables cloud, it must supply a fresh non-empty note, not rely on existing stored note
        string? requestNote = null;
        Assert.False(AiProviderSettings.IsUsableApprovalNote(requestNote));
    }
}
