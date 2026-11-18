namespace Zharfa.Api.Tests.Security;

using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Zharfa.Api.Controllers;
using Zharfa.Api.Platform;

/// <summary>
/// T056 (research R15, contracts/api.md §5): the reserved vendor-financials endpoints answer the "data unavailable"
/// body in both data modes, with no figure anywhere in the JSON. Pure: the controller is built directly.
/// </summary>
public class VendorFinancialsTests
{
    private static readonly DataModeOptions Demo = new("demo");
    private static readonly DataModeOptions Production = new("production");

    [Fact]
    public void Summary_in_demo_is_the_unavailable_body_with_the_demo_label()
    {
        AssertUnavailable(new VendorFinancialsController(Demo).Summary(), "demo");
    }

    [Fact]
    public void Summary_in_production_is_the_unavailable_body_with_the_real_label()
    {
        AssertUnavailable(new VendorFinancialsController(Production).Summary(), "real");
    }

    [Fact]
    public void Vendor_is_the_unavailable_body_for_any_key()
    {
        AssertUnavailable(new VendorFinancialsController(Demo).Vendor("SimVendor-1"), "demo");
        AssertUnavailable(new VendorFinancialsController(Production).Vendor("SimVendor-1"), "real");
    }

    private static void AssertUnavailable(IActionResult result, string dataLabel)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var root = document.RootElement;

        Assert.Equal(
            new[] { "available", "reason", "message", "dataLabel" },
            root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.False(root.GetProperty("available").GetBoolean());
        Assert.Equal("not-available-until-phase-6", root.GetProperty("reason").GetString());
        Assert.Equal("داده در دسترس نیست.", root.GetProperty("message").GetString());
        Assert.Equal(dataLabel, root.GetProperty("dataLabel").GetString());

        // No figure: no JSON number anywhere in the body.
        Assert.DoesNotContain(
            root.EnumerateObject().Select(property => property.Value.ValueKind),
            kind => kind == JsonValueKind.Number);
    }
}
