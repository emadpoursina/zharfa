namespace Zharfa.Api.Tests.Sync;

using Zharfa.Api.Mabna;
using Zharfa.Api.Mabna.Sync;

/// <summary>T030: keyset paging through the connector, with a fake reader (no database, no network).</summary>
public class KeysetPagerTests
{
    /// <summary>Returns the scripted pages in order and records every query it receives.</summary>
    private sealed class FakeReader(IReadOnlyList<object?[][]> pages) : IMabnaReader
    {
        private int _call;

        public List<MabnaQuery> Queries { get; } = new();

        public bool IsEnabled => true;

        public Task<MabnaReadResult> ReadAsync(MabnaQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            var rows = _call < pages.Count ? pages[_call] : Array.Empty<object?[]>();
            _call++;
            return Task.FromResult(new MabnaReadResult(
                new MabnaObjectName("dbo", "SimVendor"),
                query.Columns,
                rows,
                Truncated: false));
        }
    }

    private static async Task<List<IReadOnlyList<SourceRow>>> ReadAll(KeysetPager pager, KeysetRequest request)
    {
        var pages = new List<IReadOnlyList<SourceRow>>();
        await foreach (var page in pager.ReadPagesAsync(request))
        {
            pages.Add(page);
        }

        return pages;
    }

    private static readonly string[] Columns = { "VendorID", "VendorName" };

    [Fact]
    public async Task Query_uses_explicit_columns_gt_cursor_order_by_key_and_top_equal_to_page_size()
    {
        var reader = new FakeReader(new List<object?[][]>
        {
            new object?[][] { new object?[] { 1L, "الف" }, new object?[] { 2L, "ب" } },
            new object?[][] { new object?[] { 3L, "ج" } },
        });
        var pager = new KeysetPager(reader, pageSize: 2);

        var pages = await ReadAll(pager, new KeysetRequest("dbo.SimVendor", Columns, "VendorID", Array.Empty<MabnaFilter>()));

        Assert.Equal(2, pages.Count);
        Assert.Equal(2, reader.Queries.Count);

        var first = reader.Queries[0];
        Assert.Equal(Columns, first.Columns);
        Assert.Empty(first.Filters!);
        Assert.Equal(2, first.Top);
        Assert.Equal("VendorID", first.OrderBy![0].Column);
        Assert.False(first.OrderBy[0].Descending);

        var second = reader.Queries[1];
        Assert.Single(second.Filters!);
        Assert.Equal("VendorID", second.Filters![0].Column);
        Assert.Equal(MabnaFilterOp.Gt, second.Filters[0].Op);
        Assert.Equal(2L, second.Filters[0].Value);
        Assert.Equal(2, second.Top);
    }

    [Fact]
    public async Task Stops_after_a_short_page_and_returns_every_row_in_order()
    {
        var reader = new FakeReader(new List<object?[][]>
        {
            new object?[][] { new object?[] { 1L, "الف" }, new object?[] { 2L, "ب" } },
            new object?[][] { new object?[] { 3L, "ج" } },
        });
        var pager = new KeysetPager(reader, pageSize: 2);

        var pages = await ReadAll(pager, new KeysetRequest("dbo.SimVendor", Columns, "VendorID", Array.Empty<MabnaFilter>()));

        Assert.Equal(2, reader.Queries.Count);
        var keys = pages.SelectMany(p => p).Select(r => r.Get("VendorID")).ToList();
        Assert.Equal(new object?[] { 1L, 2L, 3L }, keys);
    }

    [Fact]
    public async Task Stops_on_an_empty_page_after_a_full_page()
    {
        var reader = new FakeReader(new List<object?[][]>
        {
            new object?[][] { new object?[] { 1L, "الف" }, new object?[] { 2L, "ب" } },
            Array.Empty<object?[]>(),
        });
        var pager = new KeysetPager(reader, pageSize: 2);

        var pages = await ReadAll(pager, new KeysetRequest("dbo.SimVendor", Columns, "VendorID", Array.Empty<MabnaFilter>()));

        Assert.Single(pages);
        Assert.Equal(2, reader.Queries.Count);
    }

    [Fact]
    public async Task Map_filters_are_sent_as_parameters_on_every_page()
    {
        var reader = new FakeReader(new List<object?[][]>
        {
            new object?[][] { new object?[] { 1L, "الف" }, new object?[] { 2L, "ب" } },
            Array.Empty<object?[]>(),
        });
        var pager = new KeysetPager(reader, pageSize: 2);
        var filter = new MabnaFilter("SimOpCode", MabnaFilterOp.Eq, 11m);

        await ReadAll(pager, new KeysetRequest("dbo.SimVendor", Columns, "VendorID", new[] { filter }));

        Assert.All(reader.Queries, q => Assert.Equal(new[] { "SimOpCode" }, q.Filters!.Where(f => f.Column == "SimOpCode").Select(f => f.Column)));
        Assert.Equal(11m, reader.Queries[1].Filters![0].Value);
        Assert.Equal(MabnaFilterOp.Gt, reader.Queries[1].Filters![1].Op);
    }

    [Fact]
    public async Task Page_column_is_added_to_the_selected_columns_when_missing()
    {
        var reader = new FakeReader(new List<object?[][]> { Array.Empty<object?[]>() });
        var pager = new KeysetPager(reader, pageSize: 5);

        await ReadAll(pager, new KeysetRequest("dbo.SimVendor", new[] { "VendorName" }, "VendorID", Array.Empty<MabnaFilter>()));

        Assert.Contains("VendorID", reader.Queries[0].Columns);
        Assert.Contains("VendorName", reader.Queries[0].Columns);
    }

    [Fact]
    public async Task A_full_page_with_a_null_cursor_value_is_refused_instead_of_looping()
    {
        var reader = new FakeReader(new List<object?[][]>
        {
            new object?[][] { new object?[] { 1L, "الف" }, new object?[] { null, "ب" } },
        });
        var pager = new KeysetPager(reader, pageSize: 2);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReadAll(pager, new KeysetRequest("dbo.SimVendor", Columns, "VendorID", Array.Empty<MabnaFilter>())));
    }

    [Fact]
    public void Page_size_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new KeysetPager(new FakeReader(new List<object?[][]>()), 0));
    }
}
