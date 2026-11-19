---
description: "Task list for Local AI over Purchasing (Release 1, Phase 4)"
---

# Tasks: Local AI over Purchasing (Release 1, Phase 4)

**Input**: Design documents from `specs/006-local-ai-purchasing/` — [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/api.md](contracts/api.md), [contracts/tools.md](contracts/tools.md), [contracts/benchmark.md](contracts/benchmark.md), [quickstart.md](quickstart.md), [fixtures/purchasing-questions.json](fixtures/purchasing-questions.json)

**Phase ref**: `zharfa-prd.phases.md#4` · Governing PRD `zharfa-prd.md` v3.0 (F12, F13, F10 AI events) · Rules `AGENTS.md` · Foundation Phases 1–3 (`specs/003-real-purchase-data`, `specs/004-vendor-purchasing-reports`, `specs/005-permissions-audit-quality`) and Demo 1 `IAiProvider`

**Tests**: Included. Plan **Testing** and research R12–R15 plus LOOP_BACK R21–R25 require pure xunit suites in `backend/Zharfa.Api.Tests` (no database, no network, no new packages): payload redaction, tool allowlist, **question router / scope gate**, period default including future-empty month, cloud-flag rules, in-flight gate, DemoOnly/permission matrix, no-financial DTO/JSON, **503 `code=assistant-unavailable` vs 200 metric-unavailable**. HTTP, models and UI are proven by [quickstart.md](quickstart.md) Flows A–H (Compose + simulator + pinned 7–8B local models + 9router overlay for Flow E), run by the tester, not by workers. Workers do not run installs or validation commands (AGENTS.md).

**Organization**: Setup (migration + env), then the shared AI seam (Phase 2), then one phase per user story in spec order with P1 before P2 (US1 → US2 → US3 → US4 → US6 → US5 → US7), then polish, then **Phase 16 plan revision** (`unpark-plan-1`) for remaining LOOP_BACK work. **T001–T080 stay done** — do not re-open them unless a named file below must be edited to close R21–R25.

**Revision**: `unpark-plan-1` (tester LOOP_BACK). SC-001 stays **20/20**. Remaining work: `PurchasingQuestionRouter` / server-dispatched tools / 503 vs 200 / pinned 7–8B trio / 9router capture / Q13 out-of-domain.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: User story from spec.md (US1–US7)
- Every task names its exact file path(s)

## Path Conventions

Web application (existing layout): `backend/Zharfa.Api/`, `backend/Zharfa.Api.Tests/`, `frontend/src/`, `db/migrations/`, `deploy/`. New backend types live under `backend/Zharfa.Api/Ai/` (namespace `Zharfa.Api.Ai`) except controllers. Tests mirror them under `backend/Zharfa.Api.Tests/Ai/` (namespace `Zharfa.Api.Tests.Ai`). **No new NuGet or npm packages.** Inference stays an external Ollama (or OpenAI-compatible local) process.

## Hard rules for every task

- Authorization is decided on the server only. Purchasing chat is `[Authorize(Policy = Permissions.AiQuery)]` **without** `[DemoOnly]`. Demo 1 chat stays `[DemoOnly]` + `ai.query`. Settings → AI is `[Authorize(Policy = Permissions.Admin)]` **without** `[DemoOnly]`. The SPA hiding a tab is cosmetic.
- AI answers only through allowlisted read-only purchasing tools. Never invent SQL, never query Mabna, never write, never register vendor-financial tools. Caps: 100 result rows per tool, 10 s per tool, 30 s for the user-visible answer, at most 3 purchasing tool calls per question.
- Cite period, filters and source metric, or «داده در دسترس نیست». Empty valid period is **zero**, not unavailable. Provider down/timeout/unconfigured/liveness-fail is **503** `{ "code": "assistant-unavailable" }` with the assistant-unavailable phrasing and empty facts — never a partial guess. Metric/out-of-domain/no-sync is **200** `unavailable=true` with «داده در دسترس نیست» and **must not** use that code.
- Out-of-domain (sales/pharmacy/inventory/warehouse), vendor-financials, and Mabna-bypass are a **server scope gate** (`PurchasingQuestionRouter`) **before** tools or `CompleteAsync`. Q13 must never execute `purchasing_overview`. In-domain questions **server-dispatch** the mapped tool; the model receives **no** tool specs.
- Facts vs interpretation: server-built `facts[]` only from tool JSON; model text is `interpretation` and MUST NOT introduce extra numbers. No tables/charts/export in this phase.
- Purchases ≠ payments ≠ vendor balance; a price difference is never profit.
- Keys and credentials stay in server env / Docker secrets (`AiProvider__ApiKey`). Never in the frontend bundle, repo, API JSON, audit `detail`/`target`, or `ILogger` messages. Status endpoints never include `apiKey`. Endpoints shown to Admin must strip userinfo and query.
- Cloud off by default (`ai.cloud.enabled` absent or `false`). While enabled, cloud is the **sole** answering provider; failure MUST NOT fall back to local. Enable requires a non-empty trimmed owner-approval note; whitespace-only is missing.
- Each question is independent: messages are `[system, user]` only. Body of `POST /api/ai/chat` is `{ "question" }` only (1–2000 chars). Omitted period → Overview default (current Jalali month).
- At most one in-flight purchasing/demo chat per `user_id`. Second request: 409 `{ "message": "لطفاً صبر کنید تا پاسخ پرسش فعلی آماده شود.", "code": "ai-busy" }`.
- No chat store. Audit `ai.question` target is the question text cut to 200 characters (existing `AuditLogger` cut). Detail allowlist: `period`, `from`, `to`, `vendor`, `product`, `category`, `tools`, `outcome`. Never tool JSON, facts, amounts, keys or full prompts.
- Real and synthetic never mix. Purchasing `dataLabel` comes from `DataModeOptions`. Demo 1 stays on `/api/ai/demo/chat`. Production never reads Demo 1 KPI views.
- Persian-first: RTL, Jalali, Persian digits, ریال, Persian messages copied from contracts/api.md. Keyboard use and WCAG 2.1 AA contrast on new pages.
- Do not commit, push, or run `npm install` / `dotnet test`; the tester runs `.ainative/project.yaml` `validation_commands`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Migration `0009`, LocalAi configuration shape, and env/compose passthrough every later phase builds on. No answering behavior yet.

- [X] T001 Create `db/migrations/0009_local_ai_purchasing.sql` (forward-only, idempotent; do not edit `0001`–`0008`): `INSERT INTO install_setting (key, value, set_at) SELECT 'ai.cloud.enabled', 'false', now() WHERE NOT EXISTS (SELECT 1 FROM install_setting WHERE key = 'ai.cloud.enabled');`. Do not insert `ai.cloud.approval_note` / `approved_by` / `approved_at`. Do not change `mabna.*`, `audit_log`, or the append-only trigger.
- [X] T002 [P] Add the LocalAi configuration section (no secrets) to `backend/Zharfa.Api/appsettings.json`: `LocalAi` with `Name` default `"ollama"`, `Endpoint` `""`, `Model` `""`. Do not add `ApiKey` under `LocalAi`. Keep existing `AiProvider` keys unchanged.
- [X] T003 [P] Extend `deploy/.env.example` and the api service environment in `deploy/docker-compose.yml` with `LocalAi__Name`, `LocalAi__Endpoint`, `LocalAi__Model` (empty model until the benchmark names a winner — empty model = unconfigured, research R20). Keep `AiProvider__*` as secrets; never copy a key into the example as a real value.
- [X] T004 [P] Create `deploy/docker-compose.ollama.yml` as an **optional** compose file/profile `ollama` documenting a sibling Ollama for the LAN server (research R16). Default `deploy/docker-compose.yml` must still start without pulling a model.
- [X] T005 [P] Add `aiSettings: boolean` to `SystemModules` in `frontend/src/services/api.ts` (contracts/api.md §0 / §6). Do not add assistant routes yet.

