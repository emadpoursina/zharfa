# Feature Specification: AI Reports, Saved Reports, and Excel Export (Release 1, Phase 5)

**Feature Branch**: `zharfa-prd-phase-5`

**Created**: 2026-11-18

**Status**: Draft

**Input**: User description: "phase: zharfa-prd.phases.md#5 — Ship Zharfa Release 1 Phase 5 so users can get tables/charts from purchasing questions, save reports, and export Excel (F14–F16)."

**Scope authority**: `zharfa-prd.md` v3.0, Phase 5 of `zharfa-prd.phases.md` ("AI reports, saved reports, and Excel export"), covering F14 (AI dynamic reports), F15 (saved reports) and F16 (Excel export) in full, plus the F10 audit events for exports (and save/open of saved reports) that Phase 3 reserved until those actions exist. `AGENTS.md` rules bind every decision. Phase 2 (`specs/004-vendor-purchasing-reports`) and Phase 4 (`specs/006-local-ai-purchasing`) are existing behavior this phase extends: standard purchasing reports and the cited-text purchasing assistant. Phases 1–4 specs are not reopened.

**Phase goal**: Users turn questions into tables and charts, save any report for later, and take report tables into Excel.

## Clarifications

### Carried over from earlier phases (owner decisions, binding)

- Release 1 roles are Admin, Management, Purchasing and Finance. Vendor financials are visible to Management and Finance only; no vendor-balance figures exist until Phase 6. All authorization is enforced on the server.
- Export permission is held by Management, Purchasing and Finance; Admin does not hold it (Phase 3 FR-002). AI-queries permission is held by Management, Purchasing and Finance; Admin does not hold it.
- Amounts are shown in Rial, labeled "ریال". Jalali calendar, Persian RTL, Persian digits.
- Simulator example columns and objects use `Sim*` names; real Mabna data never enters the repository; development uses `deploy/mabna-sim/`.
- Each install is exactly one data mode. Production shows real data only; demo installs label everything as demonstration data (including generated tables, charts, saved reports and exports). The AI uses one dataset per install.
- Audit entries never contain secrets, passwords, provider keys, database credentials, or result rows. Phase 3 reserved export events; Phase 4 already audits AI questions.
- Source-item status rules are unchanged. A figure that cannot be computed shows "data unavailable", never an estimate. Purchases, payments and vendor balance stay separate; a price difference is never profit.
- Phase 4 assistant rules still apply: approved purchasing tools only, 100-row cap per tool, 10-second tool timeout, 30-second user-visible answer budget, independent questions (no chat memory), at most one in-flight question per user, facts separate from interpretation, "data unavailable" vs zero vs assistant-unavailable.
- Phase 2 print of a report remains browser-print of the whole filtered result capped at 1,000 rows; this phase adds Excel export as a separate action.
- Period comparison rules stay as Phase 2: calendar presets compare with the previous Jalali unit of the same kind; a custom range compares with the same number of days ending the day before the range starts.

### Assumptions taken in place of questions (no-question run; skip)

