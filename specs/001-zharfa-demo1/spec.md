# Feature Specification: Zharfa Demo 1 — Single-Path Analytics Demo

**Feature Branch**: `001-zharfa-demo1`

**Created**: 2026-10-30

**Status**: Draft

**Input**: Demo 1 Zharfa analytics platform single demo path synthetic data — Persian RTL dashboard (sales, inventory alerts, receivables) + one AI Business Analyst + Manager/Admin RBAC + Docker Compose install, per zharfa-demo1-prd.md v2.0 governing scope.

**Scope authority**: `zharfa-demo1-prd.md` v2.0 Demo 1 governs; `PRD.md` v1.1 is reference only for deferred work. `AGENTS.md` rules bind every decision. Locked orchestrator decisions (single-path: custom React dashboard only, one Business Analyst agent only, one cloud AI provider with Ollama seam preserved-not-implemented, browser-print only; no Mabna connection; three financial formulas only; Persian-first RTL; RBAC Manager+Admin server-side; secrets server-env only; same KPI definitions everywhere; AI cites period+filters+view or says unavailable; host gaps planned around) are stamped into this spec and all downstream artifacts.

## Clarifications

### Session 2026-10-30

Headless run: no live user to interview, so the scan below resolved every candidate with a documented default (no open questions, no NEEDS CLARIFICATION markers). The human reviewer may override any default before build.

- Q: How do Manager and Admin authenticate in Demo 1 (mechanism, login UX, seed users)? → A: (default) Seeded demo accounts (one Manager, one Admin) with password login on a Persian login screen; server-side sessions; hashed passwords; no self-registration; all authorization enforced server-side. Production identity integration deferred.
- Q: How does the Admin configure the single AI provider (in-app UI vs server env)? → A: (default) Server env / deploy/.env only (AiProvider__*); an admin-only redacted status endpoint exposes name/endpoint/model but never the key; no in-app provider-editing UI in Demo 1; rotation via env plus restart.
- Q: Where is the single low-stock threshold defined, and is it per product? → A: (default) One server-side configured value with a documented default, same for all products; per-product and per-warehouse thresholds deferred to pilot.
- Q: How many debtors sit in "top debtors", and what defines "this month" under a Jalali UI? → A: (default) Top 5 debtors, consistent with the sales top-5s; period selection uses Jalali months (current plus previous presets, plus a custom range mapped to Gregorian date ranges at the API boundary); month-over-month compares the selected Jalali month with the previous Jalali month.
- Q: Which cloud AI provider ships in Demo 1? → A: (default) No provider named in the spec; the backend defines a generic HTTP provider interface (name/endpoint/model/key from server env) and the operator supplies values at deploy; the Ollama seam is preserved as an interface only, not implemented.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - View sales overview in Persian (Priority: P1)

A manager opens the Persian app loaded with demonstration data and sees this month's sales: total sales, sales trend, this-month vs last-month comparison, top 5 products, top 5 customers. The manager changes the month (presets plus custom range) and the single product-category filter; every number and its supporting rows update consistently. All labels, numbers, currency and dates are Persian with Jalali dates. Every screen states the data is demonstration data.

**Why this priority**: This is the core of the Demo 1 sentence — "sales up/down" — and the first of the five observable checks. Without it there is no demo.

**Independent Test**: Can be fully tested by opening the dashboard with seed data spanning multiple months, selecting a month preset and a category, and confirming totals, MoM delta, trend, top-5 lists and supporting rows all reflect the same period and filter and match the approved KPI view.

**Acceptance Scenarios**:

1. **Given** seeded sales across at least three months and several categories, **When** the manager selects a month and a category, **Then** total sales, MoM comparison, trend, top 5 products and top 5 customers all reflect that period and filter and match the sales KPI view.
2. **Given** the sales overview is shown, **When** the manager opens supporting rows behind any sales metric, **Then** a read-only table lists the rows behind that figure for the same period and filter.
3. **Given** any sales screen, **When** rendered, **Then** all text is Persian RTL with Jalali dates and Persian number/currency formatting, and a demonstration-data label is visible.

---

### User Story 2 - View inventory alerts in Persian (Priority: P1)

A manager sees low-stock items (single threshold) and out-of-stock items for the selected period and category, with supporting rows behind each list. No batch/expiry drill-down is offered; batch presence is at most a flag.

