# Contracts: Zharfa Demo 1 API

**Feature**: specs/001-zharfa-demo1 | **Date**: 2026-10-30 | **Phase**: 1

Base: `/api`. Auth: session cookie from Persian login (seeded demo Manager/Admin); every endpoint except `/api/health` requires auth; admin endpoints require `admin` role server-side (direct-API negative test required). All reads come from the approved KPI views only. Errors are Persian messages; no secret, stack trace or key material ever in responses or logs. Every data response carries `demoData: true`.

## Conventions

- Period: `from`/`to` Gregorian ISO dates (API boundary); UI shows Jalali labels. `categoryId` optional single-select. AI echoes resolved Gregorian range with the Jalali label. Server responses include `prev:{from,to,jalaliLabel}` for the comparative previous-period window.
- Amounts: strings with two decimals in JSON (Persian formatting is UI-side).

## Endpoints

### GET /api/health

- Auth: none. 200 `{ status, component, demoData }`. Exists (Program.cs); unchanged contract.

### POST /api/auth/login — POST /api/auth/logout — GET /api/auth/me

- Login body: `{ username, password }`. 200 `{ username, role }` + session cookie; 401 Persian error otherwise. `me` returns current principal. No registration endpoint in Demo 1.

### GET /api/sales/overview?from=&to=&categoryId=

- Roles: manager, admin. Reads `v_sales_overview`.
- 200: `{ demoData:true, period:{from,to,jalaliLabel,prev:{from,to,jalaliLabel}}, filters:{categoryId}, totals:{revenue, prevRevenue, momDelta, momPct}, trend:[{bucket, revenue}], topProducts:[{productId,name, revenue}≤5], topCustomers:[{customerId,name, revenue}≤5], view:'v_sales_overview' }`. `prev:{from,to,jalaliLabel}` is the previous-period window computed by the server (custom ranges get the same-length preceding window; exact Jalali months get the previous Jalali month).

### GET /api/sales/lines?from=&to=&categoryId=

- Roles: manager, admin. Reads `v_sales_lines`. 200: `{ demoData:true, period:{from,to,jalaliLabel,prev:{from,to,jalaliLabel}}, filters, lines:[{invoiceId, date, customer, product, qty, unitPrice, lineDiscount, net}], view:'v_sales_lines' }`.

### GET /api/meta/categories

- Roles: manager, admin. Reads `v_categories()` (db/migrations/0003_categories.sql).
- Purpose: source for the single-select product-category filter (FR-004) so the filter reads through an approved read-only view rather than a hardcoded list.
- 200: `{ demoData:true, categories:[{categoryId, name}], view:'v_categories' }`.

### GET /api/inventory/alerts?categoryId=

- Roles: manager, admin. Reads `v_inventory_alerts`.
- 200: `{ demoData:true, filters, threshold, lowStock:[{productId,name,category,warehouse,onHand,hasBatch}] , outOfStock:[...], view:'v_inventory_alerts' }`.

### GET /api/receivables?from=&to=

- Roles: manager, admin. Reads `v_receivables` + `v_receivable_rows`.
- 200: `{ demoData:true, period:{from,to,jalaliLabel,prev:{from,to,jalaliLabel}}, totals:{receivables, paymentsReceived}, topDebtors:[{customerId,name,balance}≤5], view:'v_receivables' }`.

### GET /api/receivables/rows?from=&to=

- Supporting rows for FR-005. Same auth/views pattern (`period:{from,to,jalaliLabel,prev:{from,to,jalaliLabel}}`).

### POST /api/ai/chat

- Roles: manager, admin. Body: `{ question, from?, to?, categoryId? }` (Persian question over the 3 metric sets only).
- Behavior: allowlisted tools over the three views only (fixed params, row/time limits, call timeout); unconfigured/unreachable provider → 503 Persian unavailable (no secret leakage).
- 200: `{ demoData:true, answer, period, filters, view, facts:[...], interpretation, unavailable:boolean }` — every answer cites period+filters+source view or states unavailable; facts separate from interpretation; zero invented figures.

### GET /api/admin/provider

- Roles: admin only (manager/direct-anonymous → 403/401, no disclosure). 200: `{ name, endpoint, model, configured:boolean }` — never the key.

### GET /api/admin/reconciliation?from=&to=

- Roles: admin only. Recomputes sales + receivables totals with the same predicates as the views, compares to view totals; 200 `{ match:boolean, apiTotals, viewTotals, at, signOffBlocked:boolean }` (`signOffBlocked`: true when match=false — sign-off is blocked, FR-013); on mismatch also writes AuditLog `reconciliation.mismatch` and sign-off is blocked (FR-013).

### Print/export

- No export endpoint: browser-print over rendered views with print CSS (FR-014).