- **When a table and a chart appear.** A question that asks for a comparison or a ranking (including "top N", "compare", "بیشترین", "مقایسه") returns a table. A chart is included when the result has two or more rows and at most 20 series or categories; otherwise the table stands alone. A question that is not a comparison or ranking keeps Phase 4 cited-text answers (period, filters, source metric) and does not invent a table. "Data unavailable" and assistant-unavailable answers have no table or chart.
- **Canonical Phase 5 question.** "Compare the top 10 purchased products over the last six months" (and its Persian equivalent) is a ranking-plus-period question. "Last six months" means the six Jalali months ending with the Overview default month (current Jalali month). Ranking is by net purchase amount, same definition as the most-purchased-products report. The table has one row per product (up to 10) with the same measures that report would show for that ranking and period. The chart is a bar chart of those ten products by net purchases. Each of table and chart is labeled with period, filters and metrics. Figures match the purchasing reports for the same period and filters.
- **Tables and charts are built only from approved metric results.** The assistant still cannot invent queries, read Mabna, or use tools outside the Phase 4 purchasing allowlist. Chart series are those metric columns; no second calculation. Cloud-bound payloads (if cloud is enabled) remain aggregates the user could see — a generated table is still an approved-metric result, not raw document lines.
- **Keeping a generated report.** After a table (and optional chart) is shown, the user can save it. The save stores a name, the report type "generated", and the filters/period/metrics/ranking that produced it — not a snapshot of numbers. Reopening re-runs those metrics on current synced data and applies the same table-and-chart rules as a live generated answer.
- **Saving a standard report.** From a Phase 2 purchasing report, vendor profile purchasing tables, vendor comparison, or drill-down document list, the user can save the current report type, period and filters under a name. Overview KPI tiles are not saved as reports in this phase.
- **Whose saved reports.** Saved reports belong to the signed-in user who saved them. A user sees only their own list. Opening another user's saved report is refused with a Persian reason. Names are unique per user after Persian ی/ک normalization and trimming; saving with an existing name asks for overwrite confirmation, then replaces that saved report's type and filters. Empty names are refused. A user may keep at most 50 saved reports; at the cap, a **new** name is refused until they delete one, but replacing an existing name is still allowed after confirm.
- **Delete.** A user may delete their own saved report. Delete is audited. There is no share, schedule or email.
- **Re-run on current data.** Opening a saved report always recomputes from the latest successful sync. If the needed source is unverified or there is no successful sync, the opened report shows "data unavailable" like live reports. Last-sync time is shown.
- **Permission on reopen (exit criterion).** Each saved report records the permission required to see its data (purchasing reports for standard and generated purchasing reports; vendor-financials later in Phase 6). Opening checks the caller's **current** role. If that permission is missing, the server refuses with a Persian reason naming the missing permission, does not run the query, and writes an access-denied audit entry. **Phase 5 test:** a Management user saves a **generated** report; an Admin then changes that user to Admin (Admin lacks AI-queries). Generated saved reports additionally require AI-queries to reopen, because they originate from the assistant. The former Management user is then refused with a reason. A standard purchasing saved report remains openable by Admin (Admin holds purchasing reports) but cannot be exported (Admin lacks export).
- **Who may save.** Anyone who can open the live report may save a standard report (including Admin). Saving a generated report requires AI-queries (the keep action is on the assistant). Admin cannot save generated reports.
- **Excel export contents.** Export produces one `.xlsx` file of the **current filtered report table** on screen — live or reopened saved report (not the chart image, not only the visible page). Headers are Persian and match the on-screen columns. The sheet direction is RTL. A header/meta area (not result rows) states: report name, period, active filters, export time (Jalali date and time), last-sync time, and the install's data-mode label (demonstration data vs real). Amounts remain Rial labeled "ریال"; digits Persian. Demo-mode files are labeled demonstration data. The download name is the Persian report name, Jalali export date, and `.xlsx`.
- **Export row cap.** The file includes the whole filtered result up to 10,000 rows. Above the cap the file states it is truncated (first 10,000 of N) and asks the user to narrow filters. This is separate from the 1,000-row print cap.
- **What can be exported.** Standard purchasing report tables, vendor comparison table, vendor profile document/product tables, drill-down document lists, and AI-generated tables. Charts are not exported as drawings. Vendor-financial tables do not exist yet; any reserved financial export is refused by existing permission rules (Purchasing 403; Management/Finance "data unavailable" until Phase 6).
- **Export permission.** Management, Purchasing and Finance may export. Admin and unsigned callers cannot: Admin receives 403 (and the attempt is logged); unsigned receives 401. The UI does not offer export where the role lacks it; the server is the enforcement.
- **Audit events added in this phase.** Each successful export: user, time, action, target (report type/name), period and filters (no result rows). Each refused export: access-denied as Phase 3. Each save, replace, open and delete of a saved report: user, time, action, saved-report name and type, period and filters when relevant. AI questions remain audited as in Phase 4. Audit write failure does not change the action's outcome (Phase 3 FR-018), except a refused request stays refused.
- **Data-quality notice.** A generated table/chart, a reopened saved report, and an Excel file that includes figures depending on flagged records carry the same data-quality notice Phase 3 already places on reports (Persian; Admin may link to Data quality). The Excel meta area includes the notice text when it applies.
- **Performance.** Opening a saved report or producing an export of a typical filtered purchasing table meets the PRD p95 under 2 seconds on synced data for the data fetch; building the `.xlsx` for up to a few thousand rows completes in under 10 seconds. AI table/chart answers stay within the Phase 4 30-second user-visible budget.

