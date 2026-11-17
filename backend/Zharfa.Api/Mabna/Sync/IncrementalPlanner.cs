namespace Zharfa.Api.Mabna.Sync;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Zharfa.Api.Mabna;

/// <summary>
/// Which previously synced rows a run may delete when they were not read. A deletion is only
/// ever decided against a complete set of source keys: a full read, or the key-only pass of a
/// header table (<see cref="TablePlan.KeyPass"/>).
/// </summary>
public enum DeleteScope
{
    /// <summary>No key diff: local rows are never deleted by this run.</summary>
    None,

    /// <summary>Every local row whose key was not read in this run is deleted.</summary>
    AllUnseen,
}

/// <summary>The change marker of one logical table, from the source map.</summary>
/// <remarks>
/// <c>lookbackDays</c> from earlier maps is ignored: header tables are no longer read through a
/// re-read window (research R5, spec 003 Phase 13).
/// </remarks>
public sealed record ChangeMarker(ChangeStrategy Strategy, string? Column);

/// <summary>Stored sync state of one table (source_record). Null watermark = never read incrementally.</summary>
public sealed record TableSyncState(string? Watermark, DateTimeOffset? LastFullLoadAt)
{
    public static readonly TableSyncState Unloaded = new(null, null);
}

/// <summary>What one table read looks like in this run.</summary>
/// <param name="PageColumn">Column the keyset pages are ordered and resumed by.</param>
/// <param name="FullRead">True when the read has no change predicate (every source row is read).</param>
/// <param name="ChangePredicate">Extra predicate for an incremental read, or null.</param>
/// <param name="DeleteScope">Which local rows the run may delete.</param>
/// <param name="KeyPass">
/// True for an incremental header read: the changed rows are read with the change predicate, and a
/// second read of every source key (no predicate) stages the full key set before deletions are decided.
/// </param>
public sealed record TablePlan(
    string PageColumn,
    bool FullRead,
    MabnaFilter? ChangePredicate,
    DeleteScope DeleteScope,
    bool KeyPass);

/// <summary>
/// Pure planning rules (data-model.md §2, research R5–R7). No I/O: the runner reads the stored
/// watermark state and asks this class what to read and delete. The SQL in
/// <see cref="SyncRunner"/> applies the same rules to the synced copy.
///
/// Every table must converge to a full load, so:
/// <list type="bullet">
/// <item><c>idWatermark</c> headers are read in full on every run, with a key diff (no window);</item>
/// <item><c>rowversion</c> and <c>modifiedAt</c> headers read the changed rows, plus a key-only pass
/// that finds deletions;</item>
/// <item>masters (vendor, product) are read in full on every run with a key diff, whatever change
/// marker the map gives them (research R5 <c>full</c> rule: masters are small, and a renamed or removed
/// master must reach the copy on the nightly run).</item>
/// </list>
/// </summary>
public static class IncrementalPlanner
{
    /// <summary>modifiedAt reads start this long before the stored watermark, so late commits are not missed.</summary>
    public static readonly TimeSpan ModifiedAtOverlap = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Decides how one table is read.
    /// </summary>
    /// <param name="keyColumn">Entity key; the paging column of full reads.</param>
    /// <param name="marker">The table's change marker from the source map.</param>
    /// <param name="state">Stored watermark and last full load of the table.</param>
    /// <param name="runIsFull">True for a full run.</param>
    /// <param name="isDocument">True for a header table (invoice, return); false for a master.</param>
    public static TablePlan PlanTable(
        string keyColumn,
        ChangeMarker marker,
        TableSyncState state,
        bool runIsFull,
        bool isDocument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyColumn);
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentNullException.ThrowIfNull(state);

        // Masters: always a full read with a key diff; the change marker is ignored.
        if (!isDocument)
        {
            return FullReadPlan(keyColumn);
        }

        // Headers: idWatermark is never incremental (no marker finds deletions), so it is a full read every run.
        var fullRead = runIsFull
            || marker.Strategy is ChangeStrategy.Full or ChangeStrategy.IdWatermark
            || state.LastFullLoadAt is null;
        if (!fullRead)
        {
            fullRead = state.Watermark is null || marker.Column is null;
        }

