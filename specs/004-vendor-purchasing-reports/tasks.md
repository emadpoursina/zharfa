---
description: "Task list for Vendor and Purchasing Reports (Release 1, Phase 2)"
---

# Tasks: Vendor and Purchasing Reports (Release 1, Phase 2)

**Input**: Design documents from `specs/004-vendor-purchasing-reports/` — [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/api.md](contracts/api.md), [contracts/source-map-delta.md](contracts/source-map-delta.md), [quickstart.md](quickstart.md)

**Phase ref**: `zharfa-prd.phases.md#2` · Governing PRD `zharfa-prd.md` v3.0 (F5 AC1–AC2, F6, F7, F8) · Rules `AGENTS.md` · Foundation `specs/003-real-purchase-data` (merged)

**Tests**: Included. Plan (Technical Context) and research R17 require pure xunit tests in `backend/Zharfa.Api.Tests` (no database, no network, no new packages). SQL behavior, invariants I1–I5 and UI flows are proven by quickstart Flows P0–P10, run by the tester, not by workers. Workers do not run installs or validation commands (AGENTS.md).

**Organization**: The Phase 1 metric-wrapper change and its P0 regression gate come first (Phase 1 below). Shared engine and source items follow (Phase 2). Then one phase per user story (US1–US5 from spec.md), then polish.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: User story from spec.md (US1–US5)
- Every task names its exact file path(s)

## Path Conventions

Web application (existing layout): `backend/Zharfa.Api/`, `backend/Zharfa.Api.Tests/`, `frontend/src/`, `db/migrations/`, `db/perf/`, `deploy/`, `docs/`. No new projects and **no new packages** (backend, tests or frontend; `pg_trgm` not used).

## Hard rules for every task

- Never write to Mabna and never query it per request: every Phase 2 endpoint reads `mabna.*` in PostgreSQL through the existing `NpgsqlDataSource`; only the sync reads Mabna, through the unchanged `IMabnaReader`.
- One metric definition: every figure reads `mabna.fn_purchase_lines` (or a Phase 1 function that wraps it). Never re-express a line amount in C# or in a second SQL function.
- No vendor balance, payment, credit, debt or "paid" field anywhere in Phase 2 DTOs, SQL or UI (FR-012); purchases are never labeled as payments, balance or profit (FR-003).
- No user text reaches SQL: sort keys map to fixed SQL expressions from an allowlist; every value is an `NpgsqlParameter`.
- Never commit real Mabna table, column or code names, rows or totals; committed examples use made-up `Sim*` names. Never read `HOST_ID()`-filtered views on live Mabna.
- Amounts are `decimal` / `numeric(24,4)` end to end, serialized as exact decimal strings (4 decimals); no floating point.
- Persian-first: RTL, Jalali dates, Persian digits, amounts labeled «ریال», Persian error messages exactly as in contracts/api.md.

---

## Phase 1: Setup — Range base and P0 regression gate (blocks everything)

**Purpose**: Introduce the range base `mabna.fn_purchase_lines` and turn Phase 1's `mabna.fn_purchase_line_amounts` into a thin wrapper over it, so every later figure has exactly one definition (research R1). Nothing else may build on the new base until Flow P0 passes.

- [X] T001 Create migration `db/migrations/0007_purchasing_reports.sql` (forward-only, idempotent: `ADD COLUMN IF NOT EXISTS`, `CREATE TABLE IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS`, `CREATE OR REPLACE FUNCTION`; do not edit `0005`/`0006`) with, in this order:
  - `mabna.purchase_document.jalali_key integer GENERATED ALWAYS AS (jalali_year * 10000 + jalali_month * 100 + jalali_day) STORED`; indexes `idx_purchase_document_jalali_key (jalali_key) WHERE NOT cancelled` and `idx_purchase_document_vendor_day (vendor_id, jalali_key) WHERE NOT cancelled`;
  - table `mabna.product_category` (`category_id bigint` identity PK, `source_key text NOT NULL UNIQUE`, `name_fa text NOT NULL`, `synced_at timestamptz NOT NULL`; `search_key` is added in T005);
  - `mabna.fn_purchase_lines(p_from_key int, p_to_key int, p_basis text DEFAULT 'line')` — STABLE, SECURITY INVOKER — returning one row per line of every non-cancelled document with `jalali_key BETWEEN p_from_key AND p_to_key`, columns exactly as data-model.md §2 (`doc_type`, `document_id`, `line_id`, `jalali_year`, `jalali_month`, `jalali_key`, `vendor_id`, `vendor_source_key`, `product_id`, `product_source_key`, `category_key` = `product.drug_group_key`, `category_id` from `product_category` joined on `drug_group_key = source_key` (text join; missing → NULL), `quantity`, `gross_amount`, `discount_amount`, `line_amount` = `gross_amount − discount_amount`, `vat_amount`, `sign` = +1 invoice / −1 return); the document-discount allocation CTEs are **moved verbatim** from `0006`'s `fn_purchase_line_amounts` (integer units of 0.0001, round half away from zero, remainder to the largest line, ties by lowest line key `COLLATE "C"`, partitioned by `document_id` over all lines of the document);
  - `CREATE OR REPLACE FUNCTION mabna.fn_purchase_line_amounts(...)` with the **same signature and output columns** as `0006`, body: `SELECT doc_type, document_id, line_id, product_source_key, line_amount, vat_amount FROM mabna.fn_purchase_lines(p_year*10000+p_month*100+1, p_year*10000+p_month*100+31, p_basis) WHERE (p_vendor_key IS NULL OR vendor_source_key = p_vendor_key) AND (p_product_key IS NULL OR product_source_key = p_product_key)`; leave `fn_purchase_totals`, `fn_top_vendors`, `fn_top_products`, `fn_purchase_trend`, `fn_first_data_month`, `fn_active_vendors` untouched.
- [X] T002 [P] Add a "Range base" subsection to `docs/metrics/purchasing.md` stating that `mabna.fn_purchase_lines` is now the single line-amount definition, that `fn_purchase_line_amounts` and every Phase 1 function read it, why results are identical (allocation per whole document, month = day range `[yyyymm01, yyyymm31]`), and that Flow P0 (Overview before/after diff + Phase 1 reconciliation rerun) gates any change to it.

**Checkpoint (P0 gate)**: tester runs quickstart Flow P0 — the Overview JSON for two months is identical before and after `0007`, and the Phase 1 reconciliation CLI exits 0 with every metric `pass`. A mismatch blocks all later phases; fix T001, never the Overview.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Search keys and the rest of the schema, the period model, new source items, the shared report engine (filters, catalog, SQL templates, documents/lines, print twin, envelope), the controller shell and the shared frontend pieces every story uses.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Schema (sequential edits of `0007`)

