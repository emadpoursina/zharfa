namespace Zharfa.Api.Tests.Sync;

using Zharfa.Api.Mabna;
using Zharfa.Api.Mabna.Sync;

/// <summary>
/// T029, revised by T094/T095 (spec 003 Phase 13) and T105 (Phase 14): planning for headers and masters,
/// key-diff deletes, the key-only pass, and line replacement. Pure logic; no window. Masters are full
/// reads with a key diff whatever their marker says.
/// </summary>
public class IncrementalPlannerTests
{
    private static readonly TableSyncState Loaded = new("1000", DateTimeOffset.UtcNow);

    [Fact]
    public void Full_run_reads_everything_and_deletes_unseen()
    {
        var plan = IncrementalPlanner.PlanTable("DocID", new ChangeMarker(ChangeStrategy.IdWatermark, "DocID"),
            Loaded, runIsFull: true, isDocument: true);

        Assert.True(plan.FullRead);
        Assert.Null(plan.ChangePredicate);
        Assert.Equal(DeleteScope.AllUnseen, plan.DeleteScope);
        Assert.False(plan.KeyPass);
        Assert.Equal("DocID", plan.PageColumn);
    }

    [Fact]
    public void First_run_ever_is_a_full_read_even_when_incremental_is_requested()
    {
        var plan = IncrementalPlanner.PlanTable("DocID", new ChangeMarker(ChangeStrategy.IdWatermark, "DocID"),
            TableSyncState.Unloaded, runIsFull: false, isDocument: true);

        Assert.True(plan.FullRead);
        Assert.Equal(DeleteScope.AllUnseen, plan.DeleteScope);
    }

