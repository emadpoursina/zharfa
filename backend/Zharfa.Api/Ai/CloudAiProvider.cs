namespace Zharfa.Api.Ai;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Cloud AI provider implementation using OpenAI-compatible chat completions protocol.
/// Configured via server environment only. Never exposes secrets or API keys.
/// </summary>
public class CloudAiProvider : IAiProvider
{
    private readonly OpenAiCompatibleChatClient _chatClient;
    private readonly string _name;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly string? _apiKey;

    public CloudAiProvider(
        OpenAiCompatibleChatClient chatClient,
        IConfiguration configuration)
    {
        _chatClient = chatClient;

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
        new ProviderStatus(_name, OpenAiCompatibleChatClient.RedactEndpoint(_endpoint), _model, IsConfigured);

    public static string ResolveEndpoint(string endpoint) =>
        OpenAiCompatibleChatClient.ResolveEndpoint(endpoint);

    public async Task<AiTurn> CompleteAsync(
        IReadOnlyList<AiMessage> messages,
        IReadOnlyList<AiToolSpec> tools,
        CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new AiException("سرویس هوش مصنوعی پیکربندی نشده است.");
        }

        return await _chatClient.CompleteAsync(_endpoint, _model, _apiKey, messages, tools, ct);
    }

    public async Task ProbeLivenessAsync(CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new AiException("سرویس هوش مصنوعی ابری پیکربندی نشده است.");
        }

        await _chatClient.ProbeLivenessAsync(_endpoint, _model, _apiKey, ct);
    }
}
