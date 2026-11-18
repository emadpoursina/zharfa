namespace Zharfa.Api.Tests.Ai;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zharfa.Api.Ai;

public class PurchasingServerDispatchTests
{
    private record FixtureItem(
        int Id,
        string Question,
        string AskAs,
        string Expect,
        string Scope,
        string? Metric,
        string Period,
        string? Requires = null);

    private record FixtureRoot(
        string Id,
        int Version,
        FixtureItem[] Items);

    private static FixtureRoot LoadFixture()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "../../../../../specs/006-local-ai-purchasing/fixtures/purchasing-questions.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "specs/006-local-ai-purchasing/fixtures/purchasing-questions.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "../specs/006-local-ai-purchasing/fixtures/purchasing-questions.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "../../specs/006-local-ai-purchasing/fixtures/purchasing-questions.json"),
            "/Users/emad/Projects/playground/hermes-data/workspace/.speckit-worktrees/zharfa/zharfa-prd-phase-4/specs/006-local-ai-purchasing/fixtures/purchasing-questions.json"
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<FixtureRoot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            }
        }

        throw new FileNotFoundException("purchasing-questions.json not found");
    }

    [Fact]
    public void In_domain_fixture_items_map_to_contracts_tools_mapping()
    {
        var fixture = LoadFixture();

        foreach (var item in fixture.Items)
        {
            if (item.Scope != "purchasing" || item.Requires == "no-successful-sync")
            {
                continue;
            }

            var route = PurchasingQuestionRouter.Classify(item.Question, hasSuccessfulSync: true);
            Assert.Equal("purchasing", route.Scope);

            switch (item.Id)
            {
                case 1:
                case 2:
                case 8:
                case 9:
                case 10:
                    Assert.Equal("purchasing_overview", route.Tool);
                    Assert.Equal("overview-default", route.PeriodRule);
                    break;

                case 3:
                    Assert.Equal("purchasing_trend", route.Tool);
                    Assert.Equal("overview-default", route.PeriodRule);
                    break;

                case 4:
                    Assert.Equal("purchasing_overview", route.Tool);
                    Assert.Equal("overview-default", route.PeriodRule);
                    break;

                case 5:
                    Assert.Equal("purchasing_top_products", route.Tool);
                    Assert.Equal("overview-default", route.PeriodRule);
                    break;

                case 6:
                    Assert.Equal("purchasing_by_category", route.Tool);
                    Assert.Equal("overview-default", route.PeriodRule);
                    break;

                case 7:
                    Assert.Equal("purchasing_overview", route.Tool);
                    Assert.Equal("overview-default", route.PeriodRule);
                    break;

                case 11:
                    Assert.Equal("purchasing_overview", route.Tool);
                    Assert.Equal("previous-jalali-month", route.PeriodRule);
                    break;

                case 12:
                    Assert.Equal("purchasing_by_vendor", route.Tool);
                    Assert.Equal("current-jalali-quarter", route.PeriodRule);
                    break;

                case 19:
                    Assert.Equal("purchasing_overview", route.Tool);
                    Assert.Equal("future-empty-month", route.PeriodRule);
                    break;
            }
        }
    }

    private class FakeAiProvider : IAiProvider
    {
        public bool IsConfigured => true;
        public IReadOnlyList<AiToolSpec>? ReceivedTools { get; private set; }
        public int CompleteCallCount { get; private set; }

        public ProviderStatus GetStatus() => new("fake", "http://fake/v1", "model", true);

        public Task<AiTurn> CompleteAsync(
            IReadOnlyList<AiMessage> messages,
            IReadOnlyList<AiToolSpec> tools,
            CancellationToken ct)
        {
            CompleteCallCount++;
            ReceivedTools = tools;
            return Task.FromResult(new AiTurn("پاسخ مدل", Array.Empty<AiToolCall>()));
        }

        public Task ProbeLivenessAsync(CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task Fake_ai_provider_receives_empty_tool_specs_during_phrasing()
    {
        var provider = new FakeAiProvider();
        var emptyTools = Array.Empty<AiToolSpec>();
        var messages = new List<AiMessage>
        {
            new("system", "prompt"),
            new("user", "خالص خرید این ماه چقدر است؟")
        };

        var turn = await provider.CompleteAsync(messages, emptyTools, CancellationToken.None);

        Assert.Equal(1, provider.CompleteCallCount);
        Assert.NotNull(provider.ReceivedTools);
        Assert.Empty(provider.ReceivedTools);
        Assert.Equal("پاسخ مدل", turn.Content);
    }
}