- [X] T003 Append to `db/migrations/0007_purchasing_reports.sql` `mabna.fn_search_key(p_text text) RETURNS text IMMUTABLE`: NULL → `''`; map ي and ى → ی, ك → ک; Persian ۰–۹ and Arabic-Indic ٠–٩ → 0–9; remove all whitespace, U+200C (ZWNJ), U+0640 (tatweel) and `|`; then `lower()` (data-model §2, research R6).
- [X] T004 Append to `db/migrations/0007_purchasing_reports.sql`: `mabna.vendor.code text NULL`; `mabna.vendor.search_key text GENERATED ALWAYS AS (mabna.fn_search_key(name_fa || '|' || coalesce(code, ''))) STORED`; `mabna.product.search_key text GENERATED ALWAYS AS (mabna.fn_search_key(name_fa)) STORED`; indexes `idx_product_drug_group_key (drug_group_key)` and `idx_purchase_document_vendor_source_key (vendor_source_key)` (depends on T003).
- [X] T005 Append to `db/migrations/0007_purchasing_reports.sql`: `mabna.product_category.search_key text GENERATED ALWAYS AS (mabna.fn_search_key(name_fa)) STORED`, and `CREATE OR REPLACE mabna.fn_sync_fingerprint()` extended to hash `vendor.code` into the vendor row, `product.drug_group_key` into the product row, and add a `product_category` row (`source_key`, `name_fa`), excluding generated columns (data-model §2; depends on T004).

### Period model (pure, tests first)

- [X] T006 [P] Write `backend/Zharfa.Api.Tests/Purchasing/Reports/JalaliPeriodTests.cs`: parsing `period=month|quarter|year|custom` with `month=YYYY-MM`, `quarter=YYYY-Qn`, `year=YYYY`, `from`/`to` `YYYY-MM-DD` in Latin, Persian and Arabic-Indic digits; default = current Jalali month; `FromKey`/`ToKey` `yyyymmdd`; Persian `Label` («آبان ۱۴۰۵», «فصل سوم ۱۴۰۵», «سال ۱۴۰۵», «۱۴۰۵/۰۷/۱۰ تا ۱۴۰۵/۰۸/۲۴»); `Previous()`: 1405-01 → 1404-12, 1405-Q1 → 1404-Q4, 1404 → 1403 (1403 leap, Esfand 30 included), custom 1405-07-10..1405-08-24 (45 days) → 1405-05-27..1405-07-09; errors: month 13, quarter 5, Esfand 30 in non-leap 1404, `to < from`, whole range in the future, custom > 1,098 days; range past today trimmed with `TrimmedTo` set.
- [X] T007 [P] Write `backend/Zharfa.Api.Tests/Purchasing/Reports/TrendWindowTests.cs`: preset month/quarter/year → 12 months ending with the preset's last month (year preset = its own 12 months); custom range → months intersecting the range with `partial: true` on clipped first/last buckets, ≤ 37 buckets; `TrendMonths(profile: true)` → always 12 months ending with the period's last month, also for a custom range.
- [X] T008 Implement `backend/Zharfa.Api/Purchasing/Reports/JalaliPeriod.cs` (pure value type reusing `backend/Zharfa.Api/Analytics/JalaliDate.cs` and `backend/Zharfa.Api/Purchasing/JalaliMonth.cs`): `Kind` `month|quarter|year|custom`, inclusive `From`/`To`, `FromKey`/`ToKey`, `Label`, `TrimmedTo`, `Previous()`, `TrendMonths(bool profile)`, `Parse(...)` returning Persian validation messages; validation "month 1..12, quarter 1..4, year in `JalaliMonth.MinYear..MaxYear`, valid day for the month (Esfand 30 only in leap years), `To ≥ From`, not wholly in the future, custom length ≤ 1,098 days"; day arithmetic via Julian day numbers (depends on T006, T007).

### New source items: vendor code and product category (tests first)

- [X] T009 [P] Extend `backend/Zharfa.Api.Tests/Sync/SourceMapValidationTests.cs`: a Phase 1 map without the new items still loads; `productCategory` validated like `vendor`/`product` (identifier policy, hard exclusions, must be on the allowlist, `hostFiltered` false and explicit for views, valid `change.strategy`, `key`/`name` identifiers); `vendor.code` and `product.categoryKey` identifiers checked when a column is set; `product.categoryKey` without `productCategory` is not an error.
- [X] T010 [P] Extend `backend/Zharfa.Api.Tests/Sync/PurchaseRowMapperTests.cs`: vendor code normalized with `PersianText.Normalize` (empty → NULL); product category key mapped to `drug_group_key` (NULL when unmapped/empty); category master row normalized with `PersianText.NormalizeName` (empty → «(بدون نام)»).
- [X] T011 [P] Extend `backend/Zharfa.Api.Tests/Purchasing/MetricAvailabilityTests.cs` for the new figures × production/demo × statuses (`confirmed`, `corrected`, `candidate`, `unknown`, unmapped): `vendorCode`, `category` (also unavailable when `topProducts` is), `quantity`, `vendorReport` (= `topVendors` reason), `productReport` (= `topProducts` reason), `returnsReport` (`returns` reason, or vendor key, or product key), `transactions` (`net` reason); `candidate` usable only in demo mode; `no-successful-sync` overrides all.
- [X] T012 Extend the map model in `backend/Zharfa.Api/Mabna/Sync/SourceMap.cs` with optional `entities.vendor.code`, `entities.product.categoryKey` (column + status) and optional entity `entities.productCategory` (`object`, `kind`, `hostFiltered`, `status`, `key`, `name`, `change`) per contracts/source-map-delta.md; `version` stays `1`.
- [X] T013 Apply the delta's validation rules in `backend/Zharfa.Api/Mabna/Sync/SourceMapValidator.cs` (depends on T009, T012).
- [X] T014 Map the new items in `backend/Zharfa.Api/Mabna/Sync/PurchaseMapping.cs` and `backend/Zharfa.Api/Mabna/Sync/PurchaseRowMapper.cs` (vendor code, product category key, category master rows) (depends on T010, T012).
- [X] T015 In `backend/Zharfa.Api/Mabna/Sync/SyncRunner.cs`: write `mabna.vendor.code` and `mabna.product.drug_group_key`; when `productCategory` is mapped, read it in full with a key diff on every run (Phase 1 master rule) into `mabna.product_category` inside the run's single data transaction, record `source_record`/`sync_log` under `table_logical = product_category`; when unmapped, skip it without error (depends on T005, T014).
- [X] T016 [P] Add `product_category: 'گروه‌های دارویی'` to `TABLE_LABELS` in `frontend/src/pages/DataSync.tsx`.
- [X] T017 Extend `backend/Zharfa.Api/Purchasing/MetricAvailability.cs` with figures `vendorCode`, `category`, `quantity`, `vendorReport`, `productReport`, `returnsReport`, `transactions` per research R8, same reason codes and usability rule (depends on T011, T012).
- [X] T018 [P] Add the Phase 2 entries to the made-up example map and allowlist: `vendor.code` (`SimVendorCode`, candidate), `product.categoryKey` (`SimDrugGroupID`, candidate), `productCategory` (`dbo.SimDrugGroup`, `DrugGroupID`, `DrugGroupName`, candidate, strategy `full`) in `deploy/mabna-sim/example/purchase-source-map.example.json`, and `dbo.SimDrugGroup` in `deploy/mabna-sim/example/purchase-allowlist.example.txt`.
- [X] T019 [P] Add a "Phase 2 scenario (spec 004)" section to `deploy/mabna-sim/README.md`: regenerate with a `SimDrugGroup` master, `SimDrugGroupID` on products and `SimVendorCode` on vendors in the gitignored `generated/scenario.py`, plus the quickstart §0 cases (vendor named with Arabic ي/ك, vendor name with a ZWNJ, two vendors with the same normalized name and different codes, a product without a category, a product whose category key is missing from the master, three years of documents, a return dated in a later month than its invoice, a cancelled document, > 1,000 documents in one year); if the generator needs code changes keep them generic (no real names) in `deploy/mabna-sim/gen_seed.py` and/or `deploy/mabna-sim/seedgen.py`.
- [X] T020 [P] Append items 40–46 to `specs/003-real-purchase-data/source-verification.md` (logical names only): 40 vendor code shown to users (candidate), 41 product drug group key (candidate), 42 drug group master table (candidate), 43 key (candidate), 44 name (candidate), 45 change marker (candidate, strategy `full`), 46 drug class second level (unknown, not built in Phase 2).
- [X] T021 [P] Add section "7.6 Phase 2 items" to `specs/002-mabna-accounting-connector/onsite-checklist.md`: per item 40–46 the catalog-metadata check to run, where to record it, the rule that the drug group object joins the live allowlist only once `confirmed`/`corrected`, and that `HOST_ID()` views are never read.

