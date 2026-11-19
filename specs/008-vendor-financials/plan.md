# Implementation Plan: Vendor Financials (Release 1, Phase 6)

**Branch**: `zharfa-prd-phase-6` | **Date**: 2026-11-19 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/008-vendor-financials/spec.md` · Phase ref `zharfa-prd.phases.md#6` · Governing PRD `zharfa-prd.md` v3.0 (F17 all, F5 AC3) · Rules `AGENTS.md` · Foundation Phases 1–5 (`specs/003-real-purchase-data` … `specs/007-ai-reports-export`) and spec 002 connector/simulator/checklist

Skip mode: already applied in the spec; do not re-ask. Do not implement in this plan pass. Do not reopen Phase 1–5 specs.

## Summary

Finance and Management see **signed vendor balance**, **outstanding payments**, **payment history**, and **purchases vs payments** for a Jalali period, from synced Mabna (simulator candidate mix off-site). Purchasing and Admin still get **403** and no figures. Production shows «داده در دسترس نیست» until accounting confirms the balance formula. F5 AC3 adds the **same** balance on the vendor profile via a parallel financial GET — purchasing DTOs stay clean.

Technical approach (details in [research.md](research.md)):

- **Sources**: optional v1 source-map entities + `Sim*` simulator tables; same sync job; reporting SQL functions; `VendorFinancialAvailability` (R1, R6).
- **API**: fill `VendorFinancialsController` (keep `/summary` and `/vendors/{key}`); add history/outstanding/export twins (R5). Namespace `Zharfa.Api.VendorFinancials` so `NoVendorFinancialsTests` remain.
- **Formulas**: `docs/metrics/vendor-financials.md`; purchases cited from Phase 1 net only (R7).
- **Authz**: existing `vendor.financials` policy; `modules.vendorFinancials`; saved-report kinds + CHECK widen; AI `VendorFinancialAnalystTools` only when both permissions (R8, R9, R10).
- **Reconcile**: `reconcile vendor-balance` beside purchasing (R11).
- **SPA**: `#/financials` + profile balance block (R15).
- **Tests**: xunit for formulas, availability, matrix, router (permitted vs Purchasing), signed credit, no-profit labels. HTTP/UI: [quickstart.md](quickstart.md).

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`), TypeScript 7 / React 19, SQL (PostgreSQL 18), T-SQL simulator seed

**Primary Dependencies**: ASP.NET Core (existing permission policies), Npgsql, existing `JalaliPeriod` / report paging / `ExcelExportService` (ClosedXML already in API), Vite 8 + Tailwind 4. **No new NuGet. No new npm packages.**

**Storage**: PostgreSQL `mabna.payable_document`, `mabna.payable_cheque`, `mabna.vendor_payment`, optional `mabna.payment_allocation`; KPI functions; `saved_report` CHECKs widened; `reconciliation_run.metric` += `vendor_balance`. Migration `db/migrations/0011_vendor_financials.sql`. Reports/AI/export read the synced copy only.

**Testing**: xunit (`backend/Zharfa.Api.Tests`), pure logic (no DB or network). HTTP, xlsx, UI, reconcile CLI: [quickstart.md](quickstart.md) on Compose + simulator.

**Target Platform**: Linux containers on the company LAN; browsers and unchanged WebView2 shell.

**Project Type**: Web application (ASP.NET Core API + React SPA).

**Performance Goals**: Financials list and payment-history data view p95 < 2 s on synced data (same as Phase 2). AI financial answers stay within the Phase 4 30 s budget.

**Constraints**: Server-side authz on every new endpoint; AI only via allowlisted financial tools for Management/Finance; Purchasing never receives financial digits; Mabna read-only; no `HOST_ID()` views; purchases ≠ payments ≠ balance; no price-difference-as-profit; real/demo never mix; Persian RTL, Jalali, ریال; audit append-only without rows/secrets; production candidate formula unused; Phase 1–5 specs not edited.

**Scale/Scope**: ~20 users; Financials list of vendors; paged instruments; 1 migration; 1 SPA tab; reserved controller filled; optional sync tables. One API replica.

**Unknowns**: None open for planning. Live Mabna mix is **on-site**, not a code unknown: production stays unavailable until confirmed (research R1, R18).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md` is still the unfilled template, so the binding gates are `AGENTS.md` and `zharfa-prd.md` (§8–§9), as in Phases 1–5.

| Gate (AGENTS.md / PRD) | Pre-design | Post-design | How the design satisfies it |
|---|---|---|---|
| Authorization server-side; vendor financials = Management + Finance only | Pass | Pass | Existing `vendor.financials` policy on the controller; export dual-check; saved-report reopen; AI tools gated (R8) |
| Purchasing (and Admin) cannot see financials on any surface | Pass | Pass | 403 matrix; no financial fields on purchasing DTOs; Purchasing AI no tools/digits (R5, R9, R14) |
| AI only through approved read-only tools; cite or «data unavailable»; facts ≠ interpretation | Pass | Pass | New allowlist class; cloud aggregates only; 20-question set not rewritten (R9) |
| Local default / cloud aggregates only | Pass | Pass | No payment-line dumps in cloud JSON ([ai-tools.md](contracts/ai-tools.md)) |
| Secrets only in env; never bundle, repo, logs | Pass | Pass | No new secrets; audit/xlsx forbid keys and result rows |
| Mabna read-only; reports never query live per request; no `HOST_ID()` views | Pass | Pass | Sync copy + allowlist + `hostFiltered` false (R1, R6) |
| Real and synthetic never mix; demo labelled | Pass | Pass | `dataLabel` / demo notice on pages, export, AI (R10, R15) |
| Financial formulas stay separate; never present purchases as payments or difference as profit | Pass | Pass | Three definitions; PvP labeled «خرید منهای پرداخت» (R7) |
| Unverified source → data unavailable, never an estimate | Pass | Pass | `VendorFinancialAvailability`; production candidate unused (R1, R3) |
| Audit append-only, no secrets or result rows | Pass | Pass | `AuditReportOpen` + export/access-denied (R8) |
| Persian-first RTL, Jalali, ریال | Pass | Pass | Same report/Excel stack (R10, R15) |
| Prefer existing abstractions; no unjustified dependencies | Pass | Pass | Reuse period, paging, ClosedXML, sync job, reconcile host. **No new packages** |
| Do not reopen Phases 1–5 specs; fill reserved hole | Pass | Pass | Additive contracts under `specs/008`; Phase 3 routes kept (R5, R17) |
| Update docs when behavior changes | Pass | Pass | Metrics file, checklist §7.8, README, sim examples (R17) |

