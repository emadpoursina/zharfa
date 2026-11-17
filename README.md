# Zharfa

Persian-first business intelligence and AI analytics platform for a
pharmaceutical distribution company. It is built as an independent
intelligence layer **around** the existing operational system
(`سیستم جامع شرکت مبنا`) — never replacing it and never writing to it.

- **Governing scope:** [`zharfa-prd.md`](zharfa-prd.md) v3.0 — three
  releases on read-only Mabna data (1 Vendors & Purchasing, 2 Pharmacy Sales,
  3 Warehouse) with one central AI agent. Release 1 is split into phases in
  [`zharfa-prd.phases.md`](zharfa-prd.phases.md).
- **History and reference (not scope authority):** [`docs/prd-archive/`](docs/prd-archive/)
  holds the Demo 1 PRD (built and accepted), PRD v1.1 (vision) and the
  three-release direction draft.
- **Stack:** React + TypeScript, ASP.NET Core Web API, PostgreSQL, WebView2
  Windows shell, Docker Compose, read-only Mabna SQL Server connector, local
  AI (Ollama) with an opt-in cloud provider.
- **Project rules:** [`AGENTS.md`](AGENTS.md).
- **Workflow:** work is filed as cards on the `zharfa` Hermes Kanban board.

Demo installs run on synthetic demonstration data, labeled as such.
Production installs read real Mabna data through a read-only sync and never
write to Mabna; development uses the offline simulator in `deploy/mabna-sim/`.

## Release 1, Phase 1: Real Purchase Data

- **Data mode:** every install runs in exactly one mode, set by `ZHARFA_DATA_MODE`
  (`production` or `demo`). It is required; the API refuses to start without it.
  `production` shows real purchasing data only and hides the demo pages and the AI chat.
  `demo` shows the synthetic data, labeled as demonstration data.
- **Overview (Manager and Admin):** the purchasing Overview for a Jalali month: net, gross,
  returns and VAT, comparison with the previous month, a 12-month trend, active vendors and the
  top five vendors and products. Each figure carries its data label and last-sync time.
- **Data sync (Admin):** a read-only nightly incremental sync from Mabna (default 02:00 Tehran
  time, weekly full load) plus an Admin "sync now". Reports read the synced copy, never Mabna per request.
- **Reconciliation:** `reconcile purchasing` compares each purchasing total with the same query on the
  source data. A mismatch blocks sign-off.
- **Metric definitions:** [`docs/metrics/purchasing.md`](docs/metrics/purchasing.md) (single source for every purchasing figure).
- **Deployment and configuration:** [`deploy/README.md`](deploy/README.md) and
  [`backend/Zharfa.Api/README.md`](backend/Zharfa.Api/README.md). The real source map and
  allowlist are never committed.
- **Spec:** [`specs/003-real-purchase-data/`](specs/003-real-purchase-data/).

## Repository layout

```
frontend/        React + TypeScript + Vite + Tailwind shared frontend (Persian RTL;
                 self-hosted Vazirmatn variable font, SIL OFL-1.1 — see
                 frontend/public/OFL-Vazirmatn.txt, no font CDN)
backend/         ASP.NET Core Web API (Zharfa.Api) + its Dockerfile
desktop/         Thin WebView2 + WPF Windows shell (Zharfa.Desktop)
db/              PostgreSQL migrations/ + seed/ (synthetic demonstration data)
deploy/          docker-compose.yml, .env.example, nginx reverse proxy
.ainative/       project manifest with the tester's validation_commands
scratch/         throwaway notes (gitignored)
```

The Demo 1 features and the Release 1 Phase 1 purchasing features are in place.

## Local commands

```bash
npm --prefix frontend install && npm --prefix frontend run build   # frontend build
dotnet run --project backend/Zharfa.Api                            # API on :5080
dotnet build desktop/Zharfa.Desktop                                # desktop shell compile
dotnet test backend/Zharfa.Api.Tests                               # backend unit tests (no DB or network)
docker compose -f deploy/docker-compose.yml up --build -d          # full deployment (deploy/.env must set ZHARFA_DATA_MODE)
```

For a full deployment, copy `deploy/.env.example` to `deploy/.env` and set at least
`POSTGRES_PASSWORD` and `ZHARFA_DATA_MODE` (`production` or `demo`). Compose refuses to start without them.

`validation_commands` in [`.ainative/project.yaml`](.ainative/project.yaml)
list exactly what the tester runs; the backend and desktop builds need the
.NET 10 SDK.

## Mabna simulator (offline development)

A local SQL Server 2019 container holds the captured Mabna schema with
**synthetic** data, so the accounting connector can be built and tested without
the company server. Live reads happen only on-site, following the checklist.

```bash
docker compose -f deploy/docker-compose.mabna-sim.yml up -d       # start (first run builds the DB)
docker compose -f deploy/docker-compose.mabna-sim.yml down        # stop, keep data
docker compose -f deploy/docker-compose.mabna-sim.yml down -v     # reset: delete the volume
```

Set `MABNA_SIM_SA_PASSWORD` and `MABNA_SIM_RO_PASSWORD` in `deploy/.env` (see
`deploy/.env.example`). It listens on `127.0.0.1:14330`.

- Simulator setup, regeneration and seed notes:
  [`deploy/mabna-sim/README.md`](deploy/mabna-sim/README.md)
- On-site verification steps:
  [`specs/002-mabna-accounting-connector/onsite-checklist.md`](specs/002-mabna-accounting-connector/onsite-checklist.md)
