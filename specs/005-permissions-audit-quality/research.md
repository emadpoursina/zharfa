# Research: Permissions, Audit, and Data Quality (Release 1, Phase 3)

**Feature**: `specs/005-permissions-audit-quality` | **Date**: 2026-11-18 | **Plan**: [plan.md](plan.md) | **Spec**: [spec.md](spec.md)

Skip mode: no questions were asked. Every open point is decided below; owner-facing assumptions are collected in R20 so the owner can overturn them. Phase 1 (`specs/003-real-purchase-data/research.md`) and Phase 2 (`specs/004-vendor-purchasing-reports/research.md`) decisions stay in force; this document only adds to them.

Code facts this research relies on (read in the worktree, 2026-11-18):

- Auth is a cookie scheme (`zharfa_session`, 8 h sliding) set up in `backend/Zharfa.Api/Program.cs`; `AuthController` signs in with `ClaimTypes.Name` and `ClaimTypes.Role` from `app_user` and never re-reads the user afterwards.
- Every controller uses `[Authorize(Roles = "...")]` with the Demo 1 roles `admin` and `manager`; `app_user.role` has `CHECK (role IN ('manager','admin'))` (`db/migrations/0001_init_canonical.sql`).
- `audit_log(audit_id, at, actor, action, detail)` is append-only by trigger `trg_audit_log_append_only`; `AuditLogger.RecordAsync(action, detail)` takes the actor from `HttpContext.User`, scrubs the detail with `RedactionHelper.Scrub` and swallows write failures.
- `BootstrapAccounts.EnsureAsync` creates an `admin` and a `manager` account from `Bootstrap__*` keys when a production install has no users (min. 12 characters).
- Demo seeds (`db/seed/*.sql`) run only in demo mode and only while `app_user` is empty; `0002_sales_cash_users.sql` inserts `manager`/`admin` with explicit `user_id` 1 and 2 (the identity sequence is not advanced).
- The sync (`SyncCoordinator.ExecuteAsync`) runs in the background while holding the run lock; the synced copy lives in `mabna.vendor`, `mabna.product`, `mabna.product_category`, `mabna.purchase_document`, `mabna.purchase_line`. Unique keys `(doc_type, source_key)` and `(document_id, source_key)`; `doc_date` and `gross_amount` are NOT NULL; `PurchaseRowMapper` rejects rows with a missing key, an unparseable date, an unparseable or negative gross amount, a negative discount or an unparseable quantity/VAT; lines whose document is not synced are skipped (`non-purchase-document`).
- Reports share `ReportSql.BaseCte` (`fn_purchase_lines` + filter predicate + search) and `ReportEnvelope` built once in `PurchasingReportService.ResolveAsync`.

---

## R1. Role model and permission sets (FR-001, FR-002)

**Decision.** Four fixed role ids stored in `app_user.role`: `admin`, `management`, `purchasing`, `finance`. Seven permission ids, held in code (not in the database):

| Permission id | Persian label | admin | management | purchasing | finance |
|---|---|:-:|:-:|:-:|:-:|
| `overview` | نمای کلی خرید | ✓ | ✓ | ✓ | ✓ |
| `purchasing.reports` | تأمین‌کنندگان، گزارش‌های خرید و مقایسه | ✓ | ✓ | ✓ | ✓ |
| `purchasing.transactions` | جزئیات اسناد و تراکنش‌ها | ✓ | ✓ | ✓ | ✓ |
| `vendor.financials` | اطلاعات مالی تأمین‌کنندگان | – | ✓ | – | ✓ |
| `admin` | مدیریت سامانه (کاربران، همگام‌سازی، کیفیت داده، گزارش رویدادها، تطبیق) | ✓ | – | – | – |
| `ai.query` | پرسش از دستیار هوشمند | – | ✓ | ✓ | ✓ |
| `export` | خروجی اکسل | – | ✓ | ✓ | ✓ |

`RolePermissions.For(role)` (pure, `Security/RolePermissions.cs`) is the single source; the API, `/api/auth/me`, `/api/system/status` and the Users and roles page all read it.

**Rationale.** FR-002 fixes the sets and says the Admin assigns roles but does not edit sets; code constants are simpler and testable, and a table would invite editing that is out of scope. Role ids are lower-case ASCII like Demo 1's.

**Alternatives.** Role/permission tables (editable sets — out of scope, more migration); ASP.NET role checks only (cannot express "Admin lacks vendor financials" without listing roles on every endpoint, and gives no permission name for the access-denied audit entry).

## R2. Server-side enforcement: policies per permission (FR-003, FR-004)