**Checkpoint**: migration and env exist; the API still selects a provider by `AiProvider:Name` (old Demo 1 wiring).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared OpenAI-compatible HTTP client, real local provider, cloud-flag resolver, install_setting settings store, in-flight gate, audit catalog, modules, Demo 1 chat split, DI. **No user story work until this phase is complete.**

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Tests for the foundation (write first; they must fail until the code exists)

- [X] T006 [P] Create `backend/Zharfa.Api.Tests/Ai/ActiveAiProviderTests.cs`: given a fake local and fake cloud `IAiProvider`, `ActiveAiProvider` with `ai.cloud.enabled` false/absent always calls local `CompleteAsync` and reports `answeringProvider=local`; with true always calls cloud and never local even when cloud throws `AiException`; `GetStatus` never includes an apiKey field. Pure fakes, no HTTP.
- [X] T007 [P] Create `backend/Zharfa.Api.Tests/Ai/AiQuestionGateTests.cs`: `TryEnter(userId)` true then false for the same id; `Release` allows a later enter; two different ids can both enter; a second enter does not start work (the test only asserts the boolean). Document the ponytail: in-process `ConcurrentDictionary` only (research R9).
- [X] T008 [P] Create `backend/Zharfa.Api.Tests/Ai/CloudFlagRulesTests.cs` for `AiProviderSettings` pure helpers: parse `true`/`false`/absent → enabled false unless exactly `true`; `IsUsableApprovalNote` false for null, `""`, and whitespace-only; true for a Persian note after trim. Do not hit PostgreSQL.
- [X] T009 Update `backend/Zharfa.Api.Tests/Platform/DemoOnlyFilterTests.cs` (research R15): `AiController` MUST NOT carry `[DemoOnly]`; add `AiDemoController` (or the DemoChat action type) that MUST; production + management `SystemModules.Ai` **true**; production + admin `Ai` false and `AiSettings` true; purchasing production `Ai` true (has `ai.query`); empty permission set → `Ai` and `AiSettings` false. Update `Production_purchasing_has_no_admin_modules_and_no_ai` — purchasing **does** get `Ai` in this phase. Keep Demo 1 payload tests on the demo DTO path.
- [X] T010 Update `backend/Zharfa.Api.Tests/Security/PermissionMatrixTests.cs`: `AiController.Chat` → `{ai.query}`; add `AiDemoController.Chat` (or `AiController.DemoChat`) → `{ai.query}`; add `AiAdminController.GetStatus`, `Test`, `SetCloud` → `{admin}`. Keep `AdminController.GetProviderStatus` as `{admin}`. Fail if a new action lacks a policy.
- [X] T011 Update `backend/Zharfa.Api.Tests/Audit/AuditActionsTests.cs`: add `ai.question` to `ExpectedCodes`, bump the catalog length from 22 to 23, assert `AuditActions.Label("ai.question")` is «پرسش از دستیار هوشمند». `settings.ai_provider.changed` remains.

### Implementation

- [X] T012 [P] Extract the OpenAI-compatible wire mapping from `backend/Zharfa.Api/Ai/CloudAiProvider.cs` into `backend/Zharfa.Api/Ai/OpenAiCompatibleChatClient.cs` (internal): named HttpClient `"ai"`, `ResolveEndpoint` (trim `/`, append `/chat/completions` unless already present), JSON request/response, optional `Authorization: Bearer` only when a key is non-empty, log iteration counts not bodies. Cloud and local both call it. Never log the key, URL query, or response body.
- [X] T013 Point `backend/Zharfa.Api/Ai/CloudAiProvider.cs` at `OpenAiCompatibleChatClient`; keep `AiProvider__Name/Endpoint/Model/ApiKey` via `ConfigurationKeys`. `GetStatus` returns redacted name/endpoint/model/configured only (strip userinfo and query from the endpoint string).
- [X] T014 Implement `backend/Zharfa.Api/Ai/OllamaAiProvider.cs` as a real HTTP provider (research R1): `LocalAi__Name` (default `ollama`), `LocalAi__Endpoint`, `LocalAi__Model`; **no local key**. `IsConfigured` is true only when endpoint and model are both non-empty. `CompleteAsync` uses the shared client. Empty model → unconfigured (research R20), not a guessed model name.
- [X] T015 Create `backend/Zharfa.Api/Ai/AiProviderSettings.cs` (scoped): read/write `install_setting` keys `ai.cloud.enabled` (`true` or `false`; absent ⇒ local default), `ai.cloud.approval_note` (free text, trimmed; required non-empty after trim to enable; whitespace-only refused), `ai.cloud.approved_by` (Admin username), `ai.cloud.approved_at` (ISO-8601 UTC). Disable sets `enabled=false` and does **not** delete the last note. Enable overwrites the three approval keys with a **new** note from the same request. Use parameterized SQL / existing `NpgsqlDataSource` patterns; never log the note in full if over 100 characters (pass through `RedactionHelper` if copied to logs).
- [X] T016 Create `backend/Zharfa.Api/Ai/ActiveAiProvider.cs` (research R2): `IAiProvider` that selects cloud iff `AiProviderSettings` says enabled, else local. No silent fallback. Register this as the `IAiProvider` used by chat. Keep typed `OllamaAiProvider` and `CloudAiProvider` available for connection tests.
- [X] T017 Create `backend/Zharfa.Api/Ai/AiQuestionGate.cs` (singleton): `ConcurrentDictionary<long, byte>` keyed by `app_user.user_id`; `TryEnter` / `Release`. Comment: `ponytail: in-process only; one API replica on LAN compose; upgrade to a PostgreSQL advisory lock if the API is scaled.`
- [X] T018 Add `AuditActions.Question = "ai.question"` and Persian label «پرسش از دستیار هوشمند» to `All` in `backend/Zharfa.Api/Audit/AuditActions.cs` (data-model §3). Keep `SettingsAiProviderChanged`.
- [X] T019 Update `backend/Zharfa.Api/Platform/SystemModules.cs`: add `AiSettings`; `Ai = permissions.Contains(Permissions.AiQuery)` in **both** data modes (lift Phase 1 production hide for purchasing AI); `AiSettings = permissions.Contains(Permissions.Admin)`; `DemoPages` unchanged. Update the XML comment that still says demo-only AI.
- [X] T020 Update `backend/Zharfa.Api/Platform/SystemStatusController.cs` so the JSON `modules` includes `aiSettings` from `SystemModules.For`.
- [X] T021 Create `backend/Zharfa.Api/Controllers/AiDemoController.cs` (`[Route("api/ai/demo")]`, `[Authorize(Policy = Permissions.AiQuery)]`, `[DemoOnly]`): move today’s `POST /api/ai/chat` Demo 1 loop (sales/receivables/inventory `AnalystTools`, 2 calls, 5 s/tool, 20 s phase, body may include `from`/`to`/`categoryId`) to `POST /api/ai/demo/chat`. Keep Demo 1 `dataLabel` demo. Share the in-flight `AiQuestionGate` by user id (research R9).
- [X] T022 Strip `[DemoOnly]` from `backend/Zharfa.Api/Controllers/AiController.cs` and stop using Demo 1 `AnalystTools` there. Leave a compiling purchasing stub that still 503s until US1 tools exist if needed, but the route `POST /api/ai/chat` must exist, require `ai.query`, and accept `{ "question" }` only (reject/ignore `from`/`to`/`categoryId`). Wire `AiQuestionGate` (409 `ai-busy` + the exact Persian wait message in contracts/api.md §1) with `try/finally` release.
- [X] T023 Rewire DI in `backend/Zharfa.Api/Program.cs`: stop selecting `IAiProvider` by `AiProvider:Name == ollama`. Register `OllamaAiProvider`, `CloudAiProvider`, `AiProviderSettings`, `ActiveAiProvider` as `IAiProvider`, `AiQuestionGate` singleton, `PurchasingAnalystTools` (empty/allowlist stub allowed until US1), keep `AnalystTools` for demo. HttpClient `"ai"` timeout stays 30 s.

