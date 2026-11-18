# API Contract: Permissions, Audit, and Data Quality (Release 1, Phase 3)

All endpoints are under the existing API (`/api`, cookie session `zharfa_session`). Errors keep the envelope `{ "message": "<Persian>" }`; some add a machine `code`. Times: `at` = ISO 8601 with the sync time-zone offset, `atJalali` = `yyyy/mm/dd HH:mm` in Persian digits (existing `SyncTimeText` helpers). Data responses carry `dataLabel` (`real` | `demo`).

Status codes common to every protected endpoint:

| Case | Status | Body |
|---|---|---|
| no or invalid session, or user deactivated/removed | 401 | `{"message":"احراز هویت انجام نشده است یا نشست منقضی شده است."}` |
| signed in, permission missing | 403 | `{"message":"شما دسترسی لازم برای انجام این عملیات را ندارید.","code":"forbidden"}` + audit `access.denied` |
| signed in, must change password first | 403 | `{"message":"ابتدا باید گذرواژهٔ خود را تغییر دهید.","code":"password-change-required"}` (no audit) |

## §0. Endpoint → permission matrix (enforced by policies; mirrored by `PermissionMatrixTests`)

| Endpoint(s) | Policy | admin | management | purchasing | finance |
|---|---|:-:|:-:|:-:|:-:|
| `POST /api/auth/login`, `POST /api/auth/logout` | anonymous | ✓ | ✓ | ✓ | ✓ |
| `GET /api/auth/me`, `POST /api/auth/change-password`, `GET /api/system/status` | signed in | ✓ | ✓ | ✓ | ✓ |
| `GET /api/overview/purchasing` | `overview` | ✓ | ✓ | ✓ | ✓ |
| `GET /api/purchasing/vendors[/print]`, `vendor-profile`, `vendors/compare[/print]`, `lookup/{kind}`, `reports/{id}[/print]` (id ≠ transactions) | `purchasing.reports` | ✓ | ✓ | ✓ | ✓ |
| `GET /api/purchasing/documents[/print]`, `documents/{id}/lines`, `reports/transactions[/print]` | `purchasing.transactions` (+ class policy `purchasing.reports`) | ✓ | ✓ | ✓ | ✓ |
| `GET /api/vendor-financials/summary`, `GET /api/vendor-financials/vendors/{vendorKey}` | `vendor.financials` | 403 | ✓ | 403 | ✓ |
| `/api/admin/users/*`, `/api/admin/roles`, `/api/admin/audit*`, `/api/admin/data-quality`, `/api/admin/sync/*`, `GET /api/admin/provider`, `GET /api/admin/reconciliation` | `admin` | ✓ | 403 | 403 | 403 |
| `POST /api/ai/chat` (demo only, `[DemoOnly]`) | `ai.query` | 403 | ✓ (demo) | ✓ (demo) | ✓ (demo) |
| `GET /api/sales/*`, `/api/inventory/*`, `/api/receivables/*`, `/api/meta/*` (demo only) | `overview` | ✓ (demo) | ✓ (demo) | ✓ (demo) | ✓ (demo) |

`transactions` report: `reports/{reportId}` keeps class policy `purchasing.reports`; when `reportId == "transactions"` the action calls `IAuthorizationService.AuthorizeAsync(User, Permissions.PurchasingTransactions)` and on failure returns the same 403 body and writes `access.denied` through `AccessDenied.RespondAsync` (shared with the result handler). With today's matrix every role holds both permissions, so this path is defensive.

`[DemoOnly]` still runs after authorization: production answers 404 to a permitted caller (unchanged).

## §1. Authentication (`AuthController`)

### `POST /api/auth/login` (anonymous)

Request `{ "username": "…", "password": "…" }`. Lookup by `username_key` (research R4).

