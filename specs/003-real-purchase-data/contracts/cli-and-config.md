# Contract: Reconciliation CLI and Server Configuration

## 1. Reconciliation command (FR-017, FR-018)

Runs inside the API project (same DI, same configuration, same connector guards); it does not start the web host.

```bash
dotnet run --project backend/Zharfa.Api -- reconcile purchasing --month 1405-08 [--vendor <sourceKey> | --product <sourceKey>] [--basis line|lineAndDocument]
# in Docker:
docker compose -f deploy/docker-compose.yml run --rm api reconcile purchasing --month 1405-08 --vendor V-102
```

- Requires `ConnectionStrings__Zharfa`, `ConnectionStrings__Mabna`, the allowlist, the source map and `Zharfa__DataMode` (same as the API).
- Requires at least one `succeeded` sync job; otherwise exits 2 with "no successful sync".
- Computes per metric (`net`, `gross`, `returns`, `vat`): system total (`mabna.fn_purchase_totals`) and source total (fresh guarded reads + `SourceTotalsCalculator`); exact decimal comparison.
- Writes one `reconciliation_run` row per metric; on any mismatch also writes `audit_log` `reconciliation.mismatch` (period, filter, metric — no amounts in production mode).
- Prints one JSON line to stdout:

```json
{"month":"1405-08","filter":{"kind":"vendor","value":"V-102"},"basis":"line","dataMode":"demo",
 "results":[{"metric":"net","system":"…","source":"…","difference":"0.0000","outcome":"pass"}, …],
 "outcome":"pass","signOffBlocked":false}
```

- An `unavailable` metric carries a `reason` (the availability code of contracts/api.md §3) and no amounts; it does not fail the run.
- In `production` mode the printed line omits `system`/`source`/`difference` unless `--show-amounts` is passed (real totals stay on the on-site screen and database only; FR-017).
- Exit codes: `0` all metrics pass (unavailable metrics listed but do not fail), `1` any mismatch (`signOffBlocked: true`), `2` error (source unreachable, map invalid, no sync).

## 2. Configuration (environment; secrets never in repo or frontend)

| Key | Default | Notes |
|---|---|---|
| `Zharfa__DataMode` | **required** | `production` or `demo`; API refuses to start otherwise (research R12) |
| `Bootstrap__AdminUsername` / `Bootstrap__AdminPassword` | — | Production only, used once when `app_user` is empty; password ≥ 12 chars; never logged |
| `Bootstrap__ManagerUsername` / `Bootstrap__ManagerPassword` | — | Same rules |
| `ConnectionStrings__Mabna` | — | Existing (spec 002); read-only login |
| `MabnaSource__AllowlistFile` / `MabnaSource__AllowedObjects__N` | — | Existing; must contain every mapped object |
| `MabnaSource__CommandTimeoutSeconds` | 30 → **set 60** in compose | FR-011 default 60 s |
| `MabnaSync__SourceMapFile` | — | Path to the gitignored source map; unset → sync disabled ("source-not-configured") |
| `Sync__NightlyTime` | `02:00` | Local time `HH:mm` |
| `Sync__TimeZone` | `Asia/Tehran` | IANA id |
| `Sync__WeeklyFullLoadDay` | `Friday` | Weekly full load at the nightly time; `None` disables |
| `Sync__PageSize` | `5000` | Keyset page size, ≤ `MabnaSource__MaxRows` |
| `Sync__MaxRejectedRows` | `0` | Rows failing date/amount parsing allowed before the run fails |
| `Sync__FaultInjection__FailAfterTables` | — | Dev only: honoured only with `ASPNETCORE_ENVIRONMENT=Development` and demo mode |
| `Purchasing__DiscountBasis` | `line` | `line` or `lineAndDocument`; must match the map's verified basis in production (startup refusal if not) |

`deploy/docker-compose.yml` passes these through from `deploy/.env` (`ZHARFA_DATA_MODE` required with `:?`), sets `TZ=Asia/Tehran`, and mounts the source map and allowlist read-only from a gitignored path. `deploy/.env.example` documents each key with an empty value.