**Checkpoint**: Foundation ready — modules, policies, local HTTP seam, cloud flag, gate, and Demo 1 split compile; user stories can start.

---

## Phase 3: User Story 1 — Manager asks purchasing questions in Persian (Priority: P1) 🎯 MVP

**Goal**: A Management (also Purchasing/Finance) user asks a Persian purchasing question on `#/assistant` and receives a cited answer from allowlisted purchasing tools, matching Overview/report figures, or «داده در دسترس نیست», with facts separate from interpretation. Independent turns; no saved chat.

**Independent Test**: quickstart Flow C (+ C2, D): after a successful simulator sync, as Management run fixture items 1–20; cited amounts match the matching report; unavailable/zero behave as specified; second question omitting period uses the Overview default month; leaving `#/assistant` does not restore the answer.

### Tests for User Story 1

- [X] T024 [P] [US1] Create `backend/Zharfa.Api.Tests/Ai/PurchasingToolAllowlistTests.cs`: `PurchasingAnalystTools` allowlist is exactly `purchasing_overview`, `purchasing_by_vendor`, `purchasing_by_product`, `purchasing_by_category`, `purchasing_trend`, `purchasing_top_products`, `purchasing_returns`, `purchasing_transactions_summary` (contracts/tools.md); unknown name is refused (error JSON, not executed); names containing `balance`, `payment`, `cheque`, `sales_overview`, `receivables`, `inventory` are absent; `MAX_TOOL_CALLS_PER_CHAT == 3`, `PER_CALL_TIMEOUT_SECONDS == 10`, row cap 100, over-cap ⇒ `Success == false` (metric unavailable, not a truncated list). Pure allowlist/constants; fake executor for unknown-name.
- [X] T025 [P] [US1] Create `backend/Zharfa.Api.Tests/Ai/PurchasingPeriodDefaultTests.cs`: omitted period binds Overview default = current Jalali month (same helper Overview uses); a second independent `[system, user]` message list contains only one user message (no prior question/tool results).
- [X] T026 [P] [US1] Create `backend/Zharfa.Api.Tests/Ai/PurchasingChatRequestTests.cs`: question trim; length 0 and 2001 refused; 1 and 2000 accepted; request DTO has no `From`/`To`/`CategoryId` properties (reflection) so a leftover dashboard period cannot leak (research R8).
- [X] T027 [P] [US1] Create `backend/Zharfa.Api.Tests/Ai/FactsVsInterpretationTests.cs`: when tools return no successful fact, `unavailable=true`, answer contains «داده در دسترس نیست», `interpretation` empty; when facts exist, they are server strings (period/filters/metric) and interpretation is a separate field; production `dataLabel` is `real` with `demoNotice` omitted/empty; demo `dataLabel` is `demo` with notice present (FR-021). Construct response records; no HTTP.

### Implementation for User Story 1

- [X] T028 [US1] Create `backend/Zharfa.Api/Ai/PurchasingAnalystTools.cs` wrapping `PurchasingOverviewService` and `PurchasingReportService` per contracts/tools.md / research R5: `purchasing_overview` (net/gross/returns/VAT kept separate, comparison, 12-month trend, active vendors, top 5 vendors/products, last-sync, data mode); `purchasing_by_vendor` / `by_product` / `by_category` / `top_products` via `GroupedAsync`; `purchasing_trend` via `TrendAsync`; `purchasing_returns` via `ReturnsAsync`; `purchasing_transactions_summary` via `DocumentsAsync` **headers + totals only** (no `DocumentLinesAsync`). Period via existing `JalaliPeriod` (omit → current Jalali month). Vendor/product/category **names** resolved with Phase 2 lookup + `fn_search_key` (ی/ک); unknown name ⇒ metric unavailable. `limit` 1..100 default 100; if `totalRows > 100` return unavailable. Per-call timeout 10 s via a linked CTS. Max 3 calls per question. Cloud-bound JSON: totals, counts, ranked **names** + amounts, period labels, `view`, unavailable flags — never line arrays, PII, keys, or opaque source keys beyond report names.
- [X] T029 [US1] Implement the purchasing loop in `backend/Zharfa.Api/Controllers/AiController.cs` (research R8, R10): messages `[system, user]` only; system prompt in Persian — facts only from tool JSON; cite period/filters/source metric; separate interpretation; no tables/charts; no extra numbers; purchases ≠ payments ≠ balance; price difference ≠ profit; independent turns; default period = current Jalali month. User-visible CTS 30 s. Tool timeout or overall timeout → 503 assistant-unavailable (not metric unavailable). Metric miss → 200 `unavailable: true` «داده در دسترس نیست». Valid empty period → zero from tools. `dataLabel` from `DataModeOptions`. No `from`/`to`/`categoryId` on the body. After authz, audit `ai.question` for 200/409/503 (US7 will tighten detail; at minimum write action + trimmed question). Never persist answers.
- [X] T030 [P] [US1] Add purchasing chat client helpers to `frontend/src/services/api.ts`: `postPurchasingChat(question: string)` → `POST /api/ai/chat` with `{ question }` only; types matching contracts/api.md §1 (`answer`, `period`, `filters`, `view`, `facts`, `interpretation`, `unavailable`, `dataLabel`, optional `demoNotice`, 409 `ai-busy`, 503 unavailable). Keep Demo 1 `apiPost('/api/ai/chat')` **out** of the new helper.
- [X] T031 [US1] Point `frontend/src/components/AiChat.tsx` and `frontend/src/pages/Dashboard.tsx` at `POST /api/ai/demo/chat` only (research R7). Do not show Dashboard chat from `modules.ai`.
- [X] T032 [US1] Create `frontend/src/pages/Assistant.tsx`: Persian RTL page for the current question + current answer only (submit replaces both). Disable submit while in flight; if a second submit is attempted, show the wait message. Display `facts` as facts and `interpretation` separately; show `demoNotice` when present; show 503 unavailable without stack traces. Do not use `sessionStorage`/`localStorage` for Q&A. Keyboard-usable, visible focus, AA contrast.
- [X] T033 [US1] Update `frontend/src/App.tsx`: add view `assistant` at `#/assistant`, visible when `status.modules.ai`; leaving the hash unmounts `Assistant` (no restored chat). Admin has no `modules.ai`. Unknown hash still falls back to the first allowed view.

**Checkpoint**: US1 is independently testable (quickstart C, C2, D). MVP.

---

## Phase 4: User Story 2 — Local model is the default on the company server (Priority: P1)

**Goal**: Fresh install answers with the local provider. Admin opens Settings → AI, sees local marked default, redacted status, and can run a connection test. Non-admins get 403. No keys anywhere.

**Independent Test**: quickstart Flow B — fresh settings GET as Admin (`answeringProvider=local`, no key fields); connection test; Management 403 on settings; Management question answered while cloud still off; frontend bundle grep finds no `AiProvider__ApiKey`.

