# Excel file contract (Phase 5)

Produced only by `ExcelExportService` from a server-built `ExportTable`. Clients never upload cells.

## Workbook

- One worksheet, name «گزارش» (Excel sheet-name illegal characters stripped).
- `sheetFormatPr` / view: **rightToLeft = true** (ClosedXML `Worksheet.RightToLeft`).
- No embedded charts, macros, external links, or pivot caches.
- Strings written as text (no formula injection: if a cell would start with `=`, `+`, `-`, `@`, prefix with `' ` in the writer).

## Layout (rows 1…, RTL visually)

| Row | Content |
|---|---|
| 1 | Report name (Persian) |
| 2 | Period label |
| 3 | Active filters (or «بدون فیلتر») |
| 4 | Export time Jalali (`yyyy/mm/dd HH:mm`, Persian digits) |
| 5 | Last successful sync Jalali, or «همگام‌سازی موفقی ثبت نشده است» |
| 6 | Data-mode sentence: demonstration vs real (`dataLabel`) |
| 7 | Data-quality notice text, or empty |
| 8 | Truncation line when capped: «۱۰٬۰۰۰ ردیف نخست از N ردیف — فیلترها را محدود کنید.» else empty |
| 9 | blank |
| 10 | Column headers (Persian, match on-screen) |
| 11+ | Data rows, document order = current sort, max 10,000 |

Amounts in data cells: same decimal strings as the API, displayed with Persian digits in the writer. Currency stated in meta as «ریال», not as a second unit.

## Prohibitions

- No passwords, tokens, connection strings, provider keys.
- No document line dumps beyond the exported table’s own columns.
- No vendor balance/payment columns (Phase 6).
- Demo files must include the demonstration sentence on row 6; production files must not contain Demo 1 / simulator product names as a baked-in label (they may appear as real synced names only in production).