### Report engine (tests first)

- [X] T022 [P] Write `backend/Zharfa.Api.Tests/Purchasing/Reports/ReportCatalogTests.cs`: the seven report ids exist with grouping, applicable filters and sort allowlists exactly as data-model §4 (by-vendor: product, category; by-product: vendor, category; by-category: vendor; trend: vendor, product, category; top-products: vendor, category; returns: vendor, product, category; transactions: vendor, product, category, docType); default sort first; search target per report (trend: vendor name/code or product name); every report paged (trend included, FR-019); comparison applicable on all reports except `transactions`, and on the `vendors` directory but not `documents`; unknown id rejected.
- [X] T023 [P] Write `backend/Zharfa.Api.Tests/Purchasing/Reports/ReportFilterTests.cs`: a filter not declared by the report → 400 «این فیلتر برای این گزارش معتبر نیست»; unknown sort key (e.g. `drop_table`) → 400; `dir` only `asc|desc`; `page ≥ 1`; `pageSize ∈ {25, 50, 100}` (default 25); page clamping to the last page (including 0 rows → page 1); `~unknown` accepted only for vendor/product/category; `month` must lie within the period; `docType ∈ {invoice, return}`; `q` ≤ 100 characters, `%`, `_`, `\` escaped; empty-after-trim `q` = no search; `q` accepted on all seven reports including `trend` (vendor-or-product target), and `trend` honors `page`/`pageSize` with clamping (37 buckets, page size 25 → 2 pages; page 3 → clamped to 2); `match` accepted only on `documents`; `compare=true` on `transactions`, `documents` or the comparison request → 400; generated SQL text contains only catalog template fragments.
- [X] T024 [P] Write `backend/Zharfa.Api.Tests/Purchasing/Reports/PrintLimitTests.cs`: print requests ignore `page`/`pageSize`, fetch `LIMIT 1001`, return ≤ 1,000 rows, `limit: 1000`, `truncated` true only when `totalRows > 1000`.
- [X] T025 Implement `backend/Zharfa.Api/Purchasing/Reports/ReportCatalog.cs`: report ids → grouping enum (`vendor`, `product`, `category`, `vendorProduct`, `month`, `document`), applicable filters, sort key → fixed SQL expression allowlist with deterministic tiebreak (`key COLLATE "C"`), search target (trend: vendor `search_key` or product `search_key` at line level), paged (true for every report, trend included), comparison applicable (false for `transactions` and `documents`), print orientation (landscape for `transactions` only), and the extra non-report lists (`vendors` directory: sort `net`↓, `name`, `code`, `lastPurchase`; `documents`: filters period, vendor, product, category, month, docType, `q` on document number or vendor name, sort `date`↓, `number`, `vendor`, `net`) (depends on T022).
- [X] T026 Implement `backend/Zharfa.Api/Purchasing/Reports/ReportFilter.cs`: parse and validate `JalaliPeriod`, `compare`, `vendor`/`product`/`category` (source key or `~unknown`), `month`, `docType`, `q`, `match` (documents only), `sort`, `dir`, `page`, `pageSize` against the catalog entry; `compare` only where the catalog allows it; print mode without paging and with the 1,000-row limit; Persian 400 messages (depends on T008, T023, T024, T025).
- [X] T027 Implement `backend/Zharfa.Api/Purchasing/Reports/ReportDtos.cs`: envelope (`dataLabel`, `lastSuccessfulSync {at, atJalali}`, `currency` `IRR`, `currencyLabel` «ریال», `discountBasis`, `period {kind, from, to, label, trimmedTo}`, `previousPeriod`, `filters[] {name, value, label}`, `unavailable[] {figure, reason}`), table envelope (`page`, `pageSize`, `totalRows`, `sort`, `dir`, `totals`, `previousTotals`, `rows`), print envelope (`totalRows`, `truncated`, `limit`, `totals`, `rows`), grouped/trend/returns/document/line row records, lookup item — field names exactly as contracts/api.md; amounts as decimal strings; **no** balance/payment/credit/debt/paid property.
- [X] T028 Implement `backend/Zharfa.Api/Purchasing/Reports/ReportSql.cs`: fixed templates over `mabna.fn_purchase_lines(@from, @to, @basis)` — vendor filter at document level, product/category filters at line level, `~unknown` buckets (vendor: NULL key or missing from master; product: NULL or missing; category: unknown product, NULL `category_key`, or key missing from `product_category`); search via `search_key LIKE '%' || mabna.fn_search_key(@q) || '%' ESCAPE '\'`; a line-level vendor-or-product search predicate (vendor `search_key` of the document OR product `search_key` of the line) shared by the trend `q` and the documents `match`; grouped measures `net`, `gross`, `returns`, `vat`, `vatInvoice`, `vatReturn`, `netQuantity`, `invoiceQuantity`, `returnQuantity`, `quantityMissingLines`, `documentCount`, `lineCount`; document rows with in-scope `preDiscount`, `discount`, `vat`, `net`, `signedNet`, `quantity`, `lineCount`; `count(*) OVER ()` and `sum(...) OVER ()` for totals over the full filtered result in the same statement; `ORDER BY` from the allowlist; `LIMIT/OFFSET` (print: `LIMIT 1001`) (depends on T001, T004, T025).
- [X] T029 Implement the shared core of `backend/Zharfa.Api/Purchasing/Reports/PurchasingReportService.cs`: build the envelope (data label from `DataModeOptions`, last successful sync via `SyncTimeText`, discount basis from `PurchasingOptions`, resolved period and filter echo with Persian labels looked up from the masters, availability from `MetricAvailability` with all figures `null` + `no-successful-sync` when no sync succeeded, filter on an unavailable dimension → 200 with the report marked unavailable); execute a catalog query with parameters only, 15 s command timeout mapped to a 503 «گزارش در زمان مجاز آماده نشد؛ بازه را کوتاه‌تر کنید.»; clamp page (rerun at the last page when beyond) and return the effective page; shared print path (same filters, sort and auth, ≤ 1,000 rows, `truncated`) (depends on T017, T026, T027, T028).
- [X] T030 Add to `backend/Zharfa.Api/Purchasing/Reports/PurchasingReportService.cs` the documents list (`documents` catalog entry: in-scope sums per document under `product`/`category` and `match`, totals `net` = Σ `signedNet`, `invoiceAmount`, `returnAmount`, `netQuantity`, `lineCount`, `documentCount`, `quantityMissingLines`, print twin) and document lines (only in-scope lines under `product`/`category`/`match`, or every line with `all=true`; not paged, capped at 1,000; totals `lineAmount`, `signedAmount`, `quantity`; unknown or cancelled document → not found) (depends on T029).
- [X] T031 Add lookups to `backend/Zharfa.Api/Purchasing/Reports/VendorDirectoryService.cs`: `vendors`, `products`, `categories` searched via `mabna.fn_search_key`, `limit` default 20, max 50, items `{key, name, code}`; `categories` returns no items with `unavailable: category` when the category source is unusable; vendor `code` `null` when `vendorCode` is unavailable (depends on T029).
- [X] T032 Create `backend/Zharfa.Api/Controllers/PurchasingReportsController.cs` at `api/purchasing` with `[Authorize(Roles = "manager,admin")]` (not `[DemoOnly]`), Persian error envelope `{ "message" }` for 400/404/503, and endpoints `GET documents`, `GET documents/print`, `GET documents/{documentId}/lines`, `GET lookup/{vendors|products|categories}`; register `JalaliPeriod`-dependent services (`PurchasingReportService`, `VendorDirectoryService`) in `backend/Zharfa.Api/Program.cs` (depends on T030, T031).
- [X] T033 Add `PurchasingReports` (true for every signed-in user; navigation only) to `backend/Zharfa.Api/Platform/SystemModules.cs` so `GET /api/system/status` returns `modules.purchasingReports` (contracts/api.md §8).

