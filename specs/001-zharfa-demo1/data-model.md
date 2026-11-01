# Data Model: Zharfa Demo 1

**Feature**: specs/001-zharfa-demo1 | **Date**: 2026-10-30 | **Phase**: 1

Reference: spec.md Key Entities + FR-012/FR-013/FR-016. Canonical tables own facts; semantic views own KPIs; API and AI read views only, never base tables. All demo data synthetic, labeled demonstration data everywhere. Dates stored Gregorian (`timestamptz`/`date`), displayed Jalali.

## Canonical entities

### ProductCategory

- `category_id` PK (serial/identity), `name_fa` text NOT NULL, `is_active` bool NOT NULL DEFAULT true.
- Single-level grouping; Demo 1 single-select filter source.

### Product

- `product_id` PK, `category_id` FK → ProductCategory NOT NULL, `name_fa` text NOT NULL, `unit_fa` text NOT NULL (e.g. بسته، عدد), `is_active` bool NOT NULL DEFAULT true, `has_batch` bool NOT NULL DEFAULT false (flag only — no expiry workflow).
- Validation: name non-empty; category must exist and be active.

### Customer

- `customer_id` PK, `name_fa` text NOT NULL (synthetic), `opening_balance` numeric(18,2) NOT NULL DEFAULT 0 (demo opening position, disclosed in seed docs), `is_active` bool NOT NULL DEFAULT true.
- Validation: synthetic names only; no real PII. Receivables balance derived, never stored.

### SalesInvoice (header)

- `invoice_id` PK, `customer_id` FK → Customer NOT NULL, `issued_at` timestamptz NOT NULL, `status` text NOT NULL (`posted` only counts; `void`/`draft` excluded from KPIs), `note_fa` text NULL.
- Validation: posted invoices immutable in Demo 1 (no edit path); void excluded from revenue.

### SalesInvoiceItem (line)

- `item_id` PK, `invoice_id` FK → SalesInvoice NOT NULL, `product_id` FK → Product NOT NULL, `qty` numeric NOT NULL CHECK (qty <> 0; negative qty = representative return), `unit_price` numeric(18,2) NOT NULL CHECK (>= 0), `line_discount` numeric(18,2) NOT NULL DEFAULT 0 CHECK (>= 0).
- Line net = `qty * unit_price - line_discount`; validation: line net may be negative only for documented return rows.

### Warehouse

- `warehouse_id` PK, `name_fa` text NOT NULL. Demo 1 needs ≥ 1; multi-warehouse depth deferred.

### StockMovement

- `movement_id` PK, `product_id` FK NOT NULL, `warehouse_id` FK NOT NULL, `moved_at` timestamptz NOT NULL, `qty_delta` numeric NOT NULL (inbound positive, outbound negative), `reason` text NOT NULL (sale/purchase-seed/adjustment), `batch_flag` bool NOT NULL DEFAULT false.
- Current stock per product = `SUM(qty_delta)`. Validation: sale outbounds reference the same period discipline as revenue where applicable; seed covers healthy/low/out-of-stock states.

### Receipt (receivables side only)

- `receipt_id` PK, `customer_id` FK NOT NULL, `received_at` timestamptz NOT NULL, `amount` numeric(18,2) NOT NULL CHECK (> 0).
- Payables side does not exist in Demo 1.

### User / Role

- `role` text (`manager`, `admin`); `user_id` PK, `username` text UNIQUE NOT NULL, `password_hash` text NOT NULL (seeded demo accounts, one per role; no self-registration), `role` NOT NULL, `is_active` bool DEFAULT true.
- All authorization enforced server-side from this table; frontend hiding is cosmetic.

### AuditLog (append-only)

- `audit_id` PK, `at` timestamptz NOT NULL DEFAULT now(), `actor` text NULL, `action` text NOT NULL (e.g. `provider.status.viewed`, `reconciliation.run`, `reconciliation.mismatch`), `detail` text NULL.
- No update/delete path in Demo 1.

### Reserved for Mabna (empty in Demo 1 — FR-016)

- `SourceRecord`, `SyncJob`, `SyncLog`: schema present with minimal columns, zero rows, no wiring, no reads. Exist so pilot sync needs no redesign.

## Semantic KPI views (read-only; the ONLY sources API/AI read)

### v_sales_overview (monthly grain + helpers for trend/top-5)

- Inputs: Gregorian date range `[p_from, p_to)` + optional `p_category_id`.
- Formula (SQL comment on view): `sales_revenue = SUM(qty * unit_price - line_discount)` over posted invoice items in range (+ category predicate). Returns/discounts reduce net representatively; never silently dropped.
- Outputs: `period_revenue`, `prev_period_revenue` (same-length previous Jalali month resolved to Gregorian by caller), `mom_delta`, `mom_pct`, `trend_points[]` (per-day or per-week buckets in range), `top_products[5]` (product, revenue), `top_customers[5]` (customer, revenue).
- Supporting-rows helper: `v_sales_lines` (posted lines in range + category with invoice/customer/product labels) for FR-005 tables.

### v_inventory_alerts

- Threshold: single server-side configured `low_stock_threshold` (documented default), same for all products.
- Outputs: `low_stock[]` (0 < stock <= threshold), `out_of_stock[]` (stock <= 0), each with product, category, warehouse, on-hand qty, `has_batch` flag. No expiry columns beyond the flag.

### v_receivables

- Formulas (SQL comments on view): `cash_received = SUM(receipts.amount)` in range; `receivables_balance(customer) = SUM(posted invoice net for customer) + opening_balance - SUM(receipts for customer)` (lifetime-to-date; period filter scopes the `cash_received` leg only — disclosed in view comment and echoed in answers).
- Outputs: `total_receivables`, `top_debtors[5]` (customer, balance), `payments_received_this_month`; helper `v_receivable_rows` for supporting tables.

## Relationships

- ProductCategory 1—N Product; Product 1—N SalesInvoiceItem; Customer 1—N SalesInvoice + 1—N Receipt; SalesInvoice 1—N SalesInvoiceItem; Warehouse 1—N StockMovement; Product 1—N StockMovement.
- Views join across these; no view reads User/Role/AuditLog/reserved tables; no client reads base tables.

## State transitions

- SalesInvoice: `draft → posted → (void)`; only `posted` feeds KPIs. Receipts: inserted once, never edited. StockMovement: append-only. Users: active/inactive flag only. All transitions server-side; no Demo 1 edit UI for posted records.
