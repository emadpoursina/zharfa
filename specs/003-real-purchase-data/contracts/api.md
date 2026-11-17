# HTTP API Contract: Real Purchase Data (Phase 1)

Base path `/api`, cookie session (`zharfa_session`) from `POST /api/auth/login`, JSON UTF-8. Every error uses the existing Persian envelope `{ "message": "..." }`. Status codes: 401 not signed in, 403 role not allowed, 404 unknown route **or module not present in this data mode**, 409 conflict, 400 bad input. All authorization is server-side.

Every data-bearing response includes:

```json
"dataLabel": "real" | "demo",
"lastSuccessfulSync": { "at": "2026-11-16T02:04:11+03:30", "atJalali": "۱۴۰۵/۰۸/۲۵ ۰۲:۰۴" } | null
```

Amounts are JSON **strings** with up to 4 decimals (exact, no float), in Rial. An unavailable figure is `null` with its key listed in `unavailable` and a reason code.

---

## 1. `GET /api/system/status`

Roles: any signed-in user. Purpose: data mode, last sync, which modules exist (drives navigation and labels).

**200**

```json
{
  "dataMode": "production",
  "dataLabel": "real",
  "lastSuccessfulSync": { "at": "...", "atJalali": "..." },
  "modules": { "overview": true, "dataSync": true, "demoPages": false, "ai": false },
  "serverJalaliToday": "1405-08-25"
}
```

`modules.dataSync` is true only for `admin`. `modules.demoPages` and `modules.ai` are true only in demo mode (AI false in production until Phase 4). `lastSuccessfulSync` is `null` when no run has succeeded.

## 2. `GET /api/health` (existing, changed)

Anonymous. `demoData` now reflects the data mode (`true` only in demo mode). No other fields added.

## 3. `GET /api/overview/purchasing?month=YYYY-MM`

Roles: `manager`, `admin`. `month` is a Jalali month (`1405-08`, Latin or Persian digits accepted). Missing → current Jalali month (server time zone). Invalid (bad format, month ∉ 1..12) → 400. No other filters in this phase.

**200**

```json
{
  "month": "1405-08",
  "monthLabel": "آبان ۱۴۰۵",
  "dataLabel": "real",
  "lastSuccessfulSync": { "at": "...", "atJalali": "..." },
  "currency": "IRR",
  "currencyLabel": "ریال",
  "discountBasis": "line",
  "totals": {
    "net": "1250000000.0000",
    "gross": "1300000000.0000",
    "returns": "50000000.0000",
    "vat": "117000000.0000",
    "vatInvoice": "121500000.0000",
    "vatReturn": "4500000.0000"
  },
  "comparison": {
    "previousMonth": "1405-07",
    "previousNet": "1100000000.0000",
    "delta": "150000000.0000",
    "percent": "13.64"
  },
  "trend": [
    { "month": "1404-09", "net": null, "available": false },
    { "month": "1405-08", "net": "1250000000.0000", "available": true }
  ],
  "activeVendors": 37,
  "topVendors": [
    { "key": "V-102", "name": "شرکت دارویی نمونه", "net": "300000000.0000", "unknown": false },
    { "key": null, "name": "نامشخص", "net": "1200000.0000", "unknown": true }
  ],
  "topProducts": [ { "key": "P-77", "name": "...", "net": "...", "unknown": false } ],
  "unavailable": [ { "figure": "vat", "reason": "source-item-unverified" } ]
}
```

Rules:
- `trend` always has 12 entries, oldest first, ending at `month`.
- `comparison.percent` is `null` when `previousNet` is `"0.0000"`; the whole `comparison` is `null` and listed in `unavailable` (`reason: "no-previous-data"`) when the preceding month is before the first synced month.
- A month with no purchases returns zeros, not `unavailable`.
- No successful sync yet: every figure `null`, `unavailable` lists all with `reason: "no-successful-sync"`, `lastSuccessfulSync: null`.
- `topVendors`/`topProducts` have at most 5 entries; fewer when fewer exist.
- Reason codes: `no-successful-sync`, `source-item-unverified`, `source-item-unmapped`, `no-previous-data`.
- Values equal `reconcile purchasing` system totals for the same month (FR-015).

