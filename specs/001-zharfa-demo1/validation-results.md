# Validation Record: Zharfa Demo 1

**Feature**: specs/001-zharfa-demo1  
**Date**: 2026-10-30  
**Host Environment**: Linux (no Docker daemon available, no Windows host — plan-around per plan.md)  
**Execution Topology**:
- Backend API executed via `dotnet run --project backend/Zharfa.Api`
- Database: PGlite PostgreSQL 18.6 over TCP on `127.0.0.1:54330`
- AI Provider: Local OpenAI-compatible mock on `127.0.0.1:8099` with key `[redacted-mock-key]` for scenarios requiring a configured provider

> **Note on timing and envelope sanitization**:
> Recorded before the T030-T032 polish pass; error-envelope sanitization (F2) applied after this run — scenarios 1-6 do not exercise the sanitized paths.

---

## Scenario Validation Results

### Scenario 1: Sales overview + filters (US1, checks 1–2)

- **Step 1 (Persian app visible)**: PASS at build + API level; visual browser check NOT RUN on this host (browser harness unavailable — see closing note). Evidence: `demoData:true` on every response; period `jalaliLabel` «۱۴۰۵/۰۷/۰۹ تا ۱۴۰۵/۰۸/۰۹», `prev` «۱۴۰۵/۰۶/۰۹ تا ۱۴۰۵/۰۷/۰۸» (2026-08-31 -> 2026-10-01); frontend build ✓.
- **Step 2 (current month + category)**: PASS. `GET /api/sales/overview?from=2026-10-01&to=2026-11-01` -> 200, totals.revenue `"134985000.00"` (= independent base-table SQL sum and `SUM(v_sales_lines)`, verified twice earlier), prevRevenue `"134975000.00"`, momDelta `"10000.00"`, momPct `"0.01"`, trend 31 zero-filled daily buckets, topProducts 5, topCustomers 5, view `"v_sales_overview"`. With `categoryId=1` -> 200, revenue `"23910000.00"` (subset of total), filters `{categoryId:1}` echoed.
- **Step 3 (supporting rows)**: PASS. `GET /api/sales/lines` -> 200, 96 lines, truncated:false, view `"v_sales_lines"`, contract keys `{invoiceId,customer,date,lineDiscount,net,product,qty,unitPrice}`.
- **Step 4 (month change updates everything)**: PASS. Sep window (2026-09-01..2026-10-01) -> revenue `"130785000.00"` (differs from Oct), label «۱۴۰۵/۰۶/۱۰ تا ۱۴۰۵/۰۷/۰۸», 30 trend buckets, topProducts 5.

---

### Scenario 2: Inventory + receivables (US2/US3, checks 1–2)

- **Step 1 (alerts & threshold)**: PASS. `GET /api/inventory/alerts` -> threshold 10, lowStock 6, outOfStock 4 (exact bands from the 73-check independent SQL harness), each row `{productId,name,category,warehouse,onHand,hasBatch}` — hasBatch flag-only, view `"v_inventory_alerts"`, demoData:true. Config override re-proven: `Inventory__LowStockThreshold=3` -> threshold 3, 5 rows (1 low + 4 out).
- **Step 2 (receivables totals & rows)**: PASS. `GET /api/receivables?from=2026-10-01&to=2026-11-01` -> totals.receivables `"2150440000.00"`, totals.paymentsReceived `"538500000.00"` — both equal independent base-table SQL; topDebtors 5. `GET /api/receivables/rows` -> 25 rows (all customers), truncated:false, view `"v_receivable_rows"`; per-row formula `balance = invoiced_net + opening_balance - receipts` holds for all 25 rows (SQL harness).
- **Step 3 (financial formula discipline)**: PASS. revenue `"134985000.00"` vs cash received `"538500000.00"` — distinct in every API response and in code; never equated.

---

### Scenario 3: AI answers (US4, check 3) [mock provider, disclosed]

