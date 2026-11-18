# Purchasing metrics: definitions

**Scope**: Release 1, Phase 1 (`zharfa-prd.md` v3.0, F2; `specs/003-real-purchase-data`, FR-014/FR-015).
**Single source**: each metric has exactly one definition here. The SQL in `db/migrations/0006_purchasing_kpis.sql`
(`mabna.fn_purchase_line_amounts`, `mabna.fn_purchase_totals`, `mabna.fn_first_data_month`) and the C# recomputation
in `backend/Zharfa.Api/Purchasing/SourceTotalsCalculator.cs` (with `DiscountAllocator.cs`) implement it. The
reconciliation (`reconcile purchasing`) compares the two. A change to a definition changes both, and the change is
recorded here.

**Consumers**: the Overview (Phase 1, F4), the reconciliation (Phase 1), and, in later phases, reports, exports and
AI answers. Every consumer uses these definitions unchanged.

## Conventions

- **Units**: Rial (ریال), stored as `numeric(24,4)` exactly as the source records them. Displayed with Persian digits.
- **Sign**: every stored amount is positive. The direction of a figure comes from the document type (invoice or return).
- **Period**: a Jalali month, taken from the document date (`jalali_year`, `jalali_month`). A purchase return counts
  in the month of its own document date, not the month of the invoice it corrects.
- **Cancelled documents**: excluded from every figure and from the document count. A figure needs the cancelled rule of
  both headers to be verified on-site; otherwise it is "data unavailable" (MetricAvailability).
- **Vendor filter**: on the document header (source vendor key). A document with no vendor is counted in the company
  totals, under the unknown vendor bucket.
- **Product filter**: on the line (source product key). A line with no product is counted in the company totals,
  under the unknown product bucket.

## Discount basis

The discount basis decides the amount of one line. It is a server setting, `Purchasing__DiscountBasis`, and in
production it must equal the basis verified on-site in the source map (startup refusal otherwise).

| Basis | Line amount | Status |
|---|---|---|
| `line` (default) | `gross_amount − line_discount` | In use until the basis of Mabna's own purchase report total is verified on-site. |
| `lineAndDocument` | `gross_amount − line_discount − share of doc_discount` | Used when Mabna's report subtracts document-level discounts too. |

Under `lineAndDocument` the document discount is allocated over every line of its document, by line amount:

