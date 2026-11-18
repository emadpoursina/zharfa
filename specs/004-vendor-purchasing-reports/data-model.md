# Data Model: Vendor and Purchasing Reports (Release 1, Phase 2)

**Feature**: `specs/004-vendor-purchasing-reports` | **Date**: 2026-11-17 | **Plan**: [plan.md](plan.md) | **Research**: [research.md](research.md)

Builds on `specs/003-real-purchase-data/data-model.md` (the `mabna` schema, sync bookkeeping, metric definitions §4 and availability rules). Real Mabna names never appear here; they stay in the gitignored source map. One new forward-only, idempotent migration:

- `db/migrations/0007_purchasing_reports.sql` — new columns, `mabna.product_category`, search and day-key columns, indexes, `mabna.fn_search_key`, the range base function `mabna.fn_purchase_lines`, the wrapper redefinition of `mabna.fn_purchase_line_amounts`, and the extended `mabna.fn_sync_fingerprint`.

No table in `public` changes. No Demo 1 table is read by Phase 2 code.

---

## 1. Synced copy: changes and additions (schema `mabna`)

### `mabna.vendor` (changed)

| Column | Type | Rules |
|---|---|---|
| `code` | `text` NULL | **New.** Vendor code as shown to users in Mabna, from map field `vendor.code`; normalized with `PersianText.Normalize`; NULL when unmapped/empty |
| `search_key` | `text` GENERATED ALWAYS AS (`mabna.fn_search_key(name_fa || '|' || coalesce(code, ''))`) STORED | **New.** Search only, never displayed |

Existing columns unchanged (`vendor_id`, `source_key`, `name_fa`, `is_active_source`, `synced_at`).

### `mabna.product` (changed)

| Column | Type | Rules |
|---|---|---|
| `drug_group_key` | `text` NULL | **Now filled** from map field `product.categoryKey` (Phase 1 reserved it). NULL → unknown category |
| `search_key` | `text` GENERATED ALWAYS AS (`mabna.fn_search_key(name_fa)`) STORED | **New** |

Index: `idx_product_drug_group_key (drug_group_key)`.

### `mabna.product_category` (new)

Mabna's drug group master (one level; research R7).

| Column | Type | Rules |
|---|---|---|
| `category_id` | `bigint` identity PK | |
| `source_key` | `text` NOT NULL UNIQUE | Source key, as text |
| `name_fa` | `text` NOT NULL | `PersianText.NormalizeName` (empty → `(بدون نام)`) |
| `search_key` | `text` GENERATED ALWAYS AS (`mabna.fn_search_key(name_fa)`) STORED | |
| `synced_at` | `timestamptz` NOT NULL | |

Synced like the other masters: read in full with a key diff on every run, in the same data transaction (Phase 1 R5/R6). A product's category is resolved by `product.drug_group_key = product_category.source_key` (text join, like `vendor_source_key`), so a key missing from the master falls into the unknown bucket instead of breaking the sync.

### `mabna.purchase_document` (changed)

| Column | Type | Rules |
|---|---|---|
| `jalali_key` | `integer` GENERATED ALWAYS AS (`jalali_year * 10000 + jalali_month * 100 + jalali_day`) STORED | **New.** Day key for range filters (research R2) |

Indexes: `idx_purchase_document_jalali_key (jalali_key) WHERE NOT cancelled`; `idx_purchase_document_vendor_day (vendor_id, jalali_key) WHERE NOT cancelled`; `idx_purchase_document_vendor_source_key (vendor_source_key)` (last purchase date per vendor).

### `mabna.purchase_line`

Unchanged.

### Sync bookkeeping

`source_record.table_logical` gains the value `product_category`; `sync_log.table_logical` likewise. No schema change (the columns are free text in Phase 1); the Data sync page lists the new table's counts automatically if it iterates logs (verify in implementation).

---

## 2. Functions (all `STABLE` or `IMMUTABLE`, `SECURITY INVOKER`, read-only)

### `mabna.fn_search_key(p_text text) RETURNS text` — IMMUTABLE

`lower()` of the input after: ي, ى → ی; ك → ک; Persian ۰–۹ and Arabic-Indic ٠–٩ → 0–9; removing whitespace, U+200C (ZWNJ), U+0640 (tatweel) and `|`. NULL → `''`. Used for stored `search_key` columns **and** for the query term (research R6).

### `mabna.fn_purchase_lines(p_from_key int, p_to_key int, p_basis text DEFAULT 'line')` — the range base (research R1)

Rows: every line of every non-cancelled purchase document with `jalali_key BETWEEN p_from_key AND p_to_key`. Columns:

