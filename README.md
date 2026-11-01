# Zharfa

Persian-first business intelligence and AI analytics platform for a
pharmaceutical distribution company. It is built as an independent
intelligence layer **around** the existing operational system
(`سیستم جامع شرکت مبنا`) — never replacing it and never writing to it.

- **Governing scope:** [`zharfa-demo1-prd.md`](zharfa-demo1-prd.md) — Demo 1:
  one demo path on synthetic data (Persian RTL dashboard for sales, inventory
  alerts and receivables; one AI Business Analyst; Manager/Admin RBAC;
  Docker Compose install).
- **Full vision:** [`PRD.md`](PRD.md) — v1.1, reference only for everything
  deferred past Demo 1.
- **Stack (locked for Demo 1):** React + TypeScript, ASP.NET Core Web API,
  PostgreSQL, WebView2 Windows shell, Docker Compose.
- **Project rules:** [`AGENTS.md`](AGENTS.md).
- **Workflow:** work is filed as cards on the `zharfa` Hermes Kanban board.

Everything in the Demo 1 PRD is synthetic demonstration data until real
database access is granted; the legacy system stays out of Demo 1 entirely.

## Repository layout

```
frontend/        React + TypeScript + Vite + Tailwind shared frontend (Persian RTL)
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
docker compose -f deploy/docker-compose.yml up --build -d          # full deployment
```

`validation_commands` in [`.ainative/project.yaml`](.ainative/project.yaml)
list exactly what the tester runs; the backend and desktop builds need the
.NET 10 SDK.
