---
description: "Task list for Vendor Financials (Release 1, Phase 6)"
---

# Tasks: Vendor Financials (Release 1, Phase 6)

**Input**: Design documents from `specs/008-vendor-financials/` — [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/api.md](contracts/api.md), [contracts/source-map.md](contracts/source-map.md), [contracts/cli-and-config.md](contracts/cli-and-config.md), [contracts/ai-tools.md](contracts/ai-tools.md), [contracts/xlsx.md](contracts/xlsx.md), [quickstart.md](quickstart.md)

**Phase ref**: `zharfa-prd.phases.md#6` · Governing PRD `zharfa-prd.md` v3.0 (F17 all, F5 AC3) · Rules `AGENTS.md` · Foundation Phases 1–5 (`specs/003-real-purchase-data` … `specs/007-ai-reports-export`) and spec 002 connector/simulator/checklist

**Tests**: Included. Plan **Testing** and research R14 require pure xunit suites in `backend/Zharfa.Api.Tests` (no database, no network): formulas, availability (production `candidate` unused), signed credit, no-profit labels, permission matrix new actions, router permitted-tool vs Purchasing no-tool. Keep `NoVendorFinancialsTests` and `NoVendorFinancialsInToolsTests`. HTTP, xlsx, UI, reconcile CLI: [quickstart.md](quickstart.md) Flows A–H (Compose + simulator), run by the tester, not by workers. Workers do not run installs or validation commands (AGENTS.md). Do not add a new test project or Ollama/GPU to `validation_commands`.

**Organization**: Setup (metrics stub + module type), then shared schema/map/availability/sync (Phase 2), then one phase per user story in spec order (US1–US7; P1 then P2), then polish.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: User story from spec.md (US1–US7)
- Every task names its exact file path(s)

## Path Conventions

Web application (existing layout): `backend/Zharfa.Api/`, `backend/Zharfa.Api.Tests/`, `frontend/src/`, `db/migrations/`. New backend types live in `backend/Zharfa.Api/VendorFinancials/` (namespace `Zharfa.Api.VendorFinancials`). Sync extras: `backend/Zharfa.Api/Mabna/Sync/VendorFinancialMapping.cs`, `VendorFinancialRowMapper.cs`. Tests: `backend/Zharfa.Api.Tests/VendorFinancials/` plus updates under `Security/`, `Ai/`, `Purchasing/`, `Audit/`, `Platform/`, `Sync/`. **No new NuGet. No new npm packages.**

## Hard rules for every task

- Authorization is decided on the server only. SPA hiding `#/financials` is cosmetic. Controller policy stays `vendor.financials`. Export = that policy **and** `export`. AI financial tools = `vendor.financials` **and** `ai.query`. Admin and Purchasing receive **403** and **no figures** on every financial surface.
- Do **not** add `balance`, `payment`, `debt`, `credit`, or `paid` properties to any type in `Zharfa.Api.Purchasing.Reports`. `NoVendorFinancialsTests` stays green. Profile balance is a **second** GET to `/api/vendor-financials/vendors/{key}`, not a purchasing DTO field.
- Purchases, payments, outstanding payments, and vendor balance stay four separately documented figures. Difference label is only «خرید منهای پرداخت». Never سود / حاشیه / وجه نقد دریافت‌شده / مانده for that difference. Never present purchases as payments or a price difference as profit.
- Vendor balance is **signed** `numeric(24,4)`: positive = company owes the vendor; negative = credit in the vendor’s favour; never floored at zero; never labeled profit. Outstanding payments are **non-negative**. Zero qualifying rows with a usable formula → `"0.0000"` / empty list, not «داده در دسترس نیست».
- Production reporting treats `candidate` and `unknown` as unusable (`formula-unconfirmed` / `source-item-unverified`). Simulator/demo may use `candidate`. Production never calls `mabna.fn_vendor_balance` until `balanceFormula.status` is `confirmed` or `corrected`.
- Reports read the synced PostgreSQL copy only. Never query live Mabna per request. Never read `HOST_ID()`-filtered views (`hostFiltered` must stay false). Same nightly job + Admin Sync now; failure keeps last good copy.
- AI only through `VendorFinancialAnalystTools` allowlist (10 s / 100 rows / 3 calls / 30 s). Cloud JSON: vendor names + aggregate amounts only — never payment-line dumps, cheque numbers, or document lines. Do not rewrite `specs/006-local-ai-purchasing` fixtures; Phase 4 `purchasing-questions.json` items 15–16 stay no-financials (`askAs: purchasing`).
- Audit append-only: `report.opened` / `report.printed` / `report.exported` / `access.denied` / `reconciliation.mismatch`. Detail = period/filters keys — **no** amounts, result rows, secrets. Persian RTL, Jalali, ریال (`currencyLabel`).
- Do not edit `specs/003`–`specs/007` spec/plan/tasks/contracts. Fill the reserved hole. Do not commit, push, or run `npm install` / `dotnet test`; the tester runs `.ainative/project.yaml` `validation_commands`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Metrics document skeleton, verification stub, and SPA module type. No reporting behavior yet.

- [X] T001 Create `docs/metrics/vendor-financials.md` with **one** definition each for payments (payment/clear date in period; every verified completed instrument type; unverified type → unavailable), outstanding payments (unpaid remainder as of period end; non-negative), and vendor balance (signed as-of period end; candidate mix `openPayablesPlusUnpaidCheques` for demo only). Purchases = pointer to `docs/metrics/purchasing.md` net purchases (do not rewrite that file). Persian labels from research R7: مانده تأمین‌کننده / پرداخت‌های معوق / پرداخت‌ها / خالص خرید / خرید منهای پرداخت. Consumers listed: Financials, profile, export, saved reports, AI, `reconcile vendor-balance`.
- [X] T002 [P] Create `specs/008-vendor-financials/source-verification.md` as a logical-status log only (`unknown` | `candidate` | `confirmed` | `corrected`). No live Mabna table names, no business rows, no amounts. Leave on-site pass/fail for vendor-balance reconcile blank until US4/on-site. Do not edit `specs/003-real-purchase-data/source-verification.md`.
- [X] T003 [P] Add `vendorFinancials: boolean` to `SystemModules` in `frontend/src/services/api.ts` (contracts/api.md §7). Do not add `#/financials` routes yet.
- [X] T004 [P] Create `specs/008-vendor-financials/fixtures/financial-questions.json` as an empty Management/Finance array (schema comment: cited vs Financials GET, or unavailable). Not a 20/20 exit. Do not copy or edit `specs/006-local-ai-purchasing` question JSON.