| Column | Meaning |
|---|---|
| `doc_type` | `invoice` / `return` |
| `document_id`, `line_id` | Copy keys |
| `jalali_year`, `jalali_month`, `jalali_key` | Of the document |
| `vendor_id`, `vendor_source_key` | Of the document (NULL possible) |
| `product_id`, `product_source_key` | Of the line (NULL possible) |
| `category_key` | `product.drug_group_key` (NULL when product unknown or uncategorized) |
| `category_id` | `product_category.category_id` (NULL → unknown category) |
| `quantity` | As recorded (NULL possible) |
| `gross_amount` | Pre-discount line amount (positive) |
| `discount_amount` | `line_discount` + allocated document discount share (basis `lineAndDocument`), else `line_discount` |
| `line_amount` | `gross_amount − discount_amount` = **the Phase 1 line amount** (data-model 003 §4) |
| `vat_amount` | As recorded (NULL possible) |
| `sign` | `+1` invoice, `−1` return |

The allocation CTEs are moved verbatim from Phase 1's `fn_purchase_line_amounts` (integer units of 0.0001, round half away from zero, remainder to the largest line, ties by lowest line key `COLLATE "C"`). The allocation partitions by `document_id` over all lines of the document, so it does not depend on the range.

### `mabna.fn_purchase_line_amounts(p_year, p_month, p_vendor_key, p_product_key, p_basis)` — redefined as a wrapper

Same signature and output columns as Phase 1. Body: `SELECT doc_type, document_id, line_id, product_source_key, line_amount, vat_amount FROM mabna.fn_purchase_lines(p_year*10000+p_month*100+1, p_year*10000+p_month*100+31, p_basis) WHERE (p_vendor_key IS NULL OR vendor_source_key = p_vendor_key) AND (p_product_key IS NULL OR product_source_key = p_product_key)`. Day 31 as the upper bound is safe: `jalali_day` ≤ 31 by CHECK and the month is fixed. All Phase 1 functions built on it are unchanged.

### `mabna.fn_sync_fingerprint()` — extended

Adds `vendor.code` to the vendor hash, `product.drug_group_key` to the product hash, and a `product_category` row (`source_key`, `name_fa`). Generated columns are excluded.

### `mabna.fn_first_data_month()`

Unchanged; used for comparison and trend availability.

---

## 3. Phase 2 measures (documented in `docs/metrics/purchasing.md`, new section "Reports")

Let *L(P)* = rows of `fn_purchase_lines(from(P), to(P), basis)` for period *P*, further filtered by the report filters (vendor at document level; product and category at line level).

| Measure | Definition | Same as Phase 1 |
|---|---|---|
| Net purchases | Σ `sign × line_amount` | `net` |
| Gross purchases («خرید ناخالص») | Σ `line_amount` where invoice | `gross` |
| Returns («برگشت از خرید») | Σ `line_amount` where return (shown positive) | `returns` |
| VAT | Σ `sign × vat_amount`; invoice and return VAT also separately | `vat`, `vat_invoice`, `vat_return` |
| Net quantity | Σ `sign × coalesce(quantity, 0)`; `quantityMissingLines` = count of lines with NULL quantity | new |
| Invoice / return quantity | Σ quantity by doc type | new |
| Document count | Distinct `document_id` in *L(P)* (non-cancelled invoices + returns) | `document_count` when only a vendor filter applies |
| Pre-discount amount («مبلغ پیش از تخفیف») | Σ `gross_amount` (transaction history only) | new; **not** "gross purchases" |
| Discount («تخفیف») | Σ `discount_amount` (transaction history only) | new |
| Last purchase date | max `doc_date` of non-cancelled **invoices** of the vendor, all time | new |
| Comparison | `delta = net(P) − net(prev(P))`; `percent = delta / net(prev(P))`, `null` when base = 0; unavailable when `prev(P)` starts before the first synced month | Phase 1 rule, generalized to periods |
| Trend bucket | Net purchases of *L(P)* grouped by (`jalali_year`, `jalali_month`); months before the first synced month → unavailable | `fn_purchase_trend` for a preset month |

Invariants (checked in quickstart):

- I1. For a month *m*, Σ over vendor groups of net = Σ over product groups = Σ over category groups = `fn_purchase_totals(m).net`.
- I2. A vendor profile's month net = the Overview top-5 net of that vendor.
- I3. Every grouped row's net/quantity = its drill-down totals; a returns row's returns = drill-down `returnAmount`.
- I4. For each document in a drill-down, Σ in-scope line amounts × sign = the document's in-scope `net`.
- I5. For a document, pre-discount − discount = Σ line amounts (transaction history «خالص»).

Not presented as purchases or anywhere in Phase 2: payments, vendor balance, credit, price differences or profit (FR-003, FR-012).

---

## 4. Request-side entities (C#, not stored)

### `JalaliPeriod` (`Purchasing/Reports/JalaliPeriod.cs`)

| Field | Rules |
|---|---|
| `Kind` | `month` \| `quarter` \| `year` \| `custom` |
| `From`, `To` | `JalaliDate`, inclusive; `To` trimmed to today (sync time zone) if later; `TrimmedTo` set when trimmed |
| `FromKey`, `ToKey` | `yyyymmdd` integers for SQL |
| `Label` | Persian label: «آبان ۱۴۰۵», «فصل سوم ۱۴۰۵», «سال ۱۴۰۵», «۱۴۰۵/۰۷/۱۰ تا ۱۴۰۵/۰۸/۲۴» |
| `Previous()` | Month → previous month; quarter → previous quarter; year → previous year; custom of N days → N days ending `From − 1` |
| `TrendMonths(profile: bool)` | Research R4 |

