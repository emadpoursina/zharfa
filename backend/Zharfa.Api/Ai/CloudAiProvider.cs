namespace Zharfa.Api.Ai;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Cloud AI provider implementation using OpenAI-compatible chat completions protocol.
/// Configured via server environment only. Never exposes secrets or API keys.
/// </summary>
public class CloudAiProvider : IAiProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CloudAiProvider> _logger;
    private readonly string _name;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly string? _apiKey;

    public CloudAiProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<CloudAiProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _name = configuration["AiProvider:Name"] ?? configuration["AiProvider__Name"] ?? string.Empty;
        _endpoint = configuration["AiProvider:Endpoint"] ?? configuration["AiProvider__Endpoint"] ?? string.Empty;
        _model = configuration["AiProvider:Model"] ?? configuration["AiProvider__Model"] ?? string.Empty;
        _apiKey = configuration["AiProvider:ApiKey"] ?? configuration["AiProvider__ApiKey"];
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_name) &&
        !string.IsNullOrWhiteSpace(_endpoint) &&
        !string.IsNullOrWhiteSpace(_model);

    public ProviderStatus GetStatus() =>
        new ProviderStatus(_name, _endpoint, _model, IsConfigured);

    /// <summary>
    /// Resolves the chat completions endpoint from configured base.
    /// AiProvider__Endpoint is the API base INCLUDING any /v1 segment (e.g. https://api.openai.com/v1).
    /// Endpoint rule: trim trailing '/'; if it already ends with "chat/completions" use it as-is, else append "/chat/completions".
    /// </summary>
    public static string ResolveEndpoint(string endpoint)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        if (trimmed.EndsWith("chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }
        return trimmed + "/chat/completions";
    }

    public async Task<AiTurn> CompleteAsync(
        IReadOnlyList<AiMessage> messages,
        IReadOnlyList<AiToolSpec> tools,
        CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new AiException("سرویس هوش مصنوعی پیکربندی نشده است.");
        }

        var client = _httpClientFactory.CreateClient("ai");
        var resolvedUrl = ResolveEndpoint(_endpoint);

        using var request = new HttpRequestMessage(HttpMethod.Post, resolvedUrl);

        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey.Trim());
        }

        var wireMessages = new List<object>();
        foreach (var msg in messages)
        {
            if (string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
                msg.ToolCalls != null && msg.ToolCalls.Count > 0)
            {
                var wireToolCalls = new List<object>();
                foreach (var tc in msg.ToolCalls)
                {
                    wireToolCalls.Add(new
                    {
                        id = tc.Id,
                        type = "function",
                        function = new
                        {
                            name = tc.Name,
                            arguments = tc.ArgumentsJson
                        }
                    });
                }

                wireMessages.Add(new
                {
                    role = "assistant",
                    content = msg.Content,
                    tool_calls = wireToolCalls
                });
            }
            else if (string.Equals(msg.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                wireMessages.Add(new
                {
                    role = "tool",
                    tool_call_id = msg.ToolCallId ?? string.Empty,
                    content = msg.Content ?? string.Empty
                });
            }
            else
            {
                wireMessages.Add(new
                {
                    role = msg.Role,
                    content = msg.Content ?? string.Empty
                });
            }
        }

        var payloadDict = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["messages"] = wireMessages,
            ["temperature"] = 0,
            ["max_tokens"] = 800
        };

        if (tools != null && tools.Count > 0)
        {
            var wireTools = new List<object>();
            foreach (var t in tools)
            {
                wireTools.Add(new
                {
                    type = "function",
                    function = new
                    {
                        name = t.Name,
                        description = t.Description,
                        parameters = t.Parameters
                    }
                });
            }

            payloadDict["tools"] = wireTools;
            payloadDict["tool_choice"] = "auto";
        }

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payloadDict);
        request.Content = new ByteArrayContent(jsonBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("AI provider request timed out");
            throw new AiException("پاسخ سرویس هوش مصنوعی به دلیل اتمام زمان مهلت دریافت نشد (timeout).");
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("AI provider network/transport error");
            throw new AiException("خطا در برقراری ارتباط شبکه با سرویس هوش مصنوعی (network).");
        }
        catch (Exception ex) when (ex is not AiException)
        {
            _logger.LogWarning("AI provider transport failure [{ExceptionType}]", ex.GetType().Name);
            throw new AiException("خطا در برقراری ارتباط با سرویس هوش مصنوعی (network).", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI provider returned HTTP status {StatusCode}", (int)response.StatusCode);
                throw new AiException($"خطای سرویس هوش مصنوعی (کد وضعیت: {(int)response.StatusCode})");
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = doc.RootElement;

                if (!root.TryGetProperty("choices", out var choicesElem) ||
                    choicesElem.ValueKind != JsonValueKind.Array ||
                    choicesElem.GetArrayLength() == 0)
                {
                    return new AiTurn(null, Array.Empty<AiToolCall>());
                }

                var firstChoice = choicesElem[0];
                if (!firstChoice.TryGetProperty("message", out var messageElem))
                {
                    return new AiTurn(null, Array.Empty<AiToolCall>());
                }

                string? content = null;
                if (messageElem.TryGetProperty("content", out var contentElem) &&
                    contentElem.ValueKind == JsonValueKind.String)
                {
                    content = contentElem.GetString();
                }

                var toolCalls = new List<AiToolCall>();
                if (messageElem.TryGetProperty("tool_calls", out var toolCallsElem) &&
                    toolCallsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tcItem in toolCallsElem.EnumerateArray())
                    {
                        var id = tcItem.TryGetProperty("id", out var idElem) && idElem.ValueKind == JsonValueKind.String
                            ? idElem.GetString() ?? string.Empty
                            : string.Empty;

                        if (tcItem.TryGetProperty("function", out var fnElem))
                        {
                            var fnName = fnElem.TryGetProperty("name", out var nameElem) && nameElem.ValueKind == JsonValueKind.String
                                ? nameElem.GetString() ?? string.Empty
                                : string.Empty;

                            string fnArgs = "{}";
                            if (fnElem.TryGetProperty("arguments", out var argsElem))
                            {
                                fnArgs = argsElem.ValueKind == JsonValueKind.String
                                    ? argsElem.GetString() ?? "{}"
                                    : argsElem.GetRawText();
                            }

                            if (!string.IsNullOrEmpty(fnName))
                            {
                                toolCalls.Add(new AiToolCall(id, fnName, fnArgs));
                            }
                        }
                    }
                }

                return new AiTurn(content, toolCalls);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("AI provider response reading timed out");
                throw new AiException("پاسخ سرویس هوش مصنوعی به دلیل اتمام زمان مهلت دریافت نشد (timeout).");
            }
            catch (Exception ex) when (ex is not AiException)
            {
                _logger.LogWarning("AI provider response parsing failed [{ExceptionType}]", ex.GetType().Name);
                throw new AiException("خطا در پردازش پاسخ سرویس هوش مصنوعی.", ex);
            }
        }
    }
}
