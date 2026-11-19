namespace Zharfa.Api.Tests.VendorFinancials;

using System.Linq;
using System.Text.Json.Nodes;
using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.VendorFinancials;

/// <summary>
/// T005 (specs/008-vendor-financials data-model §3, research R1): which source item each financial figure needs
/// and why it is unavailable. Production treats candidate as unusable (formula-unconfirmed), demo accepts it;
/// a missing optional entity is source-item-unmapped; no succeeded sync makes every figure unavailable.
/// Pure: no database, no network.
/// </summary>
public class VendorFinancialAvailabilityTests
{
    private static readonly string[] AllFigures =
    [
        "vendorBalance",
        "outstandingPayments",
        "outstandingInstruments",
        "payments",
        "paymentHistory",
        "netPurchases",
        "purchasesMinusPayments",
    ];

    private const string BaseJson = """
    {
      "version": 1,
      "balanceFormula": { "status": "candidate", "mix": "openPayablesPlusUnpaidCheques" },
      "entities": {
        "purchaseInvoice": { "object": "SimInvDoc", "kind": "table", "hostFiltered": false, "status": "candidate",
                    "key": { "column": "DocID", "status": "candidate" },
                    "date": { "column": "SimDocDate", "format": "jalaliText", "status": "candidate" },
                    "change": { "strategy": "full" } },
        "purchaseLine": { "object": "SimInvDocItem", "kind": "table", "hostFiltered": false, "status": "candidate",
                    "key": { "column": "ItemID", "status": "candidate" },
                    "documentKey": { "column": "DocID", "status": "candidate" },
                    "grossAmount": { "column": "SimLineAmount", "amountSign": "abs", "status": "candidate" },
                    "change": { "strategy": "full" } },
        "payableDocument": { "object": "SimPayAccDoc", "kind": "table", "hostFiltered": false, "status": "candidate",
                    "key": { "column": "PayDocID", "status": "candidate" },
                    "vendorKey": { "column": "VendorID", "status": "candidate" },
                    "date": { "column": "SimDocDate", "format": "jalaliText", "status": "candidate" },
                    "remainingAmount": { "column": "RemainAmt", "amountSign": "asIs", "status": "candidate" },
                    "change": { "strategy": "full" } },
        "payableCheque": { "object": "SimPayChq", "kind": "table", "hostFiltered": false, "status": "candidate",
                    "key": { "column": "ChqID", "status": "candidate" },
                    "vendorKey": { "column": "VendorID", "status": "candidate" },
                    "date": { "column": "SimIssueDate", "format": "jalaliText", "status": "candidate" },
                    "amount": { "column": "ChqAmt", "amountSign": "asIs", "status": "candidate" },
                    "remainingAmount": { "column": "RemainAmt", "amountSign": "asIs", "status": "candidate" },
                    "instrumentType": { "column": "ChqTp", "status": "candidate" },
                    "state": { "column": "ChqState", "status": "candidate" },
                    "clearedAt": { "column": "SimClearDate", "format": "jalaliText", "status": "candidate" },
                    "change": { "strategy": "full" } },
        "vendorPayment": { "object": "SimVendorPay", "kind": "table", "hostFiltered": false, "status": "candidate",
                    "key": { "column": "PayID", "status": "candidate" },
                    "vendorKey": { "column": "VendorID", "status": "candidate" },
                    "date": { "column": "SimPayDate", "format": "jalaliText", "status": "candidate" },
                    "amount": { "column": "PayAmt", "amountSign": "asIs", "status": "candidate" },
                    "instrumentType": { "column": "PayTp", "status": "candidate" },
                    "change": { "strategy": "full" } }
      }
    }
    """;

    /// <summary>Parses the base JSON after an optional in-memory edit.</summary>
    private static SourceMap Map(System.Action<JsonObject>? edit = null)
    {
        var root = JsonNode.Parse(BaseJson)!.AsObject();
        edit?.Invoke(root);
        var result = SourceMap.Parse(root.ToJsonString());
        Assert.True(result.IsValid, string.Join("; ", result.Problems.Select(p => p.Code)));
        return result.Map!;
    }