        if (fullRead)
        {
            return FullReadPlan(keyColumn);
        }

        return marker.Strategy switch
        {
            ChangeStrategy.Rowversion => new TablePlan(
                marker.Column!,
                false,
                new MabnaFilter(marker.Column!, MabnaFilterOp.Gt, RowversionFromText(state.Watermark!)),
                DeleteScope.AllUnseen,
                KeyPass: true),

            ChangeStrategy.ModifiedAt => new TablePlan(
                keyColumn,
                false,
                new MabnaFilter(marker.Column!, MabnaFilterOp.Ge, ModifiedAtFromText(state.Watermark!) - ModifiedAtOverlap),
                DeleteScope.AllUnseen,
                KeyPass: true),

            _ => throw new ArgumentOutOfRangeException(nameof(marker), "unsupported change strategy for an incremental header read"),
        };
    }

    private static TablePlan FullReadPlan(string keyColumn) =>
        new(keyColumn, true, null, DeleteScope.AllUnseen, KeyPass: false);

    /// <summary>Largest idWatermark value seen (new watermark), never lower than the previous one.</summary>
    public static long? NextIdWatermark(long? previous, IEnumerable<long> observed)
    {
        long? best = previous;
        foreach (var value in observed)
        {
            if (best is null || value > best)
            {
                best = value;
            }
        }

        return best;
    }

    /// <summary>Largest rowversion seen, as hex text (<c>0x…</c>). rowversion is 8 big-endian bytes, so byte order is numeric order.</summary>
    public static string? NextRowversionWatermark(string? previousText, IEnumerable<byte[]> observed)
    {
        byte[]? best = string.IsNullOrWhiteSpace(previousText) ? null : RowversionFromText(previousText);
        foreach (var value in observed)
        {
            if (best is null || value.AsSpan().SequenceCompareTo(best.AsSpan()) > 0)
            {
                best = value;
            }
        }

        return best is null ? null : RowversionText(best);
    }

    /// <summary>Latest modifiedAt seen, as round-trip text.</summary>
    public static string? NextModifiedAtWatermark(string? previousText, IEnumerable<DateTime> observed)
    {
        DateTime? best = string.IsNullOrWhiteSpace(previousText) ? null : ModifiedAtFromText(previousText);
        foreach (var value in observed)
        {
            if (best is null || value > best)
            {
                best = value;
            }
        }

        return best?.ToString("O", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Local keys to delete after a complete key set was read: every local key that was not seen
    /// when the scope is <see cref="DeleteScope.AllUnseen"/>. Nothing is deleted for
    /// <see cref="DeleteScope.None"/>.
    /// </summary>
    public static IReadOnlyList<string> KeysToDelete(
        IEnumerable<string> localKeys,
        ISet<string> seenKeys,
        DeleteScope scope)
    {
        ArgumentNullException.ThrowIfNull(localKeys);
        ArgumentNullException.ThrowIfNull(seenKeys);
        if (scope == DeleteScope.None)
        {
            return Array.Empty<string>();
        }

        return localKeys.Where(key => !seenKeys.Contains(key)).ToList();
    }

    /// <summary>
    /// Line replacement as a set per document: a line is deleted when it is not in the source read
    /// and its document is in the read scope. Lines of other documents are kept.
    /// </summary>
    public static IReadOnlyList<(string Document, string Line)> LinesToDelete(
        IEnumerable<(string Document, string Line)> localLines,
        ISet<(string Document, string Line)> sourceLines,
        ISet<string> documentsRead)
    {
        ArgumentNullException.ThrowIfNull(sourceLines);
        ArgumentNullException.ThrowIfNull(documentsRead);
        return localLines
            .Where(l => documentsRead.Contains(l.Document) && !sourceLines.Contains(l))
            .ToList();
    }

    public static byte[] RowversionFromText(string text)
    {
        var hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        return Convert.FromHexString(hex);
    }

    public static string RowversionText(byte[] value) => "0x" + Convert.ToHexString(value);

    public static DateTime ModifiedAtFromText(string text) =>
        DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

}
