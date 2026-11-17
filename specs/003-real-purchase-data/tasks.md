---
description: "Task list for Real Purchase Data (Release 1, Phase 1)"
---

# Tasks: Real Purchase Data (Release 1, Phase 1)

**Input**: Design documents from `specs/003-real-purchase-data/` — [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/api.md](contracts/api.md), [contracts/source-map.md](contracts/source-map.md), [contracts/cli-and-config.md](contracts/cli-and-config.md), [quickstart.md](quickstart.md)

**Phase ref**: `zharfa-prd.phases.md#1` · Governing PRD `zharfa-prd.md` v3.0 (F1–F4) · Rules `AGENTS.md`

**Tests**: Included. The plan (research R15) and quickstart §1 require pure xunit tests in `backend/Zharfa.Api.Tests` (no database, no network, no new packages). End-to-end checks are quickstart Flows 1–11, run by the tester, not by workers. Workers do not run installs or validation commands (AGENTS.md).

**Organization**: Tasks are grouped by user story so each story can be implemented and checked on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: User story from spec.md (US1–US6)
- Every task names its exact file path(s)

## Path Conventions

Web application (existing layout): `backend/Zharfa.Api/`, `backend/Zharfa.Api.Tests/`, `frontend/src/`, `db/migrations/`, `deploy/`, `docs/`. No new projects and **no new packages** (backend, tests or frontend).

## Hard rules for every task

- Never write to Mabna. Read only through `IMabnaReader` (`backend/Zharfa.Api/Mabna/MabnaReader.cs`); do not add aggregates, joins or raw SQL to `MabnaSqlBuilder`/`MabnaSqlGuard` (research R4).
- Never commit real Mabna table, column or code names, rows or totals. Real names live only in the gitignored source map; committed examples use made-up `Sim*` names.
- Never read `HOST_ID()`-filtered report views on live Mabna.
- Secrets only from environment; scrub every stored or logged error with `RedactionHelper.Scrub`.
- Amounts are `decimal` / `numeric(24,4)` end to end, serialized as JSON strings; no floating point.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Configuration surface, fixtures and folders the rest of the work needs.

- [X] T001 Create the committed example source map with made-up names (`dbo.SimVendor`, `dbo.SimProduct`, `dbo.SimInvDoc`, `dbo.SimRetDoc`, `dbo.SimInvDocItem`) following the shape in contracts/source-map.md exactly (`version: 1`, `verifiedOn`, `discountBasis`, `entities.vendor|product|purchaseInvoice|purchaseReturn|purchaseLine`, every item with `status`, `hostFiltered: false`, `change.strategy`), plus a matching example allowlist in `deploy/mabna-sim/example/purchase-source-map.example.json` and `deploy/mabna-sim/example/purchase-allowlist.example.txt`
- [X] T002 [P] Link the example map and allowlist into the test project as content copied to the output directory (no new package) in `backend/Zharfa.Api.Tests/Zharfa.Api.Tests.csproj`
- [X] T003 [P] Add options classes bound from configuration with the defaults from contracts/cli-and-config.md §2: `SyncOptions` (`Sync__NightlyTime` default `02:00`, `Sync__TimeZone` default `Asia/Tehran`, `Sync__WeeklyFullLoadDay` default `Friday` with `None` disabling, `Sync__PageSize` default `5000` and ≤ `MabnaSource__MaxRows`, `Sync__MaxRejectedRows` default `0`, `Sync__FaultInjection__FailAfterTables` nullable), `MabnaSyncOptions` (`MabnaSync__SourceMapFile`, unset → sync disabled with reason `source-not-configured`), `PurchasingOptions` (`Purchasing__DiscountBasis` = `line` | `lineAndDocument`, default `line`) in `backend/Zharfa.Api/Mabna/Sync/SyncOptions.cs` and `backend/Zharfa.Api/Purchasing/PurchasingOptions.cs`
- [X] T004 [P] Add every new key with an empty value and a one-line comment (`ZHARFA_DATA_MODE` required, `Bootstrap__*`, `MabnaSync__SourceMapFile`, `MabnaSource__CommandTimeoutSeconds=60`, `Sync__*`, `Purchasing__DiscountBasis`) to `deploy/.env.example`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema, data mode, shared text/date helpers, source-map model and the status endpoint that every story builds on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Schema

- [X] T005 Write migration `db/migrations/0005_purchasing_sync.sql` (forward-only, idempotent guards like 0001–0004) creating, per data-model.md §1–§3 verbatim:
  - schema `mabna`;
  - `mabna.vendor` (`vendor_id bigint identity PK`, `source_key text NOT NULL UNIQUE`, `name_fa text NOT NULL`, `is_active_source boolean NULL`, `synced_at timestamptz NOT NULL`);
  - `mabna.product` (`product_id bigint identity PK`, `source_key text NOT NULL UNIQUE`, `name_fa text NOT NULL`, `drug_group_key text NULL`, `synced_at timestamptz NOT NULL`);
  - `mabna.purchase_document` (`document_id bigint identity PK`, `source_key text NOT NULL`, `doc_type text NOT NULL CHECK IN ('invoice','return')`, `doc_number text NULL`, `vendor_source_key text NULL`, `vendor_id bigint NULL → mabna.vendor`, `doc_date date NOT NULL`, `jalali_year/jalali_month/jalali_day smallint NOT NULL` with month 1..12 and day 1..31 checks, `cancelled boolean NOT NULL DEFAULT false`, `doc_discount numeric(24,4) NOT NULL DEFAULT 0` ≥ 0, `synced_at timestamptz NOT NULL`; UNIQUE (`doc_type`, `source_key`); indexes (`jalali_year`,`jalali_month`) WHERE NOT `cancelled`, (`vendor_id`), (`doc_date`));
  - `mabna.purchase_line` (`line_id bigint identity PK`, `document_id bigint NOT NULL → mabna.purchase_document ON DELETE CASCADE`, `source_key text NOT NULL`, `product_source_key text NULL`, `product_id bigint NULL → mabna.product`, `quantity numeric(24,4) NULL`, `gross_amount numeric(24,4) NOT NULL`, `line_discount numeric(24,4) NOT NULL DEFAULT 0` ≥ 0, `vat_amount numeric(24,4) NULL`; UNIQUE (`document_id`,`source_key`); index (`product_id`));
  - extensions of reserved `sync_job` (`trigger text NOT NULL CHECK IN ('manual','scheduled','startup-recovery')`, `mode text NOT NULL CHECK IN ('incremental','full')`, `status` CHECK IN (`running`,`succeeded`,`failed`,`skipped`), `requested_by text NULL`, `duration_ms bigint NULL`, `error_message text NULL`, `skip_reason text NULL`; partial unique index `ON sync_job ((true)) WHERE status = 'running'`), `sync_log` (`table_logical text NULL`, `rows_read/rows_inserted/rows_updated/rows_deleted/rows_rejected integer NULL`, `level` CHECK IN (`info`,`warning`,`error`), FK `sync_job_id → sync_job(id)`), `source_record` (`table_logical text NOT NULL` unique with `source_system`, `strategy text NOT NULL CHECK IN ('rowversion','modifiedAt','idWatermark','full')`, `watermark text NULL`, `last_full_load_at timestamptz NULL`; `payload` left unused);
  - new `install_setting` (`key text PK`, `value text NOT NULL`, `set_at timestamptz NOT NULL`) and `reconciliation_run` (`id bigint identity PK`, `run_at timestamptz NOT NULL`, `jalali_month text NOT NULL` `YYYY-MM`, `filter_kind text NOT NULL CHECK IN ('none','vendor','product')`, `filter_value text NULL`, `metric text NOT NULL CHECK IN ('net','gross','returns','vat')`, `system_total/source_total/difference numeric(24,4) NULL`, `outcome text NOT NULL CHECK IN ('pass','mismatch','unavailable','error')`, `discount_basis text NOT NULL`, `data_mode text NOT NULL`).
  Check existing column definitions in `db/migrations/0001_init_canonical.sql` first and only ALTER what is missing.

