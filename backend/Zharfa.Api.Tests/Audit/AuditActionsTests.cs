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
        "ai.question", "report.exported", "saved_report.saved", "saved_report.replaced",
        "saved_report.opened", "saved_report.deleted",
    ];

    private static readonly string[] ExpectedReportKeys =
    [
        "overview", "vendors", "vendor-profile", "by-vendor", "by-product", "by-category", "trend",
        "top-products", "returns", "transactions", "vendors-compare", "drill-down", "drill-down-lines",
        "generated",
    ];

    [Fact]
    public void All_contains_every_code_of_the_catalog()
    {
        var codes = AuditActions.All.Select(entry => entry.Code).ToArray();

        Assert.Equal(28, codes.Length);
        Assert.Equal(ExpectedCodes.OrderBy(code => code), codes.OrderBy(code => code));
        Assert.Equal("پرسش از دستیار هوشمند", AuditActions.Label("ai.question"));
        Assert.Equal("خروجی اکسل", AuditActions.Label("report.exported"));
        Assert.Equal("ذخیره گزارش", AuditActions.Label("saved_report.saved"));
        Assert.Equal("جایگزینی گزارش ذخیره شده", AuditActions.Label("saved_report.replaced"));
        Assert.Equal("باز کردن گزارش ذخیره شده", AuditActions.Label("saved_report.opened"));
        Assert.Equal("حذف گزارش ذخیره شده", AuditActions.Label("saved_report.deleted"));
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
    public void Report_keys_are_the_fourteen_keys_with_persian_names()
    {
        Assert.Equal(ExpectedReportKeys.OrderBy(key => key), ReportKeys.All.OrderBy(key => key));
        Assert.Equal("گزارش تولیدشده", ReportKeys.Name(ReportKeys.Generated));

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
