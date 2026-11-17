# Zharfa backend — `Zharfa.Api` (ASP.NET Core Web API, net10.0)

The HTTP API for Zharfa: sign-in and role checks, the purchasing Overview and sync (Release 1,
Phase 1), the demo analytics, and the AI surface (refused in production until Phase 4). Every
authorization check and every KPI calculation lives here, never in the frontend.

## Layout

| Folder | Purpose |
|---|---|
| `Controllers/` | HTTP surface: auth, `system/status`, `overview/purchasing` (Manager, Admin), `admin/sync/*` (Admin), demo routes marked `[DemoOnly]`, AI |
| `Platform/` | Data mode (`DataModeOptions`, `DataModeGate`), the `[DemoOnly]` filter, bootstrap accounts, configuration keys, module flags |
| `Purchasing/` | Overview figures and their definitions (`JalaliMonth`, `ComparisonMath`, `MetricAvailability`), discount allocation, reconciliation and the `reconcile` CLI |
| `Mabna/` | Read-only connector (`IMabnaReader`, SELECT-only guard, table allowlist) and `Mabna/Sync/`: source map, planner, runner, scheduler, job store |
| `Data/` | PostgreSQL access (Npgsql, EF Core), migration runner, audit log, redaction |
| `Analytics/` | Demo KPI definitions for sales, inventory and receivables |
| `Ai/` | Business Analyst agent and the single-provider interface (local Ollama seam preserved, not implemented) |

Schema lives in `db/migrations` (Phase 1 adds `0005_purchasing_sync.sql` and `0006_purchasing_kpis.sql`).
Demo seeds live in `db/seed` and apply in demo mode only.

## Configuration (secrets stay out of the repo)

Values come from `appsettings.json` (non-secret defaults) and the environment, in ASP.NET Core
double-underscore notation. Secrets come from the server environment or Docker secrets only:

```bash
export Zharfa__DataMode="demo"                  # required: production | demo; the API refuses to start otherwise
export ConnectionStrings__Zharfa="Host=localhost;Port=5432;Database=zharfa;Username=zharfa;Password=..."
export ConnectionStrings__Mabna="..."           # read-only Mabna login; blank = connector and sync disabled
export MabnaSync__SourceMapFile="/path/to/purchase-source-map.json"   # gitignored; blank = sync disabled
export MabnaSource__AllowlistFile="/path/to/allowlist.txt"           # gitignored
export Purchasing__DiscountBasis="line"         # line | lineAndDocument; production must match the verified map
export Sync__NightlyTime="02:00" Sync__TimeZone="Asia/Tehran" Sync__WeeklyFullLoadDay="Friday"
export AiProvider__ApiKey="..."                 # server env / Docker secrets only
```

Production only: `Bootstrap__AdminUsername`, `Bootstrap__AdminPassword`, `Bootstrap__ManagerUsername`
and `Bootstrap__ManagerPassword` create the first two accounts once, when `app_user` is empty
(passwords at least 12 characters, never logged). Blank `Sync__*` values fall back to their defaults.
The full key list and defaults are in `specs/003-real-purchase-data/contracts/cli-and-config.md` §2.

`AiProvider__ApiKey`, DB passwords and Mabna credentials are never written to `appsettings.json`,
the frontend bundle, or logs.

## Run

```bash
dotnet run --project backend/Zharfa.Api        # http://localhost:5080, GET /api/health
```

## Reconciliation CLI

The same binary runs the purchasing reconciliation without starting the web host:

```bash
dotnet run --project backend/Zharfa.Api -- reconcile purchasing --month 1405-08 [--vendor <key> | --product <key>] [--basis line|lineAndDocument] [--show-amounts]
```

It needs a succeeded sync, the Mabna settings above and `Zharfa__DataMode`. It prints one JSON line on
stdout and exits `0` (all pass), `1` (mismatch, sign-off blocked) or `2` (error). In `production` the
amounts are omitted unless `--show-amounts` is given. Deployment usage is in `deploy/README.md`; the metric
definitions are in `docs/metrics/purchasing.md`.

## Tests

```bash
dotnet test backend/Zharfa.Api.Tests    # pure unit tests: no database, no network
```
