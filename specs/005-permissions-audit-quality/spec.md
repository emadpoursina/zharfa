# Feature Specification: Permissions, Audit, and Data Quality (Release 1, Phase 3)

**Feature Branch**: `zharfa-prd-phase-3`

**Created**: 2026-11-17

**Status**: Draft

**Input**: User description: "phase: zharfa-prd.phases.md#3 — Run Release 1 Phase 3 (Permissions, audit, and data quality: F9–F11) of zharfa-prd.phases.md"

**Scope authority**: `zharfa-prd.md` v3.0, Phase 3 of `zharfa-prd.phases.md` ("Permissions, audit, and data quality"), covering F9 (roles and permissions), F10 (audit log) and F11 (data-quality page) in full. `AGENTS.md` rules bind every decision. Phase 1 (`specs/003-real-purchase-data`: sync, metric definitions, data-mode split, Overview, Data sync page) and Phase 2 (`specs/004-vendor-purchasing-reports`: vendor directory and profile, purchasing reports, vendor comparison, drill-down, printing) are existing behavior this phase protects, records and annotates.

**Phase goal**: Each user sees only what their role allows, important actions are recorded, and the Admin can see data problems before they mislead anyone.

## Clarifications

### Carried over from earlier phases (owner decisions, binding)

- Release 1 roles are Admin, Management, Purchasing and Finance. Vendor financials are visible to Management and Finance only. All authorization is enforced on the server; hiding something in the UI is not enforcement.
- Audit of exports and AI questions, and the Sales, Warehouse and Viewer roles, are out of scope for this phase (exports and AI audit events arrive with Phases 4–5; the other roles with Releases 2–3).
- Amounts are shown in Rial, labeled "ریال".
- Simulator example columns and objects use `Sim*` names; real Mabna data never enters the repository, and development uses `deploy/mabna-sim/`.
- The AI agent stays hidden in the UI and refused by the API in production-mode installs until Phase 4.
- Phase 1/2 source-item status rules apply unchanged (`unknown`, `candidate`, `confirmed`, `corrected`; production uses only `confirmed` or `corrected` items).

### Assumptions taken in place of questions (no-question run; for the clarify stage to confirm)

- One role per user. Each user holds exactly one of the four seeded roles; roles are fixed permission sets in Release 1 and the Admin assigns roles but does not edit the permission sets.
- Demo 1's `manager` role becomes Management and its `admin` role stays Admin when this phase is installed; existing accounts keep working.
- Admin holds the administrative permissions and the purchasing reports, but not the vendor-financial permission (PRD F9 AC2: only Management and Finance hold it).
- Because vendor financial data arrives only in Phase 6, the vendor-financial permission is enforced in this phase on a reserved vendor-financials API area that answers "data unavailable" to permitted users and 403 to everyone else, so the exit criterion can be tested now.
- "Report open" means loading a report, the Overview, a vendor profile, a vendor comparison, a drill-down or a print request with its period and filters; a request that differs from the same user's previous request for the same report only in paging or sorting creates no new audit entry, while any other load (including a reload with identical period and filters) does.

### Session 2026-11-17

Clarify ran in skip mode: the owner answered "Skip", so each question takes the recommended option.

- Q: Should the Admin role be able to open the purchasing reports and detailed transactions, or only the administration pages? → A: Admin sees all purchasing reports and detailed transactions, but not vendor financials (FR-002 unchanged; assumption confirmed).
- Q: When does opening a report create an audit entry: on every load with a new period or new filters, or once per visit to a report page? → A: One entry per load and per change of period or filters; paging and sorting add none (FR-013, FR-014; assumption confirmed).
- Q: What password rules and failed sign-in protection should Release 1 have? → A: At least 8 characters, plus a temporary 15-minute lockout after 5 failed sign-ins in a row. Lockouts are audited and an Admin can unlock early (FR-033).
- Q: Which flagged records should trigger the data-quality notice on a report? → A: Only records in the report's scope whose issue type FR-024 maps to that report (FR-027).
- Q: How should a fresh production install get its first Admin account? → A: The installer gives the first Admin's username and initial password through a server environment variable or Docker secret. The password must be changed at first sign-in and the secret is never logged (FR-034).

### Owner decisions 2026-11-18 (analyze remediation, "Fix artifacts first")

