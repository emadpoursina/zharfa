# Feature Specification: Vendor and Purchasing Reports (Release 1, Phase 2)

**Feature Branch**: `zharfa-prd-phase-2`

**Created**: 2026-11-17

**Status**: Draft

**Input**: User description: "phase: zharfa-prd.phases.md#2"

**Scope authority**: `zharfa-prd.md` v3.0, Phase 2 of `zharfa-prd.phases.md` ("Vendor and purchasing reports"), covering F5 in part (AC1–AC2: vendor directory, search, profile without balance), F6 (purchasing reports), F7 (vendor comparison) and F8 (report capabilities) in full. `AGENTS.md` rules bind every decision. Phase 1 (`specs/003-real-purchase-data`, merged) is the foundation: the read-only sync into the reporting database, the purchase metric definitions, the data-mode split, the Overview and the source verification record.

**Phase goal**: Managers and purchasing staff find any vendor and analyze purchases by vendor, product, category, and period, down to the documents.

## Clarifications

### Carried over from Phase 1 (owner decisions, binding)

- Net purchases follow the discount basis of Mabna's own purchase report total once verified on-site; until verified, amounts are taken after line-level discounts as recorded on each invoice line, excluding VAT. Phase 2 reuses Phase 1's single metric definitions unchanged.
- Reports compare the selected month with the immediately preceding Jalali month and show a 12-month trend.
- The AI agent stays hidden in the UI and refused by the API in production-mode installs until Phase 4.
- Amounts are shown in Rial, labeled "ریال".
- Simulator example columns and objects use `Sim*` names; real Mabna data never enters the repository, and development uses `deploy/mabna-sim/`.

### Assumptions taken in place of questions (no-question run)

- Product category means Mabna's own drug group (and class, if verified) on the product master; no extra categorization is built in this phase (PRD open question "categorization beyond Mabna's drug groups").
- Vendor "code" means the code Mabna shows users for the vendor, verified on-site (it may be the same item as the vendor master key).

### Session 2026-11-17

- Q: What should a printed report contain: only the visible page, or the whole filtered result? → A: The whole filtered result, capped at 1,000 rows, fetched by a print-specific server request that returns all rows up to the cap. Above the cap the printout states that it is truncated (shows the first 1,000 of N rows) and asks the user to narrow the filters.
- Q: Which previous period does period comparison use for quarter and year presets and for custom ranges? → A: (skip mode, recommended option) A calendar preset compares with the previous calendar unit of the same kind: month with the previous Jalali month, quarter with the previous Jalali quarter, year with the previous Jalali year. Only a custom range uses the equal-day rule (the same number of days ending the day before the range starts). **Owner sign-off pending**: this answer was taken in skip mode as the recommended option; the plan builds on it, but the owner has not yet confirmed it (listed in research R18).
- Q: How do Phase 1's source-item status rules apply to the product category source (and other Phase 2 source items still `candidate` or `unknown` in `source-verification.md`)? → A: The same as Phase 1: a `candidate` item is usable in demo mode and the simulator but not in production; in production only `confirmed` or `corrected` items are usable, and anything depending on an unusable item shows "data unavailable" until verified on-site. The simulator never shows "data unavailable" for a `candidate` item.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find a vendor and read its purchasing profile (Priority: P1)

A Manager (and, from Phase 3, a Purchasing Officer) opens Vendors, types part of a vendor's name or code in Persian, and finds the vendor regardless of whether the name was entered with Arabic ي/ك or Persian ی/ک. Opening the vendor shows its profile for the selected Jalali period: net purchases (with gross, returns and VAT shown separately), the comparison with the previous period, a 12-month trend, its top products, its returns and its purchase documents. The vendor balance is not shown in this phase.

**Why this priority**: This is step 3 of the core Release 1 journey and the most direct answer to "how much do we buy from this vendor"; it also proves the shared metric definitions work per vendor.

**Independent Test**: With synced simulator data containing a vendor whose name uses Arabic ي/ك, search with the Persian spelling (and vice versa), open the vendor, choose a month, and confirm every profile figure equals the Overview figure for that vendor and month.