    [Fact]
    public void Full_strategy_table_is_always_a_full_read()
    {
        var plan = IncrementalPlanner.PlanTable("VendorID", new ChangeMarker(ChangeStrategy.Full, null),
            Loaded, runIsFull: false, isDocument: false);

        Assert.True(plan.FullRead);
        Assert.Equal(DeleteScope.AllUnseen, plan.DeleteScope);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Idwatermark_document_is_a_full_read_with_key_diff_on_every_run(bool runIsFull)
    {
        // A key unseen in the read is deleted even when its document is older than 60 days: there is no window.
        var plan = IncrementalPlanner.PlanTable("DocID", new ChangeMarker(ChangeStrategy.IdWatermark, "DocID"),
            Loaded, runIsFull, isDocument: true);

        Assert.True(plan.FullRead);
        Assert.Null(plan.ChangePredicate);
        Assert.Equal(DeleteScope.AllUnseen, plan.DeleteScope);
        Assert.False(plan.KeyPass);
    }

    [Theory]
    [InlineData(ChangeStrategy.IdWatermark, false)]
    [InlineData(ChangeStrategy.IdWatermark, true)]
    [InlineData(ChangeStrategy.Rowversion, false)]
    [InlineData(ChangeStrategy.ModifiedAt, false)]
    [InlineData(ChangeStrategy.Full, false)]
    public void Master_is_a_full_read_with_key_diff_whatever_marker_the_map_gives(ChangeStrategy strategy, bool runIsFull)
    {
        // US2/AC5 and research R5 (`full` rule): masters converge to a full load on every run; the marker is ignored.
        var marker = new ChangeMarker(strategy, strategy == ChangeStrategy.Full ? null : "VendorID");
        var plan = IncrementalPlanner.PlanTable("VendorID", marker, Loaded, runIsFull, isDocument: false);

        Assert.True(plan.FullRead);
        Assert.Null(plan.ChangePredicate);
        Assert.Equal(DeleteScope.AllUnseen, plan.DeleteScope);
        Assert.False(plan.KeyPass);
    }

    [Fact]
    public void Master_key_diff_deletes_a_master_removed_at_the_source_and_keeps_a_renamed_one()
    {
        // V-2 was removed at the source; V-1 was renamed (same key, new name), so its key is still read.
        var local = new[] { "1", "2" };
        var seen = new HashSet<string>(StringComparer.Ordinal) { "1" };

        Assert.Equal(new[] { "2" }, IncrementalPlanner.KeysToDelete(local, seen, DeleteScope.AllUnseen));
    }

    [Fact]
    public void Master_idwatermark_without_a_numeric_watermark_is_a_full_read()
    {
        var plan = IncrementalPlanner.PlanTable("VendorID", new ChangeMarker(ChangeStrategy.IdWatermark, "VendorID"),
            new TableSyncState("not-a-number", DateTimeOffset.UtcNow), runIsFull: false, isDocument: false);

        Assert.True(plan.FullRead);
        Assert.Equal(DeleteScope.AllUnseen, plan.DeleteScope);
    }

    [Fact]
    public void Key_diff_deletes_every_unseen_key_and_keeps_seen_keys()
    {
        // 900 is old (outside any former window) and was removed at the source: it must be deleted.
        var local = new[] { "900", "950", "1000", "1100" };
        var seen = new HashSet<string>(StringComparer.Ordinal) { "1000", "1100" };

        var deleted = IncrementalPlanner.KeysToDelete(local, seen, DeleteScope.AllUnseen);

        Assert.Equal(new[] { "900", "950" }, deleted.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Changed_new_and_deleted_keys_are_handled_as_a_set()
    {
        // Source now has 1000 (changed), 1200 (new); 900, 950 and 1100 were removed at the source.
        var local = new[] { "900", "950", "1000", "1100" };
        var seen = new HashSet<string>(StringComparer.Ordinal) { "1000", "1200" };

        var deleted = IncrementalPlanner.KeysToDelete(local, seen, DeleteScope.AllUnseen);

        Assert.Equal(new[] { "1100", "900", "950" }.OrderBy(k => k, StringComparer.Ordinal), deleted.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void No_scope_deletes_nothing()
    {
        var local = new[] { "1", "2", "3" };
        var seen = new HashSet<string>(StringComparer.Ordinal) { "2" };

        Assert.Equal(new[] { "1", "3" }, IncrementalPlanner.KeysToDelete(local, seen, DeleteScope.AllUnseen));
        Assert.Empty(IncrementalPlanner.KeysToDelete(local, seen, DeleteScope.None));
    }

    [Fact]
    public void Lines_are_replaced_as_a_set_per_read_document_only()
    {
        var local = new[]
        {
            ("D1", "L1"), ("D1", "L2"), ("D2", "L1"), ("D3", "L9"),
        };
        var source = new HashSet<(string Document, string Line)> { ("D1", "L1"), ("D1", "L3") };
        var documentsRead = new HashSet<string>(StringComparer.Ordinal) { "D1", "D2" };

        var deleted = IncrementalPlanner.LinesToDelete(local, source, documentsRead);

        // D1/L2 and D2/L1 were read-scope documents without the line; D3 was not read and stays.
        Assert.Equal(new[] { ("D1", "L2"), ("D2", "L1") }, deleted.OrderBy(d => d.Document).ThenBy(d => d.Line));
    }

    [Fact]
    public void Modified_at_document_read_starts_ten_minutes_before_the_watermark_and_stages_every_key()
    {
        var state = new TableSyncState("2026-11-16T10:00:00.0000000", DateTimeOffset.UtcNow);
        var plan = IncrementalPlanner.PlanTable("DocID", new ChangeMarker(ChangeStrategy.ModifiedAt, "ModifiedAt"),
            state, runIsFull: false, isDocument: true);

        Assert.False(plan.FullRead);
        Assert.Equal(MabnaFilterOp.Ge, plan.ChangePredicate!.Op);
        Assert.Equal(new DateTime(2026, 11, 16, 9, 50, 0), (DateTime)plan.ChangePredicate.Value!);
        Assert.True(plan.KeyPass, "a deletion is only detected when the key-only pass stages every key");
        Assert.Equal(DeleteScope.AllUnseen, plan.DeleteScope);
    }

    [Fact]
    public void Rowversion_document_read_uses_the_stored_watermark_and_stages_every_key()
    {
        var plan = IncrementalPlanner.PlanTable("DocID", new ChangeMarker(ChangeStrategy.Rowversion, "RowVer"),
            new TableSyncState("0x0000000000000010", DateTimeOffset.UtcNow), runIsFull: false, isDocument: true);

        Assert.False(plan.FullRead);
        Assert.Equal("RowVer", plan.PageColumn);
        Assert.Equal(MabnaFilterOp.Gt, plan.ChangePredicate!.Op);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0x10 }, (byte[])plan.ChangePredicate.Value!);
        Assert.True(plan.KeyPass, "a deletion is only detected when the key-only pass stages every key");
        Assert.Equal(DeleteScope.AllUnseen, plan.DeleteScope);

        var low = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0x0F };
        var high = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0x20 };
        Assert.Equal("0x0000000000000020", IncrementalPlanner.NextRowversionWatermark("0x0000000000000010", new[] { low, high }));
        Assert.Equal("0x0000000000000010", IncrementalPlanner.NextRowversionWatermark("0x0000000000000010", new[] { low }));
    }

    [Fact]
    public void Rowversion_master_with_a_stored_watermark_is_a_full_read_with_key_diff_and_no_key_pass()
    {
        // The stored rowversion is not used for masters: a changed-row read would miss deletions and renames.
        var plan = IncrementalPlanner.PlanTable("ProductID", new ChangeMarker(ChangeStrategy.Rowversion, "RowVer"),
            new TableSyncState("0x0000000000000010", DateTimeOffset.UtcNow), runIsFull: false, isDocument: false);

        Assert.True(plan.FullRead);
        Assert.Null(plan.ChangePredicate);
        Assert.Equal(DeleteScope.AllUnseen, plan.DeleteScope);
        Assert.False(plan.KeyPass);
    }

    [Fact]
    public void Id_watermark_advances_to_the_largest_key_seen()
    {
        Assert.Equal(1005L, IncrementalPlanner.NextIdWatermark(1000, new long[] { 990, 1005 }));
        Assert.Equal(1000L, IncrementalPlanner.NextIdWatermark(1000, new long[] { 3 }));
        Assert.Null(IncrementalPlanner.NextIdWatermark(null, Array.Empty<long>()));
    }

    [Fact]
    public void Rowversion_read_without_a_stored_watermark_is_a_full_read()
    {
        var plan = IncrementalPlanner.PlanTable("DocID", new ChangeMarker(ChangeStrategy.Rowversion, "RowVer"),
            new TableSyncState(null, DateTimeOffset.UtcNow), runIsFull: false, isDocument: true);

        Assert.True(plan.FullRead);
        Assert.Null(plan.ChangePredicate);
        Assert.Equal(DeleteScope.AllUnseen, plan.DeleteScope);
        Assert.False(plan.KeyPass);
    }

    [Fact]
    public void Modified_at_without_a_stored_watermark_is_a_full_read()
    {
        var plan = IncrementalPlanner.PlanTable("DocID", new ChangeMarker(ChangeStrategy.ModifiedAt, "ModifiedAt"),
            new TableSyncState(null, DateTimeOffset.UtcNow), runIsFull: false, isDocument: true);

        Assert.True(plan.FullRead);
        Assert.Equal(DeleteScope.AllUnseen, plan.DeleteScope);
    }

    [Fact]
    public void Document_incremental_read_is_never_chosen_before_the_first_full_load()
    {
        var state = new TableSyncState("0x0000000000000010", LastFullLoadAt: null);
        var plan = IncrementalPlanner.PlanTable("DocID", new ChangeMarker(ChangeStrategy.Rowversion, "RowVer"),
            state, runIsFull: false, isDocument: true);

        Assert.True(plan.FullRead);
        Assert.False(plan.KeyPass);
    }
}
