---
description: "Task list for AI Reports, Saved Reports, and Excel Export (Release 1, Phase 5)"
---

# Tasks: AI Reports, Saved Reports, and Excel Export (Release 1, Phase 5)

**Input**: Design documents from `specs/007-ai-reports-export/` — [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/api.md](contracts/api.md), [contracts/xlsx.md](contracts/xlsx.md), [quickstart.md](quickstart.md)

**Phase ref**: `zharfa-prd.phases.md#5` · Governing PRD `zharfa-prd.md` v3.0 (F14, F15, F16, F10 export/saved events) · Rules `AGENTS.md` · Foundation Phases 1–4 (`specs/003-real-purchase-data`, `specs/004-vendor-purchasing-reports`, `specs/005-permissions-audit-quality`, `specs/006-local-ai-purchasing`)

**Tests**: Included. Plan **Testing** and research R14 require pure xunit suites in `backend/Zharfa.Api.Tests` (no database, no network): `VisualIntent` tokens, `JalaliPeriod.LastNMonths`, save `name_key` / empty / cap / overwrite flags, export filename sanitizer, ClosedXML workbook (RTL, meta, truncation, Persian headers, no secret-like cells), permission matrix rows, audit catalog (28 codes, 14 report keys). HTTP, xlsx-on-disk, and UI are proven by [quickstart.md](quickstart.md) Flows A–G (Compose + simulator), run by the tester, not by workers. Workers do not run installs or validation commands (AGENTS.md). Do not add Ollama/GPU to `validation_commands`.

**Organization**: Setup (migration + ClosedXML), then shared catalogs/period/name helpers (Phase 2), then one phase per user story in spec order (US1 → US6, all P1 except US6 P2), then polish.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: User story from spec.md (US1–US6)
- Every task names its exact file path(s)

## Path Conventions

Web application (existing layout): `backend/Zharfa.Api/`, `backend/Zharfa.Api.Tests/`, `frontend/src/`, `db/migrations/`. New backend types: `backend/Zharfa.Api/Ai/VisualIntent.cs`, `VisualBuilder.cs`; `backend/Zharfa.Api/SavedReports/` (namespace `Zharfa.Api.SavedReports`); `backend/Zharfa.Api/Purchasing/Reports/ExcelExportService.cs`. Tests mirror under `backend/Zharfa.Api.Tests/`. **One new NuGet**: ClosedXML on `Zharfa.Api` only (PRD §9 / research R8). **No new npm packages.**

## Hard rules for every task

- Authorization is decided on the server only. SPA hiding Save/Export/Keep/`#/saved` is cosmetic. Export = Management/Purchasing/Finance (`Permissions.Export`); Admin has neither `export` nor `ai.query`.
- Generated save **and** open require `ai.query` (permission-removed exit). Standard kinds require `purchasing.reports`; `transactions` / document drill also `purchasing.transactions` **before** any query. Other user’s id or unknown id → **404** `{ "message": "گزارش ذخیره شده یافت نشد." }` (do not leak existence). Unsigned → 401, no file, no saved data, **no** audit.
- AI answers only through the Phase 4 allowlist. **No new tools.** Visual series are copies of allowlisted metric columns (≤ 100 rows; over cap remains metric-unavailable, no table). 10 s/tool, 30 s user-visible, one in-flight question, independent turns, liveness probe first (provider down → 503, no table). Cloud JSON may include those aggregates; **never** document lines, saved-report rows, or xlsx bytes.
- Tables/charts only for comparison/ranking list intent (`VisualIntent`). Non-ranking keeps Phase 4 cited text (no obligatory `visual`). «داده در دسترس نیست» / 503 / 409: no table, no chart. Empty ranking (zero purchases): labeled table, **zero rows**, no chart; zeros are zeros.
- Canonical question (EN or FA: compare top 10 purchased products over last six months): `view=top-products`, `JalaliPeriod.LastNMonths(6)` as `period=custom` `from`/`to`, rank net desc, ≤ 10 rows, figures **equal** `GET /api/purchasing/reports/top-products` for that range. Chart = bar of those products by net when 2–20 categories.
- Saved reports store a **spec**, never result rows. Open re-runs Phase 2/4 services on the synced `mabna.*` copy. Generated open **does not** call the LLM.
- Excel is server-built ClosedXML from a re-query (never client-supplied rows). Print twins stay 1,000 rows / JSON. Export cap 10,000 data rows. `Worksheet.RightToLeft = true`. No chart drawing in the file.
- Audit append-only: `report.exported`, `saved_report.{saved,replaced,opened,deleted}`; permission failures stay `access.denied`. Detail = period/filters keys/labels only — **no** amounts, result rows, secrets, keys. Audit write failure does not change HTTP outcome. Never 401-audit.
- Purchases ≠ payments ≠ vendor balance; a price difference is never profit. Visual/export/saved DTOs must not grow `balance`/`payment`/`debt`/`credit`/`paid` properties.
- Persian-first: RTL, Jalali, Persian digits, ریال, Persian messages copied from [contracts/api.md](contracts/api.md). Demo `dataLabel` on visual, saved list/open, and xlsx meta row 6.
- Do not commit, push, or run `npm install` / `dotnet test`; the tester runs `.ainative/project.yaml` `validation_commands`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Migration `0010` and the ClosedXML package every later story builds on. No answering or export behavior yet.

