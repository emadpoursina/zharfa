# Feature Specification: Vendor Financials (Release 1, Phase 6)

**Feature Branch**: `zharfa-prd-phase-6`

**Created**: 2026-11-19

**Status**: Draft

**Input**: User description: "skip phase: zharfa-prd.phases.md#6 — Ship Zharfa Release 1 Phase 6 so Finance and Management can see vendor balances, outstanding payments, payment history, and purchases vs payments, reconciled with Mabna (F17 + F5 AC3)."

**Scope authority**: `zharfa-prd.md` v3.0, Phase 6 of `zharfa-prd.phases.md` ("Vendor financials"), covering F17 (vendor financials) in full and F5 in part (AC3: vendor balance on the profile). `AGENTS.md` rules bind every decision. Phases 1–5 (`specs/003-real-purchase-data`, `specs/004-vendor-purchasing-reports`, `specs/005-permissions-audit-quality`, `specs/006-local-ai-purchasing`, `specs/007-ai-reports-export`) are existing behavior this phase extends and MUST NOT be reopened. Spec 002 (`specs/002-mabna-accounting-connector`) remains the foundation for the read-only connector, guards, simulator and on-site checklist; its receivables and cash-received stories stay in Release 2.

**Phase goal**: Finance and Management see what the company owes each vendor, its payments, and purchases vs payments, reconciled with Mabna.

## Clarifications

### Carried over from earlier phases (owner decisions, binding)

- Release 1 roles are Admin, Management, Purchasing and Finance. Only Management and Finance hold the vendor-financial permission. Admin holds purchasing reports and administration, not vendor financials. All authorization is enforced on the server; hiding a menu is not enforcement.
- Phase 3 reserved a vendor-financials area that answered "data unavailable" to permitted callers and 403 to everyone else. This phase fills that area with real figures for permitted roles; the 403 path is unchanged.
- Amounts are shown in Rial, labeled "ریال". Jalali calendar, Persian RTL, Persian digits.
- Simulator example columns and objects use `Sim*` names; real Mabna data never enters the repository; development uses `deploy/mabna-sim/`.
- Each install is exactly one data mode. Production shows real data only; demo installs label everything as demonstration data. The AI uses one dataset per install.
- Source-item status rules are unchanged (`unknown`, `candidate`, `confirmed`, `corrected`). Production uses only `confirmed` or `corrected`. A `candidate` item is usable in demo mode and the simulator but not in production. Anything depending on an unusable item shows "data unavailable", never an estimate.
- Purchases, payments and vendor balance stay separately documented formulas. Purchases MUST NOT be presented as payments or as balance. A price difference is never presented as profit. Facts stay separate from interpretation.
- Phase 1 purchase metric definitions (net, gross, returns, VAT) are reused unchanged wherever purchases appear beside payments.
- Phase 2 period rules: calendar presets compare with the previous Jalali unit of the same kind; a custom range compares with the same number of days ending the day before the range starts. ی/ک search is unchanged.
- Phase 5 export, saved-report reopen, and AI table/chart rules stay; this phase adds vendor-financial data under the existing vendor-financial permission. Purchasing users still cannot see financials anywhere.
- Audit entries never contain secrets, passwords, provider keys, database credentials, or result rows.
- Live Mabna is never queried per report request; reports read the synced copy. `HOST_ID()`-filtered report views are never read on live Mabna.
- Three-month real-data reconciliation of purchase totals and vendor balances is Phase 7, not this phase.

### Assumptions taken in place of questions (no-question run; skip)

