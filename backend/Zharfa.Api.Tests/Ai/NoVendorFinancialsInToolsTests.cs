namespace Zharfa.Api.Tests.Ai;

using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Zharfa.Api.Ai;

public class NoVendorFinancialsInToolsTests
{
    private static readonly string[] ForbiddenTokens =
    [
        "balance", "payment", "debt", "credit", "paid", "cheque"
    ];

    [Fact]
    public void Tool_specs_contain_no_vendor_financial_names_or_properties()
    {
        var specs = PurchasingAnalystTools.GetToolSpecs();

        foreach (var spec in specs)
        {
            foreach (var token in ForbiddenTokens)
            {
                Assert.DoesNotContain(token, spec.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(token, spec.Description, StringComparison.OrdinalIgnoreCase);
            }

            var json = JsonSerializer.Serialize(spec.Parameters);
            foreach (var token in ForbiddenTokens)
            {
                Assert.DoesNotContain(token, json, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void PurchasingAnalystTools_properties_contain_no_vendor_financial_tokens()
    {
        var type = typeof(PurchasingAnalystTools);
        var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

        foreach (var member in members)
        {
            foreach (var token in ForbiddenTokens)
            {
                Assert.False(
                    member.Name.Contains(token, StringComparison.OrdinalIgnoreCase),
                    $"Member '{member.Name}' in PurchasingAnalystTools contains forbidden token '{token}'");
            }
        }
    }

    [Fact]
    public void Representative_purchasing_tool_JSON_contains_no_financial_tokens_with_digits()
    {
        var sampleOverviewPayload = new
        {
            view = "purchasing_overview",
            period = "1405-08",
            totals = new
            {
                net = "12500000000",
                gross = "13000000000",
                returns = "500000000",
                vat = "1125000000"
            },
            activeVendors = 15,
            items = new[]
            {
                new { name = "تأمین‌کننده البرز", net = "4500000000" },
                new { name = "داروسازی تهران", net = "3200000000" }
            }
        };

        var json = JsonSerializer.Serialize(sampleOverviewPayload);

        foreach (var token in ForbiddenTokens)
        {
            Assert.DoesNotContain(token, json, StringComparison.OrdinalIgnoreCase);
            var regex = new Regex($@"{token}[^\d]{{0,15}}\d+", RegexOptions.IgnoreCase);
            Assert.False(regex.IsMatch(json), $"JSON contains financial token '{token}' adjacent to digits");
        }
    }
}