- [X] T001 Create `db/migrations/0010_ai_reports_export.sql` (forward-only, idempotent `IF NOT EXISTS`; do not edit `0001`–`0009`; requires `0009`). Table `saved_report` per [data-model.md](data-model.md) §1: `id bigint GENERATED ALWAYS AS IDENTITY` PK; `user_id bigint NOT NULL` FK `app_user(user_id)` **ON DELETE CASCADE**; `name text NOT NULL`; `name_key text NOT NULL`; `kind text NOT NULL`; `required_permission text NOT NULL`; `period_json jsonb NOT NULL`; `filters_json jsonb NOT NULL`; `generated_json jsonb NULL`; `created_at timestamptz NOT NULL DEFAULT now()`; `updated_at timestamptz NOT NULL DEFAULT now()`. Indexes: `uq_saved_report_user_name_key UNIQUE (user_id, name_key)`, `idx_saved_report_user_updated (user_id, updated_at DESC)`. Checks: `chk_saved_report_kind` — `kind` in data-model §2 list (`by-vendor`,`by-product`,`by-category`,`trend`,`top-products`,`returns`,`transactions`,`vendors`,`vendors-compare`,`drill-down`,`vendor-profile`,`generated`); `chk_saved_report_permission` — `required_permission IN ('purchasing.reports','ai.query')`; `chk_saved_report_generated` — `(kind = 'generated' AND generated_json IS NOT NULL) OR (kind <> 'generated' AND generated_json IS NULL)`. No result-row columns. Do not change `mabna.*`, `audit_log`, or the append-only trigger.
- [X] T002 [P] Add PackageReference **ClosedXML** (MIT, latest 0.105.x compatible with `net10.0`) to `backend/Zharfa.Api/Zharfa.Api.csproj` only. Do not add ClosedXML (or any spreadsheet library) to the test project, the desktop shell, or any npm package. Comment that it is the PRD §9 F16 library (research R8).
- [X] T003 [P] Add `savedReports: boolean` to `SystemModules` in `frontend/src/services/api.ts` (contracts/api.md §0). Do not add `#/saved` routes yet.

**Checkpoint**: migration and ClosedXML exist; the API still behaves as Phase 4.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared period helper, name rules, catalogs, modules, and the `SavedReport` entity. **No user story work until this phase is complete.**

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Tests for the foundation (write first; they must fail until the code exists)

- [X] T004 [P] Create `backend/Zharfa.Api.Tests/Purchasing/Reports/LastNMonthsTests.cs`: `JalaliPeriod.LastNMonths(6, end)` where `end` is Overview default month (Aban 1405 in existing JalaliPeriodTests today) → first day of `end.AddMonths(-5)` through last day of `end` (inclusive of current month as month six); `Kind` is custom; wire keys `from`/`to` are `YYYY-MM-DD`. n=1 is that month only. Pure, no DB.
- [X] T005 [P] Create `backend/Zharfa.Api.Tests/SavedReports/SavedReportNameTests.cs` for `SavedReportName` in `backend/Zharfa.Api/SavedReports/SavedReportName.cs`: `name_key = PersianText.Normalize(trim(name))` (ی/ک as usernames); empty / whitespace-only → invalid; `" ده کالا "` and `"ده كالا"` (Arabic ک) collide; `CanInsert(count, isOverwrite)` false when `count >= 50` and not overwrite, true when overwrite at 50. Cap constant **50**. Pure.
- [X] T006 [P] Update `backend/Zharfa.Api.Tests/Audit/AuditActionsTests.cs`: append `report.exported`, `saved_report.saved`, `saved_report.replaced`, `saved_report.opened`, `saved_report.deleted` after `ai.question`; bump catalog length **23 → 28**; labels «خروجی اکسل», «ذخیره گزارش», «جایگزینی گزارش ذخیره شده», «باز کردن گزارش ذخیره شده», «حذف گزارش ذخیره شده». `ReportKeys.All` **13 → 14** with `generated` / «گزارش تولیدشده».
- [X] T007 Update `backend/Zharfa.Api.Tests/Platform/DemoOnlyFilterTests.cs`: `SystemModules.For` sets `SavedReports` true iff the permission set contains `purchasing.reports` (Admin and Purchasing true; empty set false). Production vs demo does not change the flag.

### Implementation

