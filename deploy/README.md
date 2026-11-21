# Deploy — Docker Compose (Demo 1 and Release 1, Phase 1)

Services: `db` (PostgreSQL 18), `api` (ASP.NET Core), `web` (static frontend +
nginx reverse proxy for `/api`).

## Quick start (helper scripts)

Copy the env file once and set `POSTGRES_PASSWORD` and `ZHARFA_DATA_MODE`, then use the helper for your OS:

```bash
cp deploy/.env.example deploy/.env       # Windows: copy deploy\.env.example deploy\.env
```

| Action | macOS / Linux | Windows (PowerShell) |
| --- | --- | --- |
| Start (build if needed) | `./deploy/zharfa.sh up` | `.\deploy\zharfa.ps1 up` |
| Stop (keeps data) | `./deploy/zharfa.sh down` | `.\deploy\zharfa.ps1 down` |
| Rebuild and restart | `./deploy/zharfa.sh restart` | `.\deploy\zharfa.ps1 restart` |
| Status | `./deploy/zharfa.sh status` | `.\deploy\zharfa.ps1 status` |
| API logs | `./deploy/zharfa.sh logs` | `.\deploy\zharfa.ps1 logs` |
| Delete database (asks first) | `./deploy/zharfa.sh reset` | `.\deploy\zharfa.ps1 reset` |

From the repository root, `make up` and `make down` run the same helper
(`make up` = `./deploy/zharfa.sh up`, `make down` = `./deploy/zharfa.sh down`).

`down`, `status` and `reset` keep working even when `deploy/.env` is incomplete
(for example `ZHARFA_DATA_MODE` not yet set): they never start containers, so
the helper supplies placeholders. `up` still requires a complete `deploy/.env`
and fails loudly when a required value is missing.

If PowerShell blocks the script, run it once as
`powershell -ExecutionPolicy Bypass -File .\deploy\zharfa.ps1 up`.

The helpers include `deploy/docker-compose.9router.yml` automatically when that
file exists (the local 9router network override).

## Install (manual)

```bash
cp deploy/.env.example deploy/.env   # then set POSTGRES_PASSWORD, ZHARFA_DATA_MODE, and the AI provider values
docker compose -f deploy/docker-compose.yml up --build -d
curl -s http://localhost:8080/api/health   # proxied to the API health endpoint
```

Open http://localhost:8080. What you see depends on the data mode (below): the
demonstration data in `demo`, the real purchasing data in `production`.

## Data mode (FR-020, FR-021, FR-022)

`ZHARFA_DATA_MODE` is **required** in `deploy/.env` and must be `production` or `demo`.
Compose refuses to start the stack without it, and the API refuses to start when it is missing or invalid.

- `production`: the company install. It shows real Mabna purchasing data only. Demo pages,
  the synthetic seed and the AI chat are hidden in the UI and refused by the API (404). Startup
  is refused if the synthetic seed was ever loaded into this database.
- `demo`: the synthetic demonstration install. Every page, report and AI answer is labeled as demonstration data.
  Seeds (demo accounts, synthetic data) apply here only.

The mode is stored in the database on first start. A later start with a different
`ZHARFA_DATA_MODE` is refused: an existing install cannot be flipped from the outside.
Use a fresh database (`./deploy/zharfa.sh reset`, which deletes data) to change it.

### Demo accounts (demo mode only)

Seed demonstration accounts (documented demonstration credentials, change before any real use):
- Administrator: `admin` / `admin123` (role `admin`: overview, purchasing reports, purchasing transactions, admin)
- Management: `manager` / `manager123` (role `management`: overview, purchasing reports, purchasing transactions, vendor financials, AI query, export)
- Purchasing: `purchasing` / `purchasing123` (role `purchasing`: overview, purchasing reports, purchasing transactions, AI query, export)
- Finance: `finance` / `finance123` (role `finance`: overview, purchasing reports, purchasing transactions, vendor financials, AI query, export)

### Production bootstrap accounts & Docker secret

In `production`, the first Admin comes from the environment or a secret file. It is created only while no active Admin exists:
`Bootstrap__AdminUsername` and either `Bootstrap__AdminPassword` or `Bootstrap__AdminPasswordFile` (a file path).
The password must be at least 12 characters. The first Admin must change the password at the first sign-in (`must_change_password`).
Missing or short values refuse startup. The passwords are never logged. Leave them blank in demo installs.
The Demo 1 Manager bootstrap keys (`Bootstrap__ManagerUsername` / `Bootstrap__ManagerPassword`) are retired and removed.

**Docker secret example:**
To supply the initial admin password via Docker secrets without embedding it in environment files, define a secret in `docker-compose.yml`:
```yaml
services:
  api:
    secrets:
      - zharfa_admin_password
    environment:
      Bootstrap__AdminUsername: admin
      Bootstrap__AdminPasswordFile: /run/secrets/zharfa_admin_password

secrets:
  zharfa_admin_password:
    file: ./secrets/admin_password.txt  # file with >= 12 characters password, trailing newline stripped
```

### Account lockout

