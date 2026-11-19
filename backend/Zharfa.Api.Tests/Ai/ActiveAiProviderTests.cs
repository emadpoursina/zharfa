namespace Zharfa.Api.Tests.Ai;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zharfa.Api.Ai;

public class ActiveAiProviderTests
{
    private class FakeAiProvider(string name, bool configured = true) : IAiProvider
    {
        public bool IsConfigured { get; set; } = configured;
        public int CallCount { get; private set; }
        public bool ShouldThrow { get; set; }

        public ProviderStatus GetStatus() => new(name, $"http://{name}:8080/v1", $"{name}-model", IsConfigured);

        public Task<AiTurn> CompleteAsync(
            IReadOnlyList<AiMessage> messages,
            IReadOnlyList<AiToolSpec> tools,
            CancellationToken ct)
        {
            CallCount++;
            if (ShouldThrow)
            {
                throw new AiException("خطا در ارتباط با سرویس ابری");
            }
            return Task.FromResult(new AiTurn("پاسخ تست", Array.Empty<AiToolCall>()));
        }
    }

    private class FakeAiSettings(bool cloudEnabled) : IAiProviderSettings
    {
        public Task<bool> IsCloudEnabledAsync(CancellationToken ct) => Task.FromResult(cloudEnabled);
        public Task<AiSettingsSnapshot> GetSnapshotAsync(CancellationToken ct) =>
            Task.FromResult(new AiSettingsSnapshot(cloudEnabled, null, null, null));
    }

    [Theory]
    [InlineData(false)]
    public async Task When_cloud_disabled_completes_via_local_and_reports_local(bool cloudEnabled)
    {
        var local = new FakeAiProvider("local");
        var cloud = new FakeAiProvider("cloud");
        var settings = new FakeAiSettings(cloudEnabled);
        var active = ActiveAiProvider.CreateForTesting(local, cloud, settings);

        var answering = await active.GetAnsweringProviderNameAsync(CancellationToken.None);
        Assert.Equal("local", answering);

        var turn = await active.CompleteAsync(Array.Empty<AiMessage>(), Array.Empty<AiToolSpec>(), CancellationToken.None);
        Assert.NotNull(turn);
        Assert.Equal(1, local.CallCount);
        Assert.Equal(0, cloud.CallCount);
    }

    [Fact]
    public async Task When_cloud_enabled_completes_via_cloud_and_never_falls_back_to_local_on_error()
    {
        var local = new FakeAiProvider("local");
        var cloud = new FakeAiProvider("cloud") { ShouldThrow = true };
        var settings = new FakeAiSettings(true);
        var active = ActiveAiProvider.CreateForTesting(local, cloud, settings);

        var answering = await active.GetAnsweringProviderNameAsync(CancellationToken.None);
        Assert.Equal("cloud", answering);

        var ex = await Assert.ThrowsAsync<AiException>(() =>
            active.CompleteAsync(Array.Empty<AiMessage>(), Array.Empty<AiToolSpec>(), CancellationToken.None));

        Assert.Equal("خطا در ارتباط با سرویس ابری", ex.Message);
        Assert.Equal(1, cloud.CallCount);
        Assert.Equal(0, local.CallCount);
    }

    [Fact]
    public void GetStatus_never_includes_apiKey_field()
    {
        var local = new FakeAiProvider("local");
        var cloud = new FakeAiProvider("cloud");
        var settings = new FakeAiSettings(false);
        var active = ActiveAiProvider.CreateForTesting(local, cloud, settings);

        var status = active.GetStatus();
        var json = JsonSerializer.Serialize(status);

        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("key", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task When_cloud_enabled_and_local_unconfigured_IsConfiguredAsync_is_true()
    {
        var local = new FakeAiProvider("local", configured: false);
        var cloud = new FakeAiProvider("cloud", configured: true);
        var settings = new FakeAiSettings(true);
        var active = ActiveAiProvider.CreateForTesting(local, cloud, settings);

        var configured = await active.IsConfiguredAsync(CancellationToken.None);
        Assert.True(configured);
        Assert.True(active.IsConfigured);
    }

    [Fact]
    public async Task When_cloud_disabled_and_local_unconfigured_IsConfiguredAsync_is_false()
    {
        var local = new FakeAiProvider("local", configured: false);
        var cloud = new FakeAiProvider("cloud", configured: true);
        var settings = new FakeAiSettings(false);
        var active = ActiveAiProvider.CreateForTesting(local, cloud, settings);

        var configured = await active.IsConfiguredAsync(CancellationToken.None);
        Assert.False(configured);
    }

    [Fact]
    public async Task When_cloud_enabled_and_cloud_unconfigured_IsConfiguredAsync_is_false()
    {
        var local = new FakeAiProvider("local", configured: true);
        var cloud = new FakeAiProvider("cloud", configured: false);
        var settings = new FakeAiSettings(true);
        var active = ActiveAiProvider.CreateForTesting(local, cloud, settings);

        var configured = await active.IsConfiguredAsync(CancellationToken.None);
        Assert.False(configured);
    }

    [Fact]
    public void DependencyInjection_resolves_ActiveAiProvider_and_IAiProvider_without_ambiguity()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationManager();
        services.AddSingleton<IConfiguration>(config);
        services.AddHttpClient();
        services.AddSingleton<OpenAiCompatibleChatClient>();
        services.AddScoped<OllamaAiProvider>();
        services.AddScoped<CloudAiProvider>();
        services.AddScoped<IAiProviderSettings>(_ => new FakeAiSettings(false));
        services.AddScoped<ActiveAiProvider>();
        services.AddScoped<IAiProvider>(sp => sp.GetRequiredService<ActiveAiProvider>());

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var active = scope.ServiceProvider.GetRequiredService<ActiveAiProvider>();
        var iface = scope.ServiceProvider.GetRequiredService<IAiProvider>();

        Assert.NotNull(active);
        Assert.NotNull(iface);
        Assert.Same(active, iface);
    }
}
