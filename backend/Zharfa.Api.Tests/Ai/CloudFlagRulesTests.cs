namespace Zharfa.Api.Tests.Ai;

using Xunit;
using Zharfa.Api.Ai;

public class CloudFlagRulesTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("yes", false)]
    public void ParseEnabled_returns_true_only_for_exact_boolean_true(string? value, bool expected)
    {
        Assert.Equal(expected, AiProviderSettings.ParseEnabled(value));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("\t\r\n", false)]
    [InlineData("تأیید مدیرعامل برای فعال‌سازی مدل ابری", true)]
    [InlineData("   تأیید با فاصله در ابتدا و انتها   ", true)]
    [InlineData("Approved by CEO for aggregated purchasing queries", true)]
    public void IsUsableApprovalNote_requires_non_whitespace_trimmed_text(string? note, bool expected)
    {
        Assert.Equal(expected, AiProviderSettings.IsUsableApprovalNote(note));
    }
}
