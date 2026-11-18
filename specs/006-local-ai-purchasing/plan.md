# Implementation Plan: Local AI over Purchasing (Release 1, Phase 4)

**Branch**: `zharfa-prd-phase-4` | **Date**: 2026-11-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/006-local-ai-purchasing/spec.md` · Phase ref `zharfa-prd.phases.md#4` · Governing PRD `zharfa-prd.md` v3.0 (F12, F13, F10 AI events) · Rules `AGENTS.md` · Foundation Phases 1–3 (`specs/003-real-purchase-data`, `specs/004-vendor-purchasing-reports`, `specs/005-permissions-audit-quality`) and Demo 1 `IAiProvider` seam

**Revision**: `unpark-plan-1` (tester LOOP_BACK). SC-001 stays **20/20**. Do not implement in this plan pass.

## Summary

A Management (also Purchasing/Finance) user asks Persian purchasing questions and gets cited answers from a **local** model on the company server by default. Admin configures providers on Settings → AI (redacted status, connection test, cloud opt-in with an owner-approval note). Answers use the same Overview/report metrics, never Mabna, never vendor financials in this phase, never mix real and demo data. Provider outages leave dashboards up. Each question is independent, audited as text only, and not saved as chat.

Technical approach (details in [research.md](research.md)):

- **Implement the Ollama seam** with the existing OpenAI-compatible HTTP mapping extracted from `CloudAiProvider` (R1). Env: `LocalAi__*` vs `AiProvider__*` (cloud keys stay in secrets).
- **Answering provider** from `install_setting` `ai.cloud.enabled`: local default; cloud exclusive while on; no silent fallback (R2, R3).
- **Domain + tool layer (revision)**: a deterministic `PurchasingQuestionRouter` classifies the question **before** tools or model. Out-of-domain (sales, pharmacy, inventory, warehouse) and vendor-financials / Mabna-bypass never receive purchasing tool specs and never execute them — HTTP **200** «داده در دسترس نیست», empty facts (R21). In-domain questions **server-dispatch** the mapped purchasing tool(s); the model does not choose tools (R22). Cited `answer` is server-authored from tool facts if the model omits or contradicts them (R22). **SC-001 remains 20/20** on the Persian set.
- **503 vs 200 (revision)**: probe the answering provider first. Ollama/cloud down, unconfigured, or `CompleteAsync` fail → **503** `code=assistant-unavailable`, empty facts. Metric-unavailable stays **200** `unavailable=true` with the metric phrase, never that code (R23). Overview/reports stay 200.
- **Pinned local candidates on this host (revision, R24)**: do **not** require `qwen2.5:14b`. This workstation (16 GB RAM, `ollama list` 2026-11-18) has only `qwen2:7b`. Benchmark **three 7–8B** models: `qwen2:7b` (present), `qwen2.5:7b`, `llama3.1:8b`. Default = max accuracy then faster median from a **live** record. SAMPLE.md is format-only until that run.
- **Cloud capture (revision, R25)**: real enable with owner-approval note on the tester 9router overlay; one **200** purchasing chat; save the outbound aggregates-only JSON (no key). A 503 to 9router is a failed capture, not a skip.
- **Purchasing tools** wrap `PurchasingOverviewService` / `PurchasingReportService` with 100-row / 10 s / 30 s caps (R5). Demo 1 tools stay on `POST /api/ai/demo/chat` (`[DemoOnly]`); `POST /api/ai/chat` is not DemoOnly (R6).
- **UI**: `#/assistant` for `ai.query`; `#/settings/ai` for Admin; Dashboard chat stays demo-only (R7). No chat store; in-process one-question-per-user gate (R8, R9).
- **Audit**: `ai.question` + existing `settings.ai_provider.changed` (R11).
- **Fixture + benchmark** score against live reports (R13, R14); fixture items carry `scope` for the router and scorer.
- **No new packages**; optional Ollama compose profile; migration `0009` seeds cloud-off (R16).

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`), TypeScript 7 / React 19, SQL (PostgreSQL 18), optional bash/Python stdlib for the benchmark driver

**Primary Dependencies**: ASP.NET Core (existing cookie auth, `ai.query` / `admin` policies), `IHttpClientFactory` named `"ai"`, Npgsql / existing purchasing services, Vite 8 + Tailwind 4. **No new NuGet or npm packages.**

**Storage**: PostgreSQL `install_setting` keys in [data-model.md](data-model.md); migration `db/migrations/0009_local_ai_purchasing.sql`. No new fact tables. Audit rows in existing `audit_log`.

**Testing**: xunit (`backend/Zharfa.Api.Tests`), pure logic only (payload redaction, tool allowlist, **question router / scope gate**, period default including future-empty month, cloud-flag rules, gate, DemoOnly/matrix, no-financial DTO/JSON, **503 code vs 200 metric-unavailable**). HTTP, models and UI: [quickstart.md](quickstart.md) on Compose + simulator + the pinned local models. Cloud capture uses `deploy/docker-compose.9router.yml`.

**Target Platform**: Linux containers on the company LAN; browsers and unchanged WebView2 shell. Local inference: Ollama (or OpenAI-compatible local server) beside the API.

**Project Type**: Web application (ASP.NET Core API + React SPA).

**Performance Goals**: User-visible answer ≤ 30 s (PRD §8) or the benchmark record states measured times; 10 s per tool; Overview/reports unaffected by provider down (p95 unchanged from Phases 1–2).

**Constraints**: Server-side authz; AI only via allowlisted read-only purchasing tools; secrets in env/Docker secrets; Mabna read-only, reports/AI read the synced copy; purchases ≠ payments ≠ balance; real/demo never mix; Persian RTL, Jalali, ریال; keys never in bundle/API/logs/audit; cloud aggregates only after recorded owner approval.

**Scale/Scope**: ~20 users; one in-flight question per user; 8 purchasing tools; 20-question fixture; 1 migration; 2 new SPA views; ~15 backend files touched plus `PurchasingQuestionRouter`. One API replica (in-memory gate). Three local 7–8B candidates on this host.

**Unknowns**: None open for planning. Owner-facing skip-mode assumptions: research R20. Host model pin: R24 (from `ollama list` + 16 GB RAM; 14b not required).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md` is still the unfilled template, so the binding gates are `AGENTS.md` and `zharfa-prd.md` (§8–§9), as in Phases 1–3.

