# Research: Local AI over Purchasing (Release 1, Phase 4)

**Feature**: `specs/006-local-ai-purchasing` | **Date**: 2026-11-18 | **Plan**: [plan.md](plan.md) | **Spec**: [spec.md](spec.md)

Skip mode: no questions were asked. Owner-facing assumptions are in R20. Phases 1–3 stay in force; this document only adds to them.

Code facts this research relies on (worktree, 2026-11-18):

- `IAiProvider` (`CompleteAsync`, redacted `ProviderStatus`) with `CloudAiProvider` (OpenAI-compatible HTTP, `AiProvider__*`, HttpClient `"ai"` timeout 30 s) and `OllamaAiProvider` stub (`IsConfigured` false, `NotSupportedException`). `Program.cs` picks Ollama only when `AiProvider:Name` is `ollama`.
- `AiController` `POST /api/ai/chat` is `[Authorize(Policy = ai.query)]` and `[DemoOnly]`; Demo 1 system prompt and `AnalystTools` allowlist `sales_overview` / `receivables` / `inventory_alerts` (2 calls, 5 s/tool, 20 s phase). Answers hard-code `dataLabel: demo`. Frontend `AiChat` lives on the Demo 1 `Dashboard` and is gated by `modules.ai` (`demo && ai.query`).
- Admin `GET /api/admin/provider` is `[DemoOnly]`; no in-app enablement; keys never leave env. Phase 3 already catalogued `settings.ai_provider.changed` and reserved AI-question audit until this phase. `AuditLogger` cuts `target` to 200 characters, scrubs secrets, swallows write failures.
- Purchasing figures already exist in `PurchasingOverviewService` and `PurchasingReportService` (`fn_purchase_lines`, `JalaliPeriod`, ی/ک `fn_search_key`). Vendor financials stay reserved (`data unavailable` / 403). `install_setting(key, value, set_at)` holds `data_mode`.
- Next migration number is `0009`. No new NuGet/npm packages in Phases 1–3.

---

## R1. Local provider is OpenAI-compatible HTTP on `IAiProvider` (FR-001, F12 AC1)

