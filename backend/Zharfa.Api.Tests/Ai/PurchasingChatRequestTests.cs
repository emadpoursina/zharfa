namespace Zharfa.Api.Tests.Ai;

using System;
using System.Reflection;
using Xunit;
using Zharfa.Api.Controllers;

public class PurchasingChatRequestTests
{
    [Fact]
    public void PurchasingChatRequest_has_no_From_To_or_CategoryId_properties()
    {
        var properties = typeof(PurchasingChatRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propNames = Array.ConvertAll(properties, p => p.Name);

        Assert.Single(propNames);
        Assert.Equal("Question", propNames[0]);

        Assert.DoesNotContain("From", propNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("To", propNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CategoryId", propNames, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void Empty_or_whitespace_question_is_refused(string? question, bool expectedValid)
    {
        var raw = question?.Trim() ?? string.Empty;
        var isValid = !string.IsNullOrWhiteSpace(raw) && raw.Length <= 2000;
        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public void Question_length_limits_enforced()
    {
        var q1 = new string('ا', 1);
        var q2000 = new string('ا', 2000);
        var q2001 = new string('ا', 2001);

        Assert.True(q1.Length >= 1 && q1.Length <= 2000);
        Assert.True(q2000.Length >= 1 && q2000.Length <= 2000);
        Assert.False(q2001.Length >= 1 && q2001.Length <= 2000);
    }
}
