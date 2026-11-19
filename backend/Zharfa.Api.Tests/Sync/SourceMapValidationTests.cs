namespace Zharfa.Api.Tests.Sync;

using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Zharfa.Api.Mabna;
using Zharfa.Api.Mabna.Sync;

public class SourceMapValidationTests
{
    private static readonly string ExampleMapPath =
        Path.Combine(AppContext.BaseDirectory, "example", "purchase-source-map.example.json");

    private static readonly string ExampleAllowlistPath =
        Path.Combine(AppContext.BaseDirectory, "example", "purchase-allowlist.example.txt");

    private static MabnaObjectPolicy ExamplePolicy() =>
        new(MabnaObjectPolicy.LoadEntries(new MabnaSourceOptions { AllowlistFile = ExampleAllowlistPath }));

    private static SourceMap LoadExample()
    {
        var result = SourceMap.LoadFromFile(ExampleMapPath);
        Assert.True(result.IsValid, string.Join("; ", result.Problems.Select(p => p.Code)));
        return result.Map!;
    }

    /// <summary>Loads the example map JSON, mutates it in memory, reparses.</summary>
    private static SourceMapLoadResult Mutate(Func<JsonElement, JsonElement> mutate)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ExampleMapPath));
        var mutated = mutate(doc.RootElement);
        return SourceMap.Parse(mutated.GetRawText());
    }

    private static JsonElement Prop(JsonElement parent, string name) => parent.GetProperty(name);

    // ---- rule 1: version / malformed / missing entities ----

    [Fact]
    public void Rule1_unknown_version_is_rejected()
    {
        var result = Mutate(root =>
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", 99);
                writer.WritePropertyName("entities");
                root.GetProperty("entities").WriteTo(writer);
                writer.WriteEndObject();
            }

            return JsonDocument.Parse(buffer.WrittenMemory).RootElement;
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Code == "version-unknown");
    }

    [Fact]
    public void Rule1_malformed_json_is_rejected_without_throwing()
    {
        var result = SourceMap.Parse("{ not json");
        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Code == "map-malformed");
    }

    [Fact]
    public void Rule1_missing_purchaseInvoice_is_rejected()
    {
        var result = Mutate(root =>
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", 1);
                writer.WritePropertyName("entities");
                writer.WriteStartObject();
                writer.WritePropertyName("purchaseLine");
                root.GetProperty("entities").GetProperty("purchaseLine").WriteTo(writer);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            return JsonDocument.Parse(buffer.WrittenMemory).RootElement;
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Code == "entity-missing");
    }

    [Fact]
    public void Rule1_missing_purchaseLine_is_rejected()
    {
        var result = Mutate(root =>
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", 1);
                writer.WritePropertyName("entities");
                writer.WriteStartObject();
                writer.WritePropertyName("purchaseInvoice");
                root.GetProperty("entities").GetProperty("purchaseInvoice").WriteTo(writer);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            return JsonDocument.Parse(buffer.WrittenMemory).RootElement;
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Code == "entity-missing");
    }

    // ---- rule 2: identifiers, exclusions, allowlist ----

    [Fact]
    public void Rule2_valid_example_map_passes_validation()
    {
        var result = SourceMapValidator.Validate(LoadExample(), ExamplePolicy());
        Assert.Empty(result);
    }

    [Fact]
    public void Rule2_mapped_object_missing_from_allowlist_is_rejected()
    {
        var map = LoadExample();
        map.PurchaseInvoice!.Object = "dbo.NotAllowlisted";
        var problems = SourceMapValidator.Validate(map, ExamplePolicy());
        Assert.Contains(problems, p => p.Code == "object-not-allowlisted");
    }

    [Fact]
    public void Rule2_hard_excluded_mapped_object_is_rejected()
    {
        var map = LoadExample();
        map.Vendor!.Object = "dbo.scuUser";
        var problems = SourceMapValidator.Validate(map, ExamplePolicy());
        Assert.Contains(problems, p => p.Code == "object-excluded");
    }

    [Fact]
    public void Rule2_invalid_column_identifier_is_rejected()
    {
        var map = LoadExample();
        map.PurchaseInvoice!.Key!.Column = "Doc ID; DROP";
        var problems = SourceMapValidator.Validate(map, ExamplePolicy());
        Assert.Contains(problems, p => p.Code == "column-invalid");
    }

    [Fact]
    public void Rule2_invalid_change_column_identifier_is_rejected()
    {
        var map = LoadExample();
        map.PurchaseInvoice!.Change!.Column = "Doc ID; DROP";
        var problems = SourceMapValidator.Validate(map, ExamplePolicy());
        Assert.Contains(problems, p => p.Code == "column-invalid");
    }

    [Fact]
    public void Rule2_valid_change_column_identifier_passes()
    {
        var map = LoadExample();
        map.PurchaseInvoice!.Change!.Column = "DocID";
        Assert.Empty(SourceMapValidator.Validate(map, ExamplePolicy()));
    }

    // ---- rule 3: hostFiltered ----

    [Fact]
    public void Rule3_view_with_hostFiltered_true_is_rejected()
    {
        var map = LoadExample();
        map.PurchaseInvoice!.Kind = "view";
        map.PurchaseInvoice.HostFiltered = true;
        var problems = SourceMapValidator.Validate(map, ExamplePolicy());
        Assert.Contains(problems, p => p.Code == "host-filtered");
    }

    [Fact]
    public void Rule3_view_with_missing_hostFiltered_is_rejected()
    {
        var map = LoadExample();
        map.PurchaseInvoice!.Kind = "view";
        map.PurchaseInvoice.HostFiltered = null;
        var problems = SourceMapValidator.Validate(map, ExamplePolicy());
        Assert.Contains(problems, p => p.Code == "host-filtered-missing");
    }

    // ---- rule 4: filters ----

    [Fact]
    public void Rule4_unknown_filter_op_is_rejected()
    {
        var map = LoadExample();
        map.PurchaseInvoice!.Filter[0].Op = "NotAnOp";
        var problems = SourceMapValidator.Validate(map, ExamplePolicy());
        Assert.Contains(problems, p => p.Code == "filter-op-invalid");
    }

    [Fact]
    public void Rule4_non_scalar_filter_value_is_rejected()
    {
        var map = LoadExample();
        map.PurchaseInvoice!.Filter[0].Value = null;
        var problems = SourceMapValidator.Validate(map, ExamplePolicy());
        Assert.Contains(problems, p => p.Code == "filter-value-invalid");
    }

    // ---- rule 5: enums ----

    [Fact]
    public void Rule5_unknown_date_format_is_rejected()
    {
        var map = LoadExample();
        map.PurchaseInvoice!.Date!.Format = "julian";
        var problems = SourceMapValidator.Validate(map, ExamplePolicy());
        Assert.Contains(problems, p => p.Code == "date-format-invalid");
    }

    [Fact]
    public void Rule5_unknown_change_strategy_is_rejected()
    {
        var map = LoadExample();
        map.PurchaseInvoice!.Change!.Strategy = "cdc";
        var problems = SourceMapValidator.Validate(map, ExamplePolicy());
        Assert.Contains(problems, p => p.Code == "change-strategy-invalid");
    }

    [Fact]
    public void Rule5_unknown_amount_sign_is_rejected()
    {
        var map = LoadExample();
        map.PurchaseLine!.GrossAmount!.AmountSign = "flip";
        var problems = SourceMapValidator.Validate(map, ExamplePolicy());
        Assert.Contains(problems, p => p.Code == "amount-sign-invalid");
    }

    [Fact]
    public void Rule5_blank_amount_sign_is_rejected()
    {
        var map = LoadExample();
        map.PurchaseLine!.GrossAmount!.AmountSign = "";
        var problems = SourceMapValidator.Validate(map, ExamplePolicy());
        Assert.Contains(problems, p => p.Code == "amount-sign-invalid");
    }

    [Fact]
    public void Rule5_missing_amount_sign_is_rejected()
    {
        var map = LoadExample();
        map.PurchaseLine!.GrossAmount!.AmountSign = null;
        var problems = SourceMapValidator.Validate(map, ExamplePolicy());
        Assert.Contains(problems, p => p.Code == "amount-sign-invalid");
    }

    [Theory]
    [InlineData("asIs")]
    [InlineData("negate")]
    [InlineData("abs")]
    public void Rule5_each_allowed_amount_sign_passes(string sign)
    {
        var map = LoadExample();
        map.PurchaseLine!.GrossAmount!.AmountSign = sign;
        Assert.Empty(SourceMapValidator.Validate(map, ExamplePolicy()));
    }

    // ---- rule 6: line linkage ----

    [Fact]
    public void Rule6_missing_documentKey_is_rejected()
    {
        var map = LoadExample();
        map.PurchaseLine!.DocumentKey = null;
        var problems = SourceMapValidator.Validate(map, ExamplePolicy());
        Assert.Contains(problems, p => p.Code == "document-key-missing");
    }

    [Fact]
    public void Rule6_same_object_without_filters_is_ambiguous()
    {
        var map = LoadExample();
        map.PurchaseReturn!.Object = map.PurchaseInvoice!.Object;
        map.PurchaseInvoice.Filter.Clear();
        map.PurchaseReturn.Filter.Clear();
        var problems = SourceMapValidator.Validate(map, ExamplePolicy());
        Assert.Contains(problems, p => p.Code == "line-linkage-ambiguous");
    }

    [Fact]
    public void Rule6_same_object_with_identical_filters_is_ambiguous()
    {
        var map = LoadExample();
        map.PurchaseReturn!.Object = map.PurchaseInvoice!.Object;
        map.PurchaseReturn.Filter.Clear();
        foreach (var filter in map.PurchaseInvoice.Filter)
        {
            map.PurchaseReturn.Filter.Add(new SourceMapFilterField
            {
                Column = filter.Column,
                Op = filter.Op,
                Value = filter.Value,
                Status = filter.Status,
            });
        }

        var problems = SourceMapValidator.Validate(map, ExamplePolicy());
        Assert.Contains(problems, p => p.Code == "line-linkage-ambiguous");
    }

    [Fact]
    public void Rule6_same_object_with_identical_filters_in_another_order_is_ambiguous()
    {
        var map = LoadExample();
        map.PurchaseReturn!.Object = map.PurchaseInvoice!.Object;
        map.PurchaseInvoice.Filter.Clear();
        map.PurchaseInvoice.Filter.Add(new SourceMapFilterField { Column = "SimOpCode", Op = "Eq", Value = 11m, Status = "candidate" });
        map.PurchaseInvoice.Filter.Add(new SourceMapFilterField { Column = "Ver", Op = "Eq", Value = 1m, Status = "candidate" });
        map.PurchaseReturn.Filter.Clear();
        map.PurchaseReturn.Filter.Add(new SourceMapFilterField { Column = "Ver", Op = "Eq", Value = 1m, Status = "candidate" });
        map.PurchaseReturn.Filter.Add(new SourceMapFilterField { Column = "SimOpCode", Op = "Eq", Value = 11m, Status = "candidate" });

        var problems = SourceMapValidator.Validate(map, ExamplePolicy());
        Assert.Contains(problems, p => p.Code == "line-linkage-ambiguous");
    }

    [Fact]
    public void Rule6_same_object_with_distinct_filter_values_is_allowed()
    {
        var map = LoadExample();
        map.PurchaseReturn!.Object = map.PurchaseInvoice!.Object;
        map.PurchaseReturn.Filter[0].Value = 12m; // invoice SimOpCode = 11, return SimOpCode = 12

        var problems = SourceMapValidator.Validate(map, ExamplePolicy());
        Assert.DoesNotContain(problems, p => p.Code == "line-linkage-ambiguous");
    }

    // ---- statuses and usability ----

    [Fact]
    public void Candidate_is_usable_in_demo_but_not_in_production()
    {
        Assert.True(SourceMap.IsStatusUsable("candidate", isProduction: false));
        Assert.False(SourceMap.IsStatusUsable("candidate", isProduction: true));
    }

    [Theory]
    [InlineData("confirmed")]
    [InlineData("corrected")]
    public void Confirmed_and_corrected_are_usable_in_both_modes(string status)
    {
        Assert.True(SourceMap.IsStatusUsable(status, isProduction: false));
        Assert.True(SourceMap.IsStatusUsable(status, isProduction: true));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bogus")]
    public void Unknown_or_missing_status_is_unusable_not_an_error(string? status)
    {
        Assert.False(SourceMap.IsStatusUsable(status, isProduction: false));
        Assert.False(SourceMap.IsStatusUsable(status, isProduction: true));
    }

    [Fact]
    public void Null_column_is_unusable_not_an_error()
    {
        var map = LoadExample();
        Assert.Null(map.PurchaseInvoice!.DocDiscount!.Column);
        Assert.False(SourceMap.IsUsable(map.PurchaseInvoice.DocDiscount, isProduction: false));
        // ... and validation still passes (no problem raised for it)
        Assert.Empty(SourceMapValidator.Validate(map, ExamplePolicy()));
    }

    [Fact]
    public void Loading_an_unset_path_reports_not_configured()
    {
        var result = SourceMap.LoadFromFile(null);
        Assert.False(result.Configured);
        Assert.False(result.IsValid);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Loading_a_missing_file_reports_a_problem()
    {
        var result = SourceMap.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "nope.json"));
        Assert.True(result.Configured);
        Assert.Contains(result.Problems, p => p.Code == "map-missing");
    }

    [Fact]
    public void Validator_on_null_map_returns_missing_problem_not_exception()
    {
        var problems = SourceMapValidator.Validate(null, ExamplePolicy());
        Assert.Contains(problems, p => p.Code == "map-missing");
    }

    // ---- retired lookbackDays (spec 003 Phase 13, research R5) ----

    [Fact]
    public void Retired_lookbackDays_key_still_loads_and_validates_unchanged()
    {
        // A map written before Phase 13 may still carry the key; it is ignored, never a problem.
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(ExampleMapPath))!.AsObject();
        var invoiceChange = node["entities"]!["purchaseInvoice"]!["change"]!.AsObject();
        invoiceChange["lookbackDays"] = 60;
        var returnChange = node["entities"]!["purchaseReturn"]!["change"]!.AsObject();
        returnChange["lookbackDays"] = 60;

        var result = SourceMap.Parse(node.ToJsonString());

        Assert.True(result.IsValid, string.Join("; ", result.Problems.Select(p => p.Code)));
        Assert.Empty(SourceMapValidator.Validate(result.Map, ExamplePolicy()));
        Assert.Equal("idWatermark", result.Map!.PurchaseInvoice!.Change!.Strategy);
        Assert.Equal("DocID", result.Map.PurchaseInvoice.Change.Column);
    }

    // ---- spec 004 (Phase 2): vendor code, product category key, productCategory master (T009) ----

    // SourceMap's entity properties have private setters, so these tests edit the example
    // JSON in memory and reparse it (the same route as the retired-lookbackDays test below).

    /// <summary>Loads the example map after applying an in-memory edit to its JSON; never throws on a valid map.</summary>
    private static SourceMap LoadExampleWith(Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(File.ReadAllText(ExampleMapPath))!.AsObject();
        edit(root);
        var result = SourceMap.Parse(root.ToJsonString());
        Assert.True(result.IsValid, string.Join("; ", result.Problems.Select(p => p.Code)));
        return result.Map!;
    }

    /// <summary>Sets (or, with null, removes) the optional productCategory entity in the example JSON.</summary>
    private static void SetProductCategory(JsonObject root, JsonObject? entity)
    {
        var entities = root["entities"]!.AsObject();
        entities.Remove("productCategory");
        if (entity is not null)
        {
            entities["productCategory"] = entity;
        }
    }

    private static JsonObject DrugGroupEntity(string obj = "dbo.SimDrugGroup") => new()
    {
        ["object"] = obj,
        ["kind"] = "table",
        ["hostFiltered"] = false,
        ["status"] = "candidate",
        ["key"] = new JsonObject { ["column"] = "DrugGroupID", ["status"] = "candidate" },
        ["name"] = new JsonObject { ["column"] = "DrugGroupName", ["status"] = "candidate" },
        ["change"] = new JsonObject { ["strategy"] = "full" },
    };

    [Fact]
    public void Phase1_map_without_the_phase2_items_still_loads_and_validates()
    {
        var map = LoadExampleWith(root => SetProductCategory(root, null));
        map.Vendor!.Code = null;
        map.Product!.CategoryKey = null;

        Assert.Empty(SourceMapValidator.Validate(map, ExamplePolicy()));
    }

    [Fact]
    public void Product_categoryKey_without_productCategory_is_not_an_error()
    {
        var map = LoadExampleWith(root => SetProductCategory(root, null));
        map.Product!.CategoryKey = new SourceMapField { Column = "SimDrugGroupID", Status = "candidate" };

        Assert.Empty(SourceMapValidator.Validate(map, ExamplePolicy()));
    }

    [Fact]
    public void Product_category_entity_must_be_on_the_allowlist()
    {
        var entity = DrugGroupEntity("dbo.SimNotListed");
        var map = LoadExampleWith(root => SetProductCategory(root, entity));

        var problems = SourceMapValidator.Validate(map, ExamplePolicy());

        Assert.Contains(problems, p => p.Code == "object-not-allowlisted");
    }

    [Fact]
    public void Product_category_entity_with_host_filtered_true_is_rejected()
    {
        var entity = DrugGroupEntity();
        entity["hostFiltered"] = true;
        var map = LoadExampleWith(root => SetProductCategory(root, entity));

        var problems = SourceMapValidator.Validate(map, ExamplePolicy());

        Assert.Contains(problems, p => p.Code == "host-filtered");
    }

    [Fact]
    public void Product_category_key_with_an_invalid_identifier_is_rejected()
    {
        var entity = DrugGroupEntity();
        entity["key"]!["column"] = "Drug Group; DROP";
        var map = LoadExampleWith(root => SetProductCategory(root, entity));

        var problems = SourceMapValidator.Validate(map, ExamplePolicy());

        Assert.Contains(problems, p => p.Code == "column-invalid");
    }

    [Fact]
    public void Product_category_with_an_unknown_change_strategy_is_rejected()
    {
        var entity = DrugGroupEntity();
        entity["change"]!["strategy"] = "lookbackDays";
        var map = LoadExampleWith(root => SetProductCategory(root, entity));

        var problems = SourceMapValidator.Validate(map, ExamplePolicy());

        Assert.Contains(problems, p => p.Code == "change-strategy-invalid");
    }

    [Fact]
    public void Vendor_code_with_an_invalid_identifier_is_rejected()
    {
        var map = LoadExample();
        map.Vendor!.Code = new SourceMapField { Column = "Code; DROP", Status = "candidate" };

        var problems = SourceMapValidator.Validate(map, ExamplePolicy());

        Assert.Contains(problems, p => p.Code == "column-invalid");
    }

    [Fact]
    public void Product_category_key_column_with_a_valid_identifier_passes()
    {
        var entity = DrugGroupEntity();
        var map = LoadExampleWith(root => SetProductCategory(root, entity));
        map.Product!.CategoryKey = new SourceMapField { Column = "SimDrugGroupID", Status = "candidate" };

        Assert.Empty(SourceMapValidator.Validate(map, ExamplePolicy()));
    }

    // ---- Phase 6 (spec 008, T007): optional financial entities, balanceFormula, cheque type/state ----
    // The example map gains the Phase 6 keys in US1 (T026); these tests insert the entities themselves when
    // they are absent, so they hold before and after that change.

    private static readonly string[] FinancialEntityNames =
        ["payableDocument", "payableCheque", "vendorPayment", "paymentAllocation"];

    private static JsonObject FinancialEntity(string name) => name switch
    {
        "payableDocument" => new JsonObject
        {
            ["object"] = "dbo.SimPayAccDoc",
            ["kind"] = "table",
            ["hostFiltered"] = false,
            ["status"] = "candidate",
            ["key"] = new JsonObject { ["column"] = "PayDocID", ["status"] = "candidate" },
            ["vendorKey"] = new JsonObject { ["column"] = "VendorID", ["status"] = "candidate" },
            ["date"] = new JsonObject { ["column"] = "SimDocDate", ["format"] = "jalaliText", ["status"] = "candidate" },
            ["remainingAmount"] = new JsonObject { ["column"] = "RemainAmt", ["amountSign"] = "asIs", ["status"] = "candidate" },
            ["number"] = new JsonObject { ["column"] = "SimDocNumber", ["status"] = "candidate" },
            ["cancelled"] = new JsonObject { ["rule"] = "column", ["column"] = "IsCanceled", ["value"] = 1, ["status"] = "candidate" },
            ["change"] = new JsonObject { ["strategy"] = "full" },
        },
        "payableCheque" => new JsonObject
        {
            ["object"] = "dbo.SimPayChq",
            ["kind"] = "table",
            ["hostFiltered"] = false,
            ["status"] = "candidate",
            ["key"] = new JsonObject { ["column"] = "ChqID", ["status"] = "candidate" },
            ["vendorKey"] = new JsonObject { ["column"] = "VendorID", ["status"] = "candidate" },
            ["date"] = new JsonObject { ["column"] = "SimIssueDate", ["format"] = "jalaliText", ["status"] = "candidate" },
            ["amount"] = new JsonObject { ["column"] = "ChqAmt", ["amountSign"] = "asIs", ["status"] = "candidate" },
            ["remainingAmount"] = new JsonObject { ["column"] = "RemainAmt", ["amountSign"] = "asIs", ["status"] = "candidate" },
            ["instrumentType"] = new JsonObject { ["column"] = "ChqTp", ["status"] = "candidate" },
            ["state"] = new JsonObject { ["column"] = "ChqState", ["status"] = "candidate" },
            ["clearedAt"] = new JsonObject { ["column"] = "SimClearDate", ["format"] = "jalaliText", ["status"] = "candidate" },
            ["number"] = new JsonObject { ["column"] = "SimChqNumber", ["status"] = "candidate" },
            ["cancelled"] = new JsonObject { ["rule"] = "column", ["column"] = "IsCanceled", ["value"] = 1, ["status"] = "candidate" },
            ["change"] = new JsonObject { ["strategy"] = "full" },
        },
        "vendorPayment" => new JsonObject
        {
            ["object"] = "dbo.SimVendorPay",
            ["kind"] = "table",
            ["hostFiltered"] = false,
            ["status"] = "candidate",
            ["key"] = new JsonObject { ["column"] = "PayID", ["status"] = "candidate" },
            ["vendorKey"] = new JsonObject { ["column"] = "VendorID", ["status"] = "candidate" },
            ["date"] = new JsonObject { ["column"] = "SimPayDate", ["format"] = "jalaliText", ["status"] = "candidate" },
            ["amount"] = new JsonObject { ["column"] = "PayAmt", ["amountSign"] = "asIs", ["status"] = "candidate" },
            ["instrumentType"] = new JsonObject { ["column"] = "PayTp", ["status"] = "candidate" },
            ["number"] = new JsonObject { ["column"] = "SimPayNumber", ["status"] = "candidate" },
            ["instrumentKey"] = new JsonObject { ["column"] = "SimChqID", ["status"] = "candidate" },
            ["cancelled"] = new JsonObject { ["rule"] = "column", ["column"] = "IsCanceled", ["value"] = 1, ["status"] = "candidate" },
            ["change"] = new JsonObject { ["strategy"] = "full" },
        },
        _ => new JsonObject
        {
            ["object"] = "dbo.SimPayAlloc",
            ["kind"] = "table",
            ["hostFiltered"] = false,
            ["status"] = "candidate",
            ["key"] = new JsonObject { ["column"] = "AllocID", ["status"] = "candidate" },
            ["instrumentKey"] = new JsonObject { ["column"] = "SimChqID", ["status"] = "candidate" },
            ["vendorKey"] = new JsonObject { ["column"] = "VendorID", ["status"] = "candidate" },
            ["amount"] = new JsonObject { ["column"] = "AllocAmt", ["amountSign"] = "asIs", ["status"] = "candidate" },
            ["change"] = new JsonObject { ["strategy"] = "full" },
        },
    };

    /// <summary>Adds the four optional financial entities and the candidate formula when the map lacks them.</summary>
    private static void EnsureFinancial(JsonObject root)
    {
        var entities = root["entities"]!.AsObject();
        foreach (var name in FinancialEntityNames)
        {
            if (!entities.ContainsKey(name))
            {
                entities[name] = FinancialEntity(name);
            }
        }

        if (!root.ContainsKey("balanceFormula"))
        {
            root["balanceFormula"] = new JsonObject
            {
                ["status"] = "candidate",
                ["mix"] = "openPayablesPlusUnpaidCheques",
            };
        }
    }

    private static void RemoveFinancial(JsonObject root)
    {
        var entities = root["entities"]!.AsObject();
        foreach (var name in FinancialEntityNames)
        {
            entities.Remove(name);
        }

        root.Remove("balanceFormula");
        root.Remove("partialPayment");
    }

    [Fact]
    public void Phase6_supported_version_stays_1()
    {
        Assert.Equal(1, SourceMap.SupportedVersion);

        var map = LoadExample();
        Assert.Equal(1, map.Version);
    }

    [Fact]
    public void Optional_financial_entities_may_be_absent_and_the_map_stays_valid()
    {
        var map = LoadExampleWith(RemoveFinancial);

        Assert.Empty(SourceMapValidator.Validate(map, ExamplePolicy()));
        Assert.Null(map.BalanceFormula);
        Assert.Null(map.PayableDocument);
        Assert.Null(map.PayableCheque);
        Assert.Null(map.VendorPayment);
        Assert.Null(map.PaymentAllocation);
    }

    [Fact]
    public void A_complete_phase6_map_validates()
    {
        var map = LoadExampleWith(EnsureFinancial);

        Assert.Empty(SourceMapValidator.Validate(map, ExamplePolicy()));
        Assert.Equal("openPayablesPlusUnpaidCheques", map.BalanceFormula!.Mix);
        Assert.Equal("candidate", map.BalanceFormula.Status);
    }

    [Fact]
    public void Financial_entity_with_host_filtered_true_is_rejected()
    {
        var map = LoadExampleWith(root =>
        {
            EnsureFinancial(root);
            root["entities"]!["payableDocument"]!["hostFiltered"] = true;
        });

        var problems = SourceMapValidator.Validate(map, ExamplePolicy());

        Assert.Contains(problems, p => p.Code == "host-filtered");
    }

    [Fact]
    public void Financial_entity_object_must_be_on_the_allowlist()
    {
        var map = LoadExampleWith(root =>
        {
            EnsureFinancial(root);
            root["entities"]!["vendorPayment"]!["object"] = "dbo.SimNotOnTheList";
        });

        var problems = SourceMapValidator.Validate(map, ExamplePolicy());

        Assert.Contains(problems, p => p.Code == "object-not-allowlisted");
    }

    [Fact]
    public void Unknown_balance_mix_is_rejected_as_balance_mix_unknown()
    {
        var map = LoadExampleWith(root =>
        {
            EnsureFinancial(root);
            root["balanceFormula"]!["mix"] = "inventedMix";
        });

        var problems = SourceMapValidator.Validate(map, ExamplePolicy());

        Assert.Contains(problems, p => p.Code == "balance-mix-unknown");
    }

    [Fact]
    public void Known_balance_mix_passes_validation()
    {
        var map = LoadExampleWith(EnsureFinancial);

        Assert.DoesNotContain(SourceMapValidator.Validate(map, ExamplePolicy()), p => p.Code == "balance-mix-unknown");
    }

    [Fact]
    public void Absent_balance_formula_is_valid_and_only_makes_the_balance_unavailable()
    {
        var map = LoadExampleWith(root =>
        {
            EnsureFinancial(root);
            root.Remove("balanceFormula");
        });

        Assert.DoesNotContain(SourceMapValidator.Validate(map, ExamplePolicy()), p => p.Code == "balance-mix-unknown");
        Assert.Null(map.BalanceFormula);
    }

    [Fact]
    public void Payable_cheque_without_instrument_type_or_state_is_rejected()
    {
        var map = LoadExampleWith(root =>
        {
            EnsureFinancial(root);
            var cheque = root["entities"]!["payableCheque"]!.AsObject();
            cheque.Remove("instrumentType");
            cheque.Remove("state");
        });

        var problems = SourceMapValidator.Validate(map, ExamplePolicy());

        Assert.Contains(problems, p => p.Code == "payable-cheque-type-state-missing");
    }

    [Fact]
    public void Payable_cheque_with_explicit_unknown_type_and_state_is_valid()
    {
        var map = LoadExampleWith(root =>
        {
            EnsureFinancial(root);
            root["entities"]!["payableCheque"]!["instrumentType"] = new JsonObject
            {
                ["column"] = null,
                ["status"] = "unknown",
            };
            root["entities"]!["payableCheque"]!["state"] = new JsonObject
            {
                ["column"] = null,
                ["status"] = "unknown",
            };
        });

        Assert.DoesNotContain(
            SourceMapValidator.Validate(map, ExamplePolicy()),
            p => p.Code == "payable-cheque-type-state-missing");
    }

    [Fact]
    public void Financial_amount_sign_is_validated()
    {
        var map = LoadExampleWith(root =>
        {
            EnsureFinancial(root);
            root["entities"]!["payableDocument"]!["remainingAmount"]!["amountSign"] = "flip";
        });

        var problems = SourceMapValidator.Validate(map, ExamplePolicy());

        Assert.Contains(problems, p => p.Code == "amount-sign-invalid");
    }

    [Fact]
    public void Financial_columns_are_identifier_checked()
    {
        var map = LoadExampleWith(root =>
        {
            EnsureFinancial(root);
            root["entities"]!["vendorPayment"]!["amount"]!["column"] = "Pay Amt; DROP";
        });

        var problems = SourceMapValidator.Validate(map, ExamplePolicy());

        Assert.Contains(problems, p => p.Code == "column-invalid");
    }
}
