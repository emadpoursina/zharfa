# Data Model: Permissions, Audit, and Data Quality (Release 1, Phase 3)

**Feature**: `specs/005-permissions-audit-quality` | **Plan**: [plan.md](plan.md) | **Research**: [research.md](research.md)

One forward-only, idempotent migration: `db/migrations/0008_permissions_audit_quality.sql` (requires `0007`). One edited seed (`db/seed/0002_sales_cash_users.sql`, role value only) and one new seed (`db/seed/0003_role_users.sql`). No change to `mabna.*` tables or functions.

---

## 1. Users, roles, permissions

### 1.1 `app_user` (extended)

| Column | Type | Rule |
|---|---|---|
| `user_id` | bigint identity PK | unchanged; sequence advanced by `setval` in `0008` and `0003` |
| `username` | text NOT NULL UNIQUE | as typed (trimmed) |
| `username_key` | text NOT NULL, UNIQUE index `uq_app_user_username_key` | **new**: `lower(translate(btrim(username), 'يىك', 'ییک'))`; C# `UsernameRules.Key` must give the same result |
| `password_hash` | text NOT NULL | PBKDF2 format unchanged; never selected into any DTO |
| `role` | text NOT NULL, `chk_app_user_role CHECK (role IN ('admin','management','purchasing','finance'))` | Demo 1 `manager` → `management` |
| `is_active` | boolean NOT NULL DEFAULT true | unchanged |
| `failed_signins` | integer NOT NULL DEFAULT 0 CHECK (≥ 0) | **new** (R5) |
| `locked_until` | timestamptz NULL | **new** (R5) |
| `must_change_password` | boolean NOT NULL DEFAULT false | **new** (R6) |
| `created_at` | timestamptz NOT NULL DEFAULT now() | **new** |
| `updated_at` | timestamptz NOT NULL DEFAULT now() | **new**; set on every admin change |

EF entity `AppUser` (`backend/Zharfa.Api/Data/ZharfaDbContext.cs`) gets the matching properties: `UsernameKey`, `FailedSignins`, `LockedUntil` (`DateTime?`, UTC), `MustChangePassword`, `CreatedAt`, `UpdatedAt`.

Validation (server, pure classes in `backend/Zharfa.Api/Security/`):

- `UsernameRules.TryValidate(string? raw, out string trimmed, out string key, out string? error)`: trimmed length 3–64; characters `[A-Za-z0-9._-]` or Persian/Arabic letters (U+0600–U+06FF, U+200C ZWNJ not allowed); error messages in Persian. `Key(s)` = trim → `ي`,`ى`→`ی`, `ك`→`ک` → `ToLowerInvariant()`.
- `PasswordRules.MinLength = 8`; `TryValidate(string? pw, out string? error)` — error «گذرواژه باید حداقل ۸ نویسه باشد.»; whitespace-only refused.
- `LastAdminGuard.Allows(string targetRole, bool targetActive, string? newRole, bool deactivating, int otherActiveAdmins) → bool`.

### 1.2 Roles and permissions (code only)

`backend/Zharfa.Api/Security/Permissions.cs`:

```text
Overview = "overview"                       PurchasingReports = "purchasing.reports"
PurchasingTransactions = "purchasing.transactions"
VendorFinancials = "vendor.financials"      Admin = "admin"
AiQuery = "ai.query"                        Export = "export"
All = [the seven, in this order]            Label(id) → Persian label (research R1 table)
```

`backend/Zharfa.Api/Security/Roles.cs` + `RolePermissions.cs`:

| Role id | Persian label | Permissions |
|---|---|---|
| `admin` | مدیر سامانه | overview, purchasing.reports, purchasing.transactions, admin |
| `management` | مدیریت | overview, purchasing.reports, purchasing.transactions, vendor.financials, ai.query, export |
| `purchasing` | واحد خرید | overview, purchasing.reports, purchasing.transactions, ai.query, export |
| `finance` | واحد مالی | overview, purchasing.reports, purchasing.transactions, vendor.financials, ai.query, export |