5 consecutive failed sign-in attempts lock the account for 15 minutes.
Lockout events are audited (`auth.locked`). An Admin can unlock a locked account prematurely from the **کاربران و نقش‌ها** settings page.

### Settings pages (Admin only)

- **تنظیمات ← کاربران و نقش‌ها** (`#/settings/users`): user creation, role assignments, password reset, unlock, activate/deactivate.
- **تنظیمات ← گزارش رویدادها** (`#/settings/audit`): append-only audit trail filterable by user, action and Jalali date range; server-paged, no secrets or report figures.
- **تنظیمات ← کیفیت داده** (`#/settings/data-quality`): 7 issue types with counts, examples, affected reports, deferred sub-checks and rejected-at-sync counts.
- **تنظیمات ← همگام‌سازی داده** (`#/settings/sync`): manual sync trigger, status and job logs.

## Purchasing sync (Mabna, read-only)

Real purchasing data reaches the install through a read-only sync. The API reads Mabna
through the read-only login only, writes its copy to PostgreSQL (`mabna` schema), and never writes to Mabna.
Reports read the synced copy, never Mabna per request.

- **Nightly run**: an incremental sync at `Sync__NightlyTime` (default `02:00`, `Sync__TimeZone` default `Asia/Tehran`).
  The weekly full load runs on `Sync__WeeklyFullLoadDay` (default `Friday`; `None` disables it).
- **Sync now**: an Admin starts a run from the **تنظیمات ← همگام‌سازی داده** page (`POST /api/admin/sync/run`).
  Only one run can be active; a second request is answered 409.
- **Status**: the Data sync page lists the runs, their row counts per table and the last successful sync.
  Error text is scrubbed of connection strings, hosts and database names.
- **Source map and allowlist**: the real purchasing source map and the table allowlist are never committed.
  Put them in `deploy/mabna-config/` (gitignored except `.gitkeep`). Compose mounts that folder read-only at
  `/etc/zharfa/mabna`, and `MabnaSync__SourceMapFile` and `MabnaSource__AllowlistFile` point at the container paths.
  Unset `MabnaSync__SourceMapFile` keeps sync disabled (`source-not-configured`).
- **Discount basis**: `Purchasing__DiscountBasis` (`line` or `lineAndDocument`) must equal the basis verified in the
  source map. In `production` a mismatch refuses startup. See `docs/metrics/purchasing.md`.
- **Time zone**: the container runs in `TZ=Asia/Tehran`, matching the default `Sync__TimeZone`.

## Reconciliation CLI (FR-017, FR-018)

The reconciliation compares each purchasing total (net, gross, returns, VAT) with the same query run on the
source data. A mismatch blocks sign-off. It runs in the API image and does not start the web host:

```bash
docker compose -f deploy/docker-compose.yml run --rm api reconcile purchasing --month 1405-08
docker compose -f deploy/docker-compose.yml run --rm api reconcile purchasing --month 1405-08 --vendor <sourceKey>
docker compose -f deploy/docker-compose.yml run --rm api reconcile purchasing --month 1405-08 --basis lineAndDocument
```

- It needs at least one succeeded sync, `ConnectionStrings__Mabna`, the allowlist, the source map and `ZHARFA_DATA_MODE`.
- It prints one JSON line on stdout. In `production` the amounts are left out unless `--show-amounts` is given, so real
  totals stay on the screen and in the database.
- Exit codes: `0` all metrics pass (unavailable metrics are listed but do not fail), `1` a mismatch (sign-off blocked),
  `2` an error (source unreachable, invalid map, no successful sync).

## Development override (simulator data only)

`deploy/docker-compose.purchasing-dev.yml` runs the API in `Development` with `demo` data against the offline Mabna
simulator (`deploy/mabna-sim/`, synthetic data). It mounts the simulator's map and allowlist under
`/etc/zharfa/mabna-sim`, a folder separate from the production `/etc/zharfa/mabna` mount:

```bash
docker compose -f deploy/docker-compose.mabna-sim.yml up -d
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.purchasing-dev.yml up --build -d
```

Never use this override against real Mabna or a production install.

### Configuration & environment variables