- Owner decision 2026-11-18: report-open audit follows Q2 A literally — one entry per load and per change of period or filters; a request whose only difference from the same user's previous request for the same report is paging or sorting (`page`, `pageSize`, `sort`, `dir`) writes nothing; any other load, including a reload with identical parameters, writes an entry. There is no time window that suppresses reopen entries (FR-013, FR-014).
- Owner decision 2026-11-18: the two inconsistent-amount sub-checks "line net amount vs gross − discount + VAT" and "document total vs sum of lines" are deferred until the on-site checklist verifies those Mabna columns; until then Data quality lists them under the issue type as «بررسی نشده» (not checked) with the reason (FR-020, FR-023, SC-006).
- Owner decision 2026-11-18: in Release 1, "inconsistent product identifiers" is a name-based heuristic (one normalized product name under several product keys) and is labelled as a heuristic on the Data quality page; the code-level rule (one product code mapped to several products, or one product with conflicting codes) is enabled once the product code columns are verified on-site (FR-020).
- Owner decision 2026-11-18: source rows the sync rejects (a date that cannot be converted, a missing date or amount, and the other Phase 1 reject reasons) are not counted in the seven issue types, because no synced record exists; Data quality shows them per table and reason in a separate "rejected at sync" box (FR-020, FR-023). Duplicate records are detected per document type, document number **and vendor**, so two vendors' invoices that happen to share a number are not flagged; whether Mabna numbers are unique per type or per vendor and type is confirmed on-site (FR-020).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Admin manages users and their roles (Priority: P1)

An Admin opens Settings → Users and roles, sees every user with their role and status, creates an account for a new colleague with one of the four roles (Admin, Management, Purchasing, Finance), changes a user's role, resets a password, or deactivates a user who left. Each person then sees only the sections their role allows, and a change takes effect on that user's very next request without them signing out.

**Why this priority**: Release 1 serves several departments; until roles exist, purchasing staff and finance staff cannot be given access safely. Every other story in this phase depends on knowing who holds which role.

**Independent Test**: As Admin, create one user per role, sign in as each and confirm the visible sections match the role's permission set; then change the Purchasing user's role to Finance while they are signed in and confirm their next request is answered with Finance's permissions.

**Acceptance Scenarios**:

1. **Given** a fresh install or an upgraded Demo 1 install, **When** the Admin opens Users and roles, **Then** exactly the roles Admin, Management, Purchasing and Finance are offered, each with its permission set described in Persian, and former Demo 1 `manager` users appear as Management and `admin` users as Admin.
2. **Given** the Users and roles page, **When** the Admin creates a user with a username, an initial password and a role, **Then** the user can sign in with that role and the password is never shown again or stored in readable form.
3. **Given** an existing user, **When** the Admin assigns a different role, **Then** the list shows the new role and the change is recorded in the audit log with the old and new role.
4. **Given** a user signed in with role Purchasing, **When** the Admin changes their role to Finance, **Then** the user's next request is authorized with Finance's permissions without signing out, and their navigation reflects the new role on the next page load.
5. **Given** a signed-in user, **When** the Admin deactivates them, **Then** their next request is refused as not signed in, and they cannot sign in again until reactivated.
6. **Given** only one active Admin, **When** an Admin tries to deactivate that Admin or change their role away from Admin, **Then** the change is refused with a message that at least one active Admin must remain.
7. **Given** a user with role Management, Purchasing or Finance, **When** they open Users and roles or call its APIs directly, **Then** the server answers 403.
8. **Given** an account, **When** 5 sign-ins in a row fail for it, **Then** the account is locked for 15 minutes (even a correct password is refused with a Persian "temporarily locked" message), the lockout is audited, and the Admin can unlock it early from Users and roles.
9. **Given** the Admin creates a user or resets a password, **When** the password is shorter than 8 characters, **Then** it is refused with a Persian message.
10. **Given** a fresh production install, **When** it starts with the first-Admin username and initial password supplied as a server environment variable or Docker secret, **Then** that Admin can sign in, must change the password before reaching any other page, and the supplied password appears in no log or audit entry.

---

### User Story 2 - The server refuses what a role does not allow (Priority: P1)

Whatever the browser or desktop shell shows, every API checks the caller's permissions. A Purchasing user who calls a vendor-financial or administrative API directly is refused with 403, and the attempt is recorded so the Admin can see it.

**Why this priority**: Server-side enforcement is a project rule (AGENTS.md) and the PRD's security requirement; without it, hiding a menu item protects nothing.

**Independent Test**: Signed in as Purchasing, call each administrative API (users and roles, data sync, data quality, audit log, AI settings) and the reserved vendor-financials API directly, bypassing the UI; confirm each returns 403 and each attempt appears in the audit log. Repeat the vendor-financials call as Management and Finance and confirm it is not refused.