### Tests for User Story 2

- [X] T034 [P] [US2] Create `backend/Zharfa.Api.Tests/Ai/ProviderStatusRedactionTests.cs`: serialize `GET` status DTO and connection-test DTO to JSON; assert no property named `apiKey`/`ApiKey`/`key`; endpoint with userinfo `http://user:pass@host/v1?token=abc` is stored/shown without userinfo or query; connection-test failure message is passed through `RedactionHelper.Scrub` so a planted key is absent.
- [X] T035 [P] [US2] Extend `backend/Zharfa.Api.Tests/Security/PermissionMatrixTests.cs` if T010 already listed admin AI actions — add a role matrix assertion: Management/Purchasing/Finance may not call `AiAdminController.*`; Admin may not call `AiController.Chat`.

### Implementation for User Story 2

- [X] T036 [US2] Create `backend/Zharfa.Api/Controllers/AiAdminController.cs` (`[Route("api/admin/ai")]`, `[Authorize(Policy = Permissions.Admin)]`, **not** `[DemoOnly]`): `GET ""` returns contracts/api.md §3 (`answeringProvider`, `aggregatesOnlyNotice` exactly «در صورت فعال بودن ارائه‌دهنده ابری، تنها نتایج تجمیعی برای آن ارسال می‌شود.», `local` with `isDefault: true`, `cloud.enabled` from settings, `ownerApprovalRecorded`, **never** `apiKey`). `GET /api/admin/provider` on `AdminController` stays `[DemoOnly]` and still returns **cloud** env redaction only (research R4).
- [X] T037 [US2] Add `POST /api/admin/ai/test` in `backend/Zharfa.Api/Controllers/AiAdminController.cs`: body `{ "target": "local" | "cloud" }`; run `CompleteAsync` with empty tools and a one-line Persian ping, 10 s timeout, on the **named** provider (not the answering resolver). 200 `{ ok, message }` success «اتصال برقرار شد.» or scrubbed Persian failure. Does **not** change `ai.cloud.enabled`. Audit `settings.ai_provider.changed` with `AuditDetail.Of` keys `op=test`, target `local|cloud`, `ok` — never the key.
- [X] T038 [P] [US2] Add Settings API helpers to `frontend/src/services/api.ts` for `getAiSettings()` and `testAiProvider(target)`.
- [X] T039 [US2] Create `frontend/src/pages/AiSettings.tsx`: Admin-only RTL page showing local as default, redacted name/endpoint/model/configured, cloud enabled flag, aggregates-only notice, connection-test button, Persian success/failure. Never an input for keys. Keyboard + AA contrast.
- [X] T040 [US2] Update `frontend/src/App.tsx`: view `aiSettings` at `#/settings/ai`, visible when `status.modules.aiSettings`. Non-admin hash → `<NoAccess/>`.

**Checkpoint**: US2 independently testable (quickstart B). Local is the answering provider while cloud is off.

---

## Phase 5: User Story 3 — Operator benchmarks candidate models and records the default (Priority: P1)

**Goal**: Documented, repeatable procedure runs the committed 20-question fixture against 2–3 local candidates, records accuracy and median time, and names one default (max accuracy, then faster median). Missing candidate is `failed`, not 0%.

**Independent Test**: quickstart Flow H / contracts/benchmark.md — a dated record lists candidates, fixture hash, accuracy, times, and exactly one default; subsequent local answers use `LocalAi__Model` set to that winner.

### Tests for User Story 3

- [X] T041 [P] [US3] Create `backend/Zharfa.Api.Tests/Ai/BenchmarkRecordTests.cs` (pure): given candidate rows `{ model, accuracy, medianMs, failed? }`, `BenchmarkChooser.Pick` returns the max accuracy, then min medianMs; a `failed` candidate is never chosen even with accuracy 0; empty successful set returns null (operator must not invent a default).

### Implementation for User Story 3

- [X] T042 [P] [US3] Confirm `specs/006-local-ai-purchasing/fixtures/purchasing-questions.json` has **exactly 20** items with `id`, `question`, `askAs` (`management`|`purchasing`), `expect` (`cited`|`unavailable`|`zero`|`no-financials`), `metric`, `period`; **no expected amounts**. If a field is missing, fix the fixture — do not add a second number sheet.
- [X] T043 [US3] Create `specs/006-local-ai-purchasing/scripts/run-benchmark.py` (Python stdlib only) and/or `specs/006-local-ai-purchasing/scripts/run-benchmark.sh` implementing contracts/benchmark.md: SHA-256 of the fixture file; for each candidate set `LocalAi__Model` is an **operator** step (script documents restart; it does not SSH); score each item by HTTP class in benchmark.md (cited facts vs live Overview/report GET; unavailable phrase; zero; no-financials regex); missing model → `failed`; write JSON/markdown with date, fixture id/hash, host (no secrets), per-candidate `{ model, accuracy, medianMs, p95Ms, failedQuestions[] }`, `chosenDefault`. Never write live row extracts.
- [X] T044 [P] [US3] Create sample record `specs/006-local-ai-purchasing/benchmark/SAMPLE.md` (simulator/demo timings or pass/fail only — FR-028). Operators copy the format on the target server; do not commit production timings with real figures.
- [X] T045 [P] [US3] Point `deploy/README.md` at `contracts/benchmark.md` and the sample record so an operator can repeat the run (question set identity, candidates, date, accuracy, times, chosen default).

**Checkpoint**: US3 is a procedure + chooser, not an in-app model picker. Default local model is taken from the record (FR-007).

---

## Phase 6: User Story 4 — Permissions apply to every AI question (Priority: P1)

**Goal**: The assistant only returns what the asking role can already see. Purchasing asking vendor balance/payables/payments/cheques gets **no financial figures**. Admin 403 on chat. Unsigned 401. Tools that would need `vendor.financials` are never registered.

**Independent Test**: quickstart Flow A + C items 15–17 — Purchasing volume question answered; Purchasing vendor-balance item has zero balance/payment/cheque amounts; Admin chat 403 + `access.denied`; unsigned 401; Management vendor-balance → «داده در دسترس نیست».

### Tests for User Story 4

- [X] T046 [P] [US4] Create `backend/Zharfa.Api.Tests/Ai/NoVendorFinancialsInToolsTests.cs`: scan `Zharfa.Api.Ai` public tool names and `PurchasingAnalystTools` source/DTO property names for `balance`, `payment`, `debt`, `credit`, `paid`, `cheque` (same idea as Phase 3 `NoVendorFinancialsTests`); fail if a financial tool or field exists. Snapshot a representative tool JSON and assert no those tokens with digits.
- [X] T047 [P] [US4] Extend `backend/Zharfa.Api.Tests/Security/PermissionMatrixTests.cs` role cases: Admin may call no `ai.query` action; Purchasing may call `AiController.Chat` but no `AiAdminController` and no `vendor.financials`; unauthenticated is not in the matrix (401 is the result handler — already covered).

### Implementation for User Story 4

- [X] T048 [US4] In `backend/Zharfa.Api/Ai/PurchasingAnalystTools.cs` and the purchasing system prompt in `backend/Zharfa.Api/Controllers/AiController.cs`: never register or describe vendor-balance/payable/payment/cheque tools; if the model asks for a missing name, return refused-tool JSON so the user-visible answer is «داده در دسترس نیست» when no successful purchasing fact was produced (contracts/tools.md). Purchasing role cannot obtain figures that reports hide because the tools do not exist.
- [X] T049 [US4] Confirm `backend/Zharfa.Api/Controllers/AiController.cs` does not catch 403 itself — missing `ai.query` stays on `AuditingAuthorizationResultHandler` (403 + `access.denied`). Unsigned stays 401 with no audit. Add a brief comment pointing at Phase 3 FR-018 (audit failure must not change the HTTP outcome).
- [X] T050 [P] [US4] In `frontend/src/App.tsx` / `frontend/src/pages/Assistant.tsx`, do not offer `#/assistant` when `modules.ai` is false (Admin). Direct URL → `<NoAccess/>`. No client-side permission bypass.

