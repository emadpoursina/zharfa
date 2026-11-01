# Project rules — Zharfa

## Project context

- **Purpose:** Self-hosted BI & AI analytics platform for a pharmaceutical
  distribution company. Persian (RTL) dashboards and an AI Business Analyst
  layered *around* the existing operational system (`سیستم جامع شرکت مبنا`) —
  never replacing it and never writing to it.
- **Name:** the project is **Zharfa**; the PRDs carry the working product name
  "Pharmecy Analytics". Use *Zharfa* in repo docs, cards and commit messages.
- **Scope authority:** `zharfa-demo1-prd.md` (v2.0 Demo 1) governs Demo 1 and
  overrides `PRD.md` (v1.1 full vision) wherever they conflict. `PRD.md` is
  reference for deferred work only.
- **Stack (locked by Demo 1 PRD §6):** React + TypeScript (+ Tailwind or
  equivalent), ASP.NET Core Web API, PostgreSQL, WebView2 + .NET thin desktop
  shell, Docker Compose. Deferred, not decided: Metabase, Wren AI, a second AI
  provider (local Ollama seam preserved), Mabna SQL Server connector.
- **Layout:** app code scaffolded by the bootstrap job — `frontend/` (React +
  TypeScript + Vite + Tailwind, Persian RTL), `backend/Zharfa.Api` (ASP.NET
  Core Web API), `desktop/Zharfa.Desktop` (WebView2 + WPF thin shell),
  `db/migrations` + `db/seed` (PostgreSQL, synthetic demo data),
  `deploy/` (docker-compose.yml, `.env.example`, nginx reverse proxy).
  Docs: `zharfa-demo1-prd.md` + `PRD.md` at the root (tracked), `README.md`,
  `AGENTS.md`, `.ainative/project.yaml`, Spec Kit layout in `.specify/` +
  `.pi/prompts/speckit.*.md`. `scratch/` is for throwaway notes and is
  gitignored.

## Workflow

- Work is submitted through this project's Hermes Kanban board (`zharfa`).
- Follow the card's `## Path` and the workflow stage assigned by Hermes.
- Keep `validation_commands` in `.ainative/project.yaml` accurate; the tester
  state runs them for managed work.
- For a `feature`, follow the spec/plan/tasks workflow and its reviews. A
  `change` is already specified; a `job` runs only its named skill.
- Demo 1 is done when its §4 five observable checks hold — that is the only
  DoD; everything else is pilot scope.
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
- Synthetic data must be labeled as demonstration data everywhere; real
  company data is explicitly out of scope for Demo 1.
- Prefer existing project abstractions; justify new dependencies.

## Project-specific notes

- Demo 1 is single-path by design: custom React dashboard (no Metabase), one
  Business Analyst agent (no Platform Assistant), one cloud AI provider,
  browser-print as the only export.
- The schema reserves empty `SourceRecord/SyncJob/SyncLog` tables so later
  Mabna integration does not require a redesign; Demo 1 does not connect to
  Mabna at all.
- One reconciliation check ships in Demo 1: API sales/receivables totals must
  match the same query against the DB views; a mismatch blocks sign-off.
- Financial integrity for Demo 1 is limited to three documented formulas
  (sales revenue, cash received, receivables balance) — never equate sales
  revenue with cash received.

## Repository commands

```bash
npm --prefix frontend install && npm --prefix frontend run build  # frontend build (tsc -b + vite)
npm --prefix frontend run dev                                     # dev server on :5173, /api -> :5080
dotnet run --project backend/Zharfa.Api                           # API on :5080, GET /api/health
dotnet build backend/Zharfa.Api/Zharfa.Api.csproj                 # backend compile
dotnet build desktop/Zharfa.Desktop/Zharfa.Desktop.csproj         # desktop shell compile (any OS)
docker compose -f deploy/docker-compose.yml up --build -d         # db + api + web/proxy
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
- Purchases / suppliers / payables modules, full rankings, advanced
  filtering, expiry-batch workflow, Excel export, custom PDF pipeline.
- Second AI provider implementation, Platform Assistant, MCP tool catalog,
  AI insights/anomaly engine, Metabase and Wren AI evaluations.
- Native mobile apps, PWA/Capacitor packaging, push, offline, biometric
  auth, English UI (MVP).
