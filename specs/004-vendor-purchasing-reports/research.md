# Research: Vendor and Purchasing Reports (Release 1, Phase 2)

**Feature**: `specs/004-vendor-purchasing-reports` | **Date**: 2026-11-17 | **Plan**: [plan.md](plan.md) | **Spec**: [spec.md](spec.md)

Skip mode: no questions were asked. Every open point is decided below and the owner-facing ones are listed in R18 so the owner can overturn them. Phase 1 decisions (`specs/003-real-purchase-data/research.md` R1–R16) stay in force; this document only adds to them.

---

## R1. One metric definition over any date range (FR-002)

**Problem.** Phase 1 defined every metric through month-scoped PostgreSQL functions (`mabna.fn_purchase_line_amounts(p_year, p_month, …)` and the functions built on it). Phase 2 needs quarter, year and custom day ranges, groupings by vendor/product/category/month, and drill-downs, all giving the same value as the Overview.

**Decision.** Add one range-based base function, `mabna.fn_purchase_lines(p_from_key int, p_to_key int, p_basis text)`, that returns one row per non-cancelled purchase line whose document date lies in the Jalali day range, with the line amount computed by exactly the Phase 1 rule (line basis, or line-and-document basis with the same integer pro-rata allocation and remainder rule), the signed amount, quantity, VAT and the joined vendor/product/category references. Then **redefine `mabna.fn_purchase_line_amounts` as a thin wrapper** over it (same signature, same output columns, month bounds as day keys, vendor filter and product filter applied as before). All Phase 1 functions (`fn_purchase_totals`, `fn_top_*`, `fn_purchase_trend`) keep their signatures and now read the same base, so there is still exactly one definition of a line amount.

**Why the results are identical.** The document discount is allocated per document over all lines of that document; the allocation does not depend on the period or on the vendor filter (a vendor filter keeps or drops whole documents), and the product filter is applied after allocation in both versions. A month is the day range `[yyyy mm 01, yyyy mm last]`, and the stored `jalali_*` columns are the same values the Phase 1 filter used.

**Proof in the run.** The Phase 1 reconciliation CLI (quickstart Flow 7 of spec 003) is rerun after migration `0007` and must still pass for every metric; the Phase 1 sync fingerprint is unaffected by the function change. Phase 2 quickstart Flow P0 compares the Overview before and after the migration on the same data.

**Alternatives considered.** (a) Write new range functions next to the month ones: two definitions of the same metric, rejected by FR-002 and `AGENTS.md` ("keep financial formulas separate and documented", one definition). (b) Compute ranges as unions of month calls: does not handle day ranges and multiplies scans. (c) Move the computation to C#: breaks "the Overview and the reconciliation use the same SQL functions" from Phase 1.

## R2. Filtering by Jalali day range

**Decision.** Add a stored generated column `mabna.purchase_document.jalali_key integer GENERATED ALWAYS AS (jalali_year * 10000 + jalali_month * 100 + jalali_day) STORED` with a partial index `(jalali_key) WHERE NOT cancelled`, and a composite partial index `(vendor_id, jalali_key) WHERE NOT cancelled`. Ranges are passed as two integer day keys (`14050801`..`14050830`). The server computes the bounds from Jalali periods with the existing `JalaliDate` (leap-year aware); SQL never converts calendars.

**Rationale.** The Jalali columns are what Phase 1 filters on, so a day key built from them gives byte-for-byte the same membership as the month filter. Integer comparison is index-friendly and calendar-free. `doc_date` (Gregorian) stays as is and is not used for filtering, avoiding a second source of truth for "which period a document belongs to".

**Alternatives.** Gregorian `doc_date` ranges (requires a second conversion path, risk of off-by-one at Esfand 30); expression index without a stored column (works, but the stored column keeps queries and plans simple and is cheap: one int per document).

## R3. Period model (FR-018, FR-020, edge cases)

**Decision.** A pure C# value type `JalaliPeriod` (new, `Purchasing/Reports/JalaliPeriod.cs`) with kinds `month`, `quarter`, `year`, `custom`, parsed from query parameters:

| Parameter | Meaning |
|---|---|
| `period=month&month=1405-08` | Jalali month (default: current month in the sync time zone, as Phase 1) |
| `period=quarter&quarter=1405-Q3` | Jalali quarter: Q1 = months 1–3 … Q4 = 10–12 |
| `period=year&year=1405` | Jalali year |
| `period=custom&from=1405-07-10&to=1405-08-24` | Inclusive Jalali day range |
| `compare=true` | Also compute the previous period |

Persian, Arabic-Indic or Latin digits are accepted (as Phase 1 month parsing). Rules:

- **Previous period**: month → previous Jalali month; quarter → previous quarter (Q1 → Q4 of the previous year); year → previous year; custom of N days → the N days ending the day before `from` (day arithmetic via Julian day numbers in `JalaliDate`, so Esfand 30 and year boundaries are exact).
- **Validation**: `to < from` → 400 with a Persian message. A range or preset reaching past "today" (sync time zone) is trimmed to today and the response states `trimmedTo`; a range entirely in the future → 400. Custom ranges longer than 1,098 days (three Jalali years, the performance envelope of FR-022) → 400 with the limit stated.
- **Comparison availability**: the previous period is "data unavailable" (`no-previous-data`) when it starts before the first day of the first synced month (`mabna.fn_first_data_month()`), which is the Phase 1 rule at month granularity extended to ranges. A zero base gives a difference but no percentage (Phase 1 `ComparisonMath`).

**Rationale.** Matches the spec clarification exactly; pure, so it is unit-tested without a database (validation rule: no DB or network in tests), including leap years and quarter/year wraps.

## R4. Trend windows (FR-011, FR-014, FR-016)

**Decision.** One window rule implemented once (`JalaliPeriod.TrendMonths()`):

- **Preset (month, quarter, year)**: the 12 Jalali months ending with the preset's last month (a year preset therefore shows exactly its own 12 months).
- **Custom range**: the Jalali months intersecting the range, each bucket summed over the days of that month **inside the range**; the first and last bucket carry `partial: true` when the range does not cover the whole month. At most 37 buckets (follows from the 1,098-day limit).
- **Vendor profile** (FR-011): always the 12 months ending with the period's last month, even for a custom range (the spec states this explicitly for the profile).
- Months before the first synced month: `available: false`, net `null` (Phase 1 trend rule), never zero.

Buckets are computed in one grouped query over `fn_purchase_lines` (group by `jalali_year, jalali_month`), so each bucket equals the drill-down of that month and the Overview's month figure.

## R5. Report engine: one service, allowlisted SQL fragments (FR-013..FR-015, FR-019)

**Decision.** A single `PurchasingReportService` builds each report from a small, fixed set of SQL templates over `mabna.fn_purchase_lines(...)`:

- **Grouped reports** (`by-vendor`, `by-product`, `by-category`, `top-products`, `returns`, `trend`): group by an enum dimension (`vendor`, `product`, `category`, `vendorProduct`, `month`). Measures per group: `net`, `gross` (metric: invoice line amounts), `returns`, `vat`, `netQuantity`, `invoiceQuantity`, `returnQuantity`, `documentCount`, `lineCount`, plus, with `compare=true`, `previousNet`, `delta`, `percent`.
- **Document list** (`transactions`, vendor profile documents, drill-down): one row per document with its in-scope line sums.
- **Lines** of one document under the row's filters.

Paging: `count(*) OVER ()` and `sum(...) OVER ()` give the total row count and report totals over the full filtered result in the same statement; `LIMIT/OFFSET` pages. Page size 25 (default), 50 or 100; a page beyond the last is clamped to the last page and the response returns the effective `page`. Sorting: each report declares an allowlist of sort keys mapped to fixed SQL expressions (no user text reaches SQL), with a deterministic tiebreak (`key COLLATE "C"`). Search applies to the report's name dimension (R6). All values are parameters (`NpgsqlParameter`).

Offset paging is enough: grouped results have at most a few thousand rows (vendors/products), and document lists are filtered by period (three years of documents is far below where offset becomes slow at page sizes ≤ 100).