**Decision.** One authorization policy per permission id, registered in `Program.cs` (`options.AddPolicy(p, b => b.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(p)))`), evaluated by `PermissionAuthorizationHandler`, which succeeds when `RolePermissions.Has(user's role claim, permission)`. Every controller replaces `[Authorize(Roles = ...)]` with `[Authorize(Policy = Permissions.X)]` (table in [contracts/api.md §0](contracts/api.md)). A custom `IAuthorizationMiddlewareResultHandler` (`Security/AuditingAuthorizationResultHandler.cs`) writes the Persian 401/403 JSON bodies and, for 403, an `access.denied` audit entry with the route pattern and the missing permission, then stops the pipeline (no handler runs, no data).

**Rationale.** The authorization middleware runs before the controller, so a refused call executes no query (FR-003). The result handler is the one place that sees both the failed requirement (the missing permission) and the request, which is exactly what FR-004 must record. It also keeps the 403 body identical to the current `OnRedirectToAccessDenied` text.

**Permission matrix test.** A reflection test (`PermissionMatrixTests`) enumerates every controller action, resolves its effective policy (method attribute overrides class attribute) and compares it with an expected table that mirrors contracts/api.md §0. A new endpoint without a policy (other than the explicit `[AllowAnonymous]` sign-in/sign-out) fails the test. This proves SC-002 statically; quickstart Flow A2 proves it over HTTP.

**Alternatives.** Imperative checks in each action (easy to forget; a forgotten check returns data); an action filter (runs after model binding, cannot see `[Authorize]` failures).

## R3. A role change applies on the next request (FR-007, SC-003)

**Decision.** The session cookie carries only the user id (`ClaimTypes.NameIdentifier`), the username and the role at sign-in. `CookieAuthenticationEvents.OnValidatePrincipal` (`Security/SessionValidator.cs`) reloads `app_user` by id on **every** request (primary-key lookup, `AsNoTracking`):

- user missing or `is_active = false` → `RejectPrincipal()` and sign-out: the request continues unauthenticated, so protected endpoints answer 401 and the cookie is cleared;
- role or `must_change_password` differs from the claims → `ReplacePrincipal(new claims)` and `ShouldRenew = true`;
- a cookie without a `NameIdentifier` claim (a Demo 1 session) is rejected → the user signs in again once after the upgrade.

**Rationale.** It is the only design that satisfies "the very next request" without a session store; the cost is one indexed lookup per request for ~20 users on a LAN. The role claim in the new cookie is a convenience for the UI only; authorization always uses the reloaded principal.

**Alternatives.** A security-stamp column checked every N minutes (not "next request"); server-side session table (more moving parts, same per-request lookup).

## R4. Users and roles administration (FR-006, FR-008, FR-010, FR-033)

**Decision.** `UsersAdminController` at `/api/admin/users` (policy `admin`) with list, create, change role, reset password, deactivate, reactivate and unlock ([contracts/api.md §2](contracts/api.md)). Mutations use `POST` (the SPA client has `apiGet`/`apiPost` only).

- **Usernames**: 3–64 characters from `[A-Za-z0-9._-]` and Persian letters; stored as typed in `username`, and as a comparison key in the new `username_key` column: trimmed, `ي`/`ى`→`ی`, `ك`→`ک`, lower-cased (`UsernameRules.Key`). `username_key` is UNIQUE; the migration backfills it with the same rule in SQL (`lower(translate(btrim(username), 'يىك', 'ییک'))`). Sign-in looks the user up by `username_key`.
- **Passwords**: at least 8 characters (`PasswordRules.MinLength = 8`) on create, reset and change; hashed with the existing `PasswordHasher` (PBKDF2-SHA256, 100 000 iterations). Never returned, logged or audited.
- **Last active Admin** (FR-008): deactivating, or changing the role away from `admin` of, an active Admin is refused (409) when no other active Admin exists. The check and the update run in one transaction that first locks the active Admin rows (`SELECT user_id FROM app_user WHERE role='admin' AND is_active FOR UPDATE`), so two concurrent demotions cannot both pass. The decision itself is pure (`LastAdminGuard.Allows(...)`) and unit-tested.
- **Concurrent edits**: last write wins; each change writes its own audit entry (edge case "two Admins").
- **Password reset** does not force a change at next sign-in (spec only requires it for the first Admin, FR-034); see R20.

**Alternatives.** PUT/PATCH verbs (would need a new client helper; no benefit); Postgres `citext` (extension, and still not ی/ک-aware).

## R5. Sign-in protection (FR-033, SC-009)

**Decision.** New columns `app_user.failed_signins int NOT NULL DEFAULT 0` and `app_user.locked_until timestamptz NULL`. Pure `SignInPolicy.Decide(state, passwordValid, nowUtc)` returns the outcome and the new state:

| State / input | Outcome | New state | Audit |
|---|---|---|---|
| unknown username | `401` «نام کاربری یا گذرواژه اشتباه است.» | – | `auth.signin.failed` reason `unknown-user` |
| inactive user | `401` same message | unchanged | `auth.signin.failed` reason `inactive` |
| `locked_until > now` (any password) | `423` «حساب شما به‌دلیل ورودهای ناموفق پیاپی تا ساعت … به‌طور موقت قفل است.» | unchanged | `auth.signin.failed` reason `locked` |
| wrong password, failures+1 < 5 | `401` | `failed_signins += 1` | `auth.signin.failed` reason `bad-password` |
| wrong password, failures+1 = 5 | `423` | `failed_signins = 0`, `locked_until = now + 15 min` | `auth.signin.failed` + `auth.locked` |
| correct password | `200` | `failed_signins = 0`, `locked_until = NULL` | `auth.signin.succeeded` |

The state update is one `UPDATE … WHERE user_id = @id` with the values from `Decide`. Admin unlock (`POST /api/admin/users/{id}/unlock`) clears both columns and writes `user.unlocked`. The last active Admin can be locked; it then expires after 15 minutes (spec edge case). The response for "unknown user" and "bad password" is identical, so usernames cannot be probed.

**Rationale.** Matches the clarified rule exactly; pure logic makes SC-009 unit-testable without a clock. 423 lets the UI show the lockout message distinctly from a wrong password.

## R6. First Admin of a production install (FR-034, SC-010)

**Decision.** Rework `Platform/BootstrapAccounts.cs`:

- Runs only in production mode, and only when **no active `admin` user exists**.
- Reads `Bootstrap__AdminUsername` and either `Bootstrap__AdminPassword` or `Bootstrap__AdminPasswordFile` (a path, for a Docker secret such as `/run/secrets/zharfa_admin_password`; the file content with trailing CR/LF removed). Minimum length stays **12** for this installer-supplied value (stricter than the 8 of FR-033; unchanged from Phase 1).
- Creates one `admin` user with `must_change_password = true`. If a user with the same `username_key` already exists, startup is refused with a Persian/English message naming the key, never the value.
- Missing or short values while no active Admin exists → startup refused with the existing message style (keys named, values never logged); no account is created and there is no default password anywhere.
- When an active Admin exists the keys are ignored and never touch an existing password.
- `Bootstrap__ManagerUsername/Password` are retired: no longer required or read. `deploy/.env.example`, `deploy/docker-compose.yml` and `deploy/README.md` drop them and add `Bootstrap__AdminPasswordFile`.

The forced change is enforced on the server by `PasswordChangeGate` middleware (after authentication, before authorization): a principal whose reloaded claim says `must_change_password` gets `403 {code:"password-change-required"}` on every `/api/*` path except `GET /api/auth/me`, `POST /api/auth/change-password` and `POST /api/auth/logout`. `POST /api/auth/change-password` checks the current password, applies the 8-character rule, refuses reusing the same password, clears the flag and writes `auth.password_changed`.

**Rationale.** One Admin from the secret is what FR-034 says; the change is enforced server-side so a client cannot skip it. Keeping 12 characters for the installer value avoids weakening Phase 1.

**Alternatives.** Keep the Manager bootstrap (spec asks for the first Admin only; the Admin creates the rest); a CLI to create the first Admin (one more operator step on-site).

## R7. Mapping Demo 1 accounts (FR-011)

**Decision.** Migration `0008` drops the inline role check, updates `role = 'management' WHERE role = 'manager'`, and adds `chk_app_user_role CHECK (role IN ('admin','management','purchasing','finance'))`. Passwords are untouched.

Demo users per role (demo installs only):

- **Fresh demo install**: migrations run before seeds, so `db/seed/0002_sales_cash_users.sql` is edited to insert `manager` with role `management` (its file name stays, so installs that already applied it are unaffected), and a new `db/seed/0003_role_users.sql` first advances the identity sequence (`setval`) and inserts `purchasing` / `purchasing123` (role `purchasing`) and `finance` / `finance123` (role `finance`) with precomputed hashes ([data-model.md §1.4](data-model.md)).
- **Upgraded demo install** (users exist, so seeds no longer run): migration `0008` inserts the same two users only when `install_setting(key='data_mode')` is `demo`, with `ON CONFLICT (username_key) DO NOTHING`.
- **Production**: no demo users ever (seeds and the conditional insert both check demo mode).

Migration `0008` also calls `setval` on the `app_user` identity sequence, because the Demo 1 seed inserted explicit ids 1 and 2 and the first Admin-created user would otherwise collide on `user_id = 1`.

## R8. Audit entry model (FR-013, FR-015, FR-016, FR-019)

