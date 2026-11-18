# Implementation Plan: Permissions, Audit, and Data Quality (Release 1, Phase 3)

**Branch**: `zharfa-prd-phase-3` | **Date**: 2026-11-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/005-permissions-audit-quality/spec.md` · Phase ref `zharfa-prd.phases.md#3` · Governing PRD `zharfa-prd.md` v3.0 (F9, F10, F11) · Rules `AGENTS.md` · Foundation `specs/003-real-purchase-data` and `specs/004-vendor-purchasing-reports` (merged)

## Summary

Give every user exactly one of four fixed roles (Admin, Management, Purchasing, Finance), enforce each role's permission set on the server for every endpoint (403 + an audit entry when refused), make role changes and deactivations apply on the very next request, record sign-ins, report opens, administrative and permission changes in the existing append-only audit log with an Admin page to filter it, validate the synced purchasing copy for seven kinds of data problems after every successful sync with an Admin Data quality page, and mark every report response whose scope contains flagged records with a Persian data-quality notice.

Technical approach (details in [research.md](research.md)):

- **Permissions as policies.** Seven permission ids and four role→permission sets in code (`Security/`); one ASP.NET Core policy per permission; every controller moves from `[Authorize(Roles=…)]` to `[Authorize(Policy=…)]`; a custom `IAuthorizationMiddlewareResultHandler` writes the 401/403 bodies and the `access.denied` audit entry (R1, R2). A reflection test pins the endpoint→permission matrix.
- **Fresh principal per request.** `OnValidatePrincipal` reloads the user by id on each request: deactivated → 401, changed role → new claims (R3). A middleware gate enforces the first Admin's password change (R6).
- **Users and roles API + page**, Persian-normalized unique usernames, 8-character passwords, 5-failure/15-minute lockout, last-active-Admin guard under a row lock (R4, R5); first Admin from `Bootstrap__AdminUsername` + password or Docker-secret file (R6); Demo 1 `manager` → `management`, demo users per role (R7).
- **Audit.** `audit_log` gains `target` and `actor_user_id` plus indexes; an `AuditEntry` overload; a report-open action filter with a per-user, per-report previous-request tracker so a paging/sorting-only request adds nothing while every other load (including a reload) is recorded, with no time window (R8, R9); existing `reconciliation.*` details lose their amounts (R8); an Admin list API filtered by user, action and Jalali date range (R10).
- **Data quality.** After each succeeded sync the coordinator runs `DataQualityValidator`: fixed SQL rules over `mabna.*` write the current run's issues into `data_quality_run` / `data_quality_issue`; a failed sync leaves the last result (R11, R12). Admin page with counts, ≤ 10 examples, affected reports, the heuristic label of the product-identifier check, the deferred «بررسی نشده» sub-checks, rejected-at-sync counts and a failed-latest-check warning.
- **Notices.** One notice query built from the reports' own `ReportSql.BaseCte`, joined to the current issues of the report's mapped types, run for the current scope and, when comparison figures are shown, the comparison scope; a failed check yields `unavailable: true` (neutral note) instead of no notice; the result travels in `ReportEnvelope` to every report response and print (R13, R14).
- **Simulator.** A generic, map-driven `dq_bad_records.py` clones valid rows into bad ones and reverts them (R17).
- **No new packages.** Migration `0008`, seed edit + seed `0003`.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`), TypeScript 7 / React 19, SQL (PostgreSQL 18), T-SQL (simulator scripts only), Python 3 (simulator tool, standard library)

**Primary Dependencies**: ASP.NET Core (cookie auth, authorization policies, MVC filters), EF Core (existing `ZharfaDbContext` for `app_user`, `audit_log`), Npgsql (`NpgsqlDataSource`, as reports and sync), Vite 8 + Tailwind 4. **No new NuGet or npm packages.**

**Storage**: PostgreSQL. Migration `db/migrations/0008_permissions_audit_quality.sql`: `app_user` columns and role check, `audit_log` columns and indexes, `data_quality_run`, `data_quality_issue` ([data-model.md](data-model.md)). Seeds: `db/seed/0002_sales_cash_users.sql` (role literal), new `db/seed/0003_role_users.sql`. No change to `mabna.*`.

**Testing**: xunit (`backend/Zharfa.Api.Tests`), pure logic only — no DB or network (validation rule) — new suites listed in research R18. HTTP, SQL rules, notices and UI are proven by [quickstart.md](quickstart.md) on Docker Compose with the simulator and seeded bad records.

**Target Platform**: Linux containers (Docker Compose) on the company LAN server; browsers and the unchanged WebView2 desktop shell.

**Project Type**: Web application (ASP.NET Core API + React SPA).

**Performance Goals**: Users and roles, Audit log (one year of entries) and Data quality pages p95 < 2 s (FR-032, SC-008); per-request user reload is one primary-key lookup; the notice adds one indexed query per report (15 s timeout; a timeout drops the notice, never the report); validation of three years of synced data well inside the sync window.

**Constraints**: Server-side authorization for every endpoint; audit entries and logs never contain passwords, hashes, tokens, keys, credentials or result rows; audit append-only; validation reads the synced copy only, never Mabna; figures never change because of a flag; Mabna read-only; real and demo data never mix (data label on every new page and in the notice); Persian RTL, Jalali, Persian digits; WCAG 2.1 AA, keyboard-usable; secrets in env/Docker secrets only.

**Scale/Scope**: ~20 users, 4 roles, 7 permissions, ~100 k audit entries per year, synced copy of three years (~416 k lines). 1 migration, 2 seeds, 4 new controllers (users, audit, data quality, vendor financials), 1 extended controller (auth), ~25 backend files, 4 SPA pages + 2 components, 1 simulator tool.

**Unknowns**: None open for planning. Two "inconsistent amounts" sub-checks depend on source columns that are not verified on-site and are deferred with a visible "not checked" note; the product-identifier check is a labelled name heuristic until code columns are verified (R11; owner decisions 2026-11-18 in spec Clarifications). Owner-facing assumptions: research R20.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md` is still the unfilled template, so the binding gates are `AGENTS.md` and `zharfa-prd.md` (§8–§9), as in Phases 1–2.

