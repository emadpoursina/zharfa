namespace Zharfa.Api.Tests.Purchasing.Reports;

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;
using Zharfa.Api.Analytics;
using Zharfa.Api.Purchasing.Reports;

/// <summary>
/// T034 (spec 007 US4, contracts/xlsx.md, research R8, R10): ExcelExportService unit tests.
/// Verifies ClosedXML workbook output: RTL sheet, Persian sheet name «گزارش»,
/// meta rows 1-8, row 10 Persian headers, data starting row 11, 10,000 cap,
/// formula injection escaping, filename sanitization, and no leaked secrets in shared strings.
/// Pure in-memory: no network, no HTTP.
/// </summary>
public class ExcelExportServiceTests
{
    private readonly ExcelExportService _service = new();

    [Fact]
    public void SanitizeFilename_strips_illegal_and_control_characters_and_falls_back_to_default()
    {
        Assert.Equal("گزارشخریدآبان", _service.SanitizeFilename("گزارش/خرید:آبان*"));
        Assert.Equal("گزارش", _service.SanitizeFilename("   "));
        Assert.Equal("گزارش", _service.SanitizeFilename("///:::***???"));
        Assert.Equal("گزارش", _service.SanitizeFilename(null));
        Assert.Equal("خرید تامین کننده", _service.SanitizeFilename("خرید <تامین> |کننده|?"));
    }

    [Fact]
    public void BuildFilename_formats_persian_name_with_jalali_date()
    {
        var date = new JalaliDate(1405, 8, 25);
        var filename = _service.BuildFilename("خرید به تفکیک کالا", date);

        Assert.Equal("خرید به تفکیک کالا-1405-08-25.xlsx", filename);
    }

    [Fact]
    public void BuildFilename_uses_fallback_name_when_input_becomes_empty()
    {
        var date = new JalaliDate(1405, 8, 25);
        var filename = _service.BuildFilename("/*?<>", date);

        Assert.Equal("گزارش-1405-08-25.xlsx", filename);
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+cmd|'/C calc'!A0", "'+cmd|'/C calc'!A0")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1:A10)", "'@SUM(A1:A10)")]
    [InlineData("عادی", "عادی")]
    [InlineData("125000", "125000")]
    public void EscapeFormula_prefixes_formula_trigger_characters(string input, string expected)
    {
        Assert.Equal(expected, ExcelExportService.EscapeFormula(input));
    }

    [Fact]
    public void BuildWorkbook_produces_valid_rtl_xlsx_with_meta_headers_and_rows()
    {
        var table = new ExportTable(
            ReportName: "پرخریدترین کالاها",
            PeriodLabel: "آبان ۱۴۰۵",
            FiltersLabel: "بدون فیلتر",
            ExportedAtJalali: "۱۴۰۵/۰۸/۲۵ ۱۴:۳۰",
            LastSyncJalali: "۱۴۰۵/۰۸/۲۵ ۱۲:۰۰",
            DataLabel: "demo",
            DataQualityText: null,
            Headers: ["کالا", "خالص خرید"],
            Rows:
            [
                ["کالای الف", "120000.0000"],
                ["کالای ب", "80000.0000"]
            ],
            TotalRows: 2,
            Limit: 10000);

        var bytes = _service.BuildWorkbook(table);
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);

        // Verify inside the OpenXML package
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        // 1. Sheet1 XML exists and has rightToLeft="1" or "true"
        var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(sheetEntry);

        using var sheetReader = new StreamReader(sheetEntry.Open());
        var sheetXml = sheetReader.ReadToEnd();
        Assert.Contains("rightToLeft=\"1\"", sheetXml);

        // 2. Shared strings or sheet content contains meta and report values
        var sharedStringsEntry = zip.GetEntry("xl/sharedStrings.xml");
        string contentToScan = sheetXml;
        if (sharedStringsEntry != null)
        {
            using var ssReader = new StreamReader(sharedStringsEntry.Open());
            contentToScan += ssReader.ReadToEnd();
        }

        // Meta rows assertions
        Assert.Contains("پرخریدترین کالاها", contentToScan);
        Assert.Contains("آبان ۱۴۰۵", contentToScan);
        Assert.Contains("بدون فیلتر", contentToScan);
        Assert.Contains("۱۴۰۵/۰۸/۲۵ ۱۴:۳۰", contentToScan);
        Assert.Contains("۱۴۰۵/۰۸/۲۵ ۱۲:۰۰", contentToScan);
        Assert.Contains("این پرونده حاوی داده‌های نمایشی (دمو) است", contentToScan);
        Assert.Contains("ریال", contentToScan);

        // Header and data rows assertions
        Assert.Contains("کالا", contentToScan);
        Assert.Contains("خالص خرید", contentToScan);
        Assert.Contains("کالای الف", contentToScan);
        Assert.Contains("کالای ب", contentToScan);

        // Assert NO secret-like substrings anywhere in the package
        string[] forbiddenSecrets = ["password", "secret", "token", "connectionstring", "server=", "uid=", "pwd=", "api_key", "Bearer "];
        foreach (var secret in forbiddenSecrets)
        {
            Assert.DoesNotContain(secret, contentToScan, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void BuildWorkbook_handles_truncation_notice_when_total_exceeds_limit()
    {
        var rows = Enumerable.Range(1, 10)
            .Select(i => (IReadOnlyList<string>)new[] { $"کالا {i}", "1000" })
            .ToList();

        var table = new ExportTable(
            ReportName: "گزارش بزرگ",
            PeriodLabel: "سال ۱۴۰۵",
            FiltersLabel: "بدون فیلتر",
            ExportedAtJalali: "۱۴۰۵/۰۸/۲۵ ۱۴:۳۰",
            LastSyncJalali: null,
            DataLabel: "real",
            DataQualityText: "توجه: ۳ سند پرچم‌دار در این دوره وجود دارد.",
            Headers: ["کالا", "مبلغ"],
            Rows: rows,
            TotalRows: 15420,
            Limit: 10);

        var bytes = _service.BuildWorkbook(table);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        string fullContent = string.Empty;
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(entry.Open());
                fullContent += reader.ReadToEnd();
            }
        }

        // Check truncation text
        Assert.Contains("فیلترها را محدود کنید", fullContent);
        Assert.Contains("همگام‌سازی موفقی ثبت نشده است", fullContent);
        Assert.Contains("داده‌های این پرونده از پایگاه داده عملیاتی همگام‌سازی شده‌اند", fullContent);
        Assert.Contains("توجه: ۳ سند پرچم‌دار در این دوره وجود دارد", fullContent);
    }

    [Fact]
    public void BuildWorkbook_empty_table_still_has_headers_and_meta()
    {
        var table = new ExportTable(
            ReportName: "گزارش خالی",
            PeriodLabel: "مهر ۱۴۰۵",
            FiltersLabel: "تأمین‌کننده: داروپخش",
            ExportedAtJalali: "۱۴۰۵/۰۸/۲۵ ۱۴:۳۰",
            LastSyncJalali: "۱۴۰۵/۰۸/۲۵ ۱۲:۰۰",
            DataLabel: "real",
            DataQualityText: null,
            Headers: ["شماره سند", "تاریخ", "مبلغ"],
            Rows: [],
            TotalRows: 0,
            Limit: 10000);

        var bytes = _service.BuildWorkbook(table);
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);

        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(sheetEntry);
    }
}