**Acceptance Scenarios**:

1. **Given** a vendor stored with Persian ی/ک, **When** a user searches the directory with Arabic ي/ك (or the reverse), **Then** the vendor is found.
2. **Given** the vendor directory, **When** a user searches by part of a vendor's name or by its code (Persian or Latin digits), **Then** matching vendors are listed with name, code, net purchases for the selected period and last purchase date, sorted and paginated on the server.
3. **Given** a vendor that appears in the Overview's top 5 vendors for a month, **When** a user opens that vendor's profile for the same month, **Then** its net purchases equal the Overview figure for that vendor.
4. **Given** a vendor profile and a selected period, **When** it is shown, **Then** it shows net purchases, gross purchases, returns and VAT as separate figures, the previous-period comparison, the 12-month trend, the top products by net purchases, the vendor's returns, and its purchase documents for the period, together with the last-sync time.
5. **Given** a vendor profile, **When** the user changes the period, **Then** every figure, list and document table on the profile updates to the new period.
6. **Given** any user in this phase, **When** they open a vendor profile, **Then** no vendor balance, payment or other vendor-financial figure is shown or returned by the API.
7. **Given** a vendor with no purchases in the selected period, **When** its profile is opened, **Then** the figures read zero and the document list is empty, not "data unavailable".

---

### User Story 2 - Standard purchasing reports (Priority: P1)

A Purchasing Officer opens Vendors → Purchasing reports and picks one of the standard reports: purchases by vendor, by product, by product category, the monthly purchase trend, most-purchased products, purchase returns, and the purchase transaction history. Each report shows amount and quantity for the selected period, and can be filtered, searched, sorted and paged.

**Why this priority**: These reports replace the fixed printed reports from Mabna (Purchasing persona pain point) and carry most of the phase's value.

**Independent Test**: With synced simulator data, open each of the seven reports, apply a period and a vendor filter, search, sort by each sortable column and move between pages, and confirm totals equal the same computation directly on the synced data.

**Acceptance Scenarios**:

1. **Given** synced data and a selected period, **When** a user opens the purchases-by-vendor, by-product or by-category report, **Then** each row shows net purchase amount and net quantity for that period, with gross, returns and VAT available as separate columns.
2. **Given** the monthly trend report, **When** it is opened for a selected month, **Then** it shows net purchases per Jalali month for the 12 months ending with the selected month.
3. **Given** the most-purchased-products report, **When** it is opened, **Then** products are ranked by net purchase amount by default and can be re-ranked by quantity.
4. **Given** the purchase-returns report, **When** it is opened, **Then** it lists return amounts and quantities by vendor and product for the period, dated by the return document's date.
5. **Given** the transaction-history report, **When** it is opened, **Then** it lists each non-cancelled purchase invoice and return in the period with document number, type, Jalali date, vendor, gross, discount, VAT and net amount.
6. **Given** any of the seven reports, **When** the user filters, searches, sorts or changes page, **Then** results come from the server already filtered, sorted and paged, and the total row count and report totals reflect the filter, not just the visible page.
7. **Given** each report, **When** it is shown, **Then** it states the period, the active filters and the last-sync time, uses Persian digits, Jalali dates, amounts in Rial labeled "ریال", and an RTL layout.

---

### User Story 3 - Drill down from a report row to the documents (Priority: P1)

From any row of a purchasing report (a vendor, a product, a category, a month, or a returns row), the user opens the purchase documents and lines behind that row, and the sum of those lines equals the row.

**Why this priority**: Drill-down is how users trust a number; the exit criterion ties report rows to source documents.

**Independent Test**: In the by-vendor, by-product, by-category and trend reports, open a row, sum the lines shown and confirm the sum equals the row's amount and quantity.

**Acceptance Scenarios**:

1. **Given** a report row, **When** the user opens it, **Then** the documents and lines behind it are shown with the same period and filters, and each document can be expanded to its lines.
2. **Given** a drill-down, **When** its lines are summed (invoice lines positive, return lines negative), **Then** the sums of net amount and quantity equal the row's values exactly.
3. **Given** a drill-down list, **When** it has more lines than one page, **Then** it is paged on the server and its stated total still covers all pages.
4. **Given** the "unknown" (نامشخص) vendor, product or category row, **When** it is opened, **Then** the lines with missing or unmatched references are listed.

