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

## Release 1, Phase 3: Permissions, Audit, and Data Quality

- **Roles & Permissions:** Four roles with server-enforced permission sets:
  | Role | Persian Label | Permissions |
  |---|---|---|
  | `admin` | مدیر سامانه | `overview`, `purchasing.reports`, `purchasing.transactions`, `admin` |
  | `management` | مدیریت | `overview`, `purchasing.reports`, `purchasing.transactions`, `vendor.financials`, `ai.query`, `export` |
  | `purchasing` | واحد خرید | `overview`, `purchasing.reports`, `purchasing.transactions`, `ai.query`, `export` |
  | `finance` | واحد مالی | `overview`, `purchasing.reports`, `purchasing.transactions`, `vendor.financials`, `ai.query`, `export` |
- **Demonstration accounts (demo mode only):**
  - Administrator: `admin` / `admin123`
  - Management: `manager` / `manager123` (role `management`)
  - Purchasing: `purchasing` / `purchasing123`
  - Finance: `finance` / `finance123`
- **First Admin bootstrap (production):** Created only while no active Admin exists from `Bootstrap__AdminUsername` and either `Bootstrap__AdminPassword` or `Bootstrap__AdminPasswordFile` (e.g. Docker secret). Minimum 12 characters; forced password change at first sign-in (`must_change_password`). Passwords are never logged. The Demo 1 Manager keys (`Bootstrap__ManagerUsername`/`Password`) are retired.
- **Account lockout:** 5 consecutive failed sign-in attempts lock the account for 15 minutes. Audited (`auth.locked`); an Admin can unlock the account via user administration.
- **Settings pages (Admin):**
  - **تنظیمات ← کاربران و نقش‌ها** (`#/settings/users`): user creation, role assignment, password reset, unlock, activate/deactivate.
  - **تنظیمات ← گزارش رویدادها** (`#/settings/audit`): append-only audit trail filterable by user, action and Jalali date range; server-paged, no secrets or report figures.
  - **تنظیمات ← کیفیت داده** (`#/settings/data-quality`): 7 issue types with counts, examples, affected reports, deferred sub-checks and rejected-at-sync counts.
  - **تنظیمات ← همگام‌سازی داده** (`#/settings/sync`): manual sync trigger, status and history.
- **Data Quality earliest date:** `DataQuality__EarliestJalaliDate` (default `1390-01-01`), set on-site to the start date of Mabna's first fiscal year. Documents dated earlier are flagged.
- **Spec:** [`specs/005-permissions-audit-quality/`](specs/005-permissions-audit-quality/).

## Release 1, Phase 4: Local AI over Purchasing

- **Local default:** The AI assistant runs by default on a local model on the company server (`LocalAi__*`), with an optional sibling Ollama compose file (`deploy/docker-compose.ollama.yml`).
- **Purchasing Assistant (`#/assistant`):** Available to users with the `ai.query` permission (`management`, `purchasing`, `finance`). Answers Persian purchasing questions citing period, filters, and source metric from allowlisted read-only tools.
- **Admin AI settings (`#/settings/ai`):** Administrators view redacted provider status, run connection tests, and can opt in to cloud AI only with a recorded owner-approval note (`ai.cloud.approval_note`).
- **Benchmark:** Model selection procedure and records in [`contracts/benchmark.md`](specs/006-local-ai-purchasing/contracts/benchmark.md) and [`quickstart.md`](specs/006-local-ai-purchasing/quickstart.md).
- **Spec:** [`specs/006-local-ai-purchasing/`](specs/006-local-ai-purchasing/).

## Release 1, Phase 5: AI Reports, Saved Reports, and Excel Export

- **Dynamic AI Reports (F14):** Ranking and comparison questions return a server-built Persian table and optional bar chart (inline SVG, at most 20 categories); canonical top-10 products over the last six Jalali months matches live reports.
- **Saved Reports (`#/saved`, F15):** Users save any standard report (Save) or AI-generated report (Keep) as a named query specification (not a row snapshot), with a cap of 50 saved reports per user and overwrite confirmation. Opening re-runs on current synced data with current permissions. Generated saved reports require `ai.query` to save and open.
- **Excel Export (F16):** Download the on-screen table as `.xlsx` (ClosedXML, RTL sheet, Persian headers, metadata, data-quality notice, demonstration label). Export is permitted for Management, Purchasing, and Finance; Admin does not hold export permission.
- **Print vs Export caps:** Print views cap at 1,000 rows (browser print); Excel exports cap at 10,000 rows with a truncation warning.
- **Audit trail:** `report.exported` on exports; `saved_report.{saved,replaced,opened,deleted}` on saved report actions; access denied logged without result rows.
- **Spec:** [`specs/007-ai-reports-export/`](specs/007-ai-reports-export/).

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
