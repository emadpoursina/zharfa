namespace Zharfa.Api.Tests.Security;

using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.VendorFinancials;

/// <summary>
/// T023 (Phase 6 US1, SC-001, research R1/R14): the reserved endpoint is no longer frozen to "not available
/// until phase 6". What matters is the availability decision: in production a map whose balanceFormula is still
/// a candidate never produces a number (formula-unconfirmed → «داده در دسترس نیست»), while the demo accepts the
/// candidate mix. 403 coverage lives in PermissionMatrixTests, not here.
/// Pure: no database, no network.
/// </summary>
public class VendorFinancialsTests
{
    private const string CandidateBalanceJson = """
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

    private static SourceMap CandidateMap() =>
        SourceMap.Parse(CandidateBalanceJson).Map!;

    [Fact]
    public void Production_with_a_candidate_balance_formula_never_shows_a_balance()
    {
        var reasons = VendorFinancialAvailability.Evaluate(
            CandidateMap(), isProduction: true, hasSucceededSync: true);

        Assert.Equal(VendorFinancialAvailability.FormulaUnconfirmed, reasons["vendorBalance"]);
    }

    [Fact]
    public void Demo_with_a_candidate_balance_formula_shows_the_balance()
    {
        var reasons = VendorFinancialAvailability.Evaluate(
            CandidateMap(), isProduction: false, hasSucceededSync: true);

        Assert.Null(reasons["vendorBalance"]);
        Assert.Null(reasons["outstandingPayments"]);
        Assert.Null(reasons["payments"]);
    }

    [Fact]
    public void Production_without_a_successful_sync_is_no_successful_sync_not_a_formula_guess()
    {
        var reasons = VendorFinancialAvailability.Evaluate(
            CandidateMap(), isProduction: true, hasSucceededSync: false);

        Assert.Equal(VendorFinancialAvailability.NoSuccessfulSync, reasons["vendorBalance"]);
    }

    [Fact]
    public void The_persian_unavailable_message_is_the_shared_one()
    {
        Assert.Equal("داده در دسترس نیست.", VendorFinancialUnavailable.PersianMessage);
        Assert.Equal("داده در دسترس نیست.", VendorFinancialDtos.DataMessage);
    }
}
