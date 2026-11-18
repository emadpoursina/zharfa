namespace Zharfa.Api.Tests.DataQuality;

using System.Linq;
using Zharfa.Api.DataQuality;

/// <summary>
/// T077 (contracts/api.md §4, FR-020, FR-023): the rejected-at-sync counts parsed from the sync log message of a table.
/// The message format is the one written by SyncRunner (<c>rejected:&lt;reason&gt;=&lt;n&gt;</c> parts). Pure.
/// </summary>
public class RejectedAtSyncTests
{
    [Fact]
    public void Each_rejected_part_becomes_one_entry_for_the_table()
    {
        var rows = DataQualityService.ParseRejected("purchaseInvoice", "read=10; rejected:date-invalid=2; rejected:key-missing=1");

        Assert.Equal(
            new[] { ("purchaseInvoice", "date-invalid", 2L), ("purchaseInvoice", "key-missing", 1L) },
            rows.Select(row => (row.Table, row.Reason, row.Count)).ToArray());
    }

    [Fact]
    public void A_message_without_rejected_parts_gives_no_entries()
    {
        Assert.Empty(DataQualityService.ParseRejected("purchaseInvoice", "warnings=0; read=10"));
    }

    [Fact]
    public void A_missing_message_gives_no_entries()
    {
        Assert.Empty(DataQualityService.ParseRejected("purchaseInvoice", null));
    }

    [Fact]
    public void Malformed_counts_and_reasons_are_ignored()
    {
        var rows = DataQualityService.ParseRejected(
            "purchaseLine",
            "rejected:date-invalid=x; rejected:key-missing=; rejected:=3; rejected:Bad Reason=4; rejected:quantity-bad=5");

        Assert.Equal(new[] { ("purchaseLine", "quantity-bad", 5L) }, rows.Select(row => (row.Table, row.Reason, row.Count)).ToArray());
    }

    [Fact]
    public void Counts_of_a_repeated_reason_are_summed()
    {
        var rows = DataQualityService.ParseRejected("purchaseInvoice", "rejected:date-invalid=2; rejected:date-invalid=3");

        Assert.Equal(new[] { 5L }, rows.Select(row => row.Count).ToArray());
    }
}
