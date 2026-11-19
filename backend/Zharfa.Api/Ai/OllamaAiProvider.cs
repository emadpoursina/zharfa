namespace Zharfa.Api.Ai;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Local AI provider implementation using OpenAI-compatible protocol (e.g. Ollama /v1).
/// Configured via LocalAi__* environment settings. No local API key required.
/// </summary>
public class OllamaAiProvider : IAiProvider
{
    private readonly OpenAiCompatibleChatClient _chatClient;
    private readonly string _name;
    private readonly string _endpoint;
    private readonly string _model;

    public OllamaAiProvider(
        OpenAiCompatibleChatClient chatClient,
        IConfiguration configuration)
    {
        _chatClient = chatClient;

        var configuredName = configuration["LocalAi:Name"] ?? configuration["LocalAi__Name"];
        _name = string.IsNullOrWhiteSpace(configuredName) ? "ollama" : configuredName.Trim();
        _endpoint = configuration["LocalAi:Endpoint"] ?? configuration["LocalAi__Endpoint"] ?? string.Empty;
        _model = configuration["LocalAi:Model"] ?? configuration["LocalAi__Model"] ?? string.Empty;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_endpoint) &&
        !string.IsNullOrWhiteSpace(_model);

    public ProviderStatus GetStatus() =>
        new ProviderStatus(_name, OpenAiCompatibleChatClient.RedactEndpoint(_endpoint), _model, IsConfigured);

    public async Task<AiTurn> CompleteAsync(
        IReadOnlyList<AiMessage> messages,
        IReadOnlyList<AiToolSpec> tools,
        CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new AiException("سرویس هوش مصنوعی محلی پیکربندی نشده است.");
        }

        return await _chatClient.CompleteAsync(_endpoint, _model, apiKey: null, messages, tools, ct);
    }

    public async Task ProbeLivenessAsync(CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new AiException("سرویس هوش مصنوعی محلی پیکربندی نشده است.");
        }

        await _chatClient.ProbeLivenessAsync(_endpoint, _model, apiKey: null, ct);
    }
}