**Decision.** Extend `audit_log` (append-only trigger unchanged) with `target text NULL` and `actor_user_id bigint NULL`, plus indexes `(at DESC)`, `(actor, at DESC)`, `(action, at DESC)`. `actor` stays the username (or the attempted username for failed sign-ins, or `anonymous`). Add to `IAuditLogger` an overload `RecordAsync(AuditEntry entry, CancellationToken)` where `AuditEntry(string Action, string? Target, string? Detail, string? ActorOverride = null)`; the old `RecordAsync(action, detail)` stays and maps to it, so Demo 1 and Phase 1 call sites (`provider.status.viewed`, `reconciliation.*`, `sync.requested`, `sync.rejected`) keep working and appear in the same list (FR-019).

- `Detail` is built with `AuditDetail` (pure): `key=value; key=value`, each value trimmed, CR/LF removed, cut to 100 characters, the whole detail cut to 500, then `RedactionHelper.Scrub`. Only allowlisted keys are accepted for report opens (period, month, quarter, year, from, to, vendor, vendors, product, category, docType, q, match, compare, all, documentId). Response bodies are never passed to the logger.
- Passwords, hashes, cookies and tokens are never parameters of any audit call. A unit test (`AuditDetailTests`) feeds secret-looking input and asserts redaction and the length bounds.
- Append-only (FR-016): the existing trigger refuses UPDATE/DELETE for every database user; there is no API to change or delete entries. Quickstart Flow B6 proves it with SQL.
- Audit write failure (FR-018): unchanged — swallowed and logged without secrets; a 403 is written before/independently of the audit result.
- Existing figures in audit details (FR-015, analyze I9): `reconciliation.mismatch` is written today with amounts — `PurchasingReconciliation.AuditDetail` adds `system`, `source`, `difference` outside production, and `AdminController.GetReconciliation` (Demo 1) adds `apiSales`, `viewSales`, `apiReceivables`, `viewReceivables`. This phase strips them: both details keep only metric, period and filters (and `match`). The detail builders become pure static methods (`PurchasingReconciliation.MismatchAuditDetail`, `AdminController.ReconciliationAuditDetail`) with a unit test. The amounts remain in the reconciliation result and `reconciliation_run` rows, which are not audit entries. Old audit rows are append-only and stay as written.

Action catalog (code → Persian label) is `Audit/AuditActions.cs`, listed in [data-model.md §2.2](data-model.md).

**AI provider settings change.** No in-app AI provider setting exists (provider config is environment-only since Demo 1), so there is no such event to record in this phase. Phase 4 must audit any setting it adds (`settings.ai_provider.changed` is reserved in the catalog).

## R9. "Report open" without paging/sorting noise (FR-013, FR-014)

**Decision.** A `[AuditReportOpen("<report key>")]` action filter (`Audit/AuditReportOpenAttribute.cs`) on every report endpoint writes `report.opened` (or `report.printed` for print twins) **after** a 2xx result, with target = report key and detail = allowlisted query parameters (R8). For screen (non-print) requests a singleton `ReportOpenTracker` remembers, per (user id, report key), the user's **previous request** for that report as two signatures: the scope signature (allowlisted period/filter parameters sorted by name) and the full signature (scope + `page`, `pageSize`, `sort`, `dir`). The rule is Clarify Q2 A taken literally (owner decision 2026-11-18):

- scope changed (period or filters) → write;
- scope and paging/sorting both identical (a reload) → write;
- scope identical, only `page`/`pageSize`/`sort`/`dir` changed → write nothing;
- prints → always write.

There is no time window. Details: [data-model.md §2.3](data-model.md).

- Overview → key `overview`; `reports/{reportId}` → the report id (`by-vendor`, `by-product`, `by-category`, `trend`, `top-products`, `returns`, `transactions`); vendor directory → `vendors`; profile → `vendor-profile`; comparison → `vendors-compare`; documents list → `drill-down`; document lines → `drill-down-lines` (detail includes `documentId`).
- Lookups (filter-bar autocomplete) are not report opens and are not audited.
- Admin pages: `audit.viewed` and `data_quality.viewed` are written when the request is the first page (`page` absent or 1) — opening the page or changing a filter.

**Rationale.** The server alone decides (a client cannot skip the audit by omitting a header). Comparing with the previous request for the same report is exactly what "paging and sorting add none" means; every other request is a load and is recorded, so reopening or reloading a report is never lost. A 30-minute suppression window (earlier draft) was dropped because it contradicted Q2 A. Cost: a reload writes one row (≈ tens of rows per user per day — negligible against the ~100 k/year budget of R10).

**Alternatives.** Client header `X-Audit-Open` (bypassable); audit every request (violates FR-014).