- [X] T008 [P] Add `JalaliPeriod.LastNMonths(int n, JalaliMonth end)` to `backend/Zharfa.Api/Purchasing/Reports/JalaliPeriod.cs` (research R3): first day of `end.AddMonths(-(n-1))` through last day of `end`. Reuse existing Jalali day arithmetic. Do not add a new SQL period kind.
- [X] T009 [P] Add `AuditActions.ReportExported = "report.exported"`, `SavedReportSaved`, `SavedReportReplaced`, `SavedReportOpened`, `SavedReportDeleted` and the Persian labels of data-model §6 to `All` (order after `ai.question`) in `backend/Zharfa.Api/Audit/AuditActions.cs`.
- [X] T010 [P] Add `ReportKeys.Generated = "generated"` with name «گزارش تولیدشده» to `backend/Zharfa.Api/Audit/ReportKeys.cs`. Keep the thirteen Phase 3 keys.
- [X] T011 Update `backend/Zharfa.Api/Platform/SystemModules.cs` and `backend/Zharfa.Api/Platform/SystemStatusController.cs`: add `SavedReports`; `SavedReports = permissions.Contains(Permissions.PurchasingReports)`. JSON `modules.savedReports`. Export is **not** a module (stays on `Me.permissions`).
- [X] T012 Create `backend/Zharfa.Api/SavedReports/SavedReportName.cs` (pure): trim, `PersianText.Normalize` → `name_key`; refuse empty/`name_key` empty; `MaxPerUser = 50`; helpers used by the service for 400 / 409 `saved-report-exists` / 409 `saved-report-cap`.
- [X] T013 Create `backend/Zharfa.Api/SavedReports/SavedReport.cs` matching data-model §1 columns (`Id`, `UserId`, `Name`, `NameKey`, `Kind`, `RequiredPermission`, `PeriodJson`, `FiltersJson`, `GeneratedJson`, `CreatedAt`, `UpdatedAt`). Optionally map `DbSet<SavedReport>` in `backend/Zharfa.Api/Data/ZharfaDbContext.cs`; schema remains SQL-owned (`MigrationRunner` only). Writers may use Npgsql as reports do.

**Checkpoint**: Foundation ready — catalogs, last-N-months, name rules, and entity compile; user stories can start.

---

## Phase 3: User Story 1 — Assistant returns a table and chart for ranking questions (Priority: P1) 🎯 MVP

**Goal**: A Management (also Purchasing/Finance with AI-queries) user asks a ranking/comparison purchasing question and receives a **server-built** labeled table and, when there are 2–20 categories, a bar chart. Canonical top-10 × six Jalali months matches the most-purchased-products report. Phase 4 cited path, 20-question scorer, and 503-when-down stay.

**Independent Test**: After a successful simulator sync, sign in as Management, ask the canonical question in Persian; confirm table and chart, labels, and that each of the ten product net-purchase figures equals the same ranking on the most-purchased-products report for those six months (quickstart Flow A). Admin POST → 403. Non-ranking fixture-style question → no `visual`. Provider stopped → 503, no table.

### Tests for User Story 1

- [X] T014 [P] [US1] Create `backend/Zharfa.Api.Tests/Ai/VisualIntentTests.cs` (research R1–R2): after `PurchasingQuestionRouter.Normalize`, `VisualIntent.IsListVisual` is true for بیشترین / پرمصرف / برتر / top / رتبه / ۱۰ / ده تا / مقایسه / compare **and** a mapped tool that can return a row set; **false** for نسبت به / تغییر on a single Overview figure (fixture 2, 11), روند without مقایسه (fixture 3), تفکیک گروه without ranking (fixture 6), out-of-domain / vendor-financials / mabna-bypass. Canonical EN and FA top-10 six-month questions are true. Pure, no LLM.
- [X] T015 [P] [US1] Extend `backend/Zharfa.Api.Tests/Ai/PurchasingQuestionRouterTests.cs` (and `PurchasingPeriodDefaultTests.cs` if the binder lives there): canonical six-month ranking maps to `purchasing_top_products` + period rule `last-six-jalali-months`. Other ranking questions without a period still use Overview default month. Do not change fixture `expect` for items 1–20.

### Implementation for User Story 1

- [X] T016 [P] [US1] Create `backend/Zharfa.Api/Ai/VisualIntent.cs`: `IsListVisual(normalizedQuestion, mappedTool)` per T014 / research R2. Do not reopen the 20-question set; extra `visual` on fixture items 4, 5, 12 is allowed and not a Phase 4 failure.
- [X] T017 [US1] Create `backend/Zharfa.Api/Ai/VisualBuilder.cs`: from an allowlisted tool row DTO build `visual` per data-model §4 and contracts/api.md §1 (`kind=generated`, Persian `table.columns`, decimal-string cells, `captions` period/filters/metrics, optional `chart` `{ type: bar|line, categories, series }` copied from the **same** columns, `saveSpec` for POST `/api/saved-reports`, `dataQuality` from the report envelope when flagged). Omit `chart` when row/category count ∉ [2, 20]. Omit entire `visual` when unavailable, empty facts, or not list-visual. Row cap = tool cap (100); over cap → no visual. No second calculation. No payment/balance fields.
- [X] T018 [US1] Extend `backend/Zharfa.Api/Ai/PurchasingQuestionRouter.cs` and `backend/Zharfa.Api/Ai/PurchasingAnalystTools.cs`: period rule `last-six-jalali-months` when the question has شش ماه / six months **and** ranking/top-10 products; bind via `JalaliPeriod.LastNMonths(6, OverviewDefaultMonth)` as `period=custom` `from`/`to` into existing `GroupedAsync("top-products")`. Ranking net desc, `limit=10`, same columns as the most-purchased-products report. Do not add tools. Do not weaken 100-row / 10 s / 30 s / one-in-flight / liveness.
- [X] T019 [US1] Extend `PurchasingChatResponse` in `backend/Zharfa.Api/Controllers/AiController.cs` with optional `visual` (omit JSON property when null). After a successful in-domain tool dispatch, if `VisualIntent` and a usable row set and `unavailable=false`, attach `VisualBuilder` output. 503 / 409 / 200-unavailable: no `visual`. System prompt still forbids markdown tables and extra numbers. Request body stays `{ "question" }` only.
- [X] T020 [P] [US1] Extend purchasing chat types in `frontend/src/services/api.ts` to match contracts/api.md §1 `visual` (`table`, optional `chart`, `captions`, `saveSpec`, `dataQuality`, `dataLabel`). Keep Demo 1 chat types unchanged.
- [X] T021 [P] [US1] Create `frontend/src/components/RankingBarChart.tsx`: inline SVG (same family as `TrendChart.tsx`), RTL, Persian digits, «ریال», at most 20 categories. **No new npm chart library.**
- [X] T022 [US1] Update `frontend/src/components/AiChat.tsx` and `frontend/src/pages/Assistant.tsx`: when `visual` is present, render the labeled table + optional chart + captions (period, filters, metrics) + demo notice/`dataLabel`; keep facts vs interpretation visually separate; no Keep button yet (US2). Cited-only and unavailable answers stay as Phase 4 (no invented table). Keyboard-usable, visible focus, AA contrast.