`Roles.All` (ordered as above), `Roles.Label(id)`, `Roles.Normalize(string? claimRole)` (`manager` → `management`, unknown → null), `RolePermissions.For(role) → IReadOnlySet<string>` (empty for unknown), `RolePermissions.Has(role, permission)`.

Claims set at sign-in and refreshed by `SessionValidator` (R3): `ClaimTypes.NameIdentifier` = user_id, `ClaimTypes.Name` = username, `ClaimTypes.Role` = role, `"zharfa:mcp"` = `"1"` when `must_change_password`. Helpers in `Security/ClaimsPrincipalExtensions.cs`: `UserId()`, `RoleId()`, `Permissions()`, `HasPermission(p)`, `MustChangePassword()`.

### 1.3 Sign-in state machine (R5)

```text
Active ──wrong pw (n<5)──▶ Active(failed=n)
Active(failed=4) ──wrong pw──▶ Locked(until=now+15m, failed=0)   [audit auth.locked]
Locked ──any pw before until──▶ Locked (423)                     [audit auth.signin.failed reason=locked]
Locked ──until passed──▶ Active(failed=0) (evaluated at next sign-in)
Locked ──Admin unlock──▶ Active(failed=0, until=NULL)             [audit user.unlocked]
Active ──correct pw──▶ Active(failed=0, until=NULL)               [audit auth.signin.succeeded]
any ──Admin deactivate──▶ Inactive (sessions end on next request) [audit user.deactivated]
Inactive ──Admin reactivate──▶ Active(failed=0, until=NULL)       [audit user.reactivated]
```

`SignInPolicy.Decide(SignInState state, bool passwordValid, DateTime nowUtc) → SignInDecision(Outcome, int NewFailed, DateTime? NewLockedUntil, bool JustLocked)`; `Outcome ∈ {Success, BadCredentials, Locked}`. Constants `MaxFailures = 5`, `LockoutDuration = 15 min`.

### 1.4 Seeds and the `0008` user section

`db/seed/0002_sales_cash_users.sql`: change only the role literal of `manager` from `'manager'` to `'management'`.

`db/seed/0003_role_users.sql` (new, demo only by the existing seed rule):

```sql
SELECT setval(pg_get_serial_sequence('app_user','user_id'), (SELECT COALESCE(MAX(user_id),0) FROM app_user) + 1, false);
INSERT INTO app_user (username, username_key, password_hash, role, is_active) VALUES
 ('purchasing','purchasing','pbkdf2-sha256$100000$WmhhcmZhUHVyY2hTYWx0MQ==$GcSbv/NL8ilbD4SKyUiLBkS964PwXec46MhnAlnk5ak=','purchasing',true),
 ('finance','finance','pbkdf2-sha256$100000$WmhhcmZhRmluYW5jU2FsdA==$RwBAESa/61z54EPl6WzeWRxvcXfcgeGKwbkxECut+TY=','finance',true);
-- purchasing: purchasing123   finance: finance123   (demonstration accounts only)
```

The hashes were computed with the repository's `PasswordHasher` scheme (PBKDF2-HMAC-SHA256, 100 000 iterations, 32-byte hash; the existing `admin` hash was re-derived with the same script as a check).

Migration `0008` user section, in order:

1. `ALTER TABLE app_user DROP CONSTRAINT IF EXISTS app_user_role_check;`
2. `UPDATE app_user SET role = 'management' WHERE role = 'manager';`
3. Add the new columns with `ADD COLUMN IF NOT EXISTS` (defaults as in §1.1); backfill `username_key`; `SET NOT NULL`; `CREATE UNIQUE INDEX IF NOT EXISTS uq_app_user_username_key ON app_user (username_key);`
4. Add `chk_app_user_role` inside a `DO $$ … IF NOT EXISTS (pg_constraint) …` block (pattern of `0005`).
5. `SELECT setval(pg_get_serial_sequence('app_user','user_id'), (SELECT COALESCE(MAX(user_id),0) FROM app_user) + 1, false);`
6. Demo upgrade: `INSERT … ('purchasing' …), ('finance' …) … WHERE EXISTS (SELECT 1 FROM install_setting WHERE key='data_mode' AND value='demo') AND EXISTS (SELECT 1 FROM app_user) ON CONFLICT (username_key) DO NOTHING;` (same hashes as `0003`; on a fresh install `install_setting` has no row yet, so seed `0003` does it).

