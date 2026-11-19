# Quickstart: Vendor Financials (Release 1, Phase 6)

Validation only. Implementation lives in `tasks.md`. Contracts: [api.md](contracts/api.md), [ai-tools.md](contracts/ai-tools.md), [xlsx.md](contracts/xlsx.md), [cli-and-config.md](contracts/cli-and-config.md), [source-map.md](contracts/source-map.md). Model: [data-model.md](data-model.md). Research: [research.md](research.md).

Does **not** re-run Phase 4 20/20 or Phase 5 export of purchasing reports as a Phase 6 exit. Does **not** reopen Phase 1–5 specs.

## Prerequisites

- Docker Compose + Mabna simulator (`deploy/docker-compose.yml`, `deploy/docker-compose.mabna-sim.yml`, purchasing-dev compose as Phase 1–5).
- Demo data mode. Candidate financial entities on the example map; successful **Sync now** that lists payable/cheque/payment table counts.
- Demo users: `management`, `purchasing`, `finance`, `admin`.
- Local answering provider up only for Flow G (AI). Flows A–F, H must work if the provider is then stopped.

`R` = API origin (e.g. `http://localhost:8080`).

## A. Source verification gate (US1, SC-001, exit 1)

1. `specs/002-mabna-accounting-connector/onsite-checklist.md` has §7.8 Phase 6 payables (balance, cheque, payment, types/states, formula, allocations, partial-pay).
2. Simulator example map uses `Sim*` names only; `hostFiltered` false; git status has no live rows.
3. Force production-mode API with `balanceFormula.status=candidate` (unit/config test or a local production flag against sim): Financials balance is «داده در دسترس نیست», not a number.
4. Demo-mode with candidate map: Financials shows numbers and `dataLabel=demo`.

SPA: not required for A.

## B. Financials list (US2, SC-002 list half, exit 2)

1. Sign in as Finance. `GET $R/api/vendor-financials/summary?kind=month&month=<current Jalali month>`.
2. 200, `available: true`, rows have name, code, signed `vendorBalance`, `outstandingPayments`, `lastSuccessfulSync`, `currencyLabel=ریال`. Search ی/ک, sort, page work server-side.
3. A vendor with no payables: both amounts `"0.0000"`, not unavailable.
4. A seeded credit vendor: `vendorBalance` starts with `-`.
5. `compare=1` on a month preset: previous Jalali month columns + delta/percent when previous base ≠ 0.
6. As Purchasing and as Admin: 403, no rows, audit `access.denied`.
7. Demo notice present.

SPA: `#/financials` visible for Finance/Management only; hidden for Purchasing/Admin (direct hash → NoAccess).

## C. Detail: history, outstanding, purchases vs payments (US3, SC-002 rest)

1. From a list vendor that has both purchases and payments, `GET …/vendors/{key}` for the same month.
2. History `GET …/history`: row amounts sum (unpaged) to `purchasesVsPayments.payments`.
3. Outstanding `GET …/outstanding`: unpaid amounts sum to `outstandingPayments`.
4. `purchasesVsPayments.netPurchases` equals `GET /api/purchasing/vendor-profile` net for that vendor and period.
5. Difference present only as `purchasesMinusPayments`; JSON/keys/headers contain no سود.
6. `compare=1` on history or outstanding → 400 `compare-not-allowed`.
7. Change month: balance as-of, outstanding, history, and PvP all follow the new period.

SPA: open a Financials row; period control updates all blocks.

## D. Profile balance (US6, F5 AC3, SC-006)

1. As Finance, open vendor profile (purchasing reports) and `GET /api/vendor-financials/vendors/{key}` with the same period: `vendorBalance` equals the Financials list cell.
2. As Purchasing, profile JSON has **no** balance/payment properties (`NoVendorFinancialsTests`); `GET` financials URL → 403.

SPA: Finance sees a مانده block; Purchasing profile unchanged.

## E. Reconciliation (US4, SC-003, exit 3)

1. After sim sync: `docker compose run --rm api reconcile vendor-balance --month <seeded month> --vendor <key>`.
2. Exit 0, `outcome=pass`, signed totals equal (demo may print amounts).
3. Unit/fixture: force mismatched totals → logged with both totals + difference, exit 1, `signOffBlocked: true`.
4. `reconcile purchasing` still runs and is not replaced.
5. On-site (later): one real month; commit only pass/fail in `source-verification.md`.

## F. Role sweep (US5, SC-005, exit 5)

1. Purchasing: Financials, profile-balance URL, financial export, open a Management-saved financial report, `POST /api/ai/chat` about مانده — **zero** financial figures; 403 or no-digits 200; `access.denied` where 403.
2. Admin: financial GETs 403; chat 403 (no `ai.query`).
3. Management/Finance: list and detail 200 with figures (demo). Finance without export (if ever split) is N/A on seeded roles — both hold export; still assert export twins require both policies in `PermissionMatrixTests`.
4. Management saves Financials; Admin sets that user to Purchasing; reopen → 403 Persian missing «اطلاعات مالی تأمین‌کنندگان», no rows.

## G. Export and AI (US7, SC-006, SC-007)

1. Finance: export Financials list `.xlsx` — Persian headers, RTL, period, filters, export time, last-sync; sampled مانده matches the list; audit `report.exported` without amounts.
2. Purchasing export URL → 403, not an xlsx.
3. Management, provider up: ask one vendor’s مانده — cited period/filters/metric, amount equals Financials, or «داده در دسترس نیست». Ranking question → table/chart using vendor-financial metrics only.
4. Purchasing same question: no digits.
5. Demo label on export and AI.

SPA: Export on `#/financials`; assistant table for a balance ranking.

## H. Quality and last-sync (FR-006, FR-031, SC-004, SC-009)

1. No succeeded sync: financial amounts unavailable; last-sync states none.
2. Fail a second sync: last **successful** sync time still shown; previous financial rows remain.
3. Flagged payable rows in sim: Financials still compute; Phase 3 DQ notice when in scope.
4. Production samples (when used): no `Sim*` / demo banner.

## Mapping to Phase 6 exit criteria

| Exit (`zharfa-prd.phases.md` §6) | Flow |
|---|---|
| Sources verified; formula confirmed or production unavailable | A |
| Financials show balance, outstanding, history, PvP | B, C |
| Vendor balance totals = source query (sim + one real month) | E |
| Unverified source → data unavailable | A3, H1 |
| Purchasing cannot see financials anywhere | F, G4 |