**Checkpoint**: US1 is independently testable (quickstart A, G). MVP.

---

## Phase 4: User Story 2 — Save a generated report and a standard report, then reopen on current data (Priority: P1)

**Goal**: After a generated table, Keep stores type `generated` + period/filters/metrics (not numbers). From a Phase 2 purchasing table the user saves the current kind + filters. List is per-user. Open re-runs on the latest successful sync and shows last-sync time. Duplicate name → 409 then overwrite confirm.

**Independent Test**: As Management, save one generated ranking and one filtered most-purchased-products (or by-vendor) report; run a new sync that changes totals; reopen both; confirm names, types, filters and that figures match live reports on the new data (quickstart Flow B). Purchasing list does not show Management’s names.

### Implementation for User Story 2

- [X] T023 [US2] Create `backend/Zharfa.Api/SavedReports/SavedReportService.cs`: list caller’s rows (`updated_at` desc); insert/replace; get metadata; open = re-run. Store name, `name_key`, `kind` (data-model §2), `required_permission` (`ai.query` iff `kind=generated` else `purchasing.reports`), `period_json` (same shape as report query; canonical six months as `kind=custom` with computed `from`/`to`), `filters_json` (`{}` if none; subset `vendor`,`product`,`category`,`docType`,`q`,`sort`,`dir`,`compare`,`vendors` 2..5, `profileTable` `documents`|`top-products`|`returns`), `generated_json` required iff generated (`tool` Phase 4 allowlist, `metrics` report column ids). **Never store result rows.** Empty name → 400. Duplicate `name_key` without `overwrite: true` → 409 `{ "message": "گزارشی با این نام دارید. آیا جایگزین شود؟", "code": "saved-report-exists", "id" }`. `overwrite: true` replaces type/filters/spec, keeps id, sets `updated_at`. Count ≥ 50 and **new** `name_key` → 409 `{ "message": "حداکثر ۵۰ گزارش ذخیره شده مجاز است. ابتدا یکی را حذف کنید.", "code": "saved-report-cap" }`; replace at cap allowed. Comparison `vendors.length` not in 2..5 → 400 (Phase 2 limit text). Unknown generated `tool` → 400. Invalid `kind` → 400. Overview KPI tiles are not a kind. Parameterized SQL / existing Npgsql patterns.
- [X] T024 [US2] Create `backend/Zharfa.Api/Controllers/SavedReportsController.cs` (`[Route("api/saved-reports")]`, `[Authorize(Policy = Permissions.PurchasingReports)]`): `GET /` list envelope `{ dataLabel, max: 50, items[] }` per contracts/api.md §2; `POST /` create 201 / replace 200; `GET /{id}` metadata; `POST /{id}/open` re-run. Unknown or not owned → 404 exact message. Generated save/open additionally require `ai.query` (403 + `AccessDenied.RespondAsync` if missing — US3 will assert the role-change path; implement the check here so Admin cannot Keep). Standard open uses existing report services (`GroupedAsync`, `TrendAsync`, `ReturnsAsync`, `DocumentsAsync`, directory, compare 2..5, profile slice). Generated open rebuilds `visual` via `VisualBuilder` on **current** data (chart only if 2..20 **now**); **no** `CompleteAsync`. No successful sync / unverified source → same unavailable shape as live reports. Audit `saved_report.saved` / `replaced` / `opened` on 2xx (`target` `saved:{id}:{name_key}`, `detail` period/filters keys only). `dataLabel` from `DataModeOptions`.
- [X] T025 [US2] Register `SavedReportService` in `backend/Zharfa.Api/Program.cs`.
- [X] T026 [US2] Update `backend/Zharfa.Api.Tests/Security/PermissionMatrixTests.cs`: `SavedReportsController.List`, `Create`, `Get`, `Open` → `{purchasing.reports}` (generated extra `ai.query` is the runtime check, same pattern as transactions on Report). Fail if a new action lacks a policy.
- [X] T027 [P] [US2] Add saved-report client helpers and types to `frontend/src/services/api.ts`: list, POST (incl. `overwrite`), GET, open; 409 codes `saved-report-exists` / `saved-report-cap`; item shape (`id`,`name`,`kind`,`kindLabel`,`requiredPermission`,`period`,`filters`,`updatedAtJalali`).
- [X] T028 [P] [US2] Create `frontend/src/components/SaveReportDialog.tsx`: Persian name field; refuse whitespace-only in the dialog; on 409 `saved-report-exists` ask overwrite confirm; confirm POST `overwrite: true`; cancel = no second POST; show cap message on `saved-report-cap`. Keyboard, focus trap, AA contrast.
- [X] T029 [US2] Add **Keep** on `frontend/src/components/AiChat.tsx` / `frontend/src/pages/Assistant.tsx` when `ai.query` and `visual` present: POST `visual.saveSpec` + name. No Keep on cited-only or unavailable.
- [X] T030 [P] [US2] Add **Save** (anyone with `purchasing.reports`, including Admin) on `frontend/src/pages/PurchasingReports.tsx`, `frontend/src/pages/Vendors.tsx`, `frontend/src/pages/VendorComparison.tsx`, `frontend/src/pages/VendorProfile.tsx`, and drill-down (`frontend/src/components/DrillDownDialog.tsx`): current kind, period, filters (comparison `vendors` 2..5). Do not offer Save on Overview KPI tiles.
- [X] T031 [US2] Create `frontend/src/pages/SavedReports.tsx` (list + open) and update `frontend/src/App.tsx`: view `saved` at `#/saved`, label «گزارش‌های ذخیره‌شده», visible when `status.modules.savedReports`. Open a standard saved report by navigating to the live report with stored filters; open generated as the assistant-style table/chart view (no new LLM call). Demo label on list and open. Last-sync from the open envelope.

