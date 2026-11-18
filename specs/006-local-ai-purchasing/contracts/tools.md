# Purchasing tool allowlist (Phase 4)

Tools run only from `POST /api/ai/chat` via `PurchasingAnalystTools`, and **only after** `PurchasingQuestionRouter` assigns `scope=purchasing`. Demo 1 tools stay on `/api/ai/demo/chat` and are not listed here. The model does **not** choose tools (research R21–R22).

Each call: timeout 10 s; at most 100 result rows; if the matching report’s `totalRows > 100` (or `limit`), return metric unavailable (no truncated guess). At most 3 calls per question (in-domain dispatch uses 1 mapped tool). Whole answer 30 s. Read-only; no Mabna; no writes; no vendor-financial tools.

## Scope gate (must not execute tools)

| `scope` | User-visible | Tools executed |
|---|---|---|
| `out-of-domain-sales` | 200 «داده در دسترس نیست» | none (never `purchasing_overview` — Q13) |
| `out-of-domain-inventory` | same | none |
| `vendor-financials` | same; no balance/payment/cheque amounts | none |
| `mabna-bypass` | same | none |
| `no-successful-sync` | same when no succeeded job | none |

## In-domain dispatch (fixture 1–12, 19)

| ids | mapped tool | period rule |
|---|---|---|
| 1, 2, 8, 9, 10 | `purchasing_overview` | `overview-default` |
| 3 | `purchasing_trend` | `overview-default` |
| 4 | `purchasing_by_vendor` or overview `topVendors` | `overview-default` |
| 5 | `purchasing_top_products` | `overview-default` |
| 6 | `purchasing_by_category` | `overview-default` |
| 7 | `purchasing_returns` **and/or** overview `returns` (must cite returns total matching Overview) | `overview-default` |
| 11 | `purchasing_overview` | `previous-jalali-month` |
| 12 | `purchasing_by_vendor` (quarter comparison) | `current-jalali-quarter` |
| 19 | `purchasing_overview` | `future-empty-month` (next empty Jalali month, **not** current) |

Period arguments (all optional): `month` (`YYYY-MM` Jalali), `quarter`, `year`, `from`+`to` (Jalali day or Gregorian `YYYY-MM-DD` as reports already accept). Omitted ⇒ current Jalali month (Overview default). Filters: `vendor`, `product`, `category` as **names**; server resolves with Phase 2 lookup + `fn_search_key` (ی/ک). Unknown name ⇒ metric unavailable, not a guessed key.

Cloud-bound JSON (same as the tool’s `ContentJson`) may include ranked **names** and amounts, counts, period labels, `view`, `unavailable` flags. Must not include: line arrays, document line text, user/PII fields, API keys, passwords, source row dumps.

## Tools

### `purchasing_overview`

Overview totals (net, gross, returns, VAT, VAT invoice, VAT return — each labeled, never renamed as profit/payment/balance), previous-month comparison, 12-month trend points, active vendor count, top 5 vendors, top 5 products, last successful sync, data mode. View: `purchasing_overview` / Overview.

### `purchasing_by_vendor` / `purchasing_by_product` / `purchasing_by_category` / `purchasing_top_products` / `purchasing_returns` / `purchasing_trend`

Same grouping and columns as `GET /api/purchasing/reports/{id}` for `by-vendor`, `by-product`, `by-category`, `top-products`, `returns`, `trend`. Default sort: report default (net desc except trend by month). `limit` default 100.

### `purchasing_transactions_summary`

Document **headers** + in-scope totals only (date, number, vendor **name**, net, doc type) — the transactions report without line expansion. No `DocumentLinesAsync`. Row cap 100; over cap ⇒ unavailable.

## Non-tools (must not exist in this phase)

- Vendor balance, payables, payments, cheques
- Document lines
- Sales, inventory, receivables (production)
- Arbitrary SQL / Mabna / HOST_ID views

If a name outside this allowlist is ever requested, refuse it; the user-visible answer is «داده در دسترس نیست» when no successful purchasing fact was produced. Out-of-domain questions never reach this step.

## System prompt duties (phrasing only)

Persian; facts only from the server-injected facts block; cite period, filters, source metric; separate interpretation; no tables/charts; no extra numbers; purchases ≠ payments ≠ balance; price difference ≠ profit; **never** present purchase net as فروش / pharmacy sales.
