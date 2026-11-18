# Implementation Plan: Vendor and Purchasing Reports (Release 1, Phase 2)

**Branch**: `zharfa-prd-phase-2` | **Date**: 2026-11-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/004-vendor-purchasing-reports/spec.md` · Phase ref `zharfa-prd.phases.md#2` · Governing PRD `zharfa-prd.md` v3.0 (F5 AC1–AC2, F6, F7, F8) · Rules `AGENTS.md` · Foundation `specs/003-real-purchase-data` (merged)

## Summary

Let Managers (and from Phase 3 Purchasing staff) find any vendor with Persian-tolerant search, read its purchasing profile, run seven standard purchasing reports with filters, search, sort, paging, period comparison and drill-down to documents and lines, compare two to five vendors side by side, and print any report as a Persian RTL page. Everything is computed on the server from the Phase 1 synced copy with Phase 1's metric definitions unchanged.

Technical approach (details in [research.md](research.md)):

- **One definition, any range.** A new range base function `mabna.fn_purchase_lines(from_key, to_key, basis)` carries Phase 1's line-amount rule verbatim; Phase 1's `fn_purchase_line_amounts` becomes a thin wrapper over it, so the Overview, reconciliation, reports, profile, comparison and drill-down all read one definition. Proven by rerunning the Phase 1 reconciliation and an Overview before/after diff (R1).
- **Calendar-free SQL.** A stored Jalali day key (`jalali_key`, `yyyymmdd`) on documents with partial indexes; periods (month, quarter, year, custom, previous period, trend window) are computed in a pure, unit-tested C# `JalaliPeriod` (R2–R4).
- **One report engine.** `PurchasingReportService` composes fixed SQL templates (grouping enum, sort-key allowlist, parameters only) and returns rows, the total row count and full-result totals in one statement; offset paging with clamping; a `/print` twin per endpoint returns up to 1,000 rows plus a truncation flag (R5, R10).
- **Persian search in one place.** `mabna.fn_search_key()` (ی/ک, digits, spaces, ZWNJ) feeds stored generated `search_key` columns and normalizes the query term, so both sides share one implementation (R6).
- **New verified items.** Vendor code and the drug-group category are optional source-map additions with Phase 1 statuses; a new `mabna.product_category` master is synced; production shows "data unavailable" until items are confirmed on-site (R7, R8).
- **Frontend.** Three new tabs in the existing view switcher (Vendors with profile, Purchasing reports, Vendor comparison), a shared filter bar, sortable/paged report table, drill-down dialog, print layout and inline-SVG charts. No new npm or NuGet packages (R15).

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`), TypeScript 7 / React 19, SQL (PostgreSQL 18)

**Primary Dependencies**: ASP.NET Core Web API, Npgsql (existing `NpgsqlDataSource`, as the Overview), Microsoft.Data.SqlClient (existing connector, for the sync only), Vite 8 + Tailwind 4 (existing). **No new packages** in backend, tests or frontend. `pg_trgm` is not used (R6).

**Storage**: PostgreSQL 18, schema `mabna`. One new migration `db/migrations/0007_purchasing_reports.sql` ([data-model.md](data-model.md)). Reports read the synced copy only; Mabna is touched only by the sync (now also reading the drug group master).

**Testing**: xunit (`backend/Zharfa.Api.Tests`), pure logic only — no DB or network (validation rule): periods, trend windows, query/sort/filter validation, comparison request, availability, map validation, no-financial-fields DTO scan, print cap (R17). SQL behavior, invariants I1–I5 and UI flows are proven by [quickstart.md](quickstart.md) on Docker Compose with the simulator. Frontend: `tsc -b` + Vite build and the quickstart UI steps.

**Target Platform**: Linux containers (Docker Compose) on the company LAN server; browsers and the unchanged WebView2 desktop shell (print via `window.print()`).

**Project Type**: Web application (ASP.NET Core API + React SPA).

**Performance Goals**: Every Phase 2 endpoint p95 < 2 s with three years of synced data (FR-022), measured with `db/perf/purchasing-3y.sql`; 15 s command timeout per report query; a filtered vendor report produced in < 1 minute (SC-008).

**Constraints**: Mabna read-only and reports never query it per request; server-side authorization and KPIs; Phase 1 metric definitions reused unchanged; purchases never shown as payments/balance/profit and no vendor financials at all in this phase; real and demo data never mix (labels from the server); Persian RTL, Jalali, Persian digits, «ریال»; WCAG 2.1 AA, keyboard-usable; print without a PDF pipeline; secrets only in env.

**Scale/Scope**: ~20 concurrent users; thousands of vendors/products; three years of documents (largest source table ~416k rows). 1 migration, 1 controller with 9 endpoints + print twins, 1 status flag, 3 SPA tabs, ~10 components.

**Unknowns**: None open for planning. Vendor code and drug-group category are **on-site verification items** handled as status-gated configuration (R7, R8), as in Phase 1. Owner-facing assumptions are listed in research R18.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md` is still the unfilled template, so the binding gates are the rules in `AGENTS.md` and `zharfa-prd.md` (§8–§9), as in Phase 1.

