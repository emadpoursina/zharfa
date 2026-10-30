# Zharfa — Demo 1 PRD (Demo-Sized Rewrite)

**Project:** Zharfa
**Document version:** 2.0 Demo 1
**Status:** Demo scope — overrides MVP scope for Demo 1 only
**Supersedes for Demo 1:** `PRD.md` v1.1 §12 MVP Scope + §14 Acceptance Criteria
**Full vision retained in:** `PRD.md` (v1.1, reference only — not Demo 1 scope)
**Scope:** Single demo path on synthetic data. No Mabna integration in Demo 1.

---

## 1. Demo Sentence (Step 1 — the only Demo 1 goal)

A manager opens the Persian Windows app loaded with demo data, sees this month vs last month sales, low-stock items, and customer debts, then asks one question in Persian and gets an answer backed by those same numbers.

Non-technical recognition test: manager can point at the screen and say "sales up/down, what is low, who owes us, and the assistant answered with our numbers."

Everything that is not required to make that sentence true is deferred. See §10.

---

## 2. Shortest Path (Step 2 — every step required, in order)

1. Synthetic PostgreSQL database with sales invoices, products, customers, inventory movements, receipts. No Mabna connection.
2. Backend API (ASP.NET Core) serving 3 read-only metric sets: sales overview, inventory alerts, receivables. All calculations from approved views only.
3. Custom React + TypeScript dashboard (no Metabase in Demo 1) rendering those 3 sets with Persian RTL, Jalali dates, date-range + single-category filter.
4. Backend-enforced RBAC: Manager (view) + Admin (users/config). All authz checks server-side.
5. One AI Business Analyst agent on one cloud API provider behind a provider interface. Read-only tools over the same 3 metric sets. No Platform Assistant in Demo 1.
6. Windows WebView2 shell hosting the same web frontend. Responsive layout, desktop-first.
7. Docker Compose deployment with documented install/restore steps.

Explicitly single-path choices (no dual paths in Demo 1):
- Dashboard renderer: **custom React only**. Metabase evaluation deferred.
- AI agent: **Business Analyst only**. Platform Assistant deferred.
- AI provider implementation: **one cloud API provider**. Local Ollama interface seam preserved, implementation deferred.
- Export: **browser-print only**. No custom PDF engine, no Excel exporter in Demo 1.

---

## 3. Demo Scope — In

### 3.1 Dashboard (3 categories only)

**Sales overview:**
- Total sales this month, sales trend, this-month vs last-month comparison, top 5 products, top 5 customers.

**Inventory alerts:**
- Low-stock list (single threshold), out-of-stock list. No batch/expiry drill-down in Demo 1 (flag only if batch data present, no separate expiry workflow).

**Receivables:**
- Total customer receivables, top debtors list, payments received this month.

**Dashboard capabilities (Demo 1 only):**
- Select date range (month presets + custom).
- Filter by product category (single select).
- View supporting rows behind each metric (read-only table).
- Compare current vs previous period (month only).

Out: purchases, suppliers, payables, purchase trends, full rankings beyond top 5, drill-down beyond supporting rows, multi-dimension filtering.

### 3.2 Reports

Same 3 metric sets as read-only tables behind the dashboard. No separate reporting module in Demo 1. Browser-print is the export.

### 3.3 AI Business Analyst (sole agent)

Persian natural-language questions over the 3 metric sets only. Example:
- میزان فروش این ماه چقدر بوده؟
- فروش این ماه نسبت به ماه قبل چقدر تغییر کرده؟
- پرفروش‌ترین محصولات کدام هستند؟
- کدام مشتریان بیشترین بدهی را دارند؟
- چه محصولاتی موجودی کمی دارند؟

Capabilities:
1. Answer using approved tools/views only.
2. Compare this month vs last month.
3. Explain a dashboard metric with its figures.
4. State reporting period and filters used in every answer.
5. Say "data unavailable/incomplete" instead of guessing. No invented figures.

No: insights engine, anomaly detection, suggestions, NL-to-arbitrary-SQL. If text-to-SQL is prototyped, it is restricted to approved read-only views with limits/timeouts — never arbitrary tables or writes.

### 3.4 Platform

- Persian RTL interface (RTL layout, Persian labels, Jalali dates, currency/number formatting). English deferred.
- Responsive frontend (desktop-first, adapts to smaller screens, touch-friendly controls where trivial). No mobile packaging, no PWA install, no push, no offline, no biometric in Demo 1.
- Windows WebView2 shell hosting shared frontend. No Windows-only UI logic.
- Backend API independent of desktop shell. No direct DB access from any client.
- Synthetic data clearly labeled as demonstration data.
- Docker Compose self-hosted deployment.

