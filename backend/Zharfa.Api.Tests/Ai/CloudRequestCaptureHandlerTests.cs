namespace Zharfa.Api.Tests.Ai;

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zharfa.Api.Ai;

public class CloudRequestCaptureHandlerTests
{
    private class DummyHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[]}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task When_in_development_and_path_set_writes_outbound_json_without_authorization_headers()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"zharfa_capture_{Guid.NewGuid():N}.json");
        try
        {
            var innerHandler = new DummyHandler();
            var captureHandler = new CloudRequestCaptureHandler(tempFile, isDevelopment: true)
            {
                InnerHandler = innerHandler
            };
            using var client = new HttpClient(captureHandler);

            var sampleJson = "{\"model\":\"qwen2:7b\",\"messages\":[{\"role\":\"user\",\"content\":\"گزارش خرید\"}],\"tools\":[{\"name\":\"purchasing_overview\",\"totals\":{\"net\":1000000},\"rankedNames\":[\"شرکت الف\"]}]}";
            using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:11434/v1/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "secret-key-12345");
            request.Headers.Add("X-Password", "super-secret");
            request.Content = new StringContent(sampleJson, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.True(File.Exists(tempFile));
            var capturedContent = await File.ReadAllTextAsync(tempFile);

            // Assert forbidden tokens are absent
            Assert.DoesNotContain("secret-key-12345", capturedContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Bearer", capturedContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Authorization", capturedContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("apiKey", capturedContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("super-secret", capturedContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"lines\":", capturedContent, StringComparison.OrdinalIgnoreCase);

            // Aggregates are kept
            Assert.Contains("purchasing_overview", capturedContent);
            Assert.Contains("1000000", capturedContent);
            Assert.Contains("شرکت الف", capturedContent);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task When_not_in_development_does_not_write()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"zharfa_capture_{Guid.NewGuid():N}.json");
        try
        {
            var innerHandler = new DummyHandler();
            var captureHandler = new CloudRequestCaptureHandler(tempFile, isDevelopment: false)
            {
                InnerHandler = innerHandler
            };
            using var client = new HttpClient(captureHandler);

            using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:11434/v1/chat/completions");
            request.Content = new StringContent("{\"model\":\"test\"}", Encoding.UTF8, "application/json");

            await client.SendAsync(request);

            Assert.False(File.Exists(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task When_path_is_empty_does_not_write()
    {
        var innerHandler = new DummyHandler();
        var captureHandler = new CloudRequestCaptureHandler(string.Empty, isDevelopment: true)
        {
            InnerHandler = innerHandler
        };
        using var client = new HttpClient(captureHandler);

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:11434/v1/chat/completions");
        request.Content = new StringContent("{\"model\":\"test\"}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task When_write_fails_logs_warning_and_does_not_abort_request()
    {
        // An impossible directory path that causes Directory.CreateDirectory or File.WriteAllTextAsync to fail
        var invalidPath = "/dev/null/impossible/dir/capture.json";
        var innerHandler = new DummyHandler();
        var captureHandler = new CloudRequestCaptureHandler(invalidPath, isDevelopment: true)
        {
            InnerHandler = innerHandler
        };
        using var client = new HttpClient(captureHandler);

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:11434/v1/chat/completions");
        request.Content = new StringContent("{\"model\":\"test\"}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