**Decision.** Implement `OllamaAiProvider` as a real HTTP client to an OpenAI-compatible chat-completions URL (Ollama's `/v1/chat/completions`). Extract the wire mapping already in `CloudAiProvider` into an internal `OpenAiCompatibleChatClient` used by both providers. Local config is `LocalAi__Endpoint` and `LocalAi__Model` (optional `LocalAi__Name`, default `ollama`). No local key. Cloud stays `AiProvider__Name/Endpoint/Model/ApiKey`.

**Rationale.** The seam the PRD names already speaks that protocol; a second SDK would be a new dependency. Ollama exposes the same path.

**Alternatives.** Native Ollama `/api/chat` (second payload shape, no reuse). Vendor SDK (forbidden by existing “no provider named / no SDK lock-in”). Keep the stub (fails F12 AC1).

---

## R2. Answering provider is selected from a persisted cloud flag, not from `AiProvider__Name` (FR-001, FR-008)

**Decision.** Fresh install: cloud off (missing `install_setting` key `ai.cloud.enabled` means false); answers go to `OllamaAiProvider`. While `ai.cloud.enabled=true`, **every** `CompleteAsync` goes to `CloudAiProvider`; local is unused. Cloud failure or unconfigured cloud while enabled → assistant-unavailable; **no** fallback to local or another host. `Program.cs` no longer switches implementation by name `ollama`.

**Rationale.** Spec session 2026-11-18: cloud is the sole answering provider until disabled.

**Alternatives.** Env-only toggle (cannot record owner-approval note). Dual-provider round-robin (violates “no silent switch”).

---

## R3. Cloud enablement lives in `install_setting`, keys stay in env (FR-002, FR-008–FR-011)

**Decision.** Reuse `install_setting`:

| key | value |
|---|---|
| `ai.cloud.enabled` | `true` / `false` (absent = false) |
| `ai.cloud.approval_note` | last non-empty owner-approval note (kept after disable for history; a new enable requires a **new** note in the request body, which overwrites this key) |
| `ai.cloud.approved_by` | Admin username of the last successful enable |
| `ai.cloud.approved_at` | ISO timestamp of that enable |

Admins never type keys in the app. Connection test and Settings page show redacted name/endpoint/model/configured/enabled only. Whitespace-only notes are missing.

**Rationale.** Existing KV table; no new entity for four fields. Note is not a secret; still scrubbed by `RedactionHelper` before audit.

**Alternatives.** New singleton table (extra migration, same data). Store the note only in audit (spec says stored with the setting).

---

## R4. Settings → AI is a production Admin page (FR-003–FR-005)

**Decision.** New `AiAdminController` at `/api/admin/ai` (`admin` policy, **not** `[DemoOnly]`): GET status, POST connection test, POST enable/disable cloud. `GET /api/admin/provider` stays Demo 1 / `[DemoOnly]` and continues to return the **cloud** env redaction (never the key) so existing demo diagnostics do not change meaning. SPA: `#/settings/ai`, `modules.aiSettings` = `admin` permission.

Connection test: `CompleteAsync` with empty tools and a one-line Persian ping, 10 s timeout; response is `{ ok, message }` with `RedactionHelper.Scrub` on the message. Audit `settings.ai_provider.changed` for enable/disable/test (`detail` = `op=enable|disable|test; target=local|cloud; ok=…`, plus `noteRecorded=true` on enable — never the key, never the full note if it might be long; store a boolean and the actor). The note itself remains in `install_setting`.

**Rationale.** F12 AC4 and Phase 3 reserved action. Lifting `[DemoOnly]` on the old provider GET would mix Demo 1 sales-AI status with the new two-provider page.

---

## R5. Purchasing tools call existing report services (FR-013–FR-017)

**Decision.** New `PurchasingAnalystTools` (separate from Demo 1 `AnalystTools`) allowlist. **Revised:** the model does not choose among these; R21–R22 dispatch. The list is still the only executable names:

| Tool name | Source | View / source metric |
|---|---|---|
| `purchasing_overview` | `PurchasingOverviewService` | Overview net/gross/returns/VAT, comparison, 12-month trend, active vendors, top 5 vendors/products, last-sync, data mode |
| `purchasing_by_vendor` | `PurchasingReportService.GroupedAsync("by-vendor")` | by-vendor |
| `purchasing_by_product` | `GroupedAsync("by-product")` | by-product |
| `purchasing_by_category` | `GroupedAsync("by-category")` | by-category |
| `purchasing_trend` | `TrendAsync` | trend |
| `purchasing_top_products` | `GroupedAsync("top-products")` | top-products |
| `purchasing_returns` | `ReturnsAsync` | returns |
| `purchasing_transactions_summary` | `DocumentsAsync` **headers + totals only** | transactions (summaries) |

Shared rules: period via `JalaliPeriod` (omit → Overview default = current Jalali month in the sync time zone). Vendor/product/category filters resolved with the same lookup + `fn_search_key` as reports. Page size 100, sort default net desc. If `totalRows > 100`, that tool returns success=false and the model is told the metric is «داده در دسترس نیست» (row cap). Per-call timeout **10 s**; overall answer budget **30 s** (raise HttpClient `"ai"` and the chat CTS to 30 s; Demo 1 tools keep 5 s / 20 s). Max **3** purchasing tool calls per question (3×10 s fits the 30 s budget). No document-line tool, no vendor-financial tool, no SQL, no Mabna.

Purchasing role: financial tools do not exist, so balance/payment/cheque questions cannot obtain figures. Management/Finance in this phase also get «داده در دسترس نیست» for those topics (Phase 6). A tool that would need `vendor.financials` is never registered.

**Rationale.** Same numbers as reports (SC-010). Splitting tools from Demo 1 avoids mixing sales KPIs into production answers.

**Alternatives.** One mega-tool (worse allowlist, larger payloads). Calling report HTTP internally (auth recursion). Generating SQL (forbidden).

---

## R6. Two chat endpoints so Demo 1 sales AI never answers production (FR-012, FR-021)

**Decision.**

- `POST /api/ai/chat` — purchasing assistant; **remove** `[DemoOnly]`; `ai.query`; `PurchasingAnalystTools`; `dataLabel` from `DataModeOptions`.
- `POST /api/ai/demo/chat` — existing Demo 1 loop; `[DemoOnly]` + `ai.query`; `AnalystTools` unchanged.

Production hide-and-refuse of Demo 1 sales/inventory/receivables stays. Demo-mode purchasing answers carry the demonstration label.

**Rationale.** Spec: production AI is purchasing only; Demo 1 Q&A remains on demo pages.

---

## R7. Navigation modules (FR-012, FR-018, FR-032)

**Decision.** `SystemModules`:

- `ai` = `ai.query` (both data modes) — purchasing assistant `#/assistant`.
- `demoPages` unchanged — Dashboard; its embedded `AiChat` posts to `/api/ai/demo/chat` and is shown only when `demoPages` (not `ai`).
- `aiSettings` = `admin` — Settings → AI.

Admin has no `ai.query`, so `modules.ai` is false; direct `POST /api/ai/chat` is 403 + `access.denied`. Unsigned 401.

Assistant UI: one current question and one current answer; submitting replaces both. Leaving the hash route unmounts state (no `sessionStorage`). A second submit while waiting is not sent; if it is, the API returns 409.

**Rationale.** Spec: no saved chat; Admin does not ask.

---

## R8. Independent turns and default period (FR-030)

**Decision.** Each purchasing chat builds messages as `[system, user]` only. No previous questions, answers, or tool results. Client body is `{ "question": "…" }` only — **no** `from`/`to`/`categoryId` on this endpoint, so a leftover dashboard period cannot leak. If tools omit a period, the server binds the current Jalali month (same as Overview). System prompt states that rule and forbids inventing figures.

**Rationale.** Spec session 2026-11-18.

---

## R9. One in-flight question per user (FR-031)

**Decision.** Singleton `AiQuestionGate`: `ConcurrentDictionary<long, byte>` keyed by `app_user.user_id`. Try-enter at the start of `POST /api/ai/chat`; always release in `finally`. Second request from the same user: **409** `{ "message": "لطفاً صبر کنید تا پاسخ پرسش فعلی آماده شود.", "code": "ai-busy" }` and no second `CompleteAsync`. Other users are independent. Demo chat is unchanged (demo-only, low traffic) but may share the gate by user id so a user cannot run both at once.

ponytail: in-process only; the LAN compose runs one API replica. A second replica would not share the gate — upgrade to a PostgreSQL advisory lock if the API is scaled.

**Rationale.** Smallest thing that matches the spec on the shipped topology.

---

## R10. Facts vs interpretation, unavailable vs zero vs down (FR-014–FR-020, FR-023)

**Decision.** Keep the Demo 1 response shape (`answer`, `facts[]`, `interpretation`, `period`, `filters`, `view`, `unavailable`, `dataLabel`) and add `demoNotice` only in demo mode (empty/omitted in production). Server-built `facts` come only from tool JSON (period, filters, metric, numbers). Model text is `interpretation`; copy into `answer` only when it does not contradict facts (R22). Out-of-domain / no tools: `unavailable: true`, answer «داده در دسترس نیست», empty interpretation, **HTTP 200**. Provider errors: **503** with `code=assistant-unavailable` (R23), not the metric phrase. Empty but valid period: tools return zero like Overview. No tables/charts/export fields.

---

## R11. Audit of questions (FR-024, FR-025)

**Decision.** New action `ai.question` (Persian «پرسش از دستیار هوشمند»). On every authenticated purchasing (and demo) chat **after** permission success, including 409, 503 and metric-unavailable: `AuditEntry(Action=ai.question, Target=trimmed question, Detail=period/filters/tools/outcome)`. `target` uses existing 200-character cut. Detail allowlist: `period`, `from`, `to`, `vendor`, `product`, `category`, `tools` (comma-separated names), `outcome=ok|unavailable|busy|provider`. Never facts, amounts, keys, prompts, or tool JSON. Enable/disable/test use existing `settings.ai_provider.changed`. Filter catalog in `AuditActions.All`. Audit write failure: log without secrets, still return the answer (Phase 3 FR-018).

---

## R12. Cloud payloads are tool aggregates only (FR-009)

**Decision.** The only content sent to `CloudAiProvider` is the system prompt, the user question, and (for in-domain) the **already-executed** purchasing aggregate JSON (totals, counts, ranked **names**, period labels) — not live tool specs for the model to pick from (R22). Tools never serialize document lines, customer/user PII, source keys beyond the vendor/product **names**. A unit test snapshots a representative payload (R12). **Live** capture is mandatory on the 9router overlay (R25). Never commit live Mabna rows.

**Rationale.** Same aggregates the user could read in reports.

---

## R13. 20-question fixture scores against live reports, not a second number sheet (FR-022, SC-001)

**Decision.** Committed [fixtures/purchasing-questions.json](fixtures/purchasing-questions.json): exactly 20 Persian items with `id`, `question`, `askAs`, `expect`, `metric`, `period`, and **`scope`** (R21). Expected **amounts** are not stored. SC-001 is **20/20** — not weakened. The scorer asks `/api/ai/chat` as `askAs`, then GETs the matching Overview/report: `cited` requires HTTP 200, `unavailable=false`, facts cite `metric` and match the report; `unavailable` requires HTTP 200 (not 503), `unavailable=true`, empty facts, metric phrase, **no** purchasing tool names in audit `tools`; `no-financials` has no balance/payment/cheque amounts; `zero` is zero not unavailable. Item 20 is scored only on a DB with **no** succeeded sync (quickstart C2).

**Rationale.** Spec assumption: handwritten totals drift.

---

## R14. Benchmark procedure (FR-006, FR-007, SC-002, SC-003)

**Decision.** Documented in [contracts/benchmark.md](contracts/benchmark.md): run the **pinned** 7–8B candidates (R24) on this host’s Ollama. For each: `ollama pull` if needed, set `LocalAi__Model`, restart API, run all 20 items, record accuracy + median/p95 ms. Default = max accuracy, then min median. Missing after pull fail → `failed`. Need ≥2 completed candidates. SAMPLE.md must not invent 14b 20/20. Product default remains local even if median >30 s.

---

## R15. Demo 1 `AiController` tests and `[DemoOnly]` matrix

**Decision.** Update `DemoOnlyFilterTests`: `AiController` purchasing `Chat` must **not** be demo-only; add `DemoChat` (or `AiDemoController`) that is. `PermissionMatrixTests` lists both actions as `ai.query` and the new admin AI actions as `admin`. `SystemModules` tests: production + management → `ai true`; production + admin → `ai false`, `aiSettings true`.

---

## R16. No new packages; Ollama is not in the default compose file (FR-028)

**Decision.** No new NuGet/npm. Optional `deploy/docker-compose.ollama.yml` (profile `ollama`) documents a sibling Ollama for the LAN server; default `docker-compose.yml` only gains `LocalAi__*` env passthrough. Development may point `LocalAi__Endpoint` at a host Ollama. Benchmark records in git contain no live rows.

**Rationale.** Default compose must keep starting without a GPU/model pull.

---

## R17. Question length

**Decision.** Purchasing chat: 1–2000 characters (same as Demo 1). Audit target 200. Model sees the full 2000.

---

## R18. HTTP client and logging

**Decision.** Reuse named client `"ai"`. Log iterations, tool **names**, duration — never bodies, URLs with keys, or facts. `AiException` stays Persian-safe.

---

## R19. Documentation set

**Decision.** README, `deploy/README.md`, `deploy/.env.example`, `backend/Zharfa.Api/README.md`, `.ainative/project.yaml` comment (mention spec 006 tests + quickstart). No CHANGELOG bump (speckit plan does not version).

---

## R20. Owner-facing assumptions (skip-mode)

- Local endpoint on the company server is Ollama’s OpenAI-compatible `/v1` unless the operator points `LocalAi__Endpoint` at another compatible local server.
- Default `LocalAi__Model` in `.env.example` is empty until the **live** R24 benchmark names a winner; an empty model with a reachable endpoint is “unconfigured” (503 assistant-unavailable), not a guessed model. `qwen2.5:14b` is not assumed present.
- 409 + `ai-busy` is the wait refusal.
- Audit question text is 200 characters because that is the existing `target` cut.
- Demo 1 dashboard chat remains a demo-mode extra, not the Phase 4 assistant.

No NEEDS CLARIFICATION remain from the original plan.

---

## Tester LOOP_BACK (2026-11-18) — facts this revision uses

Evidence: `specs/006-local-ai-purchasing/evidence.md`, `flow-C-fixture-summary.json`, `flow-C-q13.body.txt`, `flow-F-chat-down.*`, `flow-H-dev-qwen2.json`, `flow-results.json`.

- Accuracy **10/20** on the only pulled model `qwen2:7b`. SAMPLE.md’s `qwen2.5:14b` 20/20 was **not** run on this host (`ollama list` shows only `qwen2:7b`; host RAM 16 GB).
- **Q13** (فروش داروخانه‌ها): HTTP 200, `unavailable=false`, tools=`purchasing_overview`, answer used purchase **net** as pharmacy **sales**. Prompt-only refusal failed.
- **Flow F**: after intended Ollama stop, chat still **200** with Overview facts (`unavailable=false`). The API talks to `http://host.docker.internal:11434/v1` (host Ollama), not a compose `ollama` service — killing the wrong process leaves the model up. Distinction from metric-unavailable was not asserted via `code`.
- **Flow E**: Admin enable with note succeeded (`ownerApprovalRecorded=true`) but the follow-up cloud chat was **503 network** to `http://shared_9router:20128/v1`. No aggregates-only outbound body was saved.
- Cited misses also came from **model-chosen tools** (wrong view, 30 s timeout → 503 on Q5/Q6/Q9/Q18, current month on Q19, Q20 scored on a DB that already had a succeeded sync).
- Operator keep-PRD: SC-001 stays 20/20; pin 2–3 **runnable** models; out-of-domain at **allowlist/tool layer**; provider down → **503 assistant-unavailable**; live cloud capture + 2–3-model record.

---

## R21. Out-of-domain is a server scope gate, not a prompt hope (Q13)

**Decision.** Add `PurchasingQuestionRouter` (pure C#, no new package). It runs **before** tool specs are sent and **before** `ExecuteAsync`. Scopes:

| `scope` | Fixture ids | HTTP | Tools offered / executed | Model |
|---|---|---|---|---|
| `purchasing` | 1–12, 19 | 200 cited or zero | **Only** the mapped tool(s) in R22, executed by the server | One `CompleteAsync` with **no** tool specs (phrasing only) |
| `out-of-domain-sales` | 13 | 200 metric-unavailable | **None** | **Not called** |
| `out-of-domain-inventory` | 14 | 200 metric-unavailable | None | Not called |
| `vendor-financials` | 15–17 | 200 unavailable / no-financials | None | Not called |
| `mabna-bypass` | 18 | 200 metric-unavailable | None | Not called |
| `no-successful-sync` | 20 | 200 metric-unavailable when no succeeded job | None | Not called |

Matching is deterministic Persian tokens (normalized ی/ک, Arabic Yeh) plus the fixture `scope` field as the test oracle — **not** LLM classification. Examples: فروش / داروخانه / نسخه → sales; موجودی / انبار / کسری کالا → inventory; مانده / بدهی / پرداخت / چک / اسناد پرداختنی → vendor-financials; مبنا / HOST_ID → mabna-bypass.

If the mapped scope is not `purchasing`, `PurchasingAnalystTools` is not invoked. The 200 body is `unavailable: true`, `facts: []`, `view: null`, answer «داده در دسترس نیست.». Purchasing role never sees a balance/payment/cheque amount because those tools still do not exist **and** the router never runs `purchasing_overview` for those questions.

**Rationale.** Tester Q13 proved a 7B model will call `purchasing_overview` and relabel net as sales. FR-013 / AGENTS.md require allowlist enforcement on the server. SC-001 is not relaxed.

**Alternatives.** Stronger system prompt (already failed). Asking the model to return a scope tag (same failure mode). English keyword-only lists (misses the fixture language).

---

## R22. Server-dispatched tools + server-authored cited answers (20/20 on 7–8B)

**Decision.** For `scope=purchasing`, the router returns `{ tool, periodRule }` from the question (see mapping in [contracts/tools.md](contracts/tools.md)). `AiController` **executes that tool** with `JalaliPeriod` bound by the rule (`overview-default`, `previous-jalali-month`, `current-jalali-quarter`, `future-empty-month`). The model does **not** receive tool specs and cannot pick a different view.

Then **one** `CompleteAsync` with empty tools: system prompt + user question + a single assistant-visible facts block (the same `facts[]` that will go in the HTTP body). If `CompleteAsync` fails → R23 (503). If it returns, `interpretation` = model text; `answer` = model text **only if** it does not invent a number absent from facts and does not say «داده در دسترس نیست» when facts exist — otherwise `answer` is a server template from facts (period, filters, metric, ریال). Tests score `facts` vs Overview/report (SC-010) and require `unavailable=false` for `cited`/`zero`.

`future-empty-month` (item 19): bind the next Jalali month after the Overview default that the reports would show as zero net — **not** the current month (tester bound آبان ۱۴۰۵).

**Rationale.** 10/20 on `qwen2:7b` was mostly wrong or missing tool calls and 30 s timeouts from extra model/tool loops. Dispatching one tool keeps the 30 s budget. Server facts keep SC-010 even if prose is weak.

**Alternatives.** Keep model tool-calling and require 14b (operator: 14b not required if absent/heavy). Always-call-overview for every question (recreates Q13).

---

## R23. Provider down is 503 `assistant-unavailable`; metric gap is 200 (Flow F)

**Decision.** At the start of `POST /api/ai/chat`, after the in-flight gate:

1. If answering provider unconfigured → 503.
2. **Liveness probe** of the answering provider (HTTP to the OpenAI-compatible endpoint, empty tools, 2–3 s timeout, or a failed TCP connect). Failure → 503 **without** running purchasing tools.
3. Any later `CompleteAsync` / `AiException` / answer-budget cancel → 503, empty facts.

503 body: existing Persian assistant-unavailable `answer`/`message`, `unavailable: true`, **`code`: `assistant-unavailable`**, empty `facts`. Never the metric phrase «داده در دسترس نیست» as the sole answer.

200 metric-unavailable (R21): **no** `code` (or not `assistant-unavailable`), answer contains «داده در دسترس نیست».

Flow F on **this** host: stop the listener that `GET /api/admin/ai` reports (`host.docker.internal:11434` / host `ollama`), confirm `curl` to that `/v1` fails, then chat 503 **and** Overview + a purchasing report 200. Restart Ollama; chat works again. Do not kill a compose `ollama` service that the API is not using.

**Rationale.** Tester Flow F returned 200 with Overview facts because host Ollama was still up (or tools ran and the controller treated tool JSON as a successful answer). FR-019/SC-006 require a distinct service message and live dashboards.

---

## R24. Pin three runnable 7–8B candidates on this host (not 14b)

**Decision.** Pinned candidates for the Phase 4 benchmark **on this workstation** (2026-11-18):

| # | Model | Status on host | Notes |
|---|---|---|---|
| 1 | `qwen2:7b` | **pulled** (`dd314f039b9d`, 4.4 GB) | Tester default; 10/20 **before** R21–R22 |
| 2 | `qwen2.5:7b` | pull at implement/benchmark | ~4.7 GB; Persian-capable 7B |
| 3 | `llama3.1:8b` | pull at implement/benchmark | ~4.9 GB; already named in the old SAMPLE |

Do **not** require `qwen2.5:14b`. 16 GB RAM plus Postgres, Mabna sim, and the API makes 14b not a runnable candidate here. `deploy/docker-compose.ollama.yml` comment and README examples that still say 14b are updated to “winner of the live record (R24 candidates)”.

`.env.example` `LOCAL_AI_MODEL` stays empty until the live three-model run names a winner (R20). Implement sets the API to the winner after [contracts/benchmark.md](contracts/benchmark.md). Committed SAMPLE.md is a **template**: candidates listed, accuracy/times filled only by a real run — never invent 20/20.

Missing candidate after a documented `ollama pull` failure → `failed` in the record, not 0%. Need **at least two** completed runs to choose a default (FR-006). If only one model pulls, the run is blocked, not faked.

**Rationale.** Operator: pin 2–3 runnable models; three 7–8B is valid if 14b is too heavy.

---

## R25. Live cloud enable + aggregates-only capture (9router overlay)

**Decision.** Flow E is not done until all of:

1. Compose includes `deploy/docker-compose.9router.yml` (API on `hermes-9router-net`). From the **api container**, `GET`/`POST` to `AiProvider__Endpoint` (`http://shared_9router:20128/v1`) succeeds or the flow is **FAIL** (do not skip).
2. Admin `POST /api/admin/ai/cloud` with a non-empty owner-approval note → 200, `enabled=true`, `ownerApprovalRecorded=true`, audit `noteRecorded=true`.
3. Management `POST /api/ai/chat` with an in-domain purchasing question → **200** (not 503).
4. **Capture** the outbound chat-completions JSON (Development-only `DelegatingHandler` on named client `"ai"`, path from `AiProvider__CapturePath`, never logs `Authorization` / api keys). Evidence file is the capture (redact nothing that is an aggregate; strip headers). Assert: no `lines` arrays, no passwords/keys/national id/phone; tool JSON is totals, counts, ranked **names**.
5. Disable cloud → local answers again.

Unit snapshot tests (R12) remain; they do **not** replace step 4.

**Rationale.** Tester enabled cloud but the chat was 503 network — SC-005 needs a captured body, not only the enable audit row.

---

No NEEDS CLARIFICATION remain.