| Gate (AGENTS.md / PRD) | Pre-design | Post-design | How the design satisfies it |
|---|---|---|---|
| Mabna read-only; login primary, guard + allowlist secondary | Pass | Pass | Only the sync reads Mabna, through the unchanged `IMabnaReader`; the new drug-group object must be on the allowlist and passes map validation ([source-map-delta](contracts/source-map-delta.md)) |
| Never read `HOST_ID()`-filtered views live | Pass | Pass | Same `hostFiltered` rule applies to `productCategory` |
| Reports never built on assumed Mabna structures | Pass | Pass | Vendor code and category are status-gated; production shows "data unavailable" until `confirmed`/`corrected` (R8) |
| Reports read the synced copy, never Mabna per request | Pass | Pass | Every endpoint reads `mabna.*` via `NpgsqlDataSource` only (R5) |
| Every reconciled metric matches the source; mismatch blocks sign-off | Pass | Pass | Phase 1 reconciliation rerun after `0007` (quickstart P0); invariants I1–I5 checked in P3/P4 |
| Separate financial formulas; no price difference as profit | Pass | Pass | Only purchase measures; "gross purchases" vs "pre-discount amount" named and documented separately (R16); no balance/payment fields, enforced by a DTO test (R12) |
| Authorization and KPIs server-side; no client DB access | Pass | Pass | `[Authorize(Roles="manager,admin")]`, filter/sort allowlists, 2..5 comparison limit on the server; all sums in SQL |
| AI only via approved tools | Pass | Pass | No AI change; AI stays refused in production (Phase 1 `[DemoOnly]`) |
| Secrets in env only | Pass | Pass | No new secrets or configuration keys |
| Real and synthetic never mix | Pass | Pass | Same `mabna` copy and data mode; every response carries `dataLabel`; printouts state it |
| Persian-first RTL, Jalali, Rial | Pass | Pass | `JalaliPeriod`, Persian labels from the server, «ریال», RTL print CSS |
| Prefer existing abstractions; justify dependencies | Pass | Pass | Reuses `JalaliDate`, `JalaliMonth`, `ComparisonMath`, `MetricAvailability`, `SourceMap`/`SourceMapValidator`, `PersianText`, `SyncTimeText`, `DataModeOptions`, the view switcher, `TrendChart`, `utils/jalali.ts`, `utils/format.ts`, print CSS. No new packages |
| Release 1 scope (no sales/receivables/warehouse; browser print only) | Pass | Pass | Purchasing only; print via browser; export/saved reports left to Phase 5 |
| Update docs when behaviour changes | Pass | Pass | `docs/metrics/purchasing.md` (Reports section, I1–I5, wording), source-verification record items 40–46, onsite checklist §7.6, `backend/Zharfa.Api/README.md`, `deploy/mabna-sim/README.md`, `CHANGELOG.md`, `.ainative/project.yaml` comment |