**Checkpoint**: docs and types exist; API still returns the Phase 3 reserved stub.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Migration `0011`, source-map extras, availability, formula totals, optional sync tables, catalogs, DI. **No user-story HTTP figures until this phase is complete.**

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Tests for the foundation (write first; they must fail until the code exists)

- [X] T005 [P] Create `backend/Zharfa.Api.Tests/VendorFinancials/VendorFinancialAvailabilityTests.cs` for `VendorFinancialAvailability` in `backend/Zharfa.Api/VendorFinancials/VendorFinancialAvailability.cs`. Reason codes exactly: `no-successful-sync` | `source-item-unverified` | `source-item-unmapped` | `no-previous-data` | `formula-unconfirmed`. Production + `balanceFormula.status=candidate` → `vendorBalance` unavailable (`formula-unconfirmed`); demo + candidate → available when mapped entities are usable. Missing optional entity → `source-item-unmapped`. No succeeded sync → all figures `no-successful-sync`. Zero rows with a usable formula is **not** tested here (service later). Pure, no DB.
- [X] T006 [P] Create `backend/Zharfa.Api.Tests/VendorFinancials/VendorFinancialTotalsTests.cs` for `VendorFinancialTotals` in `backend/Zharfa.Api/VendorFinancials/VendorFinancialTotals.cs`: candidate mix = Σ signed `payable_document.remaining_amount` (`doc_date ≤ as_of`, not cancelled) + Σ `payable_cheque.remaining_amount` still open (`issue_date ≤ as_of`, not cancelled, not fully cleared with `cleared_at ≤ as_of`); credit document remaining negative → signed total may be negative; cancelled/voided excluded; outstanding = unpaid remainders only (history amounts excluded); payments = Σ `vendor_payment.amount` with `paid_at` in `[from,to]` not cancelled; partial-pay: paid portion only in payments, remainder only in outstanding (FR-033, no double-count). Pure in-memory rows, no DB.
- [X] T007 [P] Extend `backend/Zharfa.Api.Tests/Sync/SourceMapValidationTests.cs` (and `SourceMapGuardTests.cs` if policy sits there): optional entities `payableDocument`, `payableCheque`, `vendorPayment`, `paymentAllocation` may be absent (map still valid). Present entity with `hostFiltered: true` rejected. Unknown `balanceFormula.mix` when `balanceFormula` is present → `balance-mix-unknown`. `payableCheque` mapped without `instrumentType` or `state` (column or explicit unknown) invalid. Version remains **1**. Example file `deploy/mabna-sim/example/purchase-source-map.example.json` may still omit Phase 6 keys until US1.

### Implementation