**Alternatives.** Keyset paging (more complex, unnecessary at this scale, and awkward with arbitrary sort keys); EF Core LINQ over the `mabna` tables (would re-express the metric in C# and break R1); a generic query builder library (new dependency, not justified).

## R6. Persian search (FR-010, SC-001)

**Decision.** One SQL function `mabna.fn_search_key(text) RETURNS text IMMUTABLE` that: maps ي→ی, ى→ی and ك→ک (only the yeh/kaf letters are in scope; other Arabic letter variants are left as recorded), maps Persian (۰–۹) and Arabic-Indic (٠–٩) digits to Latin, removes all whitespace and ZWNJ (U+200C) and the tatweel (U+0640), and lower-cases Latin letters. Stored generated columns `mabna.vendor.search_key` (name and code, separated by `|`, which the query normalization strips) and `mabna.product.search_key`, `mabna.product_category.search_key`. The query term goes through the **same SQL function** and matches with `search_key LIKE '%' || fn_search_key(@q) || '%'` (with `%`, `_`, `\` escaped). A term that normalizes to empty means "no search".

**Rationale.** One implementation for both stored text and query text, so they cannot drift; generated columns fill on migration with no resync; names stay displayed exactly as synced (Phase 1 normalization, ZWNJ kept). Vendor and product masters are thousands of rows, so a sequential `LIKE` is well within the 2 s target.

**Alternatives.** `pg_trgm` GIN index (extension; not needed at this scale, kept as a fallback if the perf flow fails — it ships with PostgreSQL contrib, so no new package); C# normalization of the query plus SQL normalization of the column (two implementations, rejected); `unaccent` (does not cover ی/ک).

## R7. New source items: vendor code and product category (FR-008)

**Decision.** Extend the Phase 1 source map (contract delta in [contracts/source-map-delta.md](contracts/source-map-delta.md)):

- `entities.vendor.code` — optional field (column + status); synced to `mabna.vendor.code`.
- `entities.product.categoryKey` — optional field; synced to the existing reserved column `mabna.product.drug_group_key` (kept under its Phase 1 name; no rename migration).
- `entities.productCategory` — new optional master entity (`object`, `kind`, `hostFiltered`, `status`, `key`, `name`, `change`), read in full with a key diff like the other masters; synced to new table `mabna.product_category`.

The validator applies all Phase 1 rules to the new entity (allowlist, identifier policy, hard exclusions, `hostFiltered`), and a missing entity or field is not an error: it makes dependent figures unavailable. Sync ingestion normalizes code and category names with `PersianText`. The fingerprint (`fn_sync_fingerprint`) adds `vendor.code`, `product.drug_group_key` and a `product_category` row so the incremental-equals-full check covers the new columns.

**Drug class.** The spec says "drug group (and class, if verified)". Only **one category level** (drug group) is built in this phase; the class is recorded as an `unknown` verification item. If on-site verification finds the class is what users mean by category, the map points `categoryKey`/`productCategory` at it instead (configuration, no code change). A two-level hierarchy is deferred (R18).

**Simulator.** The simulator scenario (`deploy/mabna-sim/generated/scenario.py`, gitignored) gains a `SimDrugGroup` master and a `SimDrugGroupID` column on the simulator product table, plus a `SimVendorCode` column on the vendor table, with candidate entries in the example map (`deploy/mabna-sim/example/purchase-source-map.example.json`, made-up `Sim*` names). The scenario includes: a vendor name with Arabic ي/ك and one with a ZWNJ, two vendors with the same normalized name (different codes), a product without a category, and a product whose category key is missing from the category master.

## R8. Availability rules for Phase 2 figures (FR-004, FR-008)

**Decision.** Extend `MetricAvailability.Evaluate` with new figures; same reason codes as Phase 1 (`no-successful-sync`, `source-item-unverified`, `source-item-unmapped`, `no-previous-data`) and the same usability rule (`confirmed`/`corrected`, plus `candidate` in demo mode):

| Figure | Unavailable when |
|---|---|
| `vendorCode` | `vendor.code` unmapped or unusable → code column shows «داده در دسترس نیست», search matches name only |
| `category` | `product.categoryKey` or the `productCategory` entity/key/name unmapped or unusable, or `topProducts` (product key) unavailable → by-category report, category filter and category columns unavailable |
| `quantity` | `purchaseLine.quantity` unmapped or unusable → quantity columns `null` |
| `vendorReport` (by-vendor, vendor directory amounts, profile, comparison) | Phase 1 `topVendors` reason |
| `productReport` (by-product, top-products) | Phase 1 `topProducts` reason |
| `returnsReport` | Phase 1 `returns` reason, or vendor key, or product key |
| `transactions` | Phase 1 `net` reason; VAT column follows `vat` |

A request that filters on an unavailable dimension (e.g. `category=` while `category` is unavailable) returns 200 with the report marked unavailable (`unavailable: [{figure:"category", reason}]`) rather than silently ignoring the filter. The vendor directory still lists vendors (names come from the master) when amounts are unavailable; the amount column then shows «داده در دسترس نیست».

## R9. Drill-down and sign conventions (FR-015, US3)

**Decision.** Every report row carries a `drill` object: the filter that reproduces it (`vendor`, `product`, `category`, `month`, `docType`, and `match` for a trend row under an active search — the same line-level vendor-or-product predicate as the trend `q`). The client calls `GET /api/purchasing/documents` with the report's period and filters plus the row's `drill` filter. The response lists documents (paged) with the **in-scope** line sums of each document, and totals over all pages: `net` (invoice lines positive, return lines negative), `invoiceAmount`, `returnAmount`, `netQuantity`, `lineCount`, `documentCount`. Expanding a document calls `GET /api/purchasing/documents/{documentId}/lines` with the same filters and returns only the in-scope lines (e.g. one product's lines on a multi-product invoice); their sum equals the document's in-scope subtotal.

Equality checks (SC-004): a by-vendor/by-product/by-category/trend row's `net` equals the drill-down `totals.net`; a returns row's `returns` equals the drill-down `totals.returnAmount` (the returns report shows returns as positive amounts, consistent with the Phase 1 "Returns" metric; the drill-down shows the same lines with a negative sign in the signed column). Quantities likewise.

The unknown bucket uses the reserved filter value `~unknown` (not a valid source key: `~` is never produced by key normalization; the filter parser checks this) for vendor, product and category. Unknown vendor = document with no vendor key or a key missing from the master; unknown product = line with no product key or missing from the master; unknown category = unknown product, product without category key, or a key missing from the category master.

## R10. Printing (FR-021, SC-007)

**Decision.** Each report endpoint has a print twin under the same path with `/print` (e.g. `GET /api/purchasing/reports/by-vendor/print`), sharing the handler: same filters, sort and authorization, no paging, `LIMIT 1001` to detect truncation, returning at most 1,000 rows, `totalRows` (from `count(*) OVER ()`), report totals over the full filtered result and `truncated: true|false`. The client renders a dedicated print view (`PrintReport` component) into a hidden container, then calls `window.print()`; `@media print` (existing `frontend/src/index.css` print block, extended) hides navigation and the screen table, shows the print container, sets A4 portrait with `table-layout: fixed`, wraps long names, repeats the table header on every page (`thead { display: table-header-group }`), and switches to landscape via a named `@page` only for the transaction history (eight columns). The header states report name, period (and comparison period), every active filter, last-sync time and data label; when truncated, a notice states "۱٬۰۰۰ از N ردیف" and asks the user to narrow the filters.

The vendor comparison has a print twin too, `GET /api/purchasing/vendors/compare/print`: same validation and body as the screen response (≤ 5 vendors × ≤ 37 months, never truncated), rendered by `PrintReport` as the five measures per vendor plus a month × vendor trend table, so every Phase 2 report is printable (FR-021).

The WebView2 desktop shell supports `window.print()` natively (print preview dialog), so no shell change is needed; this is verified in quickstart Flow P8 on Windows.

**Alternatives.** Printing only the visible page (rejected by the clarification); server-side PDF (out of scope for Release 1); a print library (not justified).

## R11. Vendor comparison (FR-016, FR-017)

**Decision.** `GET /api/purchasing/vendors/compare?vendors=k1,k2,k3&<period>`. The server de-duplicates keys, then refuses fewer than 2 (`400`, «برای مقایسه دست‌کم دو تأمین‌کننده لازم است») or more than 5 distinct vendors (`400`, «حداکثر پنج تأمین‌کننده را می‌توان مقایسه کرد») and unknown keys (`400`, naming the key count not found). One grouped query over `fn_purchase_lines` restricted to the selected vendor IDs computes per vendor: net, net quantity, document count (non-cancelled invoices and returns, the Phase 1 `document_count` definition), returns, and the monthly trend per R4. The `~unknown` bucket cannot be compared. The UI disables "add" at five and states the limit, but the server check is the enforcement.

## R12. Vendor directory and profile (FR-009..FR-012)

**Decision.** `GET /api/purchasing/vendors` lists every synced vendor (master rows, not only vendors with purchases) with `name`, `code`, period `net` and `lastPurchaseDate` (latest non-cancelled **invoice** date, all time, Jalali text) — a LEFT JOIN of the master with the grouped period lines. Default sort: net descending, then name. `GET /api/purchasing/vendor-profile?vendor=<key>` (key as a query parameter: source keys may contain `/` and a path segment could collide with `compare`/`print`) returns the profile: header (name, code), totals (net, gross, returns, VAT, invoice VAT, return VAT), comparison (always computed for the profile: FR-011 requires it), the 12-month trend, top 10 products by net, returns grouped by product, and the first page of documents (further pages and lines via the document endpoints with `vendor=<key>`). DTOs have **no** balance, payment, credit or debt fields (FR-012); a unit test asserts the profile and directory DTO property names contain none of `balance`, `payment`, `debt`, `credit`, `paid`.

Vendor equality with the Overview (SC-002): the profile's month net and the Overview's top-5 entry both come from `fn_purchase_lines` grouped by `vendor_id` for the month; quickstart Flow P2 checks all five.

## R13. Authorization, modules and data labels (FR-005, FR-007)

**Decision.** New controller `PurchasingReportsController` at `api/purchasing` with `[Authorize(Roles = "manager,admin")]` (Demo 1 roles; Phase 3 replaces this with permission sets). Not `[DemoOnly]`: the pages work on real data in production. `SystemModules` gets `PurchasingReports` (true for every signed-in user, since only `manager`/`admin` exist; the endpoint attribute is the enforcement). Every response carries `dataLabel`, `lastSuccessfulSync`, `currency`, `currencyLabel`, `discountBasis`, the resolved `period` (with labels) and `filters` echo, so the UI and printout state them from server data.

## R14. Performance (FR-022, SC-008)

**Decision.** Indexes from R2, plus `idx_product_drug_group_key (drug_group_key)`. Lines are reached by document through the existing unique index `(document_id, source_key)` (leading column), so no new line index is added. Each report query has a 15 s command timeout (fail closed with a Persian error rather than hanging). Report totals and rows come from one statement. The vendor profile issues its sub-queries sequentially on one connection (simple, predictable; total under 2 s at scale per the perf flow). `db/perf/purchasing-3y.sql` is extended to fill categories and vendor codes; quickstart Flow P9 measures p95 of each endpoint over 20 requests with a year preset and a 1,098-day custom range.

**Fallback if p95 fails.** A materialized per-document-line amount table refreshed inside the sync transaction (the line amount does not depend on the period). Not built up front: it would duplicate the line amount in storage and needs care to stay the single definition.

## R15. Frontend structure (no new dependencies)

**Decision.** Extend the Phase 1 view switcher (no router) with three tabs: «تأمین‌کنندگان» (directory → profile as an in-view sub-state), «گزارش‌های خرید» (report picker with the seven reports) and «مقایسه تأمین‌کنندگان». Shared components: `FilterBar` (period presets with a Jalali month/quarter/year picker and a custom day-range input reusing `utils/jalali.ts`, vendor/product/category pickers backed by `GET /api/purchasing/lookup/*`, comparison toggle; offers only the filters the report declares), `ReportTable` (sortable header buttons with `aria-sort`, server paging, debounced search, totals footer, «نامشخص» rows, drill-down button per row), `DrillDownDialog` (documents with expandable lines), `ComparisonCells`, `PrintReport`, and a `MultiTrendChart` (inline SVG, extends the Phase 1 `TrendChart` approach). Keyboard: every control is a native button/input/select; focus is visible; `DrillDownDialog` is a modal dialog with focus trap and Escape. Colors use the existing Tailwind palette at AA contrast. Filter state lives in one `useReportQuery` hook that builds the query string; the "open vendor" links from the Overview's top-5 navigate to the profile with the same month.

## R16. Quantities and amount wording (FR-003, edge cases)

**Decision.**

- **Net quantity** = Σ invoice line quantity − Σ return line quantity, as recorded, no unit conversion; every quantity column header says «تعداد (واحد ثبت‌شده)». A line with `NULL` quantity counts as 0 and the response returns `quantityMissingLines` (count) so the UI can footnote it.
- **Wording.** The Phase 1 metric "Gross purchases" (invoice line amounts after line discount, excl. VAT) is labeled «خرید ناخالص». The transaction history's pre-discount amount (Σ `gross_amount`) is a different figure and is labeled «مبلغ پیش از تخفیف», with «تخفیف» (line discount + allocated document discount under the basis) and «ارزش افزوده», and «خالص» = pre-discount − discount for invoices (the line amount) — so a document's «خالص» equals its Phase 1 line-amount sum. These definitions are added to `docs/metrics/purchasing.md` (FR-003; never shown as payments, balance or profit).

## R17. Testing under the "no DB or network" rule

**Decision.** Unit tests (xunit, `backend/Zharfa.Api.Tests/Purchasing/Reports/`): `JalaliPeriodTests` (presets, previous period incl. Q1→Q4 and year after a leap year, 45-day custom, Esfand 30, trimming, limits, digit parsing), `TrendWindowTests`, `ReportCatalogTests` (report ids, applicable filters, sort allowlists, search targets, comparison applicability), `ReportFilterTests` (rejected inapplicable filter and unknown sort, page clamping, `~unknown` parsing, search-term escaping, SQL text contains only template fragments), `VendorComparisonRequestTests` (dedupe, 2..5), `MetricAvailabilityTests` (extended: new figures × production/demo × statuses), `SourceMapValidationTests` additions for `productCategory`, `NoVendorFinancialsTests` (DTO property scan), `PrintLimitTests` (1,000 cap, truncation flag). SQL correctness (R1 equivalence, search, sums) is proven by the quickstart flows on Docker Compose and the simulator, exactly as Phase 1.

## R18. Assumptions taken in place of questions (skip mode; owner may overturn)

1. Product category = one level, Mabna's drug group; the class is a separate `unknown` item and is not built (R7).
2. Vendor code = the code users see in Mabna; when unusable, search uses names only (spec assumption).
3. Previous period availability is decided at month granularity against the first synced month (R3).
4. Custom ranges are limited to 1,098 days; a range into the future is trimmed to today (R3).
5. Custom-range trend buckets are partial months clipped to the range; the profile trend is always 12 months (R4).
6. "Last purchase date" = latest non-cancelled invoice date, all time (R12).
7. Vendor directory lists all synced vendors, including those with no purchases in the period (zero net).
8. "Number of documents" in the comparison = non-cancelled invoices + returns (Phase 1 definition).
9. Top products on the vendor profile = top 10 by net; the most-purchased-products report shows all products ranked (paged).
10. Returns report groups by vendor × product (AC4 says "by vendor and product").
11. The transaction-history printout uses A4 landscape; all other reports A4 portrait.
12. Search applies to the name dimension of a report (vendor name/code, product name, category name); the transaction history searches document number and vendor name; the trend report searches vendor name/code or product name and restricts the lines summed into each month (FR-019, no exemption).
13. Period comparison basis (spec Clarifications): a preset compares with the previous calendar unit of the same kind, only a custom range uses the equal-day rule. Taken in skip mode; **owner sign-off pending**.
