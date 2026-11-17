# Feature Specification: Real Purchase Data (Release 1, Phase 1)

**Feature Branch**: `zharfa-prd-phase-1`

**Created**: 2026-11-16

**Status**: Draft

**Input**: User description: "phase: zharfa-prd.phases.md#1"

**Scope authority**: `zharfa-prd.md` v3.0, Phase 1 of `zharfa-prd.phases.md` ("Real purchase data"), covering F1 (Mabna purchase-data sync), F2 (purchase metric definitions and reconciliation), F3 (demo-data mode) and F4 (Overview dashboard, purchasing) in full. `AGENTS.md` rules bind every decision. Spec 002 (`specs/002-mabna-accounting-connector`) is superseded; its read-only connector, guards, simulator and on-site checklist are the existing foundation this feature builds on.

**Phase goal**: An Admin syncs real purchasing data from Mabna, and a Manager sees reconciled purchase totals on the Overview instead of demo numbers.

## Clarifications

### Session 2026-11-16

- Q: Should net purchases be counted after discounts, and if so which discounts? → A: Follow the discount basis of Mabna's own purchase report total, verified on-site; until verified, use amounts after line-level discounts as recorded on each invoice line, excluding VAT.
- Q: Which periods can the Manager pick on the Overview, and what is "previous period" compared against? → A: Jalali month only; compare with the immediately preceding Jalali month; 12-month trend.
- Q: What should the AI agent do in a production-mode install during Phase 1, before AI over real data arrives in Phase 4? → A: Hide and refuse the AI agent (UI and API) in production mode until Phase 4.
- Q: How is the reconciliation check run and where is its result seen? → A: A developer-run command or automated test that writes a reconciliation log (no UI); run on-site for the real month.
- Q: In which currency unit should the Overview show amounts? → A: Rial, labeled "ریال", matching Mabna's screens.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Verify the purchasing source before building on it (Priority: P1)

Before any purchase formula is written, the developer verifies on-site which Mabna tables and operation codes hold purchase invoices, purchase returns, the vendor (supplier) master and the product master. Each item is recorded as confirmed, corrected, or unknown, following the existing on-site checklist. The simulator is then aligned with the verified structure so development can continue off-site with synthetic data.

**Why this priority**: The purchasing tables are not yet verified (PRD risk R1). Every later story in this phase depends on knowing where purchases, returns, vendors and products live and how their dates and amounts are stored. Reports are never built on assumed Mabna structures.

**Independent Test**: Run the on-site verification for the purchasing slice using catalog metadata and aggregate-only probes, record the outcome per table and per column used by any formula, and confirm the record exists before any formula references those tables.

**Acceptance Scenarios**:

1. **Given** the on-site checklist extended with a purchasing section, **When** the developer verifies purchase invoice, purchase return, vendor and product tables on-site, **Then** each table, key column, date column, amount column, discount column, VAT column and operation code used by a formula, and the discount basis of Mabna's own purchase report total, is recorded as confirmed, corrected, or unknown.
2. **Given** a source item recorded as unknown, **When** a formula would need it, **Then** that formula is not implemented on it and the dependent figure shows "data unavailable".
3. **Given** a Jalali date column in a purchasing table, **When** its type and format are verified, **Then** the conversion rule is documented before any date filter uses it.
4. **Given** verification results, **When** they are recorded, **Then** no business rows leave the site; only schema metadata and approved aggregates are kept, and nothing from the live server is committed.

---

### User Story 2 - Admin syncs purchasing data and sees the result (Priority: P1)

An Admin opens Settings → Data sync, clicks "Sync now", and watches the run complete. The page lists each sync run with status, start and end time, duration, and row counts per table. Each night the system runs an incremental sync on its own. If a run fails, the previous data stays in place and the failure is visible with its error.

**Why this priority**: Without synced real data there is nothing to reconcile or show. The sync is the foundation of all Release 1 reports.