- [X] T008 Create `db/migrations/0011_vendor_financials.sql` (forward-only, idempotent `IF NOT EXISTS`; do not edit `0001`–`0010`; requires `0010`). Tables per [data-model.md](data-model.md) §1: `mabna.payable_document` (`source_key` text UNIQUE NOT NULL; `remaining_amount numeric(24,4) NOT NULL` **signed**; `cancelled boolean NOT NULL DEFAULT false`; `doc_date date NOT NULL`; index `(vendor_id, doc_date)` WHERE NOT cancelled); `mabna.payable_cheque` (`amount numeric(24,4) NOT NULL CHECK ≥ 0`; `remaining_amount numeric(24,4) NOT NULL CHECK ≥ 0`; `instrument_type text NOT NULL`; `state text NOT NULL`; `cleared_at date NULL`; `cancelled boolean NOT NULL DEFAULT false`; index `(vendor_id, issue_date)` WHERE NOT cancelled); `mabna.vendor_payment` (`amount numeric(24,4) NOT NULL CHECK ≥ 0`; `paid_at date NOT NULL`; `instrument_type text NOT NULL`; `cancelled boolean NOT NULL DEFAULT false`; index `(vendor_id, paid_at)` WHERE NOT cancelled); `mabna.payment_allocation` (only used when mapped). Jalali year/month/day smallint with the same CHECKs as `mabna.purchase_document`. Functions `mabna.fn_vendor_balance(as_of date, vendor_source_key text)`, `mabna.fn_outstanding_payments(as_of date, vendor_source_key text)`, `mabna.fn_vendor_payments(from date, to date, vendor_source_key text)` matching T006 / data-model §1.5. Drop/recreate `saved_report` `chk_saved_report_kind` adding `vendor-financials`, `vendor-financials-history`, `vendor-financials-outstanding`, `vendor-financials-purchases-vs-payments`; `chk_saved_report_permission` adding `vendor.financials`. Drop/recreate `reconciliation_run.metric` CHECK adding `vendor_balance`. Do not change purchasing tables or `fn_purchase_totals`.
- [X] T009 Extend `backend/Zharfa.Api/Mabna/Sync/SourceMap.cs` and `SourceMapEntity`: optional entities `PayableDocument`, `PayableCheque`, `VendorPayment`, `PaymentAllocation`; extra fields `RemainingAmount` (`SourceMapAmountField`, document remaining **signed** `asIs | negate | abs`), `InstrumentType` `{ column, status }`, `State` `{ column, status }`, `ClearedAt` date field, `InstrumentKey`. Top-level `balanceFormula` `{ status: confirmed|corrected|candidate|unknown, mix }` and `partialPayment` `{ status }`. Missing optional keys remain valid. Do not bump `SupportedVersion` off 1.
- [X] T010 Extend `backend/Zharfa.Api/Mabna/Sync/SourceMapValidator.cs` with [contracts/source-map.md](contracts/source-map.md) validation additions (allowlist + `MabnaObjectPolicy` for listed Phase 6 objects; `hostFiltered` true rejected; `balance-mix-unknown` only if `balanceFormula` present; `payableCheque` requires type/state column or explicit unknown).
- [X] T011 Create `backend/Zharfa.Api/VendorFinancials/VendorFinancialAvailability.cs` mirroring `MetricAvailability`: figures `vendorBalance` (usable `balanceFormula` + every entity the mix names), `outstandingPayments` / `outstandingInstruments` (usable cheque and other outstanding items), `payments` / `paymentHistory` (usable `vendorPayment` + every instrument type the formula includes), `netPurchases` (Phase 1 net unchanged), `purchasesMinusPayments` (both net and payments available), comparison columns (`no-previous-data` when previous base missing). Production usable statuses `confirmed|corrected` only; demo also `candidate`.
- [X] T012 [P] Create `backend/Zharfa.Api/VendorFinancials/VendorFinancialDtos.cs` matching [contracts/api.md](contracts/api.md) §1–§4: amounts as decimal strings with 4 places; signed balance may start with `-`; `currency`/`currencyLabel` `IRR`/`ریال`; `dataLabel` `real`|`demo`; `available: true` means Phase 6 implemented; per-figure holes in `unavailable[]` with null amounts (not `"0"`); stub `{ available: false, reason: "not-available-until-phase-6" }` must disappear once US2 wires the controller. Namespace `Zharfa.Api.VendorFinancials` only.
- [X] T013 Create `backend/Zharfa.Api/VendorFinancials/VendorFinancialTotals.cs`: source-side recompute of the three SQL functions for reconciliation (same mix and partial-pay rule as T006/T008). `discount_basis` unused → callers store `-`.
- [X] T014 [P] Create `backend/Zharfa.Api/VendorFinancials/VendorFinancialSql.cs`: parameterized Npgsql for list/detail/history/outstanding calling the `mabna.fn_*` functions and Phase 1 net via existing purchase totals (do not add a fourth SQL function). Search via `fn_search_key` on vendor name/code. Sort allowlist: `name`, `code`, `vendorBalance`, `outstandingPayments` (default balance desc). Page size same as purchasing reports.
- [X] T015 [P] Create `backend/Zharfa.Api/Mabna/Sync/VendorFinancialMapping.cs` next to `PurchaseMapping.cs`: maps optional entities to logical table names; if an entity is present in the map, its object **must** be on the allowlist.
- [X] T016 [P] Create `backend/Zharfa.Api/Mabna/Sync/VendorFinancialRowMapper.cs` next to `PurchaseRowMapper.cs`: reuse date parser, cancelled rule, amount sign; cheque remaining ≥ 0; document remaining signed; payment `amount` is paid portion only.
- [X] T017 Add optional logical names `payable_document`, `payable_cheque`, `vendor_payment`, `payment_allocation` to `LogicalTables` in `backend/Zharfa.Api/Mabna/Sync/SyncModels.cs`. **Do not** put them in `SyncOrder` (same pattern as `product_category`: installs without Phase 6 entities stay complete).
- [X] T018 Extend `backend/Zharfa.Api/Mabna/Sync/SyncRunner.cs`: after purchase lines (and category), sync mapped financial entities in the same transaction, watermarks, reject budget, and “failure keeps last good copy”. Per-table counts on Data sync. If a mapped financial entity fails, the whole run fails. Unmapped entities are skipped (purchasing-only install).
- [X] T019 [P] Add report keys `vendor-financials`, `vendor-financials-detail`, `vendor-financials-history`, `vendor-financials-outstanding` with Persian names to `backend/Zharfa.Api/Audit/ReportKeys.cs`. Update `backend/Zharfa.Api.Tests/Audit/AuditActionsTests.cs` ExpectedReportKeys **14 → 18**.
- [X] T020 Add `VendorFinancials` to `backend/Zharfa.Api/Platform/SystemModules.cs` (`VendorFinancials = permissions.Contains(Permissions.VendorFinancials)`); JSON `modules.vendorFinancials`. Update `backend/Zharfa.Api.Tests/Platform/DemoOnlyFilterTests.cs`: true for Management/Finance; false for Admin/Purchasing/empty. Production vs demo does not change the flag.
- [X] T021 Extend `backend/Zharfa.Api/DataQuality/DataQualityCatalog.cs` (and `DataQualityCatalogTests.cs`) so existing issue types that can flag payable/cheque/payment rows include the new financial report keys in `AffectedReports`. Do not invent a second DQ product.
- [X] T022 Register `VendorFinancialsService` (stub type OK until US2 fills methods) in `backend/Zharfa.Api/Program.cs`. Optionally map DbSets in `backend/Zharfa.Api/Data/ZharfaDbContext.cs`; schema remains SQL-owned (`MigrationRunner` only).

**Checkpoint**: Foundation ready — schema, map, availability, totals, optional sync, catalogs compile; reserved HTTP stub may still be in place until US2.

---

## Phase 3: User Story 1 — Verify payables sources and the balance formula before using them (Priority: P1) 🎯 MVP (source gate)

**Goal**: On-site checklist §7.8, simulator `Sim*` financial tables, example map/allowlist, and production-mode refusal of candidate formulas. Off-site Finance demo can sync candidate data; production never uses unverified items.

**Independent Test**: Quickstart Flow A — checklist §7.8 exists; example map uses `Sim*` only and `hostFiltered` false; production + `balanceFormula.status=candidate` yields «داده در دسترس نیست» (not a number); demo + candidate map shows numbers and `dataLabel=demo`. SPA not required.

### Tests for User Story 1

