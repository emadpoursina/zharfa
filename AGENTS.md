# Project rules — Zharfa

## Project context

- **Purpose:** Self-hosted BI & AI analytics platform for a pharmaceutical
  distribution company. Persian (RTL) dashboards and an AI Business Analyst
  layered *around* the existing operational system (`سیستم جامع شرکت مبنا`) —
  never replacing it and never writing to it.
- **Name:** the project is **Zharfa**; older PRDs carry the working product
  name "Pharmacy Analytics". Use *Zharfa* in repo docs, cards and commit messages.
- **Scope authority:** `zharfa-prd.md` (v3.0, 2026-11-16) governs all work
  after Demo 1: three releases (1 Vendors & Purchasing — detailed; 2 Pharmacy
  Sales and 3 Warehouse — outlined) on read-only Mabna data. Its phase plan
  `zharfa-prd.phases.md` orders Release 1 into feature runs
  (`phase: zharfa-prd.phases.md#N`). It is the only PRD in scope. Everything
  in `docs/prd-archive/` (Demo 1 PRD v2.0, PRD v1.1, the three-release
  direction draft) is history/reference and yields to `zharfa-prd.md`. Spec 002 is superseded: its connector,
  guards and simulator are the Release 1 foundation, and its receivables/cash
  stories move to Release 2.
- **Stack (locked since Demo 1, confirmed by `zharfa-prd.md` §9):** React +
  TypeScript (+ Tailwind or equivalent), ASP.NET Core Web API, PostgreSQL,
  WebView2 + .NET thin desktop shell, Docker Compose, read-only Mabna SQL
  Server connector, local AI via Ollama (default) with the cloud provider
  opt-in. Not used: Metabase, Wren AI.
- **Layout:** app code scaffolded by the bootstrap job — `frontend/` (React +
  TypeScript + Vite + Tailwind, Persian RTL), `backend/Zharfa.Api` (ASP.NET
  Core Web API), `desktop/Zharfa.Desktop` (WebView2 + WPF thin shell),
  `db/migrations` + `db/seed` (PostgreSQL, synthetic demo data),
  `deploy/` (docker-compose.yml, `.env.example`, nginx reverse proxy).
  Docs: `zharfa-prd.md` + `zharfa-prd.phases.md` (governing),
  `docs/prd-archive/` (superseded PRDs, history only), `README.md`,
  `AGENTS.md`, `.ainative/project.yaml`, Spec Kit layout in `.specify/` +
  `.pi/prompts/speckit.*.md`. `scratch/` is for throwaway notes and is
  gitignored. Spec 002 adds `deploy/mabna-sim/` (offline Mabna SQL Server
  simulator, synthetic data) and `backend/Zharfa.Api.Tests` (xunit tests).

## Workflow

- Work is submitted through this project's Hermes Kanban board (`zharfa`).
- Follow the card's `## Path` and the workflow stage assigned by Hermes.
- Keep `validation_commands` in `.ainative/project.yaml` accurate; the tester
  state runs them for managed work.
- For a `feature`, follow the spec/plan/tasks workflow and its reviews. A
  `change` is already specified; a `job` runs only its named skill.
- Each Release 1 phase is done when its exit criteria in
  `zharfa-prd.phases.md` hold; phases run strictly in order, one feature run
  each. Release 1 is done after Phase 7 (go-live). Demo 1 was done when its
  §4 five checks held (history).
- Ask rather than guessing when a consequential project decision is unclear.
- Update related project documentation when behavior or interfaces change.

## Code and design constraints

- Persian-first RTL: Jalali calendar, Persian typography, labels, search and
  number/currency formatting; responsive, desktop-first layouts.
- All authorization and every KPI calculation is enforced server-side;
  frontend hiding is not enforcement, and no client may talk to a database
  directly.
- AI answers only through approved read-only views/tools with allowlist,
  limits and timeouts; every answer cites its period and filters and says
  "data unavailable" rather than guessing. Facts stay separate from
  interpretation.