### Data mode core (blocks US2–US6)

- [X] T006 Implement `DataModeOptions` in `backend/Zharfa.Api/Platform/DataModeOptions.cs`: reads `Zharfa__DataMode`, accepts only `production` | `demo` (case-insensitive), exposes `IsProduction`, `IsDemo`, `DataLabel` (`real` | `demo`); a pure `Validate(string? raw)` returning a Persian + English error naming the key when missing or invalid
- [X] T007 Implement the startup data-mode gate in `backend/Zharfa.Api/Platform/DataModeGate.cs`: after migrations, write `install_setting(key='data_mode')` on first start; refuse to start when the configured mode differs from the stored one, or when mode is `production` and any `db/seed/*` file name is recorded in `schema_migrations`; pure decision method `Decide(configured, stored, appliedSeedNames)` for unit tests
- [X] T008 Change `backend/Zharfa.Api/Data/MigrationRunner.cs` so the seed step runs only in demo mode (production never applies `db/seed/*`), keeping current behaviour for demo installs
- [X] T009 Implement production first-run accounts in `backend/Zharfa.Api/Platform/BootstrapAccounts.cs`: when mode is `production` and `app_user` is empty, create Admin and Manager from `Bootstrap__AdminUsername/Password` and `Bootstrap__ManagerUsername/Password` (password ≥ 12 chars, hashed with the existing `PasswordHasher`, never logged); refuse to start with a clear message if values are missing or too short
- [X] T010 Wire T006–T009 into `backend/Zharfa.Api/Program.cs`: fail fast before `app.Run()` on a missing/invalid mode; register `DataModeOptions` as a singleton; set `/api/health` `demoData` from the data mode (true only in demo)
- [X] T011 [P] Unit tests for mode parsing and the startup gate (missing, invalid, flip refused, production with seed names refused, first start stores mode) in `backend/Zharfa.Api.Tests/Platform/DataModeTests.cs`

### Shared ingestion helpers

- [X] T012 [P] Implement `PersianText.Normalize` in `backend/Zharfa.Api/Mabna/Sync/PersianText.cs`: ي→ی, ك→ک, trim, collapse internal whitespace, keep ZWNJ (U+200C), keep digits as-is; empty result → caller substitutes `'(بدون نام)'`
- [X] T013 [P] Implement `SourceDateParser` in `backend/Zharfa.Api/Mabna/Sync/SourceDateParser.cs` for formats `jalaliText` (`1403/12/30`, separators `/` or `-`, Persian or Latin digits), `jalaliInt` (`14031230`) and `gregorian` (`date`/`datetime`), returning Gregorian `DateOnly` plus Jalali year/month/day via the existing `backend/Zharfa.Api/Analytics/JalaliDate.cs`; invalid input returns a failure (no exception), including Esfand 30 in a non-leap year
- [X] T014 [P] Unit tests for normalization (ي/ك, ZWNJ kept, whitespace, names differing only by ي/ك compare equal) in `backend/Zharfa.Api.Tests/Sync/PersianTextTests.cs`
- [X] T015 [P] Unit tests for date parsing (all three formats, Persian digits, `1403/12/30` → Esfand 1403, `1404/01/01` → Farvardin 1404, invalid day/month, Esfand 30 in a non-leap year rejected) in `backend/Zharfa.Api.Tests/Sync/SourceDateParserTests.cs`

### Source map model, loader and validator

- [X] T016 Implement the source-map model and loader in `backend/Zharfa.Api/Mabna/Sync/SourceMap.cs`: types for the contracts/source-map.md shape; item `status` ∈ `confirmed | corrected | candidate | unknown`; `IsUsable(item, dataMode)` (production: `confirmed`/`corrected` only; demo: also `candidate`); `column: null` or `unknown` = unusable, not an error; loads from `MabnaSync__SourceMapFile`
- [X] T017 Implement `SourceMapValidator` in `backend/Zharfa.Api/Mabna/Sync/SourceMapValidator.cs` enforcing contracts/source-map.md rules 1–6: unknown `version`/malformed JSON/missing `purchaseInvoice` or `purchaseLine`; identifiers via `MabnaObjectPolicy`; hard-excluded objects (`scuUser`, `UserRights`, `users`, `scu*`, `msg*`) and objects **not on the connector allowlist** rejected; `hostFiltered` `true`, or missing for `kind: view`, rejected; `filter[].op` must be a `MabnaFilterOp` name and `value` a number or string; `date.format` ∈ `jalaliText | jalaliInt | gregorian`, `change.strategy` ∈ `rowversion | modifiedAt | idWatermark | full`, `amountSign` ∈ `asIs | negate | abs`; `purchaseLine.documentKey` required and invoice/return line linkage unambiguous. Returns a list of problems (Persian message + code), never throws
- [X] T018 Implement `MetricAvailability` in `backend/Zharfa.Api/Purchasing/MetricAvailability.cs` from the data-model.md "Metric availability" table: per figure (`net`, `gross`, `returns`, `vat`, `activeVendors`, `topVendors`, `topProducts`, `trend`, `comparison`) the reason `no-successful-sync` | `source-item-unverified` | `source-item-unmapped` | `no-previous-data`, given map + data mode + "has succeeded sync"
- [X] T019 [P] Unit tests for map loading and validation (each rule 1–6 with the example map mutated in memory, candidate usable in demo but not production, unknown/null not an error) in `backend/Zharfa.Api.Tests/Sync/SourceMapValidationTests.cs`
- [X] T020 [P] Unit tests for `MetricAvailability` (each row of the availability table, production vs demo) in `backend/Zharfa.Api.Tests/Purchasing/MetricAvailabilityTests.cs`

### System status (drives labels and last-sync on every page)

- [X] T021 Implement `GET /api/system/status` in `backend/Zharfa.Api/Platform/SystemStatusController.cs` per contracts/api.md §1: any signed-in user; `dataMode`, `dataLabel`, `lastSuccessfulSync` (`at` ISO with offset, `atJalali` with Persian digits, `null` when no `succeeded` job), `modules` (`overview` true; `dataSync` true only for `admin`; `demoPages` and `ai` true only in demo), `serverJalaliToday` in `Sync__TimeZone`
- [X] T022 [P] Add typed calls and types for `SystemStatus`, `PurchasingOverview`, `SyncJob`, `SyncJobsResponse` (amounts as `string`) to `frontend/src/services/api.ts`
- [X] T023 Add the view switcher and status loading to `frontend/src/App.tsx`: fetch `/api/system/status` after login; views Overview (default), Data sync (only when `modules.dataSync`), Demo pages (existing `Dashboard`, only when `modules.demoPages`); no router dependency
- [X] T024 [P] Implement `LastSyncIndicator` in `frontend/src/components/LastSyncIndicator.tsx` (Jalali date/time, Persian digits; "هنوز همگام‌سازی موفقی انجام نشده است" when `null`) and render it in the header of every view from `frontend/src/App.tsx` (FR-012, US2 AC6)

**Checkpoint**: API starts only with a valid data mode; schema in place; map validated; status endpoint and shell ready.

---

## Phase 3: User Story 1 - Verify the purchasing source before building on it (Priority: P1) 🎯 MVP

**Goal**: Every purchasing table, column, code and the discount basis used by a formula has a recorded status, and the simulator holds synthetic purchasing data in the candidate structure.

**Independent Test**: The onsite checklist section 7 and `source-verification.md` list every logical item with a status; unknown items make the dependent figure unavailable (T018/T020); the simulator exposes the purchasing tables (quickstart Flow 11 on-site; Flow 2 counts off-site).