### Session 2026-11-18

- Q: When a user saves a report using a name they already have, should the product replace it immediately or ask them to confirm first? → A: (skip mode, recommended option) Ask to confirm overwrite, then replace. Cancel leaves the existing saved report unchanged.
- Q: When the owner opens a saved generated report, should they see the table and chart again, or only the table? → A: (skip mode, recommended option) Re-run uses the same table-and-chart rules as a live generated answer: a labeled table always; a chart when the current result has two to twenty rows or categories.
- Q: If the user already has 50 saved reports, may they still save using an existing name, or is every save refused until they delete one? → A: (skip mode, recommended option) Replace of an existing name is allowed at the cap (still after overwrite confirm). Only a new name is refused until they delete one.
- Q: Can a user with export permission export the table of a saved report they have opened, the same way they export a live report? → A: (skip mode, recommended option) Yes. Export always means the current on-screen table (live or reopened), with the same permission, 10,000-row cap, meta, data-quality notice and audit rules.
- Q: What should the downloaded Excel file be named? → A: (skip mode, recommended option) The report’s Persian name, then the Jalali export date, then `.xlsx`. Characters that cannot appear in a file name are stripped; if the name is empty after that, the product uses «گزارش».

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Assistant returns a table and chart for ranking questions (Priority: P1)

A Management user (or Purchasing/Finance with AI-queries) asks in Persian to compare the top 10 purchased products over the last six months. The assistant returns a table and a bar chart. Each is labeled with the period (six Jalali months ending with the current month), the filters, and the metrics (net purchases, with gross/returns/VAT kept separate if shown). The figures match the most-purchased-products purchasing report for the same period and ranking. Interpretation, if any, is visually separate and introduces no extra numbers.

**Why this priority**: Phase 5 goal, F14 AC1–AC2, Journey 1 step 5, and the first exit criterion.

**Independent Test**: After a successful simulator sync, sign in as Management, ask the canonical question in Persian; confirm table and chart, labels, and that each of the ten product net-purchase figures equals the same ranking on the most-purchased-products report for those six months.

**Acceptance Scenarios**:

1. **Given** a signed-in Management user and a successful purchasing sync, **When** they ask "Compare the top 10 purchased products over the last six months" (or the Persian equivalent), **Then** the assistant returns a table and a chart of those products, each labeled with period, filters and metrics, and the figures match the purchasing reports for the same period and filters.
2. **Given** that answer, **When** a reader looks at the table and the chart, **Then** both cite the six Jalali months ending with the current month, ranking by net purchase amount, and no other undocumented metric.
3. **Given** a comparison or ranking question with two to twenty result rows, **When** it is asked, **Then** a table is always returned and a chart is returned as well.
4. **Given** a ranking that would have more than twenty categories, **When** it is asked, **Then** a labeled table is returned and no chart is drawn.
5. **Given** a purchasing question that is not a comparison or ranking, **When** it is asked, **Then** the answer remains cited text as in Phase 4 (no obligatory table or chart).
6. **Given** missing, unverified or out-of-scope data, **When** a ranking question is asked, **Then** the answer is "data unavailable" with no table, chart or guessed series.
7. **Given** a Purchasing user asking a ranking that would include vendor balances or payments, **When** it is asked, **Then** no financial figures appear in the table or chart.
8. **Given** a demo-mode install, **When** a table or chart is shown, **Then** it is labeled as demonstration data and uses only the demo dataset.
9. **Given** an Admin, **When** they ask for a generated table, **Then** the server answers 403 and no table is produced (unchanged AI-queries rule).