| Gate (AGENTS.md / PRD) | Pre-design | Post-design | How the design satisfies it |
|---|---|---|---|
| Authorization enforced server-side; frontend hiding is not enforcement | Pass | Pass | Policies on every endpoint, evaluated before the controller; reflection matrix test; fresh principal per request (R2, R3); SPA hiding is cosmetic (R16) |
| Roles are permission sets; R1 seeds Admin, Management, Purchasing, Finance; vendor financials Management + Finance | Pass | Pass | `RolePermissions` table (R1); reserved vendor-financials API guarded by `vendor.financials` (R15) |
| Secrets only in server env / Docker secrets; never in repo, bundle or logs | Pass | Pass | First Admin from env or secret file, never logged/audited; demo passwords only in demo seeds (as Demo 1); `AuditDetail` + `RedactionHelper` (R6, R8) |
| Mabna read-only; never `HOST_ID()` views live | Pass | Pass | Validation and notices read `mabna.*` in PostgreSQL only; the sync is unchanged (R11); simulator scripts run against the simulator only (R17) |
| Reports never built on assumed Mabna structures | Pass | Pass | Rules use only already-synced, status-gated items; net/total checks deferred until verified (R11) |
| Reports read the synced copy, never Mabna per request | Pass | Pass | Notice SQL uses `ReportSql.BaseCte` over `fn_purchase_lines` (R14) |
| Financial formulas separate; nothing presented as another | Pass | Pass | No new figure; flags never change figures (FR-026, quickstart C6); vendor financials answer "data unavailable" (R15) |
| AI only via approved tools; production refuses AI until Phase 4 | Pass | Pass | AI endpoint gets `ai.query` policy and stays `[DemoOnly]` |
| Real and synthetic never mix | Pass | Pass | Demo users only in demo installs (R7); `dataLabel` on new responses; bad records only in the simulator |
| Persian-first RTL, Jalali | Pass | Pass | Persian labels from the server catalogs; Jalali date filters and times (R10) |
| Prefer existing abstractions; justify dependencies | Pass | Pass | Reuses `AuditLogger`, `PasswordHasher`, `RedactionHelper`, `ConfigurationKeys`, `SyncTimeText`, `JalaliDate`, `ReportSql`, `ReportEnvelope`; no new packages (R19) |
| Update docs when behavior changes | Pass | Pass | README, `deploy/README.md`, `.env.example`, `deploy/mabna-sim/README.md`, `.ainative/project.yaml` comment, onsite checklist item (below) |