---

## 4. Demo-Ready Stopping Point (Step 3 — 5 observable checks)

Demo 1 passes iff all 5 hold on the documented Docker install with synthetic data:

1. Manager opens Windows app, sees sales (this vs last month), low/out-of-stock list, and receivables/top debtors, all in Persian with Jalali dates.
2. Manager changes month and category filter; numbers and supporting rows update consistently (same KPI definition in dashboard, tables, and AI answers).
3. Manager asks one Persian sales question and one Persian debt/low-stock question; both answers cite the period/filters and match dashboard figures, or explicitly state data is unavailable.
4. Admin configures the single AI provider (endpoint/key/model) from server-side settings; secrets never appear in frontend code or logs; a non-manager role cannot access admin functions even by direct API call.
5. Full flow runs from clean Docker Compose install per the README with no Mabna access and no manual DB edits, and browser-print produces a readable Persian report.

All other v1.1 acceptance items (§14 items 7, 9-partial, 10-partial, 12–15 beyond the above) move to pilot validation, not Demo 1 DoD.

---

## 5. Data Architecture (minimal for Demo 1)

Own PostgreSQL database, independent of Mabna. Mabna remains source of truth for operations; platform never writes to it (and in Demo 1 does not connect to it at all).

Minimal entities: Product, ProductCategory, Customer, SalesInvoice + SalesInvoiceItem, Warehouse, StockMovement (+ ProductBatch flag only), Receipt/Payment (receivables side only), User, Role, AuditLog.

Layers kept but thinned:
- Staging: skipped in Demo 1 (no source to stage). Schema reserves `SourceRecord/SyncJob/SyncLog` tables empty for later.
- Canonical: minimal entities above.
- Semantic: KPI definitions for the 3 metric sets only (sales total, MoM delta, top 5s, low/out-of-stock, receivables total, top debtors). Dashboard and AI use the same views.
- Application: users/roles, AI provider config, dashboard config, audit log.

Financial integrity (Demo 1): document the 3 formulas used (sales revenue, cash received, receivables balance); never equate sales revenue with cash received. Full accounting reconciliation (purchases, payables, inventory valuation, profit) deferred.

Synthetic data must cover: multiple months of history, returns/discounts at least representatively, varied stock states, varied balances. Labeled as demo data everywhere.

---

## 6. Technology Stack (Demo 1 locked)

| Component | Demo 1 choice | Note |
|---|---|---|
| Frontend | React + TypeScript, Tailwind or equivalent | Shared responsive app, RTL |
| Desktop host | WebView2 + .NET | Thin shell only |
| Backend | ASP.NET Core Web API | All authz + calculations server-side |
| Platform DB | PostgreSQL | Synthetic + app data |
| Dashboard | Custom React | Metabase deferred |
| AI engine | Custom backend agent service, one cloud API provider | Provider interface preserved; Wren AI evaluation deferred; local Ollama implementation deferred |
| AI protocol | Direct controlled tools; MCP only if trivial reuse | No requirement to MCP-ify every function |
| Deploy | Docker Compose | Backend, frontend, DB, reverse proxy |

No legacy SQL Server connector in Demo 1. No PWA/Capacitor packaging. Frontend holds no secrets, no authz authority, no direct DB drivers.

---

## 7. Minimal Safeguards (Step 5 — shipped in Demo 1, not phased late)

Each has a minimal implementation in Demo 1:

1. **Auth/authz boundary:** Backend-enforced Manager vs Admin roles. Frontend hiding is not enforcement. Verified by direct-API negative test (Check 4).
2. **Secrets handling:** AI keys, DB credentials in server env / Docker secrets only. Never in frontend bundle, repo, or logs.
3. **Read-only analytics:** AI and dashboard read only approved views. Query allowlist + limits + timeouts. No writes, no arbitrary SQL, no operational tables.
4. **Privacy:** No customer PII beyond synthetic demo data. No sensitive data sent to cloud provider beyond the demo dataset; local-model path documented as the future private option. Real company data is explicitly out of Demo 1.
5. **Data integrity:** One reconciliation check ships: sales total + receivables total from API match the same query run directly against the DB views; mismatch blocks demo sign-off and is logged. Full reconciliation suite deferred.
6. **Traceability:** Every AI answer cites period + filters + source view; unavailable/incomplete data disclosed; facts separated from interpretation; financial calculations traceable to §5 KPI definitions.
7. **Backup/recovery:** Documented PostgreSQL backup + restore procedure validated once before sign-off. Full monitoring/production hardening deferred to pilot.

