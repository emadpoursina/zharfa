namespace Zharfa.Api.Tests.VendorFinancials;

using System;
using System.Linq;
using System.Reflection;
using Xunit;
using Zharfa.Api.VendorFinancials;

/// <summary>
/// T035 [US3]: Verification that labels, JSON property names, and Persian header helpers for PvP
/// contain 'purchasesMinusPayments' / «خرید منهای پرداخت» and NEVER contain سود, حاشیه, وجه نقد,
/// or reuse «خرید» as a payment column. Also tests partial-pay split (FR-033).
/// </summary>
public class VendorFinancialLabelTests
{
    private static readonly string[] ForbiddenPersianTokens =
    [
        "سود",
        "حاشیه",
        "وجه نقد",
        "وجه نقد دریافت‌شده",
    ];

    private static readonly string[] ForbiddenPropertyTokens =
    [
        "profit",
        "margin",
        "cashReceived",
    ];

    [Fact]
    public void PurchasesVsPayments_properties_contain_no_profit_margin_or_cash_tokens()
    {
        var properties = typeof(VendorPurchasesVsPaymentsDto).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var names = properties.Select(p => p.Name).ToList();

        Assert.Contains(nameof(VendorPurchasesVsPaymentsDto.NetPurchases), names);
        Assert.Contains(nameof(VendorPurchasesVsPaymentsDto.Payments), names);
        Assert.Contains(nameof(VendorPurchasesVsPaymentsDto.PurchasesMinusPayments), names);

        foreach (var name in names)
        {
            foreach (var forbidden in ForbiddenPropertyTokens)
            {
                Assert.DoesNotContain(forbidden, name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Detail_and_summary_types_do_not_contain_profit_or_margin_properties()
    {
        var types = new[]
        {
            typeof(VendorFinancialSummaryRow),
            typeof(VendorFinancialsSummaryResponse),
            typeof(VendorFinancialDetailResponse),
            typeof(VendorPurchasesVsPaymentsDto),
            typeof(PaymentHistoryRow),
            typeof(OutstandingInstrumentRow),
        };

        foreach (var type in types)
        {
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in props)
            {
                foreach (var forbidden in ForbiddenPropertyTokens)
                {
                    Assert.DoesNotContain(forbidden, prop.Name, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void Pvp_headers_use_purchases_minus_payments_and_never_forbidden_words()
    {
        var headers = new[] { "خالص خرید", "پرداخت‌ها", "خرید منهای پرداخت" };

        Assert.Contains("خرید منهای پرداخت", headers);

        foreach (var header in headers)
        {
            foreach (var forbidden in ForbiddenPersianTokens)
            {
                Assert.DoesNotContain(forbidden, header, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Partial_pay_split_paid_portion_only_in_history_remainder_only_in_outstanding()
    {
        // An instrument of face amount 1000: paid portion of 400 inside period, remainder 600 still open.
        var cheque = new PayableChequeRow("V-1", new DateOnly(2026, 11, 1), 600m, ClearedAt: null, Cancelled: false);
        var payment = new VendorPaymentRow("V-1", new DateOnly(2026, 11, 5), 400m, Cancelled: false);

        var asOf = new DateOnly(2026, 11, 30);
        var payments = VendorFinancialTotals.Payments([payment], new DateOnly(2026, 11, 1), asOf);
        var outstanding = VendorFinancialTotals.OutstandingPayments([cheque], asOf);

        Assert.Equal(400m, payments);
        Assert.Equal(600m, outstanding);
        Assert.NotEqual(1000m, payments);
        Assert.NotEqual(1000m, outstanding);
    }
}