No violations; Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/005-permissions-audit-quality/
├── plan.md               # This file
├── research.md           # Phase 0: decisions R1–R20
├── data-model.md         # Phase 1: tables, rules, catalogs, migration content
├── quickstart.md         # Phase 1: validation flows A–E mapped to the exit criteria
├── contracts/
│   ├── api.md            # Endpoint/permission matrix, users, audit, data quality, notice, status
│   └── sim-bad-records.md# Simulator bad-record generator contract
├── checklists/requirements.md
└── tasks.md              # Phase 2 (/speckit.tasks)
```

### Source Code (repository root) — files to add (A) or modify (M)

```text
db/
├── migrations/0008_permissions_audit_quality.sql        A  data-model §1.4, §2.1, §3.1
└── seed/
    ├── 0002_sales_cash_users.sql                        M  'manager' role literal → 'management'
    └── 0003_role_users.sql                              A  setval + purchasing/finance demo users
db/perf/audit-1y.sql                                     A  ~100 k synthetic audit rows (throwaway DB only)
db/perf/data-quality-50k.sql                             A  ~50 k synthetic data_quality_issue rows on real synced ids (throwaway DB only)

backend/Zharfa.Api/
├── Program.cs                                           M  policies, handlers, result handler, SessionValidator in cookie events,
│                                                           PasswordChangeGate after UseAuthentication, DataQualityOptions,
│                                                           register new services (see "Wiring" below)
├── Security/                                            A  (new folder)
│   ├── Permissions.cs            ids + Persian labels
│   ├── Roles.cs                  ids, labels, Normalize
│   ├── RolePermissions.cs        For/Has
│   ├── PermissionRequirement.cs  + PermissionAuthorizationHandler.cs
│   ├── AuditingAuthorizationResultHandler.cs   401/403 JSON + access.denied (uses AccessDenied)
│   ├── AccessDenied.cs           shared 403 body + audit (also used by the transactions check)
│   ├── SessionValidator.cs       OnValidatePrincipal (R3)
│   ├── PasswordChangeGate.cs     middleware (R6)
│   ├── SignInPolicy.cs           pure lockout logic (R5)
│   ├── UsernameRules.cs, PasswordRules.cs, LastAdminGuard.cs
│   └── ClaimsPrincipalExtensions.cs
├── Users/UserAdminService.cs                            A  list/create/role/password/deactivate/reactivate/unlock; FOR UPDATE guard
├── Audit/                                               A
│   ├── AuditActions.cs           catalog + Persian labels
│   ├── ReportKeys.cs             report keys + Persian names (shared with DataQuality)
│   ├── AuditDetail.cs            allowlist, trim 100/500, scrub
│   ├── ReportOpenTracker.cs      per (user, report) previous-request tracker: paging/sorting-only → no entry; reload → entry (R9)
│   ├── AuditReportOpenAttribute.cs  action filter (report.opened / report.printed)
│   └── AuditQuery.cs + AuditLogService.cs   parse filters (Jalali→UTC) and query
├── DataQuality/                                         A
│   ├── DataQualityCatalog.cs     types, labels, fields, affected reports
│   ├── DataQualityOptions.cs     DataQuality__EarliestJalaliDate
│   ├── DataQualitySql.cs         rule SQL (data-model §3.2)
│   ├── DataQualityValidator.cs   IDataQualityValidator.RunAsync(jobId, finishedUtc)
│   ├── DataQualityService.cs     page read model (contracts §4)
│   └── DataQualityNoticeService.cs  ForScopeAsync (R14): current + comparison scope; failure → unavailable
├── Controllers/
│   ├── AuthController.cs                                M  username_key lookup, SignInPolicy, claims, audit, me, change-password, logout audit
│   ├── UsersAdminController.cs                          A  /api/admin/users, /api/admin/roles
│   ├── AuditLogController.cs                            A  /api/admin/audit, /api/admin/audit/actions
│   ├── DataQualityController.cs                         A  /api/admin/data-quality
│   ├── VendorFinancialsController.cs                    A  /api/vendor-financials/* (reserved)
│   ├── AdminController.cs, SyncAdminController.cs       M  [Authorize(Policy = Permissions.Admin)]; AdminController: amount-free
│   │                                                       reconciliation audit detail (static ReconciliationAuditDetail)
│   ├── OverviewController.cs                            M  policy overview + [AuditReportOpen("overview")]
│   ├── PurchasingReportsController.cs                   M  policies, [AuditReportOpen(...)] per action, transactions check
│   ├── AiController.cs                                  M  policy ai.query (keeps [DemoOnly])
│   └── SalesController.cs, InventoryController.cs, ReceivablesController.cs, MetaController.cs   M  policy overview (keep [DemoOnly])
├── Data/
│   ├── ZharfaDbContext.cs                               M  AppUser + AuditLog new properties
│   └── AuditLogger.cs                                   M  AuditEntry overload, actor_user_id, target
├── Platform/
│   ├── BootstrapAccounts.cs                             M  first Admin only, AdminPasswordFile, must_change_password (R6)
│   ├── SystemModules.cs                                 M  For(mode, permissions) + users/auditLog/dataQuality
│   └── SystemStatusController.cs                        M  role, roleLabel, permissions, modules
├── Mabna/Sync/
│   ├── SyncCoordinator.cs                               M  optional IDataQualityValidator; run after succeeded (R12)
│   └── SyncServiceCollectionExtensions.cs               M  register IDataQualityValidator singleton
└── Purchasing/
    ├── PurchasingReconciliation.cs                      M  static MismatchAuditDetail: metric/period/filters only, no amounts (FR-015)
    ├── PurchasingOverviewService.cs                     M  notice for the month (`overview`) + previous month when the comparison is shown
    └── Reports/
        ├── ReportDtos.cs                                M  DataQualityNotice records; `DataQuality` last param (default null) on envelope + responses
        ├── ReportSql.cs                                 M  DataQualityNotice(SearchTarget) SQL
        ├── PurchasingReportService.cs                   M  inject notice service; ResolveAsync sets envelope.DataQuality; trend window recompute; copy in Table()/Print()/lines
        ├── VendorDirectoryService.cs                    M  copy DataQuality into directory/print/profile responses
        └── VendorComparisonService.cs                   M  notice with vendorKeys; copy into response

backend/Zharfa.Api.Tests/                                (mirrors tasks.md and research R18)
├── Security/  RolePermissionsTests, PermissionAuthorizationHandlerTests, AuditingAuthorizationResultHandlerTests,
│              UsernameRulesTests, PasswordRulesTests, LastAdminGuardTests, SignInPolicyTests, SessionValidatorTests,
│              PasswordChangeGateTests, PermissionMatrixTests, VendorFinancialsTests                                    A
├── Audit/     AuditDetailTests, AuditActorTests, AuditActionsTests, ReportOpenTrackerTests, AuditReportOpenAttributeTests,
│              AuditQueryTests, ReconciliationAuditDetailTests                                                          A
├── DataQuality/ DataQualityCatalogTests, DataQualityOptionsTests, DataQualitySqlTests, RejectedAtSyncTests,
│                DataQualityNoticeTests                                                                                 A
└── Platform/  DataModeTests (Bootstrap cases), DemoOnlyFilterTests (SystemModules.For)                                M

frontend/src/
├── services/api.ts                                      M  Me/permissions, SystemModules flags, users/audit/data-quality/vendor-fin calls, DataQualityNotice type on envelopes + overview
├── App.tsx                                              M  hash views, Settings tabs, NoAccess, role label from server, forced ChangePassword, 403 → NoAccess
├── pages/UsersRoles.tsx                                 A  list, create, role select, reset password, deactivate/reactivate, unlock, role/permission descriptions
├── pages/AuditLog.tsx                                   A  filters (user, action, Jalali from/to), paged table, empty state
├── pages/DataQuality.tsx                                A  sync status, check time, per-type cards (count, examples table, affected reports, not-checked), rejected-at-sync, focus via ?types=
├── pages/ChangePassword.tsx                             A  forced change form
├── pages/Login.tsx                                      M  423 message; mustChangePassword hand-off
├── components/NoAccess.tsx                              A
├── components/DataQualityNotice.tsx                     A  notice box; link only when modules.dataQuality
├── components/PrintReport.tsx                           M  render notice in PrintHeader
└── pages/OverviewPurchasing.tsx, Vendors.tsx, VendorProfile.tsx, PurchasingReports.tsx, VendorComparison.tsx, components/DrillDownDialog.tsx   M  render <DataQualityNotice/>

deploy/
├── mabna-sim/dq_bad_records.py                          A  contracts/sim-bad-records.md
├── mabna-sim/README.md                                  M  "Data-quality bad records" section
├── .env.example                                         M  drop Manager bootstrap keys; add Bootstrap__AdminPasswordFile, DataQuality__EarliestJalaliDate
├── docker-compose.yml                                   M  pass the two new env keys; drop Manager keys
└── README.md                                            M  roles, demo accounts, first Admin, lockout

README.md, .ainative/project.yaml (comment: spec 005 suites), specs/002-mabna-accounting-connector/onsite-checklist.md (items: first fiscal-year start for DataQuality__EarliestJalaliDate; line net amount / document total columns; product code columns; document-number uniqueness per type or per vendor and type)   M
```

**Structure Decision**: Existing web-application layout (`backend/Zharfa.Api`, `backend/Zharfa.Api.Tests`, `frontend/`, `db/`, `deploy/`). New backend code is grouped by concern in `Security/`, `Users/`, `Audit/`, `DataQuality/`, next to the existing `Platform/`, `Purchasing/`, `Mabna/` folders.

### Wiring in `Program.cs` (order matters)

1. After `SyncOptions` checks: `var dqOptions = DataQualityOptions.FromConfiguration(builder.Configuration); builder.Services.AddSingleton(dqOptions);`
2. Services: `ReportOpenTracker` (singleton), `IAuthorizationHandler → PermissionAuthorizationHandler` (singleton), `IAuthorizationMiddlewareResultHandler → AuditingAuthorizationResultHandler` (singleton; resolves `IAuditLogger` from `context.RequestServices`), `UserAdminService`, `AuditLogService`, `DataQualityService`, `DataQualityNoticeService` (scoped), `IDataQualityValidator → DataQualityValidator` (singleton, in `AddPurchasingSync`).
3. Cookie options: keep name/flags; `options.Events.OnValidatePrincipal = SessionValidator.ValidateAsync;` keep the 401/403 redirect handlers as fallbacks.
4. `builder.Services.AddAuthorization(o => { foreach (var p in Permissions.All) o.AddPolicy(p, b => b.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(p))); });`
5. Pipeline: `app.UseAuthentication(); app.UseMiddleware<PasswordChangeGate>(); app.UseAuthorization(); app.MapControllers();`
6. `BootstrapAccounts.EnsureAsync` stays where it is (after migrations and the data-mode gate).

### Report-open audit placement

| Action | Attribute |
|---|---|
| `OverviewController.Purchasing` | `[AuditReportOpen("overview")]` |
| `PurchasingReportsController.Report` / `ReportPrint` | `[AuditReportOpen("{reportId}")]` / `[AuditReportOpen("{reportId}", Print = true)]` |
| `Vendors` / `VendorsPrint` | `"vendors"` / print |
| `VendorProfile` | `"vendor-profile"` |
| `VendorCompare` / `VendorComparePrint` | `"vendors-compare"` / print |
| `Documents` / `DocumentsPrint` | `"drill-down"` / print |
| `DocumentLines` | `"drill-down-lines"` (detail adds `documentId` from the route) |
| `Lookup` | none |

### Implementation order (for tasks)

1. Migration `0008`, seeds, EF entity changes, `Security/` pure classes + tests (no behavior change yet).
2. Policies, result handler, session validator, password gate; switch every controller to policies; `PermissionMatrixTests`; Auth controller rework; `SystemModules`/status; Bootstrap rework + tests.
3. Users admin service/controller; audit entry overload, action catalog, report-open filter/tracker, audit list API.
4. Data quality: options, catalog, SQL rules, validator + coordinator hook, page API; notice service, envelope/DTO propagation.
5. Vendor-financials reserved controller.
6. Frontend: api types, App hash views + NoAccess + Settings tabs, Login/ChangePassword, UsersRoles, AuditLog, DataQuality pages, DataQualityNotice in report pages and print.
7. Simulator tool + README; docs and env examples; `db/perf/audit-1y.sql`, `db/perf/data-quality-50k.sql`.

## Complexity Tracking

No constitution violations to justify.