| Outcome | Status | Body |
|---|---|---|
| success | 200 | `Me` (below) |
| unknown user, inactive user, wrong password (< 5th) | 401 | `{"message":"نام کاربری یا گذرواژه اشتباه است."}` |
| locked, or this failure is the 5th in a row | 423 | `{"message":"حساب شما به‌دلیل ورودهای ناموفق پیاپی به‌طور موقت قفل شده است. پس از ۱۵ دقیقه دوباره تلاش کنید یا با مدیر سامانه تماس بگیرید.","code":"locked","lockedUntilJalali":"۱۴۰۵/۰۸/۲۷ ۱۰:۴۵"}` |

Audit per research R5. Never logs or audits the password.

### `POST /api/auth/logout` (anonymous allowed)

200 `{"message":"خروج با موفقیت انجام شد."}`; when a user was signed in, audit `auth.signout`.

### `GET /api/auth/me` (signed in) → `Me`

```json
{
  "id": 7,
  "username": "purchasing",
  "role": "purchasing",
  "roleLabel": "واحد خرید",
  "permissions": ["overview","purchasing.reports","purchasing.transactions","ai.query","export"],
  "mustChangePassword": false
}
```

### `POST /api/auth/change-password` (signed in; allowed while `mustChangePassword`)

Request `{ "currentPassword": "…", "newPassword": "…" }`. 204 on success (flag cleared, audit `auth.password_changed`). 400 `{"message":"گذرواژهٔ فعلی نادرست است."}` / `{"message":"گذرواژه باید حداقل ۸ نویسه باشد."}` / `{"message":"گذرواژهٔ جدید باید با گذرواژهٔ فعلی متفاوت باشد."}`.

## §2. Users and roles (`UsersAdminController`, policy `admin`)

### `GET /api/admin/roles`

```json
{ "roles": [ { "id": "admin", "label": "مدیر سامانه",
               "permissions": [ { "id": "overview", "label": "نمای کلی خرید" }, … ] }, … ] }
```

Exactly four roles in the order admin, management, purchasing, finance.

### `GET /api/admin/users`

```json
{
  "dataLabel": "demo",
  "users": [
    { "id": 2, "username": "admin", "role": "admin", "roleLabel": "مدیر سامانه",
      "active": true, "locked": false, "lockedUntilJalali": null,
      "mustChangePassword": false, "createdAtJalali": "۱۴۰۵/۰۸/۲۰ ۰۹:۱۲" }
  ]
}
```

Sorted by username. Never contains `password_hash` or `failed_signins`.

### `POST /api/admin/users` → 201 `User`

Request `{ "username": "…", "password": "…", "role": "purchasing" }`. 400 Persian messages for: username rules, duplicate username after normalization («این نام کاربری قبلاً ثبت شده است.»), password < 8, unknown role («نقش انتخاب‌شده معتبر نیست.»). Audit `user.created` (target user, detail `role`).

### `POST /api/admin/users/{id}/role` → 200 `User`

Request `{ "role": "finance" }`. 404 unknown user. 409 `{"message":"دست‌کم یک مدیر سامانهٔ فعال باید باقی بماند.","code":"last-admin"}`. Same role → 200, no audit. Otherwise audit `user.role_changed` (`from`, `to`).

### `POST /api/admin/users/{id}/password` → 204

Request `{ "password": "…" }`. 400 when < 8. Audit `user.password_reset` (no detail). Also clears a lockout? **No** (separate action).

### `POST /api/admin/users/{id}/deactivate` → 200 `User`

409 `last-admin` as above. Audit `user.deactivated`. The user's next request answers 401 (research R3).

### `POST /api/admin/users/{id}/reactivate` → 200 `User`

Resets `failed_signins` and `locked_until`. Audit `user.reactivated`.

### `POST /api/admin/users/{id}/unlock` → 200 `User`

Clears `failed_signins`, `locked_until`. Audit `user.unlocked`. Not locked → 200, no audit.