- **Steps 1–2 (tool calling & unavailable paths)**: PASS.
  - S4 full tool-calling loop: mock returned tool_calls[sales_overview] -> API executed the allowlisted tool -> 200, view `"v_sales_overview"`, unavailable:false, 13 SERVER-BUILT facts each citing period + «(مرجع: v_sales_overview)»; headline fact «درآمد فروش ۱۴۰۵/۰۷/۰۹ تا ۱۴۰۵/۰۸/۰۹: ۱۳۴٬۹۸۵٬۰۰۰ ریال» == dashboard figure; top-product fact ۹٬۶۶۰٬۰۰۰ ریال == dashboard; interpretation kept separate from facts.
  - S5 non-allowlisted tool (`drop_database`) -> not executed, 200 unavailable:true, facts [], «دادهای در دسترس نیست؛ پاسخ بدون مرجع داده ارائه نشد.» — zero invented figures.
  - S1 unconfigured -> 503 «پیکربندی نشده است» unavailable:true, facts [].
  - S2 name=ollama -> 503 same unavailable path (seam exists, never implemented).
  - S3 unreachable endpoint -> 503 «خطا در برقراری ارتباط شبکه…(network)»; key `[redacted-mock-key]` absent from response AND API log (grep 0).
  - S6 empty question -> 400 Persian.
  - S0 anonymous -> 401 Persian.

---

### Scenario 4: RBAC + secrets (US5, check 4)

- **Step 1 (direct-API negative authz matrix)**: PASS. `GET /api/admin/provider` anonymous -> 401 «احراز هویت انجام نشده است یا نشست منقضی شده است.»; manager -> 403 «شما دسترسی لازم برای انجام این عملیات را ندارید.»; admin -> 200 `{"name":"mockcloud","endpoint":"http://127.0.0.1:8099/v1","model":"mock-model","configured":true}` — key grep 0.
- **Step 2 (secret scanning)**: PASS. key `[redacted-mock-key]` greps: frontend/dist bundle 0, repo (excl. harness/test dirs) 0, API log 0.
- **Step 3 (server-side enforcement & audit log)**: PASS. Direct-API manager call denied server-side 403. `audit_log` rows (append-only, trigger-enforced): `provider.status.viewed` x4, `reconciliation.run` x3, `reconciliation.mismatch` x1 (+1 from the fresh mismatch re-run below) = 9 rows total, all actor=admin, details carry no key material.

---

### Scenario 5: Clean install + reconciliation + print (US6, check 5) [partially DOCKER-HOST]

- **Step 1 (clean Compose install)**: NOT RUN on this host — no Docker daemon (locked plan-around).
  - *Host command to run on Docker host*: `docker compose -f deploy/docker-compose.yml up --build -d` followed by checking health endpoint `curl http://localhost:5080/api/health`.
  - *PARTIAL EVIDENCE on this host*: migration runner against an EMPTY database applied 0001+0002+both seeds at API boot ("Database migration complete. Total files applied: 4."), then the incremental path applied 0003_categories.sql on next boot, then an idempotent no-op ("Seed skipped: app_user table already contains records.") — no manual DB edits at any point; the container image now ships db/ (backend/Dockerfile:15 COPY db/ /app/db/, verified T028).