**Checkpoint**: US1 and US2 both work independently (quickstart B).

---

## Phase 5: User Story 3 — Saved report is refused when permission is gone (Priority: P1)

**Goal**: After a Management user saves a generated report and an Admin changes that user to Admin, open is 403 with a Persian missing-permission reason, no figures, `access.denied`. A standard purchasing saved report they own still opens (Admin holds purchasing reports). Unsigned → 401.

**Independent Test**: As Management, save a generated report; as Admin, change that user to Admin; as that user, open the saved report; confirm refusal with a reason, no table, and an access-denied audit entry (quickstart Flow C). Standard saved open 200; unsigned GET 401.

- [X] T032 [US3] Tighten generated open/save in `backend/Zharfa.Api/SavedReports/SavedReportService.cs` and `backend/Zharfa.Api/Controllers/SavedReportsController.cs`: if caller lacks `ai.query`, refuse **before** any metric query; Persian body names the missing AI-queries permission; **no** rows in the body; audit `access.denied` with `permission=ai.query` (not `saved_report.opened`). Transactions kind without `purchasing.transactions` → 403 naming that permission, no query. Standard kinds still open for Admin.
- [X] T033 [P] [US3] On `frontend/src/pages/SavedReports.tsx`, do not offer open of `kind=generated` when `me.permissions` lacks `ai.query`; a direct API 403 still shows the server Persian reason and no table. Do not treat 403 generated-open as a successful navigation.

**Checkpoint**: Permission-removed exit is independently testable (quickstart C).

---

## Phase 6: User Story 4 — Export a filtered report to Excel (Priority: P1)

**Goal**: A user with export permission downloads the current on-screen table (live or reopened) as `.xlsx`: Persian headers, RTL sheet, meta (period, filters, Jalali export time, last-sync, data-mode, optional DQ notice), rows matching the filtered report up to 10,000, download name = Persian report name + Jalali date + `.xlsx`.

**Independent Test**: As Purchasing, open most-purchased products, apply a period and a vendor or category filter, export; open the file and confirm Persian RTL headers, meta fields, and row values equal the filtered report (quickstart Flow D). Generated and saved-report export follow the same meta rules. Print `/print` still 1,000.

### Tests for User Story 4

- [X] T034 [P] [US4] Create `backend/Zharfa.Api.Tests/Purchasing/Reports/ExcelExportServiceTests.cs`: given an in-memory `ExportTable`, the ClosedXML workbook has `Worksheet.RightToLeft`, sheet name «گزارش», rows 1–8 per [contracts/xlsx.md](contracts/xlsx.md) (name, period, filters or «بدون فیلتر», Jalali export time Persian digits, last-sync or «همگام‌سازی موفقی ثبت نشده است», demo/real sentence, DQ text or empty, truncation «۱۰٬۰۰۰ ردیف نخست از N ردیف — فیلترها را محدود کنید.» iff `totalRows > 10000`), row 10 Persian headers, data from row 11, max 10,000 data rows, empty table still has headers+meta. Formula injection: cells starting with `=`,`+`,`-`,`@` prefixed. Filename helper: strip `/ \ : * ? " < > |` and C0; empty remainder → «گزارش»; `{name}-{jalali yyyy-mm-dd}.xlsx`. Assert **no** password/token/connection-string/provider-key substrings in shared strings. Pure in-memory; no HTTP.