**Acceptance Scenarios**:

1. **Given** a Purchasing user, **When** they call a vendor-financial API directly, **Then** the server answers 403, no financial figure is returned, and an "access denied" audit entry records the user, the time, the refused target and the missing permission.
2. **Given** a Purchasing user, **When** they call any administrative API directly (users and roles, data sync and "sync now", data quality, audit log, AI provider settings, reconciliation), **Then** the server answers 403 and logs the attempt the same way.
3. **Given** a Management or Finance user, **When** they call the vendor-financial API, **Then** it is not refused; until Phase 6 it answers "data unavailable" with no figures.
4. **Given** an Admin user, **When** they call the vendor-financial API, **Then** the server answers 403, because Admin does not hold the vendor-financial permission.
5. **Given** each role, **When** the permission matrix is tested by calling every Phase 1–3 API directly, **Then** every call the role's permission set does not allow returns 403 and every allowed call succeeds.
6. **Given** a user whose role lacks a section, **When** they navigate in the UI, **Then** that section is not shown in the navigation; if they open its address directly, the page shows a Persian "no access" message instead of data.
7. **Given** a caller who is not signed in, **When** they call any protected API, **Then** the server answers 401 and returns no data.

---

### User Story 3 - Admin reviews the audit log (Priority: P2)

The Admin opens Settings → Audit log and reviews who signed in, which reports were opened with which period and filters, what administrative settings and permissions were changed, and which requests were refused. The list can be filtered by user, action and Jalali date range. Entries never contain passwords, secrets or report result rows.

**Why this priority**: The audit log lets the company review data access after the fact (PRD F10) and is the measurement source for the adoption metric; it depends on User Stories 1–2 for the user and permission events.

**Independent Test**: Sign in as each role, open a few reports with filters, change a user's role as Admin, trigger a 403, then open the audit log and filter by each user, by each action and by today's date; confirm every action appears with user, time, action and target, and that no entry holds a password, secret or report row.

**Acceptance Scenarios**:

1. **Given** a user signs in successfully, **When** the Admin views the audit log, **Then** a sign-in entry shows the user and the Jalali date and time; a failed sign-in shows the attempted username and the failure, never the password.
2. **Given** a user opens the Overview, a purchasing report, a vendor profile, a vendor comparison, a drill-down or a print of a report, **When** the Admin views the audit log, **Then** one entry shows the user, time, the report name and its period and filters, and no figures or rows from the result; reloading it with the same period and filters adds another entry, while moving to another page or changing the sort order adds none.
3. **Given** an Admin changes an administrative setting (for example runs "sync now", creates, deactivates or reactivates a user, resets a password, or — from Phase 4, when an in-app setting exists — changes an AI provider setting), **When** the audit log is viewed, **Then** each change appears with user, time, action and target, and a password reset records only that it happened.
4. **Given** an Admin changes a user's role, **When** the audit log is viewed, **Then** the entry names the target user and the old and new role.
5. **Given** many entries, **When** the Admin filters by a user, by an action type and by a Jalali date range (alone or combined), **Then** only matching entries are listed, newest first, paged on the server, with the total count of matches.
6. **Given** any audit entry or application log line written in this phase, **When** it is inspected, **Then** it contains no password, password hash, session token, provider key, database credential or report result rows.
7. **Given** an existing audit entry, **When** anyone (including an Admin) tries to change or delete it, **Then** the change is refused; the audit log is append-only.
8. **Given** a non-Admin user, **When** they open the audit log or call its API, **Then** the server answers 403.

---

### User Story 4 - Admin sees data-quality issues after each sync (Priority: P2)

After every successful sync, the system checks the synced purchasing data for problems. The Admin opens Settings → Data quality and sees, for each issue type, how many records are affected, a few example records, and which reports those records feed, together with the status and time of the last sync.

**Why this priority**: Bad source data must never silently produce misleading reports (PRD F11); this is Journey 3's last step. It builds on Phase 1's sync and Phase 2's reports.

**Independent Test**: Seed the simulator with synthetic bad records of every issue type (`Sim*` objects), run "sync now", open Data quality and confirm each seeded issue type is listed with the right count, at least one example that matches a seeded record, and its affected reports.

**Acceptance Scenarios**:

1. **Given** a simulator seeded with bad records for each issue type (missing values, duplicate records, broken relationships, inconsistent product identifiers, invalid dates, unexpected quantities, inconsistent amounts), **When** a sync completes and the Admin opens Data quality, **Then** each issue type is listed, and its count increases by exactly the number of seeded bad records of that type compared with the count before seeding.
2. **Given** an issue type with records, **When** the Admin opens it, **Then** up to 10 example records are shown with document number, Jalali date, vendor and product where available and the field at fault, plus the list of reports the issue affects.
3. **Given** a sync has completed, **When** the Admin opens Data quality, **Then** every issue type with no issues shows "no issues found" («مشکلی یافت نشد.») and the page shows the time of the check.
4. **Given** the Data quality page, **When** it is opened, **Then** it shows the last sync's status and completion time and the time the issues were checked.
5. **Given** a sync run that fails, **When** the Admin opens Data quality, **Then** the issues from the last successful sync remain shown, and the page states that the latest sync failed; likewise, when the latest data-quality check itself fails, the last successful check's issues remain shown and the page states that the latest check failed.
6. **Given** bad records that are corrected in the source, **When** the next sync completes, **Then** those issues no longer appear and their counts drop accordingly.
7. **Given** a non-Admin user, **When** they open Data quality or call its API, **Then** the server answers 403.

---

### User Story 5 - Reports warn when their figures depend on flagged records (Priority: P2)

A Manager opens a purchasing report whose figures include records with open data-quality issues. The report shows a data-quality notice saying which kinds of issues affect it and how many records, so nobody acts on a misleading number unknowingly. Reports with no flagged records show no notice.

**Why this priority**: The notice is what turns the Admin's data-quality findings into protection for every report user; it depends on User Story 4.

**Independent Test**: After a sync with seeded bad records, open the Overview, a vendor profile, each purchasing report and a drill-down for a period and filter that include a flagged record and confirm the notice; then choose a period or vendor with no flagged records and confirm there is no notice.

**Acceptance Scenarios**:

1. **Given** a report whose period and filters include at least one record with an open issue of a type that FR-024 maps to that report, **When** it is shown, **Then** a Persian data-quality notice states those issue types and the number of affected records within this report's scope.
2. **Given** the same report with a period or filter that excludes every flagged record, **When** it is shown, **Then** no notice appears.
3. **Given** an Admin viewing a report with a notice, **When** they follow the notice, **Then** Data quality opens on the issue types named; non-Admin users see the notice without the link.
4. **Given** a report with a notice, **When** it is printed, **Then** the printout carries the notice.
5. **Given** a flagged record, **When** reports are computed, **Then** figures are unchanged by the flag (Phase 1/2 rules still apply, including "نامشخص" rows); the notice only warns.
6. **Given** a report whose scope includes flagged records only of issue types not mapped to that report, **When** it is shown, **Then** no notice appears.
7. **Given** a report that shows comparison or previous-period figures, **When** a flagged record of a mapped type lies only in the comparison period, **Then** the notice appears and counts it.
8. **Given** the notice check cannot be completed (for example it times out), **When** the report is shown, **Then** the figures are shown unchanged with a neutral Persian note that data quality could not be checked for this report, never an error and never a silent "no notice".

---

### Edge Cases

- A user's role changes while they have a report open: the open page keeps what it already loaded; the next request is authorized under the new role, and a now-forbidden section shows the "no access" message.
- A deactivated user still holds an open session: the next request is refused as not signed in and the session ends.
- The Admin tries to deactivate themselves or demote themselves while they are the last active Admin: refused with the reason.
- Two Admins change the same user at the same time: the last saved change wins, and both changes appear in the audit log.
- A username differs from an existing one only in Arabic/Persian ی/ک or letter case: treated as the same username and refused as a duplicate.
- Repeated 403s from the same user in a short time: every attempt is recorded; nothing is dropped.
- Failed sign-ins with a non-existent username: recorded with the attempted username and "unknown user", never the password.
- A locked account signs in with the correct password within the 15 minutes: still refused as locked; the lockout ends after 15 minutes or when an Admin unlocks it, and a successful sign-in resets the failure count.
- The last active Admin is locked out: an Admin unlock is impossible, so the lockout simply expires after 15 minutes; deactivating that Admin stays refused (FR-008).
- A production install starts without the first-Admin secret and has no Admin yet: the API reports the missing setting at startup without logging any value, and no default account with a known password is created.
- The first-Admin secret is still set after an Admin exists: it is ignored and never resets an existing password.
- Audit filters that match nothing show an empty state, not an error; a date range whose end is before its start is refused with a message.
- The audit log write fails: the user's action still completes and the failure goes to the application log without secrets (Demo 1 behavior); a refused request (403) is still refused even if its audit write fails.
- Free-text filters stored in an audit entry (for example a vendor search term) are kept as typed but trimmed to a bounded length; nothing from the result is stored.
- No successful sync yet: Data quality states that no check has run, and reports show no data-quality notice (they already show "data unavailable").
- A sync with zero changed rows still re-runs the checks over the synced data, so issue counts always reflect the latest synced state.
- One record has several issues (for example missing vendor and invalid date): it counts once under each issue type; a report notice counts it once.
- A record is flagged in a period outside the selected report period: that report shows no notice for it.
- Validation finds tens of thousands of issues: counts are exact; examples are limited to 10 per issue type; the page stays responsive.
- In a demo-mode install, Data quality and notices are labeled as demonstration data like every other page.
- The vendor-financials API is called by Management or Finance before Phase 6: "data unavailable", never a figure computed from purchases.

