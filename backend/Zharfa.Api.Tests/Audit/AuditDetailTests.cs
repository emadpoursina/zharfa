namespace Zharfa.Api.Tests.Audit;

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Primitives;
using Zharfa.Api.Audit;

/// <summary>
/// T010 (data-model §2.1, §2.3, FR-016): report detail keeps only the allowlisted keys, trims and cuts values,
/// cuts the whole text to 500 characters and scrubs secrets. Pure: no database, no network.
/// </summary>
public class AuditDetailTests
{
    [Fact]
    public void ForReport_keeps_only_allowlisted_keys_sorted_and_joined()
    {
        var query = new Dictionary<string, StringValues>
        {
            ["vendor"] = "V1",
            ["period"] = "1405-01",
            ["page"] = "2",
            ["pageSize"] = "50",
            ["sort"] = "name",
            ["dir"] = "asc",
            ["password"] = "hunter2",
        };

        Assert.Equal("period=1405-01; vendor=V1", AuditDetail.ForReport(query));
    }

    [Fact]
    public void ForReport_appends_extra_pairs()
    {
        var query = new Dictionary<string, StringValues> { ["period"] = "1405-01" };

        Assert.Equal(
            "documentId=42; period=1405-01",
            AuditDetail.ForReport(query, new[] { ("documentId", (string?)"42") }));
    }

    [Fact]
    public void Of_trims_and_removes_line_breaks_from_values()
    {
        Assert.Equal("q=ab", AuditDetail.Of(("q", "a\r\nb  ")));
    }

    [Fact]
    public void Of_cuts_each_value_to_100_characters()
    {
        var detail = AuditDetail.Of(("q", new string('x', 150)));

        Assert.NotNull(detail);
        Assert.Equal(102, detail!.Length);
    }

    [Fact]
    public void Of_cuts_the_whole_text_to_500_characters()
    {
        var pairs = Enumerable.Range(0, 10)
            .Select(index => ($"k{index}", (string?)new string('y', 100)))
            .ToArray();

        var detail = AuditDetail.Of(pairs);

        Assert.NotNull(detail);
        Assert.True(detail!.Length <= 500, $"detail length {detail.Length} exceeds 500");
    }

    [Fact]
    public void Of_scrubs_secret_values_before_they_are_returned()
    {
        var detail = AuditDetail.Of(("q", "password=abc"));

        Assert.NotNull(detail);
        Assert.DoesNotContain("abc", detail);
        Assert.Contains("[REDACTED]", detail);
    }

    [Fact]
    public void Of_scrubs_bearer_tokens()
    {
        var detail = AuditDetail.Of(("q", "bearer=xyz"));

        Assert.NotNull(detail);
        Assert.DoesNotContain("xyz", detail);
    }

    [Fact]
    public void Empty_input_gives_null()
    {
        Assert.Null(AuditDetail.Of());
        Assert.Null(AuditDetail.Of(("q", "   ")));
    }

    [Fact]
    public void ReportViewParams_returns_only_the_present_paging_and_sorting_pairs()
    {
        var query = new Dictionary<string, StringValues>
        {
            ["page"] = " 3 ",
            ["pageSize"] = string.Empty,
            ["sort"] = "name",
            ["dir"] = "  ",
            ["period"] = "1405-01",
        };

        var view = AuditDetail.ReportViewParams(query);

        Assert.Equal(new[] { "page", "sort" }, view.Keys.OrderBy(key => key));
        Assert.Equal("3", view["page"]);
        Assert.Equal("name", view["sort"]);
    }

    [Fact]
    public void ReportSignatureParams_holds_the_scope_and_never_the_view_parameters()
    {
        var query = new Dictionary<string, StringValues>
        {
            ["period"] = "1405-01",
            ["vendor"] = "V1",
            ["page"] = "2",
            ["pageSize"] = "50",
            ["sort"] = "name",
            ["dir"] = "asc",
        };

        var signature = AuditDetail.ReportSignatureParams(query);

        Assert.Equal(new[] { "period", "vendor" }, signature.Keys.OrderBy(key => key));
        Assert.DoesNotContain("page", signature.Keys);
        Assert.DoesNotContain("pageSize", signature.Keys);
        Assert.DoesNotContain("sort", signature.Keys);
        Assert.DoesNotContain("dir", signature.Keys);
    }
}