**Why this priority**: "What is low" is the second leg of the Demo 1 sentence and part of check 1. It shares filters and KPI discipline with sales.

**Independent Test**: Can be fully tested by seeding varied stock states (healthy, low, out-of-stock) and confirming the low-stock and out-of-stock lists, counts and supporting rows update with the category filter and match the inventory KPI view.

**Acceptance Scenarios**:

1. **Given** products in healthy, low and out-of-stock states, **When** the manager views inventory alerts, **Then** low-stock (single threshold) and out-of-stock lists are shown separately with counts matching the inventory KPI view.
2. **Given** a category filter is applied, **When** the manager views inventory alerts, **Then** both lists and their supporting rows reflect that category.
3. **Given** batch data is present on a product, **When** shown, **Then** it appears only as a flag with no separate expiry workflow.

---

### User Story 3 - View receivables in Persian (Priority: P1)

A manager sees total customer receivables, the top debtors list, and payments received this month, with supporting rows, under the same period and category discipline and Persian RTL presentation.

**Why this priority**: "Who owes us" is the third leg of the Demo 1 sentence and part of check 1. Receivables balance is one of the three documented financial formulas.

**Independent Test**: Can be fully tested by seeding varied customer balances and receipts, selecting a month, and confirming total receivables, top debtors and payments-received-this-month match the receivables KPI view and update with filters.

**Acceptance Scenarios**:

1. **Given** seeded customers with varied balances and receipts across months, **When** the manager views receivables for a month, **Then** total receivables, top debtors and payments received this month match the receivables KPI view.
2. **Given** the receivables view is shown, **When** the manager opens supporting rows, **Then** the rows behind each figure are listed read-only for the same period and filter.
3. **Given** sales revenue and cash received figures are both shown, **When** compared, **Then** they are never equated or mixed; each cites its own documented formula.

---

### User Story 4 - Ask the AI Business Analyst in Persian (Priority: P1)

A manager asks one Persian sales question (for example میزان فروش این ماه چقدر بوده؟) and one Persian debt or low-stock question (for example کدام مشتریان بیشترین بدهی را دارند؟ or چه محصولاتی موجودی کمی دارند؟). Each answer cites its period, filters and source view, uses the same KPI definitions as the dashboard and tables, matches dashboard figures, and states data is unavailable or incomplete instead of guessing. No invented figures.

**Why this priority**: The cited Persian answer backed by the same numbers is the second half of the Demo 1 sentence and check 3. It proves dashboard, tables and AI share one KPI truth.

**Independent Test**: Can be fully tested by asking one sales question and one debt/low-stock question against seeded data and verifying both answers cite period plus filters plus source view, match dashboard figures for the same period and filters, or explicitly state data is unavailable.

**Acceptance Scenarios**:

1. **Given** seeded demo data for a month and category, **When** the manager asks a Persian sales question for that period, **Then** the answer cites period, filters and source view and its figures match the dashboard for the same period and filters.
2. **Given** seeded balances and stock states, **When** the manager asks a Persian debt or low-stock question, **Then** the answer cites period, filters and source view and matches dashboard/table figures, or explicitly states data is unavailable.
3. **Given** a question outside the three metric sets or with missing data, **When** asked, **Then** the agent states data is unavailable or incomplete rather than guessing, keeps facts separate from interpretation, and only uses approved read-only views with allowlist, limits and timeouts.

---

### User Story 5 - Admin configures AI provider; roles enforced server-side (Priority: P2)

An admin configures the single AI provider (endpoint, key, model) from server-side settings. Secrets never appear in frontend code, the repository, or logs. Manager (view) and Admin (users/config) roles are enforced server-side; frontend hiding is not enforcement. A non-manager role cannot reach admin functions even by direct API call (verified by negative test — check 4).

**Why this priority**: Check 4 is a Demo 1 gate. Secrets handling and server-side authz are §7 safeguards that must ship in Demo 1, but they do not demo the business sentence on their own.

**Independent Test**: Can be fully tested by configuring the provider server-side, inspecting the frontend bundle, repo and logs for secrets (none present), and calling an admin endpoint directly as a non-manager role (denied).

**Acceptance Scenarios**:

1. **Given** an admin user, **When** configuring the single AI provider, **Then** endpoint, key and model are stored server-side (env / Docker secrets) and applied without any secret reaching the frontend bundle, repo or logs.
2. **Given** a non-manager role, **When** calling an admin function directly via API, **Then** access is denied server-side regardless of what the UI shows or hides.

