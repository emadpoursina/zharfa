namespace Zharfa.Api.Ai;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Answering AI provider resolver (research R2).
/// Selects cloud iff AiProviderSettings says enabled, else local default.
/// If cloud is enabled but fails, never falls back to local (cloud is sole answering provider).
/// </summary>
public class ActiveAiProvider : IAiProvider
{
    private readonly IAiProvider _local;
    private readonly IAiProvider _cloud;
    private readonly IAiProviderSettings _settings;

    [ActivatorUtilitiesConstructor]
    public ActiveAiProvider(
        OllamaAiProvider localProvider,
        CloudAiProvider cloudProvider,
        IAiProviderSettings settings)
        : this((IAiProvider)localProvider, (IAiProvider)cloudProvider, settings)
    {
    }

    private ActiveAiProvider(IAiProvider local, IAiProvider cloud, IAiProviderSettings settings)
    {
        _local = local ?? throw new ArgumentNullException(nameof(local));
        _cloud = cloud ?? throw new ArgumentNullException(nameof(cloud));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// Test-only factory to construct an instance with test doubles for IAiProvider.
    /// </summary>
    public static ActiveAiProvider CreateForTesting(
        IAiProvider local,
        IAiProvider cloud,
        IAiProviderSettings settings) =>
        new(local, cloud, settings);

    public bool IsConfigured => _local.IsConfigured || _cloud.IsConfigured;

    public async Task<bool> IsConfiguredAsync(CancellationToken ct)
    {
        var cloudEnabled = await _settings.IsCloudEnabledAsync(ct);
        return cloudEnabled ? _cloud.IsConfigured : _local.IsConfigured;
    }

    public ProviderStatus GetStatus() => _local.GetStatus();

    public async Task<string> GetAnsweringProviderNameAsync(CancellationToken ct)
    {
        var cloudEnabled = await _settings.IsCloudEnabledAsync(ct);
        return cloudEnabled ? "cloud" : "local";
    }

    public async Task<AiTurn> CompleteAsync(
        IReadOnlyList<AiMessage> messages,
        IReadOnlyList<AiToolSpec> tools,
        CancellationToken ct)
    {
        var cloudEnabled = await _settings.IsCloudEnabledAsync(ct);
        if (cloudEnabled)
        {
            // Cloud is sole answering provider when enabled. No silent fallback to local.
            return await _cloud.CompleteAsync(messages, tools, ct);
        }

        return await _local.CompleteAsync(messages, tools, ct);
    }

    public async Task ProbeLivenessAsync(CancellationToken ct)
    {
        var cloudEnabled = await _settings.IsCloudEnabledAsync(ct);
        if (cloudEnabled)
        {
            if (!_cloud.IsConfigured)
            {
                throw new AiException("سرویس هوش مصنوعی ابری پیکربندی نشده است.");
            }
            await _cloud.ProbeLivenessAsync(ct);
        }
        else
        {
            if (!_local.IsConfigured)
            {
                throw new AiException("سرویس هوش مصنوعی محلی پیکربندی نشده است.");
            }
            await _local.ProbeLivenessAsync(ct);
        }
    }
}
