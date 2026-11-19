# API contract: Vendor Financials (Phase 6)

Extends Phase 3 `specs/005-permissions-audit-quality/contracts/api.md` §0 (reserved vendor-financials rows) and Phase 5 export/saved-report patterns. Cookie `zharfa_session`. Error envelope `{ "message": "<Persian>" }` plus optional `code`. Amounts are decimal strings (4 places). `dataLabel`: `real` | `demo`. Times: ISO `at` + Jalali `atJalali` as elsewhere.

Phase 2 purchasing endpoints, Phase 4 `POST /api/ai/chat` request shape, and Phase 5 print twins are **unchanged**. Chat 200 may now include financial facts/visuals for permitted callers (see [ai-tools.md](ai-tools.md)).

Common statuses: unsigned 401; missing permission 403 `code: forbidden` + `access.denied`; password-change 403 `password-change-required`.

## §0. Permission matrix (additions)

Existing rows stay. `GET /api/vendor-financials/summary` and `GET /api/vendor-financials/vendors/{vendorKey}` remain `vendor.financials` (Admin 403, Purchasing 403, Management/Finance ✓). New actions:

| Endpoint(s) | Policies (all must pass) | admin | management | purchasing | finance |
|---|---|:-:|:-:|:-:|:-:|
| `GET /api/vendor-financials/vendors/{vendorKey}/history` | `vendor.financials` | 403 | ✓ | 403 | ✓ |
| `GET /api/vendor-financials/vendors/{vendorKey}/outstanding` | `vendor.financials` | 403 | ✓ | 403 | ✓ |
| `GET /api/vendor-financials/summary/export` | `vendor.financials` then `export` | 403 | ✓ | 403 | ✓ |
| `GET /api/vendor-financials/vendors/{vendorKey}/history/export` | same | 403 | ✓ | 403 | ✓ |
| `GET /api/vendor-financials/vendors/{vendorKey}/outstanding/export` | same | 403 | ✓ | 403 | ✓ |
| `GET /api/vendor-financials/vendors/{vendorKey}/purchases-vs-payments/export` | same | 403 | ✓ | 403 | ✓ |
| `POST /api/saved-reports` `kind` financial | class `purchasing.reports` + runtime `vendor.financials` | 403 | ✓ | 403 | ✓ |
| `POST /api/saved-reports/{id}/open` financial kind | current `vendor.financials` | 403 | ✓ | 403 | ✓ |
| `GET /api/saved-reports/{id}/export` financial kind | open rules **and** `export` | 403 | ✓ | 403 | ✓ |
| `POST /api/ai/chat` financial intent | `ai.query`; tools only if also `vendor.financials` | 403 | ✓ tools | ✓ no figures | ✓ tools |

`GET /api/system/status` adds `modules.vendorFinancials`: true iff `vendor.financials`.

`PermissionMatrixTests` must list every new action. Purchasing may call no action whose policy set contains `vendor.financials`. Admin may call no `vendor.financials` action.

---

## §1. Financials list — `GET /api/vendor-financials/summary`

Query: same period/search/page/sort/compare as Phase 2 vendor directory (`kind`, `month`/`quarter`/`year`/`from`/`to`, `q`, `page`, `pageSize`, `sort`, `dir`, `compare`). Default sort `vendorBalance` desc. `sort` allowlist: `name`, `code`, `vendorBalance`, `outstandingPayments`.

`[AuditReportOpen("vendor-financials")]` — paging/sorting alone does not add a second entry (Phase 3 tracker).

**200** when the caller is permitted (even if figures are unavailable):

```json
{
  "dataLabel": "demo",
  "demoNotice": "…",
  "lastSuccessfulSync": { "at": "…", "atJalali": "…" },
  "currency": "IRR",
  "currencyLabel": "ریال",
  "period": { "kind": "month", "month": "1405-08", "from": "1405-08-01", "to": "1405-08-30", "label": "…" },
  "previousPeriod": null,
  "filters": [],
  "unavailable": [ { "figure": "vendorBalance", "reason": "formula-unconfirmed", "message": "داده در دسترس نیست." } ],
  "compare": false,
  "page": 1,
  "pageSize": 50,
  "totalRows": 12,
  "search": { "q": "", "normalized": "" },
  "sort": { "key": "vendorBalance", "dir": "desc" },
  "dataQuality": null,
  "available": true,
  "rows": [
    {
      "key": "V-1",
      "name": "…",
      "code": "1001",
      "vendorBalance": "-1500000.0000",
      "outstandingPayments": "0.0000",
      "previousVendorBalance": null,
      "previousOutstandingPayments": null,
      "deltaVendorBalance": null,
      "percentVendorBalance": null,
      "deltaOutstanding": null,
      "percentOutstanding": null
    }
  ]
}
```