Validation: month 1..12, quarter 1..4, year in `JalaliMonth.MinYear..MaxYear`, valid day for the month (Esfand 30 only in leap years), `To ≥ From`, not wholly in the future, custom length ≤ 1,098 days.

### `ReportFilter` (`Purchasing/Reports/ReportFilter.cs`)

| Field | Rules |
|---|---|
| `Period` | `JalaliPeriod` |
| `Compare` | bool |
| `VendorKey`, `ProductKey`, `CategoryKey` | Source key or `~unknown`; only those the report declares as applicable are accepted (others → 400 «این فیلتر برای این گزارش معتبر نیست») |
| `Month` | Optional `YYYY-MM` drill filter (trend rows) inside the period |
| `DocType` | Optional `invoice` \| `return` (returns rows) |
| `Search` | ≤ 100 characters; normalized in SQL; target per catalog "Search on" |
| `Match` | Optional, documents list only: the trend search carried by a trend row's `drill` (line-level vendor-or-product match) |
| `Sort`, `Direction` | Sort key from the report's allowlist; `asc` \| `desc` |
| `Page`, `PageSize` | Page ≥ 1 (clamped to last page); size ∈ {25, 50, 100} |

A print request uses the same `ReportFilter` without `Page`/`PageSize` and a fixed limit of 1,000 rows.

### `VendorComparisonRequest`

`VendorKeys`: 2..5 distinct source keys after de-duplication (no `~unknown`), plus `Period`. Violations → 400 with the Persian limit message (research R11).

### Report catalog (`Purchasing/Reports/ReportCatalog.cs`)

| Report id | Grouping | Filters | Sort keys (default first) | Search on | Paged | Compare |
|---|---|---|---|---|---|---|
| `by-vendor` | vendor | period, product, category | `net`↓, `gross`, `returns`, `vat`, `quantity`, `documents`, `name` | vendor name/code | yes | yes |
| `by-product` | product | period, vendor, category | `net`↓, `gross`, `returns`, `vat`, `quantity`, `name` | product name | yes | yes |
| `by-category` | category | period, vendor | `net`↓, `gross`, `returns`, `vat`, `quantity`, `name` | category name | yes | yes |
| `trend` | month | period, vendor, product, category | `month`↑, `net` | vendor name/code **or** product name (restricts the lines summed into each month) | yes | yes |
| `top-products` | product | period, vendor, category | `net`↓, `quantity`↓ | product name | yes | yes |
| `returns` | vendor × product, return docs only | period, vendor, product, category | `returns`↓, `quantity`, `vendor`, `product` | vendor or product name | yes | yes (on `returns`) |
| `transactions` | document | period, vendor, product, category, docType | `date`↓, `number`, `vendor`, `net` | document number, vendor name | yes | no (400) |

Non-report lists in the same catalog: `vendors` directory (search vendor name/code, paged, compare yes), `documents` (search document number or vendor name, plus the drill-only `match` term below, paged, compare no).

**Trend search and paging (FR-019, no exemption).** `q` on `trend` keeps only lines whose document vendor `search_key` or line product `search_key` contains `fn_search_key(q)`; each month bucket and the totals sum those lines only, months with no matching line show `net` `0` (or unavailable before the first synced month). Month rows (≤ 37) are paged with the common page sizes 25/50/100 and clamping; `totals` and `totalRows` cover every bucket. A trend row's `drill` carries `{ month, match: q }` when a search is active, and the documents list applies `match` with the same line-level rule, so drill-down totals still equal the row (I3).

**Comparison applicability.** `compare=true` is accepted only where the Compare column says yes (and on the directory); on `transactions`, `documents` and `vendors/compare` it is refused with 400 «این فیلتر برای این گزارش معتبر نیست». The vendor profile always compares.

---

## 5. State and lifecycle

No new persistent state machines. Reports are read-only views of the last committed sync. A sync committing while a report is open does not change the open report; the next request reads the new data (single PostgreSQL transaction per sync, Phase 1 R6), and every response carries the `lastSuccessfulSync` it was computed against.

## 6. Source verification record (FR-008)

New logical items appended to `specs/003-real-purchase-data/source-verification.md` (one record for Release 1; logical names only) and to `specs/002-mabna-accounting-connector/onsite-checklist.md` as section 7.6 "Phase 2 items":

| # | Logical item | Initial status |
|---|---|---|
| 40 | Vendor master: code shown to users | candidate |
| 41 | Product master: drug group key | candidate |
| 42 | Drug group master: table (object and entity status) | candidate |
| 43 | Drug group master: key | candidate |
| 44 | Drug group master: name | candidate |
| 45 | Drug group master: change marker | candidate (strategy `full`) |
| 46 | Product master: drug class (second category level) | unknown (not built in Phase 2) |