- Secrets (AI keys, DB credentials) live in server env / Docker secrets only —
  never in the frontend bundle, the repo, or logs.
- Real and synthetic data never mix: production installs show real data only;
  demo installs label everything as demonstration data; the AI uses one
  dataset per install. Real Mabna data never leaves the company site or enters
  the repo; development uses `deploy/mabna-sim/`.
- Mabna is read-only: the read-only login is the primary control; the
  connector's SELECT-only guard and table allowlist are secondary. Never read
  Mabna's `HOST_ID()`-filtered report views on live Mabna.
- Real data goes to a cloud AI provider only as aggregates, and only after an
  Admin enables it with recorded owner approval; the local model is default.
- Prefer existing project abstractions; justify new dependencies.

## Project-specific notes

- Mabna data reaches Zharfa through a nightly incremental sync plus an Admin
  "sync now" into PostgreSQL via the reserved `SourceRecord/SyncJob/SyncLog`
  tables; reports read the synced copy, never Mabna per request.
- Reports are never built on assumed Mabna structures: verify the source
  tables on-site (`specs/002-mabna-accounting-connector/onsite-checklist.md`)
  before any formula uses them.
- Every reconciled metric must match the same query against the source data;
  a mismatch blocks sign-off. Release 1 reconciles purchase totals and vendor
  balances.
- Keep financial formulas separate and documented: purchases vs payments vs
  vendor balance (Release 1); sales revenue vs cash received vs receivables
  balance (Release 2, from Demo 1/spec 002). Never present one as another, and
  never present a price difference as profit.
- Roles are permission sets enforced server-side; Release 1 seeds Admin,
  Management, Purchasing and Finance (vendor financials: Management + Finance).
- Release 1 deploys to the company LAN server only (HTTPS, no remote access).

## Repository commands

```bash
npm --prefix frontend install && npm --prefix frontend run build  # frontend build (tsc -b + vite)
npm --prefix frontend run dev                                     # dev server on :5173, /api -> :5080
dotnet run --project backend/Zharfa.Api                           # API on :5080, GET /api/health
dotnet build backend/Zharfa.Api/Zharfa.Api.csproj                 # backend compile
dotnet build desktop/Zharfa.Desktop/Zharfa.Desktop.csproj         # desktop shell compile (any OS)
docker compose -f deploy/docker-compose.yml up --build -d         # db + api + web/proxy
docker compose -f deploy/docker-compose.mabna-sim.yml up -d       # Mabna simulator, synthetic data (deploy/mabna-sim/README.md)
make up                                                           # same as ./deploy/zharfa.sh up (docker stack, background)
make down                                                         # same as ./deploy/zharfa.sh down (stops, keeps data)
dotnet test backend/Zharfa.Api.Tests                              # backend unit tests (no DB or network)
```

- The tester runs `.ainative/project.yaml` `validation_commands`; workers do
  not run installs or validation.
- Backend/desktop builds need the .NET 10 SDK (`net10.0` / `net10.0-windows`);
  the shell compiles cross-platform via `EnableWindowsTargeting`, but runs
  only on Windows with the WebView2 Runtime.
- Secrets are supplied as environment variables
  (`ConnectionStrings__Zharfa`, `AiProvider__*`) or via `deploy/.env`
  (gitignored) — never committed, never in the frontend.

## Out of scope

- Replacing Mabna, editing operational transactions, writes to the legacy
  database, ERP functionality, automated accounting or business decisions.
- In Release 1: sales, pharmacy, receivables and warehouse/inventory modules
  on real data (Releases 2–3), Sales/Warehouse/Viewer roles, cross-domain
  reports, custom PDF pipeline (browser-print only).
- Platform Assistant, MCP tool catalog, AI anomaly/pattern engine, Metabase
  and Wren AI.
- Remote/VPN/internet access, native mobile apps, PWA/Capacitor packaging,
  push, offline, biometric auth, English UI.