No unjustified violations. Complexity Tracking is empty (no new host, no new package).

### Post-design re-check

Contracts keep `/api/vendor-financials/summary` and `/vendors/{key}` so Phase 3 matrix keys stay. History/outstanding are extra GETs under the same policy. Profile balance is a second request, not a purchasing DTO field — constitution tension with `NoVendorFinancialsTests` is **avoided**, not waived. Financial AI tools are in-scope F17 AC5; purchasing tools stay non-financial. `reconciliation_run` CHECK widen is additive. Saved-report permission CHECK adding `vendor.financials` is the Phase 5 foreshadowed extension.

## Project Structure

### Documentation (this feature)

```text
specs/008-vendor-financials/
├── spec.md
├── plan.md                 # this file
├── research.md             # Phase 0: R1–R18
├── data-model.md           # Phase 1
├── quickstart.md           # Phase 1: flows A–H ↔ exit criteria
├── contracts/
│   ├── api.md
│   ├── source-map.md
│   ├── cli-and-config.md
│   ├── ai-tools.md
│   └── xlsx.md
├── checklists/requirements.md
├── source-verification.md  # created at implement (logical statuses only)
├── fixtures/financial-questions.json  # implement; not the Phase 4 20-set
└── tasks.md                # /speckit.tasks — not created here
```

### Source Code (repository root) — files to add (A) or modify (M)

```text
db/migrations/0011_vendor_financials.sql                    A

docs/metrics/vendor-financials.md                           A
specs/002-mabna-accounting-connector/onsite-checklist.md    M  §7.8
deploy/mabna-sim/example/purchase-source-map.example.json   M
deploy/mabna-sim/example/purchase-allowlist.example.txt     M
deploy/mabna-sim/{seedgen.py,gen_schema.py,dq_bad_records.py,README.md}  M

backend/Zharfa.Api/
├── VendorFinancials/
│   ├── VendorFinancialsService.cs                          A
│   ├── VendorFinancialAvailability.cs                      A
│   ├── VendorFinancialDtos.cs                              A
│   ├── VendorFinancialSql.cs                               A
│   └── VendorFinancialTotals.cs                            A  source-side recompute
├── Mabna/Sync/
│   ├── SourceMap.cs                                        M  optional entities
│   ├── VendorFinancialMapping.cs                           A
│   ├── VendorFinancialRowMapper.cs                         A
│   ├── SyncRunner.cs                                       M  after purchase lines
│   └── LogicalTables.cs (SyncModels.cs)                    M  optional names
├── Controllers/VendorFinancialsController.cs               M  real figures + children + export
├── Purchasing/ReconcileCli.cs + ReconcileCommandLine       M  vendor-balance
├── Purchasing/VendorBalanceReconciliation.cs               A
├── Ai/VendorFinancialAnalystTools.cs                       A
├── Ai/PurchasingQuestionRouter.cs                          M  tool map when permitted
├── Ai/VisualIntent.cs                                      M  list tool visual-capable
├── Controllers/AiController.cs                             M  dispatch financial tools
├── SavedReports/SavedReportService.cs                      M  kinds + permission
├── Platform/SystemModules.cs                               M  VendorFinancials
├── Audit/ReportKeys.cs                                     M
├── DataQuality/DataQualityCatalog.cs                       M  affected reports
├── Program.cs                                              M  DI
└── Data/ZharfaDbContext.cs                                 M  if DbSets used

backend/Zharfa.Api.Tests/
├── VendorFinancials/*                                      A  formulas, availability, signed credit, labels
├── Security/VendorFinancialsTests.cs                       M  replace always-unavailable
├── Security/PermissionMatrixTests.cs                       M  new actions
├── Ai/PurchasingQuestionRouterTests.cs                     M  permitted tool vs Purchasing no-tool
├── Purchasing/Reports/NoVendorFinancialsTests.cs           keep
└── Ai/NoVendorFinancialsInToolsTests.cs                    keep

frontend/src/
├── App.tsx                                                 M  #/financials tab
├── pages/VendorFinancials.tsx                              A
├── pages/VendorFinancialDetail.tsx                         A  or in-page detail
├── pages/VendorProfile.tsx                                 M  balance block if canSeeFinancials
├── services/api.ts                                         M
└── types for SystemModules                                 M
```

**Structure Decision**: Continue the existing web-app layout (API + SPA). Vendor financials sit in their own API folder, not inside `Purchasing.Reports`, and not a fourth host.

## Complexity Tracking

> No constitution violations that need justification.