## R10. Audit log query (FR-017, SC-004, SC-008)

**Decision.** `GET /api/admin/audit?user=&action=&from=&to=&page=&pageSize=` (policy `admin`). `from`/`to` are Jalali `yyyy-mm-dd` (Persian or Latin digits), converted with `JalaliDate` to the start of `from` and the start of the day after `to` in the sync time zone (`Sync__TimeZone`), then to UTC; `to < from` → 400 with a Persian message. Newest first (`ORDER BY at DESC, audit_id DESC`), `pageSize` 1–100 (default 50), with `total` from a separate `SELECT count(*)` over the same predicate. The SQL is fixed text with optional predicates driven by bound parameters (`(@user::text IS NULL OR actor = @user)` …), like `ReportSql`. Each row carries `atJalali` (Persian digits, `yyyy/mm/dd HH:mm`), the Persian action label and the raw code. The pure parser `AuditQuery.TryParse` is unit-tested.

Performance: with the three new indexes, a one-year table (~100 k rows, `db/perf/audit-1y.sql`) answers in well under 2 s (quickstart Flow B7).

## R11. Data-quality checks on the synced copy (FR-020, FR-021, FR-026)

**Decision.** `DataQualityValidator` (singleton, `NpgsqlDataSource`) runs fixed SQL rules over `mabna.*` only. Each rule is one `INSERT INTO data_quality_issue … SELECT …` with `@runId`. Only **non-cancelled** documents and their lines are checked (cancelled documents feed no report). Rules by issue type ([data-model.md §3.2](data-model.md) has the exact predicates):

| Issue type id | Records flagged in the synced copy |
|---|---|
| `missing-values` | document without number (when the number is mapped), without vendor key (when mapped); line without product key (when mapped), without quantity (when mapped) |
| `duplicate-records` | non-cancelled documents of the same type **and the same vendor** sharing a document number (normalized with `mabna.fn_search_key`): every one except the lowest `document_id` (owner decision 2026-11-18; on-site confirmation item) |
| `broken-relationships` | document whose vendor key is not in `mabna.vendor`; line whose product key is not in `mabna.product`; product whose category key is not in `mabna.product_category` (only when the category master is mapped) |
| `inconsistent-product-ids` | **heuristic** (labelled on the page, S-DQ-HEURISTIC): products sharing one normalized non-empty name (`mabna.product.search_key`) under different source keys: every one except the lowest `product_id`. The code-level rule waits for on-site verification of product code columns |
| `invalid-dates` | document dated after the sync day (Jalali key > sync-day key in `Sync__TimeZone`) or before `DataQuality__EarliestJalaliDate` |
| `unexpected-quantities` | line with quantity ≤ 0 |
| `inconsistent-amounts` | line whose discount exceeds its gross amount; line with negative VAT; document whose document discount exceeds the sum of its lines' (gross − line discount) by more than 1 Rial |

**What the synced copy cannot contain, and where it shows instead.**

- *Same source document or line synced twice*: impossible — unique keys `(doc_type, source_key)` and `(document_id, source_key)`. The duplicate rule therefore checks duplicate document numbers per type and vendor.
- *Unconvertible or missing dates, missing keys, missing or negative gross amounts, unparseable quantities*: rejected by `PurchaseRowMapper` before storage (spec FR-020/FR-023 state this). Lines whose parent document is missing are skipped. These already appear per table and reason in `sync_log` (Phase 1); Data quality shows them in a "rejected at sync" box read from the latest succeeded job's `sync_log` rows (counts and reason codes only, no values). They are not part of the seven counts, because no synced record exists to count or to attach a notice to.
- *Line net amount vs gross − discount + VAT, document total vs sum of lines*: the synced copy has no source net-amount or document-total column, and AGENTS.md forbids building on unverified Mabna structures. These two sub-checks are **deferred** until a line net amount and a document total are verified on-site and added to the source map (onsite checklist; owner decision 2026-11-18, spec FR-020). The page lists them under the issue type with the title S-DQ-NOT-CHECKED and reasons S-DQ-NET / S-DQ-TOTAL (contracts/api.md §9). See R20.

**Earliest plausible date.** New key `DataQuality__EarliestJalaliDate` (Jalali `yyyy-mm-dd`, default `1390-01-01`). On-site it is set to the start of Mabna's first fiscal year (onsite checklist item). Invalid value → startup refused with the key named (same pattern as `SyncOptions`).

**Figures unchanged (FR-026).** Validation only inserts into its own tables; no report SQL reads them except the notice query. Phase 2 invariants are rerun in quickstart Flow C6.

**Alternatives.** Run checks inside the sync transaction (a check bug would fail the sync); views computed on read (slow on the page and for every report notice); checks in C# over loaded rows (memory, and duplicates the SQL definitions).

