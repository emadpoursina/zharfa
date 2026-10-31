# Tasks: Zharfa Demo 1 — Single-Path Analytics Demo

**Input**: Design documents from `/specs/001-zharfa-demo1/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: No test framework is mandated by the PRD; no test tasks are generated (Demo 1 verification is the quickstart scenarios + tester validation_commands + required negative/reconciliation checks embedded below as implementation tasks).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [X] T001 Add Npgsql EF Core data-access packages to backend/Zharfa.Api/Zharfa.Api.csproj
- [X] T002 Wire migration runner into API startup in backend/Zharfa.Api/Program.cs (forward-only db/migrations, no manual DB edits)
- [X] T003 [P] Scaffold Persian login + demo-data banner shell in frontend/src/pages/ + frontend/src/components/ per plan.md structure

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T004 Create canonical + reserved tables migration db/migrations/0001_init_canonical.sql (Product, ProductCategory, Customer, SalesInvoice, SalesInvoiceItem with qty <> 0 CHECK and line-discount CHECKs, Warehouse, StockMovement, Receipt with amount > 0 CHECK, User/Role, AuditLog append-only, empty SourceRecord/SyncJob/SyncLog)
- [X] T005 [P] Create KPI views migration db/migrations/0002_kpi_views.sql (v_sales_overview + v_sales_lines with sales_revenue formula comment, v_inventory_alerts with threshold comment, v_receivables + v_receivable_rows with cash_received/receivables_balance comments)
- [X] T006 Seed synthetic multi-month demo data in db/seed/ (sales across ≥3 months with returns/discounts, healthy/low/out-of-stock states, varied balances + opening_balance, seeded manager+admin users with hashed passwords, demonstration-data labeling note)
- [X] T007 Implement Persian session auth + server-side Manager/Admin enforcement in backend/Zharfa.Api/Controllers/AuthController.cs + auth middleware (seeded accounts, hashed passwords, no self-registration)
- [X] T008 Implement Persian error envelope + no-secret logging guard in backend/Zharfa.Api/Program.cs (no secret/stack/key material in responses or logs)
- [X] T009 Implement Jalali period mapping + single-category predicate helpers in backend/Zharfa.Api/Analytics/PeriodFilters.cs (Jalali month presets + custom range → Gregorian range; MoM = selected Jalali month vs previous)

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - View sales overview in Persian (Priority: P1) 🎯 MVP

**Goal**: Manager sees this-vs-last-month sales, trend, top 5s with consistent filters and supporting rows.

**Independent Test**: Seed multi-month data; select month + category; totals/MoM/trend/top-5s and supporting rows match v_sales_overview/v_sales_lines; Persian RTL with Jalali dates and demo-data label.

- [X] T010 [P] [US1] Implement sales KPI service over approved views in backend/Zharfa.Api/Analytics/SalesAnalytics.cs (sales_revenue = SUM(qty*unit_price-line_discount), MoM, trend buckets, top 5 products/customers; never equate with cash)
- [X] T011 [US1] Implement sales endpoints in backend/Zharfa.Api/Controllers/SalesController.cs (GET /api/sales/overview + GET /api/sales/lines per contracts/api.md; manager/admin only; demoData:true)
- [X] T012 [US1] Build sales dashboard page + filters in frontend/src/pages/Dashboard.tsx (month presets + custom range + single category; every number + row updates consistently)
- [X] T013 [US1] Build sales metric cards + supporting tables in frontend/src/components/SalesCards.tsx (Persian RTL, Jalali dates, Persian numbers/currency, demo-data label)

**Checkpoint**: At this point, User Story 1 should be fully functional and testable independently

---

## Phase 4: User Story 2 - View inventory alerts in Persian (Priority: P1)

**Goal**: Manager sees low-stock (single server-side threshold, documented default) and out-of-stock lists with supporting rows; batch flag only.

**Independent Test**: Seed healthy/low/out-of-stock states; both lists + counts + rows match v_inventory_alerts and update with category filter.

- [X] T014 [P] [US2] Implement inventory KPI service in backend/Zharfa.Api/Analytics/InventoryAnalytics.cs (single server-side low_stock_threshold with documented default; low vs out-of-stock split; batch flag only)
- [X] T015 [US2] Implement inventory endpoint in backend/Zharfa.Api/Controllers/InventoryController.cs (GET /api/inventory/alerts per contracts/api.md; manager/admin only)
- [X] T016 [US2] Build inventory alerts UI in frontend/src/components/InventoryAlerts.tsx (two lists, counts, supporting rows, Persian RTL + demo-data label)

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently

---

## Phase 5: User Story 3 - View receivables in Persian (Priority: P1)

**Goal**: Manager sees total receivables, top 5 debtors, payments received this month with supporting rows; revenue never equated with cash.

**Independent Test**: Seed varied balances/receipts; totals + top-5 debtors + payments match v_receivables and update with filters.

- [X] T017 [P] [US3] Implement receivables KPI service in backend/Zharfa.Api/Analytics/ReceivablesAnalytics.cs (cash_received = SUM(receipts) in range; receivables_balance = posted net + opening_balance − receipts; top 5 debtors)
- [X] T018 [US3] Implement receivables endpoints in backend/Zharfa.Api/Controllers/ReceivablesController.cs (GET /api/receivables + GET /api/receivables/rows per contracts/api.md; manager/admin only)
- [X] T019 [US3] Build receivables UI in frontend/src/components/Receivables.tsx (totals, top debtors, payments received, supporting rows, Persian RTL + demo-data label)

**Checkpoint**: All P1 dashboard stories now independently functional

---

## Phase 6: User Story 4 - Ask the AI Business Analyst in Persian (Priority: P1)

**Goal**: One Persian sales + one debt/low-stock question answered with period+filters+view citations matching dashboard figures, or explicit unavailable.

**Independent Test**: Ask both questions against seed; answers cite period/filters/view, match dashboard for same params, or state unavailable; zero invented figures.

- [X] T020 [P] [US4] Implement provider interface + one cloud HTTP implementation + Ollama seam in backend/Zharfa.Api/Ai/ (IAiProvider.cs, CloudAiProvider.cs from AiProvider__* env, OllamaAiProvider.cs unimplemented stub; redacted status only)
- [X] T021 [US4] Implement three allowlisted read-only AI tools in backend/Zharfa.Api/Ai/AnalystTools.cs (sales/inventory/receivables over approved views, fixed params, row/time limits, call timeout; no writes/arbitrary SQL)
- [X] T022 [US4] Implement AI chat endpoint in backend/Zharfa.Api/Controllers/AiController.cs (POST /api/ai/chat per contracts/api.md; cited answers, facts-vs-interpretation split, unavailable path, 503 when provider down)
- [X] T023 [US4] Build Persian AI chat UI in frontend/src/components/AiChat.tsx (question box, cited answers with period/filters/view, unavailable states, demo-data label)

---

## Phase 7: User Story 5 - Admin configures AI provider; roles enforced server-side (Priority: P2)

**Goal**: Env-only provider config; admin-only redacted status; non-manager direct-API access denied (check 4 gate).

**Independent Test**: Manager/anonymous admin calls denied; admin sees name/endpoint/model only; zero secrets in bundle/repo/logs.

- [X] T024 [US5] Implement admin endpoints in backend/Zharfa.Api/Controllers/AdminController.cs (GET /api/admin/provider redacted status, admin-only; no in-app provider editing in Demo 1)
- [X] T025 [US5] Implement direct-API negative authz verification task artifact backend/Zharfa.Api/Controllers/AdminController.cs (manager/anonymous → 403/401 on admin endpoints; document in quickstart scenario 4)

---

## Phase 8: User Story 6 - Clean install, reconciliation, browser-print (Priority: P2)

**Goal**: Documented clean Compose install; API-vs-view reconciliation blocks sign-off on mismatch; readable Persian browser-print; validated backup/restore.

**Independent Test**: Clean Docker-host install works with no Mabna/manual edits; reconciliation match:true (mismatch → blocked + AuditLog); print readable; backup/restore run once.

- [X] T026 [P] [US6] Implement reconciliation check in backend/Zharfa.Api/Analytics/Reconciliation.cs + GET /api/admin/reconciliation (same-predicate API vs view totals; mismatch → AuditLog reconciliation.mismatch + blocked sign-off)
- [X] T027 [US6] Add print CSS for Persian RTL report in frontend/src/index.css + views (readable browser-print; demo-data label prints)
- [X] T028 [US6] Extend deploy/README.md install/restore docs (runner behavior, seed accounts, provider env, ZHARFA_FRONTEND_URL, backup/restore validated-once note; no new services)
- [X] T029 [US6] Confirm desktop shell hosts deployed frontend via ZHARFA_FRONTEND_URL with no shell-side logic in desktop/Zharfa.Desktop/MainWindow.xaml.cs (compile-only on Linux; run verified on Windows host)

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [X] T030 [P] AuditLog coverage for admin/config + reconciliation events in backend/Zharfa.Api/Data/AuditLog.cs path (reconciliation.run/mismatch, provider.status.viewed)
- [X] T031 Persian UX consistency pass across frontend/src (RTL, Jalali, Persian numbers/currency, demo-data labels on every data view, empty-state Persian copy)
- [X] T032 Run quickstart.md validation scenarios 1-6 and record results (Docker/Windows-host steps on capable host or CI)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3+)**: All depend on Foundational phase completion
  - User stories can then proceed in parallel (if staffed)
  - Or sequentially in priority order (P1 → P2)
- **Polish (Final Phase)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories
- **User Story 2 (P1)**: Can start after Foundational (Phase 2) - May integrate with US1 but should be independently testable
- **User Story 3 (P1)**: Can start after Foundational (Phase 2) - May integrate with US1/US2 but should be independently testable
- **User Story 4 (P1)**: Can start after Foundational (Phase 2) - Reads the same three views; independently testable via POST /api/ai/chat
- **User Story 5 (P2)**: Can start after Foundational (Phase 2) - Auth/session foundation required; admin endpoints independent
- **User Story 6 (P2)**: Can start after Foundational (Phase 2) - Reconciliation needs views + seed; docs/shell independent

### Within Each User Story

- Models before services
- Services before endpoints
- Core implementation before integration
- Story complete before moving to next priority

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel
- All Foundational tasks marked [P] can run in parallel (within Phase 2)
- Once Foundational phase completes, all user stories can start in parallel (if team capacity allows)
- T010/T014/T017/T020 service + tool tasks touch different files and can run in parallel
- Different user stories can be worked on in parallel by different team members

---

## Parallel Example: User Story 1

```bash
# Launch all models for User Story 1 together:
Task: "Implement sales KPI service over approved views in backend/Zharfa.Api/Analytics/SalesAnalytics.cs"
Task: "Implement sales endpoints in backend/Zharfa.Api/Controllers/SalesController.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: Test User Story 1 independently
5. Deploy/demo if ready

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 → Test independently → Deploy/Demo (MVP!)
3. Add User Story 2 → Test independently → Deploy/Demo
4. Add User Story 3 → Test independently → Deploy/Demo
5. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: User Story 1
   - Developer B: User Story 2
   - Developer C: User Story 3
3. Stories complete and integrate independently

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Verify tests fail before implementing
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Avoid: vague tasks, same file conflicts, cross-story dependencies that break independence

---

## Phase 10: Convergence

- [X] T033 [C1] Remove the unused `v_inventory_rows` function via a new forward-only migration `db/migrations/0004_drop_inventory_rows.sql` (`DROP FUNCTION IF EXISTS v_inventory_rows(bigint, numeric)`); do NOT edit already-tracked 0002; reconcile db/README's KPI-entry-point wording with the resulting `v_*` function set (plan: data-model semantic-view set) (unrequested)
- [X] T034 [C2] Record the additive category-filter source in specs/001-zharfa-demo1/contracts/api.md: `GET /api/meta/categories` and the `v_categories()` function behind it (db/migrations/0003_categories.sql) — justify-and-document the build-time addition that keeps FR-004's single-select filter reading through an approved read-only view (FR-004) (unrequested)
- [X] T035 [C3] Align contracts/api.md response schemas with shipped, mandated additive fields: sales/receivables `period.prev:{from,to,jalaliLabel}` (locked decision of this build) and reconciliation `signOffBlocked` (T026-mandated), matching the actual API responses byte-for-byte (contracts/api.md) (partial)