| Gate (AGENTS.md / PRD) | Pre-design | Post-design | How the design satisfies it |
|---|---|---|---|
| AI answers only through approved read-only views/tools; allowlist, limits, timeouts; cite period/filters or «data unavailable»; facts ≠ interpretation | Pass | Pass | [contracts/tools.md](contracts/tools.md); router denies out-of-domain **before** tools (R21); 100 rows / 10 s / 30 s; `facts` vs `interpretation` (R5, R10, R22) |
| Local model default; cloud opt-in with owner approval; aggregates only | Pass | Pass | `install_setting` flag; Settings → AI; cloud exclusive while on; payload snapshot test (R2–R4, R12) |
| Secrets only in server env / Docker secrets; never bundle, repo, logs | Pass | Pass | Keys stay `AiProvider__ApiKey`; status/test/audit redacted (R3, R18) |
| Authorization server-side; AI-queries = Management, Purchasing, Finance; Admin configures only | Pass | Pass | Policies; `modules.ai` / `aiSettings`; 403 Admin chat (R7) |
| Purchasing cannot see vendor financials in AI answers | Pass | Pass | No financial tools; router `vendor-financials` scope; scorer `no-financials` (R5, R21, fixture 15–16) |
| Mabna read-only; never HOST_ID views live; reports/AI read synced copy | Pass | Pass | Tools call existing PostgreSQL services only (R5) |
| Real and synthetic never mix; demo labelled | Pass | Pass | `DataModeOptions` on purchasing chat; Demo 1 endpoint stays `[DemoOnly]` (R6) |
| Financial formulas stay separate; no price-difference-as-profit | Pass | Pass | Overview/report field names reused; system prompt (R5, R10) |
| Audit append-only, no secrets or result rows | Pass | Pass | `ai.question` target = question text; no tool JSON (R11) |
| Persian-first RTL, Jalali | Pass | Pass | Assistant + Settings pages; Jalali period default (R8) |
| Prefer existing abstractions; justify dependencies | Pass | Pass | `IAiProvider`, HttpClient `"ai"`, report services, `install_setting`, `AuditLogger`; no new packages (R1, R16) |
| Production refused AI until Phase 4 — this phase lifts **purchasing** only | Pass | Pass | Remove `[DemoOnly]` from purchasing chat only (R6, R15) |
| Update docs when behavior changes | Pass | Pass | README, deploy README, `.env.example`, backend README, `project.yaml` comment (R19) |

