# Implementation Plan: AI Reports, Saved Reports, and Excel Export (Release 1, Phase 5)

**Branch**: `zharfa-prd-phase-5` | **Date**: 2026-11-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/007-ai-reports-export/spec.md` · Phase ref `zharfa-prd.phases.md#5` · Governing PRD `zharfa-prd.md` v3.0 (F14, F15, F16, F10 export/saved events) · Rules `AGENTS.md` · Foundation Phases 1–4 (`specs/003-real-purchase-data`, `specs/004-vendor-purchasing-reports`, `specs/005-permissions-audit-quality`, `specs/006-local-ai-purchasing`)

Skip mode: already applied in the spec; do not re-ask. Do not implement in this plan pass.

## Summary

Users ask ranking/comparison purchasing questions and get a **server-built** table (and a bar chart when there are 2–20 categories), save any standard or generated report as a **named spec** (not a snapshot), reopen it on current synced data with **current** permissions, and download the on-screen table as **RTL Persian `.xlsx`**. Admin still has no `ai.query` or `export`. Phase 4 cited answers, tools, 20-question gate, and Phase 2 report pages stay; this phase only extends them.

Technical approach (details in [research.md](research.md)):

- **F14 visual**: `VisualIntent` + existing `PurchasingQuestionRouter` / allowlisted tools. Canonical top-10 × six Jalali months uses new `JalaliPeriod.LastNMonths` as a custom range on `top-products`. Optional `visual` on `POST /api/ai/chat` 200. Charts are SPA SVG (R1–R4). No new AI tools; 100-row / 30 s / 503-when-down unchanged (R13).
- **F15 saved reports**: table `saved_report` (migration `0010`), per-user unique `name_key`, cap 50, overwrite 409→confirm (R5–R7). Generated kind requires `ai.query` on save **and** open (permission-removed exit). Open re-runs services; generated open does not call the LLM.
- **F16 export**: ClosedXML (only new NuGet; PRD §9) in `ExcelExportService`. `/export` twins of print + generated/saved export URLs. Second policy check `export`. 10,000-row cap, meta, DQ notice, demo label (R8–R11). Print stays 1,000 / browser.
- **Audit**: `report.exported`, `saved_report.{saved,replaced,opened,deleted}`; 403 still `access.denied` (R10).
- **SPA**: table/chart/Keep on `#/assistant`; Save/Export on report tables; `#/saved` when `modules.savedReports` (R12).
- **Tests**: pure xunit (intent, period, filename, workbook RTL/meta, matrix, catalog). HTTP/UI: [quickstart.md](quickstart.md).

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`), TypeScript 7 / React 19, SQL (PostgreSQL 18)

**Primary Dependencies**: ASP.NET Core (cookie auth, existing permission policies), Npgsql / existing purchasing + AI services, Vite 8 + Tailwind 4, **ClosedXML** (MIT, API project only — R8). **No new npm packages.**

**Storage**: PostgreSQL `saved_report` ([data-model.md](data-model.md)); migration `db/migrations/0010_ai_reports_export.sql`. Reports/AI/export read the synced `mabna.*` copy only. Audit rows in existing `audit_log`.

**Testing**: xunit (`backend/Zharfa.Api.Tests`), pure logic only (no DB or network). HTTP, xlsx bytes, UI: [quickstart.md](quickstart.md) on Compose + simulator.

**Target Platform**: Linux containers on the company LAN; browsers and unchanged WebView2 shell.

**Project Type**: Web application (ASP.NET Core API + React SPA).

**Performance Goals**: Saved-report open p95 < 2 s for the data fetch (same as Phase 2 reports). `.xlsx` for a typical filtered table (hundreds–few thousand rows) < 10 s. AI visual answers stay within the Phase 4 30 s user-visible budget.

**Constraints**: Server-side authz on every new endpoint; AI only via Phase 4 allowlist; ClosedXML justified; secrets in env only; Mabna read-only; purchases ≠ payments ≠ balance; real/demo never mix; Persian RTL, Jalali, ریال; audit append-only without rows/secrets; export permission not held by Admin.

**Scale/Scope**: ~20 users; 50 saved reports per user; export cap 10,000 rows; 1 migration; 1 new NuGet; 1 new SPA tab; chat + report pages extended. One API replica.

**Unknowns**: None open for planning. Skip-mode owner answers: spec Session 2026-11-18 and research R16.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md` is still the unfilled template, so the binding gates are `AGENTS.md` and `zharfa-prd.md` (§8–§9), as in Phases 1–4.

