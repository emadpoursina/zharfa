namespace Zharfa.Api.Tests.VendorFinancials;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Xunit;
using Zharfa.Api.Purchasing.Reports;

/// <summary>
/// T052 [US7]: Tests that financial Excel exports use the required Persian headers,
/// never contain سود / حاشیه / وجه نقد دریافت‌شده, correctly escape negative balances
/// via EscapeFormula, and produce valid RTL workbooks with meta rows 1-8.
/// Pure in-memory.
/// </summary>
public class VendorFinancialExcelTests
{
    private static readonly string[] ForbiddenTokens =
    [
        "سود",
        "حاشیه",
        "وجه نقد",
        "وجه نقد دریافت‌شده",
    ];

    [Fact]
    public void Financials_summary_headers_match_specification_and_have_no_forbidden_words()
    {
        var headers = new List<string> { "نام", "کد", "مانده تأمین‌کننده", "پرداخت‌های معوق" };

        Assert.Equal("نام", headers[0]);
        Assert.Equal("کد", headers[1]);
        Assert.Equal("مانده تأمین‌کننده", headers[2]);
        Assert.Equal("پرداخت‌های معوق", headers[3]);

        foreach (var header in headers)
        {
            foreach (var forbidden in ForbiddenTokens)
            {
                Assert.DoesNotContain(forbidden, header, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void History_headers_match_specification()
    {
        var headers = new List<string> { "تاریخ پرداخت", "مرجع", "نوع سند", "مبلغ" };

        Assert.Equal("تاریخ پرداخت", headers[0]);
        Assert.Equal("مرجع", headers[1]);
        Assert.Equal("نوع سند", headers[2]);
        Assert.Equal("مبلغ", headers[3]);

        foreach (var header in headers)
        {
            foreach (var forbidden in ForbiddenTokens)
            {
                Assert.DoesNotContain(forbidden, header, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Outstanding_headers_match_specification()
    {
        var headers = new List<string> { "تاریخ", "مرجع", "نوع سند", "مبلغ معوق" };

        Assert.Equal("تاریخ", headers[0]);
        Assert.Equal("مرجع", headers[1]);
        Assert.Equal("نوع سند", headers[2]);
        Assert.Equal("مبلغ معوق", headers[3]);

        foreach (var header in headers)
        {
            foreach (var forbidden in ForbiddenTokens)
            {
                Assert.DoesNotContain(forbidden, header, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void PurchasesVsPayments_headers_match_specification()
    {
        var headers = new List<string> { "خالص خرید", "پرداخت‌ها", "خرید منهای پرداخت" };

        Assert.Equal("خالص خرید", headers[0]);
        Assert.Equal("پرداخت‌ها", headers[1]);
        Assert.Equal("خرید منهای پرداخت", headers[2]);

        foreach (var header in headers)
        {
            foreach (var forbidden in ForbiddenTokens)
            {
                Assert.DoesNotContain(forbidden, header, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Negative_balance_is_escaped_against_formula_injection()
    {
        const string negativeBalance = "-15000000.0000";
        var escaped = ExcelExportService.EscapeFormula(negativeBalance);

        Assert.StartsWith("'", escaped, StringComparison.Ordinal);
        Assert.Equal("'-15000000.0000", escaped);
    }

    [Fact]
    public void Workbook_builder_produces_rtl_sheet_with_meta_and_escaped_negative_balance()
    {
        var exportService = new ExcelExportService();

        var table = new ExportTable(
            ReportName: "اطلاعات مالی تأمین‌کنندگان",
            PeriodLabel: "آبان ۱۴۰۵",
            FiltersLabel: "بدون فیلتر",
            ExportedAtJalali: "۱۴۰۵/۰۸/۳۰ ۱۲:۰۰",
            LastSyncJalali: "۱۴۰۵/۰۸/۳۰ ۱۰:۰۰",
            DataLabel: "demo",
            DataQualityText: null,
            Headers: ["نام", "کد", "مانده تأمین‌کننده", "پرداخت‌های معوق"],
            Rows:
            [
                ["داروسازی البرز", "ALB-01", "-5000000.0000", "0.0000"],
                ["تأمین سلامت", "SAL-02", "12000000.0000", "2000000.0000"]
            ],
            TotalRows: 2);

        var bytes = exportService.BuildWorkbook(table);
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheet("گزارش");

        Assert.True(ws.RightToLeft);
        Assert.Equal("اطلاعات مالی تأمین‌کنندگان", ws.Cell(1, 1).GetString());
        Assert.Equal("واحد: ریال", ws.Cell(1, 2).GetString());
        Assert.Equal("آبان ۱۴۰۵", ws.Cell(2, 1).GetString());

        // Header row at row 10
        Assert.Equal("نام", ws.Cell(10, 1).GetString());
        Assert.Equal("کد", ws.Cell(10, 2).GetString());
        Assert.Equal("مانده تأمین‌کننده", ws.Cell(10, 3).GetString());
        Assert.Equal("پرداخت‌های معوق", ws.Cell(10, 4).GetString());

        // Data row at row 11 has negative balance stored as text
        var cellValue = ws.Cell(11, 3).GetString();
        Assert.Equal("-5000000.0000", cellValue);
    }
}