## Requirements *(mandatory)*

### Functional Requirements

**Roles and permissions (F9)**

- **FR-001**: The system MUST seed exactly four roles in Release 1: Admin, Management, Purchasing and Finance, each a fixed permission set. Each user MUST hold exactly one role.
- **FR-002**: The permission sets MUST be:

  | Permission | Admin | Management | Purchasing | Finance |
  |------------|:-----:|:----------:|:----------:|:-------:|
  | Overview | yes | yes | yes | yes |
  | Vendor directory and profile, purchasing reports, vendor comparison | yes | yes | yes | yes |
  | Detailed transactions (drill-down, transaction history, documents) | yes | yes | yes | yes |
  | Vendor financials | no | yes | no | yes |
  | Administration (users and roles, data sync, data quality, audit log, AI provider settings, reconciliation) | yes | no | no | no |
  | AI queries (enforced from Phase 4; refused in production until then) | no | yes | yes | yes |
  | Export (enforced from Phase 5) | no | yes | yes | yes |

- **FR-003**: Every API MUST check the caller's permission on the server for each section, report, financial data, detailed transactions, AI queries and exports, independent of what the frontend shows. No API MAY return data the caller's role does not allow.
- **FR-004**: A request from a signed-in user without the required permission MUST receive 403 with no data, and MUST be recorded in the audit log as an access-denied entry with user, time, refused target and missing permission. A request with no valid session MUST receive 401.
- **FR-005**: The system MUST provide a reserved vendor-financials API area guarded by the vendor-financial permission; until Phase 6 it MUST answer "data unavailable" to permitted callers and MUST NOT compute any figure.
- **FR-006**: An Admin MUST be able to list users with role and active status, create a user with a username, initial password and role, change a user's role, reset a user's password, and deactivate or reactivate a user. A password reset does not force a change at the user's next sign-in (accepted default).
- **FR-007**: A role change, deactivation or reactivation MUST take effect on the user's next request, without requiring the user to sign out; a deactivated user's next request MUST be treated as not signed in.
- **FR-008**: The system MUST refuse any change that would leave no active Admin.
- **FR-009**: Passwords MUST be stored only in hashed form, MUST never be shown after entry, and MUST never appear in audit entries, application logs or API responses.
- **FR-010**: Usernames MUST be unique after Persian normalization (ی/ک and ي/ك equal) and case folding.
- **FR-011**: Installing this phase MUST map Demo 1's `manager` users to Management and `admin` users to Admin without losing accounts or passwords; demo-mode installs MUST seed one demonstration user per role from the project's seed data.
- **FR-012**: The navigation and pages MUST show only sections the user's role allows; opening a forbidden page directly MUST show a Persian "no access" message (the server refusal remains the enforcement).

**Audit log (F10)**

- **FR-013**: The system MUST write an audit entry with user, time, action and target for: successful and failed sign-ins and sign-outs; account lockouts and Admin unlocks; the first Admin's required password change; each open of the Overview, a purchasing report, a vendor profile, a vendor comparison, a drill-down or a report print, including the report name, period and filters; each administrative change (sync now, user create, deactivate, reactivate, password reset, AI provider setting change, and other Settings changes — Release 1 has no in-app AI provider setting before Phase 4, so the AI-provider-setting event (`settings.ai_provider.changed`) is reserved now and starts being written in Phase 4); each role change, with old and new role; each access-denied (403) request; and each open of the audit log and Data quality pages.
- **FR-014**: A report request whose only difference from the same user's previous request for the same report is paging or sorting (`page`, `pageSize`, `sort`, `dir`) MUST NOT create an audit entry. Every other load MUST create a report-open entry, including a change of period or filters and a reload with identical period and filters. No time window suppresses entries.
- **FR-015**: Audit entries and application logs MUST NOT contain secrets, passwords or password hashes, session tokens, provider keys, database credentials, or report result rows or figures. Free-text filter values MAY be recorded, trimmed to a bounded length.
- **FR-016**: The audit log MUST be append-only: no user, including an Admin, can change or delete an entry.
- **FR-017**: The Admin MUST be able to list audit entries newest first, filtered by user, action type and Jalali date range (alone or combined), with server-side paging and the total count of matches; action types MUST be shown with Persian labels and times as Jalali date and time.
- **FR-018**: Audit logging failures MUST NOT block or change the outcome of the audited action, except that a refused request stays refused.
- **FR-019**: Existing Demo 1 audit events (provider status, reconciliation) and Phase 1 sync events MUST keep being recorded and MUST be listed and filterable with the new events.