---

### User Story 6 - Clean install, reconciliation, browser-print (Priority: P2)

An operator follows the README install on a Docker host: clean Compose install with no Mabna access and no manual DB edits brings up the Persian app on demonstration data; API sales and receivables totals match the same query run directly against the DB views (one reconciliation check — mismatch blocks sign-off and is logged); browser-print produces a readable Persian report; a documented PostgreSQL backup plus restore procedure validates once before sign-off.

**Why this priority**: Check 5 plus the §7.5 and §7.7 safeguards close the Demo 1 DoD. They prove the demo is reproducible, honest about data, and financially disciplined.

**Independent Test**: Can be fully tested on a Docker host by running the documented clean install, comparing API totals to direct DB-view totals, printing a dashboard page from the browser, and running the documented backup plus restore once.

**Acceptance Scenarios**:

1. **Given** a clean host with only the documented prerequisites, **When** the operator runs the README Compose install, **Then** the full flow works with no Mabna access and no manual DB edits.
2. **Given** the running deployment, **When** API sales and receivables totals are compared to the same query against the DB views, **Then** they match; on mismatch sign-off is blocked and the event is logged.
3. **Given** any dashboard or table view, **When** browser-printed, **Then** the output is a readable Persian report; every view carries a demonstration-data label.

### Edge Cases

- Empty month (no sales, receipts or movements in the selected period): metrics show zero or empty state in Persian, supporting rows show empty, AI states data is unavailable rather than inventing figures.
- Returns and discounts: represented at least representatively in seed; sales revenue formula documents how they reduce gross to net; never silently dropped nor double-counted.
- Category with no products or customers with zero balance: lists render empty in Persian, counts stay consistent between headline figures and supporting rows.
- Custom date range spanning partial months: MoM comparison labels exactly which periods are compared; AI cites the same periods.
- Provider unconfigured or unreachable: AI states data or service is unavailable; no secret, stack trace or key material leaks to the client or logs.
- Direct API access without or with insufficient role: denied server-side with no data disclosure.
- Reconciliation mismatch: sign-off blocked, mismatch logged, dashboard figures not presented as verified.
- Batch data present: shown only as a flag; no expiry workflow is reachable.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST show sales overview (total sales this month, trend, this-vs-last-month comparison, top 5 products, top 5 customers) for a selected month and single product category.
- **FR-002**: System MUST show inventory alerts as two lists (low-stock under a single server-side configured threshold with a documented default, same for all products; out-of-stock) for the selected filter, with no batch/expiry drill-down beyond a flag.
- **FR-003**: System MUST show receivables (total customer receivables, top 5 debtors list, payments received this month) for the selected period and filter.
- **FR-004**: System MUST provide month presets plus custom date range and a single-select product-category filter that update every headline number and every supporting row consistently.
- **FR-005**: System MUST show read-only supporting rows behind each metric for the same period and filter; no separate reporting module ships in Demo 1.
- **FR-006**: System MUST render everything Persian-first RTL (Persian labels, Jalali dates, Persian number/currency formatting), responsive desktop-first; English UI is out of scope.
- **FR-007**: System MUST label synthetic data as demonstration data on every data view.
- **FR-008**: System MUST answer Persian questions over the three metric sets only, with every answer citing period, filters and source view, using the same KPI definitions as dashboard and tables, and stating data-unavailable instead of guessing.
- **FR-009**: System MUST restrict AI and dashboard reads to approved read-only views with an allowlist plus query limits and timeouts; no writes, no arbitrary SQL, no operational or legacy tables.
- **FR-010**: System MUST enforce Manager (view) vs Admin (users/config) authorization server-side; frontend hiding MUST NOT grant or imply access. Demo 1 default: seeded demo accounts with password login and server-side sessions; no self-registration.
- **FR-011**: System MUST keep secrets (AI keys, DB credentials) in server env / Docker secrets only; they MUST NEVER appear in the frontend bundle, repository, or logs. Demo 1 default: provider configured via server env only, with an admin-only redacted status endpoint (name/endpoint/model, never key); no in-app provider editing.
- **FR-012**: System MUST document and implement exactly three financial formulas (sales revenue, cash received, receivables balance) and MUST NEVER equate sales revenue with cash received.
- **FR-013**: System MUST ship one reconciliation check (API sales and receivables totals vs the same query on DB views); a mismatch MUST block demo sign-off and be logged.
- **FR-014**: System MUST provide a clean Docker Compose install (documented README steps) that works with no Mabna access and no manual DB edits; browser-print MUST produce a readable Persian report.
- **FR-015**: System MUST keep the WebView2 desktop shell thin (hosting the shared frontend only, no business logic, no Windows-only UI logic) with the backend API independent of the shell and no client talking directly to the database.
- **FR-016**: System MUST reserve empty `SourceRecord` / `SyncJob` / `SyncLog` tables for later Mabna integration without redesign, and MUST NOT connect to Mabna in Demo 1.
- **FR-017**: System MUST record an audit trail entry for admin/config and sign-off-relevant events including reconciliation results.
- **FR-018**: System MUST provide a documented PostgreSQL backup plus restore procedure validated once before sign-off.