`available: true` means the endpoint is implemented (Phase 6). Per-figure holes use `unavailable[]` and null amounts on those columns — **not** the Phase 3 stub `{ available: false, reason: "not-available-until-phase-6" }`.

When a figure is unavailable, its amount properties are `null` (not `"0"`). When usable and no activity: `"0.0000"`. Negative balance is a leading `-`. Comparison fields omitted/null when `compare` is false or previous base is missing (`no-previous-data` on that figure; zeros in the current period stay zeros).

Print: `GET /api/vendor-financials/summary/print` — existing print envelope, cap 1,000, `report.printed`.

Export: `GET /api/vendor-financials/summary/export` — [xlsx.md](xlsx.md).

---

## §2. Vendor financial detail — `GET /api/vendor-financials/vendors/{vendorKey}`

Query: period + optional `compare` (applies only to purchases vs payments). Unknown vendor → 404 `{ "message": "تأمین‌کننده یافت نشد." }`.

`[AuditReportOpen("vendor-financials-detail")]`.

**200** header (no paged document lists — those are §3–§4):

```json
{
  "dataLabel": "demo",
  "lastSuccessfulSync": { "at": "…", "atJalali": "…" },
  "currency": "IRR",
  "currencyLabel": "ریال",
  "period": { },
  "previousPeriod": null,
  "vendor": { "key": "V-1", "name": "…", "code": "1001" },
  "unavailable": [],
  "dataQuality": null,
  "available": true,
  "vendorBalance": "25000000.0000",
  "outstandingPayments": "8000000.0000",
  "purchasesVsPayments": {
    "netPurchases": "40000000.0000",
    "payments": "15000000.0000",
    "purchasesMinusPayments": "25000000.0000",
    "previousNetPurchases": null,
    "previousPayments": null,
    "deltaNetPurchases": null,
    "percentNetPurchases": null,
    "deltaPayments": null,
    "percentPayments": null
  }
}
```

`purchasesMinusPayments` is omitted when either side is unavailable. It MUST NOT be named profit in `message`, labels, or export headers (Persian header «خرید منهای پرداخت»).

This payload is what the **vendor profile** uses for F5 AC3 (balance = list amount for the same vendor and period). Purchasing profile GET is unchanged and contains no financial fields.

---

## §3. Payment history — `GET /api/vendor-financials/vendors/{vendorKey}/history`

Query: period, `page`, `pageSize`, `sort` (`paidAt` | `amount` | `reference`, default `paidAt` desc). **No `compare`** — if present and truthy → 400 `{ "message": "مقایسه دوره برای تاریخچه پرداخت در دسترس نیست.", "code": "compare-not-allowed" }`.

`[AuditReportOpen("vendor-financials-history")]`.

**200** `ReportTablePage` shape: rows `{ "paidAtJalali", "reference", "instrumentType", "instrumentTypeLabel", "amount" }`. Sum of `amount` over the **unpaged** filtered set equals `purchasesVsPayments.payments` when payments are available. Empty list when zero activity.

Export twin: `…/history/export`.

---

## §4. Outstanding instruments — `GET /api/vendor-financials/vendors/{vendorKey}/outstanding`

As-of = period end. Query: page/sort (`date` | `amount` | `reference`). `compare` refused as in §3.

`[AuditReportOpen("vendor-financials-outstanding")]`.

Rows: `{ "dateJalali", "reference", "instrumentType", "instrumentTypeLabel", "unpaidAmount" }`. Sum of `unpaidAmount` (unpaged) equals detail `outstandingPayments` when that figure is available.

Export twin: `…/outstanding/export`.

---

## §5. Purchases vs payments export

`GET /api/vendor-financials/vendors/{vendorKey}/purchases-vs-payments/export` — one/two-row sheet of the §2 `purchasesVsPayments` object (current + previous when compare). Same Excel meta as other twins.

---

## §6. Saved reports

`POST /api/saved-reports` body `kind` may be the four financial kinds. Server sets `requiredPermission: "vendor.financials"`. Caller without that permission → 403, no row. Duplicate name 409 `saved-report-exists` unchanged.

`POST …/open` financial kind: if current role lacks `vendor.financials` → 403 `{ "message": "برای مشاهده این گزارش به دسترسی «اطلاعات مالی تأمین‌کنندگان» نیاز است.", "code": "forbidden" }`, no query, `access.denied`. Management/Finance reopen on **current** synced data.

Generated financial visual: reopen also requires `ai.query`.

---

## §7. System status

`modules` JSON adds `"vendorFinancials": true|false`. Existing flags unchanged. `permissions` array already includes `vendor.financials` for Management/Finance.

---

## §8. SPA hashes

| Hash | Who sees the tab |
|---|---|
| `#/financials` | `modules.vendorFinancials` |
| `#/vendors` profile | all with purchasing reports; balance block only if `vendorFinancials` |

Direct `#/financials` without permission: SPA `NoAccess`; API still 403.
