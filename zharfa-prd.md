# Product Requirements Document: Zharfa

**Version:** 3.0
**Date:** 2026-11-16
**Status:** Governing — confirmed in discovery 2026-11-16; Release 1 detailed, Releases 2–3 outlined
**Author:** PRD Writer Agent, based on discovery with Emad Poursina
**Supersedes:** [`docs/prd-archive/PRD-v1.1.md`](docs/prd-archive/PRD-v1.1.md) (vision reference) and [`docs/prd-archive/zharfa-demo1-prd.md`](docs/prd-archive/zharfa-demo1-prd.md) v2.0 (Demo 1 history)
**Product direction from:** [`docs/prd-archive/prd-direction-three-releases.md`](docs/prd-archive/prd-direction-three-releases.md) ("Approved Project Direction"), reconciled with Demo 1 and spec 002 findings
**Phase plan:** [`zharfa-prd.phases.md`](./zharfa-prd.phases.md) (Release 1)

---

## 1. Executive Summary

Zharfa is a self-hosted, Persian (RTL) business-intelligence and AI reporting application for a pharmaceutical distribution company. It reads the company's operational system, سیستم جامع شرکت مبنا (Mabna), through a read-only connection, copies the needed data into its own reporting database, and presents it as dashboards, reports, and answers from one central AI agent. It never writes to Mabna or replaces it. The product ships as three independently usable releases on one shared foundation: **Release 1 — Vendor & Purchasing Analytics**, Release 2 — Pharmacy Sales Analytics, and Release 3 — Warehouse & Inventory Analytics. Demo 1 (accepted by the company) proved the stack on synthetic data; Release 1 is the first release built on real company data.

## 2. Business Model

| Element | Detail |
|---------|--------|
| **Who** | Management, purchasing staff, and finance staff of one pharmaceutical distribution company, working on Windows PCs in the company LAN; one administrator who runs the platform |
| **Problem** | Purchasing, vendor, sales, and inventory information sits in Mabna and is hard to combine and analyze. Answers need manually prepared reports, and cross-department questions (what we buy vs what we sell vs what we hold) cannot be answered quickly |
| **Solution** | A read-only analytics layer around Mabna: nightly-synced reporting database, reconciled metric definitions, Persian dashboards and reports, export, and a central AI agent that answers questions only from approved metrics and cites its period, filters, and source |
| **Name** | Zharfa (PRDs before v3.0 used the working name "Pharmacy Analytics") |
| **Business model** | Internal tool for one company; self-hosted, no licensing or monetization. Cost drivers: on-prem server, optional cloud AI usage |

## 3. User Personas

### Primary Persona: Manager (role `Management`)

- **Who they are**: Company owner or senior manager; reads numbers, not SQL. Saw and accepted Demo 1.
- **Goals**: See total purchasing, top vendors and products, trends, and vendor balances at a glance; ask follow-up questions in Persian.
- **Pain points**: Waits for staff to prepare reports from Mabna; cannot compare vendors or periods quickly.
- **Context**: Office Windows PC (desktop shell or browser), a few times a week, often before vendor meetings.

### Secondary Persona: Purchasing Officer (role `Purchasing`)

- **Who they are**: Staff member who places and tracks orders with vendors.
- **Goals**: Purchase history per vendor and product, returns, price and volume trends, vendor comparison, Excel export.
- **Pain points**: Mabna's printed reports are fixed and hard to filter or combine.
- **Context**: Daily, on an office PC; does not see vendor financials.

### Secondary Persona: Finance Officer (role `Finance`)

- **Who they are**: Accounting staff responsible for vendor payments.
- **Goals**: Vendor balances, outstanding payments, payment history, purchases vs payments.
- **Pain points**: Balances are spread across accounting documents and cheques in Mabna.
- **Context**: Weekly and at month end; confirms Zharfa's formulas against Mabna before sign-off.

### Supporting Persona: Administrator (role `Admin`)