## 4. Data sync (Admin only)

All under `[Authorize(Roles = "admin")]`. Manager → 403, anonymous → 401.

### 4.1 `POST /api/admin/sync/run`

Body: none (always `incremental`; the first run ever is promoted to `full` automatically).

- **202** `{ "job": SyncJob }` — run started in the background.
- **409** `{ "message": "یک همگام‌سازی در حال اجراست.", "job": SyncJob }` — a run is already active; no new run.
- **503** `{ "message": "...", "reason": "source-not-configured" | "source-map-invalid" | "login-not-read-only" }` — sync cannot start; nothing recorded except an `audit_log` entry.

Audit: `sync.requested` (actor, job id) or `sync.rejected` (reason).

### 4.2 `GET /api/admin/sync/jobs?limit=50`

**200** `{ "jobs": [SyncJob, ...], "running": SyncJob | null, "source": { "configured": true, "mapValid": true, "mapProblems": [] }, "schedule": { "nightlyTime": "02:00", "timeZone": "Asia/Tehran", "nextRunAt": "...", "nextRunAtJalali": "..." } }` — newest first, `limit` 1..200 (default 50).

### 4.3 `GET /api/admin/sync/jobs/{id}`

**200** `SyncJob` with `tables` filled. **404** unknown id.

### SyncJob

```json
{
  "id": 42,
  "trigger": "manual" | "scheduled" | "startup-recovery",
  "mode": "incremental" | "full",
  "status": "running" | "succeeded" | "failed" | "skipped",
  "requestedBy": "admin" | null,
  "startedAt": "...", "startedAtJalali": "...",
  "finishedAt": "..." | null, "finishedAtJalali": "..." | null,
  "durationMs": 8312 | null,
  "error": "منبع داده در دسترس نیست (Mabna read timed out)." | null,
  "skipReason": null,
  "tables": [
    { "table": "purchase_invoice", "read": 1200, "inserted": 40, "updated": 3, "deleted": 0, "rejected": 0 }
  ]
}
```

`error` never contains connection strings, host, login or database names (scrubbed with `RedactionHelper`).

## 5. Demo-only and Phase-4 endpoints (behaviour change)

In **production** mode the following return **404** with `{ "message": "این بخش در این نسخه فعال نیست." }` for every role, before any handler code or database query runs (endpoint filter `[DemoOnly]`):

- `/api/sales/*`, `/api/inventory/*`, `/api/receivables/*`, `/api/meta/categories`
- `/api/admin/reconciliation` (Demo 1 synthetic reconciliation)
- `/api/admin/provider` (redacted AI provider status, admin only). It is part of the AI surface: FR-023 hides and refuses AI in production until Phase 4, and this status describes that provider, so it is refused with the AI routes. The decision is recorded in spec 003 tasks T110; the sync routes under `/api/admin/sync/*` stay available in production.
- `/api/ai/*` (until Phase 4 adds AI over real data)

In **demo** mode they behave as in Demo 1, and every AI answer payload carries `"dataLabel": "demo"` and the Persian demonstration notice.

## 6. Negative authorization matrix (tester)

| Request | anonymous | manager | admin |
|---|---|---|---|
| `GET /api/system/status` | 401 | 200 | 200 |
| `GET /api/overview/purchasing` | 401 | 200 | 200 |
| `POST /api/admin/sync/run` | 401 | 403 | 202/409/503 |
| `GET /api/admin/sync/jobs` | 401 | 403 | 200 |
| `GET /api/sales/overview` (production) | 401 | 404 | 404 |
| `POST /api/ai/chat` (production) | 401 | 404 | 404 |

Authentication runs first (anonymous → 401), then the `[DemoOnly]` filter (signed-in user in production → 404, no handler or query executed).