    [Fact]
    public void Reason_codes_are_exactly_the_five_of_the_data_model()
    {
        Assert.Equal(
            new[]
            {
                "no-successful-sync",
                "source-item-unverified",
                "source-item-unmapped",
                "no-previous-data",
                "formula-unconfirmed",
            },
            new[]
            {
                VendorFinancialAvailability.NoSuccessfulSync,
                VendorFinancialAvailability.SourceItemUnverified,
                VendorFinancialAvailability.SourceItemUnmapped,
                VendorFinancialAvailability.NoPreviousData,
                VendorFinancialAvailability.FormulaUnconfirmed,
            });
    }

    [Fact]
    public void Every_figure_is_named_in_the_evaluation()
    {
        var reasons = VendorFinancialAvailability.Evaluate(Map(), isProduction: false, hasSucceededSync: true);

        Assert.Equal(AllFigures.OrderBy(f => f), reasons.Keys.OrderBy(k => k));
    }

    [Fact]
    public void No_successful_sync_makes_every_figure_no_successful_sync()
    {
        var reasons = VendorFinancialAvailability.Evaluate(Map(), isProduction: false, hasSucceededSync: false);

        foreach (var figure in AllFigures)
        {
            Assert.Equal(VendorFinancialAvailability.NoSuccessfulSync, reasons[figure]);
        }
    }

    [Fact]
    public void Production_with_a_candidate_formula_refuses_the_balance_as_formula_unconfirmed()
    {
        var reasons = VendorFinancialAvailability.Evaluate(Map(), isProduction: true, hasSucceededSync: true);

        Assert.Equal(VendorFinancialAvailability.FormulaUnconfirmed, reasons["vendorBalance"]);
        // The candidate formula never gates purchases: net stays available once a sync succeeded.
        Assert.Null(reasons["netPurchases"]);
    }

    [Fact]
    public void Demo_with_a_candidate_formula_and_usable_entities_is_available()
    {
        var reasons = VendorFinancialAvailability.Evaluate(Map(), isProduction: false, hasSucceededSync: true);

        Assert.Null(reasons["vendorBalance"]);
        Assert.Null(reasons["outstandingPayments"]);
        Assert.Null(reasons["outstandingInstruments"]);
        Assert.Null(reasons["payments"]);
        Assert.Null(reasons["paymentHistory"]);
        Assert.Null(reasons["purchasesMinusPayments"]);
    }

    [Fact]
    public void Production_with_a_confirmed_formula_and_confirmed_entities_is_available()
    {
        var map = Map(root =>
        {
            root["balanceFormula"]!["status"] = "confirmed";
            foreach (var (name, node) in root["entities"]!.AsObject())
            {
                _ = name;
                if (node is JsonObject entityObj)
                {
                    entityObj["status"] = "confirmed";
                    foreach (var (_, fieldNode) in entityObj)
                    {
                        if (fieldNode is JsonObject fieldObj && fieldObj.ContainsKey("status"))
                        {
                            fieldObj["status"] = "confirmed";
                        }
                    }
                }
            }
        });

        var reasons = VendorFinancialAvailability.Evaluate(map, isProduction: true, hasSucceededSync: true);

        Assert.Null(reasons["vendorBalance"]);
        Assert.Null(reasons["outstandingPayments"]);
        Assert.Null(reasons["payments"]);
        Assert.Null(reasons["purchasesMinusPayments"]);
    }

    [Fact]
    public void No_declared_formula_is_formula_unconfirmed_not_an_error()
    {
        var map = Map(root => root.Remove("balanceFormula"));

        var reasons = VendorFinancialAvailability.Evaluate(map, isProduction: false, hasSucceededSync: true);

        Assert.Equal(VendorFinancialAvailability.FormulaUnconfirmed, reasons["vendorBalance"]);
        // The other figures do not depend on the balance formula.
        Assert.Null(reasons["outstandingPayments"]);
        Assert.Null(reasons["payments"]);
    }