`{id}` of an unknown user → 404 `{"message":"کاربر موردنظر یافت نشد."}`.

## §3. Audit log (`AuditLogController`, policy `admin`)

### `GET /api/admin/audit/actions`

`{ "actions": [ { "code": "auth.signin.succeeded", "label": "ورود موفق" }, … ] }` — the catalog (data-model §2.2).

### `GET /api/admin/audit?user=&action=&from=&to=&page=&pageSize=`

| Param | Rule |
|---|---|
| `user` | exact `actor` (username) |
| `action` | exact code |
| `from`, `to` | Jalali `yyyy-mm-dd` (Latin or Persian digits), inclusive days in `Sync__TimeZone`; `to < from` → 400 «تاریخ پایان نمی‌تواند پیش از تاریخ شروع باشد.»; invalid date → 400 |
| `page` | ≥ 1, default 1 |
| `pageSize` | 1–100, default 50 |

```json
{
  "dataLabel": "demo",
  "total": 132, "page": 1, "pageSize": 50,
  "rows": [
    { "id": 981, "at": "2026-11-18T10:21:05+03:30", "atJalali": "۱۴۰۵/۰۸/۲۷ ۱۰:۲۱",
      "actor": "purchasing", "action": "access.denied", "actionLabel": "رد دسترسی",
      "target": "GET api/vendor-financials/summary", "detail": "permission=vendor.financials" }
  ]
}
```

Newest first. Empty match → `total: 0, rows: []` (200). First page (`page` absent or 1) writes `audit.viewed` with the filter values.

No `PUT`, `PATCH` or `DELETE` exists for audit entries (append-only, FR-016).

## §4. Data quality (`DataQualityController`, policy `admin`)

### `GET /api/admin/data-quality[?types=a,b]`

`types` only narrows `focus` for the UI; all types are always returned.

```json
{
  "dataLabel": "demo",
  "latestSync": { "id": 41, "status": "failed", "finishedAt": "…", "finishedAtJalali": "…", "error": "همگام‌سازی ناموفق بود: …" },
  "latestSyncFailed": true,
  "check": { "runId": 17, "syncJobId": 40, "checkedAt": "…", "checkedAtJalali": "…" },
  "latestCheckFailed": false,
  "focus": ["missing-values"],
  "issueTypes": [
    {
      "type": "missing-values",
      "label": "مقادیر ناقص",
      "description": "سند یا ردیف خرید بدون مقدار لازم (شماره سند، تأمین‌کننده، کالا یا مقدار).",
      "count": 4,
      "examples": [
        { "recordKind": "document", "documentNumber": null, "dateJalali": "۱۴۰۵/۰۷/۱۵",
          "vendor": "…", "product": null, "field": "number", "fieldLabel": "شماره سند" }
      ],
      "affectedReports": [ { "key": "overview", "label": "نمای کلی خرید" }, … ],
      "heuristic": null,
      "notChecked": []
    },
    {
      "type": "inconsistent-product-ids", "label": "شناسه‌های ناسازگار کالا", "count": 1, "examples": [ … ],
      "affectedReports": [ … ],
      "heuristic": "بررسی تقریبی: یک نام کالا با چند کد کالا. بررسی بر پایهٔ کد کالا پس از تأیید ستون‌های کد در محل فعال می‌شود.",
      "notChecked": []
    },
    {
      "type": "inconsistent-amounts", "label": "مبالغ ناسازگار", "count": 0, "examples": [],
      "affectedReports": [ … ],
      "heuristic": null,
      "notChecked": [ { "code": "line-net-mismatch", "reason": "ستون مبلغ خالص ردیف در نقشهٔ منبع تأیید نشده است." },
                      { "code": "document-total-mismatch", "reason": "ستون جمع سند در نقشهٔ منبع تأیید نشده است." } ]
    }
  ],
  "rejectedAtSync": [ { "table": "purchaseInvoice", "reason": "date-invalid", "count": 1 } ]
}
```

