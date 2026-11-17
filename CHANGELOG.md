# Changelog

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
