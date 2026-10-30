# Implementation Plan: Zharfa Demo 1 — Single-Path Analytics Demo

**Branch**: `001-zharfa-demo1` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-zharfa-demo1/spec.md` (Demo 1 PRD v2.0 governing; PRD.md v1.1 deferred reference; AGENTS.md binding; locked single-path decisions stamped).

**Note**: This plan is the `/speckit.plan` output. Constitution file `.specify/memory/constitution.md` is an unratified placeholder, so authority is the Demo 1 PRD + AGENTS.md (recorded in the Constitution Check gate below). ANALYZE: yes — cross-artifact analysis runs after tasks (see Completion Report).

## Summary

Deliver the Demo 1 sentence on synthetic data: a Persian RTL dashboard (sales overview, inventory alerts, receivables) plus one AI Business Analyst sharing the same three server-side KPI definitions, behind Manager/Admin server-side authz, in a thin WebView2 shell and a documented Docker Compose install that passes the five observable checks. Approach: PostgreSQL canonical entities + read-only semantic KPI views (three financial formulas documented next to their views); ASP.NET Core read-only API over those views only with server-side authz and one reconciliation check; React + TypeScript RTL frontend with month/category filters, supporting rows, browser-print; one cloud-provider AI agent behind a generic interface (Ollama seam preserved, not implemented) with allowlisted read-only tools, limits and timeouts; empty reserved sync tables; documented backup/restore.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0` API; `net10.0-windows` desktop shell) + TypeScript on React 19 + SQL (PostgreSQL 18).

**Primary Dependencies**: ASP.NET Core Web API; React 19 + react-dom + Vite 8 + Tailwind CSS 4 (+ Jalali date, Persian formatting and HTTP client libraries to be chosen at build within existing stack); Npgsql (PostgreSQL driver, to be added); Microsoft.Web.WebView2 1.0.4314.35 (already referenced); nginx reverse proxy (already configured).

**Storage**: PostgreSQL 18 (Compose `db` service). Canonical tables for Product, ProductCategory, Customer, SalesInvoice + SalesInvoiceItem, Warehouse, StockMovement (+ ProductBatch flag), Receipt/Payment (receivables side), User, Role, AuditLog, plus empty reserved SourceRecord/SyncJob/SyncLog. Read-only semantic KPI views for the three metric sets only. No migration runner chosen yet (scaffold note in db/README.md) — build wires one into API startup.

**Testing**: Tester runs `.ainative/project.yaml` validation_commands (frontend install+build; backend compile; desktop compile) — spec/plan/tasks workers never run installs or validation. Demo 1 adds: direct-API negative authz test (check 4), API-vs-DB-view reconciliation check (FR-013), clean-install + browser-print verification on a Docker host, documented backup/restore run once. No test framework is mandated by the PRD; build chooses within the locked stack.

**Target Platform**: Linux Docker Compose deployment (db + api + web/proxy) plus Windows WebView2 desktop shell hosting the same frontend (ZHARFA_FRONTEND_URL). WebView2 compiles cross-platform via EnableWindowsTargeting but runs only on Windows + WebView2 Runtime.

**Project Type**: Web application (backend API + shared SPA frontend) + thin desktop shell + Docker Compose packaging.

**Performance Goals**: Demo-scale: dashboard for one manager answers from seed data (multi-month, representative volume) interactively; AI answers return within a documented timeout (exact bound set at build); no numeric PRD target — "within one minute to first sight" per SC-001 and readable browser-print per check 5.

**Constraints**: Single-path only (custom React, one analyst agent, one cloud provider + Ollama seam not implemented, browser-print only — no Metabase/Platform Assistant/second provider/Excel/PDF engine/Mabna). Read-only analytics (allowlist + limits + timeouts; no writes, no arbitrary SQL). Secrets server-env only. Persian-first RTL everywhere with demonstration-data labels. No real company data. Host gaps planned around: no Docker daemon/compose on this host; WebView2 run needs Windows — Compose and shell-run verification documented for a Windows/Docker host or CI.