- [X] T023 [P] [US1] Replace the always-stub assertions in `backend/Zharfa.Api.Tests/Security/VendorFinancialsTests.cs` that freeze `not-available-until-phase-6` **for production candidate**: given `DataModeOptions("production")` and a map with `balanceFormula.status=candidate`, availability for `vendorBalance` is `formula-unconfirmed` (delegate to `VendorFinancialAvailability`; keep 403 coverage in matrix tests, not here). Demo candidate is **not** forced unavailable. Pure.

### Implementation for User Story 1

- [X] T024 [P] [US1] Add **§7.8 Phase 6 payables** to `specs/002-mabna-accounting-connector/onsite-checklist.md` after §7.7: vendor balance / payable cheque / payment source tables; key, date, amount columns; cheque type/state used by a formula; accounting-confirmed balance formula; allocations; partial-pay rule. Catalog metadata and owner-approved aggregates only. Do not commit live rows.
- [X] T025 [P] [US1] Extend `deploy/mabna-sim/{gen_schema.py,seedgen.py,README.md}` with `dbo.SimPayAccDoc`, `dbo.SimPayChq`, `dbo.SimVendorPay` (and optional `dbo.SimPayAlloc`) using `Sim*` names only. Seed at least: one **credit** vendor (negative remaining), one vendor with both purchases and payments in the same Jalali month, vendors with zero payables, fully open and fully cleared cheques. One partial-pay row only if documented as candidate for unit tests — demo Financials must not need an unverified split. Never include real company rows.
- [X] T026 [P] [US1] Add optional entities + `balanceFormula.status=candidate` `mix=openPayablesPlusUnpaidCheques` and `partialPayment.status=unknown` to `deploy/mabna-sim/example/purchase-source-map.example.json`. Add matching object lines to `deploy/mabna-sim/example/purchase-allowlist.example.txt`. All `hostFiltered: false`.
- [X] T027 [US1] Extend `deploy/mabna-sim/dq_bad_records.py` with optional flagged payable/cheque/payment rows so Admin Data quality can list them; Financials still compute as specified (FR-031).

**Checkpoint**: US1 independently testable (quickstart A). Production cannot show a guessed balance.

---

## Phase 4: User Story 2 — Finance sees balances and outstanding payments per vendor (Priority: P1) 🎯 MVP (list)

**Goal**: Finance/Management open Vendors → Financials for a Jalali period and see per-vendor signed balance and outstanding payments, last-sync, search/sort/page, comparison toggle, demo label. Purchasing/Admin still 403 on the existing policy.

**Independent Test**: Quickstart Flow B — Finance `GET /api/vendor-financials/summary?kind=month&month=<seeded>`; 200 `available: true`; signed credit; zero vendor `"0.0000"`; `compare=1` previous Jalali unit; `#/financials` visible only with `modules.vendorFinancials`.

### Tests for User Story 2

- [X] T028 [P] [US2] Extend `backend/Zharfa.Api.Tests/VendorFinancials/VendorFinancialTotalsTests.cs` (or a DTO serializer test in `backend/Zharfa.Api.Tests/VendorFinancials/VendorFinancialDtoTests.cs`): credit serializes with a leading `-`; zero activity is `"0.0000"` not null; unavailable figure is JSON `null` plus `unavailable[]` reason; labels/keys never contain سود; `currencyLabel` is `ریال`. Pure.
- [X] T029 [P] [US2] Create `backend/Zharfa.Api.Tests/VendorFinancials/VendorFinancialPeriodTests.cs`: as-of = last Jalali day of a month preset (leap Esfand last day); custom range as-of = `to`; comparison uses Phase 2 previous Jalali unit / equal-day custom; `no-previous-data` when previous base missing while current zeros stay zeros. Pure.

### Implementation for User Story 2

- [X] T030 [US2] Create `backend/Zharfa.Api/VendorFinancials/VendorFinancialsService.cs`: list envelope per [contracts/api.md](contracts/api.md) §1 (`kind`/`month`/`quarter`/`year`/`from`/`to`, `q`, `page`, `pageSize`, `sort` `name|code|vendorBalance|outstandingPayments`, `dir`, `compare`). Server-side filter/sort/page via `VendorFinancialSql`. Vendors come from `mabna.vendor` (purchasing master): a vendor with purchases but no payables **appears** with `"0.0000"` when the formula is usable. Last successful sync time on every payload. Demo notice when `dataLabel=demo`. Data-quality notice when flagged rows in scope. Parameterized SQL only.
- [X] T031 [US2] Fill `backend/Zharfa.Api/Controllers/VendorFinancialsController.cs`: keep `[Authorize(Policy = Permissions.VendorFinancials)]` and routes `GET summary` / `GET vendors/{vendorKey}` (matrix keys `VendorFinancialsController.Summary` / `Vendor` unchanged). Replace the Phase 3 stub body with service results (`available: true`, per-figure `unavailable[]`). `[AuditReportOpen("vendor-financials")]` on summary (paging/sorting alone does not add a second entry). Print twin `GET summary/print` existing print envelope, cap 1,000, `report.printed`. Unknown query sort → 400. Inject the service; keep `DataModeOptions` for `dataLabel`.
- [X] T032 [P] [US2] Add Financials list client types and `getVendorFinancialsSummary` in `frontend/src/services/api.ts` matching §1 (decimal-string amounts, `unavailable`, `lastSuccessfulSync`, `compare` fields).
- [X] T033 [P] [US2] Create `frontend/src/pages/VendorFinancials.tsx`: Persian RTL table (name, code, مانده تأمین‌کننده, پرداخت‌های معوق, ریال), `FilterBar` / `Pager` / `useReportQuery`, ی/ک search, period presets + custom, comparison toggle, last-sync, demo banner, data-quality notice. Keyboard-usable, visible focus, AA contrast.
- [X] T034 [US2] Update `frontend/src/App.tsx`: view `financials` at `#/financials`, tab «اطلاعات مالی تأمین‌کنندگان», `isVisible: modules.vendorFinancials`. Direct hash without permission → `NoAccess` (API still 403). Do not show the tab for Admin/Purchasing.