No violations, so Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/004-vendor-purchasing-reports/
├── spec.md
├── plan.md                     # this file
├── research.md                 # Phase 0: decisions R1–R18
├── data-model.md               # Phase 1: migration 0007, functions, measures, request entities
├── quickstart.md               # Phase 1: validation flows P0–P10 and exit-criteria map
├── contracts/
│   ├── api.md                  # HTTP endpoints, payloads, auth matrix
│   └── source-map-delta.md     # vendor code, category key, productCategory entity
├── checklists/requirements.md
└── tasks.md                    # /speckit.tasks (not created here)
```

### Source Code (repository root)

```text
backend/Zharfa.Api/
├── Purchasing/Reports/                    # new
│   ├── JalaliPeriod.cs                    # presets, custom range, previous period, trend window, labels, keys
│   ├── ReportFilter.cs                    # query parsing/validation (filters, ~unknown, search, sort, paging)
│   ├── ReportCatalog.cs                   # report ids → grouping, applicable filters, sort allowlist
│   ├── ReportSql.cs                       # fixed SQL templates over mabna.fn_purchase_lines
│   ├── PurchasingReportService.cs         # reports, documents, lines, print twins
│   ├── VendorDirectoryService.cs          # directory, profile, lookups
│   ├── VendorComparisonService.cs         # compare 2..5
│   └── ReportDtos.cs                      # envelope, table, rows (no financial fields)
├── Purchasing/MetricAvailability.cs       # + vendorCode, category, quantity and report figures
├── Mabna/Sync/{SourceMap, SourceMapValidator, PurchaseMapping, PurchaseRowMapper, SyncRunner}.cs
│                                          # + vendor.code, product.categoryKey, productCategory master
├── Platform/SystemModules.cs              # + PurchasingReports flag
├── Controllers/PurchasingReportsController.cs   # api/purchasing/* (contracts/api.md)
└── Program.cs                             # DI registrations

backend/Zharfa.Api.Tests/
├── Purchasing/Reports/{JalaliPeriodTests, TrendWindowTests, ReportFilterTests, ReportCatalogTests,
│                       VendorComparisonRequestTests, PrintLimitTests, NoVendorFinancialsTests}.cs
├── Purchasing/MetricAvailabilityTests.cs  # extended
└── Sync/{SourceMapValidationTests, PurchaseRowMapperTests}.cs   # extended for the new items

db/migrations/0007_purchasing_reports.sql
db/perf/purchasing-3y.sql                  # + categories, vendor codes

frontend/src/
├── App.tsx                                # + tabs: vendors, reports, compare (modules.purchasingReports)
├── pages/{Vendors.tsx, VendorProfile.tsx, PurchasingReports.tsx, VendorComparison.tsx}
├── components/{FilterBar, PeriodPicker, EntityPicker, ReportTable, SortHeader, Pager,
│               DrillDownDialog, ComparisonCells, PrintReport, MultiTrendChart, UnavailableNote}.tsx
├── hooks/useReportQuery.ts                # filter state → query string, debounced search
├── services/api.ts                        # typed calls for contracts/api.md
├── utils/jalali.ts                        # + quarter helpers, day-range parsing
└── index.css                              # print layout (A4, repeated headers, landscape class)

deploy/mabna-sim/
├── example/purchase-source-map.example.json   # + Sim vendor code, SimDrugGroup (made-up names)
├── example/purchase-allowlist.example.txt     # + dbo.SimDrugGroup
└── README.md                                  # Phase 2 scenario additions

