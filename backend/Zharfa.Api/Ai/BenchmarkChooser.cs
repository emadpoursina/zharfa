namespace Zharfa.Api.Ai;

using System.Collections.Generic;
using System.Linq;

public record BenchmarkCandidate(string Model, double Accuracy, int MedianMs, bool Failed = false);

/// <summary>
/// Chooses the default local model from candidate benchmark results (contracts/benchmark.md).
/// The default is the candidate with the highest accuracy; ties break to faster median response time.
/// Failed or missing candidates are never chosen.
/// </summary>
public static class BenchmarkChooser
{
    public static BenchmarkCandidate? Pick(IEnumerable<BenchmarkCandidate> candidates)
    {
        return candidates
            .Where(c => !c.Failed)
            .OrderByDescending(c => c.Accuracy)
            .ThenBy(c => c.MedianMs)
            .FirstOrDefault();
    }
}
