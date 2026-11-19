# Data model: AI Reports, Saved Reports, and Excel Export (Phase 5)

**Feature**: `specs/007-ai-reports-export` | **Plan**: [plan.md](plan.md) | **Spec**: [spec.md](spec.md)

Phases 1–4 tables are unchanged. This phase adds `saved_report` and extends the in-memory catalogs (audit actions, report keys, visual spec). Reports, generated tables and exports still read `mabna.*` via existing services only.

---

## 1. `saved_report` (PostgreSQL)

Migration `db/migrations/0010_ai_reports_export.sql`. Forward-only, idempotent (`IF NOT EXISTS`). Requires `0009`.

| Column | Type | Rules |
|---|---|---|
| `id` | `bigint GENERATED ALWAYS AS IDENTITY` | PK |
| `user_id` | `bigint NOT NULL` | FK `app_user(user_id)` **ON DELETE CASCADE** |
| `name` | `text NOT NULL` | display name as typed (trimmed); not empty |
| `name_key` | `text NOT NULL` | `PersianText.Normalize` of `name`; unique with `user_id` |
| `kind` | `text NOT NULL` | check: catalog in §2 |
| `required_permission` | `text NOT NULL` | `purchasing.reports` \| `ai.query` (Phase 6 may add `vendor.financials`) |
| `period_json` | `jsonb NOT NULL` | §3 |
| `filters_json` | `jsonb NOT NULL` | §3; `{}` if none |
| `generated_json` | `jsonb NULL` | required iff `kind = generated`; else NULL |
| `created_at` | `timestamptz NOT NULL DEFAULT now()` | |
| `updated_at` | `timestamptz NOT NULL DEFAULT now()` | set on replace |

Indexes:

- `uq_saved_report_user_name_key UNIQUE (user_id, name_key)`
- `idx_saved_report_user_updated (user_id, updated_at DESC)`

Constraints:

- `chk_saved_report_kind` — `kind` in §2 list.
- `chk_saved_report_permission` — `required_permission IN ('purchasing.reports','ai.query')`.
- `chk_saved_report_generated` — `(kind = 'generated' AND generated_json IS NOT NULL) OR (kind <> 'generated' AND generated_json IS NULL)`.
- Application cap: at most **50** rows per `user_id` for **insert of a new `name_key`**. Enforced in `SavedReportService` (count before insert). Replace of an existing `name_key` does not increment.

No result rows, amounts, or document lines are stored.

EF: optional `DbSet<SavedReport>` for convenience; writers may use Npgsql as reports do. Schema remains SQL-owned (`MigrationRunner` only).

---

## 2. Kind catalog

| `kind` | Saved from | `required_permission` | Open = re-run |
|---|---|---|---|
| `by-vendor` | purchasing report | `purchasing.reports` | `GroupedAsync("by-vendor")` |
| `by-product` | purchasing report | `purchasing.reports` | `GroupedAsync("by-product")` |
| `by-category` | purchasing report | `purchasing.reports` | `GroupedAsync("by-category")` |
| `trend` | purchasing report | `purchasing.reports` | `TrendAsync` |
| `top-products` | purchasing report | `purchasing.reports` | `GroupedAsync("top-products")` |
| `returns` | purchasing report | `purchasing.reports` | `ReturnsAsync` |
| `transactions` | purchasing report | `purchasing.reports` **and** `purchasing.transactions` at open | `DocumentsAsync` |
| `vendors` | vendor directory table | `purchasing.reports` | directory list |
| `vendors-compare` | vendor comparison | `purchasing.reports` | compare 2..5 keys stored in filters |
| `drill-down` | document list from a row drill | `purchasing.reports` (+ transactions if documents) | `DocumentsAsync` with stored drill filters |
| `vendor-profile` | one profile table | `purchasing.reports` | profile slice in `filters_json.profileTable` |
| `generated` | assistant Keep | `ai.query` | visual re-run of `generated_json` |

Not a kind: Overview KPI tiles; vendor financials; Demo 1 sales/inventory.

`ReportKeys` gains `generated` («گزارش تولیدشده») for audit/export titles. The thirteen Phase 3 keys remain; tests update to fourteen.

---

## 3. JSON shapes (stored, not rows)

### `period_json`