### Implementation for User Story 4

- [X] T035 [US4] Create `backend/Zharfa.Api/Purchasing/Reports/ExcelExportService.cs` (+ `ExportTable` record in the same folder) per data-model §5 and xlsx.md. Format amounts with existing Persian-digit / Rial helpers; currency «ریال» in meta, not a second unit. Always re-query via report/visual services then cap — never accept client row arrays. Register in `backend/Zharfa.Api/Program.cs`.
- [X] T036 [US4] Add `/export` twins on `backend/Zharfa.Api/Controllers/PurchasingReportsController.cs` (same query as the on-screen table; **ignore** `page`/`pageSize`; full filtered result up to 10,000): `GET reports/{reportId}/export`, `vendors/export`, `vendors/compare/export`, `documents/export`, `vendor-profile/export?vendor=&table=documents|top-products|returns`. Class policy stays `purchasing.reports`. Each export action also `[Authorize(Policy = Permissions.Export)]` **or** `AuthorizeAsync(User, Permissions.Export)` then `AccessDenied.RespondAsync` (403, empty body, not xlsx). Transactions / documents: transactions check **before** export. Print actions and 1,000 cap unchanged.
- [X] T037 [P] [US4] Create `backend/Zharfa.Api/Controllers/AiVisualExportController.cs` (or an extra action on `AiController`): `POST /api/ai/visual/export`, policy `ai.query` then `export`. Body = `visual.saveSpec` (period, filters, generated). Server re-runs the allowlisted tool; 400 if spec invalid; 400 «جدولی برای خروجی وجود ندارد.» if there is no table. Do not send xlsx to the cloud.
- [X] T038 [US4] Add `GET /api/saved-reports/{id}/export` on `backend/Zharfa.Api/Controllers/SavedReportsController.cs`: open-rules **and** `export`. Admin opening a **standard** saved report: open 200, export 403. Generated export requires `ai.query` + `export`.
- [X] T039 [US4] Success responses: 200 raw bytes, `Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `Content-Disposition` attachment with RFC 5987 `filename*` = sanitized Persian name + `-` + Jalali `yyyy-mm-dd` + `.xlsx` (research R10; empty → «گزارش»).
- [X] T040 [US4] Update `backend/Zharfa.Api.Tests/Security/PermissionMatrixTests.cs`: each live export action → `{purchasing.reports, export}` (documents + `purchasing.transactions`); `AiVisualExport` (or `AiController.VisualExport`) → `{ai.query, export}`; `SavedReportsController.Export` → `{purchasing.reports, export}`.
- [X] T041 [P] [US4] Add blob download helpers in `frontend/src/services/api.ts` for the export URLs (credentials/cookie, do not parse xlsx in JS). Use `Content-Disposition` filename when present.
- [X] T042 [US4] Add **Export** on the same report surfaces as Save (`PurchasingReports.tsx`, `Vendors.tsx`, `VendorComparison.tsx`, `VendorProfile.tsx`, drill-down, generated table on Assistant, reopened saved view). Do not draw charts into Excel. Keep Print as the Phase 2 `/print` path.

**Checkpoint**: US4 is independently testable (quickstart D).

---

## Phase 7: User Story 5 — Export is audited; users without export permission are refused (Priority: P1)

**Goal**: Every 2xx export writes `report.exported` (user, time, report target, period/filters; no rows). Admin (no export) GET `/export` → 403, no file, `access.denied`. UI does not offer Export where the role lacks it. Unsigned → 401.

**Independent Test**: Export once as Purchasing; attempt export as Admin; as Admin, filter the audit log and confirm the successful export and the 403, with no result rows or secrets (quickstart Flow E).

- [X] T043 [US5] Write `report.exported` on **2xx** in the export actions (`PurchasingReportsController`, `AiVisualExportController`, `SavedReportsController.Export`): `target` = `report:{kind}` or saved `saved:{id}:{name_key}`; `detail` = period and filters (keys/labels only). Failed export permission: existing `access.denied` only. Never put amounts, row dumps, or keys in `detail`. Audit failure does not change HTTP. Never audit 401.
- [X] T044 [US5] Confirm Admin (and any role lacking `export`) receives 403 with **empty body** (not an xlsx content-type) from every `/export` path; unsigned 401. Reuse `AccessDenied.RespondAsync`.
- [X] T045 [US5] Hide Export in the SPA when `me.permissions` does not include `export` (`frontend/src/pages/PurchasingReports.tsx` and the other T042 surfaces). Server 403 remains the enforcement.
- [X] T046 [P] [US5] Ensure `GET /api/admin/audit/actions` already lists the five new codes via `AuditActions.All` (T009). On `frontend/src/pages/AuditLog.tsx` the new labels appear through the existing actions catalog — add no extra client filter list unless the page hard-codes codes.

**Checkpoint**: Export audit + 403 exit is independently testable (quickstart E).

---

## Phase 8: User Story 6 — Manage own saved reports within the cap (Priority: P2)

**Goal**: The owner lists names/types/period/filters, deletes one (audited), and at 50 reports a **new** name is refused until they delete; overwrite of an existing name at the cap still works after confirm.

**Independent Test**: Save two reports, delete one, confirm it is gone; confirm a duplicate name replaces; confirm the 50th extra name is refused with a Persian cap message (quickstart Flow F). Direct GET of another user’s id → 404.

- [X] T047 [US6] Add `DELETE /api/saved-reports/{id}` on `backend/Zharfa.Api/Controllers/SavedReportsController.cs` (204; own only; 404 if not owned). Audit `saved_report.deleted`. Update `PermissionMatrixTests` with `SavedReportsController.Delete` → `{purchasing.reports}`. Implement delete in `backend/Zharfa.Api/SavedReports/SavedReportService.cs`.
- [X] T048 [US6] Confirm cap/overwrite behavior in `SavedReportService` matches FR-011 / research R7 (new name at 50 → `saved-report-cap`; overwrite at 50 allowed). If T023 already does this, add only a focused assertion in `backend/Zharfa.Api.Tests/SavedReports/SavedReportNameTests.cs` — do not duplicate service SQL here.
- [X] T049 [US6] On `frontend/src/pages/SavedReports.tsx`: show only the signed-in user’s names, types, stored period/filters; Delete with a confirm; gone from list and open 404; surface the cap message. Cross-user ids are not listed.

**Checkpoint**: All six stories are independently functional.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Docs, DTO scan, a11y, and the comment on validation_commands. No new product behavior.

- [X] T050 [P] Update `README.md` and `backend/Zharfa.Api/README.md`: saved-reports tab, Keep/Save, Excel export permission (Admin cannot), print vs export caps (1,000 / 10,000). No new env vars (ClosedXML needs none).
- [X] T051 [P] Extend the comment in `.ainative/project.yaml` to mention spec 007 suites (`VisualIntent`, `LastNMonths`, `SavedReportName`, `ExcelExportService`, matrix/catalog). Do **not** add Ollama/GPU or Compose to `validation_commands`. Point HTTP/UI proof at `specs/007-ai-reports-export/quickstart.md`.
- [X] T052 [P] Add an `[Unreleased]` Phase 5 note to `CHANGELOG.md` following the existing Release 1 unreleased sections (F14 table/chart, F15 saved specs, F16 ClosedXML export, F10 export/saved audit events). Do not invent a SemVer bump unless the implementer is asked to commit a versioned deliverable.
- [X] T053 [P] Extend `backend/Zharfa.Api.Tests/Ai/NoVendorFinancialsInToolsTests.cs` (and/or `NoVendorFinancialsTests.cs`) so visual/export/saved DTO types in `Zharfa.Api.Ai` / `Zharfa.Api.SavedReports` / `ExcelExportService` have no `balance`/`payment`/`debt`/`credit`/`paid` properties.
- [X] T054 Confirm Save/Keep/Export/saved-list dialogs and buttons on the T028–T042 surfaces keep existing Tailwind focus rings and WCAG 2.1 AA contrast (FR-025). No new design system.
- [X] T055 Confirm Phase 2 print twins in `backend/Zharfa.Api/Controllers/PurchasingReportsController.cs` still cap at 1,000 and still return JSON — export must not replace print.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup — **BLOCKS** all user stories
- **User Story 1 (P1)**: After Foundational — MVP (table/chart). No dependency on save/export
- **User Story 2 (P1)**: After Foundational; uses US1 `visual.saveSpec` / `VisualBuilder` for generated Keep/open
- **User Story 3 (P1)**: After US2 open path exists
- **User Story 4 (P1)**: After Foundational + ClosedXML; generated/saved export also need US1 visual spec and US2 saved ids
- **User Story 5 (P1)**: After US4 export endpoints exist
- **User Story 6 (P2)**: After US2 list/save; delete/cap UX
- **Polish (Phase 9)**: After the stories intended for the increment

### User Story Dependencies

- **US1**: Independent after Phase 2 (plus T016–T022)
- **US2**: Integrates US1 Keep; standard Save does not need US1
- **US3**: Depends on US2 generated save/open
- **US4**: Live report export is independent of US1/US2; generated/saved export need those stories
- **US5**: Depends on US4
- **US6**: Depends on US2

### Within Each User Story

- Tests (where listed) MUST be written and FAIL before implementation
- Models / pure helpers before services
- Services before endpoints
- Endpoints before SPA
- Story complete before moving to the next priority when staffing is sequential

### Parallel Opportunities

- T002, T003 in Setup
- T004–T006 and T008–T010 in Foundational
- T014–T015, T016, T020–T021 in US1
- T027–T028, T030 in US2
- T034, T037, T041 in US4
- T046, T050–T053 in Polish
- After Foundational, US1 (visual) and US4 live-report export can proceed in parallel if two implementers; US2 should wait for US1 `saveSpec` before Keep

---

## Parallel Example: User Story 1

```bash
# Tests together:
Task: "Create VisualIntentTests in backend/Zharfa.Api.Tests/Ai/VisualIntentTests.cs"
Task: "Extend PurchasingQuestionRouterTests for last-six-jalali-months"

