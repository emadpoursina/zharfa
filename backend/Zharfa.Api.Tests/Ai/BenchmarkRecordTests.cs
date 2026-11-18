namespace Zharfa.Api.Tests.Ai;

using System;
using Xunit;
using Zharfa.Api.Ai;

public class BenchmarkRecordTests
{
    [Fact]
    public void Pick_selects_highest_accuracy_candidate()
    {
        var candidates = new[]
        {
            new BenchmarkCandidate("model-a", 0.85, 12000),
            new BenchmarkCandidate("model-b", 0.95, 18000),
            new BenchmarkCandidate("model-c", 0.70, 8000),
        };

        var chosen = BenchmarkChooser.Pick(candidates);

        Assert.NotNull(chosen);
        Assert.Equal("model-b", chosen.Model);
        Assert.Equal(0.95, chosen.Accuracy);
    }

    [Fact]
    public void Pick_breaks_ties_with_faster_median_response_time()
    {
        var candidates = new[]
        {
            new BenchmarkCandidate("model-slow", 0.90, 25000),
            new BenchmarkCandidate("model-fast", 0.90, 14000),
        };

        var chosen = BenchmarkChooser.Pick(candidates);

        Assert.NotNull(chosen);
        Assert.Equal("model-fast", chosen.Model);
    }

    [Fact]
    public void Failed_candidate_is_never_chosen_even_with_high_accuracy()
    {
        var candidates = new[]
        {
            new BenchmarkCandidate("model-crashed", 1.0, 5000, Failed: true),
            new BenchmarkCandidate("model-ok", 0.80, 15000, Failed: false),
        };

        var chosen = BenchmarkChooser.Pick(candidates);

        Assert.NotNull(chosen);
        Assert.Equal("model-ok", chosen.Model);
    }

    [Fact]
    public void Empty_or_all_failed_candidates_returns_null()
    {
        Assert.Null(BenchmarkChooser.Pick(Array.Empty<BenchmarkCandidate>()));

        var allFailed = new[]
        {
            new BenchmarkCandidate("model-1", 0.0, 0, Failed: true),
            new BenchmarkCandidate("model-2", 0.5, 10000, Failed: true)
        };
        Assert.Null(BenchmarkChooser.Pick(allFailed));
    }
}