- **Who they are**: IT person or the developer operating the on-prem install.
- **Goals**: Run and monitor the Mabna sync, see data-quality issues, manage users and permissions, configure AI providers, back up and restore.
- **Pain points**: Must guarantee nothing writes to Mabna and that secrets never leak.
- **Context**: Occasional; server console and the Settings section.

## 4. User Journeys

### Journey 1: Manager reviews vendor purchasing (Release 1 core path)

1. Manager opens the Zharfa desktop app on an office PC and signs in.
2. Overview shows this month's net purchases, the trend, top vendors and products, and the last-sync time.
3. Manager opens Vendors, picks a vendor, and sees its profile, purchase history, trend, and returns for the selected Jalali period.
4. Manager compares that vendor with two others over the last six months.
5. Manager asks the AI agent in Persian: "Which vendor's purchases grew most this quarter?" The answer shows a table, cites period, filters, and source, and matches the report figures.
6. Manager saves the comparison as a report and exports it to Excel.

### Journey 2: Finance checks vendor balances

1. Finance officer opens Vendors → Financials (hidden for roles without the financial permission, and refused by the API for them).
2. Sees balances and outstanding payments per vendor, then one vendor's payment history and purchases vs payments.
3. Exports the list to Excel; the export is audit-logged.

### Journey 3: Administrator runs and checks the sync

1. Admin opens Settings → Data sync, sees the last nightly run, its row counts and status.
2. Clicks "Sync now"; the run appears with progress and finishes.
3. Opens Data quality, sees validation issues (for example missing vendor on a purchase line) with counts and examples, and which reports they affect.

### Journey 4: Admin configures the AI provider

1. Admin opens Settings → AI, sees the local model (default) and its connection test.
2. Optionally enables the cloud provider after recording owner approval; the screen states that only aggregated results are sent.

## 5. Feature Set

### MVP (Must-Have) — Release 1: Vendor & Purchasing Analytics

| # | Feature | Description | Priority |
|---|---------|-------------|----------|
| F1 | Mabna purchase-data sync | Read-only full load, nightly incremental sync, and Admin "sync now" of vendors, products, purchase documents and returns into PostgreSQL | P0 |
| F2 | Purchase metric definitions and reconciliation | Documented purchase formulas (net, gross, returns, VAT) with an API-vs-Mabna reconciliation gate | P0 |
| F3 | Demo-data mode | Production installs show real data only; synthetic Demo 1 data and pages exist only in demo installs | P0 |
| F4 | Overview dashboard (purchasing) | Company overview with Release 1 purchasing KPIs and last-sync time | P0 |
| F5 | Vendor directory and profile | List and search vendors; per-vendor profile with purchase summary and history | P0 |
| F6 | Purchasing reports | Purchases by vendor, product, and category; trends; most purchased products; returns; transaction history | P0 |
| F7 | Vendor comparison | Compare two to five vendors over a period | P0 |
| F8 | Report capabilities | Jalali date range, vendor/product/category filters, search, sort, pagination, drill-down to transactions, period comparison, browser-print | P0 |
| F9 | Roles and permissions | Server-side permission sets; seeded roles Admin, Management, Purchasing, Finance | P0 |
| F10 | Audit log | Record sign-ins, report access, exports, AI queries, admin and permission changes | P0 |
| F11 | Data-quality page | Sync status and validation issues visible to Admin | P0 |
| F12 | AI provider configuration | Local model provider (default) with model benchmark; cloud provider opt-in, aggregates only | P0 |
| F13 | Central AI agent — purchasing | Persian Q&A over approved purchasing metrics with citations and permission enforcement | P0 |
| F14 | AI dynamic reports | AI answers can return a table or chart built from approved metrics | P0 |
| F15 | Saved reports | Save a report's filters and reopen it | P0 |
| F16 | Excel export | Export report tables to .xlsx, permission-checked and audited | P0 |
| F17 | Vendor financials | Vendor balances, outstanding payments, payment history, purchases vs payments, reconciled with Mabna | P0 |
| F18 | LAN production deployment | Docker Compose install on the company LAN server with HTTPS, backup/restore, and a go-live checklist | P0 |