### Shared frontend (no new packages)

- [X] T034 [P] Add Phase 2 types and calls to `frontend/src/services/api.ts`: `SystemModules.purchasingReports`, report envelope, table envelope, print envelope, document/line rows, lookups, and a generic `getPurchasing(path, params)` that builds the query string and surfaces the server's Persian `message` for 400/404/503.
- [X] T035 [P] Extend `frontend/src/utils/jalali.ts` with Jalali quarter helpers (current quarter, `YYYY-Qn` label «فصل اول…چهارم»), year helpers, and `YYYY-MM-DD` day-range parsing/validation (Persian, Arabic-Indic or Latin digits) reusing `parseJalaliInput` and `isLeapJalaliYear`.
- [X] T036 Create `frontend/src/hooks/useReportQuery.ts`: one filter state (period kind + value or custom range, compare, vendor, product, category, month, docType, q debounced ~300 ms, sort, dir, page, pageSize), reset `page` to 1 on filter/search/sort change, build the query string for the screen and the `/print` twin with only the parameters the current report or list accepts (never `compare` to `documents`/`transactions`/`vendors/compare`), adopt the server's effective `page` (depends on T034, T035).
- [X] T037 [P] Create `frontend/src/components/PeriodPicker.tsx` (month/quarter/year presets with a Jalali picker reusing `frontend/src/components/JalaliMonthPicker.tsx`, custom from/to day inputs with Persian validation messages; native controls, visible focus) and `frontend/src/components/EntityPicker.tsx` (vendor/product/category combobox backed by `lookup/*`, shows «داده در دسترس نیست» when the lookup is unavailable) (depends on T034, T035).
- [X] T038 Create `frontend/src/components/FilterBar.tsx` composing `PeriodPicker` and `EntityPicker`, offering **only** the filters the current report declares (prop), showing active filters, and a search input (depends on T036, T037).
- [X] T039 [P] Create `frontend/src/components/SortHeader.tsx` (header button with `aria-sort`), `frontend/src/components/Pager.tsx` (page, page size 25/50/100, total rows in Persian digits) and `frontend/src/components/UnavailableNote.tsx` («داده در دسترس نیست» with reason text, including "no sync has completed").
- [X] T040 Create `frontend/src/components/ReportTable.tsx`: column definitions prop, `SortHeader`s, server-paged rows, totals footer labelled as covering the full filtered result, «نامشخص» rows styled distinctly, empty state (not an error), optional per-row action slot (drill-down / expand), last-sync time and data label from the envelope, amounts via `faAmount` with «ریال», quantities via `faQty` (depends on T039).

**Checkpoint**: Foundation ready — `0007` complete, period model and source items tested, report engine and documents/lines endpoints live, shared UI ready. User stories can now proceed.

---

