namespace Zharfa.Api.Tests.Ai;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zharfa.Api.Ai;

public class PurchasingToolAllowlistTests
{
    private static readonly string[] ExpectedTools =
    [
        "purchasing_overview",
        "purchasing_by_vendor",
        "purchasing_by_product",
        "purchasing_by_category",
        "purchasing_trend",
        "purchasing_top_products",
        "purchasing_returns",
        "purchasing_transactions_summary"
    ];

    [Fact]
    public void Allowlist_is_exactly_the_eight_approved_tools()
    {
        var actual = PurchasingAnalystTools.AllowlistedToolNames.OrderBy(t => t).ToArray();
        var expected = ExpectedTools.OrderBy(t => t).ToArray();

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("balance")]
    [InlineData("payment")]
    [InlineData("vendor_balance")]
    [InlineData("cheque")]
    [InlineData("sales_overview")]
    [InlineData("receivables")]
    [InlineData("inventory")]
    [InlineData("inventory_alerts")]
    public void Disallowed_tool_names_are_absent_from_allowlist(string disallowed)
    {
        Assert.False(
            PurchasingAnalystTools.AllowlistedToolNames.Any(tool => tool.Contains(disallowed, StringComparison.OrdinalIgnoreCase)),
            $"Tool name containing '{disallowed}' must not be in PurchasingAnalystTools allowlist.");
    }

    [Fact]
    public void Limits_and_timeouts_match_specification()
    {
        Assert.Equal(3, PurchasingAnalystTools.MAX_TOOL_CALLS_PER_CHAT);
        Assert.Equal(10, PurchasingAnalystTools.PER_CALL_TIMEOUT_SECONDS);
        Assert.Equal(100, PurchasingAnalystTools.MAX_ROWS);
        Assert.Equal(30, PurchasingAnalystTools.WHOLE_ANSWER_BUDGET_SECONDS);
    }

    [Fact]
    public async Task Unknown_tool_name_is_refused_with_unsuccessful_result()
    {
        // PurchasingAnalystTools with null dependencies (unknown tool check happens before service calls)
        var tools = new PurchasingAnalystTools(null!, null!, null!, null!, null!, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<PurchasingAnalystTools>.Instance);

        var call = new AiToolCall("call_1", "arbitrary_sql_tool", "{}");
        var result = await tools.ExecuteAsync(call, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.View);
        Assert.Contains("unavailable", result.ContentJson);
    }
}