**Checkpoint**: US2 independently testable (quickstart B). MVP list.

---

## Phase 5: User Story 3 — Payment history and purchases vs payments for one vendor (Priority: P1)

**Goal**: Opening a Financials row shows payment history (period), outstanding instruments (as-of period end), and purchases vs payments (Phase 1 net beside payments). Comparison on PvP only; history/outstanding refuse `compare=1`.

**Independent Test**: Quickstart Flow C — unpaged history sums to `purchasesVsPayments.payments`; outstanding unpaid sums to `outstandingPayments`; net purchases equal `GET /api/purchasing/vendor-profile` for the same vendor/period; difference key `purchasesMinusPayments` only; `compare=1` on history/outstanding → 400 `compare-not-allowed`.

### Tests for User Story 3

- [X] T035 [P] [US3] Create `backend/Zharfa.Api.Tests/VendorFinancials/VendorFinancialLabelTests.cs`: JSON property names and any Persian header helpers for PvP contain `purchasesMinusPayments` / «خرید منهای پرداخت» and do **not** contain سود, حاشیه, وجه نقد, or reuse «خرید» as a payment column. Partial-pay split: paid portion only in history, remainder only in outstanding. Pure.

### Implementation for User Story 3

- [X] T036 [US3] Extend `backend/Zharfa.Api/VendorFinancials/VendorFinancialsService.cs` + `VendorFinancialSql.cs`: detail header [contracts/api.md](contracts/api.md) §2 (`vendorBalance`, `outstandingPayments`, `purchasesVsPayments.netPurchases` via existing purchase net, `payments`, optional `purchasesMinusPayments` omitted when either side unavailable, previous-period fields when `compare` on PvP). History §3: rows `{ paidAtJalali, reference, instrumentType, instrumentTypeLabel, amount }`; sort `paidAt|amount|reference` default `paidAt` desc. Outstanding §4: as-of period end; rows `{ dateJalali, reference, instrumentType, instrumentTypeLabel, unpaidAmount }`; sort `date|amount|reference`. Unknown vendor → 404 `{ "message": "تأمین‌کننده یافت نشد." }`. Truthy `compare` on history/outstanding → 400 `{ "message": "مقایسه دوره برای تاریخچه پرداخت در دسترس نیست.", "code": "compare-not-allowed" }` (outstanding message may name outstanding list; same `code`). Changing period updates as-of, history window, and PvP together.
- [X] T037 [US3] Add `GET vendors/{vendorKey}/history` and `GET vendors/{vendorKey}/outstanding` on `backend/Zharfa.Api/Controllers/VendorFinancialsController.cs` with `[AuditReportOpen("vendor-financials-detail")]` on the header, `vendor-financials-history` / `vendor-financials-outstanding` on the lists. Keep the same controller policy.
- [X] T038 [P] [US3] Add detail/history/outstanding client helpers in `frontend/src/services/api.ts`.
- [X] T039 [US3] Create `frontend/src/pages/VendorFinancialDetail.tsx` (or in-page detail from `VendorFinancials.tsx`): paged history, paged outstanding, PvP as separate labeled figures + optional «خرید منهای پرداخت»; comparison toggle **only** on PvP; period control updates all blocks; last-sync; demo/DQ notices. Link from a Financials row. Do not duplicate purchasing document tables.

**Checkpoint**: US2 and US3 work independently (quickstart C).

---

## Phase 6: User Story 4 — Vendor balance totals match the source (Priority: P1)

**Goal**: `reconcile vendor-balance` compares `mabna.fn_vendor_balance` to a guarded source re-read using the same map mix. Purchasing reconcile unchanged. Mismatch logs both totals + difference and blocks sign-off.

**Independent Test**: Quickstart Flow E — simulator pass exit 0 equal signed totals; fixture mismatch exit 1 `signOffBlocked: true`; `reconcile purchasing` still parses.

### Tests for User Story 4

- [X] T040 [P] [US4] Extend `backend/Zharfa.Api.Tests/Purchasing/ReconciliationComparatorTests.cs` (or add `backend/Zharfa.Api.Tests/VendorFinancials/VendorBalanceReconciliationTests.cs`): metric id `vendor_balance`; equal signed totals pass (negative credit included); any difference mismatch with `system - source`; unavailable formula `outcome=unavailable` `reason=formula-unconfirmed` does **not** fail the run (exit 0) and is **not** a Phase 6 sign-off pass. Command line: `reconcile vendor-balance --month 1405-08 [--vendor key] [--show-amounts]`; `reconcile purchasing` still works. Pure.

### Implementation for User Story 4

- [X] T041 [US4] Create `backend/Zharfa.Api/Purchasing/VendorBalanceReconciliation.cs`: compare SQL function totals to `VendorFinancialTotals` on a guarded Mabna read (same map, SELECT-only allowlist). Write `reconciliation_run` with `metric=vendor_balance`, `filter_kind` `none|vendor`, `discount_basis='-'`. Mismatch → `audit_log` `reconciliation.mismatch` (period, filter, metric; **no amounts in production**). JSON line per [contracts/cli-and-config.md](contracts/cli-and-config.md). Production omits `system`/`source`/`difference` unless `--show-amounts`.
- [X] T042 [US4] Extend `backend/Zharfa.Api/Purchasing/ReconcileCommandLine.cs` and `backend/Zharfa.Api/Purchasing/ReconcileCli.cs` to accept `reconcile vendor-balance` without breaking `reconcile purchasing`. Docker: `docker compose -f deploy/docker-compose.yml run --rm api reconcile vendor-balance --month <month>`. Register the new service in `backend/Zharfa.Api/Program.cs`.
- [X] T043 [P] [US4] Document the simulator command and on-site “commit only pass/fail” rule in `specs/008-vendor-financials/source-verification.md` (no real amounts).

**Checkpoint**: US4 independently testable (quickstart E).

---