---

### User Story 2 - Save a generated report and a standard report, then reopen on current data (Priority: P1)

After a generated table is shown, the user saves it under a Persian name. From a standard purchasing report (filters applied), they save that too. Later they open each saved report and see current figures for the stored type, period and filters — not the old numbers — with last-sync time. The saved-reports list shows only that user's names.

**Why this priority**: F14 AC3, F15 AC1–AC2, Journey 1 step 6 (save), and the second exit criterion.

**Independent Test**: As Management, save one generated ranking and one filtered most-purchased-products (or by-vendor) report; run a new sync that changes totals; reopen both; confirm names, types, filters and that figures match live reports on the new data.

**Acceptance Scenarios**:

1. **Given** a generated table from a permitted user, **When** they save it with a non-empty name, **Then** it appears in that user's saved reports with type generated, the period/filters/metrics used, and that name.
2. **Given** a standard purchasing report with period and filters set, **When** the user saves it with a name, **Then** it is stored as that report type with those filters for that user.
3. **Given** a saved generated report and a saved standard report, **When** the owner opens each after data has changed (new successful sync), **Then** each re-runs on current data with the stored type and filters, figures match the live report or assistant metrics for those filters, and a generated report that still has two to twenty rows also shows a chart (otherwise the table stands alone).
4. **Given** user A's saved reports, **When** user B opens the saved-reports list, **Then** B does not see A's names; if B requests A's report directly, they are refused with a Persian reason.
5. **Given** a save with a name the same user already used, **When** they confirm overwrite, **Then** the existing saved report is replaced (new type/filters, same name). **When** they cancel, **Then** the existing saved report is unchanged.
6. **Given** a demo-mode install, **When** a saved report is listed or opened, **Then** it is labeled as demonstration data.

---

### User Story 3 - Saved report is refused when permission is gone (Priority: P1)

A user who saved a generated report later loses the permission needed to reopen it (role changed so they no longer hold AI-queries). Opening that saved report is refused with a clear Persian reason. No figures are returned. The attempt is audit-logged as access denied.

**Why this priority**: F15 AC3 and the third exit criterion; this is the guard Phase 6 will reuse for vendor-financial saved reports.

**Independent Test**: As Management, save a generated report; as Admin, change that user to Admin; as that user, open the saved report; confirm refusal with a reason, no table, and an access-denied audit entry.

**Acceptance Scenarios**:

1. **Given** a Management user who saved a generated report, **When** an Admin changes that user's role to Admin and the user opens that saved report, **Then** the open is refused with a Persian reason that they no longer have permission, and no metric results are returned.
2. **Given** that refused open, **When** an Admin views the audit log, **Then** an access-denied entry records the user, time, saved-report target and missing permission, with no result rows.
3. **Given** the same user still holds purchasing-reports permission, **When** they open a standard purchasing saved report they own, **Then** it opens on current data (Admin still has purchasing reports).
4. **Given** a user without a session, **When** they request a saved report, **Then** they are treated as not signed in (401), with no data.

---

### User Story 4 - Export a filtered report to Excel (Priority: P1)

A Purchasing or Finance (or Management) user, with export permission, exports the current filtered report table. They receive an `.xlsx` file with Persian headers, RTL sheet direction, and meta stating period, filters, export time and last-sync time. Figures match the on-screen filtered table (within the export row cap).

**Why this priority**: F16 AC1–AC2, Journey 1 step 6 (export), and the fourth exit criterion.

**Independent Test**: As Purchasing, open most-purchased products, apply a period and a vendor or category filter, export; open the file and confirm Persian RTL headers, meta fields, and row values equal the filtered report.

**Acceptance Scenarios**:

1. **Given** a signed-in user with export permission and a filtered purchasing report on screen, **When** they export, **Then** they receive an `.xlsx` whose table headers are Persian, whose sheet direction is RTL, and whose meta area states period, filters, export time and last-sync time.
2. **Given** that file, **When** its data rows are compared with the on-screen filtered report (up to the export cap), **Then** they match, including Rial amounts labeled "ریال" and Jalali dates.
3. **Given** a generated table the user is allowed to see, **When** they export it with export permission, **Then** the file contains that table plus the same meta (period, filters, export time, last-sync).
4. **Given** a result larger than 10,000 rows, **When** they export, **Then** the file contains the first 10,000 rows and states that it is truncated and that filters should be narrowed.
5. **Given** a demo-mode install, **When** they export, **Then** the file is labeled as demonstration data.
6. **Given** figures that depend on flagged records, **When** they export, **Then** the file includes the data-quality notice text.
7. **Given** a reopened saved report on screen and export permission, **When** they export, **Then** they receive the same kind of `.xlsx` as a live report of that table (Persian headers, RTL, meta, cap, audit).
8. **Given** a successful export, **When** the file is saved on the user's computer, **Then** its name is the report's Persian name, the Jalali export date, and `.xlsx` (illegal file-name characters stripped; empty remainder becomes «گزارش»).

---

### User Story 5 - Export is audited; users without export permission are refused (Priority: P1)

Every export is recorded in the Admin audit log (user, time, action, report target, period and filters; no rows). A user without export permission who requests an export gets 403 and no file. Admin is such a user.

**Why this priority**: F16 AC3–AC4, F10 export events, and the fifth exit criterion.

**Independent Test**: Export once as Purchasing; attempt export as Admin (UI or direct request); as Admin, filter the audit log and confirm the successful export and the 403, with no result rows or secrets.

**Acceptance Scenarios**:

1. **Given** a permitted export, **When** an Admin views the audit log, **Then** an entry shows the user, Jalali date and time, that an export occurred, the report name/type, period and filters, and no result rows or figures.
2. **Given** a signed-in Admin (no export permission), **When** they request an export, **Then** the server returns 403 with no file, and the attempt is audit-logged as access denied.
3. **Given** a signed-in user whose role lacks export, **When** they open a report, **Then** export is not offered, and a direct export request still returns 403.
4. **Given** a caller with no session, **When** they request an export, **Then** they receive 401 and no file.
5. **Given** any audit entry from save, open, delete or export, **When** it is inspected, **Then** it contains no secrets, keys or result rows.

---

### User Story 6 - Manage own saved reports within the cap (Priority: P2)

The user lists their saved reports by name, opens one, replaces one by saving the same name, and deletes one they no longer need. At 50 saved reports, a new name is refused until they delete.

**Why this priority**: Makes F15 usable day to day; not an exit criterion by itself.

**Independent Test**: Save two reports, delete one, confirm it is gone; confirm a duplicate name replaces; confirm the 50th extra name is refused with a Persian cap message.

**Acceptance Scenarios**:

1. **Given** saved reports owned by the user, **When** they open the saved-reports list, **Then** they see only their names, types and stored period/filters.
2. **Given** a saved report they own, **When** they delete it, **Then** it no longer appears and cannot be opened, and the delete is audited.
3. **Given** 50 saved reports already, **When** they save a **new** name, **Then** save is refused with a Persian reason until they delete one.
4. **Given** 50 saved reports already, **When** they save using an existing name and confirm overwrite, **Then** that saved report is replaced and the count stays 50.

---

### Edge Cases