**Data-quality page (F11)**

- **FR-020**: When a sync run completes successfully (scheduled or "sync now", full or incremental), the system MUST validate the synced purchasing data (purchase documents and lines, returns, vendors, products) for these issue types:
  - **Missing values**: a purchase document or line without a required value that the synced copy can hold empty (document number, vendor, product, quantity). A missing date or amount is rejected at sync and appears in the "rejected at sync" box (FR-023), not in this count.
  - **Duplicate records**: the same source document or line synced more than once (impossible in the synced copy by its unique keys), or two documents of the same type **from the same vendor** with the same document number (normalized). Including the vendor avoids flagging two vendors' invoices that happen to share a number; whether Mabna numbers are unique per type or per vendor and type is confirmed on-site.
  - **Broken relationships**: a line or document referencing a vendor, product or parent document that is not in the synced master data.
  - **Inconsistent product identifiers**: in Release 1 a **name-based heuristic** — one normalized product name under several product keys — labelled as a heuristic on the Data quality page. The code-level rule (one product code mapped to more than one product, or one product with conflicting codes) is enabled once the product code columns are verified on-site (onsite checklist).
  - **Invalid dates**: a document date that lies in the future relative to the sync, or before the earliest plausible date of the source. A date that cannot be converted to a Jalali date is rejected at sync (no synced record exists) and appears in the "rejected at sync" box (FR-023), not in this count.
  - **Unexpected quantities**: a zero or negative quantity on an invoice or return line.
  - **Inconsistent amounts**: a line whose discount exceeds its gross amount, a line with a negative VAT amount, or a document whose document discount exceeds the sum of its lines (gross − line discount) by more than 1 Rial. Negative gross amounts are rejected at sync ("rejected at sync" box). Two further sub-checks — a line whose net amount differs from gross − discount + VAT, and a document whose total differs from the sum of its lines, by more than 1 Rial — are **deferred** until the on-site checklist verifies those Mabna columns; until then Data quality lists them under this type as «بررسی نشده» (not checked) with the reason.
- **FR-021**: Validation MUST run against the synced reporting copy only and MUST NOT query Mabna beyond the sync itself.
- **FR-022**: After each successful sync, the result of the new validation MUST replace the previous one; after a failed sync, the last successful validation result MUST stay in place and the page MUST state that the latest sync failed. If the latest validation run itself fails, the last successful validation result MUST stay in place and the page MUST state that the latest check failed.
- **FR-023**: Data quality MUST show the last sync's status and completion time, the time of the last check, and for each issue type its exact count, up to 10 example records (document number, Jalali date, vendor and product where available, and the field at fault), and the reports it affects. Issue types with no records MUST show "no issues found". Deferred sub-checks MUST be listed under their issue type as «بررسی نشده» (not checked) with the reason, and heuristic checks MUST be labelled as heuristics. Source rows rejected at sync (unconvertible dates, missing dates or amounts, and the other Phase 1 reject reasons) MUST be shown in a separate "rejected at sync" box with counts per source table and reason code (no values); they are not part of the seven counts.
- **FR-024**: Each issue type MUST name the reports it affects from the Phase 1–2 set: Overview, vendor directory and profile, purchases by vendor, by product and by category, monthly trend, most-purchased products, purchase returns, transaction history, vendor comparison and drill-down.
- **FR-025**: Data quality MUST be available to the Admin role only, enforced on the server.
- **FR-026**: Validation MUST NOT change any figure: flagged records are counted exactly as Phase 1–2 rules already specify, including the "نامشخص" (unknown) rows.

**Data-quality notice on reports (F11 AC3)**