## Phase 7: User Story 5 — Only Management and Finance see vendor financials, on every surface (Priority: P1)

**Goal**: Purchasing and Admin get 403 / no digits on Financials, profile-balance URL, financial export URLs, financial saved-report reopen, and AI. Navigation hide is not enforcement. Role change: Management-saved Financials refused after user becomes Purchasing.

**Independent Test**: Quickstart Flow F — Purchasing/Admin sweep; Management/Finance 200 on list/detail; saved-report reopen after role change names «اطلاعات مالی تأمین‌کنندگان».

### Tests for User Story 5

- [X] T044 [P] [US5] Update `backend/Zharfa.Api.Tests/Security/PermissionMatrixTests.cs`: add `VendorFinancialsController.History`, `Outstanding`, `SummaryExport`, `HistoryExport`, `OutstandingExport`, `PurchasesVsPaymentsExport` → `{vendor.financials}` and export twins also `{export}` (dual-check same as purchasing export). Purchasing must not appear on any action whose policy set contains `vendor.financials`. Admin must not pass `vendor.financials`. Keep existing Summary/Vendor keys.
- [X] T045 [P] [US5] Confirm `backend/Zharfa.Api.Tests/Purchasing/Reports/NoVendorFinancialsTests.cs` and `backend/Zharfa.Api.Tests/Ai/NoVendorFinancialsInToolsTests.cs` still forbid financial tokens on `Zharfa.Api.Purchasing.Reports` and `PurchasingAnalystTools` / Phase 5 purchasing DTO **type names**. Do not weaken the scanners. Add a comment that financial types live in `Zharfa.Api.VendorFinancials` / `VendorFinancialAnalystTools`.
- [X] T046 [US5] Update `backend/Zharfa.Api.Tests/Ai/PurchasingQuestionRouterTests.cs`: Purchasing (and any caller without `vendor.financials`) mapping for مانده / بدهی / پرداخت / چک / اسناد پرداختنی stays `scope=vendor-financials` with **no tool** and no digits. Do not edit `specs/006-*`. Permitted-role tool mapping waits for US7.

### Implementation for User Story 5

- [X] T047 [US5] Extend `backend/Zharfa.Api/SavedReports/SavedReportService.cs` and `backend/Zharfa.Api/Controllers/SavedReportsController.cs`: financial kinds set `required_permission=vendor.financials` **on the server** (never from the client). Reopen: if current role lacks `vendor.financials`, 403 `{ "message": "برای مشاهده این گزارش به دسترسی «اطلاعات مالی تأمین‌کنندگان» نیاز است.", "code": "forbidden" }`, **no query**, `access.denied`. Generated visual from a financial tool requires **both** `vendor.financials` and `ai.query`. Unsigned → 401.
- [X] T048 [US5] Ensure export actions on `backend/Zharfa.Api/Controllers/VendorFinancialsController.cs` refuse missing `export` **or** `vendor.financials` with 403, **empty body** (not xlsx), `access.denied` — even before US7 fills the workbook (stub 403 is acceptable until T056; do not leak JSON rows).
- [X] T049 [P] [US5] On `frontend/src/pages/SavedReports.tsx` and `frontend/src/App.tsx`, do not offer Financials navigation or financial saved-open when `modules.vendorFinancials` is false; a direct API 403 still shows the server Persian reason and no figures.

**Checkpoint**: Role sweep independently testable (quickstart F) even before export workbooks and AI tools exist.

---

## Phase 8: User Story 6 — Vendor profile shows balance when permitted (Priority: P2)

**Goal**: F5 AC3 — Management/Finance see signed مانده on the vendor profile for the selected period, equal to the Financials list cell. Purchasing profile unchanged.

**Independent Test**: Quickstart Flow D — Finance profile GET financials URL equals list `vendorBalance`; Purchasing profile JSON has no financial properties; Purchasing financial GET → 403.

- [X] T050 [P] [US6] Extend `frontend/src/pages/VendorProfile.tsx`: when `canSeeFinancials` / `modules.vendorFinancials`, fetch `GET /api/vendor-financials/vendors/{key}` for the **same** period and render a مانده block (signed, «مانده تأمین‌کننده», ریال, last-sync, unavailable message). Link to Financials detail without duplicating purchasing tables. Purchasing never calls the endpoint.
- [X] T051 [US6] Do **not** modify `backend/Zharfa.Api/Purchasing/Reports/ReportDtos.cs` or vendor-profile purchasing handlers. If a helper is needed, keep it in `frontend/src/services/api.ts` only (`getVendorFinancialDetail`). Unverified production balance → «داده در دسترس نیست» while purchasing figures still follow Phase 2.

**Checkpoint**: F5 AC3 independently testable (quickstart D).

---

## Phase 9: User Story 7 — Export and AI use the same financial figures (Priority: P2)

**Goal**: Finance exports Financials/history/outstanding/PvP under Phase 5 Excel rules. Management/Finance AI tools cite the same metrics. Purchasing still no digits. Demo files and answers labeled demonstration data.

**Independent Test**: Quickstart Flow G — xlsx Persian RTL meta + sampled مانده matches list; Purchasing export URL 403 not xlsx; permitted مانده question cited and equal to Financials; ranking → table/chart; Purchasing same question no digits.

### Tests for User Story 7

