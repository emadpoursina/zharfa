namespace Zharfa.Api.Purchasing.Reports;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using Zharfa.Api.Analytics;

/// <summary>
/// Ephemeral data model passed to ClosedXML for building an .xlsx workbook (spec 007 data-model §5).
/// </summary>
public sealed record ExportTable(
    string ReportName,
    string PeriodLabel,
    string FiltersLabel,
    string ExportedAtJalali,
    string? LastSyncJalali,
    string DataLabel,
    string? DataQualityText,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    long TotalRows,
    int Limit = 10000);

/// <summary>
/// Server-side ClosedXML writer for RTL Persian Excel workbooks (spec 007 FR-014..FR-019, contracts/xlsx.md).
/// Enforces RTL sheet layout, sheet name «گزارش», formula injection protection,
/// meta area (rows 1-8), Persian headers (row 10), data rows from row 11 up to 10,000,
/// and file name sanitization.
/// </summary>
public class ExcelExportService
{
    private static readonly HashSet<char> IllegalFilenameChars = new(
        ['/', '\\', ':', '*', '?', '"', '<', '>', '|']);

    /// <summary>
    /// Escapes formula injection triggers (=, +, -, @) by prefixing with a single quote.
    /// </summary>
    public static string EscapeFormula(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var first = text[0];
        if (first is '=' or '+' or '-' or '@')
        {
            return $"'{text}";
        }

        return text;
    }

    /// <summary>
    /// Strips illegal file name characters and C0 control codes, trimming whitespace.
    /// Falls back to «گزارش» if empty.
    /// </summary>
    public string SanitizeFilename(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "گزارش";
        }

        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (ch < 32 || IllegalFilenameChars.Contains(ch))
            {
                continue;
            }
            sb.Append(ch);
        }

        var cleaned = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "گزارش" : cleaned;
    }

    /// <summary>
    /// Formats the download filename: {sanitizedName}-{jalaliYyyyMmDd}.xlsx.
    /// </summary>
    public string BuildFilename(string reportName, JalaliDate exportDate)
    {
        var sanitized = SanitizeFilename(reportName);
        var dateText = $"{exportDate.Year:D4}-{exportDate.Month:D2}-{exportDate.Day:D2}";
        return $"{sanitized}-{dateText}.xlsx";
    }

    /// <summary>
    /// Builds a ClosedXML workbook according to contracts/xlsx.md specifications and returns its raw bytes.
    /// </summary>
    public byte[] BuildWorkbook(ExportTable table)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("گزارش");
        ws.RightToLeft = true;

        // Row 1: Report name (Persian) and currency stated in meta as «ریال» (contracts/xlsx.md)
        ws.Cell(1, 1).Value = EscapeFormula(table.ReportName);
        ws.Cell(1, 2).Value = "واحد: ریال";

        // Row 2: Period label
        ws.Cell(2, 1).Value = EscapeFormula(table.PeriodLabel);

        // Row 3: Active filters (or «بدون فیلتر»)
        var filters = string.IsNullOrWhiteSpace(table.FiltersLabel) ? "بدون فیلتر" : table.FiltersLabel;
        ws.Cell(3, 1).Value = EscapeFormula(filters);

        // Row 4: Export time Jalali (yyyy/mm/dd HH:mm, Persian digits)
        ws.Cell(4, 1).Value = EscapeFormula(table.ExportedAtJalali);

        // Row 5: Last successful sync Jalali, or «همگام‌سازی موفقی ثبت نشده است»
        var syncText = string.IsNullOrWhiteSpace(table.LastSyncJalali)
            ? "همگام‌سازی موفقی ثبت نشده است"
            : table.LastSyncJalali;
        ws.Cell(5, 1).Value = EscapeFormula(syncText);

        // Row 6: Data-mode sentence: demonstration vs real
        var modeSentence = table.DataLabel == "demo"
            ? "این پرونده حاوی داده‌های نمایشی (دمو) است و ارزش اسنادی ندارد."
            : "داده‌های این پرونده از پایگاه داده عملیاتی همگام‌سازی شده‌اند.";
        ws.Cell(6, 1).Value = EscapeFormula(modeSentence);

        // Row 7: Data-quality notice text, or empty
        ws.Cell(7, 1).Value = EscapeFormula(table.DataQualityText ?? string.Empty);

        // Row 8: Truncation line when capped: «۱۰٬۰۰۰ ردیف نخست از N ردیف — فیلترها را محدود کنید.» else empty
        if (table.TotalRows > table.Limit)
        {
            var limitText = JalaliDate.ToPersianDigits($"{table.Limit:#,0}".Replace(',', '٬'));
            var totalText = JalaliDate.ToPersianDigits($"{table.TotalRows:#,0}".Replace(',', '٬'));
            ws.Cell(8, 1).Value = $"{limitText} ردیف نخست از {totalText} ردیف — فیلترها را محدود کنید.";
        }
        else
        {
            ws.Cell(8, 1).Value = string.Empty;
        }

        // Row 9: blank (leave empty)

        // Row 10: Column headers (Persian, match on-screen)
        for (int c = 0; c < table.Headers.Count; c++)
        {
            ws.Cell(10, c + 1).Value = EscapeFormula(table.Headers[c]);
        }

        // Row 11+: Data rows, max limit
        int rowIdx = 11;
        var dataRows = table.Rows.Take(table.Limit);
        foreach (var row in dataRows)
        {
            for (int colIdx = 0; colIdx < row.Count; colIdx++)
            {
                ws.Cell(rowIdx, colIdx + 1).Value = EscapeFormula(row[colIdx]);
            }
            rowIdx++;
        }

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }
}
