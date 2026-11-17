# Research: Real Purchase Data (Release 1, Phase 1)

**Feature**: `specs/003-real-purchase-data` | **Date**: 2026-11-16 | **Plan**: [plan.md](plan.md)

Each item below resolves an unknown from the plan's Technical Context. The clarify step ran in skip mode, so the remaining decisions are documented assumptions (marked **Assumption**) that the owner can overturn without reworking the design.

---

## R1. Where real purchasing table names live (FR-02 of spec 002 vs. a committed mapping)

- **Decision**: The sync engine is written against a **logical purchasing model** (vendor, product, purchase document, purchase line). The mapping from logical fields to real Mabna tables, columns and operation codes lives in a **gitignored source-map JSON file** (`MabnaSync__SourceMapFile`), in the same way the spec 002 allowlist lives in a gitignored file. The repository holds only the schema of that file ([contracts/source-map.md](contracts/source-map.md)), an example with made-up names (`deploy/mabna-sim/example/purchase-source-map.example.json`), and the logical verification status list (`source-verification.md`, no real names).
- **Rationale**: Spec 002 FR-02 says nothing from Mabna is committed and real table names stay in configuration. The purchasing tables are not verified yet (PRD risk R1), so a config-driven map also lets on-site corrections land without a code change.
- **Alternatives considered**: (a) Hard-code real names in C#: violates FR-02 and needs a release for every on-site correction. (b) Map Mabna through SQL views created on Mabna: requires writes to Mabna; forbidden.

## R2. Enforcing "verify before any formula uses it" (FR-001, SC-001)

