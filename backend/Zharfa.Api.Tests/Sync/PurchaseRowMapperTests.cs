namespace Zharfa.Api.Tests.Sync;

using System.Globalization;
using System.Text.Json.Nodes;
using Zharfa.Api.Analytics;
using Zharfa.Api.Mabna.Sync;

/// <summary>T031: row mapping rules (pure logic) on the committed example map, demo mode.</summary>
public class PurchaseRowMapperTests
{
    private static readonly string ExampleMapPath =
        Path.Combine(AppContext.BaseDirectory, "example", "purchase-source-map.example.json");

    private static SourceMap ExampleMap()
    {
        var result = SourceMap.LoadFromFile(ExampleMapPath);
        Assert.True(result.IsValid, string.Join("; ", result.Problems.Select(p => p.Code)));
        return result.Map!;
    }

    /// <summary>Demo mode: candidate items are usable, as the simulator's map is.</summary>
    private static PurchaseRowMapper DemoMapper() => new(new PurchaseMapping(ExampleMap(), isProduction: false));

    private static SourceRow Row(params (string Column, object? Value)[] values) =>
        SourceRow.Create(values.Select(v => v.Column).ToList(), values.Select(v => v.Value).ToList());

    private static SourceRow VendorRow(object? key, object? name) => Row(("VendorID", key), ("VendorName", name));

    private static SourceRow VendorWithCodeRow(object? key, object? name, object? code) =>
        Row(("VendorID", key), ("VendorName", name), ("SimVendorCode", code));

    private static SourceRow ProductRow(object? key, object? name, object? categoryKey) =>
        Row(("ProductID", key), ("ProductName", name), ("SimDrugGroupID", categoryKey));

    private static SourceRow CategoryRow(object? key, object? name) =>
        Row(("DrugGroupID", key), ("DrugGroupName", name));

    private static SourceRow InvoiceRow(object? key, object? date, object? cancelled = null, object? vendor = null) =>
        Row(("DocID", key), ("SimDocNumber", "F-1"), ("VendorID", vendor ?? "V-1"), ("SimDocDate", date), ("IsCanceled", cancelled ?? 0));

    private static SourceRow LineRow(object? docKey, object? gross, object? discount = null, object? key = null) =>
        Row(
            ("ItemID", key ?? 1L),
            ("DocID", docKey),
            ("PrdID", "P-1"),
            ("SimQuantity", 2m),
            ("SimLineAmount", gross),
            ("Disc", discount ?? 0m),
            ("Vat", 9m));

    [Fact]
    public void Arabic_yeh_and_kaf_are_normalized_so_names_differing_only_by_them_are_equal()
    {
        // Arabic kaf U+0643 and Arabic yeh U+064A, then the Persian forms U+06A9 and U+06CC.
        var arabicName = "شركت علي";
        var persianName = "شرکت علی";

        var mapper = DemoMapper();
        var arabic = mapper.MapVendor(VendorRow("V-1", arabicName)).Record!;
        var persian = mapper.MapVendor(VendorRow("V-2", persianName)).Record!;

        Assert.Equal(persianName, arabic.NameFa);
        Assert.Equal(arabic.NameFa, persian.NameFa);
    }

    [Fact]
    public void Zero_width_non_joiner_is_kept_and_whitespace_is_collapsed()
    {
        var mapper = DemoMapper();
        var name = "می‌خواهم   دارو";
        var record = mapper.MapVendor(VendorRow("V-1", name)).Record!;

        Assert.Equal("می‌خواهم دارو", record.NameFa);
    }

    [Fact]
    public void Empty_name_becomes_the_placeholder()
    {
        var record = DemoMapper().MapVendor(VendorRow("V-1", "   ")).Record!;

        Assert.Equal("(بدون نام)", record.NameFa);
    }

    [Fact]
    public void Missing_key_rejects_the_row_with_a_reason_code()
    {
        var result = DemoMapper().MapVendor(VendorRow(null, "نام"));

        Assert.Null(result.Record);
        Assert.Equal(PurchaseRowMapper.KeyMissing, result.RejectReason);
    }

    [Fact]
    public void Jalali_text_date_maps_to_jalali_month_and_day()
    {
        var result = DemoMapper().MapInvoice(InvoiceRow("I-1", "1403/12/30"));

        Assert.NotNull(result.Record);
        Assert.Equal("invoice", result.Record!.DocType);
        Assert.Equal(1403, result.Record.JalaliYear);
        Assert.Equal(12, result.Record.JalaliMonth);
        Assert.Equal(30, result.Record.JalaliDay);
    }

