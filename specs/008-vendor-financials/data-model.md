# Data model: Vendor Financials (Release 1, Phase 6)

**Spec**: [spec.md](spec.md) · **Research**: [research.md](research.md) · **Contracts**: [contracts/api.md](contracts/api.md)

Purchasing tables from Phases 1–2 are unchanged. This file adds payable sync tables, formula functions, saved-report kinds, reconciliation metric, and availability rules.

## 1. Synced copy (`mabna` schema)

Migration `db/migrations/0011_vendor_financials.sql` (forward-only, idempotent). Amounts `numeric(24,4)` Rial. Dates Gregorian `date` plus Jalali parts like `mabna.purchase_document`. `source_key` is the Mabna/sim key as text.

### 1.1 `mabna.payable_document`

Open accounting payables / credit notes (entity `payableDocument`).

| Column | Type | Notes |
|---|---|---|
| `payable_document_id` | bigint identity PK | |
| `source_key` | text UNIQUE NOT NULL | |
| `vendor_source_key` | text NULL | Relink to `mabna.vendor` |
| `vendor_id` | bigint NULL FK vendor | |
| `doc_date` | date NOT NULL | As-of: included when `doc_date ≤ period_end` |
| `jalali_year/month/day` | smallint | Same CHECKs as purchase documents |
| `doc_number` | text NULL | Reference shown to users |
| `remaining_amount` | numeric(24,4) NOT NULL | **Signed**; negative = credit note |
| `cancelled` | boolean NOT NULL DEFAULT false | |
| `synced_at` | timestamptz NOT NULL | |

Index: `(vendor_id, doc_date)` WHERE NOT cancelled.

### 1.2 `mabna.payable_cheque`

| Column | Type | Notes |
|---|---|---|
| `payable_cheque_id` | bigint identity PK | |
| `source_key` | text UNIQUE NOT NULL | |
| `vendor_source_key` | text NULL | |
| `vendor_id` | bigint NULL FK | |
| `issue_date` | date NOT NULL | Included in outstanding/balance when `issue_date ≤ period_end` |
| `jalali_*` | | From issue date |
| `reference` | text NULL | Cheque / instrument number |
| `instrument_type` | text NOT NULL | Map-driven code (e.g. sim `ChqTp`); stored as mapped label key |
| `state` | text NOT NULL | Map-driven paid/open/cancelled state |
| `amount` | numeric(24,4) NOT NULL CHECK ≥ 0 | Face amount |
| `remaining_amount` | numeric(24,4) NOT NULL CHECK ≥ 0 | Unpaid remainder as stored at sync; formula may reduce by payments dated ≤ T |
| `cleared_at` | date NULL | Payment/clear date when fully cleared |
| `cancelled` | boolean NOT NULL DEFAULT false | |
| `synced_at` | timestamptz NOT NULL | |

Index: `(vendor_id, issue_date)` WHERE NOT cancelled.

### 1.3 `mabna.vendor_payment`

Completed payments (entity `vendorPayment`).

| Column | Type | Notes |
|---|---|---|
| `vendor_payment_id` | bigint identity PK | |
| `source_key` | text UNIQUE NOT NULL | |
| `vendor_source_key` | text NULL | |
| `vendor_id` | bigint NULL FK | |
| `paid_at` | date NOT NULL | Payment or clear date; history filter `paid_at` in period |
| `jalali_*` | | From paid_at |
| `reference` | text NULL | |
| `instrument_type` | text NOT NULL | `cheque` / `cash` / `transfer` / other mapped |
| `amount` | numeric(24,4) NOT NULL CHECK ≥ 0 | Paid portion (never the unpaid remainder) |
| `instrument_source_key` | text NULL | Link to cheque/document when known |
| `cancelled` | boolean NOT NULL DEFAULT false | |
| `synced_at` | timestamptz NOT NULL | |

Index: `(vendor_id, paid_at)` WHERE NOT cancelled.

### 1.4 `mabna.payment_allocation` (optional)

Only created/synced when `paymentAllocation` is mapped. Splits one instrument across vendors.

| Column | Type | Notes |
|---|---|---|
| `allocation_id` | bigint identity PK | |
| `source_key` | text UNIQUE NOT NULL | |
| `instrument_source_key` | text NOT NULL | |
| `vendor_source_key` | text NOT NULL | |
| `amount` | numeric(24,4) NOT NULL | |
| `synced_at` | timestamptz NOT NULL | |

If the entity is unverified, figures that need allocations are unavailable (research R1/R4).

### 1.5 Formula functions (single definition)

Implemented in the same migration; C# `VendorFinancialTotals` must match for reconciliation.

**`mabna.fn_vendor_balance(as_of date, vendor_source_key text)`** → signed numeric.

