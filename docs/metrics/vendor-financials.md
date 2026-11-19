# Vendor financial metrics: definitions

**Scope**: Release 1, Phase 6 (`zharfa-prd.md` v3.0, F17; `specs/008-vendor-financials`, FR-007–FR-012).
**Single source**: each metric below has exactly one definition here. The SQL in `db/migrations/0011_vendor_financials.sql`
(`mabna.fn_vendor_balance`, `mabna.fn_outstanding_payments`, `mabna.fn_vendor_payments`) and the C# recomputation in
`backend/Zharfa.Api/VendorFinancials/VendorFinancialTotals.cs` implement it. The reconciliation
(`reconcile vendor-balance`) compares the two; a change to a definition changes both and is recorded here.

**Purchases are not redefined here.** Purchases (net purchases) come from `docs/metrics/purchasing.md`
(Phase 1 net purchases, by document date, on the selected discount basis). Every Phase 6 consumer cites that
definition unchanged; this file only points at it.

**Consumers**: the Financials list and detail (`VendorFinancialsService`), the vendor-profile balance block,
the Excel exports, saved reports, the AI tools (`VendorFinancialAnalystTools`), and
`reconcile vendor-balance`.

## Conventions

- **Units**: Rial (ریال), stored as `numeric(24,4)`; JSON as decimal strings with 4 places; display in Persian digits.
- **Period**: a Jalali period (month / quarter / year / custom range), same `JalaliPeriod` as Phase 2.
- **As-of date**: the **last day of the selected period** (month/quarter/year preset or the `to` of a custom range).
  Balance and outstanding are evaluated *as of* that date; payments are evaluated *inside* the period.
- **Signed balance**: positive = the company owes the vendor; negative = credit in the vendor's favour. Never floored
  at zero. Never called profit.
- **Cancelled rows**: excluded from every figure (the mapped on-site cancelled rule).
- **Unverified sources**: production reporting treats `candidate` and `unknown` as unusable
  (`formula-unconfirmed` / `source-item-unverified` → «داده در دسترس نیست»); demo/simulator installs may use
  `candidate`. Zero qualifying rows with a **usable** formula is `"0.0000"`, never "data unavailable".

## Figures

**Payments** (Persian label: پرداخت‌ها; key `payments`)
= Σ `vendor_payment.amount` where `paid_at` (payment date, or the clear date of a cleared instrument) falls **inside
the selected period** and the row is not cancelled. Only the **paid portion** counts: a partially paid instrument
contributes its paid portion here and its unpaid remainder to outstanding payments — never both in full. Every
instrument type whose mapped verification status is usable is included; if the map leaves a type the formula needs
unverified, payments are "data unavailable".

**Outstanding payments** (پرداخت‌های معوق; key `outstandingPayments`)
= Σ unpaid remainders of instruments still **open as of the period end date**: non-cancelled instruments issued on or
before as-of whose remaining amount is still unpaid at as-of (a cheque fully cleared on or before as-of is excluded).
Always **non-negative**. Payment-history rows are never included. This is a stock (as-of) figure, not a flow.

**Vendor balance** (مانده تأمین‌کننده; key `vendorBalance`)
= Σ **signed** `payable_document.remaining_amount` of non-cancelled documents with `doc_date ≤ as-of` **plus**
Σ `payable_cheque.remaining_amount` of non-cancelled cheques with `issue_date ≤ as-of` that are still open as of
as-of (`openPayablesPlusUnpaidCheques`). Document remainders may be negative (credit notes), so the total may be
negative. This is the **candidate mix**: off-site demo/simulator only, until accounting confirms or corrects
`balanceFormula.status` in the source map on-site — production never evaluates it while the status is
`candidate`/`unknown` (`formula-unconfirmed`).

**Net purchases** (خالص خرید; key `netPurchases`)
= pointer to `docs/metrics/purchasing.md` → **Net purchases** for the same vendor and period. Not redefined here.

**Purchases minus payments** (خرید منهای پرداخت; key `purchasesMinusPayments`)
= `netPurchases − payments`, both computed for the same period. A simple documented difference of two separate
figures. It is **not** profit, margin or cash received, and it is never labeled سود / حاشیه / وجه نقد دریافت‌شده /
مانده. It exists only when both sides are available.

## Reconciliation

`reconcile vendor-balance --month YYYY-MM [--vendor key] [--show-amounts]` compares the stored
`mabna.fn_vendor_balance(as_of, vendor)` total with the same candidate-mix formula recomputed from a guarded source
re-read (`VendorFinancialTotals`), metric id `vendor_balance`. Equal signed totals pass; any difference is a mismatch
(`system − source`) that blocks sign-off. An unusable formula is `unavailable` and does not fail the run — it is
**not** a sign-off pass.
