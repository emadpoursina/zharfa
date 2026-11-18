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
| `Ai/` | Purchasing AI tools (PurchasingAnalystTools), Local Ollama provider (OllamaAiProvider), cloud provider (CloudAiProvider), active resolver (ActiveAiProvider), settings (AiProviderSettings), and in-flight gate (AiQuestionGate) |

Schema lives in `db/migrations` (Phase 1 adds `0005_purchasing_sync.sql` and `0006_purchasing_kpis.sql`; Phase 2 adds `0007_purchasing_reports.sql`; Phase 3 adds `0008_permissions_audit_quality.sql`; Phase 4 adds `0009_local_ai_purchasing.sql`).
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
export LocalAi__Name="ollama" LocalAi__Endpoint="http://localhost:11434/v1" LocalAi__Model="qwen2:7b" # winner among pinned candidates (qwen2:7b, qwen2.5:7b, llama3.1:8b)
export AiProvider__ApiKey="..."                 # server env / Docker secrets only
```

Production only: `Bootstrap__AdminUsername` and `Bootstrap__AdminPassword` (or `Bootstrap__AdminPasswordFile`)
create the first Admin while no active Admin exists; that Admin must change the password at first sign-in
(password at least 12 characters, never logged). Blank `Sync__*` values fall back to their defaults.
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

## Purchasing reports API (Release 1, Phase 2)

Vendor directory and profile, the seven purchasing reports, the document drill-down, the filter-bar lookups and
the vendor comparison. The controller is `Controllers/PurchasingReportsController.cs`; the logic lives in
`Purchasing/Reports/` (`ReportCatalog`, `ReportFilter`, `JalaliPeriod`, `PurchasingReportService`,
`VendorDirectoryService`, `VendorComparisonService`, `ReportSql`, `PrintLimit`). Full request and response shapes
are in `specs/004-vendor-purchasing-reports/contracts/api.md`; the measures are in `docs/metrics/purchasing.md`
("Reports").

All endpoints are under `/api/purchasing`, require `manager` or `admin` (401 anonymous, 403 other roles), and are
available in both production and demo (not `[DemoOnly]`). Money is returned as exact decimal **strings** in Rial with
4 decimals; an unavailable figure is `null` with a reason in `unavailable[]`.

| Endpoint | Purpose |
|---|---|
| `GET /vendors` | Vendor directory: net, last purchase date, search by name or code, optional comparison |
| `GET /vendor-profile?vendor={key}` | One vendor: totals, comparison, 12-month trend, top products, returns, documents. The key is a query parameter because source keys can hold `/` |
| `GET /vendors/compare?vendors=K1,K2[,…]` | Two to five vendors side by side with a 12-month trend (400 outside 2..5 distinct keys) |
| `GET /reports/{reportId}` | `by-vendor`, `by-product`, `by-category`, `trend`, `top-products`, `returns`, `transactions` |
| `GET /documents`, `GET /documents/{id}/lines` | Drill-down documents and their lines (404 for an unknown or cancelled document) |
| `GET /lookup/{vendors\|products\|categories}?q=&limit=` | Filter-bar search, at most 50 items |
| `GET …/print` | Print twin of every table endpoint above (`vendors`, `vendors/compare`, `reports/{id}`, `documents`): same parameters, no paging, at most 1,000 rows (`truncated` when more) |

Shared parameters: `period` (`month` | `quarter` | `year` | `custom`) with `month`, `quarter`, `year` or Jalali
`from`/`to`; `compare`; `vendor`, `product`, `category` (source key or `~unknown`); `q` (≤ 100 characters);
`sort`, `dir`; `page` and `pageSize` (25 | 50 | 100). Invalid input answers 400 with a Persian `message`.

**Timeout.** Each report query has a 15-second command timeout. A query over that budget answers **503** with
`{ "message": "گزارش در زمان مجاز آماده نشد؛ بازه را کوتاه‌تر کنید." }`; the Persian message tells the user to shorten the
period. (`PurchasingReportService.CommandTimeoutSeconds`.)

**Feature flag.** `GET /api/system/status` returns `modules.purchasingReports: true` for every signed-in user. It only
controls navigation; every endpoint above enforces the role on the server.

**Data.** The reports read the synced `mabna.*` copy through `mabna.fn_purchase_lines` (migration
`db/migrations/0007_purchasing_reports.sql`). They never query Mabna per request and never read Mabna's
host-filtered report views. Nothing here returns payments, vendor balance, credit or debt.

## Tests

```bash
dotnet test backend/Zharfa.Api.Tests    # pure unit tests: no database, no network
```
