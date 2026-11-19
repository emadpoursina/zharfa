# API contract: Local AI over Purchasing (Phase 4)

Cookie session `zharfa_session`. Error envelope `{ "message": "<Persian>" }` plus optional `code`. Times: ISO `at` and Jalali `atJalali` as in Phase 3. Data payloads carry `dataLabel`: `real` | `demo`.

Common statuses: 401 unsigned, 403 missing permission (`code: forbidden`) + `access.denied`, 400 validation, **503** assistant unavailable (`code: assistant-unavailable`, AI only; Overview/reports unchanged **200**), **200** metric-unavailable (`unavailable: true`, **not** `assistant-unavailable`).

## §0. Permission matrix (extends Phase 3 §0)

| Endpoint(s) | Policy | admin | management | purchasing | finance | DemoOnly? |
|---|---|:-:|:-:|:-:|:-:|---|
| `POST /api/ai/chat` | `ai.query` | 403 | ✓ | ✓ | ✓ | **no** |
| `POST /api/ai/demo/chat` | `ai.query` | 403 | ✓ demo | ✓ demo | ✓ demo | **yes** (production 404) |
| `GET /api/admin/ai` | `admin` | ✓ | 403 | 403 | 403 | no |
| `POST /api/admin/ai/test` | `admin` | ✓ | 403 | 403 | 403 | no |
| `POST /api/admin/ai/cloud` | `admin` | ✓ | 403 | 403 | 403 | no |
| `GET /api/admin/provider` | `admin` | ✓ | 403 | 403 | 403 | **yes** (unchanged Demo 1) |

`GET /api/system/status` `modules`:

| flag | rule |
|---|---|
| `ai` | `ai.query` (production and demo) |
| `aiSettings` | `admin` |
| `demoPages` | demo + `overview` (Dashboard; its chat uses `/api/ai/demo/chat`) |

## §1. Purchasing assistant — `POST /api/ai/chat`

Request:

```json
{ "question": "خالص خرید این ماه چقدر است؟" }
```

`question` required, trimmed, 1–2000 characters. No `from` / `to` / `categoryId` (period comes from the question via tools or the Overview default month).

| Outcome | Status | Body |
|---|---|---|
| cited or zero (in-domain, tools ran) | 200 | `AiChatResponse`, `unavailable: false` |
| metric-unavailable (out-of-domain, vendor-financials, mabna-bypass, no sync, row cap) | 200 | `AiChatResponse`, `unavailable: true`, «داده در دسترس نیست», `facts: []`, **no** `code: assistant-unavailable` |
| in-flight question for this user | 409 | `{"message":"لطفاً صبر کنید تا پاسخ پرسش فعلی آماده شود.","code":"ai-busy"}` |
| provider down / liveness fail / timeout / unconfigured / cloud enabled but failing | 503 | `AiChatUnavailableResponse`, **`code`: `assistant-unavailable`**, empty facts, assistant phrasing (not the metric phrase) |
| empty/too long question | 400 | `{ "message": "…" }` |
| no `ai.query` | 403 | standard forbidden |
| unsigned | 401 | standard |

Server order: authz → in-flight gate → **provider liveness** → **scope router** (research R21) → in-domain tool dispatch (R22) or 200 metric-unavailable → one phrasing `CompleteAsync` (in-domain only). Out-of-domain never executes purchasing tools (Q13 must not attach `purchasing_overview` facts).

`AiChatResponse`:

```json
{
  "answer": "…",
  "period": { "from": "2026-10-23", "to": "2026-11-21", "jalaliLabel": "آبان ۱۴۰۵", "month": "1405-08" },
  "filters": { "vendor": null, "product": null, "category": null },
  "view": "purchasing_overview",
  "facts": ["metric=net; period=1405-08; value=…"],
  "interpretation": "…",
  "unavailable": false,
  "dataLabel": "demo",
  "demoNotice": "این پاسخ فقط بر اساس داده‌های نمایشی…"
}
```

`demoNotice` is present only when `dataLabel` is `demo`. `facts` are server-authored; `interpretation` is model text and must not be the source of extra numbers in tests. `answer` for `cited` is model prose only when it stays within facts; otherwise a server template from facts (research R22). `unavailable: true` with Persian «داده در دسترس نیست» when the router or tools cannot answer; this is **200**, not 503.

`AiChatUnavailableResponse`: `unavailable: true`, `"code": "assistant-unavailable"`, `answer` and `message` are the **assistant-unavailable** phrasing (not the metric phrase), empty facts, no keys or stack traces.

Audit: `ai.question` for 200, 409 and 503 after authz (research R11). Not for 401.

Each request is an independent model conversation (research R8).

## §2. Demo 1 assistant — `POST /api/ai/demo/chat`

Body remains `{ question, from?, to?, categoryId? }`. Behavior = today’s `POST /api/ai/chat` (sales/receivables/inventory tools, `[DemoOnly]`). Production: 404 from `DemoOnlyAttribute`.

## §3. Settings — `GET /api/admin/ai`

```json
{
  "answeringProvider": "local",
  "aggregatesOnlyNotice": "در صورت فعال بودن ارائه‌دهنده ابری، تنها نتایج تجمیعی برای آن ارسال می‌شود.",
  "local": {
    "name": "ollama",
    "endpoint": "http://ollama:11434/v1",
    "model": "qwen2:7b",
    "configured": true,
    "isDefault": true
  },
  "cloud": {
    "name": "",
    "endpoint": "",
    "model": "",
    "configured": false,
    "enabled": false,
    "ownerApprovalRecorded": false
  }
}
```

Never `apiKey`. Endpoint strings must not include userinfo or query secrets (strip if present). Non-admin: 403.

## §4. Connection test — `POST /api/admin/ai/test`

```json
{ "target": "local" }
```

`target`: `local` | `cloud`. 200 `{ "ok": true, "message": "اتصال برقرار شد." }` or `{ "ok": false, "message": "<Persian, scrubbed>" }`. Audit `settings.ai_provider.changed` (`op=test`). Does not enable cloud.

## §5. Cloud opt-in — `POST /api/admin/ai/cloud`

Enable:

```json
{ "enabled": true, "ownerApprovalNote": "تأیید مدیرعامل — فقط تجمیع خرید" }
```

| Outcome | Status |
|---|---|
| enabled | 200 same shape as GET |
| missing/whitespace note | 400, local remains answering |
| disable `{ "enabled": false }` | 200, no note required |
| non-admin | 403 |

While `enabled: true`, `POST /api/ai/chat` uses cloud only (research R2). Flow E (quickstart) must capture one **200** cloud chat and the outbound aggregates JSON (research R25). A network 503 is a failed capture.

## §6. `GET /api/system/status`

Adds `modules.aiSettings`. `modules.ai` is true for `ai.query` in production (Phase 1 hide lifted for purchasing assistant only).
