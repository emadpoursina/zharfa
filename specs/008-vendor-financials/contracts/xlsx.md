# Contract: Vendor-financial Excel (Phase 6)

Uses Phase 5 `ExcelExportService` and `specs/007-ai-reports-export/contracts/xlsx.md` unchanged (RTL sheet «گزارش», meta rows 1–8, Persian headers, 10,000-row cap, formula escape, demo label, last-sync, period, filters, export time).

## Sheets

| Export | Headers (Persian) | Data |
|---|---|---|
| Financials list | نام، کد، مانده تأمین‌کننده، پرداخت‌های معوق (+ previous/delta/percent when compare) | §1 rows |
| Payment history | تاریخ پرداخت، مرجع، نوع سند، مبلغ | §3 rows |
| Outstanding | تاریخ، مرجع، نوع سند، مبلغ معوق | §4 rows |
| Purchases vs payments | خالص خرید، پرداخت‌ها، خرید منهای پرداخت (+ previous when compare) | one row (two if compare) |

No header may contain سود، حاشیه، وجه نقد دریافت‌شده، or use خرید for a payment column.

Negative مانده: stored as a leading `-`; `EscapeFormula` already prefixes `'`.

Demo: meta `dataLabel` / demonstration sentence as Phase 5. Production: no synthetic rows.

Permission failure: 403, **empty body** (not xlsx), `access.denied`.
