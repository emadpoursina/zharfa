# HTTP API Contract: Vendor and Purchasing Reports (Phase 2)

Conventions are those of `specs/003-real-purchase-data/contracts/api.md`: base path `/api`, cookie session, JSON UTF-8, Persian error envelope `{ "message": "..." }`, amounts as exact decimal **strings** in Rial (4 decimals), an unavailable figure is `null` with its key in `unavailable[]` and a reason code (`no-successful-sync` | `source-item-unverified` | `source-item-unmapped` | `no-previous-data`).

**Authorization**: every endpoint below is `[Authorize(Roles = "manager,admin")]` (Demo 1 roles until Phase 3). 401 not signed in, 403 other role. Not `[DemoOnly]`: available in production and demo. No endpoint returns vendor balance, payment, credit or debt fields (FR-012).

**Status codes**: 200; 400 invalid period, filter, sort, page size, comparison selection or a filter not applicable to the report; 404 unknown vendor key on the profile or unknown document id; 503 when a query exceeds its 15 s timeout, with `{ "message": "گزارش در زمان مجاز آماده نشد؛ بازه را کوتاه‌تر کنید." }`.

---

## 0. Common parameters and envelope

### Period (all endpoints)

| Parameter | Values |
|---|---|
| `period` | `month` (default) \| `quarter` \| `year` \| `custom` |
| `month` | `YYYY-MM` (with `period=month`; default current Jalali month) |
| `quarter` | `YYYY-Qn`, n ∈ 1..4 |
| `year` | `YYYY` |
| `from`, `to` | `YYYY-MM-DD` Jalali, inclusive (with `period=custom`) |
| `compare` | `true` \| `false` (default `false`). Accepted on the directory (§1) and on `by-vendor`, `by-product`, `by-category`, `trend`, `top-products`, `returns` (§3); refused with 400 on `transactions`, documents (§5) and the vendor comparison (§4); ignored on the profile, which always compares (spec FR-020, data-model §4) |

Latin, Persian or Arabic-Indic digits accepted. Errors (400): bad format, month/quarter/day out of range, Esfand 30 in a non-leap year, `to < from`, whole period in the future, custom range > 1,098 days.

### Filters (where the report declares them; see data-model §4 catalog)

`vendor`, `product`, `category`: source key or `~unknown`. `month`: `YYYY-MM` within the period (drill-down of a trend row). `docType`: `invoice` | `return`. `q`: search text (≤ 100 chars), matched against the report's search target (data-model §4 "Search on"). `match`: documents list only (§5), the trend search carried by a trend row's `drill`. `sort`: key from the report's allowlist. `dir`: `asc` | `desc`. `page` (≥ 1), `pageSize` (25 | 50 | 100, default 25).

### Envelope (every data response)

```json
{
  "dataLabel": "real",
  "lastSuccessfulSync": { "at": "2026-11-16T02:04:11+03:30", "atJalali": "۱۴۰۵/۰۸/۲۵ ۰۲:۰۴" },
  "currency": "IRR",
  "currencyLabel": "ریال",
  "discountBasis": "line",
  "period": {
    "kind": "month", "from": "1405-08-01", "to": "1405-08-30", "label": "آبان ۱۴۰۵",
    "trimmedTo": null
  },
  "previousPeriod": { "kind": "month", "from": "1405-07-01", "to": "1405-07-30", "label": "مهر ۱۴۰۵" },
  "filters": [ { "name": "vendor", "value": "V-102", "label": "شرکت دارویی نمونه" } ],
  "unavailable": [ { "figure": "category", "reason": "source-item-unverified" } ]
}
```

`previousPeriod` is present only when `compare=true` (always on the profile). `filters` echoes every active filter with a Persian display label (used on screen and in print). With no successful sync every figure is `null` and `unavailable` lists `no-successful-sync`.

### Table envelope (reports, directory, document lists)

```json
{
  "...envelope": "",
  "page": 2, "pageSize": 25, "totalRows": 312,
  "sort": "net", "dir": "desc",
  "totals": { "net": "...", "gross": "...", "returns": "...", "vat": "...", "netQuantity": "...", "quantityMissingLines": 0 },
  "previousTotals": { "net": "..." },
  "rows": [ ]
}
```

`totals` cover the full filtered result, not the page. `page` is the effective page (clamped to the last page).

### Print twin

