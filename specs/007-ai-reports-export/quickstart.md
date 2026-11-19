# Quickstart: AI Reports, Saved Reports, Excel Export (Phase 5)

Validation only. Implementation lives in `tasks.md`. Contracts: [api.md](contracts/api.md), [xlsx.md](contracts/xlsx.md). Data: [data-model.md](data-model.md). Research: [research.md](research.md).

Does **not** re-run the Phase 4 20/20 accuracy gate as a Phase 5 exit; Flow G only checks that ranking visuals did not break cited answers on fixture items 1 and 13.

## Prerequisites

- Docker Compose + Mabna simulator as Phase 1–4 (`deploy/docker-compose.yml`, `deploy/docker-compose.mabna-sim.yml`, `deploy/docker-compose.purchasing-dev.yml`).
- Demo data mode. Successful purchasing sync before A–F.
- Demo users: `management`, `purchasing`, `finance`, `admin` (Phase 3 seeds).
- Local answering provider up for Flow A (canonical question). Flows B–F (save/export of **standard** reports) must still work if the provider is then stopped.

`R` = API origin (e.g. `http://localhost:8080`).

## A. Canonical table and chart (F14, SC-001, Phase 5 exit 1)

1. Sign in as Management. `POST $R/api/ai/chat` with English or Persian: compare the top 10 purchased products over the last six months.
2. 200, `unavailable: false`, `visual.table` present, `visual.chart.type=bar`, captions name the six Jalali months ending the Overview default month, ranking by net, metrics labeled.
3. `GET $R/api/purchasing/reports/top-products` with the same custom `from`/`to` (and default net sort). For each of the (up to) 10 product keys, `net` equals the report row. No payment/balance fields on `visual`.
4. Demo: `dataLabel=demo` and demo notice on the visual.
5. As Admin: same POST → 403, no table.
6. Ranking with >20 categories (if a wide unfiltered ranking is asked): table, **no** `chart`.
7. Fixture-style non-ranking (e.g. «خالص خرید این ماه چقدر است؟»): 200 cited, **no** `visual`.
8. Provider stopped: ranking POST → 503 `assistant-unavailable`, no table; `GET` Overview still 200.

SPA: `#/assistant` shows table + bar + Keep.

## B. Save generated + standard; reopen on new data (F15, SC-002, exit 2)

1. As Management, Keep the canonical visual as name `ده کالا`. `POST /api/saved-reports` 201, `kind=generated`.
2. On most-purchased-products, set a vendor or category filter; Save as `گزارش کالا`. 201, `kind=top-products`.
3. `GET /api/saved-reports` as Management: both names. As Purchasing: **neither**.
4. Run a new successful sync that changes totals (simulator insert or second sync window).
5. `POST /api/saved-reports/{id}/open` each: figures match **live** report / visual on the new data, not the numbers at save; last-sync updated; generated with 2–20 rows still has a chart.
6. Duplicate save of `ده کالا` without overwrite → 409 `saved-report-exists`. With `overwrite: true` → 200, kind/filters replaced.
7. Demo label present on list and open.

SPA: `#/saved` lists only the signed-in user; open navigates to the live report or generated view.

## C. Permission removed (F15, SC-003, exit 3)

1. Management user owns a generated saved report.
2. As Admin, `POST /api/admin/users/{id}/role` `{ "role": "admin" }` for that user.
3. Sign in as that user (now Admin). `POST .../open` on the generated id → **403**, Persian missing AI-queries, body has no rows. Audit `access.denied`.
4. Same user opens a **standard** saved report they own → **200** (Admin has purchasing reports).
5. Same user `GET .../export` on that standard report → **403**, no file, `access.denied`.
6. Unsigned `GET /api/saved-reports/1` → 401.

## D. Excel of a filtered report (F16, SC-004, SC-009, exit 4)

1. As Purchasing, open most-purchased products, apply period + vendor or category filter.
2. `GET` the matching `/export` URL (same query, no page). 200 xlsx in under 10 s for hundreds of rows.
3. Inspect file ([xlsx.md](contracts/xlsx.md)): `rightToLeft`, Persian headers = on-screen columns, meta has period, filters, export time, last-sync; data rows match the filtered report (sample ≥ 5 keys). «ریال» in meta. Demo sentence present in demo mode.
4. Download name: Persian report name + Jalali date + `.xlsx`.
5. Export the generated table (`POST /api/ai/visual/export` with `saveSpec`) and a reopened saved report (`GET /api/saved-reports/{id}/export`) — same meta rules.
6. If a >10,000-row export is available (or a unit-level fixture): file states truncation. Empty filter: headers + meta, zero data rows.
7. Flagged DQ records in scope: notice text in meta.
8. Browser print still uses `/print` (1,000 cap); both actions remain.

SPA: Export hidden for Admin; visible for Purchasing/Finance/Management.

## E. Export audit and 403 (F16, SC-005, SC-007, exit 5)

1. After a Purchasing export, as Admin filter audit by `report.exported`: user, Jalali time, report target, period/filters, **no** amounts or keys.
2. As Admin, GET any `/export` → 403, no xlsx; audit `access.denied`.
3. Scan those entries (and saved-report audits) for secrets / result rows → zero.

## F. Cap, delete, cross-user (F15, SC-006, US6)

1. As Management, delete one saved report → 204, gone from list, open 404, audit `saved_report.deleted`.
2. Direct GET of another user’s saved id → 404, same message as missing.
3. (Optional scale) 50 names then a 51st new name → 409 `saved-report-cap`; overwrite of name 50 still 200.

## G. Phase 4 cited path still holds (non-exit regression)

1. Fixture item 1 as Management: 200 cited, no obligatory table.
2. Fixture item 13: 200 unavailable, empty facts, **no** `visual`, no `purchasing_overview` tool side-effect.

## Exit criteria map

| Phase 5 exit / SC | Flow |
|---|---|
| Canonical table+chart matches reports | A |
| Save generated + standard, reopen on current data | B |
| Permission removed refuses generated open | C |
| xlsx Persian RTL + meta + matching rows | D |
| Export audited; no-permission 403 | E |
| Cross-user 0 opens | F |
| Demo labels | A4, B7, D3 |
| p95 reopen / export time | D2; reopen uses existing report query |
