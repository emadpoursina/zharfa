namespace Zharfa.Api.Tests.Purchasing;

using System;
using System.Collections.Generic;
using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.Purchasing;

/// <summary>
/// T049 (spec 003 US4, FR-014/FR-015/FR-017): the source side of the reconciliation. Pure: the rows are
/// built in memory, so no database or network is involved.
/// </summary>
public class SourceTotalsCalculatorTests
{
    private const int Year = 1405;
    private const int July = 7;
    private const int August = 8;

    [Fact]
    public void Net_is_invoices_minus_returns_and_vat_is_invoice_vat_minus_return_vat()
    {
        var documents = new List<DocumentRecord>
        {
            Header("I1", "invoice", V1, 1405, July),
            Header("R1", "return", V1, 1405, July),
        };
        var lines = new List<LineRecord>
        {
            Line("I1", "L1", P1, 100m, 0m, 9m),
            Line("I1", "L2", P2, 200m, 20m, 18m),
            Line("R1", "RL1", P1, 50m, 0m, 4.5m),
        };

        var totals = Compute(documents, lines);

        Assert.Equal(280m, totals.Gross);
        Assert.Equal(50m, totals.Returns);
        Assert.Equal(230m, totals.Net);
        Assert.Equal(27m, totals.VatInvoice);
        Assert.Equal(4.5m, totals.VatReturn);
        Assert.Equal(22.5m, totals.Vat);
        Assert.Equal(2, totals.DocumentCount);
    }

    [Fact]
    public void Cancelled_documents_are_excluded_from_every_figure_and_from_the_count()
    {
        var documents = new List<DocumentRecord>
        {
            Header("I1", "invoice", V1, Year, July),
            Header("C1", "invoice", V1, Year, July, cancelled: true),
        };
        var lines = new List<LineRecord>
        {
            Line("I1", "L1", P1, 100m, 0m, 9m),
            Line("C1", "CL1", P1, 1000m, 0m, 90m),
        };

        var totals = Compute(documents, lines);

        Assert.Equal(100m, totals.Gross);
        Assert.Equal(9m, totals.VatInvoice);
        Assert.Equal(1, totals.DocumentCount);
    }

    [Fact]
    public void A_return_counts_in_its_own_month_not_in_the_month_of_its_invoice()
    {
        var documents = new List<DocumentRecord>
        {
            Header("I1", "invoice", V1, Year, July),
            Header("R2", "return", V1, Year, August),
        };
        var lines = new List<LineRecord>
        {
            Line("I1", "L1", P1, 100m, 0m, null),
            Line("R2", "RL2", P1, 30m, 0m, null),
        };

        var july = Compute(documents, lines, month: July);
        var august = Compute(documents, lines, month: August);

        Assert.Equal(100m, july.Gross);
        Assert.Equal(0m, july.Returns);
        Assert.Equal(0m, august.Gross);
        Assert.Equal(30m, august.Returns);
        Assert.Equal(-30m, august.Net);
    }

    [Fact]
    public void Vendor_filter_applies_to_the_header_and_product_filter_to_the_line()
    {
        var documents = new List<DocumentRecord>
        {
            Header("I1", "invoice", V1, Year, July),
            Header("I2", "invoice", V2, Year, July),
        };
        var lines = new List<LineRecord>
        {
            Line("I1", "L1", P1, 100m, 0m, null),
            Line("I1", "L2", P2, 40m, 0m, null),
            Line("I2", "L3", P1, 500m, 0m, null),
        };

        var vendorOnly = Compute(documents, lines, vendor: V1);
        var productOnly = Compute(documents, lines, product: P1);
        var both = Compute(documents, lines, vendor: V1, product: P1);

        Assert.Equal(140m, vendorOnly.Gross);
        Assert.Equal(1, vendorOnly.DocumentCount);
        Assert.Equal(600m, productOnly.Gross);
        Assert.Equal(100m, both.Gross);
    }

    [Fact]
    public void Under_line_and_document_the_discount_allocation_uses_every_line_before_the_product_filter()
    {
        // Document discount 10 over lines 100 (P1) and 180 (P2): the P1 line carries 3.5714 of it.
        var documents = new List<DocumentRecord> { Header("I1", "invoice", V1, Year, July, docDiscount: 10m) };
        var lines = new List<LineRecord>
        {
            Line("I1", "L1", P1, 100m, 0m, null),
            Line("I1", "L2", P2, 180m, 0m, null),
        };

        var all = Compute(documents, lines, basis: PurchasingDiscountBasis.LineAndDocument);
        var p1 = Compute(documents, lines, product: P1, basis: PurchasingDiscountBasis.LineAndDocument);

        Assert.Equal(270m, all.Gross);
        Assert.Equal(96.4286m, p1.Gross);
    }

