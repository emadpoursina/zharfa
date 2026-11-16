# Feature Specification: Mabna Accounting Read-Only Connector

**Feature Branch**: `002-mabna-accounting-connector` (not yet created)

**Created**: 2026-11-11

**Status**: Superseded 2026-11-16 by `zharfa-prd.md` v3.0 — its connector, guards and simulator are the Release 1 foundation; its receivables/cash stories (US3, open questions 4–5) move to Release 2 (F21). Not a scope source.

**Input**: Read-only analytics over Mabna (`TotalSystem`, SQL Server 2019) for the accounting sections of the ERP, read live with a read-only login.

**Scope authority**: `docs/prd-archive/zharfa-demo1-prd.md` v2.0 governs Demo 1 and excludes Mabna and accounting. This feature is a post-Demo-1 phase (PRD §10 item 10, §11 phases 6–8). `docs/prd-archive/PRD-v1.1.md` v1.1 §551–582 and NFR-07 (read-only legacy integration) bind this feature. `AGENTS.md` rules bind every decision.

## Clarifications

### Session 2026-11-14 (offline development)

Development decisions. These supersede the 2026-11-14 entry below for development work:
- Development runs offline against the local SQL Server simulator (`deploy/mabna-sim/`, synthetic data). The schema is captured on-site as catalog metadata only; no business rows are read or stored.
- The live server is read only on-site, by the developer, following `onsite-checklist.md`. Nothing from it is committed.
- Value probes that read real values: approved by the owner on 2026-11-14. P1–P10 ran on-site and returned aggregates only; the company's 2026-11-15 off-site approval covers their results, which stay gitignored and are never committed.
- Taking schema metadata off-site: approved by the company on 2026-11-15; scope covers schema captures, view/procedure definitions, value-probe aggregates and code lists; these stay gitignored (`scratch/` and `deploy/mabna-sim/generated/`) and are never committed.
- Mabna's per-user report views (filled by its report procedures) filter on `HOST_ID()`, the client's process id, so a read-only client sees no rows unless that same process ran the writing procedure. Zharfa does not read them on live Mabna (checked in the simulator, 2026-11-16). See FR-09.
- App-side guards (table allowlist with hard exclusions, SELECT-only query builder, write-permission check, rolled-back transactions) are secondary. The primary control is a server-side read-only login.

### Session 2026-11-14

Owner decisions (these supersede the 2026-11-11 notes where they conflict):
- Development reads the **live** Mabna server, read-only. The accounting tables are in the `TotalSystem` database, not `MabnaTotal` (which holds only administrative tables). No restored copy is used.
- The login in use (`userds`) has `SELECT` on `TotalSystem` and no write or DDL permission (checked with `fn_my_permissions`; no write was attempted).
- The owner accepted that this login can read `dbo.scuUser`. Zharfa does not rely on database permissions for this: the connector's table allowlist excludes `scuUser`, `UserRights`, `users`, and every `scu*` and `msg*` table.
- A dedicated read-only login is still preferred by PRD NFR-07. Not requested yet.
- Schema verification (User Story 1) is done against live catalog metadata and partition row counts only. No business rows were read. Results are in `scratch/mabna-schema-verified.md` (gitignored).

### Session 2026-11-11 (partly superseded)