## R12. When validation runs and which result is shown (FR-022)

**Decision.** In `SyncCoordinator.ExecuteAsync`, after a run is recorded `succeeded` and while the run lock is still held, call `IDataQualityValidator.RunAsync(jobId, syncFinishedUtc)`. The validator, in one transaction: inserts a `data_quality_run` row, runs all rules, sets the run `status='succeeded'` and `checked_at`, deletes `data_quality_issue` rows of older runs, commits. On any exception it rolls back, inserts a `status='failed'` run row with a fixed error code, and logs a warning with the exception type only. The **current result** is the newest `succeeded` run. A failed sync never calls the validator, so the previous result stays (FR-022); the page reads the newest `sync_job` to say that the latest sync failed. When the newest `data_quality_run` is `failed` (a check newer than the current result failed), the page response sets `latestCheckFailed: true` and the page shows S-DQ-CHECK-FAILED (analyze I6), so a broken check is never mistaken for an up-to-date result. A run with zero changed rows still validates (edge case).

The coordinator gets the validator as a new **optional** constructor parameter (`IDataQualityValidator? dataQuality = null`) so the existing test constructions in `SyncSchedulingTests` and `SourceMapGuardTests` compile unchanged.

## R13. Affected reports per issue type (FR-024, FR-027)

**Decision.** `DataQualityCatalog` (pure) holds, per issue type, the report keys it affects (keys from R9):

| Issue type | Affected report keys |
|---|---|
| `missing-values` | all 11 |
| `duplicate-records` | all 11 |
| `broken-relationships` | overview, vendors, by-vendor, by-product, by-category, top-products, returns, transactions, vendors-compare, drill-down |
| `inconsistent-product-ids` | overview, vendors, by-product, by-category, top-products, returns, drill-down |
| `invalid-dates` | all 11 |
| `unexpected-quantities` | vendors, by-vendor, by-product, by-category, top-products, returns, vendors-compare, drill-down |
| `inconsistent-amounts` | all 11 |

"All 11" = `overview`, `vendors` (directory and profile), `by-vendor`, `by-product`, `by-category`, `trend`, `top-products`, `returns`, `transactions`, `vendors-compare`, `drill-down` (documents list and document lines). Reasoning: a broken vendor/product reference moves amounts into «نامشخص» rows but not the trend total; product identity affects product-grouped views only; quantities affect only the views that show quantities (the Overview and the trend show amounts only; transactions show document amounts). The Data quality page shows exactly this list, and the notice uses exactly this list, so they never contradict (FR-027).

## R14. Data-quality notice on report responses (FR-027, FR-028, SC-007)