Every table endpoint `X` has `X/print` with the same parameters except `page`/`pageSize`. Response: the envelope plus `{ "totalRows": N, "truncated": N > 1000, "limit": 1000, "totals": {...}, "rows": [ first ≤ 1000 rows in the current sort ] }`.

---

## 1. Vendor directory — `GET /api/purchasing/vendors`

Filters: period, `compare` (supported: rows gain `previousNet`, `delta`, `percent` and the envelope `previousPeriod`/`previousTotals`, as §3 grouped rows), `q` (name or code; ی/ک, spaces, ZWNJ and digit forms ignored). Sort: `net` (default ↓), `name`, `code`, `lastPurchase`.

Row:

```json
{ "key": "V-102", "name": "شرکت دارویی نمونه", "code": "۱۰۲", "net": "125000000.0000", "lastPurchaseDate": "1405-08-21", "lastPurchaseDateJalali": "۱۴۰۵/۰۸/۲۱" }
```

`code` is `null` with `unavailable: vendorCode` when the code item is unusable. `net` is `"0.0000"` for a vendor without purchases in the period; `null` only when unavailable. `lastPurchaseDate` is `null` when the vendor has no invoice.

Print: `GET /api/purchasing/vendors/print`.

## 2. Vendor profile — `GET /api/purchasing/vendor-profile?vendor={vendorKey}`

`vendor`: source key, as a query parameter (source keys may hold characters such as `/`, and a path segment could collide with `print`/`compare`). 400 when missing or `~unknown`; 404 when not in the vendor master. Period parameters only; comparison always on.

```json
{
  "...envelope": "",
  "vendor": { "key": "V-102", "name": "شرکت دارویی نمونه", "code": "۱۰۲" },
  "totals": { "net": "...", "gross": "...", "returns": "...", "vat": "...", "vatInvoice": "...", "vatReturn": "...", "netQuantity": "...", "documentCount": 14 },
  "comparison": { "previousNet": "...", "delta": "...", "percent": "12.50" },
  "trend": [ { "month": "1404-09", "net": "...", "available": true, "partial": false } ],
  "topProducts": [ { "key": "P-9", "name": "...", "net": "...", "netQuantity": "...", "unknown": false } ],
  "returns": [ { "productKey": "P-9", "productName": "...", "returns": "...", "quantity": "...", "unknown": false } ],
  "documents": { "page": 1, "pageSize": 25, "totalRows": 14, "rows": [ "...as §5 rows" ] }
}
```

`comparison` is `null` with `no-previous-data` before the first synced month. `trend` = 12 months ending with the period's last month. `topProducts` ≤ 10. No balance/payment fields exist in this DTO.

## 3. Purchasing reports — `GET /api/purchasing/reports/{reportId}`

`reportId` ∈ `by-vendor`, `by-product`, `by-category`, `trend`, `top-products`, `returns`, `transactions`. Applicable filters and sort keys per data-model §4 catalog; any other filter → 400.

Grouped row (`by-vendor`, `by-product`, `by-category`, `top-products`):

```json
{
  "key": "P-9", "name": "...", "code": null, "unknown": false,
  "net": "...", "gross": "...", "returns": "...", "vat": "...",
  "netQuantity": "...", "documentCount": 7,
  "previousNet": "...", "delta": "...", "percent": "-3.10",
  "drill": { "product": "P-9" }
}
```

`previousNet`/`delta`/`percent` only with `compare=true`; on `returns` rows the comparison is on returns (`previousReturns`, `delta`, `percent`). `rank` (1-based in the current sort) is added for `top-products`. `unknown: true` rows have `key: null`, `name: "نامشخص"`, `drill` with `~unknown`.

Trend row: `{ "month": "1405-08", "label": "آبان ۱۴۰۵", "net": "...", "netQuantity": "...", "available": true, "partial": false, "drill": { "month": "1405-08" } }` (with `previousNet`/`delta`/`percent` under `compare=true`). Search: `q` matches vendor name/code or product name and keeps only matching lines in every month bucket and in `totals` (a month without matching lines shows `"0.0000"`); with an active search the row's `drill` is `{ "month": "1405-08", "match": "<q>" }`. Paged like every report (`page`, `pageSize` 25/50/100, clamping) over ≤ 37 month buckets; `totalRows` and `totals` cover every bucket. Sort `month` (default ↑) or `net`.