Owner decisions so far:
- Development source is a **restored copy** of `MabnaTotal` (a `COPY_ONLY` backup restored into SQL Server in Docker on the owner's Mac). The live server is not queried during development.
- The project is now being built for real use, not as a demo. Demo 1 stays in the repo as the synthetic baseline.
- Focus is the **accounting sections** of Mabna. Which reports come first is **not yet decided**.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Verify the accounting source before building on it (Priority: P1)

The owner restores the Mabna copy and confirms the schema notes in `scratch/mabna-schema-notes.md` against the real tables: keys, Jalali date types and formats, cancelled and returned invoices, and cheque state values. Nothing downstream is built until these are verified.

**Why this priority**: The schema notes are inferred and unverified. Every formula depends on them.

**Independent Test**: Run catalog queries against the restored copy and record the results (server version, table list, column types, row counts, sample rows) in a verification note. Each item in the schema notes is marked confirmed, corrected, or unknown.

**Acceptance Scenarios**:

1. **Given** the restored copy, **When** the catalog queries run, **Then** the server version and the accounting table columns are recorded, and the notes are updated.
2. **Given** a Jalali date column, **When** its type and format are checked, **Then** the conversion rule to Gregorian is documented before any date filter uses it.

### User Story 2 - Read-only accounting data for Zharfa (Priority: P2)

A dedicated read-only login (`zharfa_ro`) reads only the accounting tables and views the feature needs. The backend stores normalized copies in Zharfa's own PostgreSQL. No write to Mabna is possible through this login, and no operational table outside the allowlist is readable.

**Why this priority**: Access scope is the main safety control. A `db_datareader` role on the whole database would also expose user tables (`scuUser`, `UserRights`) and customer data.

**Independent Test**: Connect as `zharfa_ro` and confirm that writes fail, that allowlisted tables read, and that user and credential tables are denied.

**Acceptance Scenarios**:

1. **Given** the `zharfa_ro` login, **When** it attempts an INSERT, UPDATE, DELETE, or DDL statement, **Then** the server rejects it.
2. **Given** the connector, **When** a query names `scuUser`, `UserRights`, `users`, or any `scu*`/`msg*` table, **Then** the connector refuses it before it reaches the server. (Database-level denial is not in place for `scuUser`; see Clarifications 2026-11-14.)

### User Story 3 - Accounting figures that match Mabna (Priority: P3)

For the chosen first report, the Zharfa API returns totals that match the same query run directly against the Mabna copy. The three Demo 1 formulas stay separate: sales revenue, cash received, and receivables balance. Sales revenue is never presented as cash received.

**Why this priority**: AGENTS.md makes the API-to-database reconciliation a sign-off gate.

**Independent Test**: Run the reconciliation query on the restored copy and compare it with the API output for the same period and filter. Compare also with the matching Mabna report where one exists (for example `darusaleprj.txt` for receivables).

**Acceptance Scenarios**:

1. **Given** a period and filter, **When** the API and the direct query run, **Then** totals are equal, or the mismatch blocks sign-off and is logged.
2. **Given** a cheque that has not cleared, **When** cash received is computed, **Then** it is excluded and shown separately.

## Requirements *(mandatory)*

- **FR-01**: Development reads go to the local simulator only (`deploy/mabna-sim/`, synthetic data). Live `TotalSystem` reads happen only on-site, by the developer, over a read-only login, following `onsite-checklist.md`. Every read is limited to the allowlist below; no table outside it is queried.
- **FR-02**: Nothing from Mabna is committed. Schema notes and verification output stay in `scratch/` (gitignored). No row-level extract is stored in the repository.
- **FR-03**: All Mabna access uses the read-only login `zharfa_ro`, limited to an allowlist of accounting tables and views. Credentials come from environment variables or `deploy/.env` (gitignored), never from the repo or the frontend.
- **FR-04**: Mabna data is copied into Zharfa's PostgreSQL through the reserved `SourceRecord`, `SyncJob`, and `SyncLog` tables. Normalize Arabic ي/ك to ی/ک and convert Jalali dates at ingestion.
- **FR-05**: Every accounting figure shows its period, filters, and source view. Missing or incomplete data is stated as "data unavailable", not estimated.
- **FR-06**: All accounting figures are labeled as **real company data** and visible only to roles defined in the open questions. Demo 1 synthetic labeling is not reused for real data.
- **FR-07**: No accounting data is sent to a cloud AI provider unless the owner explicitly approves it (see open questions).
- **FR-08**: Sales revenue, cash received, and receivables balance remain three separate, documented formulas.
- **FR-09**: Accounting figures are computed from base tables, or from views that do not filter on `HOST_ID()`. Mabna's per-user report views and the procedures that fill them are never read or run on live Mabna (the procedures write; the connector's SELECT-only guard blocks them). In the simulator those views are used only to compare Zharfa's results with Mabna's report logic.