---

### User Story 4 - Compare vendors side by side (Priority: P2)

A Manager selects two to five vendors and a period and sees, for each vendor in one view, net purchases, quantity, number of documents, returns and the monthly trend.

**Why this priority**: Step 4 of the core journey; it builds on the per-vendor figures from User Stories 1 and 2.

**Independent Test**: Select three vendors over a six-month range and confirm all five measures appear per vendor and match each vendor's profile for the same range; then try to add a sixth vendor and confirm it is refused with the limit stated.

**Acceptance Scenarios**:

1. **Given** three vendors and a six-month period, **When** the user runs the comparison, **Then** the view shows net purchases, quantity, number of documents, returns and a monthly trend for each of the three vendors.
2. **Given** five vendors already selected, **When** the user tries to add a sixth (in the UI or by calling the API directly), **Then** the selection is refused and the message states that at most five vendors can be compared.
3. **Given** fewer than two vendors selected, **When** the user tries to run the comparison, **Then** it does not run and the message states that at least two vendors are needed.
4. **Given** a comparison, **When** each vendor's figures are checked against that vendor's profile for the same period, **Then** they are equal.

---

### User Story 5 - Consistent filters, period comparison and printing (Priority: P2)

Every Phase 2 report and the vendor profile share one filter bar: Jalali date range (month, quarter and year presets and a custom range), vendor, product and product category as applicable. The user can switch on period comparison to see the selected period next to the previous period (the previous Jalali month, quarter or year for a preset; the same number of days just before a custom range), and can print any report from the browser or desktop shell as a readable Persian RTL document holding the whole filtered result (up to 1,000 rows) and stating the period and filters.

**Why this priority**: These capabilities make every report behave the same way; they are required by the exit criteria but add little on their own.

**Independent Test**: On a report, pick a month, a quarter and a year preset and a custom 45-day range with period comparison on, confirm the previous periods shown, then print a filtered result of more than one page and fewer than 1,000 rows and inspect the printout, and print an unfiltered result of more than 1,000 rows and confirm the truncation notice.

**Acceptance Scenarios**:

1. **Given** a selected Jalali month, quarter or year preset with period comparison on, **When** the report is shown, **Then** it shows the selected period next to the immediately preceding Jalali month, quarter or year respectively, with the difference and percentage change.
2. **Given** a custom Jalali range of N days with period comparison on, **When** the report is shown, **Then** the previous period is the N days ending the day before the range starts, and both ranges are stated.
3. **Given** a report with filters applied, **When** the user prints it from the browser or desktop shell, **Then** the printout is Persian RTL, readable without clipped columns, holds every row of the filtered result (not only the visible page) up to 1,000 rows, with the report totals, and states the report name, period (and comparison period if on), every active filter, the last-sync time, and the data label (real company data or demonstration data).
4. **Given** a filtered result of more than 1,000 rows, **When** the user prints it, **Then** the printout holds the first 1,000 rows in the current sort order, states that it is truncated (1,000 of N rows), asks the user to narrow the filters, and its report totals still cover the full filtered result.
5. **Given** a filter that does not apply to a report (for example the vendor filter on the vendor comparison, whose vendors are chosen directly), **When** the report is shown, **Then** only applicable filters are offered.

---

### Edge Cases