- **How Mabna computes vendor balance (PRD open question).** Accounting confirms the formula on-site: accounting documents, payable cheques (including whether types such as ChqTp 4/6/8/18 apply), allocations, or a documented mix. Until that confirmation is recorded as `confirmed` or `corrected`, production shows "data unavailable" for balance. The simulator uses a documented **candidate** mix (open payables plus unpaid payable cheques, excluding cancelled and already-paid instruments) so Finance can be demonstrated off-site; that mix is never shown as production data.
- **What "for the selected period" means.** F17 AC1 applies the selected Jalali period to the financial views: **vendor balance** is the signed amount the company owed that vendor at the **end of the selected period**; **outstanding payments** are unpaid instrument remainders still open as of that same as-of date; **payment history** lists completed payments (or confirmed paid portions) whose payment or clear date falls in the period; **purchases vs payments** places Phase 1 net purchases for the period beside the payments formula for the same vendor and period.
- **Outstanding vs history vs balance.** Outstanding payments are not the same as payment history and not the same as balance. History is completed payments in the period. Outstanding is still unpaid as of period end. Balance is the accounting-confirmed payable at period end and MAY draw on documents, cheques and allocations; it MUST NOT be labeled as "purchases" or as "payments".
- **Purchases vs payments display.** The view shows the two figures as separate columns (or separate labeled amounts) and MAY show a labeled arithmetic difference "purchases minus payments". That difference MUST NOT be named profit, margin, cash received, or vendor balance.
- **Where Finance starts.** Journey 2: Vendors → Financials lists vendors with signed balance and outstanding payments for the selected period; opening a row (or a vendor) shows that vendor's payment history, outstanding instruments, and purchases vs payments. The same figures are available from a vendor profile for users who hold vendor-financial permission (F5 AC3 adds **balance** on the profile; payment history, outstanding instruments and purchases vs payments are reachable from the profile or Financials without duplicating purchasing tables).
- **Profile for Purchasing.** Purchasing users keep the Phase 2 profile (purchases, trend, products, returns, documents) with **no** balance, payments or other financial figure, including via a direct request.
- **Sync of financial sources.** Existing nightly incremental sync and Admin "Sync now" also load the verified vendor-financial source tables (balances/payables, payable cheques, payments) into the reporting copy. Failed sync still keeps the last good copy. Financial pages show last-sync time like other reports.
- **On-site checklist.** Phase 6 adds a purchasing/payables subsection to `specs/002-mabna-accounting-connector/onsite-checklist.md` (catalog metadata and owner-approved aggregates only). Statuses are recorded as logical names (same pattern as Phase 1 `source-verification.md`); no business rows leave the site.
- **Reconciliation check.** Same shape as Phase 1: a developer-run command or automated test (no new Finance UI required) comparing the system's **vendor balance totals** with the same query against the source, for at least one Jalali month and one vendor filter on the simulator, and for one real month on-site. A mismatch is logged with both totals and the difference and blocks sign-off. Only pass/fail is committed from the on-site run.
- **Export.** Management and Finance with export permission can export the Financials list and a vendor's payment-history, outstanding-instruments, and purchases-vs-payments tables under Phase 5 Excel rules (Persian headers, RTL sheet, period, filters, export time, last-sync time, 10,000-row cap, audit). Purchasing and Admin receive 403 and no file. Demo-mode files are labeled demonstration data.
- **Saved reports.** A saved Financials or vendor-financial view records the vendor-financial permission. Reopen checks the caller's **current** role. Purchasing (or Admin, or anyone who lost the permission) is refused with a Persian reason naming the missing permission, no query is run, and access-denied is audited. Management and Finance reopen on current synced data.
- **AI.** Approved vendor-financial tools are added for callers who hold vendor-financial **and** AI-queries (Management and Finance). They return the same documented metrics as the Financials pages, cited with period, filters and source metric, or "data unavailable". Purchasing still receives no financial figures (tools do not return them). Admin still has no AI-queries. Cloud payloads, if enabled, remain aggregates the same user could already see — never payment-line dumps. The Phase 4 20-question purchasing set is not rewritten; financial questions are additional for permitted roles.
- **Data quality.** Unverified or missing financial sources show "data unavailable" on the dependent figure. Flagged synced records that a financial view depends on carry the existing Phase 3 data-quality notice. This phase does not invent a second data-quality product; Admin still owns Data quality.
- **Who may open Financials.** Management and Finance: yes. Admin and Purchasing: navigation hides it; a direct request is 403 with an access-denied audit entry.
- **Zero vs unavailable.** A vendor with no payable and no outstanding instruments in the period shows zero, not "data unavailable". Unavailable is reserved for missing sync, unverified source, or a formula accounting has not confirmed.
- **Performance.** Opening the Financials list or a vendor's payment history for a typical period meets the existing report expectation (p95 under 2 seconds for the data view on synced data). The timed "under 1 minute" vendor report walkthrough remains a Phase 7 go-live check.

### Session 2026-11-19

- Q: When a vendor has a credit (the company overpaid, or the accounting formula yields a negative payable), how should vendor balance be shown? → A: (skip mode, recommended option) Signed amount: positive means the company owes the vendor; negative means a credit in the vendor's favour. The list, profile, export, AI and reconciliation all use that signed total. The label stays "vendor balance", never profit.
- Q: Besides the outstanding-payments total on the Financials list, should Finance see the unpaid instruments themselves? → A: (skip mode, recommended option) Yes. Vendor financial detail includes a paged outstanding-instruments table (Jalali date, type or reference, unpaid amount) separate from payment history. The list column is the total of those rows as of period end.
- Q: Should vendor-financial reports offer the same previous-period comparison toggle as purchasing reports? → A: (skip mode, recommended option) Yes on the Financials list and on purchases vs payments (previous Jalali unit for presets; equal-day rule for custom ranges). No on payment history or the outstanding-instruments list (document lists, same exclusion as purchase transaction history).
- Q: Which completed instruments count toward the payments figure and payment history? → A: (skip mode, recommended option) Every verified completed payment to the vendor whose payment or clear date falls in the period, including cleared payable cheques and other verified types (for example cash or bank transfer). If the payments formula depends on an unverified type, the payments figure is "data unavailable".
- Q: If an instrument is only partly paid by period end, how do outstanding payments and payment history treat it? → A: (skip mode, recommended option) The paid portion whose payment date falls in the period is in payment history and the payments total; the remaining unpaid amount is in outstanding as of period end. The two MUST NOT double-count. Until the on-site partial-payment rule is confirmed, production shows "data unavailable" for figures that would need it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Verify payables sources and the balance formula before using them (Priority: P1)