docs/metrics/purchasing.md                 # + Reports section
specs/003-real-purchase-data/source-verification.md   # + items 40–46
specs/002-mabna-accounting-connector/onsite-checklist.md  # + §7.6 Phase 2 items
```

**Structure Decision**: Keep the existing web-application layout. New backend code goes in `Purchasing/Reports/` next to the Phase 1 `Purchasing/` services and reuses its availability and comparison helpers; one new controller. No new projects. Frontend keeps the Phase 1 view switcher (no router) and adds pages and shared components under the existing folders.

## Implementation order (for /speckit.tasks)

1. **Foundation**: migration `0007` (columns, `product_category`, `fn_search_key`, `fn_purchase_lines`, wrapper, fingerprint, indexes); quickstart P0 (Overview unchanged, reconciliation passes). `JalaliPeriod` + tests.
2. **Source items**: map model/validator for `vendor.code`, `product.categoryKey`, `productCategory`; row mapper and sync of the category master; availability extension + tests; simulator example map/allowlist, scenario notes; verification record and onsite checklist rows.
3. **Report engine**: `ReportFilter`, `ReportCatalog`, `ReportSql`, `PurchasingReportService` (reports, documents, lines, print) + tests; controller and status flag.
4. **US1**: directory, profile, lookups (API + Vendors/VendorProfile pages, link from Overview top-5).
5. **US2 + US3**: Purchasing reports page with the seven reports, ReportTable, drill-down dialog.
6. **US4**: comparison service, endpoint, page, multi-series chart.
7. **US5**: period comparison cells (directory and comparison-capable reports; not transactions or the vendor comparison), print twin wiring (every report, the directory, profile documents and `vendors/compare/print`), PrintReport and print CSS.
8. **Docs and perf**: metrics doc, READMEs, CHANGELOG, perf script; run quickstart P0–P10.

## Test flows (summary; full commands in [quickstart.md](quickstart.md))

| Flow | Proves | Spec |
|---|---|---|
| 1 Validation suite | Builds + pure unit tests | FR-018..FR-021 logic |
| P0 Range base | Phase 1 figures unchanged; reconciliation passes | FR-002 |
| P1 Search | ی/ک, ZWNJ, digits, same-name vendors, empty state | US1 AC1–AC2, SC-001 |
| P2 Profile | Equals Overview top 5; no financial fields | US1 AC3–AC7, SC-002 |
| P3 Reports | Seven reports, totals = direct SQL, I1, paging/sort/search | US2, SC-003 |
| P4 Drill-down | Row = sum of lines, unknown rows | US3, SC-004 |
| P5 Comparison | Five measures; 6 refused; dedupe; equals profile | US4, SC-005 |
| P6 Periods | Month/quarter/year/45-day previous periods; leap year; trimming | US5 AC1–AC2, SC-006 |
| P7 Availability | Status gating per mode; no sync; labels | FR-004/005/008 |
| P8 Print | Whole result ≤ 1,000 rows; truncation notice; A4 RTL | US5 AC3–AC4, SC-007 |
| P9 Performance | p95 < 2 s with three years | FR-022, SC-008 |
| P10 Auth + keyboard | 401/200; keyboard path; AA contrast | FR-007, FR-023 |

## Risks

| Risk | Mitigation |
|---|---|
| Redefining `fn_purchase_line_amounts` changes a Phase 1 figure | Allocation CTEs moved verbatim; P0 diff + reconciliation rerun gate the change |
| Range queries with document-discount allocation exceed 2 s at three years | Day-key and vendor/day partial indexes; one statement per table; perf flow P9; fallback is a sync-time materialized line-amount table (R14) |
| Category source differs on-site (group vs class, or absent) | Map-driven; one-level design points at whichever is verified; production shows "data unavailable" until then |
| Vendor code is not a distinct item in Mabna | Code may map to the master key column; or stays unavailable and search uses names only |
| Print clips columns in WebView2 | Fixed table layout, wrapping, landscape for transactions; checked on Windows in P8 |
| Filter state gets complex without a router | One `useReportQuery` hook; filters echoed by the server drive labels and print |

## Complexity Tracking

No Constitution Check violations.