- A search term mixes Arabic and Persian letters, has extra spaces or a zero-width non-joiner: it matches the same vendors as the normalized spelling.
- A search term matches no vendor: the directory shows an empty state, not an error.
- Two vendors have the same name after normalization: both are listed, distinguished by code.
- A purchase return is dated in a different period from its invoice: it counts in the period of the return's own date (Phase 1 rule), in every report and drill-down.
- Cancelled documents are excluded from every report, profile, comparison and drill-down.
- A line with no vendor, product or category, or one not found in the master: it counts in totals and appears under an explicit "نامشخص" (unknown) row, never silently dropped.
- The product category source is not yet verified on-site (status `candidate` or `unknown`): in a production-mode install the by-category report and category filter show "data unavailable" rather than guessed categories, and other reports still work; in demo mode and on the simulator a `candidate` category source is used normally.
- A filtered result exceeds the 1,000-row print cap: the printout holds the first 1,000 rows, states it is truncated and asks the user to narrow the filters; its totals still cover the full result.
- Net quantity across different products and units: quantities are summed as recorded; the report does not convert units and states that quantities are in each product's recorded unit.
- The selected period has no purchases: figures show zero; the previous-period comparison does not show a percentage against a zero base.
- The previous period lies before the first synced data: the comparison shows "data unavailable" for the missing base.
- The 12-month trend reaches before the first synced month: only months with data are shown, and missing months are marked as without data rather than zero.
- A custom range whose end is before its start, or that extends into the future: the range is refused with a message, or trimmed to today and stated.
- A Jalali leap year (Esfand with 30 days) and month boundaries in ranges and presets are correct.
- The same vendor is selected twice in a comparison: it counts once.
- A page number beyond the last page (for example after a filter change): the user is moved to the last available page.
- No successful sync yet: every report shows "data unavailable" and the last-sync indicator says no sync has completed.
- A sync completes while a user is viewing a report: the open report keeps its figures until refreshed; the next load uses the new data and shows the new last-sync time.
- A production-mode user or client calls an AI endpoint during this phase: the server refuses it (unchanged from Phase 1).

## Requirements *(mandatory)*

### Functional Requirements

**Shared rules**

- **FR-001**: Every Phase 2 figure MUST be computed on the server from the synced reporting copy only; no report MAY query Mabna per request, and no client MAY talk to a database directly.
- **FR-002**: Every Phase 2 figure MUST use Phase 1's single metric definitions (net purchases, gross purchases, returns, VAT, discount basis, return dating, cancelled exclusion) unchanged, so the same vendor, product and period give the same value on the Overview, a profile, a report, a comparison and a drill-down.
- **FR-003**: Purchases MUST NOT be presented as payments or vendor balance, and price differences MUST NOT be presented as profit.
- **FR-004**: A figure that cannot be computed from synced or verified data MUST show "data unavailable" instead of a number.
- **FR-005**: Every report page and the vendor profile MUST show the last-sync time and the install's data label (real company data in production; demonstration data in demo mode).
- **FR-006**: All Phase 2 pages MUST follow Persian-first conventions: RTL layout, Jalali dates, Persian digits, amounts in Rial labeled "ریال", and Persian text search.
- **FR-007**: Phase 2 pages and their APIs MUST be available to signed-in users of Demo 1's Manager and Admin roles, enforced on the server; role-specific visibility (Purchasing, Finance) arrives with Phase 3.
- **FR-008**: Every new source item Phase 2 uses (product category and vendor code) MUST be recorded in the source verification record with a Phase 1 status (`unknown`, `candidate`, `confirmed`, `corrected`) and follow Phase 1's usability rule: in production mode only `confirmed` or `corrected` items are usable; in demo mode (simulator) `candidate` items are also usable. Anything depending on an unusable item MUST show "data unavailable". The simulator MUST be extended with synthetic category data using `Sim*` names.

**Vendor directory and profile (F5 AC1–AC2)**

- **FR-009**: The vendor directory MUST list all synced vendors with name, code, net purchases for the selected period and last purchase date, with server-side search, sorting and pagination.
- **FR-010**: Vendor search MUST match by any part of the name or the code, treat Arabic ي/ك and Persian ی/ک as equal, ignore differences in spaces and zero-width non-joiners, and accept Persian, Arabic or Latin digits in codes.
- **FR-011**: The vendor profile MUST show, for the selected period, net purchases, gross purchases, returns and VAT as separate figures, the previous-period comparison, a 12-month trend ending with the selected period's last month, the vendor's top products by net purchases, its returns, and its purchase documents (paged), each document openable to its lines.
- **FR-012**: The vendor profile MUST NOT show or return any vendor balance, payment or other vendor-financial figure in this phase.

**Purchasing reports (F6)**

