namespace Zharfa.Api.Tests.Audit;

using System.Collections.Generic;
using System.Linq;
using Zharfa.Api.Audit;

/// <summary>
/// T061 (data-model §2.3, Clarify Q2 A, owner decision 2026-11-18): the report-open rule. A paging or sorting change
/// alone adds nothing; every other load is recorded. No clock and no time window. Pure: no database, no network.
/// </summary>
public class ReportOpenTrackerTests
{
    private const string ByVendor = "by-vendor";
    private const string Overview = "overview";

    private static IReadOnlyDictionary<string, string?> Params(params (string Key, string? Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string?> Scope(string period = "1405-08", string? vendor = null) =>
        vendor is null ? Params(("period", period)) : Params(("period", period), ("vendor", vendor));

    private static IReadOnlyDictionary<string, string?> View(string? page = null, string? pageSize = null, string? sort = null, string? dir = null) =>
        Params(("page", page), ("pageSize", pageSize), ("sort", sort), ("dir", dir))
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    [Fact]
    public void First_open_of_a_report_is_recorded()
    {
        var tracker = new ReportOpenTracker();

        Assert.True(tracker.ShouldRecord(1, ByVendor, Scope(), View(page: "1"), print: false));
    }

    [Fact]
    public void Reload_with_identical_parameters_is_recorded()
    {
        var tracker = new ReportOpenTracker();
        tracker.ShouldRecord(1, ByVendor, Scope(), View(page: "1"), print: false);

        Assert.True(tracker.ShouldRecord(1, ByVendor, Scope(), View(page: "1"), print: false));
    }

    [Fact]
    public void Paging_and_sorting_changes_alone_are_not_recorded_and_a_reload_of_that_page_is()
    {
        var tracker = new ReportOpenTracker();
        tracker.ShouldRecord(1, ByVendor, Scope(), View(page: "1"), print: false);

        Assert.False(tracker.ShouldRecord(1, ByVendor, Scope(), View(page: "2"), print: false));
        Assert.False(tracker.ShouldRecord(1, ByVendor, Scope(), View(page: "2", sort: "name"), print: false));
        Assert.False(tracker.ShouldRecord(1, ByVendor, Scope(), View(page: "2", sort: "name", dir: "desc"), print: false));
        Assert.False(tracker.ShouldRecord(1, ByVendor, Scope(), View(page: "2", sort: "name", dir: "desc", pageSize: "100"), print: false));

        // Reloading exactly the page the user is on is a load again.
        Assert.True(tracker.ShouldRecord(1, ByVendor, Scope(), View(page: "2", sort: "name", dir: "desc", pageSize: "100"), print: false));
    }

    [Fact]
    public void A_changed_period_or_vendor_is_recorded()
    {
        var tracker = new ReportOpenTracker();
        tracker.ShouldRecord(1, ByVendor, Scope(), View(page: "1"), print: false);

        Assert.True(tracker.ShouldRecord(1, ByVendor, Scope(period: "1405-07"), View(page: "1"), print: false));
        Assert.True(tracker.ShouldRecord(1, ByVendor, Scope(period: "1405-07", vendor: "V1"), View(page: "1"), print: false));
    }

    [Fact]
    public void A_print_is_always_recorded_and_leaves_the_state_unchanged()
    {
        var tracker = new ReportOpenTracker();
        tracker.ShouldRecord(1, ByVendor, Scope(vendor: "V1"), View(page: "1"), print: false);

        Assert.True(tracker.ShouldRecord(1, ByVendor, Scope(vendor: "V1"), View(page: "1"), print: true));

        // The previous screen request is still the one with page 1, so a page change is still only paging.
        Assert.False(tracker.ShouldRecord(1, ByVendor, Scope(vendor: "V1"), View(page: "3"), print: false));
    }

    [Fact]
    public void Two_users_are_tracked_independently()
    {
        var tracker = new ReportOpenTracker();
        Assert.True(tracker.ShouldRecord(1, ByVendor, Scope(), View(page: "1"), print: false));
        Assert.True(tracker.ShouldRecord(2, ByVendor, Scope(), View(page: "2"), print: false));

        Assert.False(tracker.ShouldRecord(1, ByVendor, Scope(), View(page: "3"), print: false));
        Assert.False(tracker.ShouldRecord(2, ByVendor, Scope(), View(page: "4"), print: false));
    }

    [Fact]
    public void Two_reports_of_one_user_are_tracked_independently()
    {
        var tracker = new ReportOpenTracker();
        Assert.True(tracker.ShouldRecord(1, ByVendor, Scope(), View(page: "1"), print: false));
        Assert.True(tracker.ShouldRecord(1, Overview, Scope(), View(), print: false));

        Assert.False(tracker.ShouldRecord(1, ByVendor, Scope(), View(page: "2"), print: false));
    }

    [Fact]
    public void Parameter_order_does_not_change_the_signature()
    {
        var tracker = new ReportOpenTracker();
        var first = Params(("period", "1405-08"), ("vendor", "V1"));
        var reordered = Params(("vendor", "V1"), ("period", "1405-08"));

        tracker.ShouldRecord(1, ByVendor, first, View(page: "1"), print: false);

        Assert.True(tracker.ShouldRecord(1, ByVendor, reordered, View(page: "1"), print: false));
        Assert.False(tracker.ShouldRecord(1, ByVendor, reordered, View(page: "2"), print: false));
    }

    [Fact]
    public void Many_calls_keep_at_most_one_entry_per_user_and_report()
    {
        var tracker = new ReportOpenTracker();
        string[] reports = [ByVendor, Overview, "trend"];

        for (var index = 0; index < 1000; index++)
        {
            var user = index % 10 + 1;
            var report = reports[index % 3];
            tracker.ShouldRecord(user, report, Scope(period: "1405-0" + (index % 9 + 1)), View(page: (index % 7 + 1).ToString()), print: false);
        }

        Assert.Equal(30, tracker.TrackedCount);
    }
}