**Scale/Scope**: One demo database, two seeded roles, one provider config, three metric sets, five observable checks. Pilot/production hardening (monitoring, MFA, backup SLAs, mobile, forecasting) explicitly deferred.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md` is an unfilled template (placeholder principles, version unstated) — no ratified constitution exists. Authority substitute: Demo 1 PRD v2.0 + AGENTS.md. Gates evaluated against them:

- Single-path compliance: PASS — plan adds no Metabase, Platform Assistant, second provider, Mabna connector, Excel/PDF, mobile, or English UI. Ollama appears only as an unimplemented interface seam.
- Server-side enforcement: PASS — all authz and KPI math live in API/views; frontend holds no secrets, no direct DB access; negative direct-API test required.
- Read-only AI: PASS — approved views only, allowlist + limits + timeouts; every answer cites period+filters+view or states unavailable; facts separate from interpretation.
- Financial integrity: PASS — three formulas documented next to views; sales revenue never equated with cash received; one reconciliation check blocks sign-off on mismatch.
- Persian-first + demo-data honesty: PASS — RTL/Jalali/Persian formatting everywhere; demonstration-data labels; synthetic data only.
- Secrets: PASS — server env / Docker secrets only; none in bundle, repo, or logs.

Post-Phase-1 re-check: PASS — research, data model, contracts and quickstart below introduce no new surface (no extra provider, no extra agent, no write path, no legacy connection). No violations to justify; Complexity Tracking stays empty.

## Project Structure

### Documentation (this feature)

```text
specs/001-zharfa-demo1/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
frontend/
├── src/
│   ├── components/      # Demo 1: filters, metric cards, supporting tables, AI chat, demo-data banner
│   ├── pages/           # Demo 1: dashboard (3 metric sets), login
│   └── services/        # Demo 1: API client (backend only; no direct DB)
├── index.html           # Persian RTL shell (exists)
└── vite.config.ts       # /api -> :5080 dev proxy (exists)

backend/
├── Dockerfile           # exists
└── Zharfa.Api/
    ├── Controllers/     # Demo 1: health, sales, inventory, receivables, AI chat, admin (provider status), auth
    ├── Data/            # Demo 1: DbContext/Npgsql wiring, migration runner hookup, view mappings
    ├── Domain/          # Demo 1: entities (see data-model.md)
    ├── Analytics/       # Demo 1: KPI services over approved views, 3 formulas, reconciliation check
    ├── Ai/              # Demo 1: provider interface + one cloud implementation + Ollama seam (unimplemented) + allowlisted tools
    ├── Program.cs        # exists (health endpoint; extended at build)
    └── appsettings.json # exists (ConnectionStrings__Zharfa, AiProvider__* shape)

db/
├── migrations/         # Demo 1: NNNN_*.sql forward-only (canonical + views + reserved empty tables)
├── seed/               # Demo 1: synthetic multi-month demo data (returns/discounts, stock states, balances)
└── README.md           # exists (conventions)

deploy/
├── docker-compose.yml  # exists (db + api + web)
├── .env.example        # exists (POSTGRES_*, AI_PROVIDER_*, ZHARFA_PORT)
├── web/                # exists (Dockerfile + nginx.conf with /api proxy)
└── README.md           # exists (install + backup/restore; extended at build)

desktop/
└── Zharfa.Desktop/     # exists: thin WebView2 shell (App/MainWindow host shared frontend; no logic added)
```

**Structure Decision**: Web-application layout reusing the existing scaffold (frontend + backend/Zharfa.Api + db + deploy + desktop/Zharfa.Desktop). No new top-level projects; Demo 1 code lands in the scaffold's empty folders (Controllers/Data/Domain/Analytics/Ai, db/migrations, db/seed, frontend components/pages/services). Desktop shell gains no logic.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
(none — gate passes with no violations)