- **Step 2 (reconciliation)**: PASS. `GET /api/admin/reconciliation` (admin) -> match:true, apiTotals == viewTotals == `{sales:"134985000.00", receivables:"2150440000.00"}` (both equal independent base-table SQL), signOffBlocked:false; manager -> 403, anonymous -> 401. FORCED MISMATCH (test DB's v_sales_overview overridden +100, then restored): match:false, viewTotals.sales `"134985100.00"` vs apiTotals.sales `"134985000.00"`, signOffBlocked:true, audit row `reconciliation.mismatch` with ranges + all four totals; restored -> match:true again, test DB left consistent.
- **Step 3 (browser-print)**: NOT RUN on this host — browser harness unavailable (unix-socket unsupported on this host's mounted profile dir); no Chromium.
  - *Host command / manual verification*: Open `http://localhost:5173`, authenticate, navigate to dashboard, trigger print dialog via `window.print()` / چاپ button, verify A4 portrait formatting.
  - *Evidence on this host*: Print CSS implemented + build-verified + code-reviewed (T027): A4 portrait `@page`, RTL white/black forced, shadows/gradients stripped, interactive chrome hidden, demo-data banner KEPT visible with print styling, print header «زارفا — گزارش داشبورد» + Jalali period, thead `{display:table-header-group}`, break-inside:avoid, 10pt+ fonts, grayscale borders, trend svg rect bars preserved via `print-color-adjust:exact`. Visual check deferred to a capable host.
- **Step 4 (pg_dump/restore once)**: NOT RUN on this host — no Docker.
  - *Host command to run on Docker host*: Commands documented in `deploy/README.md` (T028):
    ```bash
    docker compose exec db pg_dump -U zharfa zharfa > backup.sql
    docker compose exec -T db psql -U zharfa zharfa < backup.sql
    ```
    Must be run and verified once on a Docker host before Demo 1 sign-off (FR-018).

---

### Scenario 6: Desktop shell [WINDOWS-HOST]

- **Step 1 (desktop shell execution)**: NOT RUN on this host.
  - *Host command to run on Windows host*: Set `ZHARFA_FRONTEND_URL` to the web service URL, compile and launch `desktop/Zharfa.Desktop/bin/.../Zharfa.Desktop.exe`.
  - *Evidence on this host*: Compile-only verified: desktop build 0 Warning(s)/0 Error(s) on Linux (`EnableWindowsTargeting`). Shell confirmed logic-free by code review + desktop/README.md note (T029): WebView2 navigates to `ZHARFA_FRONTEND_URL` only; no business logic, no auth, no DB access, no secrets. Windows/WebView2 Runtime run verification deferred to a Windows host or CI.

---

## Closing

PRD §4 observable checks: check 1 (Persian dashboard, Jalali, consistent numbers) — holds on this host at build+API level (visual spot-check pending a capable browser host); check 2 (sales/inventory/receivables match DB views and independent SQL) — holds; check 3 (AI cited answers / unavailable over guessing) — holds (mock provider); check 4 (RBAC + no key material anywhere) — holds; check 5 (clean install, reconciliation, print, backup/restore) — holds in part (reconciliation + migration-runner semantics proven here); clean Compose install, pg_dump/restore, print visual and the Windows shell run await Docker/Windows/CI per the locked plan-around.
Recorded by the build-phase orchestrator before the T030-T032 polish pass; the polish pass sanitizes the error envelope (F2) after this run — scenarios 1-6 above do not exercise the sanitized paths; the orchestrator re-probes those two paths against the rebuilt API after polish.


---

## Re-check 2026-11-01 (macOS host, live Docker stack)

- **Host**: macOS, Docker stack from `deploy/docker-compose.yml` already running (`zharfa-web`, `zharfa-api`, `zharfa-db`). AI provider is the live gateway configured in `deploy/.env` (name `9router`, model `Reasoning`), not the earlier local mock.
- **Health**: `GET http://localhost:8080/api/health` returned `status ok`, `demoData true`.
- **Auth/RBAC**: manager and admin logins returned 200. Anonymous `GET /api/admin/provider` returned 401. Manager `GET /api/admin/provider` returned 403. Admin returned `configured true` with `name 9router` and no key material.
- **Reconciliation** (admin, default range): `match true`, `apiTotals.sales 43785000.00` equal `viewTotals.sales 43785000.00`, `receivables 2150440000.00` both sides, `signOffBlocked false`.
- **Default sales overview period**: 2026-10-23 to 2026-11-02 (Jalali 1405/08/01 to 1405/08/10), revenue `43785000.00`.
- **AI chat**: Persian sales question returned facts with period and view `v_sales_overview`; Persian debt question returned receivables balance `2150440000` and top debtors from `v_receivables`; Persian low-stock question returned counts from `v_inventory_alerts`; an off-topic English question returned `unavailable true` with the Persian data-unavailable message and no facts.
- **Secrets**: the provider API key value (read from `deploy/.env`, not written here) appears 0 times in `docker logs zharfa-api-1` and 0 times in the files served by `zharfa-web`.
- **Backup/restore**: `pg_dump` of `zharfa` produced a 67390-byte dump; restoring into a scratch database `zharfa_restore_test` gave the same counts (101 invoices, 56 receipts, 2 users) in both; the scratch database was dropped afterward.
- **Windows shell (operator-reported)**: the operator ran `Zharfa.Desktop` on a Windows PC against the Mac's stack at `http://192.168.8.100:8080` (LAN, firewall off) and reported it working. Detailed per-check notes were not recorded.
- **Still NOT RUN**: clean Compose install from empty volumes; browser visual check of Persian RTL pages and browser-print output (not confirmed by the operator).