- **FR-013**: The system MUST provide reports of purchases by vendor, by product and by product category, each with net amount and net quantity for the selected period, and gross, returns and VAT as separate columns.
- **FR-014**: The system MUST provide a purchase trend report by Jalali month (12 months ending with the selected month by default, or the months of a selected custom range), a most-purchased-products report (ranked by net amount by default, re-rankable by quantity), a purchase-returns report, and a purchase transaction history of non-cancelled invoices and returns.
- **FR-015**: Opening a row in any purchasing report MUST show the purchase documents and lines behind that row under the same period and filters; the sum of those lines (invoices positive, returns negative) MUST equal the row's amount and quantity.

**Vendor comparison (F7)**

- **FR-016**: When a user selects two to five distinct vendors and a period, the system MUST show for each vendor, in one view, net purchases, net quantity, number of documents, returns and a monthly trend.
- **FR-017**: A selection of more than five vendors MUST be refused by the server and the UI, with the limit stated; fewer than two MUST not run, with the minimum stated.

**Report capabilities (F8)**

- **FR-018**: Every Phase 2 report and the vendor profile MUST be filterable by Jalali date range (month, quarter and year presets and a custom range; default the current Jalali month), and by vendor, product and product category where applicable to the report.
- **FR-019**: Every report table MUST support text search, column sorting and server-side pagination; the stated row count and report totals MUST cover the full filtered result, not only the visible page. This includes the purchase trend report, with no exemption: its search matches vendor name or code, or product name, and restricts the purchases summed into each month; its month rows are paged like any other report.
- **FR-020**: Period comparison applies to the vendor directory, the vendor profile (always on), and the by-vendor, by-product, by-category, trend, most-purchased-products and purchase-returns reports. It does not apply to the purchase transaction history (a list of individual documents with no previous-period counterpart), to drill-down document lists, or to the vendor comparison page (whose vendors are compared with each other over one period); those views do not offer the comparison toggle and the server refuses the comparison parameter for them. When period comparison is selected, the system MUST show the selected period next to its previous period: for a month, quarter or year preset, the immediately preceding Jalali month, quarter or year respectively; for a custom range only, the same number of days ending the day before the range starts. It MUST show the absolute difference and, where the base is non-zero, the percentage change, and state both periods.
- **FR-021**: Printing any Phase 2 report from the browser or the desktop shell MUST produce a readable Persian RTL printout that holds the whole filtered result in the current sort order, up to 1,000 rows, with report totals covering the full filtered result, and states the report name, period (and comparison period if selected), every active filter, the last-sync time and the data label, without navigation chrome and without clipped columns. The rows MUST come from a print-specific server request that applies the same filters, sort and authorization as the report and returns all rows up to the 1,000-row cap plus the total row count. When the filtered result exceeds 1,000 rows, the printout MUST state that it is truncated (1,000 of N rows) and ask the user to narrow the filters.
- **FR-022**: Report and profile responses MUST meet the PRD performance target (p95 under 2 seconds for three years of synced data).
- **FR-023**: Reports MUST be fully usable by keyboard and meet WCAG 2.1 AA contrast.

### Key Entities

