# Quickstart and Validation Flows: Real Purchase Data (Phase 1)

**Feature**: `specs/003-real-purchase-data` | **Contracts**: [api.md](contracts/api.md), [source-map.md](contracts/source-map.md), [cli-and-config.md](contracts/cli-and-config.md) | **Model**: [data-model.md](data-model.md)

These flows are the tester's end-to-end checks and the PR walkthrough script. Each flow lists its spec acceptance scenarios and the pass condition. All flows run against the **simulator** (synthetic data); Flow 11 is the on-site run.

## 0. Prerequisites and setup

- Docker Desktop (Rosetta enabled on Apple Silicon, ≥ 4 GB RAM for SQL Server), .NET 10 SDK, Node 20+.
- Simulator built per `deploy/mabna-sim/README.md`, regenerated with the purchasing roots and scenario (research R3). `deploy/mabna-sim/generated/` is gitignored; in a fresh worktree copy it from the main checkout:
  `cp -R ../../../zharfa/deploy/mabna-sim/generated/. deploy/mabna-sim/generated/` (path relative to the worktree; adjust to your layout).
- `deploy/mabna-sim/generated/purchase-source-map.json` and `deploy/mabna-sim/generated/allowlist.txt` exist (gitignored) and list the simulator's purchasing tables.
- `deploy/.env` (gitignored) has `POSTGRES_PASSWORD`, `MABNA_SIM_SA_PASSWORD`, `MABNA_SIM_RO_PASSWORD`, `ZHARFA_DATA_MODE=demo`, and `ConnectionStrings__Mabna=Server=host.docker.internal,14330;Database=TotalSystem;User Id=zharfa_ro;Password=…;TrustServerCertificate=True`.

```bash
docker compose -f deploy/docker-compose.mabna-sim.yml up -d
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.purchasing-dev.yml up --build -d   # dev override: Development env, map + allowlist mounts
curl -s localhost:8080/api/health            # {"status":"ok",...,"demoData":true}
```

Helper used below (cookie jars per role):

```bash
login() { curl -s -c /tmp/z_$1 -H 'Content-Type: application/json' -d "{\"username\":\"$1\",\"password\":\"$2\"}" localhost:8080/api/auth/login >/dev/null; }
login admin admin123; login manager manager123      # demo seed accounts (demo mode only)
A="-b /tmp/z_admin"; M="-b /tmp/z_manager"
psql_q() { docker compose -f deploy/docker-compose.yml exec -T db psql -U zharfa -d zharfa -At -c "$1"; }
```

## 1. Validation suite (no DB, no network)

```bash
npm --prefix frontend run build
dotnet build backend/Zharfa.Api/Zharfa.Api.csproj
dotnet test backend/Zharfa.Api.Tests
```

Pass: all green. Covers guard/allowlist (US3), source-map validation, Jalali parsing incl. Esfand 30 of 1403, ي/ك normalization, incremental planner, discount basis and allocation, `SourceTotalsCalculator`, reconciliation comparator, comparison with zero base, `[DemoOnly]` filter decisions.

## Flow 1 — Connector refuses writes and non-allowlisted tables (US3 AC1–AC3, SC-007)

```bash
dotnet test backend/Zharfa.Api.Tests --filter "FullyQualifiedName~Mabna"
```

Pass: tests for INSERT/UPDATE/DELETE/MERGE/EXEC/DDL, `scuUser`, `UserRights`, a `scu*` and a `msg*` name, a mapped table missing from the allowlist, and a map entry with `hostFiltered: true` all refuse **before** any connection is opened (the fake connection factory records zero opens).

## Flow 2 — Admin "Sync now" (US2 AC1, AC6, SC-002)

1. UI: sign in as `admin` → Settings → Data sync → click «همگام‌سازی اکنون». The row appears as running and turns succeeded without leaving the page (polling).
2. API equivalent:

```bash
curl -s -X POST $A localhost:8080/api/admin/sync/run            # 202, job.status=running, job.mode=full (first run)
sleep 10; curl -s $A localhost:8080/api/admin/sync/jobs | jq '.jobs[0] | {status,mode,durationMs,tables}'
```

Pass: `status=succeeded`, `durationMs` set, `tables` has `vendor`, `product`, `purchase_invoice`, `purchase_return`, `purchase_line` with `read` equal to the simulator's row counts for the mapped tables/filters (compare with `sqlcmd` `SELECT COUNT(*)` as `zharfa_ro` in the sim container). The header on every page shows the last-sync time in Jalali with Persian digits.

## Flow 3 — No second concurrent run (US2 AC2)

```bash
curl -s -X POST $A localhost:8080/api/admin/sync/run & curl -s -w '%{http_code}\n' -X POST $A localhost:8080/api/admin/sync/run
psql_q "select count(*) from sync_job where status='running'"
```

Pass: one request 202, the other 409 with the running `job`; never more than 1 running row. (If the sim run is too fast, set `Sync__FaultInjection__FailAfterTables` unset and `Sync__PageSize=50` in the dev override to lengthen it.)