**Decision.** Every report response gets a new top-level field `dataQuality` (null when there is nothing to warn about): `{ issueTypes: [{type, label, count}], affectedRecords }`. It is computed by `DataQualityNoticeService.ForScopeAsync(reportKey, scope, searchTarget, vendorKeys?)` with one SQL statement built from the **same** `ReportSql.BaseCte` (so the scope is exactly the report's lines: period, vendor/product/category/docType filters and search), joined to the current run's issues of the mapped types:

- a line issue is in scope when its `line_id` is a scope line;
- a document issue when its `document_id` has a scope line;
- a product issue when its `product_id` is the product of a scope line.

`count` = distinct flagged records per type; `affectedRecords` = distinct records over all types (a record with several issues counts once). No successful validation run → `dataQuality: null` (reports already show "data unavailable" before the first sync). Figures are untouched.

- **Comparison period (analyze I8, FR-027).** When the response shows comparison or previous-period figures (`compare=true`, i.e. `ReportQueryContext.PreviousScope` non-null, on reports, the vendor directory and profile; the Overview whenever its previous-month `comparison` is shown), the notice scope is the current scope **plus** the comparison scope: the notice SQL returns distinct `(issue_type, record_ref)` pairs per scope, the service merges them and counts, so a record in both periods counts once.
- **Check failure (analyze I7).** A timeout or error of the notice query returns `dataQuality: {unavailable: true, issueTypes: [], affectedRecords: 0}` and the page shows the neutral note S-NOTICE-UNAVAILABLE. Returning `null` would look exactly like "no flagged records" and could mislead; the figures themselves are unaffected either way.

Where it is computed:

- `PurchasingReportService.ResolveAsync` computes it for the context scope and puts it in `ReportEnvelope.DataQuality`; every place that copies the envelope into a response copies it too. The trend recomputes it for its 12-month window scope; the comparison passes the selected vendor keys (`vendorKeys` parameter); the document lines endpoint uses report key `drill-down` with the document's scope; the profile uses its period and vendor.
- `PurchasingOverviewService.GetAsync` computes it for the month (`overview`, no filters), plus the previous month when the comparison is shown.
- Print twins carry the same field, and the print layout renders it (FR-028).

The link to Data quality is a UI decision: the SPA shows the link only when `modules.dataQuality` is true (Admin); the API endpoint stays Admin-only regardless.

**Alternatives.** A separate notice endpoint (spec says "the server MUST mark the response"; a second request could also disagree with the first); denormalizing flags onto `mabna.*` rows (writes into the synced copy, couples sync and validation).

## R15. Reserved vendor-financials API (FR-005)

**Decision.** `VendorFinancialsController` at `/api/vendor-financials` with policy `vendor.financials`: `GET /api/vendor-financials/summary` and `GET /api/vendor-financials/vendors/{vendorKey}` both return `200 {available:false, reason:"not-available-until-phase-6", message:"داده در دسترس نیست.", dataLabel}`. It reads nothing and computes nothing. Not demo-only. Admin and Purchasing get 403 + `access.denied` audit; Management and Finance get the "data unavailable" body.

## R16. Navigation and "no access" in the SPA (FR-012)

**Decision.** `/api/auth/me` and `/api/system/status` return the role, its Persian label and the permission list; `SystemModules` gains `users`, `auditLog`, `dataQuality` (Admin) and computes the rest from permissions. The SPA keeps its tab switcher but selects the view from `location.hash` (`#/overview`, `#/vendors`, `#/reports`, `#/compare`, `#/settings/sync`, `#/settings/users`, `#/settings/audit`, `#/settings/data-quality`, `#/demo`) so a section can be opened directly and the notice can link to `#/settings/data-quality?types=a,b`. A hash naming a view the user may not see renders `<NoAccess/>` («شما به این بخش دسترسی ندارید.»); any API 403 inside a page renders the same component. Tabs are hidden when not allowed.

**Rationale.** Minimal change to the existing no-router shell; no new npm dependency.

## R17. Simulator bad records (FR-030, SC-006)

**Decision.** A tracked, generic generator `deploy/mabna-sim/dq_bad_records.py` reads a purchase source map (the gitignored dev map `deploy/mabna-sim/generated/purchase-source-map.json`, or the tracked `example/purchase-source-map.example.json` with its `Sim*` names) and writes two T-SQL scripts into the gitignored `deploy/mabna-sim/generated/dq/` (a subdirectory, so the init container never runs them): `dq-apply.sql` and `dq-revert.sql`. Object and column names come only from the map, so no real Mabna name is tracked (FR-02 of the simulator). Records are made by **cloning** a valid template row (so unmapped NOT NULL columns and the map's row filters keep valid values) and then updating only mapped columns; FK checks on the touched tables are disabled for the inserts and re-enabled `WITH NOCHECK`. Every inserted row is recorded in a `dbo.SimDqSeed` bookkeeping table, which drives the revert. The script ends with a manifest (type, count) — the expected seeded counts. Details: [contracts/sim-bad-records.md](contracts/sim-bad-records.md).

**Baseline.** The Phase 2 scenario deliberately contains imperfect data (unknown vendor/product, a category key missing from the master), so the "clean" sim is not issue-free for every type. Quickstart records the baseline counts after the first sync (B), expects B + seeded after apply, and B again after revert (US4-6). The "no issues found" rendering is checked on every type whose baseline is 0 and on a fresh install before the first sync (FR-023, US4-3 in the strictest form available off-site).

## R18. Tests within the validation rule (no DB, no network)

New xunit suites under `backend/Zharfa.Api.Tests/`:

Mirrors tasks.md exactly (task ids in brackets):

- `Security/RolePermissionsTests` [T007] — exact matrix of FR-002; unknown role has no permissions; legacy `manager` maps to `management`.
- `Security/PermissionAuthorizationHandlerTests` [T008] — handler succeeds exactly when `RolePermissions.Has`, for every role × permission.
- `Security/AuditingAuthorizationResultHandlerTests` [T009] — 403 body + one `access.denied` entry; 401 without audit; audit failure keeps the 403.
- `Security/UsernameRulesTests` [T030] — ی/ک and ي/ك equality, case folding, length and character rules.
- `Security/PasswordRulesTests` [T031] — 7 vs 8 characters.
- `Security/LastAdminGuardTests` [T032] — demote/deactivate last Admin refused; with two Admins allowed; non-Admin target always allowed.
- `Security/SignInPolicyTests` [T033] — every row of R5's table, including the 5th failure, lock expiry at exactly 15 minutes, success resetting the counter.
- `Security/SessionValidatorTests` [T034] — role change and deactivation applied on the next request.
- `Security/PasswordChangeGateTests` [T035] — only `me`, `change-password`, `logout` and non-API paths pass while a change is required.
- `Security/PermissionMatrixTests` [T055] — reflection over controllers vs expected policy table (R2).
- `Security/VendorFinancialsTests` [T056] — "data unavailable" body, no numbers.
- `Audit/AuditDetailTests` [T010] — allowlist, trimming to 100/500, CR/LF removal, `password=...` scrubbed; view params (`page`, `pageSize`, `sort`, `dir`) separated.
- `Audit/AuditActorTests` [T011] — actor and actor id resolution.
- `Audit/AuditActionsTests` [T012] — catalog codes, labels, report keys.
- `Audit/ReportOpenTrackerTests` [T061] — reload with identical params re-audits; page/sort/dir/pageSize-only changes do not; period or filter change does; per user and per report; print always; no time window.
- `Audit/AuditReportOpenAttributeTests` [T062] — report key resolution, success statuses.
- `Audit/AuditQueryTests` [T063] — Jalali range parsing (Persian digits), `to < from` refused, page bounds.
- `Audit/ReconciliationAuditDetailTests` [T109] — `reconciliation.*` details carry metric, period and filters, never amounts (I9).
- `DataQuality/DataQualityCatalogTests` [T074] — seven types; every affected key exists in the report-key list; `unexpected-quantities` not mapped to `overview`/`trend`; Persian labels non-empty; heuristic and not-checked notes.
- `DataQuality/DataQualityOptionsTests` [T075] — earliest date parsing and default.
- `DataQuality/DataQualitySqlTests` [T076] — the 15 rules, parameters, no writes to `mabna.*`, DU1 partition includes the vendor.
- `DataQuality/RejectedAtSyncTests` [T077] — parsing of `rejected:<reason>=<n>` sync-log parts.
- `DataQuality/DataQualityNoticeTests` [T091] — report → type mapping, notice building from merged current + comparison pairs, `CheckUnavailable` shape.
- `Platform/DataModeTests` [T036] — Bootstrap tests updated: Manager keys no longer required; `AdminPasswordFile` accepted; short value refused.
- `Platform/DemoOnlyFilterTests` [T013] — `SystemModules.For(mode, permissions)` replaces `For(mode, isAdmin)`; AI flag now follows `ai.query` (Admin false).

SQL rules, notices and HTTP behavior are proven in [quickstart.md](quickstart.md) on Docker Compose with the simulator.

## R19. Dependencies

No new NuGet or npm packages. Everything uses ASP.NET Core authorization/cookies, Npgsql, EF Core (existing `ZharfaDbContext` for `app_user`/`audit_log`), React and Tailwind already in the repo. The simulator tool is Python 3 standard library only, like `gen_seed.py`.

## R20. Assumptions for the owner (skip mode)

1. Admin role does **not** hold `ai.query` or `export` (FR-002 table); in demo installs the Admin therefore no longer sees the Demo 1 AI chat. Demo 1 pages (sales, inventory, receivables) require `overview` (every role) and stay demo-only.
2. A password reset by the Admin does not force a change at next sign-in (only the first Admin must change, FR-034).
3. The installer-supplied first-Admin password keeps Phase 1's 12-character minimum; passwords set in the app need 8.
4. The two "inconsistent amounts" sub-checks that need a source net amount or document total are deferred until those columns are verified on-site; the type is still checked with the three rules the synced copy supports (R11). **Confirmed by the owner 2026-11-18; recorded in spec FR-020/FR-023/SC-006.**
5. "Duplicate records" in the synced copy means duplicate document numbers per document type **and vendor** (exact duplicates are impossible by constraint). **Owner decision 2026-11-18 (spec FR-020)**; on-site item: confirm whether numbers are unique per type or per vendor and type.
6. "Inconsistent product identifiers" means one normalized product name under several product keys — a heuristic, labelled as such on the page; the code-level rule follows on-site verification of code columns. **Owner decision 2026-11-18 (spec FR-020).**
7. Unconvertible dates (and missing dates or amounts) are rejected at sync and shown in the "rejected at sync" box, not counted as `invalid-dates`/`missing-values`. **Owner decision 2026-11-18 (spec FR-020/FR-023).**
8. ~~Reopening the same report within 30 minutes is not audited again.~~ **Withdrawn 2026-11-18** (contradicted Clarify Q2 A): every load is audited, including a reload with identical parameters; only a request that differs from the previous one for the same report solely in paging/sorting is not (R9).
9. Earliest plausible date defaults to 1390-01-01 until the on-site fiscal-year start is configured.
10. Demo users: `purchasing`/`purchasing123` and `finance`/`finance123` (demo installs only, like Demo 1's `admin`/`manager`).
