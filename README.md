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

Structure only so far — Demo 1 feature code lands on feature cards.

## Local commands

```bash
npm --prefix frontend install && npm --prefix frontend run build   # frontend build
dotnet run --project backend/Zharfa.Api                            # API on :5080
dotnet build desktop/Zharfa.Desktop                                # desktop shell compile
dotnet test backend/Zharfa.Api.Tests                               # backend unit tests (no DB or network)
docker compose -f deploy/docker-compose.yml up --build -d          # full deployment
```

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