- **FR-027**: When the figures of the Overview, a vendor profile, a purchasing report, a vendor comparison or a drill-down, for the selected period and filters, include at least one record with an open issue whose type FR-024 maps to that report, the server MUST mark the response and the page MUST show a Persian data-quality notice naming those issue types and the number of affected records in that scope. Issues of types not mapped to the report MUST NOT trigger a notice on it, so notices never contradict the "affected reports" list on Data quality. When the response shows comparison or previous-period figures, the scope MUST include the comparison period as well. Otherwise no notice is shown. If the notice check cannot be completed (timeout or error), the report MUST still return its figures, and the response MUST say the check is unavailable so the page shows a neutral Persian note instead of silently showing no notice.
- **FR-028**: For Admin users the notice MUST link to Data quality; for other roles it MUST NOT link to administrative pages. Printed reports MUST carry the notice.

**Shared rules**

- **FR-029**: All new pages (Users and roles, Audit log, Data quality) and the notice MUST follow Persian-first conventions: RTL layout, Jalali dates and times, Persian digits, Persian labels, and the install's data label (real company data or demonstration data).
- **FR-030**: The simulator MUST be extended with synthetic bad records of every issue type in FR-020, using `Sim*` names, so that each exit criterion can be shown off-site; real Mabna data never enters the repository.
- **FR-031**: Nothing in this phase MAY write to Mabna or read Mabna's `HOST_ID()`-filtered report views on live Mabna.
- **FR-032**: New pages MUST meet the PRD performance target (p95 under 2 seconds on synced data), and be fully usable by keyboard with WCAG 2.1 AA contrast.

**Sign-in protection and first Admin (clarified 2026-11-17)**

