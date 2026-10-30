# Research: Zharfa Demo 1

**Feature**: specs/001-zharfa-demo1 | **Date**: 2026-10-30 | **Phase**: 0 (all NEEDS CLARIFICATION resolved — Technical Context has no unknowns)

## Decisions

### 1. PostgreSQL access + migration runner

- **Decision**: Npgsql (PostgreSQL driver) with EF Core or a lightweight Dapper-style data-access layer; forward-only numbered SQL migrations (`NNNN_name.sql`) applied in ascending order by a runner wired into API startup; KPI logic lives in SQL views, not application code.
- **Rationale**: Scaffold `db/README.md` already fixes the convention (numbered forward-only migrations; views own KPIs; seed labeled demo data) but leaves the runner open; wiring it into API startup makes clean Compose install work with no manual DB edits (check 5). Npgsql is the standard .NET PostgreSQL driver.
- **Alternatives considered**: DbUp / FluentMigrator / raw psql at container start — any is acceptable; build picks one and records it. External migration-only container — rejected, adds a moving part for a demo.

### 2. Jalali dates + Persian formatting

- **Decision**: Store Gregorian dates in PostgreSQL (`timestamptz`/`date`); convert to Jalali at presentation; Persian digits, currency and number formatting in the frontend; .NET backend formats Jalali server-side only where it renders text (AI answers, logs), using a standard .NET Persian-calendar library.
- **Rationale**: Gregorian storage keeps SQL date math and MoM comparison exact; Jalali appears only at the edges (UI, answers). No PRD requirement for Jalali storage.
- **Alternatives considered**: Jalali strings in DB — rejected (breaks ordering, ranges, MoM math).

### 3. AuthN/ZH mechanism (Demo 1 default from clarify)

- **Decision**: Seeded demo accounts (one Manager, one Admin) with password login on a Persian login screen; server-side sessions (cookie) with hashed passwords (ASP.NET Core Identity or equivalent minimal equivalent); no self-registration; every authorization check server-side (controllers/policies), frontend hiding only cosmetic; negative direct-API test required (check 4).
- **Rationale**: Smallest mechanism satisfying server-side RBAC with two roles and a negative test; production identity (SSO/MFA) is pilot scope.
- **Alternatives considered**: JWT bearer tokens — acceptable if build prefers; session cookies preferred for a browser/WebView2 demo (no token handling in JS).

### 4. Single AI provider integration shape

- **Decision**: Backend `IAiProvider` interface (chat completion over controlled tool results) with exactly one cloud HTTP implementation configured by `AiProvider__Name/Endpoint/Model` (+ key from env/secret); generic request/response mapping so the operator supplies any OpenAI-compatible endpoint at deploy; no provider SDK lock-in, no provider named in repo; Ollama preserved as an unimplemented second implementation of the same interface (seam only); admin-only redacted status endpoint (name/endpoint/model, never key); no in-app provider editing in Demo 1.
- **Rationale**: Satisfies "one cloud provider + Ollama seam preserved-not-implemented" without naming a vendor or committing secrets; env-only config satisfies §7.2.
- **Alternatives considered**: Named-vendor SDK — rejected (locks the demo to one vendor in code). MCP-ifying every function — deferred per PRD (direct controlled tools only).

### 5. Read-only AI tools + guardrails

- **Decision**: Three allowlisted tools (sales-overview query, inventory-alerts query, receivables query) bound to the same approved KPI views with fixed parameter surface (period range + single category); server-side row/time limits and a call timeout; system prompt requires citing period+filters+source view, separating facts from interpretation, and saying data-unavailable instead of guessing; no text-to-arbitrary-SQL.
- **Rationale**: Direct implementation of PRD §3.5 + §7.3/§7.6 with the smallest auditable surface.
- **Alternatives considered**: NL-to-SQL prototype — allowed only if restricted to the approved views (PRD §3.5); not needed for Demo 1 questions.

### 6. Three financial formulas (documented next to views)

- **Decision**: `sales_revenue = SUM(item qty × unit price − line discount)` over posted invoice items in period (returns modeled representatively as negative items or documented discount path); `cash_received = SUM(receipts)` in period; `receivables_balance = SUM(posted invoice net) − SUM(receipts)` per customer (opening demo balances seeded explicitly). Each view carries a SQL comment with its formula; sales revenue never equated with cash received in UI, API shapes, or answers.
- **Rationale**: PRD §5 limits Demo 1 integrity to these three documented formulas; view-adjacent documentation keeps API, dashboard and AI on one truth.
- **Alternatives considered**: Application-layer formulas — rejected (views are the single KPI source per db/README.md).

### 7. Reconciliation check (FR-013)

- **Decision**: API endpoint (or startup/sign-off routine) recomputing sales and receivables totals with the same predicates as the views and comparing to the view totals; mismatch blocks sign-off and writes an AuditLog entry; documented as the single Demo 1 check, not a full suite.
- **Rationale**: Exact PRD §7.5 requirement with mismatch-blocks-sign-off semantics.
- **Alternatives considered**: Full reconciliation suite — deferred to pilot per PRD.

### 8. Filters: month presets + custom range + single category

- **Decision**: Period presets (current Jalali month, previous Jalali month, custom range) mapped to Gregorian date ranges at the API boundary; single-select category applied as a predicate to all three metric sets; MoM compares selected Jalali month vs previous Jalali month; every AI answer echoes the resolved Gregorian range alongside the Jalali label.
- **Rationale**: Implements FR-004 and the clarify default; keeps one KPI definition across dashboard, tables and AI.
- **Alternatives considered**: Multi-dimension filtering — deferred per PRD.

### 9. Browser-print export

- **Decision**: Print CSS (`@media print`, RTL-aware) over dashboard/table views; no PDF engine, no Excel; readable Persian report verified on the Docker host (check 5).
- **Rationale**: PRD single-path choice; zero new dependencies.
- **Alternatives considered**: Custom RTL PDF pipeline / Excel — explicitly out of scope.

### 10. Backup/restore + install docs

- **Decision**: Keep `deploy/README.md` `pg_dump`/`psql` procedure; validate once before sign-off; extend README only with whatever the build adds (runner behavior, seed accounts, provider env, ZHARFA_FRONTEND_URL for the shell).
- **Rationale**: PRD §7.7 with minimal doc churn.

### 11. Host-gap plan-arounds (no Docker daemon / no Windows here)

- **Decision**: Spec/plan/tasks do not require running Compose or WebView2 on this host; quickstart validation marks Docker-host and Windows-host steps as requiring a Windows/Docker host or CI; build/tester on this host cover frontend build, backend compile, desktop compile (the four validation_commands).
- **Rationale**: Locked orchestrator decision — plan around, do not block spec.