---

## 8. Priority Reconciliation (fixes v1.1 contradictions)

v1.1 labeled local **and** cloud AI both "Must," Platform Assistant "Should" but required it in acceptance #7, MCP "Should" but required it in #9. Demo 1 resolves:

- **P0 (Demo 1 DoD):** Persian RTL dashboard (3 categories), month+category filter, supporting rows, Business Analyst (one agent, one provider), Manager/Admin RBAC, backend API, synthetic PostgreSQL, Docker install, browser-print, §7 safeguards, one reconciliation check.
- **Should → deferred to pilot:** Platform Assistant, MCP-ification beyond direct tools, Excel export, AI insights/anomaly summaries, full drill-down, purchases/suppliers/payables, expiry-batch workflow, second AI provider implementation, Metabase/Wren evaluations.
- **Later:** Mabna SQL Server connector + ETL + incremental sync, production hardening/monitoring, PWA/Capacitor, push/offline/biometric, predictive forecasting.

---

## 9. Out of Scope (carried from v1.1, still excluded)

No replacing Mabna, no editing/creating operational transactions, no writes to legacy DB, no ERP build-out, no automatic accounting decisions or transactions, no auto-actions without human approval, no native mobile apps, no push/offline/full mobile parity, no forecasting without validated history.

---

## 10. Deferred (Post-Demo) — named, not implied (Step 4)

1. Platform Assistant agent.
2. Second AI provider implementation (local Ollama) + provider-selection UI beyond single config.
3. Purchases, suppliers, payables modules.
4. Full product/customer/supplier reports + full rankings + advanced filtering/search/sort.
5. Expiry-batch workflow, multi-warehouse depth, returns/discounts full handling.
6. Excel export + custom RTL PDF pipeline (browser-print suffices for Demo 1).
7. MCP tool catalog beyond Demo 1 direct tools.
8. AI insights/anomaly detection/suggestions engine.
9. Metabase evaluation; Wren AI evaluation; NL-to-SQL beyond locked views.
10. Mabna integration: inspection, schema mapping, connector, ETL, incremental/scheduled sync, full reconciliation suite.
11. Production hardening: monitoring, backup SLAs, MFA/remote-access design, PWA/Capacitor, push, offline, biometric.
12. English language support.

---

## 11. Roadmap (Demo 1 slices of v1.1 phases)

- Phases 1–2: scope to §3 + §5 only (one dashboard renderer PoC, KPI definitions for 3 sets, synthetic data for 3 sets).
- Phase 3: dashboard + supporting rows + browser-print only.
- Phase 4: one agent + one provider + controlled tools + §7 safeguards.
- Phase 5: demo per §4 checks 1–5 + feedback capture for requirements discovery.
- Phases 6–8 (Mabna, production, mobile): not started; Demo 1 must not block them architecturally (shared frontend, backend API independence, empty sync tables reserved).

---

## 12. Risks / Open Questions (Demo 1 relevance)

Retained: unknown legacy schema/tech (no impact on Demo 1 — deferred by design); unclear management requirements (Demo 1 is the discovery vehicle); financial-formula validation (Demo 1 limits to 3 documented formulas); Metabase RTL and Wren AI fit (deferred, no longer Demo 1 risks); cloud-data exposure (mitigated: synthetic data only); local-AI hardware (deferred); WebView2 packaging (validate early — remains a Demo 1 risk); remote/mobile security (deferred to pilot).

Resolve after Demo 1: actual tables/relationships, company accounting formulas, batch/expiry recording, returns handling, balance-calculation rules, warehouse count, sync frequency, self-host hardware, post-demo report wishlist, remote/mobile needs.

---

## 13. Success Metrics (pilot, not Demo 1 DoD)

Demo 1 success = §4 checks 1–5 only. Business-value indicators (time-to-report, single-interface adoption, reconciliation rate, questions answered, manual-work reduction, satisfaction) get numeric targets after observing real workflow in pilot.

---

*End of Demo 1 PRD. Governs Demo 1 build and sign-off. Conflicts with v1.1 resolve in favor of this document for Demo 1.*