**Checkpoint**: US4 independently testable (quickstart A, C items 15–17). SC-007, SC-009.

---

## Phase 7: User Story 6 — A stopped provider does not take down dashboards (Priority: P1)

**Goal**: Provider stopped, unreachable, unconfigured or timed out → Persian assistant-unavailable (503). Overview and purchasing reports keep working. Restore provider → answers resume without restarting the app.

**Independent Test**: quickstart Flow F — stop Ollama (cloud off); Overview + a report load; chat returns 503 unavailable (not metric unavailable), no stack/key; start Ollama; question answers again.

### Tests for User Story 6

- [X] T051 [P] [US6] Create `backend/Zharfa.Api.Tests/Ai/AssistantUnavailableTests.cs`: map `AiException` / unconfigured / timeout to the 503 envelope (`unavailable: true`, `answer` and `message` are the assistant-unavailable phrasing, empty facts, no metric «داده در دسترس نیست» as the only phrase); assert serialized JSON has no `apiKey` and no stack-trace-like frames. Distinct from metric-unavailable 200.

### Implementation for User Story 6

- [X] T052 [US6] In `backend/Zharfa.Api/Controllers/AiController.cs` and `backend/Zharfa.Api/Ai/OllamaAiProvider.cs` / `CloudAiProvider.cs`: unconfigured, HTTP failure, and cancellation all throw/return `AiException` with a scrubbed Persian message; controller returns 503 `AiChatUnavailableResponse` (contracts/api.md §1). Cloud enabled but failing stays 503 with **no** call to local (already required of `ActiveAiProvider`).
- [X] T053 [US6] Enforce the 10 s per-tool and 30 s overall budgets in `backend/Zharfa.Api/Ai/PurchasingAnalystTools.cs` and `backend/Zharfa.Api/Controllers/AiController.cs` so a hung tool yields 503, not a partial guessed `facts` array.
- [X] T054 [P] [US6] In `frontend/src/pages/Assistant.tsx`, render 503 with the server Persian message (`role="alert"`), no raw exception. Do not navigate the user off Overview/reports.

**Checkpoint**: US6 independently testable (quickstart F). SC-006.

---

## Phase 8: User Story 5 — Admin may enable the cloud provider only with owner approval (Priority: P2)

**Goal**: Cloud stays off until an Admin sends a non-empty owner-approval note. While on, every cloud payload is aggregates only; local is unused. Disable needs no note; re-enable needs a new note. Non-admin 403.

**Independent Test**: quickstart Flow E — enable without note 400; with note 200 + audit `noteRecorded=true` and no key; capture one cloud HTTP body (aggregates only); disable without note returns to local; non-admin 403.

### Tests for User Story 5

- [X] T055 [P] [US5] Create `backend/Zharfa.Api.Tests/Ai/CloudEnableRulesTests.cs`: enable with null/whitespace note refused (`IsUsableApprovalNote`); enable with Persian note accepted; disable with no note accepted; re-enable requires a new note argument (the stored last note is not reused). Pure, on `AiProviderSettings` helpers.
- [X] T056 [P] [US5] Create `backend/Zharfa.Api.Tests/Ai/CloudPayloadScrubTests.cs` (research R12): snapshot a representative messages+tools JSON that `CloudAiProvider` would send; assert forbidden keys/values absent (`lines`, `password`, `apiKey`, national id, phone, document line text); allowed: totals, counts, ranked names, period labels. No network.

### Implementation for User Story 5

- [X] T057 [US5] Add `POST /api/admin/ai/cloud` in `backend/Zharfa.Api/Controllers/AiAdminController.cs` (contracts/api.md §5): `{ enabled: true, ownerApprovalNote }` → 400 if note missing/whitespace, local remains answering; 200 GET-shaped status when saved. `{ enabled: false }` → 200, no note required. Enable allowed even if cloud env is unconfigured (status shows not connected; answers then 503, no local fallback — data-model §4). Audit `settings.ai_provider.changed` with `op=enable|disable`, `noteRecorded=true` on enable, never the key, never the full note if over 100 characters (`AuditDetail` cut). Non-admin 403 via policy.
- [X] T058 [US5] When `ai.cloud.enabled=true`, `backend/Zharfa.Api/Ai/ActiveAiProvider.cs` routes every `CompleteAsync` to `CloudAiProvider`. Tool JSON sent to cloud is the aggregates-only payload from `PurchasingAnalystTools` (names + amounts + counts).
- [X] T059 [P] [US5] Add `setAiCloud(enabled, ownerApprovalNote?)` to `frontend/src/services/api.ts`.
- [X] T060 [US5] Extend `frontend/src/pages/AiSettings.tsx`: enable requires a visible non-empty note field; show aggregates-only notice; disable without a note; last approval recorded (not the key). Refuse client-side empty/whitespace but keep the server as the rule.

**Checkpoint**: US5 independently testable (quickstart E). SC-004, SC-005. Product still works local-only if the owner never enables cloud.

---

## Phase 9: User Story 7 — AI questions appear in the audit log without secrets (Priority: P2)

**Goal**: Admin filters the existing audit log and sees each AI question (user, Jalali time, bounded question text) and provider-setting changes, with no result rows or keys. Non-admin audit GET remains 403.

**Independent Test**: quickstart Flow G — questions as Management/Purchasing, refused Admin call, cloud setting change, connection test; filter `ai.question` and `settings.ai_provider.changed`; scan audit/API/logs for zero keys and zero tool JSON.

### Tests for User Story 7

- [X] T061 [P] [US7] Create `backend/Zharfa.Api.Tests/Ai/AiQuestionAuditTests.cs`: `AuditDetail.Of` for an AI question keeps only `period`, `from`, `to`, `vendor`, `product`, `category`, `tools`, `outcome` (`ok|unavailable|busy|provider`); drops `facts`, `answer`, `contentJson`, `apiKey`; values trimmed, CR/LF removed, cut per existing `AuditDetail` rules; `RedactionHelper.Scrub` applied. Target question cut is the existing 200-character `AuditLogger` behavior (assert via the same cut helper if public, else document that T062 must use `AuditEntry.Target` and rely on the logger).

### Implementation for User Story 7

- [X] T062 [US7] In `backend/Zharfa.Api/Controllers/AiController.cs` (and demo chat): after permission success, record `new AuditEntry(AuditActions.Question, Target: question, Detail: AuditDetail.Of(...))` for outcomes 200, 409, 503 with `outcome=ok|unavailable|busy|provider` and tool **names** only. Do not wait for audit success to return the answer (Phase 3 FR-018). Do not audit 401.
- [X] T063 [US7] In `backend/Zharfa.Api/Controllers/AiAdminController.cs`, every enable/disable/test writes `settings.ai_provider.changed` without keys. Denied settings calls stay on the existing `access.denied` handler.
- [X] T064 [P] [US7] Confirm `frontend/src/pages/AuditLog.tsx` already loads the action catalog from the API so `ai.question` appears with «پرسش از دستیار هوشمند» once T018 is in `All`. If the catalog endpoint filters the list, add the new code there (`backend/Zharfa.Api/Controllers/AuditLogController.cs` / `AuditLogService`). Non-admin still 403.