**Independent Test**: Against the simulator, trigger "Sync now", then confirm the Data sync page shows the run with status, duration and per-table row counts, and that the synced rows match the simulator's row counts for the allowlisted purchasing tables.

**Acceptance Scenarios**:

1. **Given** an Admin on the Data sync page and the simulator reachable, **When** the Admin clicks "Sync now", **Then** a sync run starts and the page shows the job with status, duration, and row counts per table when it finishes.
2. **Given** a sync run already in progress, **When** an Admin clicks "Sync now", **Then** no second run starts and the page shows the running job.
3. **Given** a previous successful sync, **When** a sync run is forced to fail (for example the source is unreachable or a source query times out), **Then** the previously synced data is kept unchanged, the run is marked failed, and its error is shown on the Data sync page.
4. **Given** the nightly schedule (default 02:00 local server time), **When** the scheduled time arrives, **Then** an incremental sync runs without Admin action and appears on the Data sync page as a scheduled run.
5. **Given** an incremental sync after a full load, **When** source rows were added or changed since the last successful sync, **Then** only those changes are applied, and the resulting synced data equals what a full load would produce.
6. **Given** any page that shows data, **When** it is displayed, **Then** it shows the completion time of the last successful sync (Jalali date and time).
7. **Given** a user without the Admin role, **When** they open the Data sync page or call its actions directly, **Then** the server refuses the request.

---

### User Story 3 - Read-only safety of the source connection (Priority: P1)

Every read from Mabna goes through the read-only login and the connector's table allowlist. Any statement other than SELECT, and any table not on the allowlist, is refused by the connector before it reaches the server.

**Why this priority**: Mabna must never be written to. The read-only login is the primary control; the connector guard is the secondary control that this phase must keep proving as purchasing tables join the allowlist.

**Independent Test**: Run the connector guard tests with a non-SELECT statement and with a non-allowlisted table name, and confirm both are refused without any network call to the source server.

**Acceptance Scenarios**:

1. **Given** the connector, **When** an INSERT, UPDATE, DELETE, MERGE, EXEC, or DDL statement is submitted, **Then** it is refused before reaching the server.
2. **Given** the connector, **When** a query names a table not on the allowlist (including user and credential tables such as `scuUser`, `UserRights`, any `scu*` or `msg*` table), **Then** it is refused before reaching the server.
3. **Given** the purchasing tables verified in User Story 1, **When** they are added to the allowlist, **Then** only the verified tables are added, and Mabna's `HOST_ID()`-filtered report views remain excluded from live reads.

---

### User Story 4 - Purchase figures that match Mabna (Priority: P1)

The system computes purchase figures with one documented definition each: net purchases (purchase invoices minus purchase returns, excluding VAT, dated by document date), gross purchases, returns, and VAT as separate figures. A reconciliation check, run by the developer as a command or automated test, compares the system's totals for a period and filter with the same query run directly against the source data and logs the result. A mismatch blocks sign-off.

**Why this priority**: The Manager must be able to trust Zharfa instead of re-checking it. Reconciliation is a sign-off gate in `AGENTS.md`.

**Independent Test**: For one month and one filter (for example one vendor) on the simulator, run the reconciliation check and confirm the system's net, gross, returns and VAT totals equal the direct-source query and the result is logged.

**Acceptance Scenarios**:

1. **Given** synced simulator data, **When** the reconciliation check runs for at least one Jalali month and at least one filter, **Then** the system's purchase totals equal the direct-source query totals and the result is logged.
2. **Given** a reconciliation run where totals differ, **When** the mismatch is detected, **Then** it is recorded with period, filter, both totals and the difference, and the phase is not signed off until it is resolved.
3. **Given** the on-site install, **When** the reconciliation check runs for one real month, **Then** the totals equal the direct-source query on real data, and only the pass/fail result is recorded in the repository (real figures stay on-site).
4. **Given** a vendor or product name containing Arabic ي or ك, **When** it is synced, **Then** it is stored with Persian ی and ک.
5. **Given** a purchase document with a Jalali date in the source, **When** it is synced, **Then** it is assigned to the correct Jalali month in every figure.
6. **Given** the metric definitions document, **When** a reader looks up any purchase metric, **Then** they find one definition for it, used unchanged by the Overview, the reconciliation check, and (in later phases) reports, exports and AI answers.