## Phase 3: User Story 1 — Find a vendor and read its purchasing profile (Priority: P1) 🎯 MVP

**Goal**: Persian-tolerant vendor directory and a vendor profile (net, gross, returns, VAT, previous-period comparison, 12-month trend, top products, returns, documents) for the selected period, without any vendor financials.

**Independent Test**: Quickstart Flows P1 and P2 — search a ی/ک vendor with ي/ك (and reverse), with ZWNJ/space variants and Persian-digit codes; open each Overview top-5 vendor for the same month and compare `totals.net`; no financial fields in the JSON.

- [X] T041 [P] [US1] Write `backend/Zharfa.Api.Tests/Purchasing/Reports/NoVendorFinancialsTests.cs`: reflect over every public type in the `Zharfa.Api.Purchasing.Reports` namespace that is used as a response DTO — `ReportDtos.cs`, the comparison DTOs in `VendorComparisonService.cs`, and any other DTO file added under `backend/Zharfa.Api/Purchasing/Reports/` (directory row, profile, comparison, comparison print, report rows, document/line rows) — found by assembly scan of the namespace rather than a fixed file list, recursively and assert no property name contains `balance`, `payment`, `debt`, `credit` or `paid` (case-insensitive).
- [X] T042 [US1] Implement the directory in `backend/Zharfa.Api/Purchasing/Reports/VendorDirectoryService.cs`: every vendor in `mabna.vendor` LEFT JOINed with the period's lines grouped by vendor (`net` `"0.0000"` when no purchases, `null` only when `vendorReport` unavailable), `code` (`null` with `unavailable: vendorCode` when unusable; search then matches names only — compare against `mabna.fn_search_key(name_fa)` instead of the stored `search_key`, which includes the code), `lastPurchaseDate` = latest non-cancelled **invoice** date all time (Jalali text; `null` when none), search via `search_key`, sort `net`↓ (then name), `name`, `code`, `lastPurchase`, paging with clamping, totals, print twin (depends on T029, T031).
- [X] T043 [US1] Implement the profile in `backend/Zharfa.Api/Purchasing/Reports/VendorDirectoryService.cs`: `vendor` required (400 when missing or `~unknown`, 404 when not in the master); header `{key, name, code}`; totals `net`, `gross`, `returns`, `vat`, `vatInvoice`, `vatReturn`, `netQuantity`, `documentCount` (zeros, not unavailable, when no purchases); comparison always computed (`previousNet`, `delta`, `percent` via `ComparisonMath`, `percent` null on zero base, `null` + `no-previous-data` when the previous period starts before `mabna.fn_first_data_month()`); trend = `JalaliPeriod.TrendMonths(profile: true)` grouped by month with `available: false`/`net: null` before the first synced month; `topProducts` ≤ 10 by net; returns grouped by product; first page of documents via the documents list with `vendor=<key>`; sub-queries sequential on one connection (depends on T030, T042).
- [X] T044 [US1] Add `GET vendors`, `GET vendors/print` and `GET vendor-profile?vendor=` to `backend/Zharfa.Api/Controllers/PurchasingReportsController.cs` (depends on T032, T042, T043).
- [X] T045 [P] [US1] Create `frontend/src/pages/Vendors.tsx`: `FilterBar` (period + search only), `ReportTable` with name, code («داده در دسترس نیست» when unavailable), net «ریال», last purchase date (Jalali), sort and paging; empty state for no match; row action opens the profile (in-view sub-state, no router) with the current period (depends on T038, T040).
- [X] T046 [US1] Create `frontend/src/pages/VendorProfile.tsx`: header (name, code), period `FilterBar`, KPI cards (reuse `frontend/src/components/KpiCard.tsx`) for net, gross («خرید ناخالص»), returns («برگشت از خرید»), VAT as separate figures, previous-period comparison with both period labels, 12-month trend (reuse `frontend/src/components/TrendChart.tsx`, months without data marked as such, not zero), top products list, returns list, paged documents table whose rows expand to their lines (`documents/{id}/lines`), last-sync time and data label; every block reloads on period change; no balance or payment element (depends on T045).
- [X] T047 [US1] Add the «تأمین‌کنندگان» tab (`vendors` view, visible when `modules.purchasingReports`) to `frontend/src/App.tsx` rendering `Vendors`/`VendorProfile` (depends on T046).
- [X] T048 [US1] Make each Overview top-5 vendor in `frontend/src/pages/OverviewPurchasing.tsx` (via `frontend/src/components/TopList.tsx` item action) open the vendor profile with the Overview's month (unknown vendor row not linked) (depends on T047).

**Checkpoint**: US1 complete — Flows P1 and P2 pass; exit criteria 1 and 2 hold.

---

## Phase 4: User Story 2 — Standard purchasing reports (Priority: P1)

**Goal**: Seven standard reports with amount and quantity, filter, search, sort and server paging.

**Independent Test**: Quickstart Flow P3 — each report opens, filters, sorts, searches and pages; by-vendor/by-product/by-category `totals.net` = Overview total (I1) and = direct SQL over `fn_purchase_lines`; inapplicable filter and bad sort → 400.