- **FR-033**: Passwords set at user creation, password reset or required change MUST be at least 8 characters. After 5 failed sign-ins in a row for one account, the account MUST be locked for 15 minutes, during which every sign-in, even with the correct password, is refused with a Persian "temporarily locked" message. A successful sign-in resets the failure count. An Admin MUST be able to unlock an account early from Users and roles. The lockout and the unlock MUST be audited.
- **FR-034**: A production install MUST get its first Admin from a username and initial password supplied as a server environment variable or Docker secret, used only while no active Admin exists. That Admin MUST change the password at first sign-in before reaching any other page. The supplied initial password MUST be at least 12 characters (stricter than FR-033's 8, because it is set outside the application). The supplied value MUST never be logged, audited or returned, and the system MUST NOT create any default account with a known password. Demo-mode installs keep FR-011's seeded demonstration users.

### Key Entities

- **User**: a person who signs in; username (unique after Persian normalization), hashed password, exactly one role, active status, consecutive failed sign-ins and lockout end time, and a must-change-password flag (first Admin).
- **Role**: one of Admin, Management, Purchasing, Finance; a fixed set of permissions.
- **Permission**: the right to a section or kind of data (Overview, purchasing reports, detailed transactions, vendor financials, administration, AI queries, export); checked on the server for every request.
- **Audit entry**: append-only record of user, time, action type, target and safe details (report name, period, filters, old and new role, refused permission); never secrets or result rows.
- **Validation run**: the data-quality check after a successful sync; time of check, the sync run it belongs to, and its issues.
- **Data-quality issue**: one record flagged with one issue type, identifying the record (document, line, vendor or product) and the field at fault; grouped by issue type with counts, examples and affected reports.
- **Data-quality notice**: a server-provided marker on a report response stating the issue types and affected record count within the report's scope (including the comparison period when shown), or that the check is unavailable for this response.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The Admin can create a user and assign one of the four roles in under 1 minute; 100% of seeded roles (Admin, Management, Purchasing, Finance) are available to assign.
- **SC-002**: In a direct-API test of every Phase 1–3 endpoint for each of the four roles, 100% of calls the role is not allowed return 403 with no data and are recorded in the audit log, and 100% of allowed calls succeed; in particular a Purchasing user is refused on every vendor-financial and administrative endpoint.
- **SC-003**: After a role change or deactivation, 100% of the user's subsequent requests are authorized under the new role or refused, starting with the very next request.
- **SC-004**: In a test session covering sign-in, report opens, administrative changes and permission changes, 100% of those actions appear in the audit log with user, time, action and target, and filtering by user, action and date returns exactly the matching entries.
- **SC-005**: A scan of all audit entries and application logs from the test session finds zero passwords, password hashes, session tokens, keys, credentials or report result rows.
- **SC-006**: After a sync with seeded bad records on the simulator, Data quality lists all seven issue types; for each type the count increases by exactly the seeded number compared with the pre-seed baseline (100% match), with at least one correct example of a seeded record each and their affected reports; the two deferred inconsistent-amount sub-checks are listed as «بررسی نشده».
- **SC-007**: 100% of tested reports whose period and filters (and comparison period, when shown) include a record flagged with an issue type mapped to that report show the notice, and 100% of tested reports whose scope excludes such records (including scopes with only unmapped issue types) show none.
- **SC-008**: The Users and roles, Audit log (with one year of entries) and Data quality pages open within 2 seconds.
- **SC-009**: In a sign-in test, 100% of accounts with 5 failed sign-ins in a row are refused for 15 minutes (or until an Admin unlocks them), and every lockout and unlock appears in the audit log.
- **SC-010**: A fresh production install started with the first-Admin secret allows exactly one Admin sign-in that requires a password change first, creates no default account with a known password, and leaves the supplied password in zero log lines or audit entries.

## Assumptions

- **Roles and users**: one role per user; four fixed permission sets as in FR-002; the Admin manages users and roles but not permission sets. Demo 1 `manager` maps to Management and `admin` to Admin. Who holds which role at go-live is the owner's decision (PRD open question) and is made in the installed system, not in the code.
- **Admin and data**: the Admin holds administrative permissions and the purchasing reports so they can verify sync and data quality against reports, but not vendor financials (PRD F9 AC2).
- **AI and export permissions**: defined now so later phases enforce them; default holders are Management, Purchasing and Finance. AI stays refused in production until Phase 4; export does not exist until Phase 5. Owner may adjust in clarification.
- **Vendor-financials API**: a reserved, permission-guarded API area answering "data unavailable" until Phase 6, so the 403 exit criterion can be tested before financial data exists.
- **Authentication**: Demo 1's username/password sign-in with hashed storage and server sessions is kept; MFA is deferred with remote access. Password rules (clarified): at least 8 characters, with a 15-minute lockout after 5 failed sign-ins in a row (FR-033). Production installs get their first Admin from a server environment variable or Docker secret, at least 12 characters (FR-034). An Admin password reset does not force the user to change the password at next sign-in (accepted default, 2026-11-18; only the first Admin must change, FR-034).
- **Report open**: one audit entry per load of a report with its period and filters, including a reload with identical parameters; a request that differs from the same user's previous request for the same report only in paging or sorting adds none (FR-014).
- **Audit retention**: entries are kept for the life of the install in Release 1 (no automatic purge); they are included in the daily backup (Phase 7).
- **Unauthenticated calls**: answered 401 and not audit-logged individually, except failed sign-ins.
- **Data-quality thresholds**: amount tolerance 1 Rial for rounding; earliest plausible date is the start of the source's first fiscal year as verified on-site (simulator: its first synthetic year); issues are recomputed after every successful sync, with no manual acknowledge or dismiss in this phase.
- **Validation scope**: the purchasing data synced in Phases 1–2 (purchase documents and lines, returns, vendors, products, product categories). Vendor-financial and sales data are validated when they arrive.
- **Examples**: up to 10 per issue type, shown only to Admins on the Data quality page; they are not written to logs.
- **Currency and calendar**: Rial labeled "ریال"; Jalali dates and times; Persian digits.
- **Dependencies**: Phase 1 sync, Data sync page, sync run records and metric definitions (`specs/003-real-purchase-data`); Phase 2 reports, profile, comparison, drill-down and print (`specs/004-vendor-purchasing-reports`); Demo 1 authentication, append-only audit log and redaction of secrets; the `deploy/mabna-sim/` simulator with `Sim*` objects.

## Out of Scope

- Export and AI audit events (added by Phases 4–5 when those actions exist), the local AI provider and AI over real data (Phase 4: F12, F13).
- AI dynamic reports, saved reports and Excel export (Phase 5: F14, F15, F16).
- Vendor financial data and the vendor balance on the profile (Phase 6: F17, F5 AC3); this phase only guards the reserved API area.
- LAN production deployment, backups and go-live (Phase 7: F18).
- Sales, Warehouse and General Viewer roles (Releases 2–3: F24, F30); custom roles or editing permission sets; multiple roles per user; per-vendor or per-row data restrictions.
- MFA, single sign-on, self-service password reset and remote access (F32).
- Manual acknowledge, dismiss or correction of data-quality issues; fixing data in Mabna; validation of sales, receivables, inventory or vendor-financial data.
- Deferred items F19–F33: sales, pharmacy, sales financials, cross-domain reports, warehouse and inventory, AI pattern detection, mobile/PWA, English UI, Platform Assistant.
- Any write to Mabna, live per-request queries to Mabna, and reading `HOST_ID()`-filtered report views on live Mabna.