No violations; Complexity Tracking is empty.

### Post-design re-check

Contracts split purchasing vs Demo 1 AI, persist only the cloud flag + note, and keep inference off-box. The router is not a second product: it is the allowlist enforcement the 7B tool-calling loop failed in tester evidence (Q13 called `purchasing_overview` and labeled net as فروش). Constitution tension is **intentional and in-scope**: Phase 1’s production AI refuse is lifted for `POST /api/ai/chat` while Demo 1 routes stay refused. No unjustified new packages.

## Project Structure

### Documentation (this feature)

```text
specs/006-local-ai-purchasing/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── api.md
│   ├── tools.md
│   └── benchmark.md
├── fixtures/purchasing-questions.json
├── checklists/requirements.md
└── tasks.md                      # /speckit.tasks — not created here
```

### Source Code (repository root) — files to add (A) or modify (M)

```text
db/migrations/0009_local_ai_purchasing.sql          A  data-model §1

backend/Zharfa.Api/
├── Ai/
│   ├── OpenAiCompatibleChatClient.cs               A  shared wire format (R1)
│   ├── OllamaAiProvider.cs                         M  real HTTP local provider
│   ├── CloudAiProvider.cs                          M  use shared client
│   ├── IAiProvider.cs                              M  optional: TestConnection if kept on interface
│   ├── ActiveAiProvider.cs                         A  R2 resolver (cloud flag)
│   ├── AiProviderSettings.cs                       A  read/write install_setting (R3)
│   ├── PurchasingAnalystTools.cs                   A  R5
│   ├── PurchasingQuestionRouter.cs                 A  R21–R22 (scope + mapped tools)
│   ├── CloudRequestCaptureHandler.cs               A  R25 Development-only outbound snapshot
│   ├── AnalystTools.cs                             M  Demo 1 only; keep limits
│   └── AiQuestionGate.cs                           A  R9
├── Controllers/
│   ├── AiController.cs                             M  purchasing chat; drop DemoOnly; gate; audit
│   ├── AiDemoController.cs                         A  POST /api/ai/demo/chat (or extra action)
│   └── AiAdminController.cs                        A  /api/admin/ai*
├── Audit/AuditActions.cs                           M  ai.question
├── Platform/SystemModules.cs                       M  ai, aiSettings
├── Program.cs                                      M  DI: both providers + resolver; LocalAi config
└── appsettings.json                                M  LocalAi__* shape (no secrets)

backend/Zharfa.Api.Tests/
├── Ai/*                                            A  allowlist, router/scope, period default, payload scrub, flag, gate, 503 vs 200
├── Platform/DemoOnlyFilterTests.cs                 M  R15
└── Security/PermissionMatrixTests.cs               M  new actions

frontend/src/
├── App.tsx                                         M  #/assistant, #/settings/ai
├── pages/Assistant.tsx                             A  current Q&A only
├── pages/AiSettings.tsx                            A  Admin
├── pages/Dashboard.tsx                             M  demo chat → /api/ai/demo/chat
├── components/AiChat.tsx                           M  optional reuse for demo path
└── services/api.ts                                 M  types, modules.aiSettings

deploy/
├── docker-compose.yml                              M  LocalAi__* env
├── docker-compose.ollama.yml                       A  optional profile (R16)
├── .env.example                                    M  LocalAi + existing AiProvider
└── README.md                                       M  local default, benchmark pointer

README.md, backend/Zharfa.Api/README.md, .ainative/project.yaml   M
```

**Structure Decision**: Existing `frontend/` + `backend/Zharfa.Api` + `db/migrations`. Inference remains an external process (Ollama), not an in-process model host.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

None.