| Gate (AGENTS.md / PRD) | Pre-design | Post-design | How the design satisfies it |
|---|---|---|---|
| AI answers only through approved read-only tools; cite period/filters or «data unavailable»; facts ≠ interpretation | Pass | Pass | No new tools; `visual` copied from allowlisted metric rows (R1, R13); [contracts/api.md](contracts/api.md) §1 |
| Local default / cloud aggregates only; generated table is still aggregates | Pass | Pass | Cloud payload = tool aggregates; xlsx/saved specs never sent to cloud (R13) |
| Secrets only in env; never bundle, repo, logs | Pass | Pass | No new secrets; audit/xlsx forbid keys (R10, [xlsx.md](contracts/xlsx.md)) |
| Authorization server-side; export = Management/Purchasing/Finance; Admin has neither export nor AI-queries | Pass | Pass | Dual check on export twins; generated save/open requires `ai.query` (R6, R9) |
| Purchasing cannot see vendor financials | Pass | Pass | No financial kinds or columns; DTO scan remains |
| Mabna read-only; reports/AI/export read synced copy | Pass | Pass | Services only; no live Mabna (R5, R8) |
| Real and synthetic never mix; demo labelled | Pass | Pass | `dataLabel` on visual, saved list/open, xlsx meta (R11) |
| Financial formulas stay separate | Pass | Pass | Same report columns; no price-difference-as-profit |
| Audit append-only, no secrets or result rows | Pass | Pass | New action codes; amounts not in `detail` (R10) |
| Persian-first RTL, Jalali, ریال | Pass | Pass | RTL sheet, Persian headers/digits, Jalali export date (R4, R8, R10) |
| Prefer existing abstractions; justify dependencies | Pass | Pass | Reuses report/AI/audit/print twin pattern; **ClosedXML** is the PRD-named xlsx library (R8). Complexity Tracking records that one package |
| Phase 2 print unchanged; Phase 4 20-question gate not reopened | Pass | Pass | Print twins untouched; visual additive; scorer still cited-facts (R2) |
| Update docs when behavior changes | Pass | Pass | R15 |

No unjustified violations.

### Post-design re-check

Contracts add `/export` twins and `/api/saved-reports` without changing print or `/api/ai/chat` request shape. Generated open skips the LLM so provider outages do not block F15. ClosedXML stays in the API project; the SPA still has no spreadsheet library. Constitution tension with prior “no new packages” is **in-scope**: PRD §9 already named this library for F16.

## Project Structure

### Documentation (this feature)

```text
specs/007-ai-reports-export/
├── spec.md
├── plan.md                 # this file
├── research.md             # Phase 0: R1–R16
├── data-model.md           # Phase 1: saved_report, catalogs
├── quickstart.md           # Phase 1: flows A–G ↔ exit criteria
├── contracts/
│   ├── api.md              # HTTP: visual, saved reports, export
│   └── xlsx.md             # file layout
├── checklists/requirements.md
└── tasks.md                # /speckit.tasks — not created here
```

### Source Code (repository root) — files to add (A) or modify (M)

```text
db/migrations/0010_ai_reports_export.sql                    A

backend/Zharfa.Api/
├── Zharfa.Api.csproj                                       M  ClosedXML
├── Ai/
│   ├── VisualIntent.cs                                     A  R1–R2
│   ├── VisualBuilder.cs                                    A  table/chart from tool DTO
│   ├── PurchasingQuestionRouter.cs                         M  last-six-jalali-months + canonical
│   └── PurchasingAnalystTools.cs                           M  period rule; top-10 limit
├── Purchasing/Reports/
│   ├── JalaliPeriod.cs                                     M  LastNMonths
│   └── ExcelExportService.cs                               A  ClosedXML writer
├── SavedReports/
│   ├── SavedReport.cs                                      A
│   ├── SavedReportService.cs                               A
│   └── SavedReportName.cs                                  A  normalize, cap, overwrite
├── Controllers/
│   ├── AiController.cs                                     M  attach visual
│   ├── AiVisualExportController.cs                         A  or extra action on AiController
│   ├── SavedReportsController.cs                           A
│   └── PurchasingReportsController.cs                      M  /export twins + export policy
├── Audit/AuditActions.cs                                   M
├── Audit/ReportKeys.cs                                     M  generated
├── Platform/SystemModules.cs                               M  savedReports
├── Data/ZharfaDbContext.cs                                 M  optional DbSet
└── Program.cs                                              M  DI

backend/Zharfa.Api.Tests/
├── Ai/VisualIntentTests.cs                                 A
├── Purchasing/Reports/LastNMonthsTests.cs                  A
├── Purchasing/Reports/ExcelExportServiceTests.cs           A
├── SavedReports/SavedReportNameTests.cs                    A
├── Security/PermissionMatrixTests.cs                       M
└── Audit/AuditActionsTests.cs                              M

frontend/src/
├── App.tsx                                                 M  #/saved tab
├── pages/SavedReports.tsx                                  A
├── pages/Assistant.tsx / components/AiChat.tsx             M  table, chart, Keep
├── pages/PurchasingReports.tsx (and Vendors, Compare, …)   M  Save + Export
├── components/RankingBarChart.tsx                          A  SVG
├── components/SaveReportDialog.tsx                         A
└── services/api.ts                                         M  types + calls
```

**Structure Decision**: Continue the existing web-app layout (API + SPA). New saved-report and export code lives beside purchasing reports and AI, not a fourth host.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| New NuGet ClosedXML | PRD §9 F16 requires a real `.xlsx` with RTL sheet and Persian headers, produced and authorized on the server | Hand-rolled OOXML is larger and error-prone; client SheetJS would skip server permission and audit; CSV is not `.xlsx` |
