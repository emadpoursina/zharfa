namespace Zharfa.Api.Mabna.Sync;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Zharfa.Api.Mabna;

/// <summary>
/// One keyset-paged read of a source object. <see cref="PageColumn"/> is the ordering and resume
/// column (the entity key, or a rowversion/change column for a rowversion read).
/// </summary>
public sealed record KeysetRequest(
    string Object,
    IReadOnlyList<string> Columns,
    string PageColumn,
    IReadOnlyList<MabnaFilter> Filters,
    object? StartAfter = null);

/// <summary>
/// Pages through a source object with the connector only (<see cref="IMabnaReader"/>): explicit
/// columns, the map's filters plus <c>PageColumn &gt; last</c>, <c>ORDER BY PageColumn</c>, and
/// <c>TOP pageSize</c>. A page shorter than <see cref="PageSize"/> ends the read. No raw SQL.
/// </summary>
public sealed class KeysetPager(IMabnaReader reader, int pageSize)
{
    private readonly IMabnaReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    public int PageSize { get; } = pageSize > 0 ? pageSize : throw new ArgumentOutOfRangeException(nameof(pageSize));

    /// <summary>Yields non-empty pages in key order until a short page is read.</summary>
    public async IAsyncEnumerable<IReadOnlyList<SourceRow>> ReadPagesAsync(
        KeysetRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PageColumn);

        var columns = request.Columns.Any(c => string.Equals(c, request.PageColumn, StringComparison.OrdinalIgnoreCase))
            ? request.Columns.ToList()
            : request.Columns.Append(request.PageColumn).ToList();

        object? cursor = request.StartAfter;
        while (true)
        {
            var filters = request.Filters.ToList();
            if (cursor is not null)
            {
                filters.Add(new MabnaFilter(request.PageColumn, MabnaFilterOp.Gt, cursor));
            }

            var query = new MabnaQuery(
                request.Object,
                columns,
                filters,
                new[] { new MabnaOrder(request.PageColumn) },
                PageSize);

            var result = await _reader.ReadAsync(query, cancellationToken);
            var page = result.Rows
                .Select(values => SourceRow.Create(columns, values))
                .ToList();

            if (page.Count == 0)
            {
                yield break;
            }

            yield return page;

            if (page.Count < PageSize)
            {
                yield break;
            }

            cursor = page[^1].Get(request.PageColumn)
                     ?? throw new InvalidOperationException("keyset cursor value is null; the page column must be non-null");
        }
    }
}