---

### User Story 5 - Manager sees real purchasing on the Overview (Priority: P2)

A Manager opens the Overview, picks a Jalali month, and sees net purchases, gross purchases, returns, VAT, the comparison with the previous period, the monthly trend, the number of active vendors, and the top 5 vendors and top 5 products by net purchases, together with the last-sync time.

**Why this priority**: This is the visible outcome of the phase for the primary persona, but it depends on User Stories 1, 2 and 4.

**Independent Test**: With synced simulator data, open the Overview as a Manager, choose a month, and confirm every figure matches the reconciled totals for that month and changes when the month changes.

**Acceptance Scenarios**:

1. **Given** a Manager signed in and synced data, **When** the Manager opens Overview and chooses a Jalali month, **Then** it shows net purchases, gross purchases, returns, VAT, comparison with the preceding Jalali month, 12-month trend, active vendors, top 5 vendors and top 5 products for that month, plus the last-sync time.
2. **Given** the Overview, **When** the Manager changes the Jalali period, **Then** every figure on the page updates to the new period.
3. **Given** a figure that cannot be computed from the synced data (for example a source item recorded as unknown, or no successful sync yet), **When** the Overview is shown, **Then** that figure reads "data unavailable" instead of a number.
4. **Given** the Overview net purchases for a month, **When** compared with the reconciliation check for the same month, **Then** the values are equal.
5. **Given** figures on the Overview, **When** displayed, **Then** they use Persian digits, amounts in Rial labeled "ریال" and Jalali dates in an RTL layout, and are labeled as real company data in a production install.

---

### User Story 6 - Real and demo data never mix (Priority: P2)

Each install runs in exactly one data mode. A production-mode install shows only real-data modules and never loads the synthetic dataset or Demo 1 pages. A demo-mode install labels every page, report, export and AI answer as demonstration data. The AI agent answers from one dataset per install only.

**Why this priority**: Once real data arrives, mistaking demo numbers for company figures (or the reverse) is a serious trust failure.

**Independent Test**: Start one install in production mode and one in demo mode; confirm the production install shows no synthetic data or Demo 1 pages, and the demo install labels everything as demonstration data.

**Acceptance Scenarios**:

1. **Given** an install in production mode, **When** any user navigates the app or calls the API, **Then** no synthetic data, Demo 1 pages (Demo 1 sales, inventory alerts, receivables) or demo labels appear, and the synthetic dataset is not loaded.
2. **Given** an install in demo mode, **When** any page, report, export or AI answer is shown, **Then** it is labeled as demonstration data.
3. **Given** an install in either mode, **When** the AI agent answers, **Then** it uses only that install's dataset and never combines real and synthetic figures; in a production-mode install during Phase 1 the AI agent is hidden in the UI and refused by the API until Phase 4 adds AI over real data.
4. **Given** an install, **When** its mode is checked, **Then** the mode is set by server configuration only and cannot be switched from the browser.

---

### Edge Cases