Before any vendor-financial formula is used in production, the developer verifies on-site which Mabna tables and columns hold vendor balances, payable cheques and payments, and accounting confirms how a vendor balance is computed. Each item is recorded as confirmed, corrected, candidate or unknown on the on-site checklist. The simulator is aligned with the verified (or candidate) structure so Finance can be demonstrated off-site with synthetic data.

**Why this priority**: PRD risk R3 and the first Phase 6 exit criterion. Reports are never built on assumed Mabna structures; a guessed mix of documents and cheques would mislead Finance.

**Independent Test**: Extend and run the on-site checklist for vendor balance, payable cheque and payment sources using catalog metadata and aggregate-only probes; record the balance formula as confirmed by accounting (or still unverified); confirm no formula in a production-mode install uses an unverified item.

**Acceptance Scenarios**:

1. **Given** the on-site checklist with a Phase 6 payables section, **When** the developer verifies vendor balance, payable cheque and payment source tables on-site, **Then** each table, key column, date column, amount column, cheque type/state used by a formula, and the accounting-confirmed balance formula is recorded as confirmed, corrected, candidate or unknown before any production formula uses them.
2. **Given** a source item or the balance formula recorded as unknown or only candidate, **When** a production-mode install would need it, **Then** that formula is not used on live data and the dependent figure shows "data unavailable".
3. **Given** verification results, **When** they are recorded, **Then** no business rows leave the site; only schema metadata and approved aggregates are kept, and nothing from the live server is committed.
4. **Given** verified or candidate structures, **When** development continues off-site, **Then** the simulator holds synthetic vendor-financial data matching those structures (`Sim*` names) and never contains real company rows.

---

### User Story 2 - Finance sees balances and outstanding payments per vendor (Priority: P1)

A Finance Officer (or Management) opens Vendors → Financials, chooses a Jalali period, and sees each vendor's balance and outstanding payments for that period, with last-sync time. They can search (ی/ک), sort, page and filter as on other Release 1 reports. Opening a vendor from this list leads to payment history and purchases vs payments (User Story 3).

**Why this priority**: This is Journey 2 steps 1–2 and the second Phase 6 exit criterion (the list half). Without a trusted payable list, Finance still depends on Mabna printouts.

**Independent Test**: After a successful simulator sync of candidate financial tables, sign in as Finance, open Financials for a month, and confirm each listed balance equals the documented balance formula on the synced copy for that vendor at period end, and that outstanding payments are a separate column that is not labeled as purchases or as balance.

**Acceptance Scenarios**:

