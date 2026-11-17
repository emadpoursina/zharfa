# Data Model: Real Purchase Data (Release 1, Phase 1)

**Feature**: `specs/003-real-purchase-data` | **Date**: 2026-11-16 | **Plan**: [plan.md](plan.md) | **Research**: [research.md](research.md)

PostgreSQL 18, forward-only SQL migrations in `db/migrations/` applied by `MigrationRunner`. Two new migrations:

- `0005_purchasing_sync.sql` — `mabna` schema tables, extensions of the reserved `sync_job` / `sync_log` / `source_record`, `install_setting`, `reconciliation_run`, indexes.
- `0006_purchasing_kpis.sql` — metric functions in `mabna` (single definition per metric) and `mabna.fn_sync_fingerprint()`.

Real Mabna table and column names never appear here; they live in the gitignored source map ([contracts/source-map.md](contracts/source-map.md)). All synced business data lives in schema `mabna`; Demo 1 synthetic data stays in `public` and is never loaded in production mode.

---

## 1. Synced copy (schema `mabna`)

### `mabna.vendor`

| Column | Type | Rules |
|---|---|---|
| `vendor_id` | `bigint` identity PK | Zharfa surrogate key |
| `source_key` | `text` NOT NULL UNIQUE | Source primary key, as text |
| `name_fa` | `text` NOT NULL | Normalized (ي→ی, ك→ک, trimmed, whitespace collapsed); empty source name → `'(بدون نام)'` |
| `is_active_source` | `boolean` NULL | Source active flag if mapped, else NULL |
| `synced_at` | `timestamptz` NOT NULL | Commit time of the run that last wrote the row |

### `mabna.product`

| Column | Type | Rules |
|---|---|---|
| `product_id` | `bigint` identity PK | |
| `source_key` | `text` NOT NULL UNIQUE | |
| `name_fa` | `text` NOT NULL | Normalized as above |
| `drug_group_key` | `text` NULL | Reserved for later phases; mapped only if verified |
| `synced_at` | `timestamptz` NOT NULL | |

### `mabna.purchase_document`

| Column | Type | Rules |
|---|---|---|
| `document_id` | `bigint` identity PK | |
| `source_key` | `text` NOT NULL | Source header key |
| `doc_type` | `text` NOT NULL CHECK IN (`invoice`, `return`) | From the verified operation code(s) |
| `doc_number` | `text` NULL | Display only |
| `vendor_source_key` | `text` NULL | NULL when the source has no vendor |
| `vendor_id` | `bigint` NULL → `mabna.vendor` | NULL when missing from master (counted under "unknown vendor") |
| `doc_date` | `date` NOT NULL | Gregorian, converted at ingestion |
| `jalali_year` / `jalali_month` / `jalali_day` | `smallint` NOT NULL | From the same conversion; month 1..12, day 1..31 |
| `cancelled` | `boolean` NOT NULL DEFAULT false | Rule from on-site verification (flag, zeroed header, or code) |
| `doc_discount` | `numeric(24,4)` NOT NULL DEFAULT 0 | Document-level discount, ≥ 0; 0 when unmapped |
| `synced_at` | `timestamptz` NOT NULL | |

Unique: (`doc_type`, `source_key`) — invoices and returns may come from different source tables with overlapping keys.
Indexes: (`jalali_year`, `jalali_month`) WHERE NOT `cancelled`; (`vendor_id`); (`doc_date`).

### `mabna.purchase_line`

| Column | Type | Rules |
|---|---|---|
| `line_id` | `bigint` identity PK | |
| `document_id` | `bigint` NOT NULL → `mabna.purchase_document` ON DELETE CASCADE | Lines are replaced as a set per document |
| `source_key` | `text` NOT NULL | Source line key |
| `product_source_key` | `text` NULL | |
| `product_id` | `bigint` NULL → `mabna.product` | NULL → "unknown product" bucket |
| `quantity` | `numeric(24,4)` NULL | As recorded |
| `gross_amount` | `numeric(24,4)` NOT NULL | Before discounts, excl. VAT, **positive** (sign normalized at ingestion; direction comes from `doc_type`) |
| `line_discount` | `numeric(24,4)` NOT NULL DEFAULT 0 | ≥ 0 |
| `vat_amount` | `numeric(24,4)` NULL | NULL when VAT is not mapped/verified → VAT unavailable |

Unique: (`document_id`, `source_key`). Index: (`product_id`).
Validation at ingestion: `gross_amount ≥ line_discount` is **not** enforced (source truth wins) but a violation is counted as a warning in `sync_log`.

### Ingestion rules (all tables)