- No successful sync has run yet: Overview figures show "data unavailable" and the last-sync indicator states that no sync has completed.
- The source server is unreachable or a query exceeds its timeout (default 60 seconds): the run fails, previous data is kept, the error is shown.
- A sync is interrupted midway (service restart): the run is marked failed, and partially applied changes are not visible to reports.
- The nightly schedule fires while an Admin-started run is in progress: no overlapping run starts; the scheduled run is skipped and that is recorded.
- A purchase return is dated in a different month from its original invoice: it reduces net purchases in the month of the return document date.
- A purchase line has no vendor or product, or references a vendor or product missing from the master: the line still counts in company totals where its amounts are valid, and appears under an explicit "unknown" entry in top-5 lists rather than being silently dropped (detailed data-quality reporting is Phase 3).
- Cancelled purchase documents: excluded from all figures, following the rule verified on-site.
- A selected month with no purchases: figures show zero (not "data unavailable"), and the previous-period comparison handles a zero base without a misleading percentage.
- Fewer than 5 vendors or products in the period: the top lists show only those that exist.
- Vendor names that differ only by ي/ك vs ی/ک: after normalization they are treated as the same text.
- Jalali leap year (Esfand with 30 days): month boundaries are correct.
- The selected month is the first month with synced data: the previous-month comparison shows "data unavailable" for the missing base, and the 12-month trend shows only months that have data.
- Mabna's purchase report total uses a discount basis different from line-level discounts (for example it also subtracts document-level discounts): the net purchases definition follows the verified Mabna basis, the metric definitions document is updated, and reconciliation is rerun.
- A production-mode user or client calls an AI agent endpoint during Phase 1: the server refuses the request.

## Requirements *(mandatory)*

### Functional Requirements

**Source verification**

- **FR-001**: The purchase invoice, purchase return, vendor and product source tables, and every column and operation code used by a Phase 1 formula, MUST be verified on-site and recorded as confirmed, corrected, or unknown before any formula uses them.
- **FR-002**: Verification MUST use catalog metadata and owner-approved aggregate probes only; no business rows from the live source MAY be stored off-site or committed.
- **FR-003**: The development simulator MUST be extended with synthetic purchasing data matching the verified structure, so sync and reconciliation can be tested off-site.

**Sync (F1)**

- **FR-004**: The system MUST read the source only through the read-only login and the connector's table allowlist; any non-SELECT statement and any non-allowlisted table MUST be refused before reaching the server.
- **FR-005**: Only verified purchasing tables MAY be added to the allowlist; Mabna's `HOST_ID()`-filtered report views MUST NOT be read on live Mabna.
- **FR-006**: The system MUST perform a full initial load and, after that, incremental syncs of vendors, products, purchase documents and purchase returns into the reporting database.
- **FR-007**: A nightly schedule (default 02:00 local server time, configurable by the operator) MUST run an incremental sync without Admin action.
- **FR-008**: An Admin MUST be able to start a sync with "Sync now"; if a run is already in progress, no new run starts and the running job is shown.
- **FR-009**: Every sync run MUST be recorded with trigger (scheduled or manual), start time, end time, duration, status, row counts per table, and error details on failure.
- **FR-010**: If a sync run fails, the system MUST keep the last successfully synced data unchanged, mark the run failed, and show the error on the Data sync page.
- **FR-011**: Each source query MUST have a timeout (default 60 seconds).
- **FR-012**: Every page that shows data MUST display the completion time of the last successful sync.
- **FR-013**: The Data sync page and its actions MUST be available to the Admin role only, enforced on the server.

**Metrics and reconciliation (F2)**

- **FR-014**: Net purchases MUST be computed as purchase invoices minus purchase returns, excluding VAT, dated by document date, using the same discount basis as Mabna's own purchase report total once that basis is verified on-site; until then, amounts MUST be taken after line-level discounts as recorded on each invoice line. Gross purchases, returns and VAT MUST be available as separate figures, and the discount basis in use MUST be stated in the metric definitions document.
- **FR-015**: Each purchase metric MUST have exactly one documented definition, used by every consumer (Overview and reconciliation in this phase; reports, exports and AI answers in later phases). Purchases MUST NOT be presented as payments or vendor balance, and price differences MUST NOT be presented as profit.
- **FR-016**: All KPI calculations MUST be performed on the server.
- **FR-017**: A reconciliation check, run by the developer as a command or automated test (no UI in this phase), MUST compare the system's purchase totals for a given period and filter with the same query run directly against the source data, and MUST write a reconciliation log with period, filter, both totals, difference and pass/fail. On-site, the check runs for one real month and only its pass/fail outcome is recorded in the repository.
- **FR-018**: A reconciliation mismatch MUST be recorded and MUST block phase sign-off until resolved.
- **FR-019**: Jalali dates MUST be converted, and Arabic ي/ك normalized to Persian ی/ک, at ingestion.