    [Fact]
    public void Vat_is_null_when_the_vat_column_is_not_mapped()
    {
        var documents = new List<DocumentRecord> { Header("I1", "invoice", V1, Year, July) };
        var lines = new List<LineRecord> { Line("I1", "L1", P1, 100m, 0m, null) };

        var totals = Compute(documents, lines, vatMapped: false);

        Assert.Null(totals.VatInvoice);
        Assert.Null(totals.VatReturn);
        Assert.Null(totals.Vat);
        Assert.Equal(100m, totals.Gross);
    }

    [Fact]
    public void Lines_without_a_header_or_with_both_header_types_are_not_purchase_lines()
    {
        var documents = new List<DocumentRecord>
        {
            Header("I1", "invoice", V1, Year, July),
            Header("SAME", "invoice", V1, Year, July),
            Header("SAME", "return", V1, Year, July),
        };
        var lines = new List<LineRecord>
        {
            Line("I1", "L1", P1, 100m, 0m, null),
            Line("NOHEADER", "X1", P1, 999m, 0m, null),
            Line("SAME", "X2", P1, 777m, 0m, null),
        };

        var totals = Compute(documents, lines);

        Assert.Equal(100m, totals.Gross);
        Assert.Equal(0m, totals.Returns);
    }

    [Fact]
    public void Amounts_are_exact_decimals()
    {
        // 0.1 + 0.2 is not exact in binary floating point; the calculator must return exactly 0.3.
        var documents = new List<DocumentRecord> { Header("I1", "invoice", V1, Year, July) };
        var lines = new List<LineRecord>
        {
            Line("I1", "L1", P1, 0.1000m, 0m, null),
            Line("I1", "L2", P1, 0.2000m, 0m, null),
        };

        var totals = Compute(documents, lines);

        Assert.Equal(0.3m, totals.Gross);
        Assert.Equal(0.3m, totals.Net);
    }

    [Fact]
    public void Amounts_are_rounded_to_four_decimals_as_storage_does()
    {
        var documents = new List<DocumentRecord> { Header("I1", "invoice", V1, Year, July) };
        var lines = new List<LineRecord> { Line("I1", "L1", P1, 0.00005m, 0m, null) };

        var totals = Compute(documents, lines);

        Assert.Equal(0.0001m, totals.Gross);
    }

    [Fact]
    public void Esfand_30_of_a_leap_year_is_counted_in_its_own_month_only()
    {
        Assert.True(SourceDateParser.TryParse("1403/12/30", SourceDateParser.JalaliTextFormat, out var date));
        var documents = new List<DocumentRecord>
        {
            new("E1", "invoice", null, V1, date.Gregorian, date.JalaliYear, date.JalaliMonth, date.JalaliDay, false, 0m),
        };
        var lines = new List<LineRecord> { Line("E1", "L1", P1, 75m, 0m, null) };

        Assert.Equal(75m, Compute(documents, lines, year: 1403, month: 12).Gross);
        Assert.Equal(0m, Compute(documents, lines, year: 1404, month: 1).Gross);
    }

    [Fact]
    public void A_duplicate_line_key_keeps_the_last_row_read()
    {
        var documents = new List<DocumentRecord> { Header("I1", "invoice", V1, Year, July) };
        var lines = new List<LineRecord>
        {
            Line("I1", "L1", P1, 100m, 0m, null),
            Line("I1", "L1", P1, 130m, 0m, null),
        };

        Assert.Equal(130m, Compute(documents, lines).Gross);
    }

    private static PurchaseTotals Compute(
        IReadOnlyList<DocumentRecord> documents,
        IReadOnlyList<LineRecord> lines,
        int year = Year,
        int month = July,
        string? vendor = null,
        string? product = null,
        string basis = PurchasingDiscountBasis.Line,
        bool vatMapped = true) =>
        SourceTotalsCalculator.Compute(documents, lines, year, month, vendor, product, basis, vatMapped);

    private const string V1 = "V1";
    private const string V2 = "V2";
    private const string P1 = "P1";
    private const string P2 = "P2";

    private static DocumentRecord Header(
        string key,
        string type,
        string vendor,
        int year,
        int month,
        bool cancelled = false,
        decimal docDiscount = 0m) =>
        new(key, type, null, vendor, new DateOnly(2026, 10, 1), year, month, 1, cancelled, docDiscount);

    private static LineRecord Line(string document, string key, string product, decimal gross, decimal discount, decimal? vat) =>
        new(document, key, product, null, gross, discount, vat);
}