- **Decision**: Every mapped item in the source map carries a `status` of `confirmed`, `corrected`, `candidate` or `unknown`, plus `verifiedOn`. At startup the API validates the map:
  - In **production** mode only `confirmed`/`corrected` items are usable.
  - In **demo** mode (simulator) `candidate` items are also usable, so off-site development can proceed against the simulator before the on-site visit.
  - An unusable or unmapped item makes every metric that depends on it **unavailable** ("data unavailable", FR-027); the dependency table is in [data-model.md](data-model.md#metric-availability).
  - A mapped table that is not on the connector allowlist, or that matches a hard exclusion or a `HOST_ID()`-filtered report view flag, fails validation and disables the sync (startup warning, Data sync page shows the reason).
- **Rationale**: Turns SC-001 into a machine check instead of a convention, and keeps production from running on unverified structures.
- **Alternatives considered**: A doc-only checklist (cannot be enforced); refusing to build anything before the on-site visit (blocks all off-site work for no safety gain, since the simulator is synthetic).

## R3. Identifying candidate purchasing tables before the visit

- **Decision**: Off-site, candidates are picked from the already-approved full catalog capture (metadata only, approved 2026-11-15) and the gitignored schema notes in `scratch/`; they are marked `candidate`. The simulator is regenerated with these tables added as `gen_schema.py --roots` and a purchasing section in the gitignored `generated/scenario.py`. On-site, the extended checklist (new section in `specs/002-mabna-accounting-connector/onsite-checklist.md`: "7. Purchasing slice") confirms or corrects each item using catalog metadata and owner-approved aggregate probes only (FR-002), and records the discount basis of Mabna's own purchase report total.
- **Rationale**: Uses only approved off-site material; keeps the on-site visit short.
- **Alternatives considered**: Asking the Mabna vendor for documentation (open-ended, no schedule); probing business rows (not approved).

## R4. Reading at scale through the existing connector

- **Decision**: No change to the connector's safety surface. The sync reads with **keyset paging** using what `MabnaQuery` already supports: explicit columns, `Gt` filter on the key, `ORDER BY key`, `Top = page size` (default 5,000, below `MaxRows`). Each page is one guarded SELECT inside the always-rolled-back transaction. The per-command timeout is set to **60 s** for sync (`MabnaSource__CommandTimeoutSeconds=60`, FR-011; current code default is 30 s, the ceiling is 300 s).
- **Rationale**: The largest Mabna table is about 416k rows (PRD §9); about 85 pages at 5,000 rows. No aggregates, joins or raw SQL are needed for the sync, so the SELECT-only guard and allowlist stay exactly as tested.
- **Alternatives considered**: Raising `MaxRows` to the 100k ceiling and reading whole tables (larger memory spikes, longer single commands at risk of the timeout); adding JOIN support to the builder (widens the guard surface; not needed).

## R5. Incremental sync and change detection (FR-006, US2 AC5)

- **Decision**: Per-table change strategy chosen in the source map from what on-site verification finds:
  - `rowversion` — SQL Server `rowversion`/`timestamp` column; read `> last watermark`.
  - `modifiedAt` — a reliable modification time column; read `>= watermark − overlap` (overlap default 10 min).
  - `idWatermark` — increasing document id. Header tables (purchase invoice, purchase return) are read **in full on every run**, with a key diff: every stored header key missing from the read is deleted with its lines. No lookback and no re-read window (Phase 13 retired both).
  - `rowversion` and `modifiedAt` — header tables read the changed rows (`> watermark`, or `>= watermark − 10 min`), plus a **key-only pass** that reads every source key (same filters, no change predicate). Stored keys missing from the key set are deleted, so a deletion or cancellation reaches the copy on the next run.
  - `full` — no reliable marker: reload the whole table each run with a key diff.
  - **Masters (vendor, product) — Phase 14, T105:** whatever marker the map gives (`full`, `idWatermark`, `rowversion`, `modifiedAt`), a master is read in full with a key diff on every run. The marker is ignored: no watermark is kept for masters, there is no changed-row read and no key-only pass, and the source-record strategy is recorded as `full`. Vendors and products are small, and a renamed master (same key, new name) or a removed one reaches the copy on the nightly run, as in a full load.
  Lines are always re-read in full and replaced as a set (a stored line is kept only when the full read still returns it; a line of a deleted header goes with it by `ON DELETE CASCADE`). The lines removed that way are counted in the `purchase_line` deleted count of the run (Phase 14, T106; FR-009), so the sync log does not understate them. A header row that is read but cannot be stored (for example an unparseable date) removes its older copy, as a full load would. A weekly full run (Friday 02:00 by default, `Sync__WeeklyFullLoadDay`) is still scheduled; it is a full load with the same result as an incremental run, not a bound on drift.
- **Equivalence proof (US2 AC5)**: `mabna.fn_sync_fingerprint()` returns per-table row count and an order-independent hash of keys and amounts. The quickstart compares an incremental-synced database with a freshly full-loaded one; fingerprints must be equal. Because every header run sees the complete key set, an incremental run and a full load produce the same rows, including old documents cancelled or deleted at the source (quickstart Flow 6).
- **Rationale**: Mabna's change markers are unknown (spec Assumptions). Reading the header keys in full is cheap (one key per document, paged) and removes the only path where an old document could stay stale; the marker still limits the amount of data read for rowversion and modifiedAt tables.
- **Alternatives considered**: SQL Server Change Tracking/CDC (needs `ALTER DATABASE` on Mabna: a write; forbidden). Full reload of every table each night, with the amounts too (simple, but heavier load on Mabna and longer runs; the key-only pass keeps only the keys heavy). The 60-day re-read window (rejected in Phase 13: it misses an old document cancelled or deleted at the source).

## R6. Sync atomicity, failure and restart (FR-010, edge cases)

- **Decision**: All data changes of one run are applied in **one PostgreSQL transaction** on a dedicated connection; it commits only after every table succeeds. Job/log rows are written on a **separate connection** so the failure is recorded even when the data transaction rolls back. Watermarks (`source_record`) are updated inside the data transaction, so a failed run never advances them. On API startup, any `sync_job` still `running` is marked `failed` with error "interrupted (service restart)".
- **Rationale**: Data volume (hundreds of thousands of rows) fits a single transaction comfortably in PostgreSQL. Readers keep seeing the last committed state (MVCC), which is exactly "previous data stays in place".
- **Alternatives considered**: Staging schema + rename swap (more moving parts; only needed at much larger volumes); per-table commits (leaves a half-updated dataset, violates the Assumption "Sync atomicity").

## R7. Single-run guard and scheduler (FR-007, FR-008)

- **Decision**: A .NET `BackgroundService` (`SyncScheduler`) wakes at the configured local time (`Sync__NightlyTime`, default `02:00`, time zone `Sync__TimeZone`, default `Asia/Tehran`). Manual and scheduled runs go through one `SyncCoordinator` that takes a PostgreSQL **session advisory lock** and relies on a **partial unique index** (`sync_job` where `status = 'running'`) as the durable guard. If a run is already active: manual request returns the running job (HTTP 409 with the job body); a scheduled trigger records a `skipped` job with reason (edge case). Runs execute in the background; `POST` returns 202 immediately.
- **Rationale**: No new dependency (PRD §9: hosted background service). The unique index survives multiple API replicas; the advisory lock avoids races between the check and the insert.
- **Alternatives considered**: Hangfire/Quartz (new dependency, not justified for one daily job); an in-memory flag (wrong with more than one process).

## R8. Where the synced copy lives in PostgreSQL

- **Decision**: A dedicated PostgreSQL schema **`mabna`** holds the synced copy: `mabna.vendor`, `mabna.product`, `mabna.purchase_document`, `mabna.purchase_line`. The reserved public tables are extended, not replaced: `sync_job` (run header), `sync_log` (per-table detail and errors), `source_record` (per-source-table sync state and watermark). New: `install_setting`, `reconciliation_run`. See [data-model.md](data-model.md).
- **Rationale**: Demo 1 already has a synthetic `product` table in `public`; a separate schema keeps real and synthetic data physically apart and makes "production never loads the synthetic dataset" easy to check. The reserved tables are reused as AGENTS.md requires.
- **Alternatives considered**: Storing raw rows as JSON in `source_record.payload` (the name suggests it, but it makes every KPI a JSON query and kills the 2 s target); prefixing tables in `public` (mixes with Demo 1 objects).

## R9. Amounts, discount basis and sign conventions (FR-014)

- **Decision**: Amounts are stored as `numeric(24,4)` exactly as read (Rial, verified on-site), always **positive**; direction comes from `doc_type` (`invoice` or `return`). Lines store `gross_amount` (before discounts), `line_discount`, `vat_amount`; headers store `doc_discount` (document-level discount, if any) and `cancelled`. The discount basis is a server setting `Purchasing__DiscountBasis`:
  - `line` (default until verified): line amount = `gross_amount − line_discount`.
  - `lineAndDocument`: additionally subtracts header `doc_discount` from company totals; for top-5 products it is allocated to lines pro rata by line amount (rounded at 4 decimals; the remainder goes to the largest line so totals stay exact).
  The active basis is returned by the API and stated in `docs/metrics/purchasing.md`.
- **Rationale**: Clarification 2026-11-16 (follow Mabna's report basis once verified; line-level until then). Keeping raw components means the basis can change without resync or data loss.
- **Alternatives considered**: Storing only a computed net line amount (a basis change would need a full resync and loses traceability).

## R10. Jalali dates and text normalization at ingestion (FR-019)

- **Decision**: The map declares each date column's format: `jalaliText` (for example `1403/12/30`, separators `/` or `-`, Persian or Latin digits), `jalaliInt` (`14031230`) or `gregorian` (`date`/`datetime`). Ingestion converts with the existing `JalaliDate` helper (`backend/Zharfa.Api/Analytics/JalaliDate.cs`) and stores **both** `doc_date` (Gregorian `date`) and `jalali_year`, `jalali_month`, `jalali_day`. An unparseable date fails the row: the line is counted in `sync_log.rows_rejected` with the key, and the run fails if rejections exceed `Sync__MaxRejectedRows` (default 0). Text normalization: `ي→ی`, `ك→ک`, Arabic-Indic and Persian digits kept as-is in names, trimmed, internal whitespace collapsed; ZWNJ preserved. One shared `PersianText.Normalize` function is used by sync and by later search.
- **Rationale**: Grouping by the stored Jalali month avoids conversion at query time and makes month edges (including Esfand 30 in leap years) exact.
- **Alternatives considered**: Converting at query time (slow and duplicates logic in SQL).

## R11. Reconciliation: what "the same query against the source" means (FR-017)

- **Decision**: A developer-run **CLI mode of the API** (`dotnet run --project backend/Zharfa.Api -- reconcile purchasing --month 1405-08 [--vendor <sourceId>] [--product <sourceId>]`) computes two independent totals for net, gross, returns and VAT:
  1. **System side**: the same PostgreSQL function the Overview uses (`mabna.fn_purchase_totals`) over the synced copy.
  2. **Source side**: a fresh read of the source rows for that period and filter through the guarded connector (keyset paging, same map), summed in C# by a separate `SourceTotalsCalculator` that applies the same documented definition but shares no SQL with side 1.
  Each metric is compared separately for exact equality. The result is written to `reconciliation_run` (PostgreSQL), printed as one JSON line, and the process exits 0 (all pass), 1 (any mismatch) or 2 (error). A mismatch is also written to `audit_log` (`reconciliation.mismatch`). On-site, the developer additionally runs the existing gitignored control-total script (`scratch/mabna-control-totals.sql`, extended for purchases; owner-approved aggregates) as a third cross-check of the source side; only pass/fail is recorded in the repository (`source-verification.md`).
- **Rationale**: The connector cannot run aggregates or joins (by design, R4), and widening it for a sign-off tool would weaken the guard. Two independent code paths over the same definition catch both sync errors and formula drift. The control-total script covers the "direct SQL" reading of FR-017 on real data.
- **Alternatives considered**: Adding `SUM`/`GROUP BY`/`JOIN` to `MabnaSqlBuilder` (larger attack surface for a one-off check); an Admin UI (clarification: no UI in this phase); an xunit test against live services (the validation suite must run without DB or network).

## R12. Data mode (FR-020..FR-024)

- **Decision**: `Zharfa__DataMode` = `production` | `demo`, **required**: the API refuses to start if it is missing or invalid. On first start the mode is stored in `install_setting`; a later start with a different mode refuses to start (prevents flipping an existing database). Production mode refuses to start if any demo seed is recorded in `schema_migrations`. Behaviour by mode:
  - `demo`: applies `db/seed/*` as today, shows the demo banner on every page, labels Overview, Data sync, AI answers and every API payload (`dataLabel: "demo"`).
  - `production`: never applies `db/seed/*`; creates the initial Admin and Manager accounts from `Bootstrap__*` environment variables when `app_user` is empty (passwords hashed with the existing `PasswordHasher`, never logged); Demo 1 endpoints (`/api/sales/*`, `/api/inventory/*`, `/api/receivables/*`, `/api/meta/categories`, `/api/admin/reconciliation`) and the AI endpoints (`/api/ai/*`, until Phase 4) return **404** with the Persian envelope through a `[DemoOnly]` endpoint filter; the frontend hides those pages; figures carry `dataLabel: "real"` and the UI shows a "real company data" badge.
- **Rationale**: "Set by server configuration only" and "never mix" are both enforced server-side; a hidden page alone would not satisfy AGENTS.md. A required setting avoids an install silently defaulting to the wrong mode. 404 (not 403) is used because the module does not exist in that install.
- **Alternatives considered**: Default to demo (a misconfigured production install would load the synthetic dataset into the real database); default to production (breaks existing demo installs silently); a feature flag per page (more knobs, more ways to mix).

## R13. Overview API and computation (FR-016, FR-025..FR-029, SC-005)

- **Decision**: One endpoint `GET /api/overview/purchasing?month=YYYY-MM` (Jalali, default current Jalali month in `Asia/Tehran`), roles `manager,admin`. It calls PostgreSQL functions in `mabna` (migration `0006`): `fn_purchase_totals`, `fn_purchase_trend`, `fn_top_vendors`, `fn_top_products`, `fn_active_vendors`. Indexes on `(jalali_year, jalali_month)` and the vendor/product keys keep three years of data well under 2 s. Comparison: delta and percent vs the preceding Jalali month; percent is `null` when the base is 0; the whole comparison is `unavailable` when the preceding month is before the first month with synced data. Trend: last 12 Jalali months ending at the selected month; months before the first synced month are returned as `unavailable`, not zero.
- **Rationale**: Mirrors the Demo 1 pattern (KPI functions in migrations, thin C# service), keeps every calculation server-side.
- **Alternatives considered**: Computing in C# with EF LINQ (fine, but splits the metric definition across two languages; the reconciliation source side is deliberately the only C# implementation).

## R14. Frontend structure without new dependencies

- **Decision**: No router or chart library. `App.tsx` gets a small view switcher (Overview default; Data sync for admin; "Demo pages" holding the existing `Dashboard` only in demo mode). New components: `OverviewPurchasing`, `JalaliMonthPicker`, `KpiCard`, `TrendChart` (inline SVG, as `SalesCards` already does), `TopList`, `LastSyncIndicator`, `DataModeBadge` (replaces the always-on `DemoDataBanner` logic), `DataSyncPage` (polls every 3 s while a run is active). Mode and last-sync come from `GET /api/system/status`.
- **Rationale**: AGENTS.md "prefer existing abstractions; justify new dependencies". Three views do not need a router.
- **Alternatives considered**: `react-router` and a chart library (not justified for this scope; can be revisited in Phase 2 with the report pages).

## R15. Testing approach under the "no DB or network" validation rule

- **Decision**: `dotnet test backend/Zharfa.Api.Tests` stays pure (no DB, no network): guard and allowlist tests extended for the purchasing map (non-allowlisted mapped table, excluded names, `HOST_ID()` report views); source-map validation; Jalali parsing and month edges (Esfand 30 in 1403); ي/ك normalization; incremental planner (which keys to upsert/delete for a window); discount-basis and pro-rata allocation; `SourceTotalsCalculator`; reconciliation comparator; data-mode gate (endpoint filter decisions); comparison math with zero base. End-to-end checks (sync, failure, schedule, Overview, mode, reconciliation CLI) are scripted in [quickstart.md](quickstart.md) against Docker Compose + simulator, run by the tester and used in the PR walkthrough.
- **Rationale**: Keeps `.ainative/project.yaml` validation fast and hermetic, as the current suite is.
- **Alternatives considered**: Testcontainers integration tests in the default suite (needs Docker in the validation host; the tester runs them as quickstart flows instead).

## R16. Small open items (assumptions, owner can overturn)

- **Assumption**: Purchase returns are booked in their own document month (spec Assumptions).
- **Assumption**: VAT shown on the Overview is net VAT (invoice VAT − return VAT), matching how net purchases treats returns; gross VAT and return VAT are also returned by the API for the reconciliation log.
- **Assumption**: "Active vendors" excludes the "unknown vendor" bucket.
- **Assumption**: Nightly time zone `Asia/Tehran`; weekly full load Friday 02:00.
- **Assumption**: The Data sync page lists the last 50 runs; older runs remain in the table.
- **Assumption**: `Sync__FaultInjection__FailAfterTables` (forces a mid-run failure for the quickstart atomicity check) is honoured only when `ASPNETCORE_ENVIRONMENT=Development` **and** data mode is `demo`; otherwise ignored with a warning.