# Then types / chart together after VisualIntent exists:
Task: "Create VisualIntent.cs"
Task: "Add visual types in frontend/src/services/api.ts"
Task: "Create RankingBarChart.tsx"
```

---

## Parallel Example: User Story 4

```bash
Task: "Create ExcelExportServiceTests.cs"
Task: "Add export download helpers in frontend/src/services/api.ts"
# After ExcelExportService:
Task: "PurchasingReportsController /export twins"
Task: "AiVisualExportController POST /api/ai/visual/export"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. Complete Phase 3: User Story 1 (canonical table + chart)
4. **STOP and VALIDATE**: quickstart Flow A (+ G regression)
5. Then US2 (exit 2) → US3 (exit 3) → US4 (exit 4) → US5 (exit 5) → US6 (cap/delete)

### Incremental Delivery

1. Setup + Foundational → catalogs, migration, ClosedXML
2. US1 → F14 AC1–AC2 / SC-001
3. US2 → F14 AC3, F15 AC1–AC2 / SC-002
4. US3 → F15 AC3 / SC-003
5. US4 → F16 AC1–AC2 / SC-004
6. US5 → F16 AC3–AC4 / SC-005
7. US6 → F15 day-to-day / SC-006
8. Polish → docs and DTO scan

### Parallel Team Strategy