**Data mode (F3)**

- **FR-020**: Each install MUST run in exactly one data mode (production or demo), set by server configuration.
- **FR-021**: In production mode, the system MUST NOT load the synthetic dataset and MUST NOT show Demo 1 pages or demo modules, in the UI or the API.
- **FR-022**: In demo mode, every page, report, export and AI answer MUST be labeled as demonstration data.
- **FR-023**: The AI agent MUST answer from only the install's own dataset and MUST NOT combine real and synthetic figures. In production mode during Phase 1, the AI agent MUST be hidden in the UI and refused by the server API until Phase 4 adds AI over real data.
- **FR-024**: In production mode, figures MUST be labeled as real company data.

**Overview (F4)**

- **FR-025**: The Overview MUST show, for a selected Jalali month (the only selectable period in this phase; default the current Jalali month): net purchases, gross purchases, returns, VAT, the comparison with the immediately preceding Jalali month, a monthly trend of the last 12 Jalali months, the number of active vendors, the top 5 vendors and the top 5 products by net purchases, and the last-sync time.
- **FR-026**: Changing the Jalali period MUST update every Overview figure to that period.
- **FR-027**: A figure that cannot be computed from synced data MUST show "data unavailable" instead of a number.
- **FR-028**: The Overview MUST be visible to users with Overview permission; until Phase 3 introduces new roles, Demo 1's Manager and Admin roles apply.
- **FR-029**: The Overview MUST follow Persian-first conventions: RTL layout, Jalali dates, Persian digits, and amounts shown in Rial labeled "ریال", matching Mabna's screens.

### Key Entities

- **Vendor**: A supplier from the Mabna vendor master; source identifier, normalized Persian name. A vendor is "active" in a period if it has at least one non-cancelled purchase or return document in that period.
- **Product**: An item from the Mabna product master; source identifier, normalized Persian name, and (for later phases) drug group/class.
- **Purchase document**: A purchase invoice or purchase return header; source identifier, type (invoice or return), vendor, Jalali and Gregorian document date, cancelled flag, and totals.
- **Purchase line**: A line on a purchase document; product, quantity, amount before VAT, VAT and discount as recorded in the source.
- **Source verification record**: For each source table, column or code used by a formula, the status confirmed, corrected, or unknown, with the date verified.
- **Sync job**: One sync run; trigger, start and end time, duration, status, and error.
- **Sync log entry**: Per-table detail of a sync run: rows read, inserted, updated, deleted, and errors.
- **Reconciliation result**: Period, filter, metric, system total, direct-source total, difference, pass/fail, run time.
- **Metric definition**: The documented formula for net purchases, gross purchases, returns and VAT.
- **Data mode**: The install-level setting, production or demo.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the source tables, columns and codes used by Phase 1 formulas carry a recorded verification status (confirmed, corrected, or unknown) before any formula uses them.
- **SC-002**: An Admin can start a sync and see its finished result (status, duration, per-table row counts) on the Data sync page without leaving it, and at least one scheduled nightly sync completes on the simulator with no Admin action.
- **SC-003**: A forced sync failure leaves 100% of previously synced figures unchanged and shows the failure with its error.
- **SC-004**: System purchase totals (net, gross, returns, VAT) equal the direct-source query exactly for at least one month and one filter on the simulator, and for one real month on-site.
- **SC-005**: A Manager can open the Overview and read the selected month's net purchases, top vendors and last-sync time in under 10 seconds; Overview figures load within 2 seconds with three years of data.
- **SC-006**: A production-mode install shows zero synthetic figures and zero Demo 1 pages; a demo-mode install labels 100% of its pages as demonstration data.
- **SC-007**: 100% of attempted non-SELECT statements and non-allowlisted tables are refused by the connector before reaching the server; zero write statements reach Mabna.