## Flow 4 — Forced failure keeps previous data (US2 AC3, FR-010, SC-003)

```bash
psql_q "select * from mabna.fn_sync_fingerprint()" > /tmp/fp_before
# (a) source unreachable
docker compose -f deploy/docker-compose.mabna-sim.yml stop mabna-sim
curl -s -X POST $A localhost:8080/api/admin/sync/run; sleep 20
curl -s $A localhost:8080/api/admin/sync/jobs | jq '.jobs[0] | {status,error}'
psql_q "select * from mabna.fn_sync_fingerprint()" > /tmp/fp_after; diff /tmp/fp_before /tmp/fp_after
docker compose -f deploy/docker-compose.mabna-sim.yml start mabna-sim
# (b) mid-run failure (dev override sets Sync__FaultInjection__FailAfterTables=2)
```

Pass: job `failed` with a Persian error that names no host/login/database; `diff` is empty; Overview still shows the previous figures and the previous last-sync time. For (b): two tables were read before the failure, still `diff` empty and watermarks in `source_record` unchanged.

## Flow 5 — Nightly schedule (US2 AC4, FR-007)

Set `Sync__NightlyTime` to two minutes from now (local `Asia/Tehran`) in `deploy/.env`, recreate `api`, wait.

```bash
psql_q "select trigger,mode,status from sync_job order by id desc limit 1"   # scheduled|incremental|succeeded
```

Pass: a `scheduled` run succeeded without any Admin action and is listed on the Data sync page. Then start a manual run and set the schedule to fire during it: the scheduled job is recorded `skipped` with `skip_reason`.

## Flow 6 — Incremental equals full (US2 AC5, FR-006)

1. As `sa` **in the simulator only**, make these changes (scripts in `deploy/mabna-sim/generated/src/purchasing-changes.sql`, gitignored):
   - add a purchase invoice with lines, edit one recent invoice's line amount, delete one recent return;
   - **old-document check (US2 AC5, Phase 13):** cancel (`IsCanceled=1`) one purchase invoice dated more than 60 days before the run, and delete one purchase return dated more than 60 days before the run. Neither is inside any former window; both must still reach the copy.
2. Run Sync now (incremental). Record `mabna.fn_sync_fingerprint()`. The job's `purchase_invoice` must show `updated > 0` (the cancelled old invoice is among them) and `purchase_return` must show `deleted > 0` (the old return is among them), in addition to the recent changes.
3. Bring up a second, empty Postgres (`docker compose -p zharfa-full …`) with the same configuration and run one sync there (it is a full load). Record its fingerprint.

Pass: both fingerprints are identical; the incremental job's `tables` show non-zero `inserted`/`updated`/`deleted` only where changes were made, and the old-document rows of step 1 are reflected (the cancelled invoice is `cancelled = true` and the deleted return is absent from `mabna.purchase_document`).

## Flow 7 — Reconciliation on the simulator (US4 AC1, AC2, AC4–AC6, SC-004)

```bash
docker compose -f deploy/docker-compose.yml run --rm api reconcile purchasing --month 1405-07
docker compose -f deploy/docker-compose.yml run --rm api reconcile purchasing --month 1405-07 --vendor <a sim vendor key>
echo $?                                   # 0
psql_q "select metric,outcome,difference from reconciliation_run order by id desc limit 8"
```

Pass: exit 0, every metric `pass` with difference `0.0000`, rows logged. Then:
- Mismatch path: in the sim (as `sa`) change one line amount **without** syncing and rerun → exit 1, `signOffBlocked: true`, a `reconciliation.mismatch` row in `audit_log`. Sync again → exit 0.
- Overview equality: `curl -s $M "localhost:8080/api/overview/purchasing?month=1405-07" | jq .totals.net` equals the reconciliation `system` net.
- Normalization: a sim vendor seeded with `ي`/`ك` appears with `ی`/`ک` (`psql_q "select count(*) from mabna.vendor where name_fa ~ '[يك]'"` → 0).
- Month edges: the scenario's documents dated `1403/12/30` and `1404/01/01` fall in Esfand 1403 and Farvardin 1404 respectively (`reconcile --month 1403-12` and `1404-01` pass, and `fn_purchase_totals` counts each in its own month).
- Return in a later month than its invoice reduces net in the return's month.

## Flow 8 — Overview as Manager (US5 AC1–AC5, FR-025..FR-029, SC-005)

UI: sign in as `manager`, Overview opens by default. Check: net, gross, returns, VAT in «ریال» with Persian digits; comparison with the preceding month; 12-month trend; active vendors; top 5 vendors and products; last-sync time; demo label (demo mode). Change the month with the Jalali month picker: every figure changes.

API:

```bash
curl -s $M "localhost:8080/api/overview/purchasing?month=1405-07" | jq '{totals,comparison,activeVendors,n:(.topVendors|length),trend:(.trend|length),dataLabel}'
curl -s -o /dev/null -w '%{time_total}\n' $M "localhost:8080/api/overview/purchasing?month=1405-07"
curl -s $M "localhost:8080/api/overview/purchasing?month=1405-13"     # 400
```