Because seeds run after migrations, a fresh demo install inserts `manager` with role `management` directly (step 2 of `0002` edited), satisfying the new check.

## 2. Audit log

### 2.1 `audit_log` (extended; append-only trigger unchanged)

| Column | Type | Rule |
|---|---|---|
| `audit_id` | bigint identity PK | unchanged |
| `at` | timestamptz NOT NULL | UTC |
| `actor` | text NULL | username; attempted username (≤ 64 chars, scrubbed) for failed sign-ins; `anonymous` otherwise |
| `actor_user_id` | bigint NULL | **new**; null for anonymous/unknown |
| `action` | text NOT NULL | code from §2.2 |
| `target` | text NULL | **new**; report key, route pattern, `user:<username>` etc. |
| `detail` | text NULL | `AuditDetail` text, ≤ 500 chars, scrubbed |

Indexes (new): `idx_audit_log_at (at DESC, audit_id DESC)`, `idx_audit_log_actor_at (actor, at DESC)`, `idx_audit_log_action_at (action, at DESC)`.

C# (`backend/Zharfa.Api/Data/AuditLogger.cs`): `public sealed record AuditEntry(string Action, string? Target = null, string? Detail = null, string? ActorOverride = null, long? ActorUserIdOverride = null);` and `IAuditLogger.RecordAsync(AuditEntry entry, CancellationToken ct = default)`; the existing `RecordAsync(string action, string? detail, CancellationToken)` calls it. Actor resolution: override → authenticated `ClaimTypes.Name` → `anonymous`; user id: override → `NameIdentifier` claim → null. `AuditLog` entity gets `Target` and `ActorUserId`.

### 2.2 Action catalog (`backend/Zharfa.Api/Audit/AuditActions.cs`)

| Code | Persian label | Target | Detail keys |
|---|---|---|---|
| `auth.signin.succeeded` | ورود موفق | `user:<username>` | – |
| `auth.signin.failed` | ورود ناموفق | `user:<attempted>` | `reason` = unknown-user / inactive / locked / bad-password |
| `auth.signout` | خروج | `user:<username>` | – |
| `auth.locked` | قفل موقت حساب | `user:<username>` | `until` (Jalali time) |
| `auth.password_changed` | تغییر گذرواژه توسط کاربر | `user:<username>` | `forced=true/false` |
| `user.created` | ایجاد کاربر | `user:<username>` | `role` |
| `user.role_changed` | تغییر نقش کاربر | `user:<username>` | `from`, `to` |
| `user.deactivated` | غیرفعال‌سازی کاربر | `user:<username>` | – |
| `user.reactivated` | فعال‌سازی دوباره کاربر | `user:<username>` | – |
| `user.password_reset` | بازنشانی گذرواژه | `user:<username>` | – (only that it happened) |
| `user.unlocked` | رفع قفل حساب | `user:<username>` | – |
| `report.opened` | باز کردن گزارش | report key | allowlisted query params |
| `report.printed` | چاپ گزارش | report key | allowlisted query params |
| `access.denied` | رد دسترسی | `<METHOD> <route pattern>` | `permission` |
| `audit.viewed` | مشاهده گزارش رویدادها | `audit-log` | filter params |
| `data_quality.viewed` | مشاهده کیفیت داده | `data-quality` | `types` (if any) |
| `sync.requested` | درخواست همگام‌سازی | – (existing) | `job` |
| `sync.rejected` | رد درخواست همگام‌سازی | – (existing) | `reason` |
| `provider.status.viewed` | مشاهده وضعیت ارائه‌دهنده هوش مصنوعی | – (existing) | existing |
| `reconciliation.run` | اجرای تطبیق | – (existing) | `from`, `to`, `match` (existing) |
| `reconciliation.mismatch` | عدم تطابق در تطبیق | – (existing) | metric, period and filters only (`month`, `filter`, `filterValue`, `metric`, `basis`, `dataMode` from `PurchasingReconciliation`; `from`, `to`, `match=false` from the Demo 1 `AdminController` check). **Changed in this phase (FR-015)**: the amounts (`system`, `source`, `difference`; `apiSales`, `viewSales`, `apiReceivables`, `viewReceivables`) are no longer written in any data mode; the amounts stay in the reconciliation result/CLI output and `reconciliation_run` rows, never in the audit log. Rows written before this phase are append-only and are not rewritten. |
| `settings.ai_provider.changed` | تغییر تنظیمات ارائه‌دهنده هوش مصنوعی | reserved for Phase 4 | – |