- `latestSync`: newest `sync_job` by id (null when none). `latestSyncFailed` = its status is `failed`.
- `check`: newest `succeeded` `data_quality_run` (null → the page says S-DQ-NO-CHECK and every `count` is `null`).
- `latestCheckFailed`: true when the newest `data_quality_run` (by id) has `status = 'failed'`, i.e. a check newer than `check` failed; the page then shows S-DQ-CHECK-FAILED above the (older, still current) results. False when there is no run or the newest run succeeded.
- `heuristic`: Persian note (S-DQ-HEURISTIC) for issue types whose Release 1 rule is a heuristic (`inconsistent-product-ids`), else `null`; the page shows it on the card.
- `notChecked`: deferred sub-checks of the type (only `inconsistent-amounts`: `line-net-mismatch`, `document-total-mismatch`), shown under the title S-DQ-NOT-CHECKED with their `reason`.
- `examples`: ≤ 10 per type, newest document date first, then `record_ref`; product records show `product` (name) and `field`, with null document fields. Vendor/product display names come from the synced masters; unknown → «نامشخص».
- `rejectedAtSync`: parsed from the `rejected:<reason>=<n>` parts of the `sync_log` messages of the job the current check validated (counts and codes only). This is where unconvertible dates (`date-invalid`), missing keys, dates or amounts and the other Phase 1 reject reasons appear (FR-020, FR-023); they are not part of any issue-type `count`. Box title S-DQ-REJECTED.
- Writes `data_quality.viewed` (detail `types`).

## §5. Reserved vendor financials (`VendorFinancialsController`, policy `vendor.financials`)

`GET /api/vendor-financials/summary` and `GET /api/vendor-financials/vendors/{vendorKey}` → 200

```json
{ "available": false, "reason": "not-available-until-phase-6", "message": "داده در دسترس نیست.", "dataLabel": "demo" }
```

No figure, no query. Admin and Purchasing → 403 + `access.denied` (`permission=vendor.financials`).

## §6. Report responses: `dataQuality` field (FR-027)

Added (top level, after the existing fields) to: `GET /api/overview/purchasing`, every `GET /api/purchasing/reports/{id}` and `/print`, `vendors` and `/print`, `vendor-profile`, `vendors/compare` and `/print`, `documents` and `/print`, `documents/{id}/lines`.

```json
"dataQuality": {
  "issueTypes": [ { "type": "missing-values", "label": "مقادیر ناقص", "count": 2 },
                  { "type": "inconsistent-amounts", "label": "مبالغ ناسازگار", "count": 1 } ],
  "affectedRecords": 3
}
```

When the notice query cannot be completed (timeout, error), the response still carries its figures and:

```json
"dataQuality": { "unavailable": true, "issueTypes": [], "affectedRecords": 0 }
```

The page renders the neutral note S-NOTICE-UNAVAILABLE (grey, not the amber warning, no link). `unavailable` is `false` in every normal notice.

`null` when: no successful validation run; no flagged record of a mapped type in the response's scope; or the response is empty because of `no-successful-sync`. Scope = the report's own lines (period/window, filters, search; for comparison the selected vendors) **plus the comparison period's lines whenever the response shows comparison or previous-period figures** (reports and vendor directory/profile with `compare=true`, i.e. a non-null `PreviousScope`; the Overview when its `comparison` is non-null). A record in both periods counts once. Mapping report → types: research R13. The lookups endpoint has no `dataQuality`.

Report-open audit (research R9): every endpoint in this section writes `report.opened` / `report.printed` per the tracker rules; `lookup/{kind}` writes nothing.

## §7. `GET /api/system/status` (extended)