**Checkpoint**: US7 independently testable (quickstart G). SC-008.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Docs, wiring comments, and leftover Demo 1 / production-hide cleanup that span stories.

- [X] T065 [P] Update `README.md`, `backend/Zharfa.Api/README.md`, and `deploy/README.md`: local default, Settings → AI, `#/assistant` for `ai.query`, cloud opt-in with owner-approval note, pointer to `specs/006-local-ai-purchasing/quickstart.md` and `contracts/benchmark.md`. No CHANGELOG bump.
- [X] T066 [P] Comment `.ainative/project.yaml` that spec 006 adds `backend/Zharfa.Api.Tests/Ai/*` and that HTTP/UI proof is `specs/006-local-ai-purchasing/quickstart.md` (tester, not workers). Do not add GPU/Ollama to `validation_commands`.
- [X] T067 [P] Grep the frontend bundle source (`frontend/src`) and API DTOs for `ApiKey`, `AiProvider__ApiKey`, and typical key prefixes; remove any leftover. Keep reading keys only in `CloudAiProvider` from configuration.
- [X] T068 Production hide: `POST /api/ai/demo/chat` and Demo 1 `/api/sales/*` remain `[DemoOnly]` (404 in production). Purchasing assistant does not call Demo 1 tools. Verify `backend/Zharfa.Api.Tests/Platform/DemoOnlyFilterTests.cs` still lists sales/inventory/receivables as demo-only.
- [X] T069 Log only tool **names**, iteration counts and duration in `backend/Zharfa.Api/Controllers/AiController.cs` / providers (research R18) — never bodies, facts, or URLs with keys.
- [X] T070 Run through [quickstart.md](quickstart.md) mapping (tester): Flows A–H cover phase exit. Workers do not execute Compose, Ollama, or `dotnet test`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup — **BLOCKS** all user stories
- **User Story 1 (Phase 3)**: Depends on Foundational — MVP
- **User Story 2 (Phase 4)**: Depends on Foundational; uses US1 chat to prove local answers
- **User Story 3 (Phase 5)**: Depends on US1 (fixture scoring needs `/api/ai/chat`)
- **User Story 4 (Phase 6)**: Depends on US1 tools; can start in parallel with US2 once tools exist
- **User Story 6 (Phase 7)**: Depends on US1 503 path and US2 local provider
- **User Story 5 (Phase 8)**: Depends on US2 Settings controller + US1 tools (aggregates payload)
- **User Story 7 (Phase 9)**: Depends on US1 (question audit) and US2/US5 (settings audit)
- **Polish (Phase 10)**: After the stories intended for the increment
- **Convergence (Phases 11–15)**: T071–T080 — **done**; do not re-open
- **Plan revision (Phase 16)**: Depends on T001–T080 remaining done; remaining LOOP_BACK R21–R25 only

### User Story Dependencies

- **US1 (P1)**: After Foundational — no dependency on US2–US7
- **US2 (P1)**: After Foundational — independently testable with a stub chat; full Flow B needs US1 answers
- **US3 (P1)**: After US1 (live scoring)
- **US4 (P1)**: After US1 tools — independently testable
- **US6 (P1)**: After US1 + local provider (US2 T014)
- **US5 (P2)**: After US2 admin API + US1 tools
- **US7 (P2)**: After US1 question writes + US2/US5 setting writes

### Within Each User Story

- Tests (included) MUST be written and FAIL before implementation
- Settings/store before resolver; tools before chat loop; chat before SPA
- Story complete before moving to the next priority unless staffed in parallel on different files

### Parallel Opportunities

- Phase 1: T002, T003, T004, T005
- Phase 2 tests: T006–T011
- Phase 2 impl: T012/T013 after T012; T014–T018 parallel after tests; T019–T023 sequential around DI
- US1 tests T024–T027; then T028 → T029 → frontend T030–T033
- US2 tests T034–T035 with T036–T040
- US3 T041–T045 largely parallel after US1
- US4 T046–T050
- US5 T055–T060 after US2
- Polish T065–T067
- Phase 16 tests T081–T086; then T087 + T088 [P] + T089; T090 → T091 → T092; T093/T094/T095/T096 [P] with T097 after T092

---

## Parallel Example: User Story 1

```text
# Tests together:
Task: T024 PurchasingToolAllowlistTests in backend/Zharfa.Api.Tests/Ai/PurchasingToolAllowlistTests.cs
Task: T025 PurchasingPeriodDefaultTests in backend/Zharfa.Api.Tests/Ai/PurchasingPeriodDefaultTests.cs
Task: T026 PurchasingChatRequestTests in backend/Zharfa.Api.Tests/Ai/PurchasingChatRequestTests.cs
Task: T027 FactsVsInterpretationTests in backend/Zharfa.Api.Tests/Ai/FactsVsInterpretationTests.cs

# Then implementation:
Task: T028 PurchasingAnalystTools in backend/Zharfa.Api/Ai/PurchasingAnalystTools.cs
Task: T029 AiController purchasing loop in backend/Zharfa.Api/Controllers/AiController.cs
```

---

## Parallel Example: User Story 2

```text
Task: T034 ProviderStatusRedactionTests in backend/Zharfa.Api.Tests/Ai/ProviderStatusRedactionTests.cs
Task: T036 AiAdminController GET in backend/Zharfa.Api/Controllers/AiAdminController.cs
Task: T038 getAiSettings in frontend/src/services/api.ts
```

---

## Parallel Example: Phase 16 (LOOP_BACK)