    [Fact]
    public void Missing_optional_entity_is_source_item_unmapped()
    {
        var map = Map(root => root["entities"]!.AsObject().Remove("payableCheque"));

        var reasons = VendorFinancialAvailability.Evaluate(map, isProduction: false, hasSucceededSync: true);

        Assert.Equal(VendorFinancialAvailability.SourceItemUnmapped, reasons["vendorBalance"]);
        Assert.Equal(VendorFinancialAvailability.SourceItemUnmapped, reasons["outstandingPayments"]);
        Assert.Equal(VendorFinancialAvailability.SourceItemUnmapped, reasons["outstandingInstruments"]);
        // Payments keep their own source.
        Assert.Null(reasons["payments"]);
        Assert.Null(reasons["paymentHistory"]);
        Assert.Null(reasons["netPurchases"]);
        Assert.Null(reasons["purchasesMinusPayments"]);
    }

    [Fact]
    public void Missing_vendor_payment_entity_makes_payments_unmapped()
    {
        var map = Map(root => root["entities"]!.AsObject().Remove("vendorPayment"));

        var reasons = VendorFinancialAvailability.Evaluate(map, isProduction: false, hasSucceededSync: true);

        Assert.Equal(VendorFinancialAvailability.SourceItemUnmapped, reasons["payments"]);
        Assert.Equal(VendorFinancialAvailability.SourceItemUnmapped, reasons["paymentHistory"]);
        Assert.Equal(VendorFinancialAvailability.SourceItemUnmapped, reasons["purchasesMinusPayments"]);
        Assert.Null(reasons["vendorBalance"]);
    }

    [Fact]
    public void Unusable_entity_status_is_source_item_unverified()
    {
        var map = Map(root => root["entities"]!["payableDocument"]!["status"] = "unknown");

        var reasons = VendorFinancialAvailability.Evaluate(map, isProduction: false, hasSucceededSync: true);

        Assert.Equal(VendorFinancialAvailability.SourceItemUnverified, reasons["vendorBalance"]);
    }

    [Fact]
    public void Candidate_entities_are_unverified_in_production_but_usable_in_demo()
    {
        var confirmedFormula = Map(root => root["balanceFormula"]!["status"] = "confirmed");
        var production = VendorFinancialAvailability.Evaluate(confirmedFormula, isProduction: true, hasSucceededSync: true);
        var demo = VendorFinancialAvailability.Evaluate(confirmedFormula, isProduction: false, hasSucceededSync: true);

        // All entities carry candidate status: production treats them as unverified, demo as usable.
        Assert.Equal(VendorFinancialAvailability.SourceItemUnverified, production["payments"]);
        Assert.Null(demo["payments"]);
    }

    [Fact]
    public void Unknown_balance_mix_is_formula_unconfirmed()
    {
        var map = Map(root =>
        {
            root["balanceFormula"]!["status"] = "confirmed";
            root["balanceFormula"]!["mix"] = "somethingElse";
        });

        var reasons = VendorFinancialAvailability.Evaluate(map, isProduction: true, hasSucceededSync: true);

        Assert.Equal(VendorFinancialAvailability.FormulaUnconfirmed, reasons["vendorBalance"]);
    }

    [Fact]
    public void Unusable_payment_amount_field_is_source_item_unverified()
    {
        var map = Map(root => root["entities"]!["vendorPayment"]!["amount"]!["status"] = "unknown");

        var reasons = VendorFinancialAvailability.Evaluate(map, isProduction: false, hasSucceededSync: true);

        Assert.Equal(VendorFinancialAvailability.SourceItemUnverified, reasons["payments"]);
        Assert.Null(reasons["vendorBalance"]);
    }

    [Fact]
    public void Unusable_cheque_type_field_blocks_balance_and_outstanding()
    {
        var map = Map(root => root["entities"]!["payableCheque"]!["instrumentType"]!["status"] = "unknown");

        var reasons = VendorFinancialAvailability.Evaluate(map, isProduction: false, hasSucceededSync: true);

        Assert.Equal(VendorFinancialAvailability.SourceItemUnverified, reasons["vendorBalance"]);
        Assert.Equal(VendorFinancialAvailability.SourceItemUnverified, reasons["outstandingPayments"]);
        Assert.Null(reasons["payments"]);
    }
}
