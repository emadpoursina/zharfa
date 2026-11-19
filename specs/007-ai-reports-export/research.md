# Research: AI Reports, Saved Reports, and Excel Export (Release 1, Phase 5)

**Feature**: `specs/007-ai-reports-export` | **Date**: 2026-11-18 | **Plan**: [plan.md](plan.md) | **Spec**: [spec.md](spec.md)

Skip mode: already applied in the spec. Owner-facing skip assumptions stay there. Phases 1–4 stay in force; this document only extends them. Phase 4 assistant and Phase 2 reports are not reopened.

Code facts this research relies on (worktree, 2026-11-18):

- `POST /api/ai/chat` (`AiController`, `ai.query`) returns cited `PurchasingChatResponse` with no visual. `PurchasingQuestionRouter` is token-based (scope then one mapped tool). Tools wrap `PurchasingOverviewService` / `PurchasingReportService` (100 rows / 10 s / 30 s). The 20-question fixture and scorer stay the Phase 4 accuracy gate.
- Purchasing tables: `PurchasingReportsController` with `/print` twins (1,000-row cap, JSON, browser print). Class policy `purchasing.reports`; transactions also checks `purchasing.transactions` via `AccessDenied.RespondAsync`.
- Permissions already include `export` (Management, Purchasing, Finance; **not** Admin). No export endpoint exists. Audit catalog has `report.opened` / `report.printed` / `ai.question` / `access.denied`; export and saved-report actions were reserved in Phase 3.
- Next migration number is `0010`. EF `ZharfaDbContext` maps `app_user` and `audit_log` only; schema is SQL-first.
- Frontend: `#/assistant`, report tabs, inline-SVG `TrendChart` / `MultiTrendChart`. `Me.permissions` already lists `export`. No xlsx on the client.
- Backend csproj has Npgsql + Microsoft.Data.SqlClient only. PRD §9 names a server-side `.xlsx` library (ClosedXML, MIT) as the Excel choice to justify in this plan.

---

## R1. Visual answers are an additive server payload, not a model-drawn table (F14)

**Decision.** After an in-domain tool succeeds, a pure `VisualIntent` classifier (same style as `PurchasingQuestionRouter`) decides whether the HTTP 200 also includes `visual`. The model still phrases `answer` / `interpretation` from facts. The UI renders `visual.table` and optional `visual.chart` from server JSON. The system prompt’s “do not invent tables” stays: the model must not emit markdown tables or extra numbers.

`visual` is omitted when: not comparison/ranking; `unavailable: true`; 503 assistant-unavailable; 409 busy; out-of-domain / vendor-financials / mabna-bypass / no-sync (already empty facts). Empty ranking (zero purchases) still returns a labeled table with zero data rows and **no** chart (fewer than two rows); zeros are zeros, not «داده در دسترس نیست».

**Rationale.** F14 requires tables/charts from approved metrics. Letting the 7B model invent series would violate the Phase 4 allowlist and the 20/20 gate.

**Alternatives.** Model tool-calling for chart type (failed in Phase 4). Client-only charts from `facts` strings (fragile, not labeled as a table).

---

## R2. Comparison/ranking intent does not reopen the 20-question set (F14, Phase 4 SC-001)

**Decision.** `VisualIntent.IsListVisual(question)` is true only when the normalized question has ranking or comparison **list** tokens **and** the mapped tool can produce a row set:

| Tokens (after `PurchasingQuestionRouter.Normalize`) | Typical tool | Visual? |
|---|---|---|
| بیشترین، پرمصرف، برتر، top، رتبه، ۱۰ / ده تا از | `purchasing_top_products`, overview `topVendors`/`topProducts`, grouped reports | yes |
| مقایسه، compare (of vendors/products/categories/series) | grouped report or comparison-capable tool | yes |
| Canonical six-month top-10 products (English or Persian) | `purchasing_top_products` + period `last-six-jalali-months` | yes (exit criterion) |
| نسبت به / تغییر on a **single Overview figure** (fixture 2, 11) | `purchasing_overview` totals | **no** |
| روند without مقایسه (fixture 3) | `purchasing_trend` | **no** |
| تفکیک گروه without ranking tokens (fixture 6) | `purchasing_by_category` | **no** |
| Sales / موجودی / مانده / مبنا | already non-purchasing scopes | no visual |