Same as the report query: `{ "kind": "month"|"quarter"|"year"|"custom", "month"?: "YYYY-MM", "quarter"?: "YYYY-Qn", "year"?: "YYYY", "from"?: "YYYY-MM-DD", "to"?: "YYYY-MM-DD" }`. Canonical six months is stored as `kind=custom` with the computed `from`/`to`.

### `filters_json`

Subset of report filters actually used: `vendor`, `product`, `category`, `docType`, `q`, `sort`, `dir`, `compare`, `vendors` (array of 2..5 keys for comparison), `profileTable` (`documents` \| `top-products` \| `returns`), `vendor` (profile). Unknown keys ignored on open (do not fail open). Invalid compare selection (six vendors) refused on **save** with the Phase 2 limit message.

### `generated_json`

```json
{
  "tool": "purchasing_top_products",
  "limit": 10,
  "sort": "net",
  "dir": "desc",
  "chart": "bar",
  "metrics": ["net", "gross", "returns", "vat"]
}
```

`tool` must be on the Phase 4 allowlist. `metrics` are report column ids, never a second calculation. Re-open uses current data + `VisualIntent` chart rule (chart only if 2..20 categories **now**).

---

## 4. Visual payload (not persisted except via `generated_json`)

Built only from the tool’s row DTO:

- `table.columns[]`: `{ id, header }` Persian headers matching the on-screen report.
- `table.rows[]`: cells as decimal **strings** (Rial, 4 decimals) like reports; names as text.
- `table.captions`: period label, filters, metric labels.
- `chart`: omitted or bar/line series copied from the **same** columns.
- `saveSpec`: `{ kind: "generated", period, filters, generated }` for POST `/api/saved-reports`.

Row cap for visual = tool cap (100). Over cap → no visual, metric-unavailable (Phase 4).

---

## 5. Export table (ephemeral)

In-memory `ExportTable` passed to ClosedXML:

| Field | Source |
|---|---|
| `reportName` | `ReportKeys.Name(kind)` or saved `name` |
| `periodLabel` | envelope period |
| `filtersLabel` | envelope filters joined |
| `exportedAt` / `exportedAtJalali` | `TimeProvider` + sync TZ |
| `lastSync` | last successful sync |
| `dataLabel` / demo sentence | `DataModeOptions` |
| `dataQualityText` | Phase 3 notice text if any |
| `headers[]` | Persian column titles |
| `rows[][]` | already formatted strings (Persian digits, «ریال» in meta not every cell) |
| `totalRows` | full filtered count |
| `truncated` | `totalRows > 10000` |
| `limit` | 10000 |

Sheet layout: [contracts/xlsx.md](contracts/xlsx.md). Nothing in `saved_report`.

---

## 6. Audit catalog additions

Append to `AuditActions.All` (order after `ai.question`):

| Code | Label |
|---|---|
| `report.exported` | خروجی اکسل |
| `saved_report.saved` | ذخیره گزارش |
| `saved_report.replaced` | جایگزینی گزارش ذخیره شده |
| `saved_report.opened` | باز کردن گزارش ذخیره شده |
| `saved_report.deleted` | حذف گزارش ذخیره شده |

`target`: `saved:{id}:{name_key}` or `report:{kind}` (no amounts). `detail`: `period=…; filters=…` (keys/labels only). Access failures stay `access.denied` with `permission=export` or `permission=ai.query` / `purchasing.reports`.

---

## 7. Status / modules

`GET /api/system/status` `modules.savedReports` = caller holds `purchasing.reports`. `export` is **not** a module; it remains a permission on `Me.permissions`.

---

## 8. Validation rules (application)

| Rule | On fail |
|---|---|
| Name whitespace-only | 400 |
| Duplicate `name_key` without overwrite | 409 `saved-report-exists` |
| Count ≥ 50 and new name | 409 `saved-report-cap` |
| `kind=generated` without `ai.query` | 403 + `access.denied` |
| Open generated without `ai.query` | 403, no query, `access.denied` |
| Open transactions without `purchasing.transactions` | 403 |
| Other user’s id | 404 |
| Comparison `vendors.length` not in 2..5 | 400 (Phase 2 text) |
| Generated `tool` not allowlisted | 400 |

State: there is no workflow beyond created → replaced → deleted. Deleted rows are gone (CASCADE if the user is removed). Deactivated users keep rows but cannot authenticate.
