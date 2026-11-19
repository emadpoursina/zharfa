# Quickstart: Local AI over Purchasing (Phase 4)

Validation only. Implementation lives in `tasks.md`. Contracts: [api.md](contracts/api.md), [tools.md](contracts/tools.md), [benchmark.md](contracts/benchmark.md). Fixture: [purchasing-questions.json](fixtures/purchasing-questions.json).

**Revision (`unpark-plan-1`):** SC-001 stays **20/20**. Out-of-domain is a **tool-layer** 200. Provider down is **503** `assistant-unavailable`. Benchmark three 7–8B models on this host (not 14b). Cloud capture must be a real 200 + outbound JSON on the 9router overlay.

## Prerequisites

- Docker Compose stack + Mabna simulator (`deploy/docker-compose.yml`, `deploy/docker-compose.mabna-sim.yml`, `deploy/docker-compose.purchasing-dev.yml`) as in Phase 1–3 quickstarts. For Flow E also `-f deploy/docker-compose.9router.yml`.
- Demo data mode (`ZHARFA_DATA_MODE=demo`) for off-site proof.
- Local model: host Ollama reachable as in `GET /api/admin/ai` (`LocalAi__Endpoint`, today `http://host.docker.internal:11434/v1`). Pinned candidates: `qwen2:7b` (pulled), `qwen2.5:7b`, `llama3.1:8b`. Do not require `qwen2.5:14b`.
- Demo users: `management`, `purchasing`, `admin`, `finance` (Phase 3 seeds).

`R` = API origin (e.g. `http://localhost:8080`).

## A. Production hide lifted for purchasing AI; Demo 1 stays demo-only

1. Production-mode (or demo) as Management: `GET $R/api/system/status` → `modules.ai=true`, `modules.aiSettings=false`.
2. `POST $R/api/ai/chat` `{ "question": "خالص خرید این ماه چقدر است؟" }` → not 404.
3. As Admin: `modules.ai=false`; `POST /api/ai/chat` → 403 + audit `access.denied`. Unsigned → 401.
4. Production: `POST /api/ai/demo/chat` and `/api/sales/*` still 404. Demo: Dashboard chat still uses `/api/ai/demo/chat` only.

## B. Local default + Settings → AI (F12)

1. Fresh settings: `GET $R/api/admin/ai` as Admin → `answeringProvider=local`, local `isDefault=true`, cloud `enabled=false`, **no** key fields. Management → 403. Record `local.endpoint` and `local.model` for Flow F (stop **that** listener).
2. `POST $R/api/admin/ai/test` `{ "target":"local" }` → `ok` true/false with Persian message; response/logs/audit have no key.
3. Ask a purchasing question as Management → answered by local (cloud still off).
4. Frontend bundle grep for `AiProvider__ApiKey` / typical key prefixes → zero hits.

## C. Cited purchasing answers (F13, SC-001, SC-010)

1. Successful simulator sync.
2. Run fixture items **1–19** as `askAs` (item **20** is flow C2 only).
3. Every `cited` item: HTTP **200** (not 503), `unavailable=false`, Persian answer; period/filters/metric stated; amounts = Overview or matching report for that scope. Server `facts` match the mapped tool in [tools.md](contracts/tools.md).
4. `unavailable` items (13, 14, 17, 18): HTTP **200**, `unavailable=true`, «داده در دسترس نیست», **empty facts**, **no** `purchasing_overview` in audit `tools`. Q13 must **not** present purchase net as فروش.
5. Item 19 (`zero`): **future** empty Jalali month → zero, not unavailable, not the current Overview month.
6. Item 15–16 as Purchasing: **no** balance/payment/cheque amounts (SC-007). Item 17 as Management: unavailable (Phase 6 not built).
7. Second question omitting period: cites Overview default month, not the previous question’s period (FR-030).
8. Demo install: `dataLabel=demo` and notice present.

Target: **20/20** including C2.

### C2. No sync

On a DB with **no** succeeded sync job (do not score item 20 against a succeeded latest job). Item 20 → 200 unavailable, empty facts. Overview still loads (200). Restore sync before other flows.

## D. In-flight, no chat history (SC-011, SC-012)

1. Start a slow **in-domain** question as Management; immediately POST another → 409 `ai-busy`.
2. As Purchasing, POST during that window → 200 or 503, not blocked by the other user.
3. Leave `#/assistant` and return → no restored answer; Admin audit still has question text.

## E. Cloud opt-in + live capture (F12 AC3, SC-004, SC-005)

1. Stack includes `deploy/docker-compose.9router.yml`. From the **api container**, probe `AiProvider__Endpoint` (`http://shared_9router:20128/v1`). Unreachable → **FAIL** this flow (do not skip).
2. Enable without note → 400; still local.
3. Enable with note → 200, `enabled=true`, screen/aggregates notice, audit `settings.ai_provider.changed` with `noteRecorded=true`, no key. Keep the note text in evidence (not a secret).
4. Ask one **in-domain** purchasing question; require HTTP **200**. Save outbound capture (`AiProvider__CapturePath`) → aggregates only ([benchmark.md](contracts/benchmark.md)).
5. Disable without note → local answers again.
6. Non-admin enable → 403.

## F. Provider stopped (F13 AC5, SC-006)

1. Read `GET /api/admin/ai` `local.endpoint`. Stop **that** process (on this host: host `ollama`, port 11434 — not a compose service the API is not using). Confirm `curl` to `/v1` fails.
2. Overview + a purchasing report → **200** with usual figures.
3. `POST /api/ai/chat` (in-domain question) → **503**, `code=assistant-unavailable`, empty facts, assistant phrasing (not «داده در دسترس نیست» as the only answer), no stack/key.
4. Start Ollama; in-domain question answers **200** without restarting reports.

## G. Audit (F10, SC-008)

As Admin, filter audit: `ai.question` and `settings.ai_provider.changed`. Entries have user, Jalali time, action, bounded question or op. Q13 row must not show `tools=purchasing_overview`. Scan audit, API responses, logs: zero keys, zero tool result rows. Non-admin audit GET still 403.

## H. Benchmark record (exit)

Follow [benchmark.md](contracts/benchmark.md): pull/run `qwen2:7b`, `qwen2.5:7b`, `llama3.1:8b`; dated record names one default from accuracy then median time. SAMPLE.md stays a template until that live run.

## Mapping to phase exit

| Exit criterion | Flow |
|---|---|
| Local provider default behind `IAiProvider` | B |
| Benchmark 2–3 models, default from record | H |
| 20/20 cited or unavailable | C + C2 |
| Purchasing sees no vendor-balance figures | C items 15–16 |
| Cloud enable needs note; capture aggregates | E |
| Provider down; dashboards up | F |
| AI questions audited; no keys | G |
