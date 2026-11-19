# Changelog

## [Unreleased] - Zharfa Release 1, Phase 5: AI Reports, Saved Reports, and Excel Export
### Added
- Dynamic AI reports (F14): assistant returns a server-built Persian table and optional bar chart (inline SVG) for ranking and comparison purchasing questions; canonical top-10 products over the last six Jalali months matches live reports.
- Saved reports (F15): users can save any standard or generated report as a named query specification (not a row snapshot), list own reports, and reopen them on current synced data. Generated reports require `ai.query` on save and reopen. Cap of 50 saved reports per user with duplicate name overwrite confirmation.
- Excel export (F16): ClosedXML server-built `.xlsx` export for report tables and generated tables, with RTL sheet direction, Persian headers, metadata (period, filters, Jalali export time, last-sync, demo/real label, data quality notice), and 10,000-row cap with truncation warning. Admin does not hold export permission.
- Audit events (F10): `report.exported` on successful Excel exports; `saved_report.saved`, `saved_report.replaced`, `saved_report.opened`, and `saved_report.deleted` on saved report actions; access denied logged without result rows.
- Migration `db/migrations/0010_ai_reports_export.sql`: `saved_report` table with user ownership, unique name keys, kind checks, and JSON specifications.

## [Unreleased] - Zharfa Release 1, Phase 2: Vendor and Purchasing Reports
### Added
- Vendor directory and vendor profile for Managers and Admins: net purchases, last purchase date, search by vendor name or code, a 12-month trend, top products, returns and the documents of one vendor.
- Seven purchasing reports (by vendor, by product, by category, monthly trend, top products, returns, transaction history) with period and previous-period comparison, sorting, paging and the filters each report declares.
- Drill-down from any report row to its documents and their lines, and a vendor comparison of two to five vendors.
- Print view for each report, directory, profile and comparison, limited to 1,000 rows.
- Migration `db/migrations/0007_purchasing_reports.sql`: drug group master (`mabna.product_category`), vendor code, product drug group key, search keys, the `mabna.fn_purchase_lines` range base, and the extended sync fingerprint.
- Source map items 40–46 (vendor code, product drug group key, drug group master) as candidates for on-site verification, recorded in `specs/003-real-purchase-data/source-verification.md` and `specs/002-mabna-accounting-connector/onsite-checklist.md` §7.6.
- Synthetic three-year load generator fills the drug groups, vendor codes and product categories (`db/perf/purchasing-3y.sql`).
### Changed
- `mabna.fn_purchase_line_amounts` is now a wrapper over `mabna.fn_purchase_lines`; Phase 1 Overview figures are unchanged.
- Metric definitions for the reports are in `docs/metrics/purchasing.md` ("Reports").
- Reports never show payments, vendor balance, credit, price differences or profit.

## [Unreleased] - Zharfa Release 1, Phase 1: Real Purchase Data
### Added
- Install data mode `ZHARFA_DATA_MODE` (required, `production` or `demo`). Production shows real Mabna purchasing data only and hides the demo pages and the AI chat; demo labels all data as demonstration data.
- Purchasing Overview for Managers and Admins (`GET /api/overview/purchasing`): net, gross, returns and VAT for a Jalali month, comparison with the previous month, a 12-month trend, active vendors, and the top five vendors and products.
- Read-only purchasing sync from Mabna: a nightly incremental run, a Friday weekly full load, an Admin "sync now" and the Data sync page.
- `reconcile purchasing` command: compares each purchasing total with the same query on the source data; a mismatch blocks sign-off.
- Metric definitions in `docs/metrics/purchasing.md`.
### Changed
- Demo seeds apply in demo mode only. Production accounts come from `Bootstrap__*` on first start.
- Startup refuses an install whose stored data mode differs from `ZHARFA_DATA_MODE`.
- The real source map and allowlist stay outside git (`deploy/mabna-config/`, mounted read-only).

## [0.1.3] - 2026-11-04
### Added
- Link to the live Demo 1 instance (`https://demo.mosaddeghian.com/`) in the proposal's closing section, labeled as synthetic demonstration data

## [0.1.2] - 2026-11-03
### Changed
- Richer visual design for the Persian proposal HTML (hero, cards, timeline, callouts)

## [0.1.1] - 2026-11-03
### Added
- Printable RTL HTML version of the Persian proposal (`docs/پیشنهاد-ژرفا.html`)

## [0.1.0] - 2026-11-03
### Added
- Persian stakeholder proposal for Demo 1 (`docs/پیشنهاد-ژرفا.md`)
