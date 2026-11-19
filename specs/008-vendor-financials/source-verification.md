# Source verification record: vendor financials (Release 1, Phase 6)

**Feature**: specs/008-vendor-financials | **Checklist**: `specs/002-mabna-accounting-connector/onsite-checklist.md` §7.8 | **FR**: FR-002, FR-003

This is the committed, logical-status record of the Phase 6 payables source items. It is a **logical-status log
only**: it holds **no live Mabna table or column names, no business rows, and no amounts**. The real names and
statuses live only in the gitignored on-site source map (`MabnaSync__SourceMapFile`), which is what the code
enforces. (Purchasing items are recorded separately in `specs/003-real-purchase-data/source-verification.md`,
which this file does not edit.)

Status vocabulary: `unknown` (not checked) | `candidate` (an off-site candidate exists; unusable in production) |
`confirmed` (verified on-site against the accounting owner) | `corrected` (verified and corrected). Initial
statuses follow the status pattern of the committed example map
(`deploy/mabna-sim/example/purchase-source-map.example.json`, made-up `Sim*` names).

Verified date: Jalali `yyyy/mm/dd`, filled in when the item is checked on-site.

## Logical items

| # | Logical item | Status | Verified (Jalali) | Note |
|---|---|---|---|---|
| 1 | Payable document: key | candidate | | |
| 2 | Payable document: vendor key | candidate | | |
| 3 | Payable document: document date and format | candidate | | As-of inclusion (`doc_date ≤ period end`) |
| 4 | Payable document: signed remaining amount | candidate | | Negative = credit note; sign convention to confirm |
| 5 | Payable document: reference number | candidate | | |
| 6 | Payable document: cancelled rule | candidate | | |
| 7 | Payable document: change marker | candidate | | Strategy `full` in the candidate map |
| 8 | Payable cheque: key | candidate | | |
| 9 | Payable cheque: vendor key | candidate | | |
| 10 | Payable cheque: issue date and format | candidate | | |
| 11 | Payable cheque: amount | candidate | | Face amount, non-negative |
| 12 | Payable cheque: remaining amount | candidate | | Unpaid remainder, non-negative |
| 13 | Payable cheque: instrument type (column + status) | candidate | | Types used by the balance/outstanding formulas must be recorded (FR-001) |
| 14 | Payable cheque: state (column + status) | candidate | | open / paid / cancelled states |
| 15 | Payable cheque: clear date | candidate | | Fully cleared on or before as-of → excluded from outstanding |
| 16 | Payable cheque: reference (cheque number) | candidate | | Never leaves the site; never in audit detail or cloud payloads |
| 17 | Payable cheque: cancelled rule | candidate | | |
| 18 | Payable cheque: change marker | candidate | | |
| 19 | Vendor payment: key | candidate | | |
| 20 | Vendor payment: vendor key | candidate | | |
| 21 | Vendor payment: payment/clear date and format | candidate | | History filter (`paid_at` in period) |
| 22 | Vendor payment: amount (paid portion) | candidate | | Paid portion only — never the unpaid remainder |
| 23 | Vendor payment: instrument type | candidate | | Every type the payments formula includes must be verified |
| 24 | Vendor payment: reference | candidate | | |
| 25 | Vendor payment: instrument link key | candidate | | Optional link to the cheque/document |
| 26 | Vendor payment: cancelled rule | candidate | | |
| 27 | Vendor payment: change marker | candidate | | |
| 28 | Payment allocation: instrument key | unknown | | Optional; allocation-dependent figures unavailable while unknown |
| 29 | Payment allocation: vendor key | unknown | | |
| 30 | Payment allocation: amount | unknown | | |
| 31 | **Balance formula (mix)** | **candidate** | | Candidate mix `openPayablesPlusUnpaidCheques`; production unusable until `confirmed`/`corrected` by the accounting owner |
| 32 | Partial-payment split rule | unknown | | `partialPayment.status`; while not usable, split-dependent figures are unavailable |
| 33 | Amount unit | unknown | | Rial to be confirmed by the owner |

## Commands

```bash
# Off-site (simulator, candidate mix): must pass before sign-off of the Phase 6 numbers
docker compose -f deploy/docker-compose.yml run --rm api reconcile vendor-balance --month <seeded month> [--vendor <key>] [--show-amounts]

# On-site (one real month; the live map with its confirmed/corrected statuses)
docker compose -f deploy/docker-compose.yml run --rm api reconcile vendor-balance --month <month> [--vendor <key>]
```

Exit 0 = pass or *unavailable* (an unusable formula is listed with its reason, never counted as a pass),
exit 1 = mismatch (`signOffBlocked: true`), exit 2 = error. On-site: **only the pass/fail outcome may be
copied into the table below** — never `system`, `source` or `difference`, and no amounts anywhere.

## On-site pass/fail

Leave blank until the on-site run (US4 / Phase 7 on-site). Only pass/fail may be copied here — never real amounts.

| Check | Result | Date (Jalali) |
|---|---|---|
| `reconcile vendor-balance` on one real month (simulator candidate mix passes off-site; production pass needed once the formula is confirmed) | | |
| Vendor balance totals equal the source query (signed, including credit vendors) | | |
