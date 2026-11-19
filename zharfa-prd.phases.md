# Phase plan: Zharfa — Release 1 (Vendor & Purchasing Analytics)

**PRD:** ./zharfa-prd.md
**Date:** 2026-11-16
**MVP complete after:** Phase 7

## Phase 1: Real purchase data

**Goal:** An Admin syncs real purchasing data from Mabna, and a Manager sees reconciled purchase totals on the Overview instead of demo numbers.
**Scope:**
- F1 — all
- F2 — all
- F3 — all
- F4 — all
**Exit criteria:**
- [ ] Purchase invoice, purchase return, vendor, and product tables are verified on-site and recorded (confirmed, corrected, or unknown) before any formula uses them
- [ ] Admin runs "Sync now" against the simulator; the Data sync page shows the job with status, duration, and row counts per table
- [ ] A forced sync failure keeps the previous data, marks the run failed, and shows the error
- [ ] The nightly schedule runs an incremental sync without Admin action
- [ ] Overview shows net purchases, gross, returns, VAT, previous-period comparison, monthly trend, active vendors, and top 5 vendors and products for a chosen Jalali month, with the last-sync time
- [ ] API purchase totals equal the direct-source query for at least one month and one filter on the simulator, and for one real month on-site
- [ ] A production-mode install shows no synthetic data or Demo 1 pages; a demo-mode install labels everything as demonstration data
- [ ] An attempted non-SELECT statement or a non-allowlisted table is refused by the connector before reaching the server
**Out of scope:** Vendor and purchasing report pages (Phase 2), new roles (Phase 3; Demo 1's Manager/Admin apply until then), AI over real data (Phase 4), vendor financials (Phase 6).
**Spec:** specs/003-real-purchase-data

## Phase 2: Vendor and purchasing reports

**Goal:** Managers and purchasing staff find any vendor and analyze purchases by vendor, product, category, and period, down to the documents.
**Scope:**
- F5 — part: AC1–AC2 (directory, search, profile without balance)
- F6 — all
- F7 — all
- F8 — all
**Exit criteria:**
- [ ] Searching a vendor name with Arabic ي/ك finds the Persian ی/ک spelling and vice versa
- [ ] A vendor profile shows net purchases, trend, top products, returns, and documents for the selected period, matching the Overview figures for that vendor
- [ ] Reports by vendor, product, and category, the monthly trend, most-purchased products, returns, and transaction history all open, filter, sort, search, and paginate
- [ ] Clicking a report row shows the purchase documents and lines behind it, and their sum equals the row
- [ ] Comparing three vendors over six months shows all five measures per vendor; selecting six vendors is refused with the limit stated
- [ ] Period comparison: presets compare with the previous Jalali month, quarter, or year; a custom range compares with the same number of days just before it
- [ ] Browser-print of a report yields a readable Persian RTL page stating period and filters
**Out of scope:** Vendor balance on the profile (Phase 6), role-specific visibility (Phase 3), export and saved reports (Phase 5).
**Spec:** specs/004-vendor-purchasing-reports

## Phase 3: Permissions, audit, and data quality

**Goal:** Each user sees only what their role allows, important actions are recorded, and the Admin can see data problems before they mislead anyone.
**Scope:**
- F9 — all
- F10 — all
- F11 — all
**Exit criteria:**
- [ ] Roles Admin, Management, Purchasing, and Finance exist; Admin assigns them to users
- [ ] A Purchasing user calling a vendor-financial or admin API directly gets 403, and the attempt is logged
- [ ] A role change takes effect on the user's next request
- [ ] Sign-in, report opens, admin changes, and permission changes appear in the audit log, filterable by user, action, and date, with no secrets or result rows
- [ ] After a sync with seeded bad records in the simulator, Data quality lists each issue type with count, examples, and affected reports
- [ ] A report that depends on flagged records shows a data-quality notice
**Out of scope:** Export and AI audit events (added by Phases 4–5 when those actions exist), Sales/Warehouse/Viewer roles (Releases 2–3).
**Spec:** specs/005-permissions-audit-quality

## Phase 4: Local AI over purchasing

**Goal:** A Manager asks purchasing questions in Persian and gets cited answers from a model running on the company's own server.
**Scope:**
- F12 — all
- F13 — all
**Exit criteria:**
- [ ] The local model provider answers through the existing provider interface and is the default
- [ ] The benchmark runs the fixed Persian purchasing question set on two to three candidate models and records accuracy and response time; the default model is chosen from it
- [ ] On the 20-question Persian test set, every answer is correct and states period, filters, and source metric, or says "data unavailable"
- [ ] A Purchasing user asking about vendor balances gets no financial figures
- [ ] Enabling the cloud provider requires an owner-approval note, and a captured request shows only aggregated results
- [ ] With the AI provider stopped, the assistant reports it and dashboards still work
- [ ] AI questions appear in the audit log; provider keys never appear in the frontend bundle, API responses, or logs
**Out of scope:** Tables and charts in answers and saving them (Phase 5), sales and inventory questions (Releases 2–3).
**Spec:** specs/006-local-ai-purchasing

## Phase 5: AI reports, saved reports, and Excel export

**Goal:** Users turn questions into tables and charts, save any report for later, and take report tables into Excel.
**Scope:**
- F14 — all
- F15 — all
- F16 — all
**Exit criteria:**
- [ ] "Compare the top 10 purchased products over the last six months" returns a table and a chart, each labeled with period, filters, and metrics, matching the purchasing reports
- [ ] A generated report and a standard report can be saved, then reopened on current data
- [ ] A saved report whose permission was removed is refused with a reason
- [ ] Export of a filtered report produces an .xlsx with Persian headers, RTL sheet, period, filters, export time, and last-sync time
- [ ] An export is audit-logged; a user without export permission gets 403
**Out of scope:** Vendor financial exports beyond the permission rules (Phase 6 adds the data), scheduled or emailed reports.
**Spec:** specs/007-ai-reports-export

## Phase 6: Vendor financials

**Goal:** Finance and Management see what the company owes each vendor, its payments, and purchases vs payments, reconciled with Mabna.
**Scope:**
- F17 — all
- F5 — part: AC3 (vendor balance on the profile)
**Exit criteria:**
- [ ] Vendor balance, payable cheque, and payment source tables are verified on-site before any formula uses them, and the balance formula is confirmed by accounting
- [ ] Financials show balance, outstanding payments, payment history, and purchases vs payments per vendor for a chosen period
- [ ] Vendor balance totals equal the direct-source query on the simulator and for one real month on-site
- [ ] A figure whose source is unverified shows "data unavailable"
- [ ] Purchasing users cannot see financials in pages, exports, saved reports, or AI answers
**Out of scope:** Customer receivables and cash received (Release 2), profitability and inventory valuation (Releases 2–3).
**Spec:** specs/008-vendor-financials

## Phase 7: Go-live on the LAN server

**Goal:** The company uses Zharfa daily on its own LAN server, with backups and a signed-off reconciliation on real data.
**Scope:**
- F18 — all
**Exit criteria:**
- [ ] Following the README on the company LAN server starts database, API, frontend, and proxy with Docker Compose and serves HTTPS on the LAN
- [ ] The service is not reachable from outside the LAN
- [ ] The daily backup runs and a restore to a clean instance is tested once
- [ ] Purchase totals (F2) and vendor balances (F17) reconcile with Mabna for three sample months on real data
- [ ] The desktop shell on an office PC signs in and runs Journey 1 end to end
- [ ] Go-live checklist signed: secrets only in server env, cloud AI off unless approved, timed vendor report under 1 minute
**Out of scope:** Remote or VPN access, monitoring beyond the sync page and logs, Release 2 work.
**Spec:** none

## Deferred

| Item | Reason |
|------|--------|
| F19 Sales analytics | Release 2; sales schema partly verified (spec 002), planned after Release 1 go-live |
| F20 Pharmacy analysis | Release 2 |
| F21 Sales financials | Release 2; takes over spec 002's receivables and cash-received reconciliation stories |
| F22 Vendor–sales cross-domain reports | Release 2; needs sales data |
| F23 AI agent — sales and cross-domain | Release 2 |
| F24 Sales role | Release 2; no sales data to protect in Release 1 |
| F25 Inventory analysis | Release 3; batch, warehouse, and costing data unverified |
| F26 Warehouse operations | Release 3 |
| F27 Inventory risk | Release 3; expiry data unverified |
| F28 Full cross-domain reports | Release 3; needs all three domains |
| F29 AI agent — warehouse and full cross-domain | Release 3 |
| F30 Warehouse and Viewer roles | Release 3 |
| F31 AI pattern detection | Future; needs validated history across domains |
| F32 Remote access | Future; Release 1 is LAN-only by decision, needs MFA and security design |
| F33 Mobile/PWA, English UI, Platform Assistant | Future vision carried from PRD v1.1 |