Unknown codes found in old rows are listed with the code as label.

Report keys and Persian names (`backend/Zharfa.Api/Audit/ReportKeys.cs`, shared with the data-quality catalog): `overview` نمای کلی خرید · `vendors` فهرست و نمایهٔ تأمین‌کنندگان · `vendor-profile` نمایهٔ تأمین‌کننده · `by-vendor` خرید به تفکیک تأمین‌کننده · `by-product` خرید به تفکیک کالا · `by-category` خرید به تفکیک گروه کالا · `trend` روند ماهانه · `top-products` پرخریدترین کالاها · `returns` برگشت از خرید · `transactions` تاریخچهٔ تراکنش‌ها · `vendors-compare` مقایسهٔ تأمین‌کنندگان · `drill-down` جزئیات اسناد · `drill-down-lines` اقلام سند. (For data quality, `vendor-profile` is covered by `vendors` and `drill-down-lines` by `drill-down`.)

### 2.3 Report-open tracker (in memory)

`ReportOpenTracker` (singleton): `bool ShouldRecord(long userId, string reportKey, IReadOnlyDictionary<string,string?> scopeParams, IReadOnlyDictionary<string,string?> viewParams, bool print)`.

- `scopeParams` = `AuditDetail.ReportSignatureParams(query)` (the allowlisted period/filter keys, normalized; plus `documentId` for `drill-down-lines`).
- `viewParams` = `AuditDetail.ReportViewParams(query)` = only `page`, `pageSize`, `sort`, `dir`, trimmed, empty dropped.
- `scopeSig` = `join(sorted "k=v" of scopeParams)`; `fullSig` = `scopeSig + "|" + join(sorted "k=v" of viewParams)`.
- State: `ConcurrentDictionary<(long UserId, string ReportKey), (string ScopeSig, string FullSig)>` — the user's previous **screen** request for that report.

Rule (Clarify Q2 A, FR-013/FR-014, owner decision 2026-11-18):

| Case | Record? |
|---|---|
| `print` | yes, always; state is not read or changed |
| no previous request for (user, report) | yes |
| `scopeSig` differs from the previous one (period or filters changed) | yes |
| `scopeSig` equal and `fullSig` equal (a reload with identical parameters) | yes |
| `scopeSig` equal, `fullSig` differs (only `page`/`pageSize`/`sort`/`dir` changed) | **no** |

Every non-print call stores the new `(ScopeSig, FullSig)`, whether or not it records. There is no time window. State is lost on restart (the next request then counts as a first load — harmless). Size is bounded by users × 13 report keys. The SPA must not issue the same report request twice for one load (otherwise the reload rule would write two entries); the tester checks this in quickstart B2.

## 3. Data quality

### 3.1 Tables (public schema)

`data_quality_run`

