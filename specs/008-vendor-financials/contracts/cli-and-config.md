# Contract: Vendor-balance reconciliation CLI (Phase 6)

Purchasing command in `specs/003-real-purchase-data/contracts/cli-and-config.md` is unchanged.

## Command

```bash
dotnet run --project backend/Zharfa.Api -- reconcile vendor-balance --month 1405-08 [--vendor <sourceKey>] [--show-amounts]
# Docker:
docker compose -f deploy/docker-compose.yml run --rm api reconcile vendor-balance --month 1405-08
```

Same process as `reconcile purchasing`: no web host; JSON line on stdout; logs on stderr.

| Exit | Meaning |
|---|---|
| 0 | pass (unavailable metrics listed, do not fail) |
| 1 | mismatch, `signOffBlocked: true` |
| 2 | error (no sync, bad map, source unreachable, log write failed) |

Requires `ConnectionStrings__Zharfa`, `ConnectionStrings__Mabna`, allowlist, source map, `Zharfa__DataMode`, at least one succeeded sync.

Compares system `mabna.fn_vendor_balance` to a guarded source re-read using the same mix. Writes `reconciliation_run` (`metric=vendor_balance`). Mismatch → `audit_log` `reconciliation.mismatch` (period, filter, metric; **no amounts in production** unless `--show-amounts` on stdout only).

```json
{
  "month": "1405-08",
  "filter": { "kind": "vendor", "value": "V-102" },
  "dataMode": "demo",
  "results": [
    {
      "metric": "vendor_balance",
      "system": "123.0000",
      "source": "123.0000",
      "difference": "0.0000",
      "outcome": "pass"
    }
  ],
  "outcome": "pass",
  "signOffBlocked": false
}
```

Production omits `system` / `source` / `difference` unless `--show-amounts`. On-site: one real month; only pass/fail may be copied into `specs/008-vendor-financials/source-verification.md`.

If the formula is unusable: `outcome: unavailable`, `reason: formula-unconfirmed` (or the availability code), exit 0 — not a false pass for sign-off. Phase 6 sign-off still requires a **pass** on the simulator (candidate mix) and a production pass once confirmed.

## Config

No new env keys. Financial objects join `MabnaSource__AllowlistFile`. Map path remains `MabnaSync__SourceMapFile`.
