namespace Zharfa.Api.Tests.Purchasing.Reports;

using System.Collections.Generic;
using Zharfa.Api.Analytics;
using Zharfa.Api.Purchasing.Reports;

/// <summary>
/// T059 (spec 004 US4, contracts/api.md §4, T060): the vendor comparison request. The keys are trimmed and
/// de-duplicated in request order; two to five distinct keys are accepted; <c>~unknown</c> is refused; the
/// period is parsed by <see cref="JalaliPeriod"/>. Pure: no database, no network.
/// </summary>
public class VendorComparisonRequestTests
{
    // Today is in Aban 1405, so presets ending later are trimmed.
    private static readonly JalaliDate Today = new(1405, 8, 25);

    private static IReadOnlyList<string> Keys(string? raw)
    {
        Assert.True(VendorComparisonService.TryParseKeys(raw, out var keys, out var error), error);
        Assert.Null(error);
        return keys;
    }

    private static string Reject(string? raw)
    {
        var ok = VendorComparisonService.TryParseKeys(raw, out var keys, out var error);
        Assert.False(ok);
        Assert.NotNull(keys);
        Assert.False(string.IsNullOrWhiteSpace(error));
        return error!;
    }

    [Fact]
    public void Keys_are_trimmed_and_empty_parts_ignored()
    {
        Assert.Equal(new[] { "V-1", "V-2" }, Keys("  V-1 , V-2 ,, "));
    }

    [Fact]
    public void Duplicate_keys_are_removed_keeping_the_first_occurrence_in_request_order()
    {
        Assert.Equal(new[] { "V-3", "V-1", "V-2" }, Keys("V-3,V-1,V-3,V-2,V-1"));
    }

    [Fact]
    public void Duplicates_count_once_toward_the_minimum()
    {
        Assert.Equal(VendorComparisonService.TooFewMessage, Reject("V-1, V-1 ,V-1"));
    }

    [Fact]
    public void Two_to_five_distinct_keys_are_accepted()
    {
        Assert.Equal(2, Keys("V-1,V-2").Count);
        Assert.Equal(5, Keys("V-1,V-2,V-3,V-4,V-5").Count);
    }

    [Fact]
    public void Fewer_than_two_distinct_keys_are_refused_with_the_minimum_stated()
    {
        Assert.Equal(VendorComparisonService.TooFewMessage, Reject(null));
        Assert.Equal(VendorComparisonService.TooFewMessage, Reject(""));
        Assert.Equal(VendorComparisonService.TooFewMessage, Reject("V-1"));
    }

    [Fact]
    public void More_than_five_distinct_keys_are_refused_with_the_maximum_stated()
    {
        Assert.Equal(VendorComparisonService.TooManyMessage, Reject("V-1,V-2,V-3,V-4,V-5,V-6"));
    }

    [Fact]
    public void The_unknown_vendor_key_is_refused_even_among_valid_keys()
    {
        Assert.Equal(VendorComparisonService.UnknownVendorMessage, Reject("V-1,~unknown"));
        Assert.Equal(VendorComparisonService.UnknownVendorMessage, Reject(" ~unknown , V-2 "));
    }

    [Fact]
    public void The_period_is_parsed_by_JalaliPeriod()
    {
        var ok = JalaliPeriod.TryParse("month", "1405-08", null, null, Today, out var period, out var error);

        Assert.True(ok, error);
        Assert.Equal(14050801, period!.FromKey);
        // Month 1405-08 is trimmed to today (1405-08-25), as the spec requires for periods reaching past today.
        Assert.Equal(14050825, period.ToKey);
        Assert.Equal(new JalaliDate(1405, 8, 25), period.TrimmedTo);
    }

    [Fact]
    public void An_invalid_period_is_refused_by_JalaliPeriod()
    {
        var ok = JalaliPeriod.TryParse("month", "1405-13", null, null, Today, out var period, out var error);

        Assert.False(ok);
        Assert.Null(period);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