| Column | Type | Rule |
|---|---|---|
| `id` | bigint identity PK | |
| `sync_job_id` | bigint NOT NULL REFERENCES sync_job(id) | the succeeded sync it validated |
| `started_at` | timestamptz NOT NULL | |
| `checked_at` | timestamptz NULL | set on success |
| `status` | text NOT NULL CHECK (status IN ('running','succeeded','failed')) | |
| `error_code` | text NULL | fixed code on failure (e.g. `validation-failed`), never a message with values |

Index `idx_data_quality_run_status (status, id DESC)`.

`data_quality_issue`

| Column | Type | Rule |
|---|---|---|
| `id` | bigint identity PK | |
| `run_id` | bigint NOT NULL REFERENCES data_quality_run(id) ON DELETE CASCADE | |
| `issue_type` | text NOT NULL CHECK (in the seven ids) | |
| `record_kind` | text NOT NULL CHECK (record_kind IN ('document','line','product')) | |
| `record_ref` | text NOT NULL | `document:<document_id>` / `line:<line_id>` / `product:<product_id>` |
| `field` | text NOT NULL | `number`, `vendor`, `product`, `quantity`, `date`, `discount`, `vat`, `document-discount`, `category`, `name` |
| `reason` | text NULL | sub-code: `missing`, `not-in-master`, `duplicate-number`, `duplicate-name`, `future`, `too-early`, `non-positive`, `discount-exceeds-gross`, `negative-vat`, `doc-discount-exceeds-lines` |
| `document_id` | bigint NULL | for document and line records (no FK: the copy is replaced by sync) |
| `line_id` | bigint NULL | line records |
| `product_id` | bigint NULL | product records, and line records whose product is known |

Unique `(run_id, issue_type, record_ref, field)`. Indexes `(run_id, issue_type)`, `(run_id, document_id)`, `(run_id, line_id)`, `(run_id, product_id)`.

Old runs' issues are deleted by the validator in the same transaction that completes a new run; run rows are kept (history, small).

### 3.2 Rules (`backend/Zharfa.Api/DataQuality/DataQualitySql.cs`)

