# API contract: AI Reports, Saved Reports, Excel Export (Phase 5)

Extends Phase 2 `specs/004-vendor-purchasing-reports/contracts/api.md`, Phase 3 `specs/005-permissions-audit-quality/contracts/api.md` §0, and Phase 4 `specs/006-local-ai-purchasing/contracts/api.md`. Cookie `zharfa_session`. Error envelope `{ "message": "<Persian>" }` plus optional `code`. Amounts remain decimal strings. `dataLabel`: `real` | `demo`.

Phase 4 `POST /api/ai/chat` request, 401/403/409/503, 20-question cited behaviour, Settings → AI, and Demo 1 `/api/ai/demo/chat` are **unchanged** except the 200 body may include optional `visual` (below). Print twins and their 1,000-row cap are unchanged.

## §0. Permission matrix (additions)

Existing rows stay. New / changed actions:

| Endpoint(s) | Policies (all must pass) | admin | management | purchasing | finance |
|---|---|:-:|:-:|:-:|:-:|
| `POST /api/ai/chat` | `ai.query` | 403 | ✓ | ✓ | ✓ |
| `POST /api/ai/visual/export` | `ai.query` then `export` | 403 | ✓ | ✓ | ✓ |
| `GET /api/saved-reports`, `POST /api/saved-reports`, `GET /api/saved-reports/{id}`, `DELETE /api/saved-reports/{id}` | `purchasing.reports` | ✓ (standard kinds only) | ✓ | ✓ | ✓ |
| `POST /api/saved-reports` `kind=generated` | + `ai.query` | 403 | ✓ | ✓ | ✓ |
| `POST /api/saved-reports/{id}/open` | `purchasing.reports`; generated also `ai.query`; transactions also `purchasing.transactions` | ✓ / 403 generated | ✓ | ✓ | ✓ |
| `GET /api/saved-reports/{id}/export` | open rules **and** `export` | 403 | ✓ | ✓ | ✓ |
| `GET /api/purchasing/**/export` | same as matching print **and** `export` | 403 | ✓ | ✓ | ✓ |

Unsigned → 401, no file, no saved data. Missing `export` → 403 `code: forbidden` + `access.denied`, empty body (not xlsx). `PermissionMatrixTests` must list every new action.

`GET /api/system/status` `modules.savedReports`: true iff `purchasing.reports`.

---

## §1. Purchasing chat visual (extends Phase 4 §1)

Request unchanged: `{ "question": "…" }`.

200 `PurchasingChatResponse` **adds optional** `visual` (omit property when null):

```json
{
  "answer": "…",
  "period": { "from": "…", "to": "…", "jalaliLabel": "شش ماه منتهی به آبان ۱۴۰۵", "month": null },
  "filters": { "vendor": null, "product": null, "category": null },
  "view": "top-products",
  "facts": ["metric=net; …"],
  "interpretation": "…",
  "unavailable": false,
  "dataLabel": "demo",
  "demoNotice": "…",
  "visual": {
    "kind": "generated",
    "captions": {
      "period": "شش ماه منتهی به آبان ۱۴۰۵",
      "filters": "بدون فیلتر",
      "metrics": "خالص خرید (ریال)؛ ناخالص، برگشت و مالیات جدا"
    },
    "table": {
      "columns": [
        { "id": "name", "header": "کالا" },
        { "id": "net", "header": "خالص خرید" }
      ],
      "rows": [
        { "key": "P-1", "name": "…", "net": "125000000.0000", "gross": "…", "returns": "…", "vat": "…" }
      ]
    },
    "chart": {
      "type": "bar",
      "categoryId": "name",
      "series": [{ "metric": "net", "label": "خالص خرید", "values": ["125000000.0000"] }]
    },
    "dataQuality": null,
    "saveSpec": {
      "kind": "generated",
      "period": { "kind": "custom", "from": "1405-03-01", "to": "1405-08-30" },
      "filters": {},
      "generated": {
        "tool": "purchasing_top_products",
        "limit": 10,
        "sort": "net",
        "dir": "desc",
        "chart": "bar",
        "metrics": ["net", "gross", "returns", "vat"]
      }
    }
  }
}
```

Rules ([research.md](../research.md) R1–R4, R13):

- `visual` only when `VisualIntent` is list ranking/comparison **and** tools returned a usable row set **and** `unavailable` is false.
- `chart` omitted when row/category count ∉ [2, 20].
- Canonical question (EN or FA: compare top 10 purchased products over last six months): `view` = `top-products`, six Jalali months ending Overview default month, ranking net desc, ≤ 10 rows, figures **equal** `GET /api/purchasing/reports/top-products` for that custom range.
- 503 / 409 / 200-unavailable: no `visual`.
- Cloud JSON may include the same aggregate rows; not lines.

---

## §2. Saved reports

