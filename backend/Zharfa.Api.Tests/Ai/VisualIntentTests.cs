namespace Zharfa.Api.Tests.Ai;

using Xunit;
using Zharfa.Api.Ai;

/// <summary>
/// T014 (spec 007 US1, research R1–R2): VisualIntent token-based classifier.
/// Decides whether a purchasing answer should include a server-built table/chart.
/// Pure: no database, no LLM.
/// </summary>
public class VisualIntentTests
{
    [Theory]
    [InlineData("پرمصرف‌ترین کالاها از نظر خالص خرید در این ماه کدام‌اند؟", "purchasing_top_products", true)]
    [InlineData("کدام تأمین‌کننده در این ماه بیشترین خالص خرید را داشته است؟", "purchasing_overview", true)]
    [InlineData("کدام کالاها بیشترین سهم از خرید را داشته‌اند؟", "purchasing_top_products", true)]
    [InlineData("۱۰ کالای برتر خریداری شده را نمایش بده", "purchasing_top_products", true)]
    [InlineData("ده تا از پرمصرف‌ترین کالاها", "purchasing_top_products", true)]
    [InlineData("رتبه بندی تامین کنندگان برتر", "purchasing_by_vendor", true)]
    [InlineData("مقایسه خرید از سه تامین کننده اول", "purchasing_by_vendor", true)]
    [InlineData("Compare the top 10 purchased products over the last six months", "purchasing_top_products", true)]
    [InlineData("مقایسه ۱۰ کالای پرخرید در شش ماه گذشته", "purchasing_top_products", true)]
    [InlineData("top 10 products comparison", "purchasing_top_products", true)]
    public void IsListVisual_is_true_for_ranking_and_comparison_questions(string question, string mappedTool, bool expected)
    {
        var norm = PurchasingQuestionRouter.Normalize(question);
        var actual = VisualIntent.IsListVisual(norm, mappedTool);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("خالص خرید این ماه چقدر است؟", "purchasing_overview")] // Fixture 1
    [InlineData("خالص خرید این ماه نسبت به ماه قبل چه تغییری کرده است؟", "purchasing_overview")] // Fixture 2
    [InlineData("روند ماهانه خالص خرید در دوازده ماه گذشته چگونه بوده است؟", "purchasing_trend")] // Fixture 3 (no مقایسه)
    [InlineData("تفکیک خرید بر اساس گروه‌های اصلی کالا در این ماه را نشان بده.", "purchasing_by_category")] // Fixture 6 (no ranking)
    [InlineData("مبلغ کل برگشت از خرید این ماه چقدر بوده است؟", "purchasing_overview")] // Fixture 7
    [InlineData("خالص خرید ماه جاری نسبت به میانگین چه وضعیتی دارد؟", "purchasing_overview")] // Fixture 11
    public void IsListVisual_is_false_for_non_ranking_and_single_kpi_questions(string question, string mappedTool)
    {
        var norm = PurchasingQuestionRouter.Normalize(question);
        var actual = VisualIntent.IsListVisual(norm, mappedTool);

        Assert.False(actual);
    }

    [Theory]
    [InlineData("فروش داروخانه‌ها چقدر بوده است؟", null)] // out-of-domain-sales
    [InlineData("موجودی انبار و کسری کالا", null)] // out-of-domain-inventory
    [InlineData("مانده حساب و بدهی تامین کننده", null)] // vendor-financials
    [InlineData("select * from mabna host_id()", null)] // mabna-bypass
    public void IsListVisual_is_false_for_out_of_domain_and_null_tool(string question, string? mappedTool)
    {
        var norm = PurchasingQuestionRouter.Normalize(question);
        var actual = VisualIntent.IsListVisual(norm, mappedTool);

        Assert.False(actual);
    }
}
