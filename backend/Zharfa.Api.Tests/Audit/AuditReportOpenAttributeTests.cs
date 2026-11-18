namespace Zharfa.Api.Tests.Audit;

using Microsoft.AspNetCore.Routing;
using Zharfa.Api.Audit;

/// <summary>
/// T062 (research R9): the pure helpers of the report-open attribute. The key of a request is its route value or the
/// literal template, and only a 2xx status is audited. Pure: no HTTP server.
/// </summary>
public class AuditReportOpenAttributeTests
{
    [Fact]
    public void Route_placeholder_resolves_to_the_route_value()
    {
        var values = new RouteValueDictionary(new { reportId = "by-vendor" });

        Assert.Equal("by-vendor", AuditReportOpenAttribute.ResolveKey("{reportId}", values));
    }

    [Fact]
    public void Literal_template_resolves_to_itself()
    {
        Assert.Equal("overview", AuditReportOpenAttribute.ResolveKey("overview", new RouteValueDictionary()));
    }

    [Fact]
    public void Missing_route_value_resolves_to_null_and_nothing_is_audited()
    {
        Assert.Null(AuditReportOpenAttribute.ResolveKey("{reportId}", new RouteValueDictionary()));
    }

    [Fact]
    public void Empty_route_value_resolves_to_null()
    {
        var values = new RouteValueDictionary(new { reportId = string.Empty });

        Assert.Null(AuditReportOpenAttribute.ResolveKey("{reportId}", values));
    }

    [Theory]
    [InlineData(199, false)]
    [InlineData(200, true)]
    [InlineData(204, true)]
    [InlineData(299, true)]
    [InlineData(300, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    [InlineData(503, false)]
    public void Only_2xx_statuses_are_successes(int status, bool expected)
    {
        Assert.Equal(expected, AuditReportOpenAttribute.IsSuccess(status));
    }
}