Pass: `trend` length 12; ≤ 5 top entries; "unknown" bucket appears for the scenario's line without a vendor/product; an empty month returns zeros; the first synced month returns `comparison: null` with `no-previous-data`; response time < 2 s. Optional load check: `db/perf/purchasing-3y.sql` (throwaway database only) fills three years; p95 of 20 requests < 2 s.

"Data unavailable": set the VAT item to `unknown` in the dev map, restart `api` → Overview shows «داده در دسترس نیست» for VAT only; API lists `{"figure":"vat","reason":"source-item-unmapped"|"source-item-unverified"}`. Fresh DB with no successful sync → every figure unavailable and the header says no sync has completed.

## Flow 9 — Authorization (US2 AC7, FR-013, FR-028)

```bash
curl -s -o /dev/null -w '%{http_code}\n' -X POST $M localhost:8080/api/admin/sync/run    # 403
curl -s -o /dev/null -w '%{http_code}\n' $M localhost:8080/api/admin/sync/jobs           # 403
curl -s -o /dev/null -w '%{http_code}\n' localhost:8080/api/overview/purchasing          # 401
```

Pass: matches the matrix in [api.md § 6](contracts/api.md#6-negative-authorization-matrix-tester); the Manager UI shows no Data sync entry.

## Flow 10 — Data mode separation (US6 AC1–AC4, FR-020..FR-024, SC-006)

1. **Demo**: every page shows the demonstration banner; Overview payload `dataLabel: "demo"`; Demo pages (sales, inventory alerts, receivables) and AI chat are reachable; an AI answer carries the demonstration label.
2. **Production** (fresh database: `docker compose -p zharfa-prod … up` with `ZHARFA_DATA_MODE=production` and `Bootstrap__*` set; still pointed at the simulator for this test only):

```bash
# second stack on port 8081 (ZHARFA_PORT=8081), project zharfa-prod; sign in with the Bootstrap__Admin* account
P="docker compose -p zharfa-prod -f deploy/docker-compose.yml"
curl -s -c /tmp/z_padmin -H 'Content-Type: application/json' -d '{"username":"<bootstrap admin>","password":"<bootstrap pw>"}' localhost:8081/api/auth/login >/dev/null; PA="-b /tmp/z_padmin"
$P exec -T db psql -U zharfa -d zharfa -At -c "select count(*) from public.sales_invoice"     # 0 (synthetic seed not loaded)
$P exec -T db psql -U zharfa -d zharfa -At -c "select count(*) from app_user where username in ('manager','admin')"   # 0 unless the bootstrap names are these
$P exec -T db psql -U zharfa -d zharfa -At -c "select name from schema_migrations where name in ('0001_core.sql','0002_sales_cash_users.sql')"   # no rows
curl -s $PA localhost:8081/api/system/status | jq '{dataMode,modules}'   # production, demoPages=false, ai=false
for p in /api/sales/overview /api/inventory/alerts /api/receivables /api/meta/categories /api/admin/reconciliation; do curl -s -o /dev/null -w "$p %{http_code}\n" $PA localhost:8081$p; done   # all 404
curl -s -o /dev/null -w '%{http_code}\n' -X POST $PA -H 'Content-Type: application/json' -d '{"question":"x"}' localhost:8081/api/ai/chat   # 404
```

   UI: no demo banner, a «داده واقعی شرکت» badge, no Demo pages or AI chat in navigation.
3. **Mode is server-only**: restart the production stack with `ZHARFA_DATA_MODE=demo` → the API refuses to start (log: stored mode differs). Start with `ZHARFA_DATA_MODE` unset → refuses to start. No API accepts a mode change.

## Flow 11 — On-site (developer, real Mabna; US1, US4 AC3)

Follow `specs/002-mabna-accounting-connector/onsite-checklist.md` section "7. Purchasing slice" (added by this feature):

1. Verify each logical item with catalog metadata and owner-approved aggregate probes; set statuses in the on-site source map; record logical statuses in `specs/003-real-purchase-data/source-verification.md` (no real names, no rows).
2. Record the discount basis of Mabna's own purchase report total; set `Purchasing__DiscountBasis` and the map's `discountBasis` to match.
3. Production install, Sync now, then `reconcile purchasing --month <real month>` and the extended `scratch/mabna-control-totals.sql` for the same month.
4. Pass: exit 0 and control totals equal; record only pass/fail in `source-verification.md`. A mismatch blocks sign-off (FR-018).
5. `git status` clean of any Mabna output (checklist section 6).

## Exit-criteria map (zharfa-prd.phases.md#1)

| Exit criterion | Flow |
|---|---|
| Tables verified on-site and recorded | 11 |
| Sync now shows status, duration, row counts | 2, 3 |
| Forced failure keeps previous data | 4 |
| Nightly incremental without Admin | 5, 6 |
| Overview figures for a chosen Jalali month | 8 |
| API totals equal direct-source (sim + one real month) | 7, 11 |
| Production vs demo separation | 10 |
| Non-SELECT / non-allowlisted refused | 1 |