- **Vendor** (from Phase 1): source key, code, normalized Persian name; now also searched and profiled.
- **Product** (from Phase 1): source key, normalized Persian name, product category reference.
- **Product category**: Mabna's drug group (and class, if verified) attached to a product; its source carries a Phase 1 status and is usable in production only once `confirmed` or `corrected` (in demo mode also when `candidate`); products without one fall under "نامشخص".
- **Purchase document and purchase line** (from Phase 1): the records every report row drills down to.
- **Report filter**: period (month, quarter or year preset, or custom Jalali range), optional comparison period, vendor, product, category, search text, sort and page; stated on screen and on print. A print request uses the same filter and sort without paging, capped at 1,000 rows.
- **Vendor comparison**: two to five distinct vendors plus a period, yielding five measures per vendor.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of vendors whose names contain ی/ک are found when searched with ي/ك, and vice versa, on the simulator dataset.
- **SC-002**: For every vendor in a chosen month's Overview top 5, the profile's net purchases equal the Overview figure exactly.
- **SC-003**: All seven purchasing reports open, filter, sort, search and paginate; their totals equal the same computation run directly on the synced data for at least one month and one filter.
- **SC-004**: For every drill-down tested (at least one row each in the by-vendor, by-product, by-category, trend and returns reports), the sum of the lines shown equals the row exactly.
- **SC-005**: A three-vendor, six-month comparison shows all five measures per vendor; a six-vendor selection is refused 100% of the time, in the UI and through the API, with the limit stated.
- **SC-006**: Period comparison shows the correct previous period in 100% of tested cases: the previous Jalali month for a month preset, the previous Jalali quarter for a quarter preset, the previous Jalali year for a year preset (including a year after a leap year), and the same number of days ending the day before for a custom range (tested with a 45-day range).
- **SC-007**: A printed report fits a standard A4 page width in RTL, states its period and filters, and holds every row of a filtered result of up to 1,000 rows; a result above 1,000 rows prints exactly 1,000 rows with the truncation notice.
- **SC-008**: A purchasing officer produces a vendor purchase report in under 1 minute; report pages respond within 2 seconds with three years of data.

## Assumptions

- **Metric definitions**: Phase 1's definitions are reused unchanged (net = invoices − returns, excluding VAT, by document date, discount basis per Mabna's verified report total, fallback after line discounts). If the on-site basis changes, every Phase 2 figure follows automatically.
- **Product category**: Mabna's own drug group (and class, if verified) on the product master; no extra categorization in this phase. Phase 1's status rules apply: in production the by-category report and category filter show "data unavailable" until the category source is `confirmed` or `corrected` on-site; in demo mode and on the simulator a `candidate` source is used.
- **Vendor code**: the code Mabna shows users for the vendor, verified on-site under the same status rules; where it is not usable, the directory searches by name only and shows the code as "data unavailable".
- **Periods**: default period is the current Jalali month; presets are Jalali month, quarter and year; custom ranges are by Jalali day. Comparison for a preset is with the immediately preceding Jalali month, quarter or year respectively; only a custom range uses the same number of days ending the day before it starts (skip-mode decision; owner sign-off pending). Trends show 12 Jalali months ending with the selected month (owner decision).
- **Quantity**: net quantity = invoice quantity − return quantity, in each product's recorded unit, without unit conversion.
- **Most-purchased products**: ranked by net amount by default (consistent with the Overview top 5), re-rankable by quantity.
- **Pagination**: default page size 25 rows, user-selectable 25/50/100.
- **Printing**: browser-print (browser or desktop shell) of the whole filtered result, up to 1,000 rows, loaded by a print-specific server request, with a print layout; above the cap the printout states it is truncated and asks the user to narrow the filters. No PDF pipeline (Release 1 out of scope).
- **Roles**: Demo 1's Manager and Admin roles apply until Phase 3; no Phase 2 page shows vendor financials to anyone.
- **AI**: hidden in the UI and refused by the API in production mode until Phase 4 (unchanged).
- **Currency**: Rial labeled "ریال"; no Toman display.
- **Dependencies**: Phase 1 sync, reporting tables, metric definitions, Overview and source verification record (`specs/003-real-purchase-data`); the `deploy/mabna-sim/` simulator with `Sim*` objects; Demo 1 authentication, Persian formatting and Jalali helpers; on-site verification of product category and vendor code items.

## Out of Scope

- Vendor balance on the profile and all vendor financials (Phase 6: F5 AC3, F17).
- Role-specific visibility, new roles Management/Purchasing/Finance, audit of report opens, and the Data quality page (Phase 3: F9, F10, F11).
- AI over real data and the local model provider (Phase 4: F12, F13).
- AI dynamic reports, saved reports and Excel export (Phase 5: F14, F15, F16).
- LAN production deployment (Phase 7: F18).
- Custom PDF generation; product categorization beyond Mabna's own drug groups/classes; unit conversion of quantities.
- Sales, pharmacy, receivables, warehouse and inventory reports and cross-domain reports (Releases 2–3).
- Any write to Mabna, live per-request queries to Mabna, and reading `HOST_ID()`-filtered report views on live Mabna.