1. Amounts are taken in units of 0.0001 (the storage scale), so the arithmetic is exact.
2. Each line's share is `round(doc_discount × line amount ÷ sum of the document's line amounts)`, rounded half away
   from zero to 4 decimals. The share is zero when the discount or the line total is zero.
3. The rounding remainder (`doc_discount − sum of shares`) goes to the largest line. A tie goes to the lowest line key.
   The shares therefore add up to the document discount exactly.
4. The product filter applies after the allocation, so a product's share uses the document's other lines too.
5. A document with no line has nothing to allocate to, so its document discount is not applied. The reconciliation
   applies the same rule.

To change the basis: verify it on-site, set the source map's `discountBasis` and `Purchasing__DiscountBasis` to the
same value, run a full sync, then run the reconciliation again. Figures change only through the basis, never through a
stored value.

## Figures

**Line amount** (per line): as above.

**Gross purchases** = Σ line amount of purchase **invoices** in the period.

**Returns** = Σ line amount of purchase **returns** in the period (shown as a positive figure).

**Net purchases** = Gross purchases − Returns. Excludes VAT. By document date. Net can be negative in a month where
returns exceed invoices; the figure is shown as it is.

**VAT**
- VAT on invoices = Σ VAT of invoice lines.
- VAT on returns = Σ VAT of return lines.
- **VAT (net)** = VAT on invoices − VAT on returns. This is the VAT shown on the Overview.
- A line whose VAT is not recorded adds nothing to these sums. The VAT figures are "data unavailable" when the VAT
  column is not verified on-site.

**Document count** = number of non-cancelled documents (invoices and returns) in the period that match the vendor
filter.

**Active vendors** = number of distinct vendors with at least one non-cancelled document (invoice or return) in the
period. A vendor is identified by its source key, so a document whose vendor key is missing from the vendor master
still makes its vendor active. A document with no vendor key (the unknown vendor bucket) is not counted.

**Top 5 vendors** = the five vendors with the highest net purchases in the period, descending. A vendor's net purchases
are its invoice line amounts minus its return line amounts. A document without a vendor, or whose vendor key is missing from the vendor master, appears as one entry, "نامشخص"
(unknown). Fewer than five vendors → the list shows the ones that exist.

**Top 5 products** = the same rule, grouped by product, with the same "نامشخص" entry for lines without a product.

**Comparison** (Overview only) = net purchases of the selected month − net purchases of the immediately preceding Jalali
month. The percentage is the change divided by the preceding month's net purchases, and it is "no percentage" when that
base is zero. The whole comparison is "data unavailable" when the preceding month precedes the first month with synced
data.

**Trend** (Overview only) = net purchases of the 12 Jalali months ending at the selected month. A month before the first
synced month is "data unavailable", not zero. A month with no purchases is zero.

## What these figures are not

- Purchases are **not payments**. Payments, cheques and vendor balances are a later phase (Phase 6) with their own
  definitions. Never present a purchase figure as a payment, a payable or a balance.
- A price difference (the difference between the purchase price and another price) is **not profit**. Gross margin and
  profit are not defined in Release 1.

## Range base (Phase 2)

`mabna.fn_purchase_lines(from_key, to_key, basis)` is now the single line-amount definition. It returns one row per
line of every non-cancelled purchase document whose Jalali day key (`yyyymmdd`) lies in the range. The document-discount
allocation is the same as in Phase 1 (per document, over all lines of that document). `mabna.fn_purchase_line_amounts`
is a thin wrapper over it, and every Phase 1 function (`fn_purchase_totals`, `fn_top_vendors`, `fn_top_products`,
`fn_purchase_trend`) reads that wrapper. A month is the day range `[yyyymm01, yyyymm31]`, so Phase 1 results are
identical. Any change to `fn_purchase_lines` is gated by the Phase 2 Flow P0: the Overview JSON for two months must be
identical before and after the change, and the Phase 1 reconciliation must still pass for every metric.

## Reports (Phase 2)

Scope: `specs/004-vendor-purchasing-reports` (FR-003, FR-012, FR-019, FR-020). The reports, the vendor directory and
profile, the vendor comparison and the transaction history use the definitions below. Every measure reads the range
base `mabna.fn_purchase_lines`, so a report total and an Overview figure for the same month are the same number.

**Filters and unknown buckets.** A vendor filter applies to the document header; product and category filters apply
to the line. A document with no vendor, a line with no product and a product with no category (or a category key
missing from the master) each form one "نامشخص" (unknown) bucket, with key `~unknown` in requests and in drill-down
links. Unknown buckets are counted in every total, so the buckets of any grouping add up to the same total.

**Period and previous period.** Periods are Jalali: a month, a quarter, a year or a custom range (inclusive, at most
1,098 days). A period whose `to` is later than today is trimmed to today, and the response says so (`trimmedTo`). The
previous period is the same kind of period immediately before: a month before, a quarter before, a year before, and
for a custom range the same number of days ending the day before `from`. Comparison is available only where the
catalog allows it (`compare=true`); it is "no previous data" when the previous period starts before the first
synced month.

| Measure | Definition | Phase 1 name |
|---|---|---|
| Net purchases | Σ `sign × line_amount` (invoices plus, negated, returns) | `net` |
| Gross purchases («خرید ناخالص») | Σ `line_amount` of invoice lines | `gross` |
| Returns («برگشت از خرید») | Σ `line_amount` of return lines, shown positive | `returns` |
| VAT | Σ `sign × vat_amount`; invoice and return VAT are also shown separately | `vat`, `vat_invoice`, `vat_return` |
| Net quantity | Σ `sign × coalesce(quantity, 0)`; `quantityMissingLines` counts lines with no recorded quantity | new |
| Invoice / return quantity | Σ quantity by document type | new |
| Document count | Distinct non-cancelled documents (invoices and returns) with at least one in-scope line | `document_count` (vendor filter only) |
| Pre-discount amount («مبلغ پیش از تخفیف») | Σ `gross_amount` of in-scope lines. Transaction history only. It is **not** «خرید ناخالص» | new |
| Discount («تخفیف») | Σ `discount_amount` of in-scope lines. Transaction history only | new |
| Last purchase date | Latest `doc_date` of the vendor's non-cancelled **invoices**, all time (not period-bound) | new |
| Comparison | `delta = net(P) − net(prev(P))`; `percent = delta ÷ net(prev(P))`, null when that base is 0 | Phase 1 rule, generalized to periods |
| Trend bucket | Net of the in-scope lines grouped by (`jalali_year`, `jalali_month`); a month before the first synced month is unavailable, not zero | `fn_purchase_trend` |

Search (`q`) restricts the lines that are summed, in every table and in every trend bucket: vendor name or code, product
name or category name, document number, depending on the report. Search text is normalized in SQL the same way as the
stored search keys (ی/ک, spaces, ZWNJ, digit forms), so it matches the same text in any spelling.

**Invariants** (checked by the quickstart flows; a failed invariant blocks the release):

- **I1.** For a month, the sum over vendor groups of net equals the sum over product groups, the sum over category
  groups and `fn_purchase_totals(month).net` (the Overview).
- **I2.** A vendor profile's net for a month equals that vendor's net in the Overview top five.
- **I3.** Every grouped row's net and quantity equal its drill-down totals; a returns row's returns equal the drill-down
  `returnAmount`.
- **I4.** For each document in a drill-down, Σ of the in-scope line amounts × sign equals the document's in-scope net.
- **I5.** For a document, pre-discount − discount equals Σ line amounts (the transaction history «خالص»).

**What these reports never show.** Payments, cheques, vendor balance, credit or debt, price differences and profit are
not part of any Phase 2 report, export or print. The vendor directory and profile DTOs have no such fields (FR-003,
FR-012). A purchase is a purchase: it is never presented as a payment, a payable or a margin.

## Reconciliation

For one Jalali month and one filter, `reconcile purchasing --month YYYY-MM [--vendor key | --product key]
[--basis line|lineAndDocument]` compares, per metric (net, gross, returns, VAT):

- the **system total**: `mabna.fn_purchase_totals` over the synced copy, and
- the **source total**: a fresh guarded read of the source, mapped by the same row mapper and summed by
  `SourceTotalsCalculator`, which shares no SQL with the system side.

Totals must be equal exactly. Any mismatch blocks sign-off (FR-018): the run exits 1, `reconciliation_run` gets a
`mismatch` row per metric, and `audit_log` gets a `reconciliation.mismatch` entry (period, filter, metric; amounts only
outside production). A run with no successful sync, an invalid source map or an unreachable source exits 2. A metric
whose source item is not verified is `unavailable`: it is listed but does not fail the run.
