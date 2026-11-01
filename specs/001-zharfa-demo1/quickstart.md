# Quickstart: Zharfa Demo 1 validation

**Feature**: specs/001-zharfa-demo1 | **Date**: 2026-10-30 | **Phase**: 1

Validates the five Demo 1 observable checks (PRD §4). References: [contracts/](contracts/api.md), [data-model.md](data-model.md), [research.md](research.md). No implementation code here — scenarios only. Steps marked [DOCKER-HOST] or [WINDOWS-HOST] need that host or CI (plan-around per locked decision); the rest runs anywhere the four validation_commands run.

## Prerequisites

- `deploy/.env` filled (POSTGRES_PASSWORD + AI provider values); `docker compose` available [DOCKER-HOST].
- Seed data present: multi-month sales, returns/discounts, healthy/low/out-of-stock states, varied balances.

## Scenarios

### 1. Sales overview + filters (covers checks 1–2) — US1

1. Open the Persian app (demonstration-data label visible; Jalali dates; Persian numbers).
2. Select current Jalali month + a category → totals, MoM delta, trend, top-5 products/customers match `GET /api/sales/overview` for the same params.
3. Open supporting rows → match `GET /api/sales/lines`.
4. Change month + category → every number and row updates consistently (zero mismatch across dashboard/tables).

### 2. Inventory + receivables (covers checks 1–2) — US2/US3

1. View alerts → low-stock (single configured threshold) + out-of-stock lists match `GET /api/inventory/alerts`; batch appears flag-only.
2. View receivables → totals, top-5 debtors, payments-received match `GET /api/receivables`; supporting rows match `/rows`.
3. Confirm sales revenue vs cash received never equated in any view.

### 3. AI answers (covers check 3) — US4 [needs provider configured]

1. Ask one Persian sales question + one Persian debt/low-stock question.
2. Each answer cites period + filters + source view and matches dashboard figures for the same params, or explicitly states data unavailable. Zero invented figures; facts separate from interpretation.

### 4. RBAC + secrets (covers check 4) — US5

1. `GET /api/admin/provider` as manager/anonymous → denied (403/401); as admin → name/endpoint/model only, never key.
2. Grep frontend bundle, repo, logs for key material → zero hits.
3. Direct-API admin call as manager → denied server-side.

#### Direct-API negative authz verification (T025)
- Anonymous: `curl -i -s http://localhost:5080/api/admin/provider` → `401 Unauthorized` (`{"message":"احراز هویت انجام نشده است یا نشست منقضی شده است."}`)
- Manager cookie: `curl -i -s -b "zharfa_session=$MANAGER_COOKIE" http://localhost:5080/api/admin/provider` → `403 Forbidden` (`{"message":"شما دسترسی لازم برای انجام این عملیات را ندارید."}`)
- Admin cookie: `curl -i -s -b "zharfa_session=$ADMIN_COOKIE" http://localhost:5080/api/admin/provider` → `200 OK` (`{"name":"...","endpoint":"...","model":"...","configured":true|false}`)
- Key absence verification: `curl -s -b "zharfa_session=$ADMIN_COOKIE" http://localhost:5080/api/admin/provider | grep -i "apikey\|api_key"` → zero hits (no key material exposed).

### 5. Clean install + reconciliation + print (covers check 5) — US6 [DOCKER-HOST]

1. From clean state run README Compose install → full flow works, no Mabna access, no manual DB edits.
2. `GET /api/admin/reconciliation` → `match:true`; force a mismatch in staging → sign-off blocked + AuditLog entry.
3. Browser-print a dashboard page → readable Persian report.
4. Run documented `pg_dump`/restore once → success.

### 6. Desktop shell [WINDOWS-HOST]

1. Set `ZHARFA_FRONTEND_URL` to the Compose web service, launch shell → same Persian app, no shell-side logic.

## Expected outcomes

All five checks hold; any reconciliation mismatch blocks sign-off; every AI answer is cited or explicitly unavailable.
