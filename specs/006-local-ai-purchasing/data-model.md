# Data model: Local AI over Purchasing (Release 1, Phase 4)

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Research**: [research.md](research.md)

This phase does **not** add purchasing fact tables. Reports still read `mabna.*` via `fn_purchase_lines`. New persistence is the cloud-opt-in keys on existing `install_setting`, plus audit action rows.

## 1. `install_setting` keys (existing table, migration `0009`)

Table (unchanged): `key text PK`, `value text NOT NULL`, `set_at timestamptz NOT NULL`.

| key | value | set_at | Rules |
|---|---|---|---|
| `ai.cloud.enabled` | `true` or `false` | last change | Absent ⇒ local default. Enable requires a non-empty note in the **same request**. Disable sets `false` and does not require a note. |
| `ai.cloud.approval_note` | free text, trimmed | last successful **enable** | Required non-empty (after trim) to enable. Whitespace-only refused. Not a secret; still passed through `RedactionHelper` if copied to logs. |
| `ai.cloud.approved_by` | Admin username | last successful enable | |
| `ai.cloud.approved_at` | ISO-8601 UTC | last successful enable | |

Disable does not delete the last note (history on the Settings page: “last approval recorded”); the next enable must send a **new** note which overwrites these three keys.

`0009_local_ai_purchasing.sql`: `INSERT … ai.cloud.enabled = false` where missing. Idempotent. No change to `mabna.*`, `audit_log` schema, or append-only trigger.

## 2. Runtime entities (not tables)

### 2.1 AI provider

| Field | Source | Client |
|---|---|---|
| identity (`name`) | `LocalAi__Name` / `AiProvider__Name` | redacted status |
| endpoint | `LocalAi__Endpoint` / `AiProvider__Endpoint` | redacted (host path, no query, no userinfo) |
| model | `LocalAi__Model` / `AiProvider__Model` | shown |
| configured | endpoint+model non-empty (cloud: same; key may be empty for some local-compatible proxies) | boolean |
| enabled/default | local is always “default”; cloud enabled from `ai.cloud.enabled` | |
| answering | cloud if enabled else local | `answeringProvider`: `local` \| `cloud` |
| credentials | env / Docker secrets only | **never** |

### 2.2 Owner-approval note

Non-empty trimmed text on enable. Stored as `ai.cloud.approval_note`. Audit references `noteRecorded=true`, does not copy the full note into `detail` if over 100 characters (value cut via `AuditDetail`).

### 2.3 Purchasing question scope (runtime, not a table)

`PurchasingQuestionRouter` output: `scope` (`purchasing` | `out-of-domain-sales` | `out-of-domain-inventory` | `vendor-financials` | `mabna-bypass` | `no-successful-sync`), optional mapped `tool` name, `periodRule`. Non-purchasing scopes never produce a tool-call entity.

### 2.4 Purchasing tool call

Inputs: tool name (allowlist), `JalaliPeriod` (default current month, or previous / quarter / future-empty per router), optional vendor/product/category **names** resolved server-side, optional `limit` 1..100 (default 100). Outputs: aggregate JSON + server `facts[]` + `view` id. Caps: 100 rows, 10 s. Over row cap ⇒ metric unavailable (not a truncated list). Timeout ⇒ provider/assistant unavailable for the whole answer.

Forbidden: document lines, vendor financials, arbitrary SQL, Mabna. Out-of-domain questions never create this entity (Q13).

### 2.5 AI question / answer

Question: 1–2000 chars, independent. In-flight: at most one per `user_id` (memory gate). Answer: not persisted. Audit stores question text in `audit_log.target` (max 200).

### 2.6 Benchmark record (file, not DB)

Dated markdown/JSON under `specs/006-local-ai-purchasing/benchmark/` (sample) or operator-local (production timings). Fields: date, fixture id/hash, candidates (`qwen2:7b`, `qwen2.5:7b`, `llama3.1:8b` on this host), per-candidate accuracy + median ms, chosen default, notes if 30 s missed. No live row extracts. Do not record an unpulled 14b as 20/20.

## 3. Audit catalog additions

Existing `audit_log` columns: `at`, `actor`, `actor_user_id`, `action`, `target`, `detail`.

| action | Persian label | target | detail (allowlisted keys only) |
|---|---|---|---|
| `ai.question` | پرسش از دستیار هوشمند | question text, cut 200 | `period`, `from`, `to`, `vendor`, `product`, `category`, `tools`, `outcome` |
| `settings.ai_provider.changed` | (existing) | `local` or `cloud` | `op=enable\|disable\|test`, `ok`, `noteRecorded` |

`access.denied` for Admin/non-permission assistant and settings calls (existing handler).

Never in audit or logs: keys, passwords, tool JSON, facts, amounts, full prompts.

## 4. Validation rules

- Cloud enable: `admin` + non-empty note + cloud env configured **or** allow enable while unconfigured (status shows not connected; answers then 503). **Decision:** enable is allowed if the Admin supplies a note; connection is a separate test. Answering while enabled and unconfigured is unavailable (no local fallback).
- Connection test: does not change `ai.cloud.enabled`.
- Tool names outside the allowlist: never executed. Out-of-domain scopes never offer the allowlist.
- Purchasing user + financial intent: router `vendor-financials`; no financial tool; facts contain no balance/payment/cheque amounts (scorer scans digits in those answers).
- Provider liveness fail: 503 `assistant-unavailable`, no tool entity.

## 5. State: cloud flag

```text
off (default) --[Admin + note]--> on --[Admin, no note]--> off
on --[cloud HTTP fail]--> on (still on; user sees unavailable; local unused)
```

## 6. Data mode

`DataModeOptions` labels purchasing answers (`real` | `demo`). Production never reads Demo 1 KPI views. Demo 1 tools stay on `/api/ai/demo/chat` + `[DemoOnly]`.
