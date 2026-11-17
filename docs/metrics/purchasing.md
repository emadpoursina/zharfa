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
