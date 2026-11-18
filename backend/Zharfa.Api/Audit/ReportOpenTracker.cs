namespace Zharfa.Api.Audit;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Decides whether a report request is a "report open" that is audited (data-model §2.3, FR-013, FR-014, Clarify Q2 A,
/// owner decision 2026-11-18). It keeps, per (user, report), the signatures of the user's previous screen request:
/// the scope (period and filters) and the scope plus the paging/sorting view.
/// <list type="bullet">
/// <item>A print is always recorded and never reads or changes the state.</item>
/// <item>No previous request for the report, or the scope changed: recorded.</item>
/// <item>Same scope and same view (a reload with identical parameters): recorded.</item>
/// <item>Same scope, only page, pageSize, sort or dir changed: not recorded.</item>
/// </list>
/// Every non-print request stores its signatures. There is no time window and no clock. State is in memory only; a
/// restart makes the next request a first load.
/// </summary>
public sealed class ReportOpenTracker
{
    private readonly ConcurrentDictionary<(long UserId, string ReportKey), (string ScopeSig, string FullSig)> _previous = new();

    /// <summary>Number of (user, report) entries held. Bounded by users × report keys.</summary>
    public int TrackedCount => _previous.Count;

    public bool ShouldRecord(
        long userId,
        string reportKey,
        IReadOnlyDictionary<string, string?> scopeParams,
        IReadOnlyDictionary<string, string?> viewParams,
        bool print)
    {
        if (print)
        {
            return true;
        }

        var scopeSig = Signature(scopeParams);
        var fullSig = scopeSig + "|" + Signature(viewParams);

        // The update delegate may run again under contention, so the decision is assigned on every run.
        var record = true;
        _previous.AddOrUpdate(
            (userId, reportKey),
            _ => (scopeSig, fullSig),
            (_, previous) =>
            {
                // Same scope with different paging or sorting: the same report on another page, no new open.
                record = !(previous.ScopeSig == scopeSig && previous.FullSig != fullSig);
                return (scopeSig, fullSig);
            });

        return record;
    }

    /// <summary>Sorted, ordinal <c>k=v</c> pairs joined with <c>&amp;</c>; a null value is written as an empty value.</summary>
    internal static string Signature(IReadOnlyDictionary<string, string?> parameters) =>
        string.Join("&", parameters
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value ?? string.Empty}"));
}