1. **Given** a Finance or Management user and a successful sync, **When** they open Vendors → Financials and choose a Jalali period, **Then** the list shows per vendor: name, code, vendor balance as of period end, outstanding payments as of period end, and the last-sync time, in Persian RTL with amounts in Rial labeled "ریال".
2. **Given** the Financials list, **When** the user searches by vendor name or code (Arabic ي/ك vs Persian ی/ک), sorts, pages or changes period, **Then** results come from the server already filtered, sorted and paged, and totals reflect the filter.
3. **Given** a vendor with no payable and no outstanding instruments as of period end, **When** the list is shown, **Then** balance and outstanding payments read zero, not "data unavailable".
4. **Given** a production-mode install whose balance source or formula is unverified, **When** Financials is opened, **Then** the balance figure (and any other figure that depends on that source) reads "data unavailable".
5. **Given** a demo-mode install, **When** Financials is shown, **Then** it is labeled as demonstration data and uses only the demo dataset.
6. **Given** the Financials list, **When** a reader compares the balance column with the outstanding-payments column, **Then** they are separately labeled and neither is presented as purchases.
7. **Given** a vendor whose accounting-confirmed (or simulator candidate) balance is a credit, **When** the list is shown, **Then** the balance is a negative signed amount (credit in the vendor's favour), not floored at zero and not labeled profit.
8. **Given** period comparison is on for a month, quarter or year preset, **When** the Financials list is shown, **Then** each of balance and outstanding payments is shown for the selected period next to the previous Jalali unit of the same kind, with absolute difference and, where the previous base is non-zero, percentage change; both periods are stated.

---

### User Story 3 - Payment history and purchases vs payments for one vendor (Priority: P1)

From Financials or from a vendor the user is allowed to see financially, Finance opens one vendor and sees payment history for the selected period, outstanding instruments still open as of period end, and purchases vs payments for that same vendor and period. Purchases use the existing net-purchases definition; payments use the documented payments formula (verified completed instruments in the period). The two are shown side by side and never as one number called the other.

**Why this priority**: Completes F17 AC1–AC2 and Journey 2's "one vendor's payment history and purchases vs payments". Keeps the project's financial-formula rule visible to users, not only in a document.

**Independent Test**: On the simulator, pick a vendor with both purchases and payments in a month; confirm payment-history rows sum to the payments figure, that figure matches the payments formula on the synced copy, outstanding-instrument unpaid amounts sum to the outstanding-payments total, net purchases match the vendor profile / purchasing reports for the same period, and no label calls the pair "profit".

**Acceptance Scenarios**:

1. **Given** a permitted user on a vendor's financial detail for a selected period, **When** it is shown, **Then** it includes payment history (each completed payment in the period with Jalali payment or clear date, instrument or document reference, and amount), a paged outstanding-instruments table (each unpaid instrument still open as of period end with Jalali date, type or reference, and unpaid amount), and purchases vs payments (net purchases and payments as separate labeled figures).
2. **Given** that payment history, **When** its amounts are summed, **Then** the sum equals the payments figure for that vendor and period.
3. **Given** that outstanding-instruments table, **When** unpaid amounts are summed, **Then** the sum equals the outstanding-payments total for that vendor as of period end.
4. **Given** purchases vs payments, **When** net purchases are compared with the vendor profile and purchasing reports for the same vendor and period, **Then** they are equal (Phase 1 definition unchanged).
5. **Given** purchases vs payments, **When** a difference is shown, **Then** it is labeled as purchases minus payments (or equivalent wording that names both) and is not labeled profit, margin, cash received or vendor balance.
6. **Given** a payment whose source is unverified or missing, **When** the history or payments figure would need it, **Then** that figure reads "data unavailable" rather than a partial guess.
7. **Given** the detail, **When** the user changes period, **Then** history, outstanding instruments, purchases and payments all update to the new period, and balance / outstanding payments update to the new period end.
8. **Given** period comparison is on for purchases vs payments, **When** the detail is shown, **Then** net purchases and payments are shown for the selected period next to the previous period of equal kind (Phase 2 rule), and payment history and outstanding instruments do not offer that toggle.
9. **Given** an instrument partly paid by period end and a confirmed on-site partial-payment rule, **When** history and outstanding are shown, **Then** the paid portion in the period is only in history and the remaining unpaid amount is only in outstanding.

---

### User Story 4 - Vendor balance totals match the source (Priority: P1)

A reconciliation check compares the system's vendor balance totals with the same query against the source data. It is run on the simulator for at least one month and one filter, and on-site for one real month. A mismatch is recorded and blocks sign-off. Purchases reconciliation from Phase 1 is not replaced; this check is only for vendor balance.

**Why this priority**: F17 AC3, the third Phase 6 exit criterion, and the project rule that every reconciled metric must match the source query.

**Independent Test**: Run the reconciliation check against the simulator for one Jalali month and one vendor filter; confirm pass with equal totals. Force a mismatch in a test fixture and confirm it is logged and treated as blocking. On-site, run for one real month and record only pass/fail.

**Acceptance Scenarios**:

1. **Given** synced simulator data and a confirmed or candidate balance formula, **When** the reconciliation check runs for at least one Jalali month and at least one filter, **Then** the system's vendor balance totals (signed; credits negative) equal the direct-source query and the result is logged.
2. **Given** a reconciliation run where totals differ, **When** the mismatch is detected, **Then** it is recorded with period, filter, both totals and the difference, and the phase is not signed off until it is resolved.
3. **Given** the on-site install, **When** the reconciliation check runs for one real month, **Then** the totals equal the direct-source query on real data, and only the pass/fail result is recorded in the repository (real figures stay on-site).
4. **Given** the metric definitions document, **When** a reader looks up vendor balance, payments, outstanding payments and purchases, **Then** they find one definition each, used unchanged by Financials, the vendor profile balance, exports, saved reports, AI answers and the reconciliation check.

---

### User Story 5 - Only Management and Finance see vendor financials, on every surface (Priority: P1)

Purchasing users cannot see financials in pages, exports, saved reports or AI answers. Admin cannot either (no vendor-financial permission). A direct request without the permission is refused with 403 and audited. Hiding the Financials menu is not enough.

**Why this priority**: Fifth Phase 6 exit criterion, F17 AC5, F9, and AGENTS.md. Phase 3–5 only reserved the guard; this phase must keep it while data exists.

**Independent Test**: As Purchasing, open or call Financials, vendor-profile balance, financial export, a saved financial report, and an AI question about balances; confirm no figures and 403 or a permission/"unavailable" answer with no numbers. Repeat as Admin (403 / no AI). Repeat as Finance and Management and confirm figures appear (simulator).

**Acceptance Scenarios**:

1. **Given** a Purchasing user, **When** they open Vendors → Financials or request vendor-financial data directly, **Then** the server answers 403, no financial figure is returned, and an access-denied audit entry records the user, time, refused target and missing permission.
2. **Given** a Purchasing user on a vendor profile, **When** the profile is shown or requested, **Then** it shows the Phase 2 purchasing summary only — no balance, outstanding payments, outstanding instruments, payment history or purchases-vs-payments financial block.
3. **Given** a Purchasing user, **When** they ask the assistant about vendor balances, payables, cheques or payments, **Then** the answer contains no financial figures.
4. **Given** a Purchasing user, **When** they request an export of vendor financials or open a saved report that requires vendor-financial permission, **Then** the server answers 403 (or refuses with a Persian reason naming the missing permission), no file or rows are returned, and the attempt is audited.
5. **Given** an Admin, **When** they request vendor-financial data, **Then** the server answers 403 (Admin does not hold vendor-financial permission).
6. **Given** Management or Finance, **When** they open Financials, the vendor-profile balance, export (if they also hold export), or a saved financial report they own, **Then** the request is not refused for lack of vendor-financial permission.
7. **Given** a Management user who saved a financial report, **When** an Admin later changes that user to Purchasing, **Then** the next open of that saved report is refused with a reason and returns no rows.

---

### User Story 6 - Vendor profile shows balance when permitted (Priority: P2)

A Management or Finance user opening a vendor profile sees the vendor's balance (F5 AC3) in addition to the existing purchasing summary. The balance uses the same formula and as-of rule as Financials for the selected period. Purchasing users are unchanged (User Story 5).

**Why this priority**: Explicit Phase 6 scope (F5 AC3). Secondary to the Financials list because Journey 2 starts there, but required for the feature to be complete.

**Independent Test**: As Finance, open a vendor that appears on Financials; confirm the profile balance equals the Financials list balance for the same vendor and period. As Purchasing, confirm the profile still has no balance.

**Acceptance Scenarios**:

1. **Given** a Management or Finance user, **When** they open a vendor profile for a selected period, **Then** the profile also shows that vendor's signed balance as of period end, labeled as balance (not as purchases, not as profit), with last-sync time.
2. **Given** that profile balance, **When** it is compared with Financials for the same vendor and period, **Then** the amounts are equal.
3. **Given** an unverified balance source in production, **When** the profile is opened by a permitted user, **Then** the balance reads "data unavailable" and purchasing figures still follow Phase 2 rules.

---

### User Story 7 - Export and AI use the same financial figures (Priority: P2)

Finance exports the Financials list (Journey 2 step 3) under existing Excel rules. Management or Finance may ask the assistant about a vendor's balance or payments and receive a cited answer (or a table when the question is a comparison/ranking) that matches Financials. Purchasing remains excluded (User Story 5).

**Why this priority**: Completes Journey 2 export and extends Phase 4–5 surfaces so financial data is not a dead end. Independent of the on-screen list once User Stories 2–5 exist.

**Independent Test**: As Finance, export Financials for a month and confirm the file's balance column matches the on-screen list and includes period, filters, export time and last-sync time; ask in Persian for one vendor's balance and confirm the cited figure matches Financials.

**Acceptance Scenarios**:

1. **Given** Finance or Management with export permission on Financials, **When** they export the current filtered table, **Then** the file has Persian headers, RTL sheet direction, period, filters, export time and last-sync time, and sampled balance/outstanding amounts match the on-screen list within the export cap; the export is audit-logged without result rows.
2. **Given** Management or Finance, **When** they ask a vendor-balance or payments question the approved tools can answer, **Then** the answer cites period, filters and source metric and matches Financials, or says "data unavailable" — it does not guess and does not call purchases payments.
3. **Given** a comparison or ranking question about vendor balances from a permitted user, **When** it is asked, **Then** Phase 5 table/chart rules apply using vendor-financial metrics only, still labeled and matched to Financials.
4. **Given** a demo-mode install, **When** a financial export or AI answer is shown, **Then** it is labeled as demonstration data.

---

### Edge Cases

- No successful sync yet: financial figures show "data unavailable"; last-sync states that no sync has completed.
- Sync failed after a good run: previous financial data remains; the failed run is visible on Data sync; financial pages still show the last successful sync time.
- Selected period is the first month with synced financial data: previous-period comparison on the Financials list and on purchases vs payments shows "data unavailable" for the missing previous base; zeros in a present month stay zeros. Payment history and outstanding instruments have no comparison toggle.
- Vendor exists in purchasing master but has no payable documents: listed with zero balance, empty payment history and empty outstanding instruments, not omitted and not "unavailable".
- Accounting-confirmed (or simulator candidate) vendor credit: balance is negative; outstanding payments stay a non-negative unpaid-instrument total; the credit is not labeled profit.
- Cancelled payment or voided cheque: excluded from payments, outstanding and balance, following the on-site rule.
- Uncleared / unpaid payable cheque: counted in outstanding payments (and in balance only if the confirmed formula includes it); listed on the outstanding-instruments table; not counted in payment history.
- Partly paid instrument with a confirmed on-site rule: paid portion in the period is only in payment history; remaining unpaid amount is only in outstanding. If the partial-payment rule is unverified, figures that would need it are "data unavailable".
- Payment dated in a different month from the related purchase: payments follow payment or clear date; purchases follow purchase document date (Phase 1); purchases vs payments may therefore differ without error.
- Completed payment types: cleared payable cheques and other verified types (cash or bank transfer when those sources are verified) appear in history; an unverified type that the payments formula depends on makes payments "data unavailable".
- Allocations that split a cheque across vendors: each vendor's figures follow the verified allocation rule; if allocations are unverified, dependent figures are "data unavailable".
- Arabic ي/ك in vendor names: normalized as in Phase 1–2; search still matches.
- Jalali leap Esfand: period-end as-of date is the last day of the selected month.
- User holds Finance but not export: they see Financials and cannot export (403).
- User holds Purchasing and export: they can export purchasing reports and still cannot export or see financials.
- Cloud AI enabled: only aggregated financial metrics the same user could see on Financials may leave the site; document-level payment rows must not.
- Report print of Financials: existing browser-print rules (whole filtered result capped at 1,000 rows) apply; no custom PDF pipeline.
- Data-quality flagged payable rows: the financial view still computes as specified and shows the Phase 3 notice when the view's scope includes flagged records.

## Requirements *(mandatory)*

### Functional Requirements

**Source verification and sync**

- **FR-001**: Vendor balance, payable cheque and payment source tables, and every column, cheque type/state and operation code used by a Phase 6 formula, MUST be verified on-site and recorded as confirmed, corrected, candidate or unknown before any production formula uses them. The vendor-balance formula MUST be confirmed by accounting before production uses a number.
- **FR-002**: Verification MUST use catalog metadata and owner-approved aggregate probes only; no business rows from the live source MAY be stored off-site or committed. The on-site checklist MUST gain a Phase 6 section for these items.
- **FR-003**: The development simulator MUST be extended with synthetic vendor-financial data matching the verified or candidate structure (`Sim*` names only).
- **FR-004**: Only verified (or, for the simulator/demo, candidate) vendor-financial tables MAY be added to the connector allowlist; `HOST_ID()`-filtered report views MUST NOT be read on live Mabna; non-SELECT statements and non-allowlisted tables MUST still be refused before reaching the source.
- **FR-005**: Nightly incremental sync and Admin "Sync now" MUST include the vendor-financial source tables in the same job model as Phase 1 (status, duration, row counts per table, keep previous data on failure). Reports MUST read the synced copy, never the source per request.
- **FR-006**: Every financial page MUST show the completion time of the last successful sync.

**Metric definitions (F17 AC2)**

- **FR-007**: The system MUST keep **purchases**, **payments** and **vendor balance** as three separately documented formulas, each with exactly one definition used by Financials, the vendor profile, exports, saved reports, AI answers and reconciliation. Purchases MUST NOT be presented as payments or as vendor balance.
- **FR-008**: **Purchases** in this phase MUST mean Phase 1 net purchases (invoices minus returns, excluding VAT, document date, verified discount basis) unchanged. Gross, returns and VAT remain available as separate purchase figures where a purchasing view already shows them; they MUST NOT be relabeled as payments.
- **FR-009**: **Payments** MUST mean amounts actually paid to the vendor whose payment or clear date falls in the selected period, excluding outstanding/unpaid remainder, using the on-site payment-date and cleared/paid rule. The figure MUST include every verified completed instrument type (cleared payable cheques and other verified types such as cash or bank transfer). IF the payments formula depends on an unverified type, THEN the payments figure MUST be "data unavailable".
- **FR-010**: **Outstanding payments** MUST mean the total unpaid remainder of instruments (including payable cheques still open) as of the end of the selected period, documented separately from payments and from balance. Vendor financial detail MUST list those instruments in a paged table (Jalali date, type or reference, unpaid amount) whose unpaid amounts sum to that total. Payment history MUST NOT include those rows.
- **FR-033**: IF an instrument is partly paid by period end and the on-site partial-payment rule is confirmed, THEN the paid portion whose payment date falls in the period MUST appear only in payment history and the payments total, and the remaining unpaid amount MUST appear only in outstanding. IF that rule is unverified, THEN figures that would need it MUST be "data unavailable". The paid and unpaid portions MUST NOT be double-counted.
- **FR-011**: **Vendor balance** MUST mean the signed amount the company owed that vendor at the end of the selected period according to the accounting-confirmed formula (documents, payable cheques, allocations, or the confirmed mix): positive means the company owes the vendor; negative means a credit in the vendor's favour. The label MUST remain "vendor balance" and MUST NOT be profit. Until the formula is confirmed, production MUST show "data unavailable" for it. Reconciliation MUST use the same signed totals.
- **FR-012**: Purchases vs payments MUST show purchases and payments as separate labeled figures for the same vendor and period. A labeled difference MAY appear only as purchases minus payments (or equivalent) and MUST NOT be named profit, margin, cash received or vendor balance. A price difference MUST NOT be presented as profit.
- **FR-013**: All financial KPI calculations MUST be performed on the server.

**Financials views (F17 AC1, F5 AC3)**

- **FR-014**: Users with vendor-financial permission MUST be able to open Vendors → Financials and see, for a selected Jalali period (month presets and custom range, same as other Release 1 reports), each vendor's signed balance and outstanding payments, with search, sort and server-side pagination.
- **FR-015**: From Financials (and from a vendor profile for permitted users), the user MUST be able to see that vendor's payment history for the period, outstanding instruments as of period end, and purchases vs payments for the period.
- **FR-016**: WHERE the user has vendor-financial permission, the vendor profile MUST also show the vendor's signed balance for the selected period (F5 AC3), equal to the Financials list amount for that vendor and period.
- **FR-017**: Financial views MUST follow Persian-first conventions: RTL layout, Jalali dates, Persian digits, amounts in Rial labeled "ریال".
- **FR-018**: Changing the selected period MUST update balance (new period end), outstanding payments and instruments, payment history and purchases vs payments.
- **FR-019**: IF a financial figure's source data is missing or unverified, THEN the system MUST show "data unavailable" for that figure and MUST NOT substitute purchases, another vendor's amount, or an estimate.
- **FR-020**: A vendor with no qualifying financial activity MUST show zero (empty history and empty outstanding instruments), not "data unavailable".
- **FR-032**: Period comparison MUST be available on the Financials list (balance and outstanding payments) and on purchases vs payments, using the Phase 2 previous-period rule (previous Jalali unit for month/quarter/year presets; equal number of days ending the day before a custom range). It MUST NOT be offered on payment history or the outstanding-instruments list; the server MUST refuse the comparison parameter for those document lists.

**Reconciliation (F17 AC3)**

- **FR-021**: A reconciliation check, run as a developer command or automated test, MUST compare the system's vendor balance totals for a given period and filter with the same query run directly against the source data, and MUST write a log with period, filter, both totals, difference and pass/fail. On-site it MUST run for one real month; only pass/fail MAY be recorded in the repository.
- **FR-022**: A vendor-balance reconciliation mismatch MUST be recorded and MUST block Phase 6 sign-off until resolved.

**Permissions, audit, export, saved reports, AI (F17 AC5)**

- **FR-023**: The system MUST show vendor financials only to roles with the vendor-financial permission (Management and Finance). Purchasing and Admin MUST NOT receive financial figures in pages, exports, saved reports or AI answers.
- **FR-024**: A signed-in caller without vendor-financial permission who requests vendor-financial data MUST receive 403 with no data, and the attempt MUST be audited (user, time, refused target, missing permission). Unauthenticated callers MUST receive 401.
- **FR-025**: Opening Financials, a vendor-profile balance, payment history, outstanding instruments or purchases vs payments MUST write a report-open audit entry under Phase 3 rules (period and filters; paging/sorting alone does not add an entry). Result rows MUST NOT be stored.
- **FR-026**: Export of a vendor-financial table MUST follow Phase 5 Excel rules and MUST require both export permission and vendor-financial permission. A caller missing either MUST receive 403, no file, and an access-denied audit entry. Successful exports MUST be audited without result rows.
- **FR-027**: Saved reports whose data is vendor-financial MUST record the vendor-financial permission and MUST refuse reopen when the caller's current role lacks it, with a Persian reason, no query, and an access-denied audit entry.
- **FR-028**: The assistant MAY answer vendor-financial questions only through approved read-only tools that return the same documented financial metrics, only for callers who hold both vendor-financial and AI-queries. Answers MUST cite period, filters and source metric or say "data unavailable". Purchasing MUST still receive no financial figures. Tools MUST keep the existing row cap and timeouts; exceeding them yields "data unavailable" or assistant-unavailable, never a guess.
- **FR-029**: Navigation MUST hide Financials from roles that lack the permission; that hiding is not enforcement (FR-024 is).

**Data mode and quality**

- **FR-030**: Production-mode financials MUST use real synced data only and MUST NOT show synthetic figures. Demo-mode financial pages, exports and AI answers MUST be labeled as demonstration data.
- **FR-031**: A financial view whose scope includes Phase 3 flagged records MUST show the existing data-quality notice. Unverified sources remain "data unavailable" (FR-019), not a silent zero.

### Key Entities

- **Vendor financials list**: per-vendor row for a selected period: identity (name, code), signed vendor balance as of period end, outstanding payments as of period end, last-sync time.
- **Vendor balance**: accounting-confirmed signed payable at period end (positive = company owes the vendor; negative = credit in the vendor's favour); one documented formula; reconciled against the source; never labeled as purchases, payments or profit.
- **Outstanding payment**: an unpaid instrument remainder (for example an open or partly paid payable cheque) still open as of period end; listed on the outstanding-instruments table and totaled separately from payment history.
- **Payment (history row)**: a completed payment (or confirmed paid portion) in the selected period: Jalali payment or clear date, vendor, reference, amount, instrument type; sums to the payments formula.
- **Purchases vs payments**: pairing of Phase 1 net purchases and the payments formula for one vendor and period; optional labeled difference that is not profit.
- **Vendor-financial permission**: Phase 3 permission held only by Management and Finance; gates Financials, profile balance, financial export, financial saved reports and financial AI tools.
- **Metric definitions document**: the single place that states purchases, payments, outstanding payments and vendor balance; consumers must not invent a second meaning.
- **Reconciliation log**: period, filter, system total, source total, difference, pass/fail for vendor balance; on-site real figures stay off-repo.
- **Source verification record**: on-site statuses for balance, cheque and payment items; candidate vs confirmed drives production "data unavailable".

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Vendor balance, payable cheque and payment source items are recorded on the on-site checklist before any production formula uses them, and the balance formula has an accounting confirmation (or production shows "data unavailable" for balance in 100% of unverified cases) — Phase 6 exit.
- **SC-002**: A Finance or Management user can open Financials for a chosen period and see signed balance, outstanding payments, payment history, outstanding instruments and purchases vs payments per vendor in one continuous journey, with last-sync time, in 100% of successful-sync simulator tests — Phase 6 exit.
- **SC-003**: Vendor balance totals equal the direct-source query for at least one month and one filter on the simulator, and for one real month on-site; 100% match required or sign-off is blocked — Phase 6 exit.
- **SC-004**: In production, 100% of financial figures whose source is missing or unverified display "data unavailable" and 0% display an estimated or substituted number — Phase 6 exit.
- **SC-005**: In a role sweep, Purchasing users obtain 0 financial figures from Financials, vendor profile, exports, saved reports and AI answers; forbidden direct requests are refused with no data and are audited — Phase 6 exit.
- **SC-006**: For a sampled vendor and period, profile balance, Financials list balance, export balance and a permitted AI-cited balance are the same amount (one formula).
- **SC-007**: In a review of Financials, exports and AI answers, purchases, payments and balance appear under their own names in 100% of samples; 0 samples present purchases as payments or a price difference as profit.
- **SC-008**: A typical Financials list or payment-history view on synced data meets the existing report data-view expectation (p95 under 2 seconds).
- **SC-009**: Demo-mode financial screens, files and AI answers are labeled demonstration data in 100% of sampled opens; production-mode samples contain no synthetic vendor-financial rows.
- **SC-010**: A Finance user can complete Journey 2 (open Financials, inspect one vendor's history and purchases vs payments, export the list) without using Mabna, on the simulator, in a single sitting.

## Assumptions

- Skip mode is active: Clarifications Session 2026-11-19 records the five recommended answers taken without an owner interview. The owner may later change the as-of rule (period end vs last sync), the candidate cheque-type mix, or the signed-credit convention; until then production stays "data unavailable" without an accounting-confirmed formula.
- Dependencies: Phase 1 sync, purchase metrics, data-mode, Overview; Phase 2 vendor directory/profile (without balance), reports, print, ی/ک search; Phase 3 roles, vendor-financial permission, audit, data-quality notice; Phase 4 purchasing assistant constraints; Phase 5 export, saved reports, generated tables; `deploy/mabna-sim/` and `specs/002-mabna-accounting-connector/onsite-checklist.md`.
- Phase 1–5 specs are not edited by this feature. Behavior they already specified stays; this spec only fills reserved financial holes.
- Three-sample-month real reconciliation, LAN HTTPS, backups and desktop Journey 1 sign-off belong to Phase 7 (F18).
- Customer receivables, cash received, sales revenue, profitability and inventory valuation remain out of this release.

## Out of Scope

- Phases 1–5 and Phase 7; their pull requests and specs are not reopened. Existing purchase sync, purchasing reports, roles, AI purchasing Q&A, saved purchasing reports and Excel export stay as specified in `specs/003`–`specs/007`.
- Customer receivables, cash received, and sales-vs-collections (Release 2 / spec 002 leftovers).
- Profitability, price-difference-as-profit, and inventory valuation (Releases 2–3).
- Adding vendor balance to vendor comparison (F7) or to the Overview KPI tiles; Overview remains purchasing-only in Release 1.
- Scheduled or emailed reports; shared saved reports.
- Sales, pharmacy, warehouse and inventory modules on real data.
- Sales / Warehouse / Viewer roles.
- Custom PDF pipeline (browser-print only).
- Merging the phase pull request (operator merges after UAT).
- Remote/VPN access, native mobile, PWA, English UI, Platform Assistant, MCP catalog, AI anomaly engine, Metabase, Wren AI.
- Any write to Mabna, live per-request queries to Mabna, and reading `HOST_ID()`-filtered report views on live Mabna.
- Deferred items F19–F33 as listed in `zharfa-prd.phases.md`.