```json
{
  "dataMode": "demo", "dataLabel": "demo", "lastSuccessfulSync": { … }, "serverJalaliToday": "1405-08-27",
  "role": "admin", "roleLabel": "مدیر سامانه",
  "permissions": ["overview","purchasing.reports","purchasing.transactions","admin"],
  "modules": { "overview": true, "purchasingReports": true, "dataSync": true, "users": true,
               "auditLog": true, "dataQuality": true, "demoPages": true, "ai": false }
}
```

`SystemModules.For(DataModeOptions mode, IReadOnlySet<string> permissions)`: `overview` = has overview; `purchasingReports` = has purchasing.reports; `dataSync`, `users`, `auditLog`, `dataQuality` = has admin; `demoPages` = demo && has overview; `ai` = demo && has ai.query.

## §8. SPA routes (hash)

`#/overview`, `#/vendors`, `#/reports`, `#/compare`, `#/settings/sync`, `#/settings/users`, `#/settings/audit`, `#/settings/data-quality[?types=a,b]`, `#/demo`. Unknown → first allowed view. Not allowed → S-NO-ACCESS (`NoAccess`).

## §9. Persian UI strings (single source)

Tasks and quickstart refer to these ids; code copies the text exactly. Server error messages are the ones already given in §0–§5 (they are the source for those texts).

| Id | Where | Text |
|---|---|---|
| S-NO-ACCESS | `NoAccess` | «شما به این بخش دسترسی ندارید.» |
| S-NAV-USERS | Settings tab | «کاربران و نقش‌ها» |
| S-NAV-AUDIT | Settings tab | «گزارش رویدادها» |
| S-NAV-DQ | Settings tab | «کیفیت داده» |
| S-PW-MIN | Change password / Users and roles (client check; same as server) | «گذرواژه باید حداقل ۸ نویسه باشد.» |
| S-AUDIT-EMPTY | Audit log empty state | «رویدادی با این فیلترها یافت نشد.» |
| S-DQ-NO-CHECK | Data quality, `check` null | «هنوز بررسی کیفیت داده انجام نشده است.» |
| S-DQ-NONE | Data quality, type with count 0 | «مشکلی یافت نشد.» |
| S-DQ-SYNC-FAILED | Data quality, `latestSyncFailed` | «آخرین همگام‌سازی ناموفق بود؛ نتایج زیر مربوط به آخرین همگام‌سازی موفق است.» |
| S-DQ-CHECK-FAILED | Data quality, `latestCheckFailed` | «آخرین بررسی کیفیت داده ناموفق بود؛ نتایج زیر مربوط به آخرین بررسی موفق است.» |
| S-DQ-NOT-CHECKED | Data quality, title of the `notChecked` list | «بررسی نشده» |
| S-DQ-NET | `notChecked` reason `line-net-mismatch` | «ستون مبلغ خالص ردیف در نقشهٔ منبع تأیید نشده است.» |
| S-DQ-TOTAL | `notChecked` reason `document-total-mismatch` | «ستون جمع سند در نقشهٔ منبع تأیید نشده است.» |
| S-DQ-HEURISTIC | `heuristic` of `inconsistent-product-ids` | «بررسی تقریبی: یک نام کالا با چند کد کالا. بررسی بر پایهٔ کد کالا پس از تأیید ستون‌های کد در محل فعال می‌شود.» |
| S-DQ-REJECTED | Data quality, rejected-at-sync box title | «ردیف‌های ردشده هنگام همگام‌سازی» |
| S-NOTICE | Report notice (amber), `{n}` = `affectedRecords` in Persian digits, followed by each issue label and count | «هشدار کیفیت داده: ارقام این گزارش شامل {n} رکورد دارای مشکل داده است.» |
| S-NOTICE-LINK | Notice link (Admin only, hidden in print) | «مشاهده در کیفیت داده» |
| S-NOTICE-UNAVAILABLE | Notice with `unavailable: true` (neutral grey) | «بررسی کیفیت داده برای این گزارش اکنون ممکن نشد؛ ارقام گزارش تغییری نکرده‌اند.» |
