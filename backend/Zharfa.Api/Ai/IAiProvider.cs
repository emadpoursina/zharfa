namespace Zharfa.Api.Ai;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Redacted status of an AI provider.
/// Contains name, endpoint, model, and configured flag ONLY — never secrets or keys.
/// </summary>
public record ProviderStatus(
    string Name,
    string Endpoint,
    string Model,
    bool Configured);

/// <summary>
/// A tool call requested by the AI model.
/// </summary>
public record AiToolCall(
    string Id,
    string Name,
    string ArgumentsJson);

/// <summary>
/// Chat message for the neutral AI model conversation.
/// Supports system, user, assistant (with content and/or tool calls), and tool role results.
/// </summary>
public record AiMessage(
    string Role,
    string? Content,
    string? ToolCallId = null,
    IReadOnlyList<AiToolCall>? ToolCalls = null);

/// <summary>
/// OpenAI-compatible tool specification defining a callable function.
/// </summary>
public record AiToolSpec(
    string Name,
    string Description,
    object Parameters);

/// <summary>
/// Represents a single turn/response from the AI provider.
/// </summary>
public record AiTurn(
    string? Content,
    IReadOnlyList<AiToolCall> ToolCalls);

/// <summary>
/// Exception carrying ONLY a scrubbed Persian-safe reason for AI errors.
/// Never contains response bodies, URL-with-key, or request headers.
/// </summary>
public class AiException : Exception
{
    public AiException(string persianMessage) : base(persianMessage) { }
    public AiException(string persianMessage, Exception? innerException) : base(persianMessage, innerException) { }
}

/// <summary>
/// Generic interface for an AI provider.
/// Single-path cloud provider in Demo 1, with an unimplemented Ollama seam.
/// </summary>
public interface IAiProvider
{
    bool IsConfigured { get; }

    ProviderStatus GetStatus();

    Task<AiTurn> CompleteAsync(
        IReadOnlyList<AiMessage> messages,
        IReadOnlyList<AiToolSpec> tools,
        CancellationToken ct);
}