- AI provider down: ranking questions get the assistant-unavailable message; no table/chart; saved reports and Excel export of standard reports still work.
- Second question while one is in flight: still refused with the Phase 4 wait message; no second table.
- Question omits period on a ranking: six-month canonical wording supplies six months; other ranking questions without a period use the Overview default month and cite it.
- Arabic ي/ك in a product or vendor name in a ranking question: same ی/ک matching as Phase 2 search.
- Empty ranking (no purchases in the six months): table of zero rows (or an empty-state table) with labels; chart omitted (fewer than two rows); figures are zero, not "data unavailable".
- Save with whitespace-only name: refused as missing name.
- Save with an existing name: overwrite is offered; confirm replaces type and filters; cancel leaves the previous saved report.
- Fifty saved reports and a new name: refused with the cap message. Fifty saved reports and an existing name: overwrite confirm still allowed.
- Save while data-quality notice is showing: allowed; reopen and export still show the notice when records remain flagged.
- Export of an empty filtered table: file still has Persian headers, RTL, and meta (period, filters, times); zero data rows.
- Export while a sync is running: uses the last successful synced copy, same as reports; last-sync time in the file is that completed sync.
- User loses export permission but still holds report permission: they can open the saved report and cannot export (403).
- User deactivated after saving: next request is not signed in; saved reports remain stored for that user id but are unreachable until reactivated.
- Cloud provider enabled: generated table still comes from approved aggregates only; export and saved-report files never send themselves to the cloud.
- Print vs export: print remains the Phase 2 browser-print path (1,000-row cap); export is the `.xlsx` path (10,000-row cap). One does not replace the other.
- Vendor comparison of three vendors saved, then reopened: still the five measures per vendor; selecting six at save time remains refused with the Phase 2 limit.
- Attempt to save or export vendor financials in this phase: no financial table exists; reserved financial endpoints stay "data unavailable" or 403 per Phase 3/4.

## Requirements *(mandatory)*

### Functional Requirements

**AI dynamic reports (F14)**

- **FR-001**: When a permitted user asks a comparison or ranking purchasing question, the system MUST return a table built only from approved read-only purchasing metric results. Where the result has between 2 and 20 rows/categories, the system MUST also return a chart of those same results. The system MUST NOT build tables or charts from unapproved queries, raw Mabna, or metrics the user's role cannot see in reports.
- **FR-002**: The system MUST show, under each generated table and each generated chart, the period, filters and metrics used.
- **FR-003**: For the question "Compare the top 10 purchased products over the last six months" (and the Persian equivalent), the system MUST return a table and a chart of up to 10 products ranked by net purchases over the six Jalali months ending with the current Jalali month. Figures MUST match the most-purchased-products report (same definitions) for that period and ranking.
- **FR-004**: Generated tables and charts MUST keep purchases separate from payments and vendor balance, MUST NOT present a price difference as profit, MUST keep facts separate from interpretation, and MUST say "data unavailable" rather than guess when the metric cannot be computed.
- **FR-005**: When a user who holds AI-queries chooses to keep a generated table, the system MUST save it as a saved report (F15) with type generated and the period, filters and metrics that produced it.
- **FR-006**: Phase 4 assistant constraints remain: allowlisted tools, 100-row tool cap, timeouts, independent questions, one in-flight question per user, no persisted chat, permissions on every tool call, Purchasing users receive no vendor-financial figures. This phase adds table/chart output; it MUST NOT weaken those rules.

**Saved reports (F15)**

- **FR-007**: When a user saves a report, the system MUST store for that user: name, report type (standard purchasing report kind, vendor comparison, drill-down, or generated), period, filters and, for generated reports, the metrics/ranking. The system MUST NOT store result rows as the saved report.
- **FR-008**: When the owner opens a saved report, the system MUST re-run it on current synced data with the user's **current** permissions and MUST show last-sync time. A saved generated report MUST follow the same table-and-chart rules as a live generated answer (labeled table; chart only when the current result has between 2 and 20 rows or categories).
- **FR-009**: If the user no longer holds the permission required for that saved report, the system MUST refuse to open it, MUST say why in Persian (missing permission), MUST NOT return figures, and MUST write an access-denied audit entry. Generated saved reports MUST require AI-queries to reopen. Standard purchasing saved reports MUST require purchasing-reports permission.
- **FR-010**: Saved reports MUST be visible and openable only to the owning user. Another user's request MUST be refused with a Persian reason.
- **FR-011**: Names MUST be unique per user after trim and ی/ک normalization. A duplicate name MUST prompt for overwrite confirmation: confirm MUST replace the previous saved report's type and filters; cancel MUST leave it unchanged. Empty or whitespace names MUST be refused. A user MUST have at most 50 saved reports; a **new** name beyond the cap MUST be refused with a Persian reason. Replacing an existing name MUST still be allowed at the cap after confirm.
- **FR-012**: A user MUST be able to list and delete their own saved reports. Delete MUST be audited and MUST NOT affect other users.
- **FR-013**: Admin MAY save and reopen standard purchasing reports they can see. Admin MUST NOT save or reopen generated reports (no AI-queries).