- [X] T049 [US2] Implement `by-vendor`, `by-product` and `by-category` in `backend/Zharfa.Api/Purchasing/Reports/PurchasingReportService.cs`: grouped rows `{key, name, code, unknown, net, gross, returns, vat, netQuantity, documentCount}` with the «نامشخص» row (`key: null`) included in totals; `by-category` and category columns marked unavailable per `category` figure; quantity columns `null` when `quantity` is unavailable; `quantityMissingLines` in totals (depends on T029).
- [X] T050 [US2] Implement `top-products` (rank 1-based in the current sort, default net↓, re-rank by `quantity`↓) and `returns` (return documents only, grouped vendor × product, dated by the return document's own date, rows `{vendorKey, vendorName, productKey, productName, unknownVendor, unknownProduct, returns (positive), quantity, documentCount}`) in `backend/Zharfa.Api/Purchasing/Reports/PurchasingReportService.cs` (depends on T049).
- [X] T051 [US2] Implement `trend` in `backend/Zharfa.Api/Purchasing/Reports/PurchasingReportService.cs`: buckets from `JalaliPeriod.TrendMonths(profile: false)` grouped by (`jalali_year`, `jalali_month`), rows `{month, label, net, netQuantity, available, partial}`, `available: false`/`net: null` before the first synced month; `q` applies the line-level vendor-or-product search predicate before grouping (buckets and totals sum matching lines only; a month without matches shows `0`); paged like every report (25/50/100, clamping) with `totalRows`/`totals` over every bucket; default order month↑, sort by `net` allowed (FR-019, no exemption) (depends on T049).
- [X] T052 [US2] Implement `transactions` in `backend/Zharfa.Api/Purchasing/Reports/PurchasingReportService.cs` over the documents template: every non-cancelled invoice and return in the period with number, `docType` + `docTypeLabel` («فاکتور خرید» / «برگشت از خرید»), Jalali date, vendor, `preDiscount` («مبلغ پیش از تخفیف»), `discount`, `vat`, `net` («خالص») with `preDiscount − discount = net` (I5); search on document number or vendor name; `docType` filter (depends on T030).
- [X] T053 [US2] Add `GET reports/{reportId}` and `GET reports/{reportId}/print` to `backend/Zharfa.Api/Controllers/PurchasingReportsController.cs` (unknown `reportId` → 404; inapplicable filter / bad sort / bad page size → 400) (depends on T049–T052).
- [X] T054 [US2] Create `frontend/src/pages/PurchasingReports.tsx`: report picker for the seven reports (Persian names), `FilterBar` with the report's declared filters only and a search box on every report (trend: «جستجوی تأمین‌کننده یا کالا»), `Pager` on every report including trend, `ReportTable` with per-report columns (net, gross, returns, VAT, quantity, documents; trend months with partial/without-data markers; top-products rank and a quantity re-rank control; returns vendor × product; transactions with pre-discount, discount, VAT, net), quantity header «تعداد (واحد ثبت‌شده)» and a footnote when `quantityMissingLines > 0`, unavailable notes per figure, statement of period, active filters, last-sync time and data label (depends on T038, T040, T053).
- [X] T055 [US2] Add the «گزارش‌های خرید» tab (`reports` view, `modules.purchasingReports`) to `frontend/src/App.tsx` (depends on T047, T054; same file, sequential).

**Checkpoint**: US2 complete — Flow P3 passes; exit criterion 3 holds.

---

## Phase 5: User Story 3 — Drill down from a report row to the documents (Priority: P1)

**Goal**: Every report row opens the documents and lines behind it under the same period and filters, and the lines sum to the row.

**Independent Test**: Quickstart Flow P4 — for one row each of by-vendor, by-product, by-category, trend and returns (and the «نامشخص» rows), row `net`/`netQuantity` = drill-down `totals`; returns row `returns` = `totals.returnAmount`; a document's in-scope `signedNet` = its lines' `totals.signedAmount`; totals identical on pages 1 and 2.

- [X] T056 [US3] Emit a `drill` object on every row in `backend/Zharfa.Api/Purchasing/Reports/PurchasingReportService.cs`: by-vendor `{vendor}`, by-product/top-products `{product}`, by-category `{category}`, trend `{month}` plus `match: q` when a search is active, returns `{vendor, product, docType: "return"}`, using `~unknown` for unknown rows; documents list accepts the combination of the report's filters plus the drill filter (depends on T049–T052).
- [X] T057 [US3] Create `frontend/src/components/DrillDownDialog.tsx`: modal dialog (`role="dialog"`, `aria-modal`, focus trap, Escape closes, focus returns to the opening button) listing the documents for report filters + row `drill` (server-paged, totals covering all pages, net, invoice amount, return amount, net quantity), each document expandable to its in-scope lines (`documents/{id}/lines` with the same product/category filters; signed amounts negative for returns), states period and filters (depends on T040).
- [X] T058 [US3] Wire a drill-down button on every grouped/trend/returns row (including «نامشخص» rows) in `frontend/src/pages/PurchasingReports.tsx` opening `DrillDownDialog`, and let transaction rows expand to their lines (depends on T054, T056, T057).

**Checkpoint**: US3 complete — Flow P4 passes; exit criterion 4 holds.

---

## Phase 6: User Story 4 — Compare vendors side by side (Priority: P2)

**Goal**: Two to five distinct vendors over a period, five measures per vendor in one view; limits enforced on the server.

**Independent Test**: Quickstart Flow P5 — three vendors over six months show net, net quantity, document count, returns and monthly trend each, equal to each vendor's profile for the same range; six vendors → 400 with the limit message; `K1,K1` → 400 with the minimum message.

- [X] T059 [P] [US4] Write `backend/Zharfa.Api.Tests/Purchasing/Reports/VendorComparisonRequestTests.cs`: comma-separated keys trimmed and de-duplicated in request order; < 2 distinct → «برای مقایسه دست‌کم دو تأمین‌کننده لازم است»; > 5 distinct → «حداکثر پنج تأمین‌کننده را می‌توان مقایسه کرد»; `~unknown` refused; period parsed via `JalaliPeriod`.
- [X] T060 [US4] Implement `VendorComparisonRequest` and `backend/Zharfa.Api/Purchasing/Reports/VendorComparisonService.cs`: "`VendorKeys`: 2..5 distinct source keys after de-duplication (no `~unknown`), plus `Period`"; keys not in the master → 400 naming how many were not found; one grouped query over `fn_purchase_lines` restricted to the selected vendor ids computing per vendor `net`, `netQuantity`, `documentCount` (non-cancelled invoices + returns), `returns` and the monthly trend from `JalaliPeriod.TrendMonths(profile: false)` with `available`/`partial`; `months[]` header; vendors in request order (depends on T029, T059).
- [X] T061 [US4] Add `GET vendors/compare?vendors=K1,K2[,…]` and its print twin `GET vendors/compare/print` (same validation, authorization and body, `truncated: false`, `limit: 1000`; FR-021) to `backend/Zharfa.Api/Controllers/PurchasingReportsController.cs` (routes declared so they never collide with `vendor-profile`; `compare` parameter → 400) and register `VendorComparisonService` in `backend/Zharfa.Api/Program.cs` (depends on T044, T060).
- [X] T062 [P] [US4] Create `frontend/src/components/MultiTrendChart.tsx`: inline-SVG multi-series monthly trend (one series per vendor, distinguishable by color and marker/dash for AA, legend, months without data shown as gaps, partial months marked), following `frontend/src/components/TrendChart.tsx`.
- [X] T063 [US4] Create `frontend/src/pages/VendorComparison.tsx`: period picker only (no vendor filter), vendor selection via `EntityPicker` (duplicates ignored), add control disabled at five with «حداکثر پنج تأمین‌کننده را می‌توان مقایسه کرد» stated, run disabled below two with the minimum stated, one view with the five measures per vendor and `MultiTrendChart`, the period label, the last-sync time and the data label from the envelope («داده‌های واقعی شرکت» / «داده‌های نمایشی»; FR-005), no comparison toggle (FR-020), server 400 messages shown (depends on T037, T061, T062).
- [X] T064 [US4] Add the «مقایسه تأمین‌کنندگان» tab (`compare` view, `modules.purchasingReports`) to `frontend/src/App.tsx` (depends on T055, T063; same file, sequential).

**Checkpoint**: US4 complete — Flow P5 passes; exit criterion 5 holds.

---

## Phase 7: User Story 5 — Consistent filters, period comparison and printing (Priority: P2)

**Goal**: Period comparison next to the previous period (preset → previous Jalali unit; custom → equal days before), and browser/desktop printing of the whole filtered result up to 1,000 rows.

**Independent Test**: Quickstart Flows P6 and P8 — previous periods for 1405-01, 1405-Q1, 1404 and a 45-day custom range; zero base → no percentage; `no-previous-data` before the first synced month; print of a filtered result with > 25 and < 1,000 rows holds every row; transactions print of a year with > 1,000 documents prints 1,000 rows with «۱٬۰۰۰ از N ردیف».

- [X] T065 [US5] Add period comparison to `by-vendor`, `by-product`, `by-category`, `trend`, `top-products`, `returns` (on returns: `previousReturns`) and the vendor directory (contracts/api.md §1, §3) in `backend/Zharfa.Api/Purchasing/Reports/ReportSql.cs`, `backend/Zharfa.Api/Purchasing/Reports/PurchasingReportService.cs` and `backend/Zharfa.Api/Purchasing/Reports/VendorDirectoryService.cs` — not to `transactions`, `documents` or the vendor comparison, which refuse `compare` (spec FR-020; enforced by T026): with `compare=true` compute `previousNet` per row (same grouping over `Previous()`), `delta`, `percent` (`ComparisonMath`; `null` on zero base), `previousTotals`, `previousPeriod` in the envelope; `no-previous-data` (comparison figures `null`) when the previous period starts before `mabna.fn_first_data_month()` (depends on T042, T049–T052).
- [X] T066 [P] [US5] Create `frontend/src/components/ComparisonCells.tsx`: previous net, absolute difference and percentage change (no percentage on zero base; «داده در دسترس نیست» for `no-previous-data`), colors at AA contrast with a sign, not color alone.
- [X] T067 [US5] Add the comparison toggle to `frontend/src/components/FilterBar.tsx` (shown only on the directory and the comparison-capable reports; hidden on transactions and the vendor comparison page) and show both period labels («دوره انتخابی» / «دوره قبل») plus `trimmedTo` notice; render `ComparisonCells` columns in `frontend/src/components/ReportTable.tsx` when `previousPeriod` is present (depends on T065, T066).
- [X] T068 [P] [US5] Create `frontend/src/components/PrintReport.tsx`: hidden print container rendering the `/print` response — header with report name, period (and comparison period), every active filter label from `filters[]`, last-sync time and data label («داده‌های واقعی شرکت» / «داده‌های نمایشی»); full table with `<thead>` repeated per page and report totals; truncation notice «۱٬۰۰۰ از N ردیف» asking to narrow the filters when `truncated`; `landscape` class for transactions; a vendor-comparison layout (five measures per vendor plus a month × vendor trend table) for the `vendors/compare/print` response.
- [X] T069 [US5] Extend the `@media print` block in `frontend/src/index.css`: hide navigation, filter bar and screen table; show the print container; A4 portrait default and a named landscape `@page` for the transactions print; `table-layout: fixed`, wrapping long names, `thead { display: table-header-group }`, RTL, no clipped columns (depends on T068).
- [X] T070 [US5] Add a «چاپ» button to `frontend/src/pages/PurchasingReports.tsx` (all seven reports, trend included), `frontend/src/pages/Vendors.tsx`, the vendor profile documents in `frontend/src/pages/VendorProfile.tsx` and `frontend/src/pages/VendorComparison.tsx` (fetches `vendors/compare/print` with the selected vendors and period) that fetches the `/print` twin with the current filters and sort (via `useReportQuery`), renders `PrintReport`, then calls `window.print()` (works unchanged in the WebView2 shell) (depends on T054, T045, T046, T063, T068, T069).
- [X] T071 [US5] Verify every page offers only applicable filters (comparison page has no vendor filter and no comparison toggle; by-category has no product filter; transactions has no comparison toggle) and that a custom range with `to < from`, wholly in the future, or > 1,098 days shows the server's Persian message, in `frontend/src/components/FilterBar.tsx` and `frontend/src/components/PeriodPicker.tsx` (depends on T067).

**Checkpoint**: US5 complete — Flows P6 and P8 pass; exit criteria 6 and 7 hold.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T072 [P] Extend `db/perf/purchasing-3y.sql` to fill `mabna.product_category`, `product.drug_group_key` and `vendor.code` for the three-year synthetic dataset (Flow P9).
- [X] T073 [P] Add a "Reports" section to `docs/metrics/purchasing.md`: measures table (data-model §3) incl. net quantity, document count, pre-discount amount vs «خرید ناخالص», discount, last purchase date, comparison and trend bucket rules, invariants I1–I5, unknown buckets, period/previous-period rules, and the statement that purchases are never payments, balance or profit (depends on T002; same file, sequential).
- [X] T074 [P] Document the `api/purchasing/*` endpoints, roles, print twins, 503 timeout and `modules.purchasingReports` in `backend/Zharfa.Api/README.md`.
- [X] T075 [P] Add a Phase 2 entry (vendor directory and profile, seven purchasing reports, drill-down, vendor comparison, period comparison, print; migration `0007`; new source items 40–46) to `CHANGELOG.md`.
- [X] T076 [P] Update the `validation_commands` header comment in `.ainative/project.yaml` to name the spec 004 unit suites and `specs/004-vendor-purchasing-reports/quickstart.md` (commands unchanged).
- [X] T077 Keyboard and contrast pass over `frontend/src/components/FilterBar.tsx`, `frontend/src/components/ReportTable.tsx`, `frontend/src/components/SortHeader.tsx`, `frontend/src/components/Pager.tsx`, `frontend/src/components/DrillDownDialog.tsx`, `frontend/src/components/MultiTrendChart.tsx` and the four pages: every control reachable by Tab with visible focus, `aria-sort` updates, dialog focus trap/Escape/return focus, Tailwind colors at WCAG 2.1 AA (FR-023, Flow P10).
- [X] T078 Re-read `backend/Zharfa.Api/Purchasing/Reports/*.cs` against the hard rules (no Mabna access, no user text in SQL, no financial fields, single `fn_purchase_lines` definition, decimal strings) and record any deviation as a fix in the same files.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: T001 first; T002 in parallel. **P0 gate** must pass before any task that reads `fn_purchase_lines` is accepted.
- **Foundational (Phase 2)**: depends on T001. Schema T003 → T004 → T005 (same file). Period model T006/T007 → T008. Source items T009–T011 (tests) → T012 → T013/T014 → T015; T017 after T011+T012. Engine T022–T024 (tests) → T025 → T026 → T027 → T028 → T029 → T030/T031 → T032. Frontend T034/T035 → T036/T037 → T038; T039 → T040. Blocks every story.
- **US1 (Phase 3)**: depends on Foundational.
- **US2 (Phase 4)**: depends on Foundational; T055 after T047 (`App.tsx`).
- **US3 (Phase 5)**: depends on US2 (report rows and page).
- **US4 (Phase 6)**: depends on Foundational and T044 (controller), T055 (`App.tsx`); otherwise independent of US2/US3.
- **US5 (Phase 7)**: depends on US1 and US2 (pages to print and compare); T065 after T049–T052.
- **Polish (Phase 8)**: after all stories.

### Story order

US1 → US2 → US3 → US4 → US5 (priority order; P1 stories before P2 when staffed by one worker).

### Shared files (sequential edits only)

- `db/migrations/0007_purchasing_reports.sql`: T001 → T003 → T004 → T005
- `backend/Zharfa.Api/Purchasing/Reports/PurchasingReportService.cs`: T029 → T030 → T049 → T050 → T051 → T052 → T056 → T065
- `backend/Zharfa.Api/Purchasing/Reports/VendorDirectoryService.cs`: T031 → T042 → T043 → T065
- `backend/Zharfa.Api/Controllers/PurchasingReportsController.cs`: T032 → T044 → T053 → T061 (incl. `vendors/compare/print`)
- `backend/Zharfa.Api/Program.cs`: T032 → T061
- `frontend/src/App.tsx`: T047 → T055 → T064
- `frontend/src/pages/PurchasingReports.tsx`: T054 → T058 → T070
- `frontend/src/pages/VendorComparison.tsx`: T063 → T070
- `frontend/src/components/FilterBar.tsx`: T038 → T067 → T071
- `docs/metrics/purchasing.md`: T002 → T073

## Parallel Opportunities

- Setup: T002 alongside T001.
- Foundational: T006, T007, T009, T010, T011, T016, T018, T019, T020, T021, T022, T023, T024, T034, T035, T039 all together (different files); T037 alongside T036.
- US1: T041 and T045 alongside backend T042–T044.
- US4: T059 and T062 together; T062 can start right after Foundational.
- US5: T066 and T068 together while T065 proceeds.
- Polish: T072–T076 together.

### Parallel example: Foundational tests

```text
Task: "JalaliPeriodTests in backend/Zharfa.Api.Tests/Purchasing/Reports/JalaliPeriodTests.cs"
Task: "TrendWindowTests in backend/Zharfa.Api.Tests/Purchasing/Reports/TrendWindowTests.cs"
Task: "ReportCatalogTests in backend/Zharfa.Api.Tests/Purchasing/Reports/ReportCatalogTests.cs"
Task: "ReportFilterTests in backend/Zharfa.Api.Tests/Purchasing/Reports/ReportFilterTests.cs"
Task: "PrintLimitTests in backend/Zharfa.Api.Tests/Purchasing/Reports/PrintLimitTests.cs"
Task: "SourceMapValidationTests additions in backend/Zharfa.Api.Tests/Sync/SourceMapValidationTests.cs"
```

### Parallel example: User Story 4

```text
Task: "VendorComparisonRequestTests in backend/Zharfa.Api.Tests/Purchasing/Reports/VendorComparisonRequestTests.cs"
Task: "MultiTrendChart in frontend/src/components/MultiTrendChart.tsx"
```

## Implementation Strategy

### MVP first

1. Phase 1 (range base) → **stop and validate Flow P0**.
2. Phase 2 (Foundational) → validation suite (quickstart §1) green.
3. Phase 3 (US1) → **stop and validate Flows P1, P2** (exit criteria 1–2).

### Incremental delivery

4. Phase 4 (US2) → Flow P3. 5. Phase 5 (US3) → Flow P4. 6. Phase 6 (US4) → Flow P5. 7. Phase 7 (US5) → Flows P6, P8. 8. Phase 8 → Flows P7, P9, P10. Phase 2 of Release 1 is done when every exit criterion below holds.

## Coverage map

### Exit criteria (`zharfa-prd.phases.md#2`)

| Exit criterion | Tasks | Flow |
|---|---|---|
| ي/ك ↔ ی/ک vendor search | T003, T004, T042, T045 | P1 |
| Profile matches the Overview (net, trend, top products, returns, documents) | T001, T043, T046, T048 | P0, P2 |
| Seven reports open, filter, sort, search, paginate (all seven, trend included: vendor-or-product search and month paging; no exemption) | T022, T023, T025–T029, T049–T055 | P3 |
| Row drill-down; lines sum to the row | T030, T056–T058 | P4 |
| Three vendors × six months, five measures; six refused | T059–T064 | P5 |
| Period comparison shows the previous period | T006, T008, T065–T067 | P6 |
| Browser print readable Persian RTL with period and filters (every report, directory, profile documents and vendor comparison) | T024, T061, T068–T070 | P8 |

### Quickstart flows

| Flow | Tasks |
|---|---|
| §1 Validation suite | T006, T007, T009–T011, T022–T024, T041, T059 |
| P0 Range base | T001, T002, T005 |
| P1 Search | T003, T004, T019, T042, T044, T045 |
| P2 Profile | T043, T044, T046, T048, T041 |
| P3 Reports | T049–T055 |
| P4 Drill-down | T028, T030, T056–T058 |
| P5 Comparison | T059–T064 |
| P6 Periods | T006–T008, T065–T067, T071 |
| P7 Availability and data modes | T011, T015, T017, T018, T020, T021, T029, T031, T039 |
| P8 Print | T024, T061, T068–T070 |
| P9 Performance | T001, T004, T072 |
| P10 Auth + keyboard | T032, T033, T057, T077 |

## Notes

- [P] = different files, no dependency on an unfinished task.
- Commit after each task or logical group with Conventional Commits; never commit generated simulator output, `scratch/`, real names or real totals.
- A P0 or invariant (I1–I5) mismatch blocks sign-off: fix the base function or the query template, never a per-report adjustment.
- If Flow P9 fails p95 < 2 s, apply the research R14 fallback (materialized line-amount table refreshed in the sync transaction) as a separate, reviewed change.