- Text: `PersianText.Normalize` (ي→ی, ك→ک, trim, collapse spaces, keep ZWNJ).
- Dates: per-column format from the map (`jalaliText`, `jalaliInt`, `gregorian`); unparseable → row rejected, counted in `sync_log.rows_rejected`; run fails if rejections > `Sync__MaxRejectedRows` (default 0).
- Amounts: `decimal` end to end; no floating point; sign normalized to positive using the map's `amountSign` (`asIs` | `negate` | `abs`).
- Cancelled: map rule `cancelled` (column + value, or "header total = 0 and no lines").

---

## 2. Sync bookkeeping (reserved tables, extended by `0005`)

### `sync_job` (one row per run)

Existing columns kept (`id`, `job_name`, `status`, `started_at`, `finished_at`); added:

| Column | Type | Rules |
|---|---|---|
| `job_name` | | Set to `purchasing` |
| `trigger` | `text` NOT NULL CHECK IN (`manual`, `scheduled`, `startup-recovery`) | |
| `mode` | `text` NOT NULL CHECK IN (`incremental`, `full`) | First run ever is always `full` |
| `status` | | CHECK IN (`running`, `succeeded`, `failed`, `skipped`) |
| `requested_by` | `text` NULL | Username for manual runs |
| `duration_ms` | `bigint` NULL | Set when finished |
| `error_message` | `text` NULL | Scrubbed with `RedactionHelper` (no connection strings, server or login names) |
| `skip_reason` | `text` NULL | e.g. "a run was already in progress" |

Partial unique index: `ON sync_job ((true)) WHERE status = 'running'` — at most one running job.

State transitions:

```
(new) --manual/scheduled--> running --all tables ok, commit--> succeeded
                              |--error/timeout/fault--------> failed   (data transaction rolled back)
(new) --scheduled while running--> skipped
running --API restart detected at startup--> failed ("interrupted")
```

### `sync_log` (per-table detail and errors)

Existing columns kept (`id`, `sync_job_id`, `level`, `message`, `logged_at`); added:

| Column | Type | Rules |
|---|---|---|
| `table_logical` | `text` NULL | `vendor`, `product`, `purchase_invoice`, `purchase_return`, `purchase_line` (NULL for job-level messages) |
| `rows_read` / `rows_inserted` / `rows_updated` / `rows_deleted` / `rows_rejected` | `integer` NULL | Per table; reported by the Data sync page |
| `level` | | CHECK IN (`info`, `warning`, `error`) |

`sync_log.sync_job_id` gets an FK to `sync_job(id)`.

### `source_record` (per source table sync state)

Repurposed reserved table; existing columns kept; added:

| Column | Type | Rules |
|---|---|---|
| `source_system` | | `mabna` |
| `table_logical` | `text` NOT NULL | Unique with `source_system` |
| `strategy` | `text` NOT NULL CHECK IN (`rowversion`, `modifiedAt`, `idWatermark`, `full`) | From the map. Header tables: `idWatermark` is read in full on every run; `rowversion` and `modifiedAt` add a key-only pass for deletions (research R5). No window |
| `watermark` | `text` NULL | Last committed watermark (hex rowversion, ISO time or id) |
| `last_full_load_at` | `timestamptz` NULL | |
| `recorded_at` | | Updated in the same data transaction as the rows |

`payload` stays unused (NULL).

---

## 3. Install and reconciliation

### `install_setting`

| Column | Type | Rules |
|---|---|---|
| `key` | `text` PK | `data_mode` |
| `value` | `text` NOT NULL | `production` or `demo` |
| `set_at` | `timestamptz` NOT NULL | |

Written once on first start; startup refuses if config mode ≠ stored mode, or if mode is `production` and any `db/seed/*` name is in `schema_migrations`.

### `reconciliation_run`

| Column | Type | Rules |
|---|---|---|
| `id` | `bigint` identity PK | |
| `run_at` | `timestamptz` NOT NULL | |
| `jalali_month` | `text` NOT NULL | `YYYY-MM` |
| `filter_kind` | `text` NOT NULL CHECK IN (`none`, `vendor`, `product`) | |
| `filter_value` | `text` NULL | Source key |
| `metric` | `text` NOT NULL CHECK IN (`net`, `gross`, `returns`, `vat`) | One row per metric |
| `system_total` / `source_total` / `difference` | `numeric(24,4)` NULL | NULL when the metric is unavailable |
| `outcome` | `text` NOT NULL CHECK IN (`pass`, `mismatch`, `unavailable`, `error`) | |
| `discount_basis` | `text` NOT NULL | Basis in force for the run |
| `data_mode` | `text` NOT NULL | |

Append-only by convention (no update path in code). On-site rows stay in the on-site database; only pass/fail goes to `source-verification.md`.

---

## 4. Metric definitions (single source: `0006_purchasing_kpis.sql`, documented in `docs/metrics/purchasing.md`)

Let *D(m)* be non-cancelled documents with `(jalali_year, jalali_month) = m`, optionally filtered by vendor or product (product filter applies at line level).