### `GET /api/saved-reports`

```json
{
  "dataLabel": "demo",
  "max": 50,
  "items": [
    {
      "id": 12,
      "name": "ده کالای شش‌ماهه",
      "kind": "generated",
      "kindLabel": "گزارش تولیدشده",
      "requiredPermission": "ai.query",
      "period": { "kind": "custom", "from": "1405-03-01", "to": "1405-08-30", "label": "…" },
      "filters": [],
      "updatedAtJalali": "۱۴۰۵/۰۸/۲۷ ۱۴:۰۲"
    }
  ]
}
```

Sorted `updated_at` desc. Only the caller’s rows.

### `POST /api/saved-reports`

```json
{
  "name": "ده کالای شش‌ماهه",
  "overwrite": false,
  "kind": "generated",
  "period": { "kind": "custom", "from": "1405-03-01", "to": "1405-08-30" },
  "filters": {},
  "generated": { "tool": "purchasing_top_products", "limit": 10, "sort": "net", "dir": "desc", "chart": "bar", "metrics": ["net"] }
}
```

Standard save: `kind` from the catalog, `generated` omitted, `filters` as on screen (including `vendors` for comparison).

| Outcome | Status |
|---|---|
| created | 201 item |
| replaced (`overwrite: true`, same `name_key`) | 200 item |
| duplicate name, `overwrite` false/omitted | 409 `{ "message": "گزارشی با این نام دارید. آیا جایگزین شود؟", "code": "saved-report-exists", "id": 12 }` |
| 50 rows and new name | 409 `{ "message": "حداکثر ۵۰ گزارش ذخیره شده مجاز است. ابتدا یکی را حذف کنید.", "code": "saved-report-cap" }` |
| empty name | 400 |
| generated without `ai.query` | 403 |
| bad kind / six comparison vendors / bad generated tool | 400 |

Audit: `saved_report.saved` or `saved_report.replaced`.

### `GET /api/saved-reports/{id}`

200 metadata (same item shape). 404 if missing or not owned.

### `POST /api/saved-reports/{id}/open`

Re-runs on current sync. Body empty.

| Outcome | Status | Body |
|---|---|---|
| standard | 200 | same table/envelope as the live report (including `dataQuality`, `lastSuccessfulSync`) plus `{ "saved": { "id", "name", "kind" } }` |
| generated | 200 | chat-like `{ period, filters, view, visual, dataLabel, unavailable }` **without** requiring a new model call; table/chart rules as live visual; last-sync in envelope |
| generated, caller lacks `ai.query` | 403 | Persian reason naming AI-queries; **no** figures; `access.denied` |
| missing standard permission | 403 | names purchasing-reports (or transactions) |
| no successful sync / unverified source | 200 | live-report unavailable shape (`unavailable` figures / «داده در دسترس نیست» table none for generated) |
| not owned / unknown | 404 | |
| unsigned | 401 | |

Audit: `saved_report.opened` on 200. Generated 403: `access.denied` only (not opened).

Generated open **does not** call the LLM (spec: re-run metrics). Provider down does not block generated or standard open.

### `DELETE /api/saved-reports/{id}`

204. Audit `saved_report.deleted`. 404 if not owned.

---

## §3. Excel export

Success: **200** raw xlsx bytes. Headers:

- `Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`
- `Content-Disposition: attachment; filename="…xlsx"; filename*=UTF-8''…` (sanitized Persian name + `-` + Jalali `yyyy-mm-dd` + `.xlsx`; empty → `گزارش-…xlsx`)
- File interior: [xlsx.md](xlsx.md)

### Live twins (GET, same query as the table, ignore `page`/`pageSize`)

- `GET /api/purchasing/reports/{reportId}/export`
- `GET /api/purchasing/vendors/export`
- `GET /api/purchasing/vendors/compare/export`
- `GET /api/purchasing/documents/export`
- `GET /api/purchasing/vendor-profile/export?vendor=&table=documents|top-products|returns` (+ period params)

### Generated

`POST /api/ai/visual/export` body = `visual.saveSpec` (period, filters, generated). Server re-runs the tool; 400 if spec invalid; 200-unavailable chat is not exportable (400 «جدولی برای خروجی وجود ندارد.»).

### Saved

`GET /api/saved-reports/{id}/export` — open the spec then export that table. 403 if cannot open or cannot export (Admin opening a **standard** saved report: open 200, export 403).

Cap: first 10,000 data rows; meta states truncation and «فیلترها را محدود کنید» when `totalRows > 10000`. Empty table: headers + meta, zero data rows.

Audit `report.exported` on 2xx. Admin GET export → 403 + `access.denied`.

---

## §4. Audit filter labels

`GET /api/admin/audit/actions` includes the five new codes ([data-model.md](../data-model.md) §6). Entries never contain result rows, secrets, or keys.
