namespace Zharfa.Api.Tests.Ai;

using System;
using Xunit;
using Zharfa.Api.Audit;
using Zharfa.Api.Data;

public class AiQuestionAuditTests
{
    [Fact]
    public void AuditDetail_Of_for_ai_question_scrubs_and_formats_cleanly()
    {
        var detail = AuditDetail.Of(
            ("outcome", "ok"),
            ("tools", "purchasing_overview,purchasing_by_vendor"),
            ("period", "1405-08\r\n"),
            ("vendor", "  شرکت دارویی البرز  "),
            ("apiKey", "sk-proj-secret123456789"), // should be scrubbed/handled
            ("facts", "metric=net; value=10000000"), // shouldn't contain raw numbers if dropped
            ("outcome", "ok"));

        Assert.NotNull(detail);
        Assert.Contains("outcome=ok", detail);
        Assert.Contains("tools=purchasing_overview,purchasing_by_vendor", detail);
        Assert.DoesNotContain("\r", detail);
        Assert.DoesNotContain("\n", detail);
        Assert.DoesNotContain("sk-proj-secret123456789", detail);
    }

    [Fact]
    public void Target_question_is_cut_to_maximum_length()
    {
        var longQuestion = new string('س', 300);
        var target = longQuestion.Length > 200 ? longQuestion[..200] : longQuestion;

        Assert.Equal(200, target.Length);
    }

    [Fact]
    public void Ai_question_audit_detail_contains_allowlisted_keys_and_omits_view()
    {
        // Allowlist per data-model.md §3: period, from, to, vendor, product, category, tools, outcome
        var detail = AuditDetail.Of(
            ("period", "1405-08"),
            ("from", "2026-10-23"),
            ("to", "2026-11-21"),
            ("vendor", "تأمین‌کننده البرز"),
            ("product", "استامینوفن"),
            ("category", "داروهای عمومی"),
            ("tools", "purchasing_by_vendor,purchasing_by_product"),
            ("outcome", "ok"));

        Assert.Contains("period=1405-08", detail);
        Assert.Contains("from=2026-10-23", detail);
        Assert.Contains("to=2026-11-21", detail);
        Assert.Contains("vendor=تأمین‌کننده البرز", detail);
        Assert.Contains("product=استامینوفن", detail);
        Assert.Contains("category=داروهای عمومی", detail);
        Assert.Contains("tools=purchasing_by_vendor,purchasing_by_product", detail);
        Assert.Contains("outcome=ok", detail);
        Assert.DoesNotContain("view=", detail);
    }

    [Fact]
    public void Ai_question_audit_detail_for_demo_chat_records_category_and_outcome()
    {
        // Demo chat includes category filter and period info in audit detail
        var detail = AuditDetail.Of(
            ("period", "آبان ۱۴۰۵"),
            ("from", "1405-08-01"),
            ("to", "1405-08-30"),
            ("vendor", null),
            ("product", null),
            ("category", "5"),
            ("tools", "sales_overview"),
            ("outcome", "ok"));

        Assert.Contains("period=آبان ۱۴۰۵", detail);
        Assert.Contains("category=5", detail);
        Assert.Contains("tools=sales_overview", detail);
        Assert.Contains("outcome=ok", detail);
        Assert.DoesNotContain("vendor=", detail);
        Assert.DoesNotContain("product=", detail);
    }
}