Common: `d` = `mabna.purchase_document` with `NOT d.cancelled`; `l` = `mabna.purchase_line` joined to such a `d`. Parameters: `@runId bigint`, `@todayKey int` (sync-day Jalali key in `Sync__TimeZone`, from the job's `finished_at`), `@earliestKey int` (from `DataQuality__EarliestJalaliDate`), flags `@numberMapped`, `@vendorMapped`, `@productMapped`, `@quantityMapped`, `@categoryMapped` (from `PurchaseMapping`: invoice/return `Number`/`VendorKey` non-null, `Line.ProductKey`/`Line.Quantity` non-null, `ProductCategory` non-null). Each rule is `INSERT INTO data_quality_issue (run_id, issue_type, record_kind, record_ref, field, reason, document_id, line_id, product_id) SELECT … ON CONFLICT DO NOTHING`.

| # | Type | Kind | Predicate | field / reason |
|---|---|---|---|---|
| MV1 | missing-values | document | `@numberMapped AND (d.doc_number IS NULL OR btrim(d.doc_number) = '')` | number / missing |
| MV2 | missing-values | document | `@vendorMapped AND d.vendor_source_key IS NULL` | vendor / missing |
| MV3 | missing-values | line | `@productMapped AND l.product_source_key IS NULL` | product / missing |
| MV4 | missing-values | line | `@quantityMapped AND l.quantity IS NULL` | quantity / missing |
| DU1 | duplicate-records | document | `d.document_id <> min(document_id) OVER (PARTITION BY d.doc_type, d.vendor_source_key, mabna.fn_search_key(d.doc_number))` over docs with non-empty number and non-null `vendor_source_key` (owner decision 2026-11-18: type + number + vendor, safer against vendor-issued invoice numbers colliding across vendors; confirm on-site whether numbers are unique per type or per vendor and type) | number / duplicate-number |
| BR1 | broken-relationships | document | `d.vendor_source_key IS NOT NULL AND d.vendor_id IS NULL` | vendor / not-in-master |
| BR2 | broken-relationships | line | `l.product_source_key IS NOT NULL AND l.product_id IS NULL` | product / not-in-master |
| BR3 | broken-relationships | product | `@categoryMapped AND p.drug_group_key IS NOT NULL AND NOT EXISTS (SELECT 1 FROM mabna.product_category c WHERE c.source_key = p.drug_group_key)` | category / not-in-master |
| PI1 | inconsistent-product-ids | product | `btrim(p.name_fa) <> '' AND p.product_id <> min(product_id) OVER (PARTITION BY p.search_key)` — **name-based heuristic** in Release 1 (labelled on the page); the code-level rule (one product code → several products, one product → conflicting codes) is added when product code columns are verified on-site | name / duplicate-name |
| DT1 | invalid-dates | document | `d.jalali_key > @todayKey` | date / future |
| DT2 | invalid-dates | document | `d.jalali_key < @earliestKey` | date / too-early |
| QT1 | unexpected-quantities | line | `l.quantity IS NOT NULL AND l.quantity <= 0` | quantity / non-positive |
| AM1 | inconsistent-amounts | line | `l.line_discount > l.gross_amount` | discount / discount-exceeds-gross |
| AM2 | inconsistent-amounts | line | `l.vat_amount < 0` | vat / negative-vat |
| AM3 | inconsistent-amounts | document | `d.doc_discount > (SELECT sum(gross_amount - line_discount) FROM mabna.purchase_line WHERE document_id = d.document_id) + 1` and the document has lines | document-discount / doc-discount-exceeds-lines |

Line records also set `document_id` and `product_id` (`l.product_id`). Counting: per type, `count(DISTINCT record_ref)`.

Deferred sub-checks (R11, owner decision 2026-11-18): `line-net-mismatch` and `document-total-mismatch` are listed on the page under S-DQ-NOT-CHECKED (contracts/api.md §9) with their reasons S-DQ-NET / S-DQ-TOTAL; they become rules when the on-site checklist verifies a line net amount and a document total and the source map gains those items.

Not in the seven counts: source rows rejected by `PurchaseRowMapper` (unconvertible or missing date, missing key, missing or negative gross amount, unparseable quantity/VAT) never reach `mabna.*`; they are shown in the page's "rejected at sync" box from `sync_log` (contracts/api.md §4 `rejectedAtSync`).

### 3.3 Catalog (`backend/Zharfa.Api/DataQuality/DataQualityCatalog.cs`, pure)

`IssueTypes` (ordered): `missing-values` «مقادیر ناقص», `duplicate-records` «رکوردهای تکراری», `broken-relationships` «ارتباط‌های ناقص», `inconsistent-product-ids` «شناسه‌های ناسازگار کالا», `invalid-dates` «تاریخ‌های نامعتبر», `unexpected-quantities` «مقادیر غیرمنتظره», `inconsistent-amounts` «مبالغ ناسازگار». Each has a Persian one-line description, the affected report keys (research R13), an optional `Heuristic` note (only `inconsistent-product-ids`: S-DQ-HEURISTIC, contracts/api.md §9), the `NotChecked` list (only `inconsistent-amounts`: `line-net-mismatch` S-DQ-NET, `document-total-mismatch` S-DQ-TOTAL) and the Persian field labels: number «شماره سند», vendor «تأمین‌کننده», product «کالا», quantity «مقدار», date «تاریخ», discount «تخفیف ردیف», vat «مالیات بر ارزش افزوده», document-discount «تخفیف سند», category «گروه کالا», name «نام کالا».

### 3.4 Options

`DataQualityOptions.FromConfiguration(IConfiguration)` reads `DataQuality__EarliestJalaliDate` through `ConfigurationKeys.Get` (default `1390-01-01`); invalid → `InvalidOperationException` naming the key (startup refusal, called in `Program.cs` next to the sync options). `EarliestKey` = `yyyy*10000+mm*100+dd`.

### 3.5 Notice (response field)

```text
DataQualityNotice(IReadOnlyList<DataQualityNoticeType> IssueTypes, long AffectedRecords, bool Unavailable = false)
DataQualityNoticeType(string Type, string Label, long Count)
static DataQualityNotice DataQualityNotice.CheckUnavailable = new([], 0, Unavailable: true)
```

Defined in `backend/Zharfa.Api/Purchasing/Reports/ReportDtos.cs`. Added as the **last positional parameter with default `null`** (`DataQualityNotice? DataQuality = null`) to `ReportEnvelope`, `ReportTablePage<TRow>`, `ReportPrintPage<TRow>`, `DocumentLinesResponse`, `VendorProfileResponse`, `VendorComparisonResponse` and `PurchasingOverviewResult`, so existing constructions compile; every construction site that copies the envelope passes `DataQuality: envelope.DataQuality`.

Notice SQL (`ReportSql.DataQualityNotice(SearchTarget target)`, using the private `BaseCte`). It returns the distinct flagged `(issue_type, record_ref)` pairs of one scope; the service merges the pairs of the current scope and, when the response shows comparison or previous-period figures, of the comparison scope, then counts (a record in both periods counts once):

```sql
<BaseCte with {SEARCH}>,
scope_lines AS (SELECT DISTINCT document_id, line_id, product_id FROM scoped
                WHERE @vendorKeys::text[] IS NULL OR vendor_source_key = ANY(@vendorKeys::text[])),
run AS (SELECT id FROM data_quality_run WHERE status = 'succeeded' ORDER BY id DESC LIMIT 1),
hit AS (
  SELECT DISTINCT i.issue_type, i.record_ref
  FROM data_quality_issue i JOIN run ON i.run_id = run.id
  WHERE i.issue_type = ANY(@types::text[])
    AND ((i.record_kind = 'line'     AND i.line_id     IN (SELECT line_id     FROM scope_lines))
      OR (i.record_kind = 'document' AND i.document_id IN (SELECT document_id FROM scope_lines))
      OR (i.record_kind = 'product'  AND i.product_id  IN (SELECT product_id  FROM scope_lines))))
SELECT issue_type, record_ref FROM hit
```

Bound with `ReportSql.Bind(cmd, scope)` plus `@types` (the report's mapped types from the catalog) and `@vendorKeys` (null except for the comparison). Run once for the current scope and once more for the comparison scope when one is shown (`ReportQueryContext.PreviousScope` non-null for reports, vendor directory and profile; the Overview's previous month when its `Comparison` is non-null). `BuildNotice` groups the merged distinct pairs: `Count` per type = distinct `record_ref`, `AffectedRecords` = distinct `record_ref` over all types. Empty result → `null` notice. Command timeout 15 s like the reports; a timeout or any other failure of either query (other than cancellation requested by the caller) → `DataQualityNotice.CheckUnavailable` (`{unavailable:true, issueTypes:[], affectedRecords:0}`) and a warning log with the exception type only. The report itself never fails because of the notice, and the page shows the neutral note S-NOTICE-UNAVAILABLE instead of silently showing nothing.

## 4. Relationships

```text
app_user 1─* audit_log (by actor_user_id, soft; no FK so deleted/renamed users keep history)
sync_job 1─* data_quality_run 1─* data_quality_issue ─(soft)→ mabna.purchase_document / purchase_line / product
report response ── dataQuality ← DataQualityNoticeService(scope, catalog types)
```

## 5. Configuration keys (new or changed)

| Key | Where | Default | Notes |
|---|---|---|---|
| `Bootstrap__AdminUsername` | api env | – | required in production while no active Admin exists |
| `Bootstrap__AdminPassword` | api env | – | or the file variant; ≥ 12 chars; never logged |
| `Bootstrap__AdminPasswordFile` | api env | – | **new**: path to a Docker secret file |
| `Bootstrap__ManagerUsername/Password` | – | – | **retired** (removed from `.env.example`, compose and README) |
| `DataQuality__EarliestJalaliDate` | api env | `1390-01-01` | **new**; set on-site to the first fiscal year start |