## Assumptions

- **Default purchase formula**: Net purchases = purchase invoices − purchase returns, excluding VAT, by document date (PRD F2). The discount basis follows Mabna's own purchase report total once verified on-site (clarified 2026-11-16); until then, amounts are taken after line-level discounts as recorded on the invoice, and the basis in use is stated in the metric definitions. Accounting still confirms the treatment before sign-off (PRD open question 2). Gross, returns and VAT are shown separately so the definition can change without data loss.
- **Returns dating**: A purchase return reduces net purchases in the month of its own document date, not the original invoice's month.
- **Overview period**: The selected period is a Jalali month only (default: current Jalali month); the previous period is the immediately preceding Jalali month; the monthly trend covers the last 12 Jalali months. Quarter, year and equal-length period comparison come with Phase 2 (F8).
- **Currency**: Amounts are stored as recorded in the source (assumed Rial, verified on-site) and displayed in Rial labeled "ریال" with the existing formatting helpers; no Toman display or toggle in this phase.
- **Roles**: No new roles in this phase. Demo 1's Manager sees the Overview; Admin sees the Overview and Data sync (Phase 1 out-of-scope note).
- **Incremental sync**: Change detection uses whatever the verified source supports (for example a modification timestamp, row version, or increasing document id); a table without a reliable change marker is fully reloaded each run. Source deletions (for example cancelled documents whose items are removed) are reflected on the next sync.
- **Sync atomicity**: A run's changes become visible to reports only when the run succeeds, so a failed run never leaves a half-updated dataset.
- **Data mode switch**: The mode is chosen at install time by server configuration; switching an existing install between modes is an operator action outside the UI.
- **Demo 1 pages**: In production mode the Demo 1 sales, inventory alerts and receivables pages and their APIs, and the Demo 1 AI agent, are hidden and refused; their code stays in the repository for demo installs.
- **On-site reconciliation**: The reconciliation check is a developer-run command or automated test with no UI; the real-month check is run on-site by the developer; real totals stay on-site and only the pass/fail outcome is recorded in the repository.
- **Dependencies**: The spec 002 read-only connector, SELECT-only guard, allowlist and simulator; the reserved `SourceRecord/SyncJob/SyncLog` tables; Demo 1 authentication, Manager/Admin roles, Persian formatting and Jalali helpers; owner approval for any new aggregate value probes on-site; accounting confirmation of the purchase formula before sign-off.

## Out of Scope

- Vendor directory and profile, purchasing reports, vendor comparison, and report capabilities such as drill-down, period-comparison pages and browser-print (Phase 2: F5 AC1–AC2, F6, F7, F8).
- New roles Management, Purchasing and Finance; audit-log extensions; Data quality page (Phase 3: F9, F10, F11). Demo 1's Manager and Admin roles apply until then.
- AI over real data, the local model provider and its benchmark (Phase 4: F12, F13).
- AI dynamic reports, saved reports and Excel export (Phase 5: F14, F15, F16).
- Vendor financials: balances, payments, payable cheques, purchases vs payments (Phase 6: F17, F5 AC3).
- LAN production deployment, HTTPS, backup and go-live checklist (Phase 7: F18).
- Deferred items F19–F33: sales, pharmacy, sales financials and receivables on real data, cross-domain reports, warehouse and inventory, Sales/Warehouse/Viewer roles, AI pattern detection, remote access, mobile/PWA, English UI, Platform Assistant.
- Any write to Mabna, live per-request queries to Mabna for reports, and reading `HOST_ID()`-filtered report views on live Mabna.