Fixture items keep `expect: cited` / `unavailable` / `zero`. The scorer continues to judge `answer`, `facts`, `unavailable`, period, and no-financials. Extra `visual` on items 4, 5, and 12 is allowed and **not** scored as a Phase 4 failure. Phase 5 adds a separate canonical-question check (quickstart A).

**Rationale.** Spec: non-ranking questions keep Phase 4 cited text; ranking includes «بیشترین» / «مقایسه» / top N. Treating every «نسبت به» as a table would change Q2 into a two-row table and fight the 20/20 gate’s cited-prose shape.

**Alternatives.** Table on every grouped tool (would table Q6). No tables on any of the 20 (would miss Q5 ranking).

---

## R3. Canonical six months is a new period rule on existing `JalaliPeriod` (F14 AC1)

**Decision.** Add `JalaliPeriod.LastNMonths(int n, JalaliMonth end)` (n=6 for the canonical question): first day of `end.AddMonths(-(n-1))` through last day of `end` (inclusive of the Overview default month as month six). Wire it as `period=custom` with those `from`/`to` keys so `PurchasingReportService.GroupedAsync("top-products")` is unchanged. Router period rule `last-six-jalali-months` when the question has شش ماه / six months (and ranking/top-10 products). Ranking: net desc, `limit=10`, same columns as the most-purchased-products report.

Chart: bar of those products by net; included because 2–20 categories when there are at least two products with data.

**Rationale.** Reports already accept custom Jalali ranges; a new SQL period kind is unnecessary.

**Alternatives.** New `period=trailing-months` query param (extra catalog for one question). Twelve-month trend window (wrong span).

---

## R4. Chart is frontend SVG from server series; no new npm chart library (F14, PRD §9)

**Decision.** `visual.chart` is `{ "type": "bar" | "line", "categories": [...], "series": [{ "metric": "net", "label": "خالص خرید", "values": ["..."] }] }` with at most 20 categories. The SPA draws it with a small inline-SVG component next to `TrendChart` (RTL, Persian digits, «ریال»). No chart image in Excel (FR-019).

Omit chart when row/category count is 0, 1, or > 20. Table still returned for ranking/comparison.

**Rationale.** PRD: use existing frontend charts; add a library only if justified. `TrendChart` already does RTL bars without npm.

**Alternatives.** Chart.js / Recharts (new dependency). Server-rendered PNG (not required; Excel must not embed it).

---

## R5. Saved reports store a spec, never rows (F15)

**Decision.** New table `saved_report` (migration `0010`, [data-model.md](data-model.md)): per user, name + normalized `name_key`, `kind`, required permission, period JSON, filters JSON, optional generated spec JSON. Opening **re-runs** the matching Phase 2/4 service on the synced copy.

Kinds: Phase 2 report ids (`by-vendor`, `by-product`, `by-category`, `trend`, `top-products`, `returns`, `transactions`), `vendors`, `vendors-compare`, `drill-down`, `vendor-profile` (plus `profileTable`: documents | top-products | returns), and `generated`. Overview KPI tiles are not a kind.

`required_permission`: `purchasing.reports` for every standard kind; `ai.query` for `generated` (spec permission-removed exit). Phase 6 may add `vendor.financials` later; no financial kind in this phase.

**Rationale.** Spec: not a snapshot; reopen uses current sync and **current** role.

**Alternatives.** Snapshot rows (stale, huge, contradicts spec). Client localStorage (not shared across desktop/browser, not permission-checked).

---

## R6. Saved-report HTTP is a small CRUD + run, owned by user id (F15)

**Decision.** `SavedReportsController` at `/api/saved-reports`, policy `purchasing.reports` on the class (every role that can see reports can list/save **standard** kinds). Generated save/open additionally requires `ai.query` inside the action (Admin 403, Persian missing-permission message, `access.denied`).

| Verb | Path | Notes |
|---|---|---|
| GET | `/api/saved-reports` | caller’s rows only |
| POST | `/api/saved-reports` | create or replace; see R7 |
| GET | `/api/saved-reports/{id}` | metadata |
| POST | `/api/saved-reports/{id}/open` | re-run; 403 if permission gone |
| DELETE | `/api/saved-reports/{id}` | own only |
| GET | `/api/saved-reports/{id}/export` | export policy + open rules (R10) |

Unknown id **or** another user’s id: **404** `{ "message": "گزارش ذخیره شده یافت نشد." }` (do not leak that the id exists). Unsigned: 401.