| Metric | Definition |
|---|---|
| `line_amount` | `gross_amount − line_discount` (basis `line`); with basis `lineAndDocument`, minus the line's pro-rata share of `doc_discount` |
| **Gross purchases** | Σ `line_amount` over lines of `invoice` documents in *D(m)* |
| **Returns** | Σ `line_amount` over lines of `return` documents in *D(m)* |
| **Net purchases** | Gross purchases − Returns (excl. VAT, by document date) |
| **VAT** | Σ `vat_amount` (invoices) − Σ `vat_amount` (returns); invoice VAT and return VAT are also returned separately |
| **Active vendors** | Count of distinct non-NULL `vendor_source_key` with ≥ 1 document in *D(m)* (non-cancelled invoice or return). A document whose vendor key is missing from the vendor master still counts; a document with no vendor key (the unknown bucket) does not (`mabna.fn_active_vendors`) |
| **Top 5 vendors** | Net purchases grouped by `vendor_id`, descending, top 5; NULL vendor shown as "نامشخص" (unknown) and may appear in the list |
| **Top 5 products** | Net purchases grouped by line `product_id`, same rules |
| **Comparison** | `delta = net(m) − net(m−1)`; `pct = delta / net(m−1)` or `null` when `net(m−1) = 0`; `unavailable` when *m−1* precedes the first synced month |
| **Trend** | Net purchases for the 12 Jalali months ending at *m*; months before the first synced month → `unavailable` |

Not presented as purchases: payments, vendor balance, price differences (FR-015).

Functions (all `STABLE`, `SECURITY INVOKER`, read-only):

- `mabna.fn_purchase_line_amounts(p_year, p_month, p_vendor_key, p_product_key, p_basis)` → one row per purchase line of the month: `doc_type, document_id, line_id, product_source_key, line_amount, vat_amount` (the line amount after the basis; the document discount is allocated over all lines of its document before the product filter, research R9)
- `mabna.fn_purchase_totals(p_year smallint, p_month smallint, p_vendor_key text DEFAULT NULL, p_product_key text DEFAULT NULL, p_basis text DEFAULT 'line')` → `gross, returns, net, vat_invoice, vat_return, vat, document_count`
- `mabna.fn_purchase_trend(p_year, p_month, p_months int DEFAULT 12, p_basis)` → rows `(jalali_year, jalali_month, net, has_data)`
- `mabna.fn_top_vendors(p_year, p_month, p_limit int DEFAULT 5, p_basis)` / `mabna.fn_top_products(...)` → `(key, name_fa, net, is_unknown)`
- `mabna.fn_active_vendors(p_year, p_month)` → `integer`
- `mabna.fn_first_data_month()` → `(jalali_year, jalali_month)` (no row when the copy is empty)
- `mabna.fn_sync_fingerprint()` → `(table_name, row_count, content_hash)`; hash = `md5(string_agg(... ORDER BY source_key))` over key + amount columns

### Metric availability

| Metric | Unavailable when |
|---|---|
| All figures | No `succeeded` sync job exists |
| Gross | `cancelled` rule on the invoice header not mapped (no column/value for rule `column`, unknown rule) or not usable; cancelled invoices cannot be excluded (FR-001) |
| Returns | `cancelled` rule on the return header not mapped or not usable; cancelled returns cannot be excluded (FR-001) |
| Net, Top lists, Trend, Comparison, VAT, Active vendors | `cancelled` rule on either header not usable, because each of these figures uses both invoices and returns (FR-001) |
| Gross, Returns, Net, Top lists, Trend, Comparison | Basis `lineAndDocument` and `docDiscount` on the invoice (Gross) or return (Returns) header not mapped or not usable (FR-014) |
| Gross, Net, Top lists, Trend | Invoice header/line amount, date, or doc-type items not usable in the map |
| Returns, Net | Return header/line items not usable |
| VAT | VAT column not mapped or not usable |
| Active vendors, Top vendors | Vendor key on header not usable |
| Top products | Product key on line not usable |
| Comparison | Preceding month before the first synced month, or Net unavailable |

"Usable" = status `confirmed`/`corrected` in production; additionally `candidate` in demo mode (research R2).

---

## 5. Source verification record

Kept in two places by design (FR-002, spec 002 FR-02):

- **Gitignored source map** (real names): per item `status`, `verifiedOn`, `note`. This is what the code enforces.
- **Committed `specs/003-real-purchase-data/source-verification.md`** (logical names only): one row per logical item (e.g. "purchase invoice header — document date", "purchase return — operation code", "discount basis of Mabna's purchase report total"), status, date, and the on-site reconciliation pass/fail. Created during implementation from the checklist section added to `onsite-checklist.md`.

## 6. Data mode entity

Install-level, from `Zharfa__DataMode`, mirrored once into `install_setting`. Exposed read-only by `GET /api/system/status`. No API or UI can change it.
