# Benchmark and accuracy procedure (Phase 4)

## Fixture

[fixtures/purchasing-questions.json](../fixtures/purchasing-questions.json) — exactly 20 items. Identity for records: file path + SHA-256 of the file. **SC-001 is 20/20** on this set (not weakened).

Do **not** store expected amounts. After a successful simulator sync (except item 20), score each item by:

1. `POST /api/ai/chat` as `askAs` (demo users `management` / `purchasing`).
2. Classify the HTTP/body:
   - `expect=cited`: **200** (never 503), `unavailable=false`, facts cite `metric` and period rule; amounts equal the corresponding Overview or report GET for that period/filters (Persian digits allowed). Audit `tools` matches the mapped purchasing tool.
   - `expect=unavailable`: **200** (never 503), `unavailable=true`, answer contains «داده در دسترس نیست», **`facts` empty**, no invented amounts. For `scope` starting with `out-of-domain` / `vendor-financials` / `mabna-bypass`, audit must **not** list `purchasing_overview` (Q13 failure mode).
   - `expect=zero`: 200, net/total fact is zero, not unavailable; period is a **future** empty Jalali month, not the current Overview month.
   - `expect=no-financials`: 200, no balance/payment/cheque amounts (regex on digits next to those words); Purchasing vendor-balance item; no financial tools.
3. Record wall time per question.
4. HTTP **503** `code=assistant-unavailable` is a **fail** for every fixture `expect` (those items are metric/cited paths). 503 is only correct in Flow F.

Accuracy = share of items whose classification matches `expect` (and citation/amount match when `cited`). Target: **20/20** on the default model (SC-001).

## Pinned candidates (this host — do not require 14b)

From `ollama list` + 16 GB RAM (2026-11-18):

1. `qwen2:7b` — already pulled.
2. `qwen2.5:7b` — pull.
3. `llama3.1:8b` — pull.

`qwen2.5:14b` is **not** a candidate here. Three 7–8B models satisfy F12 AC2.

## Candidate run (F12 AC2)

On **this** Ollama (`LocalAi__Endpoint`, today `http://host.docker.internal:11434/v1`):

1. `ollama pull` each pinned model. A missing model **after** pull failure is `failed` / unavailable, not 0% accuracy. At least **two** candidates must complete or the benchmark is blocked.
2. For each candidate: set `LocalAi__Model`, restart API, wait until `GET /api/admin/ai` shows that model and `configured: true`, cloud `enabled: false`.
3. Run the fixture (Management for scoring; additionally run the `no-financials` items as Purchasing). Item 20 only on a no-succeeded-sync DB (quickstart C2).
4. Write a dated record: fixture hash, host (no secrets), per candidate `{ model, accuracy, medianMs, p95Ms, failedQuestions[] }`, chosen default = max accuracy, then min medianMs. If no candidate is under 30 s typical (median of cited items), still name a local default and state the times.

Committed records use simulator/demo timings or pass/fail only. [benchmark/SAMPLE.md](../benchmark/SAMPLE.md) is a **template** until a live three-model run fills it — do not invent 20/20 for an unpulled 14b.

## Cloud capture (exit)

Demo/simulator + `deploy/docker-compose.9router.yml`:

1. From the **api container**, confirm the cloud endpoint is reachable (otherwise FAIL, do not skip).
2. Enable cloud with a non-empty owner-approval note.
3. Ask one in-domain cited purchasing question; require HTTP **200**.
4. Save the outbound chat-completions JSON (`AiProvider__CapturePath`, Development). Assert: no line arrays, no keys, only aggregates (names + amounts + counts).
5. Never commit live Mabna extracts.