    [Fact]
    public void Persian_digit_date_and_first_farvardin_map_correctly()
    {
        var mapper = DemoMapper();
        var persian = mapper.MapReturn(Row(("DocID", "R-1"), ("VendorID", "V-1"), ("SimDocDate", "۱۴۰۴/۰۱/۰۱"))).Record;
        Assert.NotNull(persian);
        Assert.Equal("return", persian!.DocType);
        Assert.Equal((1404, 1, 1), (persian.JalaliYear, persian.JalaliMonth, persian.JalaliDay));
    }

    [Fact]
    public void Esfand_30_of_a_non_leap_year_is_rejected()
    {
        Assert.False(JalaliDate.IsLeapYear(1404), "fixture assumption: 1404 is not a leap year");

        var result = DemoMapper().MapInvoice(InvoiceRow("I-2", "1404/12/30"));

        Assert.Null(result.Record);
        Assert.Equal(PurchaseRowMapper.DateInvalid, result.RejectReason);
    }

    [Fact]
    public void Cancelled_rule_compares_the_header_value_with_the_verified_value()
    {
        var mapper = DemoMapper();

        Assert.True(mapper.MapInvoice(InvoiceRow("I-1", "1403/12/30", cancelled: 1)).Record!.Cancelled);
        Assert.True(mapper.MapInvoice(InvoiceRow("I-1", "1403/12/30", cancelled: "1")).Record!.Cancelled);
        Assert.False(mapper.MapInvoice(InvoiceRow("I-1", "1403/12/30", cancelled: 0)).Record!.Cancelled);
        Assert.False(mapper.MapInvoice(InvoiceRow("I-1", "1403/12/30", cancelled: null)).Record!.Cancelled);
    }

    [Fact]
    public void Amount_sign_asIs_negate_and_abs_are_applied_as_the_map_states()
    {
        Assert.Equal(5m, PurchaseRowMapper.ApplySign(5m, "asIs"));
        Assert.Equal(-5m, PurchaseRowMapper.ApplySign(5m, "negate"));
        Assert.Equal(5m, PurchaseRowMapper.ApplySign(-5m, "abs"));
    }

    [Fact]
    public void Line_gross_is_made_positive_by_the_map_abs_sign()
    {
        var result = DemoMapper().MapLine(LineRow("I-1", -1234.5m));

        Assert.NotNull(result.Record);
        Assert.Equal(1234.5m, result.Record!.GrossAmount);
    }

    [Fact]
    public void Decimal_amounts_stay_exact_and_accept_persian_digits_and_thousands_separators()
    {
        var mapper = DemoMapper();
        var fromText = mapper.MapLine(LineRow("I-1", "۱٬۲۳۴٫۵۶۷۸")).Record!;
        var fromDouble = mapper.MapLine(LineRow("I-1", 0.1d)).Record!;

        Assert.Equal(1234.5678m, fromText.GrossAmount);
        Assert.Equal(0.1m, fromDouble.GrossAmount);
    }

    [Fact]
    public void Gross_below_line_discount_is_kept_and_reported_as_a_warning_not_a_rejection()
    {
        var result = DemoMapper().MapLine(LineRow("I-1", 100m, discount: 150m));

        Assert.NotNull(result.Record);
        Assert.Null(result.RejectReason);
        Assert.Equal(PurchaseRowMapper.DiscountExceedsGross, result.Warning);
    }

    [Fact]
    public void Negative_line_discount_is_rejected()
    {
        var result = DemoMapper().MapLine(LineRow("I-1", 100m, discount: -1m));

        Assert.Null(result.Record);
        Assert.Equal(PurchaseRowMapper.DiscountNegative, result.RejectReason);
    }

    [Fact]
    public void Unparseable_amount_is_rejected_and_never_thrown()
    {
        var result = DemoMapper().MapLine(LineRow("I-1", "abc"));

        Assert.Null(result.Record);
        Assert.Equal(PurchaseRowMapper.AmountInvalid, result.RejectReason);
    }

    [Fact]
    public void Line_without_a_document_key_is_rejected_with_its_own_code()
    {
        var result = DemoMapper().MapLine(LineRow(null, 100m));

        Assert.Null(result.Record);
        Assert.Equal(PurchaseRowMapper.DocumentKeyMissing, result.RejectReason);
    }

    [Fact]
    public void Line_keeps_its_document_key_and_product_key_as_text()
    {
        var record = DemoMapper().MapLine(LineRow(7L, 100m)).Record!;

        Assert.Equal("7", record.DocumentSourceKey);
        Assert.Equal("1", record.SourceKey);
        Assert.Equal("P-1", record.ProductSourceKey);
        Assert.Equal(2m, record.Quantity);
        Assert.Equal(9m, record.VatAmount);
    }

    [Fact]
    public void Production_refuses_unverified_items_and_demo_accepts_candidates()
    {
        var map = ExampleMap();

        Assert.Empty(SourceMapRequirements.Problems(map, isProduction: false));
        Assert.NotEmpty(SourceMapRequirements.Problems(map, isProduction: true));
        Assert.Throws<InvalidOperationException>(() => new PurchaseMapping(map, isProduction: true));
    }