Returns row: `{ "vendorKey", "vendorName", "productKey", "productName", "unknownVendor", "unknownProduct", "returns": "...", "quantity": "...", "documentCount", "drill": { "vendor": "...", "product": "...", "docType": "return" } }`.

Transactions row = document row of §5.

Print: `GET /api/purchasing/reports/{reportId}/print`.

## 4. Vendor comparison — `GET /api/purchasing/vendors/compare?vendors=K1,K2[,…]`

Period parameters only (`compare` → 400). 400 when fewer than 2 or more than 5 distinct keys (messages: «برای مقایسه دست‌کم دو تأمین‌کننده لازم است» / «حداکثر پنج تأمین‌کننده را می‌توان مقایسه کرد»), a key is `~unknown`, or a key is not in the master.

```json
{
  "...envelope": "",
  "months": [ "1405-03", "1405-04", "1405-05", "1405-06", "1405-07", "1405-08" ],
  "vendors": [
    {
      "key": "V-102", "name": "...", "code": "...",
      "net": "...", "netQuantity": "...", "documentCount": 14, "returns": "...",
      "trend": [ { "month": "1405-03", "net": "...", "available": true, "partial": false } ]
    }
  ]
}
```

Vendors in request order after de-duplication. Each vendor's measures equal its profile totals for the same period (SC-005, invariant I2 generalized).

Print: `GET /api/purchasing/vendors/compare/print?vendors=K1,K2[,…]` — same parameters, validation (2..5 vendors), authorization and body as the screen response (at most 5 vendors × ≤ 37 months, so never truncated; `truncated: false`, `limit: 1000` for a uniform print envelope). Used by the comparison page's print button (FR-021).

## 5. Documents (drill-down, profile documents) — `GET /api/purchasing/documents`

Filters: period, `vendor`, `product`, `category`, `month`, `docType`, `q` (document number or vendor name), `match` (from a trend row's `drill`: keeps only lines whose document vendor name/code or line product name contains the term, so in-scope sums equal the trend row). `compare` → 400. Sort: `date` (default ↓), `number`, `vendor`, `net`.

Row (in-scope sums: only lines matching the product/category filters and `match`):

```json
{
  "documentId": 88123, "number": "۴۵۱۲", "docType": "invoice", "docTypeLabel": "فاکتور خرید",
  "date": "1405-08-12", "dateJalali": "۱۴۰۵/۰۸/۱۲",
  "vendorKey": "V-102", "vendorName": "...",
  "preDiscount": "...", "discount": "...", "vat": "...", "net": "...", "signedNet": "-...",
  "quantity": "...", "lineCount": 3
}
```

`net` is the positive line-amount sum; `signedNet` is negative for returns. Totals: `{ "net": "<Σ signedNet>", "invoiceAmount", "returnAmount", "netQuantity", "lineCount", "documentCount", "quantityMissingLines" }`.

Print: `GET /api/purchasing/documents/print` (used by the transaction-history print).

## 6. Document lines — `GET /api/purchasing/documents/{documentId}/lines`

Same `product`/`category`/`match` filters as the drill-down (to show only in-scope lines) plus `all=true` to show every line of the document. 404 for an unknown id, or a cancelled document.

```json
{
  "...envelope (no period)": "",
  "document": { "...row of §5 (unfiltered)": "" },
  "lines": [ { "lineId": 1, "productKey": "P-9", "productName": "...", "categoryName": "...", "unknownProduct": false, "quantity": "...", "preDiscount": "...", "discount": "...", "lineAmount": "...", "vat": "...", "signedAmount": "..." } ],
  "totals": { "lineAmount": "...", "signedAmount": "...", "quantity": "..." }
}
```

Not paged (a document's lines are few); capped at 1,000 lines.

## 7. Lookups for the filter bar — `GET /api/purchasing/lookup/{vendors|products|categories}?q=&limit=20`

Returns `{ "items": [ { "key", "name", "code" } ] }`, search via `fn_search_key`, ≤ 50 items. `categories` returns 200 with `unavailable: category` and no items when the category source is unusable.

## 8. `GET /api/system/status` (changed)

`modules` gains `"purchasingReports": true` for every signed-in user (navigation only; §1–§7 enforce roles). No other change.

---

## Authorization matrix

| Endpoint | anonymous | manager | admin |
|---|---|---|---|
| §1–§7 | 401 | 200 | 200 |
| §8 | 401 | 200 | 200 |