- [X] T052 [P] [US7] Extend `backend/Zharfa.Api.Tests/Purchasing/Reports/ExcelExportServiceTests.cs` or add `backend/Zharfa.Api.Tests/VendorFinancials/VendorFinancialExcelTests.cs`: Financials headers نام، کد، مانده تأمین‌کننده، پرداخت‌های معوق; history تاریخ پرداخت، مرجع، نوع سند، مبلغ; outstanding تاریخ، مرجع، نوع سند، مبلغ معوق; PvP خالص خرید، پرداخت‌ها، خرید منهای پرداخت. No header contains سود / حاشیه / وجه نقد دریافت‌شده. Negative مانده goes through existing `EscapeFormula`. RTL + meta rows 1–8 reused. Pure in-memory.
- [X] T053 [P] [US7] Create `backend/Zharfa.Api.Tests/Ai/VendorFinancialAnalystToolsTests.cs`: allowlist names `vendor_financials_list`, `vendor_balance`, `vendor_payments`, `vendor_outstanding`, `vendor_purchases_vs_payments`; 100-row cap → metric unavailable (no truncated guess); facts cite period, filters, metric id; cloud payload fixture contains names + aggregates only (no history rows / cheque numbers). Pure.
- [X] T054 [US7] Update `backend/Zharfa.Api.Tests/Ai/PurchasingQuestionRouterTests.cs`: Management/Finance + financial intent → mapped financial tool; Purchasing rows stay no-tool. Extend `backend/Zharfa.Api.Tests/Ai/VisualIntentTests.cs`: `vendor_financials_list` is visual-capable for ranking/comparison tokens; chart only 2–20 vendors.

### Implementation for User Story 7

- [X] T055 [US7] Wire export twins on `backend/Zharfa.Api/Controllers/VendorFinancialsController.cs` using existing `backend/Zharfa.Api/Purchasing/Reports/ExcelExportService.cs`: `GET summary/export`, `GET vendors/{key}/history/export`, `GET …/outstanding/export`, `GET …/purchases-vs-payments/export`. Dual permission. Audit `report.exported` without amounts. 10,000-row cap. Demo label. Empty 403 body on failure.
- [X] T056 [US7] Create `backend/Zharfa.Api/Ai/VendorFinancialAnalystTools.cs` (separate type so purchasing scanners stay green). Same 10 s / 100 rows / 3 calls / 30 s; read synced copy; no SQL, no Mabna, no writes. Tools per [contracts/ai-tools.md](contracts/ai-tools.md). Labels from `docs/metrics/vendor-financials.md`.
- [X] T057 [US7] Extend `backend/Zharfa.Api/Ai/PurchasingQuestionRouter.cs`: if caller **has** `vendor.financials`, map financial keywords to a tool; else no tool / empty facts / «داده در دسترس نیست». Scope id `vendor-financials` unchanged.
- [X] T058 [US7] Extend `backend/Zharfa.Api/Ai/VisualIntent.cs` so `vendor_financials_list` is visual-capable; captions use R7 labels. Dispatch from `backend/Zharfa.Api/Controllers/AiController.cs` without registering methods on `PurchasingAnalystTools`.
- [X] T059 [P] [US7] Fill `specs/008-vendor-financials/fixtures/financial-questions.json` with Management/Finance Persian questions (balance, payments, ranking). Scorer note: cited amounts match Financials GET, or unavailable phrase. Include a Purchasing control row expecting no digits. Not a Phase 6 20/20 exit.
- [X] T060 [US7] Add Save/Export on `frontend/src/pages/VendorFinancials.tsx` and detail (kinds from data-model §4; server assigns `vendor.financials`). Assistant already renders `visual`; ensure financial ranking table/chart uses financial metric labels. Demo notice on export download and AI.

**Checkpoint**: US7 independently testable (quickstart G). SC-006 same amount on list, profile, export sample, AI cite.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Docs, print, last-sync/quality edges, README. Depends on US1–US7 as implemented.

- [X] T061 [P] Update `README.md` and `deploy/README.md` with Financials tab, `reconcile vendor-balance`, and demo vs production availability (no new secrets, no live table names).
- [X] T062 [P] Update `deploy/mabna-sim/README.md` for Sim* payable tables, example map keys, and candidate-only demo.
- [X] T063 [P] Add print twins already specified (`summary/print` and browser-print 1,000-row cap) on `frontend/src/pages/VendorFinancials.tsx`; no custom PDF pipeline.
- [X] T064 Extend last-sync / no-sync / failed-sync behavior on `backend/Zharfa.Api/VendorFinancials/VendorFinancialsService.cs` so no succeeded sync → figures unavailable with last-sync stating none; after a failed run the last **successful** sync time remains (quickstart H). Do not invent a second DQ page.
- [X] T065 Keep `backend/Zharfa.Api.Tests/Purchasing/Reports/NoVendorFinancialsTests.cs` and `backend/Zharfa.Api.Tests/Ai/NoVendorFinancialsInToolsTests.cs` unmodified in spirit (scanners still pass). If a new purchasing type name would trip them, rename/move it into `VendorFinancials`.
- [X] T066 [P] If `.ainative/project.yaml` `validation_commands` need a note that Phase 6 tests live in the existing `dotnet test backend/Zharfa.Api.Tests` project, update the comment only — do **not** add a new project or GPU/Ollama commands.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup — **BLOCKS** all user stories
- **US1 (Phase 3)**: After Foundational — source gate; simulator/map needed before meaningful list numbers
- **US2 (Phase 4)**: After Foundational; demo numbers need US1 sim/map
- **US3 (Phase 5)**: After US2 service/controller exist (same `VendorFinancialsService`)
- **US4 (Phase 6)**: After Foundational totals + US1 map; can parallel US2/US3 if staffed
- **US5 (Phase 7)**: After US2 routes exist; export/AI 403 can precede US7 workbooks/tools
- **US6 (Phase 8)**: After US3 detail GET exists
- **US7 (Phase 9)**: After US2–US5 (same figures, permission gates)
- **Polish (Phase 10)**: After desired stories

### User Story Dependencies

- **US1 (P1)**: After Foundational — no dependency on list UI
- **US2 (P1)**: After Foundational + US1 sim for demo figures; production unavailable path is US1
- **US3 (P1)**: Integrates with US2 list navigation; independently testable via detail/history/outstanding GETs
- **US4 (P1)**: After Foundational formulas + US1 sources; does not replace purchase reconcile
- **US5 (P1)**: After US2 routes; saved-report reopen independent of Excel bytes
- **US6 (P2)**: After US3 header GET; purchasing profile must stay unchanged
- **US7 (P2)**: After US2–US5; tools/export consume the same service as the pages