**Rationale.** Same 404 hide as users-admin unknown ids. Permission-removed is a distinct 403 with the missing permission named.

**Alternatives.** 403 for other users (leaks existence). Nested under each report path (duplicated).

---

## R7. Name uniqueness, overwrite confirm, and the 50 cap (F15)

**Decision.** `name_key = PersianText.Normalize(trim(name))` (ی/ک, as usernames). Empty/`name_key` empty → 400. Unique `(user_id, name_key)`.

POST without `overwrite: true` when `name_key` exists → **409** `code: saved-report-exists` with the existing id and name. SPA confirms; POST again with `overwrite: true` replaces type/filters/spec, keeps id, updates `updated_at`. Cancel = no second POST.

Cap 50 **rows per user**. New name at cap → **409** `code: saved-report-cap`. Overwrite at cap is allowed.

**Rationale.** Spec session 2026-11-18: confirm then replace; new names blocked at 50, replace not.

**Alternatives.** Silent replace (rejected in skip-mode). Cap including replaces (would block the specified overwrite).

---

## R8. Excel is ClosedXML on the server (F16, PRD §9)

**Decision.** Add **ClosedXML** (MIT) to `Zharfa.Api` only. One `ExcelExportService` builds a workbook from a `ExportTable` (Persian headers, string cells already formatted with existing Rial/Jalali/Persian-digit helpers, meta rows, optional data-quality notice, truncation line). `Worksheet.RightToLeft = true`. No chart drawing in the file.

This is the **only** new NuGet in Release 1 so far; Phases 1–4 had none. Justified by PRD §9 (“server-side .xlsx library (for example ClosedXML, MIT)”). Frontend still has no xlsx library.

Do **not** stream Mabna or client-supplied row arrays: the service always re-queries via the same report/AI visual path, then caps at 10,000 data rows (`truncated`, `totalRows` in meta).

**Rationale.** Hand-rolled OOXML is more code and easy to get RTL/content-types wrong. EPPlus is not MIT-clear for this install. Client-side SheetJS would skip server permission and audit.

**Alternatives.** DocumentFormat.OpenXml only (verbose). EPPlus. Browser CSV (not `.xlsx`, not RTL sheet).

---

## R9. Live export twins parallel print, with a second permission check (F16)

**Decision.** Same routes as print, suffix `/export` instead of `/print`, same query string as the on-screen table (period, filters, sort; **no** page — full filtered result up to 10,000). Class policy remains `purchasing.reports`. Each export action also `AuthorizeAsync(User, Permissions.Export)` and on failure `AccessDenied.RespondAsync` (**403**, no file, `access.denied`). Transactions also keeps the transactions check **before** export.

Coverage: `reports/{id}/export`, `vendors/export`, `vendors/compare/export`, `documents/export`, vendor-profile table export via `documents/export` or a `vendor-profile/export?table=` query (one table per request). Generated: `POST /api/ai/visual/export` with the **spec** from `visual.saveSpec` (not rows); policy `ai.query` + export check.

Print stays 1,000 rows / JSON. Export does not replace print.

**Rationale.** Smallest extension of an existing twin pattern. Server re-fetch prevents tampered client rows.

**Alternatives.** One `POST /api/export` mega-body (easy to get kinds wrong). Client POST of rows (not trustworthy).

---

## R10. Download name, content type, and audit without rows (F16, F10)

**Decision.** `Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`. `Content-Disposition` attachment with RFC 5987 `filename*` = `{sanitizedPersianName}-{jalaliExportDate}.xlsx`. Sanitize: strip `/ \ : * ? " < > |` and C0 controls; if empty → «گزارش». Date = Jalali `yyyy-mm-dd` of export time in the sync time zone (same helper family as `SyncTimeText`).

Audit on **2xx** export: `report.exported` (target = report kind/name, detail = period and filters, **no** amounts). Failed export permission: existing `access.denied`. Saved-report save/replace/open/delete: `saved_report.saved` (create), `saved_report.replaced`, `saved_report.opened`, `saved_report.deleted`. Audit write failure does not change HTTP outcome (Phase 3 FR-018). Never 401-audit.

**Rationale.** Spec file-name rule and F10 “no result rows”.

**Alternatives.** `export` as a single action with `op=` in detail (harder for Admin filter). Include row counts as “figures” (avoid; counts of rows exported as `rowCount`/`truncated` flags are metadata, not result amounts — store `truncated=true` only, not cell values).

