namespace Zharfa.Api.Ai;

using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zharfa.Api.Platform;

/// <summary>
/// Development-only delegating handler for capturing outbound OpenAI-compatible chat-completions JSON (research R25).
/// Writes request payload to the path configured in AiProvider__CapturePath without Authorization or secrets.
/// </summary>
public class CloudRequestCaptureHandler : DelegatingHandler
{
    private readonly string? _capturePath;
    private readonly bool _isDevelopment;
    private readonly ILogger<CloudRequestCaptureHandler>? _logger;

    public CloudRequestCaptureHandler(string? capturePath, bool isDevelopment, ILogger<CloudRequestCaptureHandler>? logger = null)
    {
        _capturePath = capturePath?.Trim();
        _isDevelopment = isDevelopment;
        _logger = logger;
    }

    public CloudRequestCaptureHandler(IConfiguration configuration, IHostEnvironment environment, ILogger<CloudRequestCaptureHandler>? logger = null)
        : this(
            ConfigurationKeys.Get(configuration, "AiProvider__CapturePath")
            ?? configuration["AiProvider:CapturePath"]
            ?? configuration["AiProvider__CapturePath"],
            environment.IsDevelopment(),
            logger)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_isDevelopment && !string.IsNullOrWhiteSpace(_capturePath) && request.Content != null)
        {
            try
            {
                var contentString = await request.Content.ReadAsStringAsync(cancellationToken);
                var dir = Path.GetDirectoryName(_capturePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                await File.WriteAllTextAsync(_capturePath, contentString, cancellationToken);
            }
            catch (Exception ex)
            {
                // Capture file write failure must not abort the HTTP request
                _logger?.LogWarning(ex, "Failed to capture outbound cloud AI request payload to {CapturePath}", _capturePath);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
