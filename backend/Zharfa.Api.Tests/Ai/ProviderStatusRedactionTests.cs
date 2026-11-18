namespace Zharfa.Api.Tests.Ai;

using System;
using System.Text.Json;
using Xunit;
using Zharfa.Api.Ai;
using Zharfa.Api.Data;

public class ProviderStatusRedactionTests
{
    [Theory]
    [InlineData("http://user:pass@host.local:11434/v1?token=secret123", "http://host.local:11434/v1")]
    [InlineData("https://admin:p@ss@api.openai.com/v1?key=abc", "https://api.openai.com/v1")]
    [InlineData("http://user:p@ss:w0rd!#$@host.local:11434/v1?token=secret123#frag", "http://host.local:11434/v1")]
    [InlineData("https://admin:p%40ss@api.openai.com:443", "https://api.openai.com:443")]
    [InlineData("http://user:pass@127.0.0.1:11434", "http://127.0.0.1:11434")]
    [InlineData("http://user:p@ss@[::1]:11434/v1", "http://[::1]:11434/v1")]
    [InlineData("http://localhost:11434/v1", "http://localhost:11434/v1")]
    public void Endpoint_with_userinfo_and_query_is_redacted(string input, string expected)
    {
        var redacted = OpenAiCompatibleChatClient.RedactEndpoint(input);
        Assert.Equal(expected, redacted);
        Assert.DoesNotContain("secret", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pass", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", redacted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialized_status_DTO_never_contains_key_properties()
    {
        var status = new ProviderStatus(
            Name: "ollama",
            Endpoint: "http://ollama:11434/v1",
            Model: "qwen2.5:14b",
            Configured: true);

        var json = JsonSerializer.Serialize(status);

        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("key", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Connection failed to https://api.openai.com/v1 with Bearer sk-proj-1234567890abcdef")]
    [InlineData("401 Unauthorized: Authorization: Bearer sk-proj-1234567890abcdef")]
    [InlineData("Request failed with Bearer: sk-proj-1234567890abcdef")]
    [InlineData("Request failed with bearer=sk-proj-1234567890abcdef")]
    [InlineData("Request failed with Bearer token sk-proj-1234567890abcdef")]
    public void Connection_test_failure_message_is_scrubbed_by_RedactionHelper(string rawFailureMessage)
    {
        var scrubbed = RedactionHelper.Scrub(rawFailureMessage);

        Assert.DoesNotContain("sk-proj-1234567890abcdef", scrubbed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[REDACTED]", scrubbed);
    }
}