All configuration is supplied via environment variables (`deploy/.env` or server env). `deploy/.env.example`
documents every key with an empty value:
- `ZHARFA_DATA_MODE`: required, `production` or `demo` (see Data mode above).
- `Bootstrap__AdminUsername`, `Bootstrap__AdminPassword`, `Bootstrap__AdminPasswordFile`: production first Admin (see above).
- `ZHARFA_MABNA_CONFIG_DIR`: host folder with the source map and allowlist (default `./mabna-config`, i.e. `deploy/mabna-config/`).
- `ConnectionStrings__Mabna`: read-only Mabna login. Blank keeps the connector disabled. Never commit it.
- `MabnaSource__AllowlistFile`, `MabnaSource__CommandTimeoutSeconds` (default `60`): the read allowlist and per-query timeout.
- `MabnaSync__SourceMapFile`: container path of the purchasing source map.
- `Sync__NightlyTime` (`02:00`), `Sync__TimeZone` (`Asia/Tehran`), `Sync__WeeklyFullLoadDay` (`Friday`), `Sync__PageSize` (`5000`, at most `MabnaSource__MaxRows`), `Sync__MaxRejectedRows` (`0`). Blank means the default.
- `Purchasing__DiscountBasis`: `line` (default) or `lineAndDocument`.
- `DataQuality__EarliestJalaliDate`: default `1390-01-01`. Set on-site to the start date of Mabna's first fiscal year. Purchase documents dated earlier are flagged.
- `AI_PROVIDER_NAME`: Display name of the cloud AI provider (e.g. OpenAI).
- `AI_PROVIDER_ENDPOINT`: API base URL including any `/v1` path (e.g. `https://api.openai.com/v1`).
- `AI_PROVIDER_MODEL`: Model identifier (e.g. `gpt-4o-mini`).
- `AI_PROVIDER_API_KEY`: API secret key.
  * Note: Provider configuration is env-only; an admin-only redacted status endpoint (`GET /api/admin/provider`, demo mode only in this phase) returns `name`, `endpoint`, `model`, and `configured` status, and NEVER the secret key.
  * The local Ollama seam is an interface only (never implemented in Demo 1).
- `INVENTORY_LOW_STOCK_THRESHOLD`: Optional threshold for low-stock inventory alerts (default `10`, one server-side threshold for all products).
- `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`: PostgreSQL connection credentials.
- `ZHARFA_PORT`: Published port for the reverse proxy (default `8080`).

## Migration & seed behavior

- Migrations run automatically at API startup:
  * Forward-only SQL migrations located in `db/migrations`.
  * Tracked in the `schema_migrations` table.
  * Each migration file runs inside its own database transaction.
  * The API fails fast on any migration error and halts startup.
- Seeds (`db/seed`) apply only in `demo` mode, and only when the `app_user` table is empty. `production` never applies them.
- NO manual DB edits and NO Mabna access at any point outside the read-only sync and the reconciliation (FR-014).

## Restore / backup

```bash
docker compose -f deploy/docker-compose.yml exec db pg_dump -U zharfa -d zharfa > zharfa.sql   # backup
docker compose -f deploy/docker-compose.yml exec -T db psql -U zharfa -d zharfa < zharfa.sql   # restore
```

A documented PostgreSQL backup + restore procedure must be RUN AND VERIFIED ONCE
on a Docker host before Demo 1 sign-off (FR-018) with the result recorded by the
tester — do not claim it was run here (there is no Docker daemon on this host) (PRD §7.7).

## Local AI over Purchasing (Release 1, Phase 4)

- The assistant runs by default on a local model on the company server (`LocalAi__*`).
- Optional sibling Ollama container:
  ```bash
  docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.ollama.yml --profile ollama up -d
  ```
- Admins configure AI providers and run connection tests via `#/settings/ai` (`GET/POST /api/admin/ai*`).
- The default model is selected via the documented benchmark procedure:
  * Benchmark procedure & criteria: [contracts/benchmark.md](../specs/006-local-ai-purchasing/contracts/benchmark.md)
  * Pinned 7–8B candidate models: `qwen2:7b`, `qwen2.5:7b`, `llama3.1:8b` (research R24).
  * Benchmark runner script: `specs/006-local-ai-purchasing/scripts/run-benchmark.py`
  * Sample benchmark record: [specs/006-local-ai-purchasing/benchmark/SAMPLE.md](../specs/006-local-ai-purchasing/benchmark/SAMPLE.md)
  * Set `LOCAL_AI_MODEL` in `deploy/.env` to the benchmark winner (highest accuracy across the 20-question fixture; ties broken by faster median response time).
- Development outbound capture: set `AiProvider__CapturePath` in `deploy/.env` to record outbound chat-completions JSON for verification against the 9router overlay.

## Desktop Shell

The desktop shell connects to this deployment by setting:
`ZHARFA_FRONTEND_URL=http://localhost:8080` for the WebView2 shell.

## Vendor Financials & Balance Reconciliation (Release 1, Phase 6)

- Financials UI (`#/financials`) is visible only to roles with `vendor.financials` (`management`, `finance`).
- Run vendor balance reconciliation against synced Mabna data:
  ```bash
  docker compose -f deploy/docker-compose.yml run --rm api reconcile vendor-balance --month 1405-08
  ```
- Demo mode allows `candidate` balance formulas for simulation. Production requires accounting-confirmed formulas; unconfirmed candidate formulas return «داده در دسترس نیست».

## Notes

- Nothing here connects to any legacy system unless `ConnectionStrings__Mabna` is set in `deploy/.env`; left blank, the Mabna connector and the purchasing sync stay disabled.
- The API container mounts `deploy/mabna-config/` (gitignored except `.gitkeep`) read-only at `/etc/zharfa/mabna`. It holds the purchasing source map and the allowlist; their contents never enter git.
- AI keys and DB credentials live in `deploy/.env` / Docker secrets only.
- Release 1 deploys to the company LAN server only (HTTPS, no remote access).