### V1.1 (Should-Have) — Release 2: Pharmacy Sales Analytics

| # | Feature | Description | Priority |
|---|---------|-------------|----------|
| F19 | Sales analytics | Total sales, trends, by product, pharmacy, sales representative, category; most and least sold; returns; transaction history | P1 |
| F20 | Pharmacy analysis | Pharmacy directory and profiles, purchasing history, segmentation, top and declining customers | P1 |
| F21 | Sales financials | Receivables balance, aging, cash received, sales vs collections (spec 002's three-formula work) | P1 |
| F22 | Vendor–sales cross-domain reports | Purchasing vs sales volume, purchase vs selling price, product-level comparison | P1 |
| F23 | AI agent — sales and cross-domain | Extend F13/F14 to sales and vendor–sales questions | P1 |
| F24 | Sales role | Seeded `Sales` role and permissions | P1 |

### Future (Nice-to-Have) — Release 3 and later

| # | Feature | Description | Priority |
|---|---------|-------------|----------|
| F25 | Inventory analysis | Stock levels by product and warehouse, valuation, stock history | P2 |
| F26 | Warehouse operations | Incoming, outgoing, transfers, adjustments, movement history | P2 |
| F27 | Inventory risk | Expired and expiring products, low stock, overstock, slow-moving, dead stock, turnover | P2 |
| F28 | Full cross-domain reports | Purchasing vs sales vs inventory, inventory investment, turnover vs purchasing frequency | P2 |
| F29 | AI agent — warehouse and full cross-domain | Extend the agent to all three domains | P2 |
| F30 | Warehouse and Viewer roles | Seeded `Warehouse` and `General Viewer` roles | P2 |
| F31 | AI pattern detection | Agent flags unusual patterns, with facts kept separate from interpretation | P2 |
| F32 | Remote access | VPN or internet access with MFA and hardening | P2 |
| F33 | Mobile and PWA, English UI, Platform Assistant | Carried from PRD v1.1 as long-term vision | P2 |

## 6. Page / Screen List

```
/
├── Sign in
├── Overview                       (F4; R2/R3 add sales and inventory KPIs)
├── Vendors                        (Release 1)
│   ├── Vendor directory           (F5)
│   ├── Vendor profile             (F5, F6)
│   ├── Purchasing reports         (F6: by vendor / product / category, trends, top products, returns, transactions)
│   ├── Vendor comparison          (F7)
│   └── Financials                 (F17; Management + Finance only)
├── Sales                          (Release 2 — not shown in production until built)
├── Warehouse                      (Release 3 — not shown in production until built)
├── AI Assistant                   (F13, F14)
├── Reports
│   └── Saved reports              (F15)
├── Settings                       (Admin)
│   ├── Users and roles            (F9)
│   ├── Data sync                  (F1)
│   ├── Data quality               (F11)
│   ├── AI providers               (F12)
│   └── Audit log                  (F10)
└── Demo pages (demo installs only: Demo 1 sales, inventory alerts, receivables)   (F3)
```

## 7. Functional Requirements

Release 1 requirements (F1–F18) are binding for the phase plan. Release 2–3 entries are outlines; each gets EARS criteria in a later PRD revision once its Mabna domain is verified (§16 of the three-release direction: reports are never built on assumed structures).

### F1: Mabna purchase-data sync

**User Story:** As an Admin, I want Zharfa to copy the purchasing data it needs from Mabna on a schedule, so that reports are current without loading Mabna during work hours.

#### Acceptance Criteria

1. THE System SHALL read Mabna only through a read-only login and the connector's table allowlist, and SHALL refuse any statement other than SELECT before it reaches the server.
2. WHEN the nightly schedule fires (default 02:00 local time), THE System SHALL run an incremental sync of the allowlisted purchasing tables into PostgreSQL and record a SyncJob with start time, end time, status, and row counts per table.
3. WHEN an Admin clicks "Sync now", THE System SHALL start a sync run unless one is already running, in which case it SHALL show the running job instead.
4. IF a sync run fails, THEN THE System SHALL keep the last successful data, mark the run failed with its error in SyncLog, and show the failure on the Data sync page.
5. WHILE data is shown on any page, THE System SHALL display the completion time of the last successful sync.

### F2: Purchase metric definitions and reconciliation

**User Story:** As a Manager, I want purchase figures that match Mabna, so that I can trust Zharfa instead of re-checking it.

#### Acceptance Criteria

1. THE System SHALL compute net purchases as purchase invoices minus purchase returns, excluding VAT, dated by document date, and SHALL show gross purchases, returns, and VAT as separate figures.
2. THE System SHALL use one documented definition per metric for dashboards, reports, exports, and AI answers.
3. WHEN the reconciliation check runs for a period and filter, THE System SHALL compare the API totals with the same query run directly against the source data and SHALL log the result.
4. IF the reconciliation totals differ, THEN THE System SHALL record the mismatch and the release SHALL NOT be signed off until it is resolved.
5. THE System SHALL convert Jalali dates and normalize Arabic ي/ك to Persian ی/ک at ingestion.

### F3: Demo-data mode

**User Story:** As an Admin, I want real and synthetic data kept apart, so that no one mistakes demo numbers for company figures.

#### Acceptance Criteria

1. WHERE the install runs in production mode, THE System SHALL show only real-data modules and SHALL NOT load the synthetic dataset.
2. WHERE the install runs in demo mode, THE System SHALL label every page, report, export, and AI answer as demonstration data.
3. THE AI agent SHALL answer from only one dataset per install and SHALL NOT combine real and synthetic figures.

### F4: Overview dashboard (purchasing)

**User Story:** As a Manager, I want one screen with the main purchasing numbers, so that I understand the current position in seconds.

#### Acceptance Criteria

1. WHEN a user with Overview permission opens Overview, THE System SHALL show net purchases for the selected period, the previous-period comparison, a monthly trend, the number of active vendors, the top 5 vendors, and the top 5 products by net purchases.
2. WHEN the user changes the Jalali period, THE System SHALL update every Overview figure to that period.
3. IF a figure cannot be computed from the synced data, THEN THE System SHALL show "data unavailable" for that figure instead of a number.

### F5: Vendor directory and profile

**User Story:** As a Purchasing Officer, I want to find a vendor and see its purchasing summary, so that I can prepare for vendor discussions.

#### Acceptance Criteria

1. WHEN a user searches the vendor directory by name or code in Persian, THE System SHALL return matching vendors, matching ی/ک variants as equal.
2. WHEN a user opens a vendor profile, THE System SHALL show the vendor's net purchases for the selected period, its trend, its top products, its returns, and its purchase documents.
3. WHERE the user has the vendor-financial permission, THE System SHALL also show the vendor's balance on the profile.

### F6: Purchasing reports

**User Story:** As a Purchasing Officer, I want standard purchasing reports, so that I can analyze what we buy, from whom, and how it changes.

#### Acceptance Criteria

1. THE System SHALL provide reports of purchases by vendor, by product, and by product category, each with amount and quantity for the selected period.
2. THE System SHALL provide a purchase trend report by Jalali month, a most-purchased-products report, a purchase-returns report, and a purchase transaction history.
3. WHEN a user opens a row in a purchasing report, THE System SHALL show the purchase documents and lines behind that row.

### F7: Vendor comparison

**User Story:** As a Manager, I want to compare vendors side by side, so that I can see which relationships need attention.

#### Acceptance Criteria

1. WHEN a user selects two to five vendors and a period, THE System SHALL show for each vendor net purchases, quantity, number of documents, returns, and monthly trend in one view.
2. IF a user selects more than five vendors, THEN THE System SHALL refuse the selection and state the limit.

### F8: Report capabilities

**User Story:** As any report user, I want consistent filtering and navigation, so that every report works the same way.

#### Acceptance Criteria

1. THE System SHALL let users filter every Release 1 report by Jalali date range (month presets and custom), vendor, product, and product category, as applicable to the report.
2. THE System SHALL support search, column sorting, and server-side pagination on every report table.
3. WHEN a user selects period comparison, THE System SHALL show the selected period next to the previous period of equal length.
4. WHEN a user prints a report from the browser or desktop shell, THE System SHALL produce a readable Persian RTL page that states the period and filters.

### F9: Roles and permissions

**User Story:** As an Admin, I want permission sets enforced by the server, so that each person sees only what their job needs.

#### Acceptance Criteria

1. THE System SHALL enforce permissions server-side for each section, report, financial data, detailed transactions, AI queries, and exports.
2. THE System SHALL seed the roles Admin, Management, Purchasing, and Finance, where only Management and Finance hold the vendor-financial permission and only Admin holds administrative permissions.
3. IF a request calls an API without the required permission, THEN THE System SHALL return 403 and log the attempt, regardless of what the frontend displays.
4. WHEN an Admin changes a user's role, THE System SHALL apply the change on the user's next request.

### F10: Audit log

**User Story:** As an Admin, I want a record of important actions, so that data access can be reviewed.

#### Acceptance Criteria

1. WHEN a user signs in, opens a report, exports data, asks the AI agent a question, or changes administrative settings or permissions, THE System SHALL write an audit entry with user, time, action, and target.
2. THE System SHALL NOT write secrets, passwords, or full result rows into audit entries or application logs.
3. WHEN an Admin opens the audit log, THE System SHALL list entries filterable by user, action, and date.

### F11: Data-quality page

**User Story:** As an Admin, I want to see data problems from the sync, so that bad source data never silently produces misleading reports.

#### Acceptance Criteria

1. WHEN a sync run completes, THE System SHALL validate the synced data for missing values, duplicate records, broken relationships, inconsistent product identifiers, invalid dates, unexpected quantities, and inconsistent amounts.
2. WHEN an Admin opens Data quality, THE System SHALL list each issue type with its count, example records, and the reports it affects.
3. IF a report's figures depend on records with open validation issues, THEN THE System SHALL show a data-quality notice on that report.

### F12: AI provider configuration

**User Story:** As an Admin, I want to run the AI on a local model by default, so that real company data stays on our server.

#### Acceptance Criteria

1. THE System SHALL implement a local model provider behind the existing provider interface and SHALL use it as the default provider.
2. THE System SHALL include a benchmark procedure that runs a fixed Persian purchasing question set against two to three candidate local models on the target server and records accuracy and response time.
3. WHERE an Admin enables the cloud provider, THE System SHALL require an owner-approval note and SHALL send only aggregated tool results, never raw rows or personal data.
4. WHEN an Admin runs a connection test, THE System SHALL report success or the failure reason without exposing keys.
5. THE System SHALL keep provider keys in server environment or Docker secrets only and SHALL NOT return them to any client.

### F13: Central AI agent — purchasing

**User Story:** As a Manager, I want to ask purchasing questions in Persian, so that I get answers without building reports.

#### Acceptance Criteria

1. WHEN a user asks a question, THE System SHALL answer only through approved read-only purchasing tools with an allowlist, row limits, and timeouts.
2. THE System SHALL state the period, filters, and source metric in every answer, and SHALL separate figures from interpretation.
3. IF the data needed is unavailable or incomplete, THEN THE System SHALL say "data unavailable" and SHALL NOT estimate.
4. THE System SHALL apply the asking user's permissions to every tool call, so that a question cannot return data the user's role cannot see in reports.
5. WHEN the AI provider is unavailable or times out, THE System SHALL tell the user and leave dashboards and reports unaffected.

### F14: AI dynamic reports

**User Story:** As a Manager, I want the agent to build a table or chart for my question, so that I can see answers that no predefined report covers.

#### Acceptance Criteria

1. WHEN a question asks for a comparison or ranking, THE System SHALL return a table, and where it fits, a chart, built only from approved metric results.
2. THE System SHALL show, under each generated table or chart, the period, filters, and metrics used.
3. WHEN a user chooses to keep a generated report, THE System SHALL save it as a saved report (F15).

### F15: Saved reports

**User Story:** As a report user, I want to save a report with its filters, so that I can reopen it later.

#### Acceptance Criteria

1. WHEN a user saves a report, THE System SHALL store its report type, filters, and name for that user.
2. WHEN a user opens a saved report, THE System SHALL re-run it on current data with the user's current permissions.
3. IF the user no longer has permission for the saved report, THEN THE System SHALL refuse to open it and say why.

### F16: Excel export

**User Story:** As a Purchasing or Finance officer, I want to export report tables to Excel, so that I can share and work with them.

#### Acceptance Criteria

1. WHERE the user has the export permission, THE System SHALL export the current report table with its filters to an .xlsx file with Persian headers and RTL sheet direction.
2. THE System SHALL include the period, filters, export time, and last-sync time in the exported file.
3. WHEN a user exports, THE System SHALL write an audit entry.
4. IF a user without the export permission requests an export, THEN THE System SHALL return 403.

### F17: Vendor financials

**User Story:** As a Finance Officer, I want vendor balances and payments from Mabna, so that I can track what we owe.

#### Acceptance Criteria

1. WHEN the vendor financial source tables are verified, THE System SHALL show each vendor's balance, outstanding payments, payment history, and purchases vs payments for the selected period.
2. THE System SHALL keep purchases, payments, and balance as separately documented formulas and SHALL NOT present purchases as payments.
3. WHEN the reconciliation check runs, THE System SHALL compare vendor balance totals with the same query against the source data, and a mismatch SHALL block sign-off.
4. IF a financial figure's source data is missing or unverified, THEN THE System SHALL show "data unavailable" for it.
5. THE System SHALL show vendor financials only to roles with the vendor-financial permission.

### F18: LAN production deployment

**User Story:** As an Admin, I want a documented install on our server, so that the company can use Zharfa daily and recover from failure.

#### Acceptance Criteria

1. WHEN the README install steps are followed on the company LAN server, THE System SHALL start the database, API, web frontend, and reverse proxy with Docker Compose and serve over HTTPS on the LAN.
2. THE System SHALL NOT be reachable from outside the company LAN in Release 1.
3. THE System SHALL back up the Zharfa PostgreSQL database daily, and the restore procedure SHALL be tested once before go-live.
4. WHEN the go-live checklist is complete, THE System SHALL have passed the F2 and F17 reconciliation checks on real data for three sample months.

### F19–F24: Release 2 — Pharmacy Sales Analytics (outline)

**User Story:** As a Manager, I want sales, pharmacy, and sales-financial analytics on real data, and comparisons with purchasing, so that I can see how buying decisions relate to sales.

Outline: sales and pharmacy reports per the three-release direction §6; receivables, aging, cash received and sales vs collections built on spec 002's verified sales and cheque findings, with sales revenue, cash received, and receivables balance kept as three separate formulas; vendor–sales cross-domain reports using shared product identity and periods; a simple price difference is never presented as profit. EARS criteria are written when Release 2 is phased.

### F25–F31: Release 3 — Warehouse & Inventory Analytics (outline)

**User Story:** As a Manager, I want inventory, expiry, and stock-movement analytics combined with purchasing and sales, so that I can manage inventory risk and investment.

Outline: per the three-release direction §7; batch and expiry data must be verified in Mabna first; inventory valuation needs an approved costing method. EARS criteria are written when Release 3 is phased.

### F32–F33: Future (outline)

Remote access, mobile/PWA, English UI, and Platform Assistant stay as long-term vision from PRD v1.1 and need their own security design.

## 8. Non-Functional Requirements

| Category | Requirement | Target |
|----------|-------------|--------|
| Data safety | Writes to Mabna | None: read-only login is the primary control; SELECT-only guard and allowlist are secondary |
| Data safety | Mabna load | Scheduled sync runs off-hours (default 02:00); each Mabna query has a timeout (default 60 s) |
| Data freshness | Sync | Nightly incremental + manual; last-sync time on every page |
| Performance | Dashboard and report API on synced data | p95 < 2 s for 3 years of data |
| Performance | First screen load on LAN | < 3 s |
| Performance | AI answer (local model) | Target < 30 s; confirmed by the F12 benchmark |
| Availability | Service hours | Business hours on the LAN; no formal SLA in Release 1 |
| Recovery | Backup | Daily PostgreSQL backup; RPO 24 h; restore tested before go-live |
| Security | Authentication | Username and password with hashed storage, server sessions; MFA deferred with remote access |
| Security | Transport | HTTPS on the LAN |
| Security | Authorization | Server-side permission checks on every API; direct-API negative tests per role |
| Security | Secrets | Server env or Docker secrets only; never in frontend bundle, repo, or logs |
| Privacy | Cloud AI | Off by default; aggregates only when enabled with owner approval |
| Integrity | Reconciliation | API totals equal direct-source totals for every reconciled metric; mismatch blocks sign-off |
| Scalability | Concurrent users (Release 1) | 20 |
| Localization | Language and calendar | Persian RTL, Jalali dates, Persian digits, Rial/Toman formatting, ی/ک normalization |
| Accessibility | Level | WCAG 2.1 AA contrast and full keyboard navigation for dashboards and reports |
| Labeling | Data source | Real company data labeled as such; demo installs labeled as demonstration data |

## 9. Tech Stack Recommendations

| Layer | Recommended | Rationale |
|-------|-------------|-----------|
| Backend | ASP.NET Core Web API (.NET 10) | Locked since Demo 1; all authorization and KPI calculation already server-side |
| Auth | ASP.NET Core Identity-style local accounts with role/permission tables | Self-hosted LAN, no external identity provider; extends Demo 1's Manager/Admin RBAC |
| Frontend | React + TypeScript + Vite + Tailwind, Vazirmatn font | Locked since Demo 1; shared by browser and desktop shell |
| Desktop | WebView2 + WPF thin shell | Locked since Demo 1; office PCs run Windows |
| Database | PostgreSQL (reporting copy + app data) | Locked; reserved SourceRecord/SyncJob/SyncLog tables already exist for the sync |
| Source connector | Microsoft.Data.SqlClient read-only connector (spec 002 code) | Already built with allowlist, SELECT-only guard, and write checks; tested against the simulator |
| Sync scheduler | .NET hosted background service | No new dependency for a nightly job at this data volume (largest table ~416k rows) |
| Dev source | `deploy/mabna-sim/` SQL Server simulator with synthetic data | Real data never leaves the site; schema captured as metadata only |
| AI (default) | Local model via Ollama behind the existing `IAiProvider` | Real data stays on-prem; the seam exists (`OllamaAiProvider` is a stub today) |
| AI (optional) | Existing cloud provider, aggregates only | Kept from Demo 1; opt-in with owner approval |
| Excel export | Server-side .xlsx library (for example ClosedXML, MIT) | Exports must be permission-checked and audited server-side; final choice justified in the phase's plan |
| Charts | Existing frontend chart components; add a library only if the plan justifies it | Avoid new dependencies without need |
| Hosting | Docker Compose on the company LAN server, nginx reverse proxy with TLS | Self-hosted, next to Mabna, no cloud dependency |
| CI/CD | `.ainative/project.yaml` validation commands run by the tester state | Existing project workflow |
| Monitoring | Sync status page, structured application logs, audit log | Enough for a 20-user LAN install; full monitoring later |

### Liberties and Alternatives

- Assumes the LAN server can run Docker and reach Mabna's SQL Server. If it cannot run Linux containers, the API and database can run as Windows services with the same configuration.
- Assumes a Persian-capable local model performs acceptably on the server. If the F12 benchmark fails, the fallback is the cloud provider with aggregates only (owner approval required) while hardware is sourced.
- Direct live queries to Mabna for selected reports remain possible later through the same connector if nightly freshness proves too slow.

## 10. Design Direction

- **Visual tone**: Clean, professional, data-dense but calm; consistent with Demo 1's dashboard.
- **Key interaction patterns**: Summary cards above tables, filter bar per report, click-through drill-down to documents, side-by-side vendor comparison, AI chat with inline tables and charts and a "save as report" action.
- **Accessibility targets**: WCAG 2.1 AA contrast, keyboard navigation, readable Persian typography at default zoom, print-friendly layouts.
- **Responsive strategy**: Desktop-first for office PCs; layouts adapt to smaller windows; no mobile packaging.
- **Design system**: Extend the existing Demo 1 Tailwind components and Persian formatting helpers.

## 11. Success Metrics

| Metric | Target | Measurement |
|--------|--------|-------------|
| Reconciliation | Purchase and vendor totals equal Mabna for 3 sample months (100%) | Reconciliation check log, before sign-off |
| Adoption | Management and Purchasing users sign in at least weekly for the first 2 months after go-live | Audit log |
| Time to report | A vendor purchase report in under 1 minute, compared with today's manual process | Timed walkthrough with a purchasing officer at go-live |
| AI accuracy | On a fixed 20-question Persian purchasing test set, every answer is correct and cited or says "data unavailable" | Test set run at Phase 4 and at go-live |
| Data quality | No report shows figures from records with unflagged validation issues | Data-quality page review at go-live |
| Mabna safety | Zero write statements reach Mabna | Login permission check plus connector guard tests |

## 12. Risks and Assumptions

| # | Risk / Assumption | Impact | Mitigation |
|---|-------------------|--------|------------|
| R1 | Mabna's purchasing, vendor, and payables tables are not yet verified (schema work so far covers sales, receivables, and cheques) | High | Phase 1 starts with on-site verification of purchase tables per `onsite-checklist.md`; no report is built on assumed structures |
| R2 | The purchase formula default (net of returns, excl. VAT, by document date) may differ from the company's accounting practice | High | Accounting confirms before sign-off; gross, returns, and VAT are shown separately so the definition can change without data loss |
| R3 | Vendor balances may be spread across accounting documents and payable cheques in ways that are hard to reproduce | High | Vendor financials are the last Release 1 phase, reconciliation-gated, with "data unavailable" when unverified |
| R4 | The local model may be too slow or weak in Persian on the available hardware | Medium | F12 benchmark; cloud fallback with aggregates and owner approval |
| R5 | Mabna's per-user report views filter on `HOST_ID()` and return no rows to a read-only client | Medium | Compute from base tables or non-host-filtered views only (spec 002 FR-09) |
| R6 | Nightly sync may be too stale for some users | Low | "Sync now" button; revisit cadence after go-live |
| R7 | Development cannot see real data off-site | Medium | Simulator with synthetic data; aggregate control totals only with owner approval; reconciliation on-site |
| R8 | Scope growth from new report requests during Release 1 | Medium | New requests go to a report catalogue and a later phase or release |
| A1 | The company LAN server can run Docker Compose and reach Mabna | — | Confirmed on-site during Phase 7 preparation; Windows-services fallback |
| A2 | Real company data stays on the LAN; only the owner can approve otherwise | — | Enforced by F12 and the deployment design |

## 13. Open Questions

- Which Mabna tables and operation codes hold purchase invoices, purchase returns, and the vendor (supplier) master? (On-site verification, Phase 1.)
- Does the company's accounting treat purchase VAT and purchase discounts the way the default formula assumes? (Accounting, before Phase 1 sign-off.)
- How does Mabna compute a vendor balance: accounting documents, payable cheques (ChqTp 4/6/8/18), allocations, or a mix? (On-site verification, Phase 6.)
- What hardware does the LAN server have (CPU, RAM, GPU), and can it host a local model? (Phase 4 benchmark.)
- Who in the company holds each seeded role at go-live? (Owner, Phase 3.)
- Does management want any product categorization beyond Mabna's own drug groups and classes for the category filter? (Owner, Phase 2.)
- When should Release 2 planning start, and does the spec 002 receivables work need refreshing against newer Mabna data by then? (Owner, after Release 1 go-live.)

---

*Generated by the PRD Writer agent. Review with stakeholders before committing to implementation.*