### Within Each User Story

- Tests (where listed) MUST be written and FAIL before implementation
- Models/availability/SQL before services
- Services before endpoints
- Endpoints before SPA
- Story complete before moving to the next priority unless parallel-staffed on different files

### Parallel Opportunities

- Phase 1: T002, T003, T004 in parallel after T001 starts
- Phase 2: T005–T007 in parallel; T012/T014/T015/T016/T019 in parallel after map types exist
- US1: T024, T025, T026 in parallel
- US2: T028/T029; T032/T033 in parallel after T031
- US3: T035; T038 in parallel with T036
- US4: T040 in parallel with T041
- US5: T044/T045 in parallel
- US7: T052/T053 in parallel; T056 after T053

---

## Parallel Example: User Story 2

```bash
# Tests together:
Task: "VendorFinancialDtoTests signed credit / zero / unavailable in backend/Zharfa.Api.Tests/VendorFinancials/"
Task: "VendorFinancialPeriodTests as-of and comparison in backend/Zharfa.Api.Tests/VendorFinancials/VendorFinancialPeriodTests.cs"

# SPA types + page together after controller:
Task: "getVendorFinancialsSummary in frontend/src/services/api.ts"
Task: "VendorFinancials.tsx list page in frontend/src/pages/VendorFinancials.tsx"
```

---

## Implementation Strategy

### MVP First (US1 source gate + US2 list)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL)
3. Complete Phase 3: US1 (production cannot lie)
4. Complete Phase 4: US2 (Finance can open Financials)
5. **STOP and VALIDATE**: quickstart A + B
6. Then US3 (Journey 2 complete on screen) → US4 reconcile → US5 role sweep → US6 profile → US7 export/AI

### Incremental Delivery

1. Setup + Foundational → schema and availability ready
2. US1 → production unavailable unless confirmed
3. US2 → Financials list (MVP demo)
4. US3 → history / outstanding / PvP
5. US4 → vendor-balance reconcile (sign-off gate)
6. US5 → 403 matrix on every new surface
7. US6 → profile balance (F5 AC3)
8. US7 → Excel + AI tools
9. Each story must not weaken Purchasing isolation or Phase 1–5 specs

### Parallel Team Strategy

1. Team completes Setup + Foundational together
2. Then: A = US1 simulator/checklist, B = US2/US3 API+SPA, C = US4 CLI
3. US5 after routes exist; US6/US7 after detail GET and permissions

---

## Notes

- [P] tasks = different files, no unfinished dependency
- [Story] maps to spec.md US1–US7
- Suggested MVP: US1 + US2 (then US3 to finish Journey 2 on screen)
- Workers do not run Compose/quickstart; tester does
- Avoid: putting financial fields on purchasing DTOs, new packages, rewriting the Phase 4 20-question set, live Mabna per request

---

## Phase 11: Convergence

- [X] T067 Convert payment history and outstanding instrument dates using `JalaliDate.FromDate` in `backend/Zharfa.Api/VendorFinancials/VendorFinancialsService.cs` so `paidAtJalali` and `dateJalali` (and Excel twins) format genuine Jalali dates instead of Gregorian `DateOnly` strings [critic: C1] per FR-017 (contradicts)
- [X] T068 Update `backend/Zharfa.Api/Ai/VendorFinancialAnalystTools.cs` to state «داده در دسترس نیست» (or JSON null) in AI facts and cloud payloads instead of substituting zero ("۰" / "0.0000") when financial list amounts or purchases vs payments figures are unavailable [critic: C2] per FR-019 (contradicts)

## Phase 12: Convergence

- [X] T069 Preserve null or unavailable representation for null vendorBalance and outstandingPayments in `backend/Zharfa.Api/Ai/VisualBuilder.cs` instead of coercing to "0.0000" or chart series "0" [critic: C3] per FR-019 (contradicts)
- [X] T070 Respect `route.PeriodRule` for vendor-financial questions in `backend/Zharfa.Api/Controllers/AiController.cs` so non-default periods (ماه قبل / فصل) bind correctly instead of always falling back to `GetDefaultPeriod()` [critic: C4] per FR-028 (contradicts)
- [X] T071 Resolve target vendor from question text via `FindVendorInTextAsync` and pass vendor arguments to `financialTools.DispatchAsync` in `backend/Zharfa.Api/Controllers/AiController.cs` (or inside `VendorFinancialAnalystTools.cs`) so single-vendor financial queries resolve their vendor [critic: C5] per US7 (missing)

## Phase 13: Convergence

- [X] T072 Gate balance and outstanding flags false in `SummaryTotalsSql` / `CountSummaryAsync` in `backend/Zharfa.Api/VendorFinancials/VendorFinancialSql.cs` so directory count queries do not call `mabna.fn_vendor_balance` or `fn_outstanding_payments` [critic: C6] per FR-011 (contradicts)

## Phase 14: Convergence

- [X] T073 Fix frontend build compilation errors in `frontend/src/services/api.ts`, `frontend/src/pages/VendorFinancials.tsx`, and `frontend/src/pages/VendorFinancialDetail.tsx` by replacing undeclared `ReportSyncTime` and `FilterEcho` with `LastSuccessfulSync` and `ReportFilterEcho`, aligning `ReportPeriod` with `trimmedTo`, fixing `PagerProps` (`onPage`, `onPageSize`), `SaveReportDialogProps` (`open`), and `PrintReport` props (`print`, `columns`, `rowKey`), and removing unused variables so `npm run build` / `tsc -b` succeeds [validator: unit-frontend-build] per FR-014 (partial)

## Phase 15: Convergence

- [X] T074 Pass raw API decimal strings (or null when unavailable) into KpiCard for vendorBalance and outstandingPayments in `frontend/src/pages/VendorFinancialDetail.tsx` instead of pre-formatted faRialFromDecimal strings so KPI cards format correctly [tester: C-spa] per FR-016 (contradicts)