```text
# Tests together:
Task: T081 PurchasingQuestionRouterTests in backend/Zharfa.Api.Tests/Ai/PurchasingQuestionRouterTests.cs
Task: T082 PurchasingServerDispatchTests in backend/Zharfa.Api.Tests/Ai/PurchasingServerDispatchTests.cs
Task: T083 PurchasingPeriodDefaultTests in backend/Zharfa.Api.Tests/Ai/PurchasingPeriodDefaultTests.cs
Task: T084 PurchasingCitedAnswerTests in backend/Zharfa.Api.Tests/Ai/PurchasingCitedAnswerTests.cs
Task: T085 AssistantUnavailableTests in backend/Zharfa.Api.Tests/Ai/AssistantUnavailableTests.cs
Task: T086 CloudRequestCaptureHandlerTests in backend/Zharfa.Api.Tests/Ai/CloudRequestCaptureHandlerTests.cs

# Then implementation (T087/T088 parallel; T090→T091→T092 sequential on the chat path):
Task: T087 PurchasingQuestionRouter in backend/Zharfa.Api/Ai/PurchasingQuestionRouter.cs
Task: T088 PurchasingCitedAnswer in backend/Zharfa.Api/Ai/PurchasingCitedAnswer.cs
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: quickstart C / C2 / D
5. Demo cited Persian purchasing answers on local (even if Settings page is still missing)

### Incremental Delivery

1. Setup + Foundational → seam ready
2. US1 → cited purchasing Q&A (MVP)
3. US2 → Settings → AI + local default visible
4. US3 → benchmark record names the default model
5. US4 → Purchasing vs vendor balances; Admin refused
6. US6 → provider down, dashboards up
7. US5 → cloud opt-in with owner-approval note (optional for local-only installs)
8. US7 → audit filters for AI events
9. Each story adds value without breaking previous stories
10. Phase 16 (`unpark-plan-1`) → router + dispatch + 503 vs 200 + 7–8B pin + 9router capture (T081–T098); T001–T080 stay done

### Parallel Team Strategy

1. Together: Setup + Foundational
2. Then: Developer A US1+US4; Developer B US2+US5+US6; Developer C US3+US7 — merge on `AiController` / `AiAdminController` carefully (those files are not [P] across stories)

---

## Notes

- [P] tasks = different files, no unfinished dependency
- [Story] label maps to spec.md US1–US7
- Constitution template is unfilled; AGENTS.md + `zharfa-prd.md` remain the gates (plan Constitution Check)
- Fixture amounts are never stored; scorer compares live reports
- Default product provider remains local even if benchmark medians exceed 30 s — record the times (SC-002)
- Avoid: reopening Phases 1–3, Phase 5 tables/charts, Phase 6 vendor financials, new packages, in-process model hosting

---

## Phase 11: Convergence

- [X] T071 Fix endpoint userinfo redaction in backend/Zharfa.Api/Ai/OpenAiCompatibleChatClient.cs for passwords with special characters per FR-002, US2/AC3, T034 (partial)
- [X] T072 Fix Bearer token scrubbing in backend/Zharfa.Api/Data/RedactionHelper.cs so connection-test failure messages redact tokens without '=' or ':' per FR-002, FR-004, US2/AC3, T034 (partial)

---

## Phase 12: Convergence

- [X] T073 Fix ActiveAiProvider.IsConfigured and AiController so cloud provider can answer when LocalAi__Model is unconfigured per FR-001, US5 (contradicts)
- [X] T074 Remove document headers from purchasing_transactions_summary tool JSON to enforce aggregates-only payloads per FR-009, US5/AC2 (contradicts)
- [X] T075 Populate period and filters in PurchasingChatResponse and render period citation in Assistant UI footer per FR-014, US1/AC2 (partial)
- [X] T076 Align ai.question audit detail in AiController with data-model allowlist (include period/filters, drop view) per FR-024, US7/AC1 (partial)
- [X] T077 Implement ai.question audit logging in AiDemoController for demo chat outcomes per FR-024, T062 (missing)

---

## Phase 13: Convergence

- [X] T078 Fix compiler error CS0103 in backend/Zharfa.Api/Ai/PurchasingAnalystTools.cs where period is referenced out of scope in ExecuteAsync catch block per plan: compile and test gate, T075 (partial)

---

## Phase 14: Convergence

- [X] T079 Fix endpoint userinfo redaction in backend/Zharfa.Api/Ai/OpenAiCompatibleChatClient.cs for passwords containing '#' before '@' per FR-002, US2/AC3, T034 (partial)

---

## Phase 15: Convergence

- [X] T080 Resolve ActiveAiProvider constructor ambiguity in backend/Zharfa.Api/Ai/ActiveAiProvider.cs for ASP.NET Core DI resolution per plan: DI / ActiveAiProvider, T016, T023 (contradicts)

---

## Phase 16: Plan revision (`unpark-plan-1`) — remaining LOOP_BACK (R21–R25)

**Purpose**: Close tester LOOP_BACK without reopening T001–T080. Keep those checkboxes **done**. Edit named files below only as needed. SC-001 stays **20/20**. Do not implement 14b as a required candidate. Workers do not run Compose, Ollama, or `dotnet test`.

**Goal**: Deterministic `PurchasingQuestionRouter` before tools/model; server-dispatched purchasing tools (model gets **no** tool specs); 503 `{ "code": "assistant-unavailable" }` vs 200 metric-unavailable; pinned 7–8B trio + live record; Development 9router outbound capture; Q13 never runs `purchasing_overview`.

**Independent Test** (tester, [quickstart.md](quickstart.md)): Flow C items 13–18 HTTP **200** `unavailable=true` empty facts, Q13 audit `tools` must not list `purchasing_overview`; Flow C2 item 20; Flow F 503 `code=assistant-unavailable` while Overview/report stay 200; Flow E 9router overlay **200** + saved aggregates JSON; Flow H three 7–8B candidates (`qwen2:7b`, `qwen2.5:7b`, `llama3.1:8b`).

### Tests (write first; they must fail until the code exists)

- [X] T081 [P] [US1] Create `backend/Zharfa.Api.Tests/Ai/PurchasingQuestionRouterTests.cs` (pure, no DB/network): `PurchasingQuestionRouter.Classify` on every item in `specs/006-local-ai-purchasing/fixtures/purchasing-questions.json` matches `scope` (Q13 `out-of-domain-sales`, Q14 `out-of-domain-inventory`, Q15–17 `vendor-financials`, Q18 `mabna-bypass`, Q1–12 and Q19 `purchasing`). Normalize ی/ک and Arabic Yeh. Tokens per research R21 (فروش/داروخانه/نسخه; موجودی/انبار/کسری کالا; مانده/بدهی/پرداخت/چک/اسناد پرداختنی; مبنا/HOST_ID). Q13 must not return `scope=purchasing` or a tool name. Q20: token classify may be `purchasing`; overlay `hasSuccessfulSync=false` → `no-successful-sync` and **no** tool. Not LLM classification.
- [X] T082 [P] [US1] Create `backend/Zharfa.Api.Tests/Ai/PurchasingServerDispatchTests.cs`: in-domain fixture ids map to contracts/tools.md (`1,2,8,9,10` → `purchasing_overview` + `overview-default`; `3` trend; `4` `purchasing_by_vendor` or overview `topVendors`; `5` `purchasing_top_products`; `6` `purchasing_by_category`; `7` returns and/or overview `returns`; `11` overview + `previous-jalali-month`; `12` `purchasing_by_vendor` + `current-jalali-quarter`; `19` overview + `future-empty-month`). A fake `IAiProvider` for phrasing receives **empty** tool specs. Out-of-domain: `PurchasingAnalystTools.ExecuteAsync` is never called (fake executor counter stays 0).
- [X] T083 [P] [US1] Extend `backend/Zharfa.Api.Tests/Ai/PurchasingPeriodDefaultTests.cs`: `future-empty-month` binds the **next** Jalali month after Overview default (not the current month — tester bound آبان ۱۴۰۵ as current). `previous-jalali-month` and `current-jalali-quarter` match the same helpers reports use. Empty valid future month is **zero**, not unavailable.
- [X] T084 [P] [US1] Create `backend/Zharfa.Api.Tests/Ai/PurchasingCitedAnswerTests.cs`: given server `facts[]` with a ریال amount, if model text invents a different number or says «داده در دسترس نیست» while facts exist, `answer` is the server template (period, filters, metric, ریال) and `interpretation` is still the model text; if model text stays within facts, `answer` may equal model text. Out-of-domain helper: `unavailable=true`, empty facts, «داده در دسترس نیست», empty interpretation — **no** `code`.
- [X] T085 [P] [US6] Extend `backend/Zharfa.Api.Tests/Ai/AssistantUnavailableTests.cs`: serialize `PurchasingChatUnavailableResponse` JSON includes `"code":"assistant-unavailable"`; `answer`/`message` are the assistant-unavailable phrasing, empty facts; must **not** use «داده در دسترس نیست» as the sole answer. Serialize a 200 metric-unavailable `PurchasingChatResponse` and assert it has **no** `code` (or not `assistant-unavailable`). Liveness-fail / unconfigured / `AiException` / cancel all map to the 503 envelope, not 200 with Overview facts.
- [X] T086 [P] [US5] Create `backend/Zharfa.Api.Tests/Ai/CloudRequestCaptureHandlerTests.cs` (pure, no network): given a sample chat-completions request, the handler writes JSON to a temp `AiProvider__CapturePath` **without** `Authorization` / api keys / password headers; body may keep aggregates (totals, counts, ranked names). When capture path empty or environment is not Development, it does not write. Forbidden tokens in captured body: `lines` arrays, `apiKey`, national id, phone.

### Implementation

- [X] T087 [US1] Create `backend/Zharfa.Api/Ai/PurchasingQuestionRouter.cs` (namespace `Zharfa.Api.Ai`, pure C#, no new package): `Classify(question, hasSuccessfulSync)` → `{ scope, tool?, periodRule? }` per data-model §2.3, research R21–R22, and contracts/tools.md. Deterministic Persian tokens after ی/ک / Arabic Yeh normalize. Non-`purchasing` scopes set `tool=null`. Q13 (فروش داروخانه‌ها) is `out-of-domain-sales` and must never map to `purchasing_overview`.
- [X] T088 [P] [US1] Create `backend/Zharfa.Api/Ai/PurchasingCitedAnswer.cs`: build `facts[]` only from dispatched tool JSON; template `answer` from facts when model invents numbers or uses the metric-unavailable phrase despite facts; copy model text to `interpretation`; never present purchase net as فروش / pharmacy sales.
- [X] T089 [US1] Add period-rule binders on `backend/Zharfa.Api/Ai/PurchasingAnalystTools.cs`: `overview-default`, `previous-jalali-month`, `current-jalali-quarter`, `future-empty-month` (next empty Jalali month, **not** current). Keep allowlist/caps. Add a dispatch entry point the controller can call with a **named** tool + bound period (server-chosen; ignore model tool calls).
- [X] T090 [US6] Add a 2–3 s answering-provider liveness probe (empty-tools ping or failed TCP/HTTP to the OpenAI-compatible endpoint) on `backend/Zharfa.Api/Ai/OpenAiCompatibleChatClient.cs` / `OllamaAiProvider.cs` / `CloudAiProvider.cs` / `ActiveAiProvider.cs` (`IAiProvider` as needed). Unconfigured or probe fail → `AiException` **without** running purchasing tools. Probe the provider `GET /api/admin/ai` reports (this host: `host.docker.internal:11434`), not a compose `ollama` the API is not using.
- [X] T091 [US6] Add `Code` to `PurchasingChatUnavailableResponse` in `backend/Zharfa.Api/Controllers/AiController.cs` (`"assistant-unavailable"`). Every 503 path uses that code + assistant phrasing + empty facts. Do **not** put that code on 200 `PurchasingChatResponse`.
- [X] T092 [US1] Rewrite the purchasing loop in `backend/Zharfa.Api/Controllers/AiController.cs` to contracts/api.md §1 order: authz → `AiQuestionGate` → **liveness** (T090) → **scope router** (T087) → if not `purchasing` (or overlay `no-successful-sync`) return **200** metric-unavailable with empty facts / `view=null` / «داده در دسترس نیست» and **do not** call `ExecuteAsync` or `CompleteAsync` (Q13 must not attach `purchasing_overview` facts or audit `tools=purchasing_overview`) → else **server-dispatch** the mapped tool (T089) → **one** `CompleteAsync` with **empty** tool specs and the server facts block → T088 for `answer`/`interpretation`. Timeout/`AiException` → 503 (T091), never a 200 with guessed facts. Audit `tools` = executed names only (empty when gated).
- [X] T093 [P] [US6] Update `frontend/src/services/api.ts` (`PurchasingChatUnavailableResponse.code`) and `frontend/src/pages/Assistant.tsx`: 503 shows server assistant-unavailable message (`role="alert"`); 200 `unavailable` shows «داده در دسترس نیست»; do not treat metric-unavailable as provider-down.
- [X] T094 [US5] Create `backend/Zharfa.Api/Ai/CloudRequestCaptureHandler.cs` (Development-only `DelegatingHandler` on named HttpClient `"ai"`). Path from `AiProvider__CapturePath` via `ConfigurationKeys`. Write outbound chat-completions JSON; strip `Authorization` and never log keys. Register in `backend/Zharfa.Api/Program.cs` only when Development **and** path non-empty. Document in `backend/Zharfa.Api/appsettings.Development.json` (empty default). A 503 to 9router is a **failed** capture, not a skip.
- [X] T095 [P] [US5] Add `AiProvider__CapturePath` (empty) to `deploy/.env.example` and pass it on the api service in `deploy/docker-compose.yml` if other `AiProvider__*` are already passed. Comment Flow E: compose **must** include `deploy/docker-compose.9router.yml`; probe `http://shared_9router:20128/v1` from the **api container** before enable+chat.
- [X] T096 [P] [US3] Replace required `qwen2.5:14b` examples with the pinned trio in `deploy/docker-compose.ollama.yml`, `backend/Zharfa.Api/README.md`, and the model example in `specs/006-local-ai-purchasing/scripts/run-benchmark.py` (`qwen2:7b`, `qwen2.5:7b`, `llama3.1:8b`). Keep `LOCAL_AI_MODEL` empty in `deploy/.env.example` until a live record names a winner. Do not invent SAMPLE.md scores.
- [X] T097 [US3] Update `specs/006-local-ai-purchasing/scripts/run-benchmark.py` `classify_answer` per contracts/benchmark.md: HTTP 503 `code=assistant-unavailable` is a **fail** for every fixture `expect`; `unavailable` requires 200, empty `facts`, metric phrase; for `scope` starting with `out-of-domain` / `vendor-financials` / `mabna-bypass` fail if the chat body/`tools` imply `purchasing_overview` (Q13); `zero` requires a **future** Jalali month, not current Overview month. Default CLI example uses the 7–8B trio. Need ≥2 completed candidates; missing after pull fail → `failed`, not 0%.
- [X] T098 Confirm `specs/006-local-ai-purchasing/benchmark/SAMPLE.md` stays a **template** (no invented 20/20 / 14b). Point `deploy/README.md` at contracts/benchmark.md pinned candidates and 9router capture. Tester (not workers) re-runs quickstart Flows C, C2, E, F, H.

