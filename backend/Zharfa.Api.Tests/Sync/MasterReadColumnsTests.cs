namespace Zharfa.Api.Tests.Sync;

using System.Text.Json.Nodes;
using Zharfa.Api.Mabna.Sync;

/// <summary>
/// Phase 2 optional master columns (vendor code, product drug group key): the master read asks for
/// them when the source map has them usable, and a missing optional column never breaks the read.
/// </summary>
public class MasterReadColumnsTests
{
    private static readonly string ExampleMapPath =
        Path.Combine(AppContext.BaseDirectory, "example", "purchase-source-map.example.json");

    private static SourceMap ExampleMap()
    {
        var result = SourceMap.LoadFromFile(ExampleMapPath);
        Assert.True(result.IsValid, string.Join("; ", result.Problems.Select(p => p.Code)));
        return result.Map!;
    }

    private static SourceRow Row(params (string Column, object? Value)[] values) =>
        SourceRow.Create(values.Select(v => v.Column).ToList(), values.Select(v => v.Value).ToList());

    [Fact]
    public void Master_read_asks_for_vendor_code_and_product_category_key_when_usable()
    {
        // Demo mode: the example map's candidate vendor.code and product.categoryKey are usable.
        var mapping = new PurchaseMapping(ExampleMap(), isProduction: false);

        Assert.Equal("SimVendorCode", mapping.VendorCode);
        Assert.Equal("SimDrugGroupID", mapping.ProductCategoryKey);
        Assert.Contains("SimVendorCode", mapping.Vendor.Columns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("SimDrugGroupID", mapping.Product.Columns, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Master_read_does_not_ask_for_candidate_optional_columns_in_production()
    {
        // Production: the required master entities are verified (confirmed), but the optional
        // columns (vendor code, product drug group key) are still candidate, so they are left out
        // of the master read. The constructor must not throw: only a required entity can refuse it.
        var mapping = new PurchaseMapping(ProductionExampleMap(), isProduction: true);

        Assert.Null(mapping.VendorCode);
        Assert.Null(mapping.ProductCategoryKey);
        Assert.DoesNotContain("SimVendorCode", mapping.Vendor.Columns, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("SimDrugGroupID", mapping.Product.Columns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("VendorID", mapping.Vendor.Columns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ProductID", mapping.Product.Columns, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The example map with every candidate status confirmed (a production-verified map), except the
    /// optional vendor.code and product.categoryKey columns, which stay candidate, and the optional
    /// productCategory entity, which is left as is. Edits the in-memory JSON only; the example file is untouched.
    /// </summary>
    private static SourceMap ProductionExampleMap()
    {
        var root = JsonNode.Parse(File.ReadAllText(ExampleMapPath))!.AsObject();
        var entities = root["entities"]!.AsObject();
        foreach (var name in entities.Select(p => p.Key).ToList())
        {
            if (name != "productCategory")
            {
                ConfirmCandidates(entities[name]);
            }
        }

        entities["vendor"]!["code"]!["status"] = "candidate";
        entities["product"]!["categoryKey"]!["status"] = "candidate";

        var result = SourceMap.Parse(root.ToJsonString());
        Assert.True(result.IsValid, string.Join("; ", result.Problems.Select(p => p.Code)));
        return result.Map!;
    }

    private static void ConfirmCandidates(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (key == "status" && obj[key] is JsonValue value &&
                        value.TryGetValue<string>(out var status) && status == "candidate")
                    {
                        obj[key] = "confirmed";
                    }
                    else
                    {
                        ConfirmCandidates(obj[key]);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    ConfirmCandidates(item);
                }

                break;
        }
    }

    [Fact]
    public void Missing_optional_vendor_code_does_not_break_the_master_read()
    {
        var map = ExampleMap();
        map.Vendor!.Code = null;

        var mapping = new PurchaseMapping(map, isProduction: false);
        var mapper = new PurchaseRowMapper(mapping);

        Assert.Null(mapping.VendorCode);
        Assert.DoesNotContain("SimVendorCode", mapping.Vendor.Columns, StringComparer.OrdinalIgnoreCase);

        var mapped = mapper.MapVendor(Row(("VendorID", "V-1"), ("VendorName", "شرکت علی")));
        Assert.Null(mapped.RejectReason);
        Assert.Equal("V-1", mapped.Record!.SourceKey);
        Assert.Null(mapped.Record.Code);
    }

    [Fact]
    public void Missing_optional_product_category_key_does_not_break_the_master_read()
    {
        var map = ExampleMap();
        map.Product!.CategoryKey = null;

        var mapping = new PurchaseMapping(map, isProduction: false);
        var mapper = new PurchaseRowMapper(mapping);

        Assert.Null(mapping.ProductCategoryKey);
        Assert.DoesNotContain("SimDrugGroupID", mapping.Product.Columns, StringComparer.OrdinalIgnoreCase);

        var mapped = mapper.MapProduct(Row(("ProductID", "P-1"), ("ProductName", "قرص")));
        Assert.Null(mapped.RejectReason);
        Assert.Equal("P-1", mapped.Record!.SourceKey);
        Assert.Null(mapped.Record.CategoryKey);
    }

    [Fact]
    public void Vendor_code_and_product_category_key_are_mapped_from_the_read_row()
    {
        var mapper = new PurchaseRowMapper(new PurchaseMapping(ExampleMap(), isProduction: false));

        var vendor = mapper.MapVendor(Row(("VendorID", "V-1"), ("VendorName", "شرکت"), ("SimVendorCode", " 0042 ")));
        Assert.Equal("0042", vendor.Record!.Code);

        var product = mapper.MapProduct(Row(("ProductID", "P-1"), ("ProductName", "قرص"), ("SimDrugGroupID", 7L)));
        Assert.Equal("7", product.Record!.CategoryKey);
    }
}