- [X] T025 [US1] Add section "7. Purchasing slice" to `specs/002-mabna-accounting-connector/onsite-checklist.md`: per logical item (vendor/product master key + name; invoice and return header key, number, vendor key, document date + format, cancelled rule, operation codes, document discount; line key, document key, product key, quantity, gross amount + sign, line discount, VAT; change marker per table; discount basis of Mabna's own purchase report total; amount unit Rial) the check to run with catalog metadata or owner-approved aggregate probes only, where to record it (on-site source map), and the rule that only `confirmed`/`corrected` objects join the live allowlist and `HOST_ID()` report views are never read
- [X] T026 [P] [US1] Create `specs/003-real-purchase-data/source-verification.md` with one row per logical item from T025 (logical names only, no real names, no rows), columns status (`unknown` initially / `candidate` where an off-site candidate exists), verified date, note, plus an empty "On-site reconciliation" table (month label, outcome pass/fail only)
- [X] T027 [P] [US1] Document the purchasing simulator steps in `deploy/mabna-sim/README.md`: regenerate with the candidate purchasing tables as `gen_schema.py --roots`, add a purchasing section to the gitignored `generated/scenario.py` (vendors with ي/ك in names, documents dated `1403/12/30` and `1404/01/01`, a return in a later month than its invoice, a cancelled document, a line with no vendor/product, an empty month), produce gitignored `generated/purchase-source-map.json`, `generated/allowlist.txt` and `generated/src/purchasing-changes.sql` (quickstart Flow 6), and how to copy `generated/` into a worktree
- [X] T028 [US1] If the generator scripts need code changes to emit the purchasing scenario (vendor/product masters, invoice/return headers, lines with VAT and discounts, the edge cases listed in T027), make them generic (no real names) in `deploy/mabna-sim/gen_seed.py` and/or `deploy/mabna-sim/seedgen.py`; keep all real-schema-derived output under `deploy/mabna-sim/generated/` (gitignored)

**Checkpoint**: Verification record exists and gates formulas through map statuses; off-site work proceeds on `candidate` items in demo mode only.

---

## Phase 4: User Story 2 - Admin syncs purchasing data and sees the result (Priority: P1)

**Goal**: "Sync now" and the nightly schedule load vendors, products, invoices, returns and lines into `mabna.*` in one transaction, recorded per run and per table, shown on the Data sync page.

**Independent Test**: Quickstart Flows 2–6 and 9 against the simulator.

### Tests for User Story 2

- [X] T029 [P] [US2] Unit tests for the incremental planner (new/changed/deleted keys inside the re-read window, keys outside the window untouched, lines replaced as a set per changed header, first run promoted to `full`, weekly full-load day detection, overlap for `modifiedAt`) in `backend/Zharfa.Api.Tests/Sync/IncrementalPlannerTests.cs`
- [X] T030 [P] [US2] Unit tests for keyset paging (query uses explicit columns, `Gt` on the key, `ORDER BY` key, `Top = PageSize`; stops on a short page; uses a fake `IMabnaReader`, no network) in `backend/Zharfa.Api.Tests/Sync/KeysetPagerTests.cs`
- [X] T031 [P] [US2] Unit tests for the row mapper (`amountSign` `asIs|negate|abs` → positive amounts, cancelled rule `column` and "header total = 0 and no lines", empty name → `'(بدون نام)'`, rejected rows counted, `gross_amount < line_discount` counted as a warning not rejected) in `backend/Zharfa.Api.Tests/Sync/PurchaseRowMapperTests.cs`
- [X] T032 [P] [US2] Unit tests for scheduler timing (next run at `Sync__NightlyTime` in `Asia/Tehran`, weekly full on `Friday`, `None` disables) and for coordinator decisions (manual while running → conflict with running job; scheduled while running → `skipped` with `skip_reason`; fault injection honoured only with `Development` + demo) in `backend/Zharfa.Api.Tests/Sync/SyncSchedulingTests.cs`

### Implementation for User Story 2

- [X] T033 [P] [US2] Implement `KeysetPager` in `backend/Zharfa.Api/Mabna/Sync/KeysetPager.cs`: pages through `IMabnaReader.ReadAsync` with `MabnaQuery` (explicit columns, `MabnaFilter(key, Gt, last)`, map filters as `MabnaFilter` parameters, `MabnaOrder(key)`, `Top = Sync__PageSize`); no raw SQL
- [X] T034 [P] [US2] Implement `PurchaseRowMapper` in `backend/Zharfa.Api/Mabna/Sync/PurchaseRowMapper.cs`: maps source rows to vendor/product/document/line records using the map, `PersianText.Normalize`, `SourceDateParser`, `amountSign`, cancelled rule, doc-type from the entity (`purchaseInvoice` → `invoice`, `purchaseReturn` → `return`); collects rejected rows with key and reason
- [X] T035 [P] [US2] Implement the pure `IncrementalPlanner` in `backend/Zharfa.Api/Mabna/Sync/IncrementalPlanner.cs`: per-table strategy (`rowversion` > watermark; `modifiedAt` ≥ watermark − overlap 10 min; `idWatermark` + `lookbackDays` re-read window default 60 with key diff for deletions; `full` reload) and full-load promotion (first run, weekly day)
- [X] T036 [US2] Implement `SyncRunner` in `backend/Zharfa.Api/Mabna/Sync/SyncRunner.cs`: one run = one PostgreSQL transaction on a dedicated Npgsql connection; order vendor → product → invoice → return → lines; upsert by `source_key` / (`doc_type`,`source_key`), delete + insert lines per changed document, key-diff deletes inside the window; resolve `vendor_id`/`product_id` (NULL when missing from master); update `source_record` watermarks inside the same transaction; fail if rejected rows > `Sync__MaxRejectedRows`; honour `Sync__FaultInjection__FailAfterTables` only when `ASPNETCORE_ENVIRONMENT=Development` and demo mode (warn and ignore otherwise); return per-table counts (read, inserted, updated, deleted, rejected)
- [X] T037 [US2] Implement `SyncCoordinator` in `backend/Zharfa.Api/Mabna/Sync/SyncCoordinator.cs`: validates map + allowlist before each run (503 reasons `source-not-configured` | `source-map-invalid` | `login-not-read-only` via the existing `MabnaReadOnlyGate`); takes a PostgreSQL session advisory lock and inserts the `sync_job` row (`job_name='purchasing'`) relying on the partial unique index; writes `sync_job`/`sync_log` on a separate connection so failures are recorded after rollback; sets `duration_ms`, `error_message` scrubbed with `RedactionHelper.Scrub` and a Persian prefix; on startup marks any `running` job `failed` with "interrupted (service restart)"; runs work in the background and returns immediately
- [X] T038 [US2] Implement `SyncScheduler : BackgroundService` in `backend/Zharfa.Api/Mabna/Sync/SyncScheduler.cs`: wakes at `Sync__NightlyTime` in `Sync__TimeZone`, triggers an incremental (or weekly full) run via `SyncCoordinator` with trigger `scheduled`; overlapping run → `skipped` job with `skip_reason` "a run was already in progress"; pure next-run calculation reused by the jobs endpoint
- [X] T039 [US2] Add `mabna.fn_sync_fingerprint()` → `(table_name, row_count, content_hash)` with `md5(string_agg(... ORDER BY source_key))` over key + amount columns, `STABLE`, `SECURITY INVOKER`, to new migration `db/migrations/0006_purchasing_kpis.sql`
- [X] T040 [US2] Implement `SyncAdminController` in `backend/Zharfa.Api/Controllers/SyncAdminController.cs` with `[Authorize(Roles = "admin")]` per contracts/api.md §4: `POST /api/admin/sync/run` (202 `{job}`, 409 Persian message + running `job`, 503 `{message, reason}`; audit `sync.requested` / `sync.rejected` via `AuditLogger`), `GET /api/admin/sync/jobs?limit=50` (1..200; `jobs` newest first, `running`, `source {configured, mapValid, mapProblems}`, `schedule {nightlyTime, timeZone, nextRunAt, nextRunAtJalali}`), `GET /api/admin/sync/jobs/{id}` (404 unknown); `SyncJob` shape exactly as in the contract with Jalali timestamps
- [X] T041 [US2] Register `SourceMap`, validator, pager, mapper, planner, runner, coordinator (singleton) and `SyncScheduler` (hosted service) plus startup recovery in `backend/Zharfa.Api/Program.cs`
- [X] T042 [P] [US2] Implement the Data sync page in `frontend/src/pages/DataSync.tsx`: «همگام‌سازی اکنون» button (disabled while a run is active), running job shown on 409, job list (trigger, mode, status, Jalali start/end, duration, per-table read/inserted/updated/deleted/rejected, error), source/map problems and next scheduled run; polls `/api/admin/sync/jobs` every 3 s while a run is active; RTL, Persian digits
- [X] T043 [US2] Add the Data sync entry ("تنظیمات ← همگام‌سازی داده") to the view switcher for `modules.dataSync` only in `frontend/src/App.tsx`

**Checkpoint**: Flows 2–6 and 9 pass on the simulator; failed runs leave `mabna.*` and watermarks unchanged.

---

## Phase 5: User Story 3 - Read-only safety of the source connection (Priority: P1)

**Goal**: The guard keeps refusing writes and non-allowlisted objects now that purchasing tables join the allowlist; mapped objects must be allowlisted and never `HOST_ID()` report views.

**Independent Test**: Quickstart Flow 1 (`dotnet test --filter FullyQualifiedName~Mabna`): refusals happen with zero connection opens.

- [X] T044 [P] [US3] Extend `backend/Zharfa.Api.Tests/Mabna/MabnaGuardTests.cs`: INSERT/UPDATE/DELETE/MERGE/EXEC/DDL refused; `scuUser`, `UserRights`, a `scu*` and a `msg*` name refused; the example purchasing tables accepted only when on the allowlist; a fake connection factory records zero opens for every refusal
- [X] T045 [P] [US3] Add map-vs-guard tests in `backend/Zharfa.Api.Tests/Sync/SourceMapGuardTests.cs`: a mapped object missing from the allowlist, a hard-excluded mapped object, and a `view` with `hostFiltered: true` or missing each make the map invalid and `SyncCoordinator`'s pre-run check refuse with `source-map-invalid` before any `IMabnaReader` call
- [X] T046 [US3] Make startup log a warning (no secrets) and expose `mapProblems` when the map is invalid, and refuse `Purchasing__DiscountBasis` mismatching the map's verified basis in production with a hard refusal at startup (failing startup), in `backend/Zharfa.Api/Mabna/Sync/SourceMapValidator.cs` and `backend/Zharfa.Api/Program.cs`
- [X] T047 [US3] Set `MabnaSource__CommandTimeoutSeconds=60` and mount the source map and allowlist read-only from a gitignored path (`MabnaSync__SourceMapFile`, `MabnaSource__AllowlistFile`) in `deploy/docker-compose.yml`

**Checkpoint**: Flow 1 passes; no connector code path changed except tests.

---

## Phase 6: User Story 4 - Purchase figures that match Mabna (Priority: P1)

**Goal**: One documented definition per metric (net, gross, returns, VAT), and a reconciliation CLI that compares system totals with an independent source recomputation and logs the result.

**Independent Test**: Quickstart Flow 7: `reconcile purchasing --month 1405-07` (and with `--vendor`) exits 0 with all metrics `pass`; a forced mismatch exits 1 and writes `audit_log`.

### Tests for User Story 4

- [X] T048 [P] [US4] Unit tests for discount basis and pro-rata allocation (`line`: `gross_amount − line_discount`; `lineAndDocument`: `doc_discount` allocated pro rata by line amount, rounded to 4 decimals, remainder to the largest line so totals stay exact) in `backend/Zharfa.Api.Tests/Purchasing/DiscountAllocationTests.cs`
- [X] T049 [P] [US4] Unit tests for `SourceTotalsCalculator` (invoices minus returns, VAT net = invoice VAT − return VAT, cancelled excluded, return counted in its own month, vendor and product filters, VAT `null` when unmapped, exact decimals) in `backend/Zharfa.Api.Tests/Purchasing/SourceTotalsCalculatorTests.cs`
- [X] T050 [P] [US4] Unit tests for the reconciliation comparator and CLI argument parsing (exact equality per metric, `unavailable` does not fail, any mismatch → exit 1 + `signOffBlocked: true`, errors → exit 2, `--vendor` and `--product` mutually exclusive, production output omits amounts unless `--show-amounts`) in `backend/Zharfa.Api.Tests/Purchasing/ReconciliationComparatorTests.cs`

### Implementation for User Story 4

- [X] T051 [US4] Add `mabna.fn_purchase_totals(p_year smallint, p_month smallint, p_vendor_key text DEFAULT NULL, p_product_key text DEFAULT NULL, p_basis text DEFAULT 'line')` → `gross, returns, net, vat_invoice, vat_return, vat, document_count` (`STABLE`, `SECURITY INVOKER`) implementing data-model.md §4 exactly (non-cancelled documents of the Jalali month; product filter at line level; `lineAndDocument` pro-rata allocation identical to T048 rules) and `mabna.fn_first_data_month()` to `db/migrations/0006_purchasing_kpis.sql`
- [X] T052 [P] [US4] Write the single metric-definitions document `docs/metrics/purchasing.md`: line amount, gross purchases, returns, net purchases (invoices − returns, excl. VAT, by document date), VAT (net, plus invoice/return), active vendors, top 5 vendors/products, comparison, trend; the discount basis in use (`line` until verified on-site) and how to change it; returns dated by their own document; cancelled excluded; unknown vendor/product bucket; explicit statement that purchases are not payments or vendor balance and price differences are never profit; consumers (Overview, reconciliation; later reports/exports/AI)
- [X] T053 [P] [US4] Implement `SourceTotalsCalculator` in `backend/Zharfa.Api/Purchasing/SourceTotalsCalculator.cs`: pure C# over mapped source rows applying the definitions in `docs/metrics/purchasing.md`, sharing no SQL with `fn_purchase_totals`; discount allocation in a shared pure `DiscountAllocator` in `backend/Zharfa.Api/Purchasing/DiscountAllocator.cs`
- [X] T054 [US4] Implement `PurchasingReconciliation` in `backend/Zharfa.Api/Purchasing/PurchasingReconciliation.cs`: requires a `succeeded` sync (else exit 2 "no successful sync"); system side via `mabna.fn_purchase_totals`; source side via fresh guarded `KeysetPager` reads for the month and filter + `SourceTotalsCalculator`; one `reconciliation_run` row per metric (`net`,`gross`,`returns`,`vat`) with basis and data mode; on mismatch `audit_log` `reconciliation.mismatch` (period, filter, metric; no amounts in production); prints one JSON line as in contracts/cli-and-config.md §1; exit codes 0/1/2
- [X] T055 [US4] Add CLI dispatch in `backend/Zharfa.Api/Program.cs`: when `args` start with `reconcile purchasing`, build services and configuration without starting the web host, parse `--month YYYY-MM` (Latin or Persian digits), `--vendor <sourceKey>` | `--product <sourceKey>`, `--basis line|lineAndDocument`, `--show-amounts`, run T054 and return its exit code
- [X] T056 [US4] Ensure the Docker image entrypoint accepts the CLI args (`docker compose -f deploy/docker-compose.yml run --rm api reconcile purchasing --month …`) in `backend/Dockerfile` (the API image referenced by `deploy/docker-compose.yml`): use an `ENTRYPOINT` so extra args reach `Program.cs`

**Checkpoint**: Flow 7 passes on the simulator; definitions documented once.

---

## Phase 7: User Story 5 - Manager sees real purchasing on the Overview (Priority: P2)

**Goal**: Overview page for a chosen Jalali month with totals, comparison, 12-month trend, active vendors, top 5 vendors/products, last-sync, and "data unavailable" where needed.

**Independent Test**: Quickstart Flow 8; Overview net equals reconciliation system net (Flow 7).

### Tests for User Story 5

- [X] T057 [P] [US5] Unit tests for comparison math and month handling (delta, percent rounded to 2 decimals, percent `null` when previous net is 0, comparison `null` with `no-previous-data` before the first synced month, previous month across year boundary 1405-01 → 1404-12, 12-month window oldest first, month parse `1405-08`/Persian digits, `1405-13` invalid) in `backend/Zharfa.Api.Tests/Purchasing/ComparisonMathTests.cs`

### Implementation for User Story 5

- [X] T058 [US5] Add `mabna.fn_purchase_trend(p_year, p_month, p_months int DEFAULT 12, p_basis)` → `(jalali_year, jalali_month, net, has_data)`, `mabna.fn_top_vendors(p_year, p_month, p_limit int DEFAULT 5, p_basis)` and `mabna.fn_top_products(...)` → `(key, name_fa, net, is_unknown)` (NULL vendor/product as «نامشخص», may appear in the list), `mabna.fn_active_vendors(p_year, p_month)` → `integer` (distinct non-NULL `vendor_id`), all `STABLE`, `SECURITY INVOKER`, reusing the line-amount logic of `fn_purchase_totals`, in `db/migrations/0006_purchasing_kpis.sql`
- [X] T059 [US5] Implement `PurchasingOverviewService` in `backend/Zharfa.Api/Purchasing/PurchasingOverviewService.cs`: calls the `mabna.fn_*` functions, applies `MetricAvailability` (all figures `null` with `no-successful-sync` when no succeeded job), comparison rules, trend with `available: false` before the first synced month, zeros for an empty month, `discountBasis` from `PurchasingOptions`, amounts as strings with 4 decimals, `currency: "IRR"`, `currencyLabel: "ریال"`, `monthLabel` in Persian, `dataLabel` from data mode, `lastSuccessfulSync`
- [X] T060 [US5] Implement `OverviewController` in `backend/Zharfa.Api/Controllers/OverviewController.cs`: `GET /api/overview/purchasing?month=YYYY-MM` with `[Authorize(Roles = "manager,admin")]`; missing month → current Jalali month in `Sync__TimeZone`; invalid → 400 Persian envelope; response shape exactly as contracts/api.md §3; register the service in `backend/Zharfa.Api/Program.cs`
- [X] T061 [P] [US5] Implement `JalaliMonthPicker` in `frontend/src/components/JalaliMonthPicker.tsx` (year + month selects, Persian month names, Persian digits, default from `serverJalaliToday`) using `frontend/src/utils/jalali.ts`
- [X] T062 [P] [US5] Implement `KpiCard` in `frontend/src/components/KpiCard.tsx` (amount in «ریال» with Persian digits via `frontend/src/utils/format.ts` from a decimal string, optional delta/percent, «داده در دسترس نیست» when `null`)
- [X] T063 [P] [US5] Implement `TrendChart` in `frontend/src/components/TrendChart.tsx` (inline SVG like `SalesCards`, RTL, 12 months, unavailable months drawn as gaps, not zero)
- [X] T064 [P] [US5] Implement `TopList` in `frontend/src/components/TopList.tsx` (up to 5 rows, name + net, «نامشخص» styled for unknown, fewer rows when fewer exist, unavailable state)
- [X] T065 [US5] Implement `OverviewPurchasing` in `frontend/src/pages/OverviewPurchasing.tsx`: month picker; KPI cards for net, gross, returns, VAT; comparison with the preceding month; trend; active vendors; top 5 vendors and products; last-sync; every figure refetched on month change; data-label badge; RTL layout; make it the default view in `frontend/src/App.tsx`
- [X] T066 [P] [US5] Add an optional throwaway-DB load generator for three years of purchasing data `db/perf/purchasing-3y.sql` (inserts into `mabna.*` only, header comment: never run against a real install), used by Flow 8's p95 < 2 s check

**Checkpoint**: Flow 8 passes; Overview net equals reconciliation net.

---

## Phase 8: User Story 6 - Real and demo data never mix (Priority: P2)

**Goal**: Production installs show no synthetic data, Demo 1 pages or AI; demo installs label everything; mode is server-only.

**Independent Test**: Quickstart Flow 10 (demo stack and a fresh production stack).

### Tests for User Story 6

- [X] T067 [P] [US6] Unit tests for the `[DemoOnly]` filter decision (production → 404 Persian envelope before the handler; demo → passes through) and for `modules` flags by mode and role in `backend/Zharfa.Api.Tests/Platform/DemoOnlyFilterTests.cs`

### Implementation for User Story 6

- [X] T068 [US6] Implement `DemoOnlyAttribute` as an endpoint/action filter in `backend/Zharfa.Api/Platform/DemoOnlyAttribute.cs`: in production return 404 `{ "message": "این بخش در این نسخه فعال نیست." }` without running the handler or any query; authentication still runs first (anonymous → 401)
- [X] T069 [US6] Apply `[DemoOnly]` to every endpoint in contracts/api.md §5: `backend/Zharfa.Api/Controllers/SalesController.cs`, `InventoryController.cs`, `ReceivablesController.cs`, `MetaController.cs` (`/api/meta/categories`), `AdminController.cs` (`/api/admin/reconciliation` and `/api/admin/provider`, the AI provider status kept demo-only per FR-023 and T110; not the new sync routes), `AiController.cs` (all `/api/ai/*`)
- [X] T070 [US6] Ensure every demo-mode AI answer payload carries `"dataLabel": "demo"` and the Persian demonstration notice in `backend/Zharfa.Api/Controllers/AiController.cs` (the AI keeps reading only the synthetic views; it never reads `mabna.*`)
- [X] T071 [P] [US6] Implement `DataModeBadge` in `frontend/src/components/DataModeBadge.tsx` («داده واقعی شرکت» in production, demonstration label in demo) and render `frontend/src/components/DemoDataBanner.tsx` only when `dataMode === "demo"`, both from `frontend/src/App.tsx`
- [X] T072 [US6] Hide Demo pages and AI chat in production (`modules.demoPages`, `modules.ai`) in `frontend/src/App.tsx` and `frontend/src/pages/Dashboard.tsx`; no client-side mode switch anywhere
- [X] T073 [US6] Make `ZHARFA_DATA_MODE` required (`${ZHARFA_DATA_MODE:?…}`) and pass `Zharfa__DataMode`, `Bootstrap__*`, `TZ=Asia/Tehran`, `Sync__*`, `Purchasing__DiscountBasis` through in `deploy/docker-compose.yml`
- [X] T074 [US6] Create the dev override `deploy/docker-compose.purchasing-dev.yml` (`ASPNETCORE_ENVIRONMENT=Development`, `ZHARFA_DATA_MODE=demo`, simulator map + allowlist mounts from `deploy/mabna-sim/generated/`, optional `Sync__FaultInjection__FailAfterTables`, `Sync__PageSize` override) as used in quickstart §0 and Flows 3–4

**Checkpoint**: Flow 10 passes for both modes; mode flip and missing mode refuse to start.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T075 [P] Document data mode, bootstrap accounts, sync settings, the reconcile CLI and the source map location in `deploy/README.md` and `backend/Zharfa.Api/README.md`
- [X] T076 [P] Update `README.md` (Phase 1 features, required `ZHARFA_DATA_MODE`, new pages, link to `docs/metrics/purchasing.md`) and add a Phase 1 entry to `CHANGELOG.md`
- [X] T077 [P] Update `.ainative/project.yaml` `validation_commands` only if a new command is needed (the existing build + `dotnet test` commands should already cover the new tests); keep the comment accurate
- [X] T078 Review all new/changed files for secrets, real Mabna names, floating-point amounts and unscrubbed errors; confirm `git status` shows nothing under `deploy/mabna-sim/generated/` or `scratch/` (onsite checklist section 6) (review done in implement iteration 9; no secrets or float amounts found; the database name `TotalSystem` remains in spec 002 docs, `deploy/.env.example`, `deploy/mabna-sim/README.md` and spec 003 plan/quickstart, and the guard-policy names `scuUser`/`UserRights`/`msgInbox` remain in committed code and tests, an owner decision; the `SourceMap` file-read problem text now passes through `RedactionHelper.Scrub`; iteration 10: the five example-map column names that matched the real captured schema (`DocNo`, `DocDate`, `Amount`, `Qty`, `Opr`) were renamed to `SimDocNumber`, `SimDocDate`, `SimLineAmount`, `SimQuantity` and `SimOpCode` in the example map, the contract snippet and the sync tests, and no other example-map column appears in the capture; the owner accepted the `TotalSystem` and `scuUser`/`UserRights`/`msgInbox` names, which predate this branch on master, so the names review is complete)
- [X] T079 Hand off quickstart Flows 0–10 to the tester (simulator) and Flow 11 to the on-site visit; record on-site statuses and reconciliation pass/fail only in `specs/003-real-purchase-data/source-verification.md` — simulator part DONE (tester round 2: Flows 0-10 pass, evidence.md; Flow 11 on-site still PENDING)

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; blocks every story.
- **US1 (Phase 3)**: depends on Foundational (map statuses + `MetricAvailability`). Documentation and simulator tasks; the on-site visit itself can run in parallel with US2–US5.
- **US2 (Phase 4)**: depends on Foundational; uses the simulator purchasing data from US1 (T027/T028) for end-to-end flows, not for its unit tests.
- **US3 (Phase 5)**: depends on Foundational (T016/T017) and on `SyncCoordinator` (T037) for T045; T044 and T047 can start right after Foundational.
- **US4 (Phase 6)**: depends on US2 (synced copy, `KeysetPager`, `PurchaseRowMapper`, migration `0006` file from T039).
- **US5 (Phase 7)**: depends on US4 (`fn_purchase_totals`, discount logic) and Foundational status/shell.
- **US6 (Phase 8)**: depends on Foundational data-mode core (T006–T010); independent of US2–US5 otherwise. T073 and T047 both edit `deploy/docker-compose.yml`: do them sequentially.
- **Polish (Phase 9)**: after all stories.

### Story order

US1 → US2 → US3 → US4 → US5, with US6 able to start right after Foundational. Priority P1 stories (US1–US4) complete before P2 (US5, US6) when staffed by one worker.

### Within each story

Tests first (they must fail before implementation), then models/pure logic, then services, then endpoints, then UI. Shared files edited by several tasks (sequential only): `backend/Zharfa.Api/Program.cs` (T010, T041, T046, T055, T060), `db/migrations/0006_purchasing_kpis.sql` (T039 → T051 → T058), `frontend/src/App.tsx` (T023, T024, T043, T065, T071, T072), `deploy/docker-compose.yml` (T047, T073).

## Parallel Opportunities

- Setup: T002, T003, T004 together after T001.
- Foundational: T011–T015 together; T019, T020, T022, T024 together once their subjects exist.
- US1: T026 and T027 together after T025.
- US2: T029–T032 (tests) together; T033, T034, T035 together; T042 alongside backend work once T022 exists.
- US3: T044, T045 together.
- US4: T048–T050 together; T052 and T053 together.
- US5: T061–T064 and T066 together while T058–T060 proceed.
- US6: T067 and T071 together; can run alongside US2–US5.

### Parallel example: User Story 2

```text
Task: "Unit tests for the incremental planner in backend/Zharfa.Api.Tests/Sync/IncrementalPlannerTests.cs"
Task: "Unit tests for keyset paging in backend/Zharfa.Api.Tests/Sync/KeysetPagerTests.cs"
Task: "Unit tests for the row mapper in backend/Zharfa.Api.Tests/Sync/PurchaseRowMapperTests.cs"
Task: "Unit tests for scheduling and coordinator decisions in backend/Zharfa.Api.Tests/Sync/SyncSchedulingTests.cs"
then
Task: "Implement KeysetPager in backend/Zharfa.Api/Mabna/Sync/KeysetPager.cs"
Task: "Implement PurchaseRowMapper in backend/Zharfa.Api/Mabna/Sync/PurchaseRowMapper.cs"
Task: "Implement IncrementalPlanner in backend/Zharfa.Api/Mabna/Sync/IncrementalPlanner.cs"
```

### Parallel example: User Story 5

```text
Task: "JalaliMonthPicker in frontend/src/components/JalaliMonthPicker.tsx"
Task: "KpiCard in frontend/src/components/KpiCard.tsx"
Task: "TrendChart in frontend/src/components/TrendChart.tsx"
Task: "TopList in frontend/src/components/TopList.tsx"
```

## Implementation Strategy

### MVP first

1. Phases 1–2 (Setup, Foundational).
2. Phase 3 (US1): verification record and simulator purchasing data.
3. Phase 4 (US2): sync on the simulator. **Stop and validate** with Flows 2–6 and 9.
4. Phase 5 (US3): guard proof with purchasing tables (Flow 1).

The MVP is a safe, recorded sync of purchasing data (US1 + US2 + US3), which every later report depends on.

### Incremental delivery

5. Phase 6 (US4): metric definitions + reconciliation (Flow 7) — the sign-off gate.
6. Phase 7 (US5): Overview (Flow 8) — the visible phase goal.
7. Phase 8 (US6): data-mode separation (Flow 10); can be pulled earlier in parallel.
8. Phase 9: docs, review, hand-off. Phase 1 of Release 1 is done when all exit criteria in `zharfa-prd.phases.md#1` hold, including on-site Flow 11 with reconciliation pass.

## Notes

- [P] = different files, no dependency on an unfinished task.
- Commit after each task or logical group with Conventional Commits; never commit generated simulator output, `scratch/`, real names or real totals.
- A reconciliation mismatch blocks sign-off (FR-018); fix the map or the definition, resync, rerun Flow 7/11.

## Phase 10: Convergence

Appended by `/speckit.converge` (iteration 1) after the Setup and Foundational sections. Each task closes a gap where a done task (T001–T024) does not hold in the code.

- [X] T080 Read `Zharfa__DataMode` through one shared config-key helper that also accepts the `Zharfa:DataMode` form the .NET environment provider produces, in `backend/Zharfa.Api/Program.cs` (line 28), `backend/Zharfa.Api/Platform/DataModeOptions.cs` and the seed gate in `backend/Zharfa.Api/Data/MigrationRunner.cs` (line 117), and add a configuration-level case to `backend/Zharfa.Api.Tests/Platform/DataModeTests.cs` per FR-020 (contradicts)
- [X] T081 Read `Bootstrap__AdminUsername`, `Bootstrap__AdminPassword`, `Bootstrap__ManagerUsername` and `Bootstrap__ManagerPassword` through the same helper in `backend/Zharfa.Api/Platform/BootstrapAccounts.cs` (`Validate` and `EnsureAsync`), with a configuration-level case in `backend/Zharfa.Api.Tests/Platform/DataModeTests.cs` per contracts/cli-and-config.md §2 (contradicts)
- [X] T082 Make blank `Sync__PageSize`, `Sync__MaxRejectedRows` and `Sync__FaultInjection__FailAfterTables` fall back to their defaults in `backend/Zharfa.Api/Mabna/Sync/SyncOptions.cs` (today the integer binding throws at startup in `backend/Zharfa.Api/Program.cs` line 39), and keep `deploy/.env.example` consistent with that, per contracts/cli-and-config.md §2 (contradicts)
- [X] T083 Gate every figure in `backend/Zharfa.Api/Purchasing/MetricAvailability.cs` on the cancelled rule, and on `docDiscount` when the discount basis is `lineAndDocument`; add both rows to the metric-availability table in `specs/003-real-purchase-data/data-model.md` and cases to `backend/Zharfa.Api.Tests/Purchasing/MetricAvailabilityTests.cs` per FR-001 and FR-027 (partial)
- [X] T084 Reject invoice and return entities that share an object and have identical filter lists as ambiguous line linkage in `backend/Zharfa.Api/Mabna/Sync/SourceMapValidator.cs` (the rule 6 check at lines 167–177 only covers the no-filter case), with a case in `backend/Zharfa.Api.Tests/Sync/SourceMapValidationTests.cs` per contracts/source-map.md rule 6 (partial)
- [X] T085 Validate `change.column` as an identifier and require `grossAmount.amountSign` to be one of `asIs | negate | abs` (a blank value is accepted today) in `backend/Zharfa.Api/Mabna/Sync/SourceMapValidator.cs`, with cases in `backend/Zharfa.Api.Tests/Sync/SourceMapValidationTests.cs` per contracts/source-map.md rules 2 and 5 (partial)
- [X] T086 Keep the last-sync indicator in the `frontend/src/App.tsx` header when `/api/system/status` fails (today `LastSyncIndicator` is not rendered at all), showing neutral text from `frontend/src/components/LastSyncIndicator.tsx`, per FR-012 (partial)

## Phase 11: Convergence

Appended by `/speckit.converge` after implement iteration 2. Each task closes a gap where a done task (T001–T024, T080–T086) does not hold in the code or its documentation.

- [X] T087 Refuse startup when `Sync__PageSize` is below 1 or above the effective `MabnaSource__MaxRows`, naming both keys: add a pure check beside `SyncOptions.FromConfiguration` in `backend/Zharfa.Api/Mabna/Sync/SyncOptions.cs`, call it from `backend/Zharfa.Api/Program.cs` next to the time-zone check (effective ceiling from the `MabnaSource` section, `MabnaSourceOptions.EffectiveMaxRows`), and add cases to `backend/Zharfa.Api.Tests/Sync/SyncOptionsTests.cs`. T003 names the bound, but today `MabnaQuery.Build` silently clamps a larger `Top`, so a keyset page above the ceiling stops after its first short page and truncates a full load per contracts/cli-and-config.md §2 (partial)
- [X] T088 Align the cancelled-rule row of the "Metric availability" table in `specs/003-real-purchase-data/data-model.md` (§4, now "All figures") with the per-figure gating in `backend/Zharfa.Api/Purchasing/MetricAvailability.cs`: the invoice rule gates Gross, the return rule gates Returns, and Net, Top lists, Trend, Comparison, VAT and active vendors need both rules. Rename `Unusable_cancelled_status_on_return_blocks_every_figure_because_each_uses_returns` in `backend/Zharfa.Api.Tests/Purchasing/MetricAvailabilityTests.cs` to match what it asserts (Gross stays available) per FR-001 (contradicts)

## Phase 12: Convergence

Appended by `/speckit.converge` after implement iteration 3. Closes a gap where the committed verification record (T026) holds column-level rows only, while US1 AC1 and SC-001 require each source table to carry a recorded status.

- [X] T089 Add five table-level rows (numbers 35–39: vendor master table, product master table, purchase invoice header table, purchase return header table, purchase line table) to the logical-items table in `specs/003-real-purchase-data/source-verification.md`, each with the status its entity holds in the on-site source map (`candidate` off-site, `unknown` if not yet checked) and logical names only, per US1/AC1 and SC-001 (partial)

## Phase 13: Convergence

Appended by `/speckit.converge` after the owner decision on finding C1 (US2/AC5 against the 60-day header window): option (a). Purchase header tables (invoice, return) are re-read in full on every run with a key diff, which retires the 60-day window. Rowversion and modifiedAt header tables also detect deletions on incremental runs (key diff). Incremental sync then equals a full load, and source deletions and cancellations older than 60 days reach the synced copy on the next sync. This supersedes the window semantics of T029, T035 and T036; T094 and T095 replace their window assertions. `spec.md` is unchanged, because US2/AC5 and the sync Assumption already state this intent.

- [X] T090 Remove the idWatermark re-read window from `backend/Zharfa.Api/Mabna/Sync/IncrementalPlanner.cs`: delete `IdWindowStart`, `DeleteScope.KeysFrom`, `TablePlan.DeleteFromKey` and the window parameters of `PlanTable`; an idWatermark document table (invoice, return) is a full read with `DeleteScope.AllUnseen` on every run, and masters keep their current plan, per US2/AC5 and the sync Assumption "Source deletions ... are reflected on the next sync" (partial)
- [X] T091 Make rowversion and modifiedAt header tables detect deletions on incremental runs in `backend/Zharfa.Api/Mabna/Sync/IncrementalPlanner.cs`: the plan keeps the marker-filtered read for changed rows and adds a key-only read of all source keys (explicit key column, no change predicate, same keyset paging) that stages every key, so unseen local keys are deleted with `DeleteScope.AllUnseen`; a table with no stored watermark stays a full read, per US2/AC5 and the sync Assumption (partial)
- [X] T092 Apply the T090–T091 plans in `backend/Zharfa.Api/Mabna/Sync/SyncRunner.cs`: in `SyncDocumentsAsync` remove the `QueryLocalMinKeyAsync` call, its `LocalMinKeySql` constant and the `IdWindowStart` use; for rowversion and modifiedAt header tables run the key-only pass before `DeleteUnseenDocumentsAsync`; keep the rejection limit, the upserts that write only real changes (`IS DISTINCT FROM`), and the watermark save inside the run transaction, per FR-010 and US2/AC5 (partial)
- [X] T093 Retire `lookbackDays` in `backend/Zharfa.Api/Mabna/Sync/SourceMap.cs` (property and parse at lines 72 and 470–474), `backend/Zharfa.Api/Mabna/Sync/PurchaseMapping.cs` (line 187) and `ChangeMarker.LookbackDays` in `backend/Zharfa.Api/Mabna/Sync/IncrementalPlanner.cs`: the loader still accepts the key and ignores it, so a map that still carries it loads, per the window removal in T090 (partial)
- [X] T094 Replace the window assertions of T029 in `backend/Zharfa.Api.Tests/Sync/IncrementalPlannerTests.cs`: remove `Idwatermark_window_reads_keys_from_the_window_start_and_diffs_only_inside_it`, `Window_start_is_the_smaller_of_the_local_lookback_minimum_and_the_next_key`, `Key_diff_deletes_unseen_keys_inside_the_window_and_keeps_keys_outside_it` and `New_changed_and_deleted_keys_inside_the_window_are_handled_as_a_set`; add cases asserting a full read with `AllUnseen` deletes for idWatermark document tables on every run (a key unseen in the read is deleted even when its document is older than 60 days), keeping `Master_idwatermark_reads_new_keys_only_and_never_deletes` and the first-run promotion case (partial)
- [X] T095 Update the rowversion and modifiedAt planner tests in `backend/Zharfa.Api.Tests/Sync/IncrementalPlannerTests.cs` (`Rowversion_read_uses_the_stored_watermark_and_never_lowers_it`, `Modified_at_read_starts_ten_minutes_before_the_watermark`, `Rowversion_read_without_a_stored_watermark_is_a_full_read`): assert the changed-row filter and the key-only pass that stages all keys, so a deletion is detected on an incremental run; add a case where no watermark yields a full read (partial)
- [X] T096 Update `backend/Zharfa.Api.Tests/Sync/SourceMapValidationTests.cs` (and `backend/Zharfa.Api.Tests/Sync/PurchaseRowMapperTests.cs` only if it builds a `ChangeMarker` with a lookback): a map that still contains `lookbackDays` loads and validates unchanged, and no test refers to a lookback window (partial)
- [X] T097 Update `specs/003-real-purchase-data/research.md` R5 (lines 37–47): idWatermark document tables are a full read with key diff on every run; rowversion and modifiedAt document tables detect deletions with a key-only pass; the lookback, the re-read window and the weekly full-load bound are removed; the equivalence proof stays; the rejected alternative "full reload every night" is no longer rejected for headers (partial)
- [X] T098 Update `specs/003-real-purchase-data/plan.md` (Summary sync bullet, which names the re-read window, the lookback and the weekly bound) and any other plan sentence naming the window, lookback or weekly bound, to the T097 behaviour; the Constitution Check rows change only where they name the window (partial)
- [X] T099 Update `specs/003-real-purchase-data/contracts/source-map.md`: remove `lookbackDays` from the shape example (line 33) and state in the rules that idWatermark header tables are read in full on every run, that rowversion and modifiedAt header tables detect deletions with a key-only pass, and that `lookbackDays` is ignored; update `specs/003-real-purchase-data/data-model.md` §1 and §2 (`source_record.strategy`) wherever they describe the window (partial)
- [X] T100 Remove `"lookbackDays": 60` from the two `change` objects in `deploy/mabna-sim/example/purchase-source-map.example.json` (lines 42 and 58), keeping `strategy` and `column` (partial)
- [X] T101 Update `specs/003-real-purchase-data/source-verification.md` rows 15 and 23 ("Increasing key with a 60-day lookback") and the change-marker line of `specs/002-mabna-accounting-connector/onsite-checklist.md` (line 98) to the T097 behaviour (full re-read on every run, key-diff deletes), logical names only (partial)
- [X] T102 Add an old-document check to quickstart Flow 6 in `specs/003-real-purchase-data/quickstart.md`: in the simulator, cancel (`IsCanceled=1`) one invoice dated more than 60 days before the run and delete one return dated more than 60 days before the run, in addition to the recent edits; the incremental job shows `purchase_invoice` updated > 0 and `purchase_return` deleted > 0 for those rows, and `mabna.fn_sync_fingerprint()` equals the full-load fingerprint, per US2/AC5 (partial)
- [X] T103 Extend the Flow 6 change script in `deploy/mabna-sim/README.md` (section "Purchasing scenario", step 2, `generated/src/purchasing-changes.sql`) so the simulator changes include an invoice dated more than 60 days back to cancel and a return dated more than 60 days back to delete, matching T102 (partial)
- [X] T104 Hand the revised Flow 6 (T102) and the unit suite (`dotnet test backend/Zharfa.Api.Tests`, covering T094–T096) to the tester per T079; workers do not run validation (partial) — DONE: unit suite round 1 (404 passed), Flow 6 old-document check round 2 (purchase_invoice updated=1, purchase_return deleted=2, fingerprint incremental==full)

## Phase 14: Convergence

Appended by `/speckit.converge` after implement iteration 5 (T090–T103 nightly full header re-read; US3 T044–T047 re-checked). Each task closes a gap where a done task (T001–T047, T080–T103) does not hold in the code: vendor and product masters do not converge to a full load under a non-`full` change marker, and lines removed by a header delete are not counted in the `purchase_line` deleted count.

- [X] T105 Make vendor and product masters converge to a full load on every run, whatever change marker the map gives them: in `backend/Zharfa.Api/Mabna/Sync/IncrementalPlanner.cs` `PlanTable` returns a full read with `DeleteScope.AllUnseen` for `isDocument: false` (research R5 `full` rule; masters are small), so renamed masters and deleted masters reach the copy on the nightly run, and the master branch of `SyncRunner.SyncMastersAsync` needs no key pass; replace `Master_idwatermark_reads_new_keys_only_and_never_deletes` and `Rowversion_master_read_never_deletes_and_needs_no_key_pass` in `backend/Zharfa.Api.Tests/Sync/IncrementalPlannerTests.cs` with full-read and key-diff cases; state in `specs/003-real-purchase-data/research.md` R5 and `specs/003-real-purchase-data/contracts/source-map.md` that a master marker is ignored, per US2/AC5 and the sync Assumption "Source deletions ... are reflected on the next sync" (partial)
- [X] T106 Count the purchase lines removed by `ON DELETE CASCADE` when their header is deleted, on incremental and full runs, and report them as the `purchase_line` deleted count in the sync log and on the Data sync page: in `backend/Zharfa.Api/Mabna/Sync/SyncRunner.cs` return the line count of each document that `DeleteUnseenDocumentsSql` or `DeleteRejectedDocumentsSql` removes (a CTE over the deleted documents' lines), carry it into the `purchase_line` `SyncTableCounts` so `rows_deleted` is not understated, and leave the header deleted count unchanged, per FR-009 (row counts per table) and the sync log entry (partial)

## Phase 15: Convergence

Appended by `/speckit.converge` after implement iteration 7 (US5 T057–T066 re-checked against spec US5, FR-024..FR-029, contracts/api.md §3 and SC-005). Each task closes a gap where a done task (T001–T066, T080–T106) does not hold in the code or its documentation.

- [X] T107 Correct the "Active vendors" row of `data-model.md` §4 to count distinct non-NULL `vendor_source_key` with at least one non-cancelled document in the month, as `mabna.fn_active_vendors` in `db/migrations/0006_purchasing_kpis.sql` and `docs/metrics/purchasing.md` already do (a document whose vendor key is missing from the master still counts; a document with no vendor key does not); T058's earlier `vendor_id` wording stays as history, per Key Entities: Vendor (contradicts)
- [X] T108 Make `JalaliMonth.TryParse` in `backend/Zharfa.Api/Purchasing/JalaliMonth.cs` accept only the `YYYY-MM` shape of contracts/api.md §3 (four-digit year, two-digit month, `-` separator, Latin or Persian digits), so `GET /api/overview/purchasing` answers 400 for `1405-1` and `1405/08`; update the `Month_parses_latin_and_persian_digits` cases in `backend/Zharfa.Api.Tests/Purchasing/ComparisonMathTests.cs` to match, per contracts/api.md §3 (partial)

## Phase 16: Convergence

Appended by `/speckit.converge` after implement iteration 8 (US6 T067–T074 and T107–T108 re-checked against FR-019..FR-023, contracts/api.md §3–§5 and the compose environment pass-through of contracts/cli-and-config.md §2). Each task closes a gap where a done task (T001–T074, T080–T108) does not hold in the code or its documentation.

- [X] T109 Make `JalaliMonth.TryParse` in `backend/Zharfa.Api/Purchasing/JalaliMonth.cs` accept only the exact `YYYY-MM` shape: drop the surrounding-whitespace trim so `month= 1404-12` answers 400, and change the `" 1404-12 "` case of `Month_parses_latin_and_persian_digits` in `backend/Zharfa.Api.Tests/Purchasing/ComparisonMathTests.cs` to a rejected input, per contracts/api.md §3 (partial)
- [X] T110 Record the production refusal of `GET /api/admin/provider` (AI provider status, `[DemoOnly]` on `AdminController.GetProviderStatus` in `backend/Zharfa.Api/Controllers/AdminController.cs`) in `specs/003-real-purchase-data/contracts/api.md` §5 as part of the AI surface refused until Phase 4 (FR-023), and in the AdminController entry of T069; if the owner instead wants provider status available in production, remove that attribute and the `GetProviderStatus` assertion in `backend/Zharfa.Api.Tests/Platform/DemoOnlyFilterTests.cs` (`Admin_demo_routes_are_marked_but_not_the_sync_routes`) (unrequested)
