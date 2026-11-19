# Sample Benchmark Record: Local AI over Purchasing

**Date**: (fill on live run — do not invent scores)
**Environment**: Dev workstation Ollama (`host.docker.internal:11434`), synthetic Mabna simulator dataset, 16 GB RAM
**Fixture**: `specs/006-local-ai-purchasing/fixtures/purchasing-questions.json` (version 2, with `scope`)
**Fixture SHA-256**: (sha256 of the committed fixture at run time)
**Total Questions**: 20 (Persian purchasing test set)
**SC-001**: 20/20 required on the **chosen default** after R21–R22 implement. Prior tester on `qwen2:7b` without the router was 10/20 and is **not** the record.

## Pinned candidates (this host)

`ollama list` 2026-11-18: only `qwen2:7b` present. `qwen2.5:14b` is **not** a candidate (not pulled; too heavy for 16 GB with the stack).

| Model | Accuracy | Passed / Total | Median Response Time | p95 Response Time | Status |
|---|---|---|---|---|---|
| `qwen2:7b` | — | — / 20 | — | — | Pulled — run after implement |
| `qwen2.5:7b` | — | — / 20 | — | — | Pull then run |
| `llama3.1:8b` | — | — / 20 | — | — | Pull then run |

### Notes

- Evaluation criteria: Max accuracy across the fixed 20-question fixture; ties break to faster median response time.
- All candidate runs use server-side scope gate + dispatched purchasing tools (research R21–R22).
- No confidential row extracts or database line data are stored in this record.
- If a typical cited answer exceeds 30 s, still name a local default and write the measured times.

## Chosen Default

```text
LocalAi__Model=<winner of the live three-model run>
```