**Excel export (F16)**

- **FR-014**: Where the user has the export permission, the system MUST export the current on-screen report table (live or reopened saved report) with its filters to an `.xlsx` file with Persian headers and RTL sheet direction. The downloaded file name MUST be the report's Persian name, the Jalali export date, and `.xlsx`; characters that cannot appear in a file name MUST be stripped, and an empty remainder MUST become «گزارش».
- **FR-015**: The exported file MUST include the period, filters, export time (Jalali) and last-sync time. Demo-mode files MUST be labeled as demonstration data. When a data-quality notice applies to the figures, the file MUST include that notice text.
- **FR-016**: Export MUST include the full filtered table up to 10,000 rows and MUST state truncation when the cap applies.
- **FR-017**: When a user exports, the system MUST write an append-only audit entry with user, time, action, report target, period and filters. Entries MUST NOT contain result rows, figures, secrets or keys.
- **FR-018**: If a user without the export permission requests an export, the system MUST return 403 with no file and MUST log the attempt as access denied. Callers without a session MUST receive 401.
- **FR-019**: Export MUST be offered only for tables the user is allowed to see in this phase (standard purchasing reports, comparison, profile/drill-down tables, generated purchasing tables). Charts NEED NOT be drawn inside the spreadsheet. Vendor-financial export MUST follow existing permission rules and MUST NOT add financial data (Phase 6).

**Audit (F10 events for this phase)**

- **FR-020**: The system MUST audit save, replace, open and delete of saved reports (user, time, action, name/type, period and filters when used) without result rows.
- **FR-021**: Existing Phase 3–4 audit behavior (filters, append-only, no secrets, 403 for non-Admin viewers, AI-question events) MUST remain. Export and saved-report actions MUST be filterable by the Admin with Persian labels.

**Shared rules**

- **FR-022**: Generated views, saved-report lists, reopen views and export MUST follow Persian-first conventions: RTL layout, Jalali dates and times, Persian digits, Persian labels, and the install's data-mode label.
- **FR-023**: Nothing in this phase MAY write to Mabna or read Mabna's `HOST_ID()`-filtered report views on live Mabna. Reports, generated tables and exports MUST read the synced copy only.
- **FR-024**: Real company data MUST NOT enter the repository. Demo and real data MUST NOT mix in a table, chart, saved report or export.
- **FR-025**: Keyboard use and WCAG 2.1 AA contrast apply to save, saved-reports, generated table/chart and export actions, consistent with the rest of the product.
- **FR-026**: Metric definitions remain the single Phase 1–2 set: dashboards, reports, generated tables/charts, saved-report re-runs and exports MUST use the same numbers for the same period and filters.

### Key Entities

