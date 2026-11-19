namespace Zharfa.Api.Tests.VendorFinancials;

using System.Linq;
using System.Text.Json;
using Zharfa.Api.VendorFinancials;

/// <summary>
/// T028 (contracts/api.md §1, research R3/R7): how the financial DTOs serialize — signed credit with a leading
/// '-', zero activity as "0.0000" (never null), an unavailable figure as JSON null plus its reason in
/// `unavailable[]`, ریال as the currency label, and no profit-like wording anywhere in the payload.
/// Pure: no database, no network.
/// </summary>
public class VendorFinancialDtoTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static VendorFinancialPeriodDto Period() =>
        new("month", "1405-08", "1405-08-01", "1405-08-30", "آبان ۱۴۰۵");

    private static VendorFinancialsSummaryResponse Summary(
        string? balance, string? outstanding,
        VendorFinancialUnavailable? unavailable = null) =>
        new(
            DataLabel: "demo",
            DemoNotice: VendorFinancialsService.DemoNotice,
            LastSuccessfulSync: new Zharfa.Api.Purchasing.Reports.ReportSyncTime("2026-11-21T00:00:00+00:00", "۱۴۰۵/۰۸/۳۰"),
            Currency: VendorFinancialDtos.CurrencyCode,
            CurrencyLabel: VendorFinancialDtos.CurrencyLabel,
            Period: Period(),
            PreviousPeriod: null,
            Filters: [],
            Unavailable: unavailable is null ? [] : [unavailable],
            Compare: false,
            Page: 1,
            PageSize: 25,
            TotalRows: 2,
            Search: new VendorFinancialSearchDto(string.Empty, string.Empty),
            Sort: new VendorFinancialSortDto("vendorBalance", "desc"),
            DataQuality: null,
            Available: true,
            Rows:
            [
                new VendorFinancialSummaryRow("V-1", "تأمین‌کننده نمونه", "1001", balance, outstanding,
                    null, null, null, null, null, null),
                new VendorFinancialSummaryRow("V-2", "تأمین‌کننده بدون فعالیت", "1002", "0.0000", "0.0000",
                    null, null, null, null, null, null),
            ]);

    [Fact]
    public void Signed_credit_serializes_with_a_leading_minus()
    {
        var json = JsonSerializer.Serialize(Summary("-1500000.0000", "0.0000"), Web);

        using var document = JsonDocument.Parse(json);
        var row = document.RootElement.GetProperty("rows")[0];

        Assert.Equal("-1500000.0000", row.GetProperty("vendorBalance").GetString());
        Assert.StartsWith("-", row.GetProperty("vendorBalance").GetString());
        Assert.Equal("0.0000", row.GetProperty("outstandingPayments").GetString());
    }

    [Fact]
    public void Zero_activity_is_a_zero_string_and_never_null()
    {
        var json = JsonSerializer.Serialize(Summary("0.0000", "0.0000"), Web);

        using var document = JsonDocument.Parse(json);
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            Assert.Equal(JsonValueKind.String, row.GetProperty("vendorBalance").ValueKind);
            Assert.Equal("0.0000", row.GetProperty("vendorBalance").GetString());
            Assert.Equal(JsonValueKind.String, row.GetProperty("outstandingPayments").ValueKind);
            Assert.Equal("0.0000", row.GetProperty("outstandingPayments").GetString());
        }
    }

    [Fact]
    public void An_unavailable_figure_is_json_null_with_its_reason_in_unavailable()
    {
        var json = JsonSerializer.Serialize(
            Summary(null, null, VendorFinancialUnavailable.For("vendorBalance", "formula-unconfirmed")),
            Web);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("rows")[0].GetProperty("vendorBalance").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("rows")[0].GetProperty("outstandingPayments").ValueKind);

        var unavailable = root.GetProperty("unavailable")[0];
        Assert.Equal("vendorBalance", unavailable.GetProperty("figure").GetString());
        Assert.Equal("formula-unconfirmed", unavailable.GetProperty("reason").GetString());
        Assert.Equal("داده در دسترس نیست.", unavailable.GetProperty("message").GetString());
        Assert.False(root.GetProperty("available").GetBoolean() is false, "available must stay true: the endpoint is implemented.");
    }

    [Fact]
    public void Payload_states_the_rial_currency_and_the_demo_notice()
    {
        var json = JsonSerializer.Serialize(Summary("0.0000", "0.0000"), Web);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("IRR", root.GetProperty("currency").GetString());
        Assert.Equal("ریال", root.GetProperty("currencyLabel").GetString());
        Assert.Equal("demo", root.GetProperty("dataLabel").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("demoNotice").GetString()));
    }

    [Theory]
    [InlineData("سود")]
    [InlineData("حاشیه")]
    [InlineData("وجه نقد")]
    public void No_payload_key_or_value_never_names_profit_margin_or_cash_received(string forbidden)
    {
        var json = JsonSerializer.Serialize(Summary("-1.0000", "0.0000"), Web);

        Assert.DoesNotContain(forbidden, json);
    }

    [Fact]
    public void The_purchases_minus_payments_difference_is_never_named_profit()
    {
        var pvp = new VendorPurchasesVsPaymentsDto(
            NetPurchases: "40000000.0000",
            Payments: "15000000.0000",
            PurchasesMinusPayments: "25000000.0000",
            PreviousNetPurchases: null,
            PreviousPayments: null,
            DeltaNetPurchases: null,
            PercentNetPurchases: null,
            DeltaPayments: null,
            PercentPayments: null);

        var json = JsonSerializer.Serialize(pvp, Web);

        Assert.Contains("purchasesMinusPayments", json);
        Assert.DoesNotContain("سود", json);
        Assert.DoesNotContain("حاشیه", json);
        Assert.DoesNotContain("وجه نقد", json);
    }

    [Fact]
    public void The_minus_difference_is_omitted_when_either_side_is_unavailable()
    {
        var pvp = new VendorPurchasesVsPaymentsDto(
            NetPurchases: null,
            Payments: "15000000.0000",
            PurchasesMinusPayments: null,
            PreviousNetPurchases: null,
            PreviousPayments: null,
            DeltaNetPurchases: null,
            PercentNetPurchases: null,
            DeltaPayments: null,
            PercentPayments: null);

        var json = JsonSerializer.Serialize(pvp, Web);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("purchasesMinusPayments", out _));
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("netPurchases").ValueKind);
    }

    [Fact]
    public void History_and_outstanding_rows_carry_the_persian_labels()
    {
        var history = new PaymentHistoryRow("۱۴۰۵/۰۷/۰۸", "CHQ-1", "cheque", "چک", "5000000.0000");
        var outstanding = new OutstandingInstrumentRow("۱۴۰۵/۰۶/۰۱", "CHQ-5", "cheque", "چک", "2000000.0000");

        var historyJson = JsonSerializer.Serialize(history, Web);
        var outstandingJson = JsonSerializer.Serialize(outstanding, Web);

        Assert.Contains("paidAtJalali", historyJson);
        Assert.Contains("unpaidAmount", outstandingJson);
        Assert.Contains("instrumentTypeLabel", historyJson);
        Assert.DoesNotContain("سود", historyJson + outstandingJson);
        Assert.Equal("5000000.0000", JsonDocument.Parse(historyJson).RootElement.GetProperty("amount").GetString());
    }

    [Fact]
    public void Jalali_date_conversion_formats_genuine_jalali_date_strings_not_gregorian()
    {
        // T067 / FR-017: DateOnly (Gregorian 2026-10-23) must convert to Jalali 1405-08-01, not Gregorian string
        var gregorianDate = new System.DateOnly(2026, 10, 23);
        var jalali = Zharfa.Api.Analytics.JalaliDate.FromDate(gregorianDate);
        var formatted = $"{jalali.Year:0000}-{jalali.Month:00}-{jalali.Day:00}";

        Assert.Equal("1405-08-01", formatted);
        Assert.NotEqual("2026-10-22", formatted);
    }

    [Fact]
    public void The_summary_row_never_carries_a_purchasing_field()
    {
        var row = new VendorFinancialSummaryRow("V-1", "نام", null, "1.0000", "2.0000",
            null, null, null, null, null, null);
        var names = JsonSerializer.SerializeToElement(row, Web).EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal(
            new[]
            {
                "key", "name", "code", "vendorBalance", "outstandingPayments",
                "previousVendorBalance", "previousOutstandingPayments",
                "deltaVendorBalance", "percentVendorBalance", "deltaOutstanding", "percentOutstanding",
            },
            names);
        Assert.DoesNotContain("net", names);
    }
}