### Key Entities

- **Product**: sellable item with category link, identity by product id; attributes include name (Persian), category, unit, active flag.
- **ProductCategory**: single-level grouping used by the Demo 1 single-select filter; identity by category id.
- **Customer**: buyer with receivables balance; identity by customer id; no real company PII in Demo 1 (synthetic names only).
- **SalesInvoice**: sale header with customer, date (stored Gregorian, displayed Jalali), status; returns/discounts represented representatively.
- **SalesInvoiceItem**: sale line with product, quantity, unit price, line discount; feeds the sales-revenue formula.
- **Warehouse**: stock location; Demo 1 needs at least one; multi-warehouse depth deferred.
- **StockMovement**: inventory change (in/out) linked to product and warehouse; feeds low/out-of-stock derivation.
- **Receipt (Payment, receivables side only)**: cash received against a customer; feeds cash-received and receivables-balance formulas; payables side out of scope.
- **User / Role**: Manager (view) and Admin (users/config); enforced server-side.
- **AuditLog**: append-only record of admin and reconciliation-relevant events.
- **Reserved (empty in Demo 1)**: `SourceRecord`, `SyncJob`, `SyncLog` — schema present, no rows, no Mabna wiring.
- **Semantic KPI views (read-only)**: sales-overview view, inventory-alerts view, receivables view — the only sources the API and AI read; base tables never read directly by clients or AI tools.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A manager opens the Windows app on demo data and within one minute sees this-month vs last-month sales, low/out-of-stock items and receivables/top debtors, all in Persian with Jalali dates (covers check 1).
- **SC-002**: Changing month and category updates every headline number and every supporting row to the same period and filter with zero mismatches between dashboard, tables and AI answers sharing one KPI definition (covers check 2).
- **SC-003**: One Persian sales question and one Persian debt/low-stock question each return answers citing period, filters and source view that match dashboard figures, or explicitly state data is unavailable — with zero invented figures (covers check 3).
- **SC-004**: Admin configures the single AI provider server-side with zero secrets found in frontend code, repo or logs, and a direct-API call as non-manager to an admin function is denied (covers check 4).
- **SC-005**: A clean Docker Compose install per the README succeeds with no Mabna access and no manual DB edits, and browser-print yields a readable Persian report (covers check 5).

## Assumptions

- Seed data spans multiple months with representatively modeled returns/discounts, varied stock states (healthy/low/out-of-stock) and varied customer balances, so MoM comparison and all three metric sets are demonstrable.
- Single low-stock threshold is sufficient for Demo 1; per-product or per-warehouse thresholds are pilot scope.
- Month-only period comparison is sufficient; week/quarter/year comparisons are deferred.
- Single-select category is sufficient; multi-dimension filtering, full rankings beyond top 5 and drill-down beyond supporting rows are deferred.
- One cloud AI provider configured server-side is sufficient; provider-selection UI beyond single config and any second-provider implementation are deferred.
- No Docker daemon on this spec host and no Windows runtime here are plan-around constraints: Compose and WebView2-run verification are documented for a Windows/Docker host or CI rather than blocking the spec.
- Gregorian dates stored in the database with Jalali conversion at presentation is acceptable; Jalali storage is not required.
- Constitution file `.specify/memory/constitution.md` is an unratified placeholder; project authority for Demo 1 is the Demo 1 PRD plus AGENTS.md until a constitution is ratified.