- **Generated report**: a table and optional chart produced from a comparison or ranking question; built only from approved purchasing metrics; labeled with period, filters and metrics; may be saved.
- **Saved report**: a named, per-user store of report type, period, filters (and metrics/ranking for generated); re-run on current data; not a snapshot of rows.
- **Export file**: an `.xlsx` of the current filtered table (live or reopened) plus meta (period, filters, export time, last-sync time, data-mode label, optional data-quality notice); Persian headers; RTL sheet; download name is Persian report name + Jalali export date + `.xlsx`.
- **Export permission**: Phase 3 permission held by Management, Purchasing and Finance; enforced on every export request.
- **Audit entry**: existing Phase 3–4 entity, extended with export and saved-report actions; still never secrets or result rows.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The canonical question "Compare the top 10 purchased products over the last six months" returns both a table and a chart, each labeled with period, filters and metrics, in 100% of successful-sync tests as a user with AI-queries; 100% of the product net-purchase figures match the most-purchased-products report for the same six months (Phase 5 exit).
- **SC-002**: A generated report and a standard report saved by the same user both reopen after a subsequent successful sync; 100% of reopened figures match the live report/metrics on that new data, not the numbers at save time (Phase 5 exit).
- **SC-003**: After the owner's permission for a generated saved report is removed (role no longer holds AI-queries), 100% of open attempts are refused with a Persian reason and return zero result rows (Phase 5 exit).
- **SC-004**: Export of a filtered purchasing report produces an `.xlsx` in which 100% of sampled tests show Persian headers, RTL sheet direction, and meta containing period, filters, export time and last-sync time, and data rows match the filtered report within the cap (Phase 5 exit).
- **SC-005**: 100% of successful exports in a test session appear in the audit log with user, time and target; 100% of export requests from a role without export permission return 403 with no file and are logged as access denied (Phase 5 exit).
- **SC-006**: Cross-user opens of saved reports succeed 0 times; each is refused with a reason.
- **SC-007**: In a scan of audit entries and logs from save/open/export tests, zero result rows, secrets or provider keys appear.
- **SC-008**: Ranking questions that are not comparison/ranking still receive cited-text answers; 100% of "data unavailable" ranking cases produce no table or chart.
- **SC-009**: A typical export of a filtered purchasing table (hundreds of rows) completes in under 10 seconds; reopening a saved standard report meets the existing report p95 under 2 seconds for the data view.
- **SC-010**: Demo-mode generated views, saved reports and export files are labeled demonstration data in 100% of sampled opens; production-mode samples contain no synthetic/Demo 1 figures.

## Assumptions

- **Dependencies**: Phase 1 synced purchasing copy and metric definitions; Phase 2 reports, comparison, drill-down, print and ی/ک search; Phase 3 roles, export permission (now enforced), audit log; Phase 4 purchasing assistant, tools, 20-question cited-text set (unchanged; tables are additional for comparison/ranking); `deploy/mabna-sim/` for off-site proof.
- **Skip mode**: specify defaults remain above; Session 2026-11-18 records the five clarify-queue answers taken as recommended options (no owner interview). Owner may later change generated-reopen to require only purchasing-reports permission; until then AI-queries is required so the permission-removed exit criterion is testable with seeded roles before Phase 6 financials exist.
- **No scheduling or email** of reports in Release 1.
- **No shared or org-wide saved reports** in this phase.
- **Chart type**: bar for rankings; a time comparison of few series may use a simple multi-series bar or line; the spec requires a chart is present and labeled, not a particular drawing library.
- **Canonical six months**: inclusive of the current Jalali month as month six.
- **PR #3** amount display names on master are not changed here.

## Out of Scope

- Phases 1–4 and 6–7 work; their pull requests and specs are not reopened. Phase 4 cited-text answers, provider settings, benchmark and 20-question accuracy gate stay as specified in `specs/006-local-ai-purchasing`. Phase 2 report pages stay as specified in `specs/004-vendor-purchasing-reports`.
- Vendor financial data, vendor balance on the profile, and financial exports beyond existing permission rules (Phase 6: F17, F5 AC3). Purchasing users still cannot see financials anywhere.
- Scheduled reports, emailed reports, shared saved reports, and dashboard pinning beyond the per-user saved list.
- Sales, pharmacy, inventory and warehouse questions or reports (Releases 2–3).
- Persisted assistant chat history (still out, as Phase 4).
- Custom PDF pipeline (browser-print only, unchanged).
- Merging the phase pull request (operator merges after typing UAT pass).
- Platform Assistant, MCP tool catalog, AI anomaly/pattern engine, Metabase, Wren AI.
- Remote/VPN access, native mobile, PWA, English UI.
- Any write to Mabna, live per-request queries to Mabna, and reading `HOST_ID()`-filtered report views on live Mabna.
- Deferred items F19–F33 as listed in `zharfa-prd.phases.md`.
