// ============================================================================
// OllamaAiProvider — Unimplemented seam for Zharfa Demo 1.
// Locked single-path decision: Demo 1 never implements Ollama (PRD §6 / research.md).
// IsConfigured is always false, and CompleteAsync always throws NotSupportedException.
// The controller's unavailable path handles it (HTTP 503), proving the seam works
// without any Ollama implementation.
// ============================================================================

namespace Zharfa.Api.Ai;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class OllamaAiProvider : IAiProvider
{
    public bool IsConfigured => false;

    public ProviderStatus GetStatus() =>
        new ProviderStatus("ollama", string.Empty, string.Empty, false);

    public Task<AiTurn> CompleteAsync(
        IReadOnlyList<AiMessage> messages,
        IReadOnlyList<AiToolSpec> tools,
        CancellationToken ct)
    {
        throw new NotSupportedException("ارائه‌دهنده محلی Ollama در نسخه نمایشی ۱ پشتیبانی نمی‌شود.");
    }
}
