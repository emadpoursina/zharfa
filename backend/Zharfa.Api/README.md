# Zharfa backend — `Zharfa.Api` (ASP.NET Core Web API, net10.0)

Scaffold only: no Demo 1 feature code yet. Feature cards add controllers,
approved read-only views, RBAC and the AI Business Analyst tools.

## Layout

| Folder | Purpose (Demo 1) |
|---|---|
| `Controllers/` | HTTP surface; every authorization check lives here and below |
| `Domain/` | Canonical entities: Product, Customer, SalesInvoice, Receipt, User, Role, AuditLog |
| `Data/` | PostgreSQL access (Npgsql) + migration runner hooks; SQL lives in `/db/migrations` |
| `Analytics/` | KPI definitions for the 3 metric sets (sales, inventory alerts, receivables) |
| `Ai/` | Business Analyst agent + single-provider interface (local Ollama seam preserved, not implemented) |

All tables for Demo 1 are created by `db/migrations`; the schema reserves empty
`SourceRecord` / `SyncJob` / `SyncLog` tables for later Mabna integration.
Demo 1 never connects to any legacy system.

## Configuration (secrets stay out of the repo)

Values come from `appsettings.json` (non-secret defaults) and the environment:

```bash
# ASP.NET Core double-underscore notation
export ConnectionStrings__Zharfa="Host=localhost;Port=5432;Database=zharfa;Username=zharfa;Password=..."
export AiProvider__Name="..."
export AiProvider__Endpoint="..."
export AiProvider__Model="..."
export AiProvider__ApiKey="..."   # server env / Docker secrets only
```

`AiProvider__ApiKey` and DB passwords are never written to `appsettings.json`,
the frontend bundle, or logs.

## Run

```bash
dotnet run --project backend/Zharfa.Api        # http://localhost:5080, GET /api/health
```
