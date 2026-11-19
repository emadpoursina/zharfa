namespace Zharfa.Api.Tests.VendorFinancials;

using Zharfa.Api.VendorFinancials;

/// <summary>
/// T006 (specs/008-vendor-financials data-model §1.5, research R1/R3/R4, FR-033): the C# recompute of the three
/// SQL functions — signed balance, non-negative outstanding, in-period payments — with cancelled rows excluded
/// and the partial-pay split kept apart (paid portion in payments, remainder in outstanding, counted once).
/// Pure in-memory rows: no database, no network.
/// </summary>
public class VendorFinancialTotalsTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 21);      // a period end
    private static readonly DateOnly Before = new(2026, 10, 1);
    private static readonly DateOnly After = new(2026, 12, 5);

    private static readonly PayableDocumentRow[] NoDocuments = [];

    [Fact]
    public void Balance_sums_signed_document_remainders_and_open_cheques()
    {
        var documents = new[]
        {
            new PayableDocumentRow("V-1", Before, 1000m, Cancelled: false),
            new PayableDocumentRow("V-1", Before, 250.5m, Cancelled: false),
        };
        var cheques = new[]
        {
            new PayableChequeRow("V-1", Before, 500m, ClearedAt: null, Cancelled: false),
        };

        Assert.Equal(1750.5m, VendorFinancialTotals.VendorBalance(documents, cheques, AsOf));
    }

    [Fact]
    public void Credit_document_makes_the_balance_negative_and_it_is_never_floored()
    {
        var documents = new[]
        {
            new PayableDocumentRow("V-1", Before, 4000m, Cancelled: false),
            new PayableDocumentRow("V-1", Before, -6000m, Cancelled: false), // credit note
        };

        var balance = VendorFinancialTotals.VendorBalance(documents, [], AsOf);

        Assert.Equal(-2000m, balance);
        Assert.True(balance < 0);
    }

    [Fact]
    public void Cancelled_and_future_rows_are_excluded_from_the_balance()
    {
        var documents = new[]
        {
            new PayableDocumentRow("V-1", Before, 1000m, Cancelled: true),
            new PayableDocumentRow("V-1", After, 5000m, Cancelled: false),   // issued after as-of
            new PayableDocumentRow("V-1", Before, 700m, Cancelled: false),
        };
        var cheques = new[]
        {
            new PayableChequeRow("V-1", Before, 300m, ClearedAt: null, Cancelled: true),
            new PayableChequeRow("V-1", After, 900m, ClearedAt: null, Cancelled: false), // issue after as-of
        };

        Assert.Equal(700m, VendorFinancialTotals.VendorBalance(documents, cheques, AsOf));
    }

    [Fact]
    public void Cheque_fully_cleared_on_or_before_as_of_leaves_the_balance_and_outstanding()
    {
        var cheques = new[]
        {
            new PayableChequeRow("V-1", Before, 800m, ClearedAt: AsOf, Cancelled: false),       // cleared exactly at as-of
            new PayableChequeRow("V-1", Before, 50m, ClearedAt: Before, Cancelled: false),      // cleared earlier
            new PayableChequeRow("V-1", Before, 900m, ClearedAt: After, Cancelled: false),      // cleared after as-of: still open
            new PayableChequeRow("V-1", Before, 100m, ClearedAt: null, Cancelled: false),       // never cleared
        };

        Assert.Equal(1000m, VendorFinancialTotals.VendorBalance(NoDocuments, cheques, AsOf));
        Assert.Equal(1000m, VendorFinancialTotals.OutstandingPayments(cheques, AsOf));
    }

    [Fact]
    public void Outstanding_is_the_unpaid_remainder_only_and_never_negative()
    {
        var cheques = new[]
        {
            new PayableChequeRow("V-1", Before, 600m, ClearedAt: null, Cancelled: false),
            new PayableChequeRow("V-1", Before, 0m, ClearedAt: null, Cancelled: false),   // fully settled remainder
            new PayableChequeRow("V-1", Before, 400m, ClearedAt: null, Cancelled: true),  // voided
        };

        var outstanding = VendorFinancialTotals.OutstandingPayments(cheques, AsOf);

        Assert.Equal(600m, outstanding);
        Assert.True(outstanding >= 0m);
    }

    [Fact]
    public void Outstanding_never_counts_history_amounts()
    {
        // History lives in vendor_payment rows; the outstanding formula only reads cheques (FR-033).
        var payments = new[]
        {
            new VendorPaymentRow("V-1", Before, 999m, Cancelled: false),
        };
        var cheques = new[]
        {
            new PayableChequeRow("V-1", Before, 1000m, ClearedAt: null, Cancelled: false),
        };

        Assert.Equal(1000m, VendorFinancialTotals.OutstandingPayments(cheques, AsOf));
        Assert.Equal(999m, VendorFinancialTotals.Payments(payments, Before, AsOf));
        _ = payments;
    }

    [Fact]
    public void Payments_sum_the_paid_portion_inside_the_period_only()
    {
        var from = new DateOnly(2026, 11, 1);
        var payments = new[]
        {
            new VendorPaymentRow("V-1", new DateOnly(2026, 11, 1), 100m, Cancelled: false),  // first day inclusive
            new VendorPaymentRow("V-1", AsOf, 200m, Cancelled: false),                        // last day inclusive
            new VendorPaymentRow("V-1", new DateOnly(2026, 10, 31), 400m, Cancelled: false),  // before
            new VendorPaymentRow("V-1", After, 800m, Cancelled: false),                       // after
            new VendorPaymentRow("V-1", new DateOnly(2026, 11, 10), 1600m, Cancelled: true),  // cancelled
        };

        Assert.Equal(300m, VendorFinancialTotals.Payments(payments, from, AsOf));
    }

    [Fact]
    public void Partial_payment_counts_the_paid_portion_in_payments_and_the_remainder_in_outstanding()
    {
        // A cheque of 1000 with a paid portion of 400 inside the period and a 600 remainder still open (FR-033).
        var cheques = new[]
        {
            new PayableChequeRow("V-1", Before, 600m, ClearedAt: null, Cancelled: false),
        };
        var payments = new[]
        {
            new VendorPaymentRow("V-1", new DateOnly(2026, 11, 5), 400m, Cancelled: false),
        };

        var paymentsTotal = VendorFinancialTotals.Payments(payments, new DateOnly(2026, 11, 1), AsOf);
        var outstanding = VendorFinancialTotals.OutstandingPayments(cheques, AsOf);
        var balance = VendorFinancialTotals.VendorBalance(NoDocuments, cheques, AsOf);

        Assert.Equal(400m, paymentsTotal);
        Assert.Equal(600m, outstanding);
        Assert.Equal(600m, balance);
        // No double-count: the 1000 face amount appears nowhere as a whole.
        Assert.NotEqual(1000m, paymentsTotal);
        Assert.NotEqual(1000m, outstanding);
    }

    [Fact]
    public void Zero_qualifying_rows_is_zero_not_unavailable()
    {
        Assert.Equal(0m, VendorFinancialTotals.VendorBalance([], [], AsOf));
        Assert.Equal(0m, VendorFinancialTotals.OutstandingPayments([], AsOf));
        Assert.Equal(0m, VendorFinancialTotals.Payments([], AsOf, After));
        Assert.Equal("0.0000", VendorFinancialTotals.Format(0m));
    }

    [Fact]
    public void Vendor_filter_selects_one_vendor_without_changing_the_others()
    {
        var documents = new[]
        {
            new PayableDocumentRow("V-1", Before, 100m, Cancelled: false),
            new PayableDocumentRow("V-2", Before, 900m, Cancelled: false),
            new PayableDocumentRow(null, Before, 50m, Cancelled: false), // no vendor: never matches a filter
        };

        Assert.Equal(100m, VendorFinancialTotals.VendorBalance(documents, [], AsOf, "V-1"));
        Assert.Equal(900m, VendorFinancialTotals.VendorBalance(documents, [], AsOf, "V-2"));
        Assert.Equal(1050m, VendorFinancialTotals.VendorBalance(documents, [], AsOf));
    }

    [Fact]
    public void Format_is_four_decimals_with_a_leading_minus_for_credit()
    {
        Assert.Equal("1234.5678", VendorFinancialTotals.Format(1234.5678m));
        Assert.Equal("-1500000.0000", VendorFinancialTotals.Format(-1500000m));
        Assert.Equal("0.0000", VendorFinancialTotals.Format(0m));
    }

    [Fact]
    public void IsOpenAt_is_the_single_open_rule_of_both_formulas()
    {
        var open = new PayableChequeRow("V-1", Before, 10m, ClearedAt: null, Cancelled: false);
        var cleared = new PayableChequeRow("V-1", Before, 10m, ClearedAt: Before, Cancelled: false);
        var future = new PayableChequeRow("V-1", After, 10m, ClearedAt: null, Cancelled: false);
        var voided = new PayableChequeRow("V-1", Before, 10m, ClearedAt: null, Cancelled: true);

        Assert.True(VendorFinancialTotals.IsOpenAt(open, AsOf));
        Assert.False(VendorFinancialTotals.IsOpenAt(cleared, AsOf));
        Assert.False(VendorFinancialTotals.IsOpenAt(future, AsOf));
        Assert.False(VendorFinancialTotals.IsOpenAt(voided, AsOf));
    }

    [Fact]
    public void SummaryTotalsSql_gates_balance_and_outstanding_false_so_count_never_invokes_mabna_functions()
    {
        var scope = new VendorFinancialScope(
            From: new DateOnly(2026, 1, 1),
            To: new DateOnly(2026, 1, 30),
            AsOf: new DateOnly(2026, 1, 30),
            FromKey: 14050101,
            ToKey: 14050130,
            HasPrevious: true,
            PreviousFrom: new DateOnly(2025, 1, 1),
            PreviousTo: new DateOnly(2025, 1, 30),
            PreviousAsOf: new DateOnly(2025, 1, 30),
            PreviousFromKey: 14040101,
            PreviousToKey: 14040130,
            Search: "تست",
            CodeUsable: true,
            Basis: "standard");

        var sql = VendorFinancialSql.SummaryTotalsSql(scope);

        Assert.Contains("SELECT count(*) FROM directory", sql);
        Assert.DoesNotContain("mabna.fn_vendor_balance", sql);
        Assert.DoesNotContain("mabna.fn_outstanding_payments", sql);
    }
}
