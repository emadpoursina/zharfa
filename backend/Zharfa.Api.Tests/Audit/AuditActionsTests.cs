namespace Zharfa.Api.Tests.Audit;

using System.Linq;
using Zharfa.Api.Audit;

/// <summary>
/// T012 (data-model §2.2): the action catalog holds every code with a label, and the report-key catalog holds the
/// thirteen keys with Persian names. Pure.
/// </summary>
public class AuditActionsTests
{
    private static readonly string[] ExpectedCodes =
    [
        "auth.signin.succeeded", "auth.signin.failed", "auth.signout", "auth.locked", "auth.password_changed",
        "user.created", "user.role_changed", "user.deactivated", "user.reactivated", "user.password_reset",
        "user.unlocked", "report.opened", "report.printed", "access.denied", "audit.viewed",
        "data_quality.viewed", "sync.requested", "sync.rejected", "provider.status.viewed",
        "reconciliation.run", "reconciliation.mismatch", "settings.ai_provider.changed",
    ];

    private static readonly string[] ExpectedReportKeys =
    [
        "overview", "vendors", "vendor-profile", "by-vendor", "by-product", "by-category", "trend",
        "top-products", "returns", "transactions", "vendors-compare", "drill-down", "drill-down-lines",
    ];

    [Fact]
    public void All_contains_every_code_of_the_catalog()
    {
        var codes = AuditActions.All.Select(entry => entry.Code).ToArray();

        Assert.Equal(22, codes.Length);
        Assert.Equal(ExpectedCodes.OrderBy(code => code), codes.OrderBy(code => code));
    }

    [Fact]
    public void Codes_are_unique()
    {
        var codes = AuditActions.All.Select(entry => entry.Code).ToArray();

        Assert.Equal(codes.Length, codes.Distinct().Count());
    }

    [Fact]
    public void Every_label_is_non_empty()
    {
        foreach (var entry in AuditActions.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Label), entry.Code);
        }
    }

    [Fact]
    public void An_unknown_code_is_its_own_label()
    {
        Assert.Equal("unknown.code", AuditActions.Label("unknown.code"));
    }

    [Fact]
    public void Report_keys_are_the_thirteen_keys_with_persian_names()
    {
        Assert.Equal(ExpectedReportKeys.OrderBy(key => key), ReportKeys.All.OrderBy(key => key));

        foreach (var key in ReportKeys.All)
        {
            var name = ReportKeys.Name(key);
            Assert.False(string.IsNullOrWhiteSpace(name), key);
            Assert.NotEqual(key, name);
        }
    }

    [Fact]
    public void An_unknown_report_key_is_its_own_name()
    {
        Assert.Equal("unknown-report", ReportKeys.Name("unknown-report"));
    }
}