---

## R11. Data-quality notice and data-mode label travel with visual, saved open, and xlsx (shared)

**Decision.** Reuse Phase 3 `ReportEnvelope.dataQuality` and `dataLabel`. Generated `visual` includes the same notice object when the tool’s report scope has flagged records. Excel meta area copies notice text when present; demo files always include the demonstration-data sentence.

**Rationale.** Spec FR-015 / FR-022; one notice path already exists on report SQL.

**Alternatives.** A second notice query for AI (drift). Skip notice on xlsx (fails SC samples).

---

## R12. SPA surfaces: assistant visual + save, report save/export, saved-reports tab (F14–F16)

**Decision.**

- Assistant (`#/assistant`): render table + chart + caption (period, filters, metrics); **Keep** when `ai.query` and `visual` present. No keep on cited-only or unavailable.
- Report / vendors / compare / drill-down: **Save** (anyone with `purchasing.reports`, including Admin) and **Export** only if `permissions` contains `export` (hide for Admin). Save dialog: name + overwrite confirm.
- New tab `#/saved` «گزارش‌های ذخیره‌شده», `modules.savedReports` = `purchasing.reports`. List, open (navigate to live report with stored filters, or assistant-style generated view), delete. Generated open for Admin is not offered; direct API still 403.
- Keyboard: dialogs and buttons in the existing Tailwind/focus patterns; WCAG 2.1 AA contrast as the rest of the shell.

**Rationale.** Spec UI; frontend hiding is not enforcement.

**Alternatives.** Saved list only inside assistant (hides standard saves). Export always visible then 403 (worse UX, still need server 403 tests).

---

## R13. Cloud and tool limits stay Phase 4 (FR-006)

**Decision.** No new tools. Visual series are the same columns the allowlisted tool already returned (still ≤ 100 rows; over cap remains metric-unavailable, no table). Cloud-bound JSON may include those aggregate rows (names + amounts) as today; it must not include document lines. Saved reports and xlsx files are never sent to the cloud. One in-flight question, 30 s budget, independent questions: unchanged. Ranking still runs the provider liveness probe first (provider down → 503, no table; standard save/export still work).

**Rationale.** Phase 4 constraints must not weaken.

**Alternatives.** Bypass the model for ranking (would skip 503-when-down for the canonical question; spec says assistant-unavailable when the provider is down). Extra unlimited query for top-10 (second path, drift risk). The canonical top-10 is within the 100-row cap.

---

## R14. Tests: pure logic here, HTTP/UI in quickstart

**Decision.** xunit (no DB/network): `VisualIntent` tokens; six-month period helper; save name_key / empty / cap / overwrite flags as pure functions; export filename sanitizer; ClosedXML workbook assertions (RTL, meta keys, truncation banner, Persian header strings, **no** secret-like cells) from in-memory `ExportTable`; permission matrix rows for new actions; audit action catalog extensions; DTO scan still forbids vendor-financial fields on visual/export types.

Quickstart on Compose + simulator proves: canonical table=report, save/reopen after sync, Admin role-change 403, xlsx open (Python `openpyxl` or LibreOffice if present — prefer a tiny Python stdlib+openpyxl **only in the tester flow**, not a repo dependency; if openpyxl is missing, unzip the xlsx and assert `workbook.xml` `rightToLeft="1"` and shared-string headers).

**Rationale.** Same split as Phases 2–4.

**Alternatives.** Excel COM (Windows-only). Skipping file inspect (fails SC-004).

---

## R15. Docs and validation_commands

**Decision.** After implement: README (saved reports + export), `backend/Zharfa.Api/README.md`, `.ainative/project.yaml` comment (spec 007 suites), `CHANGELOG.md` if the repo scheme requires a delivered note. No new env vars except none — ClosedXML needs no config. Do not add Ollama/GPU to `validation_commands`.

**Rationale.** AGENTS.md: update docs when behavior changes.

---

## R16. Owner-facing skip-mode restatement (no new questions)

Binding skip answers already in the spec: overwrite confirm; generated reopen uses live table-and-chart rules; cap blocks new names only; export of a reopened saved table is allowed; download name = Persian name + Jalali date + `.xlsx`. Generated reopen requires `ai.query` until the owner changes it.

---

## Unknowns

None open for planning. On-site Mabna verification is unchanged from Phases 1–2; this phase adds no source items.