## Success Criteria *(mandatory)*

- **SC-01**: Schema notes are fully verified (each item confirmed, corrected, or unknown) before any formula is implemented.
- **SC-02**: Write denial is confirmed for the login by permission metadata (no write attempted against Mabna). The connector's allowlist test refuses `scuUser` and `UserRights`.
- **SC-03**: The first chosen accounting figure reconciles between the API and the direct query for at least one month and one filter.

## Key Entities (accounting slice, from the ranked table list; to be verified)

- **Vouchers:** `AccDoc`, `AccDocItem`, `AccSnd`, `AccSndItm` (Debit/Credit, `MoinCode`)
- **Payment allocation:** `AccAlloc` (links payments to invoices)
- **Cheques:** `ChqCheque`, `ChqChequeProcess`, `ChqStateValue`
- **Sales and receivables:** `SlsDoc`, `SlsItem`, `SlsFactor`, `vAllSls`
- **Chart of accounts:** `AccAccountGrpDtl1/2/3`, `AccAccountPreHeader`, `AccTotAccNo`, `AccLevelInf`
- **Existing report views (cross-check only):** `VW_Rep_Acc002`, `TblRpt_Acc002`, `vw_Rep_Acc003_Center`, `vw_Rep_Acc006`

## Open Questions (owner decision required)

Questions 2 and 3 are superseded by the 2026-11-14 decision (live read-only access, no restored copy). Question 1 is still open and blocks the first plan.

1. **First report.** Which accounting report comes first? Candidates: receivables aging, cash received by period, sales revenue by period, voucher balances by account.
2. **Restore source and version.** Who provides the `COPY_ONLY` backup, and what is the SQL Server version? A SQL 2005 or 2008 backup may not restore into a Docker image. The schema notes give a 2017-or-older fallback, but this needs checking against the actual backup. If it fails, the agent route (a Windows-side extract) is the fallback. **Superseded** (2026-11-14: live read-only access, no restore).
3. **Permission to hold the copy.** Is the owner allowed to keep a copy of company data on the development Mac? **Superseded** (2026-11-14: no copy is held; off-site schema metadata and aggregates approved 2026-11-15).
4. **Cheque states.** Which `ChqStateValue` values count as cash received? **Partly answered** (2026-11-15 capture): Mabna's own rule counts the paid states as cash, with a stricter set for direct-collection instruments. Owner decides whether the endorsed-out state and credit-note receipts count as cash, and the date basis.
5. **Cancelled and returned invoices.** How are they treated in revenue and receivables? **Partly answered** (2026-11-15 capture): cancelled invoices have no status code; their items are deleted, so `vAllSls` drops them. Returns are negative `vAllSls` lines and are not counted twice. Owner decides gross or net, the return month, purchase returns, and tax.
6. **Roles.** Which Zharfa roles may see accounting figures? Demo 1 has only Manager and Admin.
7. **Cloud AI.** May real accounting data go to the cloud AI provider, or is the AI restricted to aggregated figures? Default in this draft: no raw rows and no real data to a cloud provider.
8. **Payables and purchases.** Are they in scope now? Demo 1 and the PRD defer them.
9. **Sync cadence.** Is a live read-only sync needed later, and how often?

## Assumptions

- The owner runs the Docker SQL Server on Apple silicon using an `linux/amd64` image under emulation. This is slower but works.
- `docs/prd-archive/zharfa-demo1-prd.md` is not edited by this feature. A separate PRD addendum may be needed once the first report is chosen.
- Work is submitted as a `feature` on the Hermes `zharfa` Kanban board. This spec is the draft for that card and has not been reviewed by the board yet.