**Checkpoint**: R21–R25 implemented in code and docs; T001–T080 remain checked; implement worker still does not run validation.

---

## Phase 17: Convergence

- [X] T099 Replace generation-based CompleteAsync liveness probe in OpenAiCompatibleChatClient with TCP/HTTP connectivity check without model generation per FR-019, R23 (contradicts)
- [X] T100 Route Q4 and Q7 to purchasing_overview in PurchasingQuestionRouter to match fixture topVendors and returns metrics per FR-022, contracts/tools.md (contradicts)
- [X] T101 Format numbers with Persian digits in PurchasingCitedAnswer.BuildServerTemplate per FR-014, AGENTS.md (contradicts)
- [X] T102 Add [JsonPropertyName("code")] to PurchasingChatUnavailableResponse and configure Web serializer defaults in AssistantUnavailableTests per FR-019, contracts/api.md (partial)
- [X] T103 Replace empty catch block in CloudRequestCaptureHandler.SendAsync with warning log per plan: CloudRequestCaptureHandler, code quality (partial)

---

## Phase 18: Convergence

- [X] T104 Return HTTP 200 with server-templated facts in `backend/Zharfa.Api/Controllers/AiController.cs` when phrasing `CompleteAsync` times out or fails after tool dispatch succeeds with facts per FR-014, SC-001, FR-013 (partial)
- [X] T105 [P] Add unit test in `backend/Zharfa.Api.Tests/Ai/PurchasingCitedAnswerTests.cs` verifying phrasing timeout falls back to server-templated cited 200 while liveness/dispatch failure returns 503 assistant-unavailable per FR-014, FR-019, SC-001 (missing)