Candidate mix (research R1): sum of non-cancelled payable_document.remaining_amount with `doc_date ≤ as_of` [and vendor] **plus** sum of payable_cheque.remaining_amount still open as of `as_of` (not cancelled, not fully cleared with `cleared_at ≤ as_of`, `issue_date ≤ as_of`). Production uses this SQL only when the map’s `balanceFormula` is confirmed/corrected; otherwise the API never calls it for live data.

**`mabna.fn_outstanding_payments(as_of date, vendor_source_key text)`** → non-negative numeric = sum of unpaid remainders of instruments open as of `as_of` (cheques and any other mapped outstanding types). History rows are not included.

**`mabna.fn_vendor_payments(from date, to date, vendor_source_key text)`** → non-negative numeric = sum of `vendor_payment.amount` with `paid_at` in `[from, to]` and not cancelled.

Partial-payment: paid portion lives only in `vendor_payment`; remainder only on the instrument’s `remaining_amount`. The two must not double-count (FR-033).

Purchases: **do not** add a fourth function; call existing `mabna.fn_purchase_totals` / report net for the same vendor and period.

## 2. Source map extensions (still version 1)

Optional `entities` keys (missing is valid): `payableDocument`, `payableCheque`, `vendorPayment`, `paymentAllocation`.

Plus top-level:

```jsonc
"balanceFormula": { "status": "candidate", "mix": "openPayablesPlusUnpaidCheques" },
"partialPayment": { "status": "unknown" }
```

`mix` is documentation for the candidate SQL; production ignores it unless `status` is confirmed/corrected. Field shapes: [contracts/source-map.md](contracts/source-map.md).

Usable item: production `confirmed|corrected`; demo/simulator also `candidate`. `unknown` / null column → dependent figure unavailable.

## 3. Availability (`VendorFinancialAvailability`)

Reason codes (reuse purchasing strings where they match): `no-successful-sync` | `source-item-unverified` | `source-item-unmapped` | `no-previous-data` | `formula-unconfirmed`.

| Figure | Needs |
|---|---|
| `vendorBalance` | usable `balanceFormula` + every entity the mix names |
| `outstandingPayments` | usable cheque (and other outstanding) items |
| `payments` | usable `vendorPayment` + every instrument type the formula includes |
| `paymentHistory` | same as payments |
| `outstandingInstruments` | same as outstanding |
| `netPurchases` (on PvP) | Phase 1 net availability (unchanged) |
| `purchasesMinusPayments` | both net and payments available |
| comparison columns | the figure + previous period has data; else `no-previous-data` |

Zero qualifying rows with a usable formula → **0** / empty list, not unavailable.

## 4. Saved reports

`saved_report.kind` adds the four financial kinds (research R10). `required_permission` may be `vendor.financials`. Server assigns permission from kind. Reopen re-runs `VendorFinancialsService` on current synced data.

Generated visual from a financial tool: `kind=generated` with `generated_json.tool` in the financial allowlist; reopen requires **both** `vendor.financials` and `ai.query`.

## 5. Reconciliation

`reconciliation_run.metric` CHECK adds `vendor_balance`. `filter_kind` still `none|vendor|product` (`product` unused for this command). `discount_basis` stored as `-`. Audit `reconciliation.mismatch` on fail; production detail has no amounts.

## 6. Audit report keys

Add: `vendor-financials`, `vendor-financials-detail`, `vendor-financials-history`, `vendor-financials-outstanding`. Print twins use `report.printed`. Export uses existing `report.exported`.

## 7. Modules

`SystemModules.VendorFinancials` = caller has `vendor.financials`. SPA flag only; API still enforces the policy.

## 8. Validation rules (API)

- Period: existing `JalaliPeriod` (month/quarter/year/custom).
- Search: `fn_search_key` on vendor name/code (ی/ک).
- Sort (list): `name`, `code`, `vendorBalance`, `outstandingPayments` (default balance desc).
- Page size: same as purchasing reports.
- `compare=1` refused (400) on history and outstanding lists.
- Amounts JSON: decimal strings with 4 places, signed for balance.
- `currency` / `currencyLabel`: `IRR` / `ریال`.
- `dataLabel`: `real` | `demo`.

## 9. State

No workflow states. Instrument “open vs paid vs cancelled” is derived from mapped columns at sync time, not a Zharfa state machine.

## 10. Relationships

```text
mabna.vendor 1───* payable_document
mabna.vendor 1───* payable_cheque
mabna.vendor 1───* vendor_payment
vendor_payment *──? payable_cheque (instrument_source_key)
payment_allocation *── instrument + vendor
```

Financial pages join vendors from `mabna.vendor` (purchasing master). A vendor with purchases but no payables still appears on Financials with balance 0 and empty tables when the formula is usable.