1. Team completes Setup + Foundational together
2. Developer A: US1 then US2 then US3
3. Developer B: US4 live export then US5 (merge generated/saved export after US1/US2)
4. Either: US6 + polish

---

## Notes

- [P] tasks = different files, no unfinished dependency
- [Story] label maps to spec.md US1–US6
- Workers do not run Compose, `dotnet test`, or `npm install`
- Do not reopen Phase 4 20/20 as a Phase 5 exit; Flow G only checks fixture items 1 and 13
- Skip-mode answers (spec Session 2026-11-18 / research R16) are binding: overwrite confirm; generated reopen uses live table-and-chart rules; cap blocks new names only; export of a reopened table is allowed; download name = Persian name + Jalali date + `.xlsx`; generated reopen requires `ai.query`

---

## Phase 10: Convergence

- [X] T056 Remove leftover statements after return in ExecuteReturnsAsync in `backend/Zharfa.Api/Ai/PurchasingAnalystTools.cs` so the project compiles [critic: C1] per FR-006 (contradicts)
- [X] T057 Ensure saved report export re-queries the full filtered table up to 10,000 rows rather than PageSize 20 in `backend/Zharfa.Api/SavedReports/SavedReportService.cs` and `backend/Zharfa.Api/Controllers/SavedReportsController.cs` [critic: C2] per FR-016 (partial)
- [X] T058 Pass stored `filters_json` and `generated_json` arguments into `analystTools.DispatchAsync` on generated reopen in `backend/Zharfa.Api/SavedReports/SavedReportService.cs` and `backend/Zharfa.Api/Ai/PurchasingAnalystTools.cs` [critic: C3] per FR-008 (partial)
- [X] T059 Save canonical data-model period object `{ kind, month|quarter|year|from,to }` from frontend report pages and parse both `kind` and `period` keys in `ResolvePeriod` in `frontend/src/pages/` and `backend/Zharfa.Api/SavedReports/SavedReportService.cs` [critic: C4] per FR-008 (partial)
- [X] T060 Display reopened report table and last-sync time or navigate to live report with stored filters for standard saved reports in `frontend/src/pages/SavedReports.tsx` [critic: C5] per FR-008 (partial)
- [X] T061 Map `VendorComparisonResponse` and `VendorProfileResponse` table slices in `BuildExportTableFromOpen` in `backend/Zharfa.Api/SavedReports/SavedReportService.cs` so saved comparison and profile exports produce full sheets [critic: C6] per FR-019 (partial)

## Phase 11: Convergence

- [X] T062 Check `table.Rows.Count > MAX_ROWS` instead of total unpaged rows when PageSize is capped in `ExecuteGroupedAsync` in `backend/Zharfa.Api/Ai/PurchasingAnalystTools.cs` [tester: A/SC-001] per FR-003 (contradicts)
- [X] T063 Fix TypeScript compilation errors in `frontend/src/pages/PurchasingReports.tsx`, `SavedReports.tsx`, `VendorProfile.tsx`, and `Vendors.tsx` [tester: frontend-build] per FR-022 (contradicts)
- [X] T064 Add `ReportKeys.Generated` to `DataQualityCatalog.cs` mapping so `TypesFor(ReportKeys.Generated)` contains `missing-values` in `backend/Zharfa.Api/DataQuality/DataQualityCatalog.cs` [tester: unit/DataQualityNoticeTests] per FR-015 (missing)
- [X] T065 Align filename sanitization and unit test in `backend/Zharfa.Api/Purchasing/Reports/ExcelExportService.cs` and `backend/Zharfa.Api.Tests/Purchasing/Reports/ExcelExportServiceTests.cs` [tester: unit/ExcelExportServiceTests] per FR-014 (contradicts)
- [X] T066 Write currency «ریال» into export worksheet metadata rows per `contracts/xlsx.md` and add unit test assertion in `backend/Zharfa.Api/Purchasing/Reports/ExcelExportService.cs` and `backend/Zharfa.Api.Tests/Purchasing/Reports/ExcelExportServiceTests.cs` [tester: D/SC-004] per FR-015 (missing)


