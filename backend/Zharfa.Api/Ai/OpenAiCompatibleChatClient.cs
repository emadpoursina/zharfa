namespace Zharfa.Api.Ai;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

/// <summary>
/// Shared OpenAI-compatible wire client for cloud and local AI providers.
/// Enforces endpoint resolution, optional Bearer auth, scrubbed Persian errors,
/// iteration count logging (never bodies, URLs with keys, or secrets).
/// </summary>
public class OpenAiCompatibleChatClient(
    IHttpClientFactory httpClientFactory,
    ILogger<OpenAiCompatibleChatClient> logger)
{
    /// <summary>
    /// Resolves the chat completions endpoint from configured base.
    /// Trim trailing '/'; if it already ends with "chat/completions" use it as-is, else append "/chat/completions".
    /// </summary>
    public static string ResolveEndpoint(string endpoint)
    {
        var trimmed = (endpoint ?? string.Empty).Trim().TrimEnd('/');
        if (trimmed.EndsWith("chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }
        return trimmed + "/chat/completions";
    }

    /// <summary>
    /// Strips userinfo (username and password, including special characters) and query/fragment
    /// parameters from an endpoint string for safe status presentation.
    /// Never exposes embedded secrets or credentials.
    /// </summary>
    public static string RedactEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return string.Empty;
        }

        var trimmed = endpoint.Trim();

        // 1. Separate scheme if present (e.g. "https://", "http://", "//")
        string scheme = string.Empty;
        string rest = trimmed;

        var schemeIdx = trimmed.IndexOf("://", StringComparison.Ordinal);
        if (schemeIdx >= 0)
        {
            scheme = trimmed[..(schemeIdx + 3)];
            rest = trimmed[(schemeIdx + 3)..];
        }
        else if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            scheme = "//";
            rest = trimmed[2..];
        }

        // 2. Separate authority from path and query.
        // In standard URL format: scheme://authority/path?query#fragment
        // Authority ends at the first '/' or '?'. Userinfo can contain '#' before '@'.
        int authorityEnd = rest.Length;
        var slashIdx = rest.IndexOf('/');
        if (slashIdx >= 0)
        {
            authorityEnd = Math.Min(authorityEnd, slashIdx);
        }
        var questionIdx = rest.IndexOf('?');
        if (questionIdx >= 0)
        {
            authorityEnd = Math.Min(authorityEnd, questionIdx);
        }

        string authorityCandidate = rest[..authorityEnd];
        string pathAndRest = rest[authorityEnd..];

        // If authority contains '@', strip everything up to the LAST '@' (userinfo: username and password).
        var lastAt = authorityCandidate.LastIndexOf('@');
        string hostCandidate = lastAt >= 0 ? authorityCandidate[(lastAt + 1)..] : authorityCandidate;

        // If there was no path ('/') or query ('?'), fragment ('#') could terminate the host
        var fragIdx = hostCandidate.IndexOf('#');
        string hostAndPort = fragIdx >= 0 ? hostCandidate[..fragIdx] : hostCandidate;

        // 3. Extract path (starts with '/') and strip query ('?') and fragment ('#')
        string path = string.Empty;
        if (pathAndRest.StartsWith('/'))
        {
            var queryOrFragIdx = pathAndRest.IndexOfAny(['?', '#']);
            path = queryOrFragIdx >= 0 ? pathAndRest[..queryOrFragIdx] : pathAndRest;
        }

        return scheme + hostAndPort + path;
    }

    public async Task ProbeLivenessAsync(
        string endpoint,
        string model,
        string? apiKey,
        CancellationToken ct)
    {
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        probeCts.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new AiException("آدرس سرویس هوش مصنوعی پیکربندی نشده است.");
            }

            var client = httpClientFactory.CreateClient("ai");
            var resolvedUrl = ResolveEndpoint(endpoint);

            using var request = new HttpRequestMessage(HttpMethod.Get, resolvedUrl);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, probeCts.Token);
            if ((int)response.StatusCode >= 500)
            {
                throw new AiException($"سرویس هوش مصنوعی در دسترس نیست (کد {response.StatusCode}).");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("AI provider liveness probe timed out");
            throw new AiException("پاسخ سرویس هوش مصنوعی به دلیل اتمام زمان مهلت دریافت نشد (timeout).");
        }
        catch (Exception ex) when (ex is not AiException)
        {
            logger.LogWarning("AI provider liveness probe failed: {Message}", ex.Message);
            throw new AiException("خطا در برقراری ارتباط با سرویس هوش مصنوعی.", ex);
        }
    }

    public async Task<AiTurn> CompleteAsync(
        string endpoint,
        string model,
        string? apiKey,
        IReadOnlyList<AiMessage> messages,
        IReadOnlyList<AiToolSpec> tools,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("ai");
        var resolvedUrl = ResolveEndpoint(endpoint);

        using var request = new HttpRequestMessage(HttpMethod.Post, resolvedUrl);

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
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
            ["model"] = model,
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

        logger.LogDebug("Sending AI chat completion request (messages count: {MessageCount}, tools count: {ToolCount})",
            messages.Count, tools?.Count ?? 0);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("AI provider request timed out");
            throw new AiException("پاسخ سرویس هوش مصنوعی به دلیل اتمام زمان مهلت دریافت نشد (timeout).");
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("AI provider network/transport error");
            throw new AiException("خطا در برقراری ارتباط شبکه با سرویس هوش مصنوعی (network).");
        }
        catch (Exception ex) when (ex is not AiException)
        {
            logger.LogWarning("AI provider transport failure [{ExceptionType}]", ex.GetType().Name);
            throw new AiException("خطا در برقراری ارتباط با سرویس هوش مصنوعی (network).", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("AI provider returned HTTP status {StatusCode}", (int)response.StatusCode);
                throw new AiException($"خطای سرویس هوش مصنوعی (کد وضعیت: {(int)response.StatusCode})");
            }

            try
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(ExtractFirstJsonValue(body));
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
                logger.LogWarning("AI provider response reading timed out");
                throw new AiException("پاسخ سرویس هوش مصنوعی به دلیل اتمام زمان مهلت دریافت نشد (timeout).");
            }
            catch (Exception ex) when (ex is not AiException)
            {
                logger.LogWarning("AI provider response parsing failed [{ExceptionType}]", ex.GetType().Name);
                throw new AiException("خطا در پردازش پاسخ سرویس هوش مصنوعی.", ex);
            }
        }
    }

    internal static string ExtractFirstJsonValue(string body)
    {
        var trimmed = body.TrimStart();
        string candidate = trimmed;

        if (!trimmed.StartsWith('{'))
        {
            foreach (var rawLine in body.Split('\n'))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var payload = line.Substring(5).TrimStart();
                if (payload.StartsWith('{'))
                {
                    candidate = payload;
                    break;
                }
            }
        }

        try
        {
            var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(candidate));
            if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
            {
                var depth = 1;
                while (depth > 0 && reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.StartObject) depth++;
                    else if (reader.TokenType == JsonTokenType.EndObject) depth--;
                }
                var bytesConsumed = (int)reader.BytesConsumed;
                return System.Text.Encoding.UTF8.GetString(
                    System.Text.Encoding.UTF8.GetBytes(candidate), 0, bytesConsumed);
            }
        }
        catch
        {
            // fallback to original candidate
        }

        return candidate;
    }
}