    [Fact]
    public void Invoice_without_a_vendor_key_maps_to_no_vendor_bucket()
    {
        var record = DemoMapper().MapInvoice(InvoiceRow("I-3", "1403/12/30", vendor: null)).Record!;

        Assert.Equal("V-1", record.VendorSourceKey);

        var noVendor = DemoMapper().MapInvoice(Row(("DocID", "I-4"), ("SimDocDate", "1403/12/30"), ("VendorID", null))).Record!;
        Assert.Null(noVendor.VendorSourceKey);
    }

    [Fact]
    public void Gregorian_date_is_converted_with_the_jalali_components()
    {
        // A variant of the example map whose date columns declare the gregorian format.
        var json = File.ReadAllText(ExampleMapPath)
            .Replace("\"format\": \"jalaliText\"", "\"format\": \"gregorian\"", StringComparison.Ordinal);
        var loaded = SourceMap.Parse(json);
        Assert.True(loaded.IsValid, string.Join("; ", loaded.Problems.Select(p => p.Code)));
        var mapper = new PurchaseRowMapper(new PurchaseMapping(loaded.Map!, isProduction: false));

        var result = mapper.MapInvoice(InvoiceRow("I-5", new DateTime(2025, 3, 20, 0, 0, 0, DateTimeKind.Unspecified)));
        Assert.NotNull(result.Record);
        Assert.Equal(new DateOnly(2025, 3, 20), result.Record!.DocDate);
        Assert.Equal(1403, result.Record.JalaliYear);
        Assert.Equal(12, result.Record.JalaliMonth);
        Assert.Equal(30, result.Record.JalaliDay);
    }

    [Fact]
    public void Vendor_code_is_normalized_with_persian_text_rules()
    {
        // Arabic yeh U+064A in the code becomes Persian yeh U+06CC; digits are kept as recorded.
        var record = DemoMapper().MapVendor(VendorWithCodeRow("V-1", "نام", "AB-ي۱")).Record!;

        Assert.Equal("AB-ی۱", record.Code);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    public void Empty_vendor_code_becomes_null(string? code)
    {
        var record = DemoMapper().MapVendor(VendorWithCodeRow("V-1", "نام", code)).Record!;

        Assert.Null(record.Code);
    }

    [Fact]
    public void Missing_vendor_code_column_value_is_null_and_does_not_reject_the_vendor()
    {
        var result = DemoMapper().MapVendor(VendorRow("V-1", "نام"));

        Assert.NotNull(result.Record);
        Assert.Null(result.RejectReason);
        Assert.Null(result.Record!.Code);
    }

    [Fact]
    public void Product_category_key_is_mapped_to_drug_group_key()
    {
        var record = DemoMapper().MapProduct(ProductRow("P-1", "دارو", "G-3")).Record!;

        Assert.Equal("G-3", record.CategoryKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Unmapped_or_empty_product_category_key_is_null(string? categoryKey)
    {
        var record = DemoMapper().MapProduct(ProductRow("P-1", "دارو", categoryKey)).Record!;

        Assert.Null(record.CategoryKey);
    }

    [Fact]
    public void Product_category_master_row_is_normalized_like_other_names()
    {
        var mapper = DemoMapper();
        var record = mapper.MapProductCategory(CategoryRow("G-3", "  گروه‌ي  دارویی ")).Record!;

        Assert.Equal("G-3", record.SourceKey);
        Assert.Equal("گروه‌ی دارویی", record.NameFa);
    }

    [Fact]
    public void Empty_product_category_name_becomes_the_placeholder()
    {
        var record = DemoMapper().MapProductCategory(CategoryRow("G-4", "   ")).Record!;

        Assert.Equal("(بدون نام)", record.NameFa);
    }

    [Fact]
    public void Phase1_map_without_the_new_items_maps_code_and_category_to_null()
    {
        // Removing the Phase 2 entries from the example yields a Phase 1 map, which must still load.
        var node = JsonNode.Parse(File.ReadAllText(ExampleMapPath))!;
        var entities = node["entities"]!.AsObject();
        entities["vendor"]!.AsObject().Remove("code");
        entities["product"]!.AsObject().Remove("categoryKey");
        entities.Remove("productCategory");
        var loaded = SourceMap.Parse(node.ToJsonString());
        Assert.True(loaded.IsValid, string.Join("; ", loaded.Problems.Select(p => p.Code)));
        var mapper = new PurchaseRowMapper(new PurchaseMapping(loaded.Map!, isProduction: false));

        Assert.Null(mapper.MapVendor(VendorWithCodeRow("V-1", "نام", "C-1")).Record!.Code);
        Assert.Null(mapper.MapProduct(ProductRow("P-1", "دارو", "G-3")).Record!.CategoryKey);
    }
}
