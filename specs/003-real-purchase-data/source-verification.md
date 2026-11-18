# Source verification record: purchasing (Release 1, Phase 1)

**Feature**: specs/003-real-purchase-data | **Checklist**: `specs/002-mabna-accounting-connector/onsite-checklist.md` section 7 | **FR**: FR-002, spec 002 FR-02

This is the committed, logical-name record of what each purchasing source item has been verified as. It holds **no real object, column, or code names, no rows, and no amounts**. The real names and statuses live only in the gitignored on-site source map (`MabnaSync__SourceMapFile`), which is what the code enforces.

Status vocabulary: `unknown` (not checked) | `candidate` (an off-site candidate exists; unusable in production) | `confirmed` | `corrected`. Initial statuses below follow the status pattern of the committed example map (`deploy/mabna-sim/example/purchase-source-map.example.json`, made-up names). The off-site candidate map (gitignored) may differ; on-site verification replaces these values.

Verified date: Jalali `yyyy/mm/dd`, filled in when the item is checked on-site.

## Logical items

| # | Logical item | Status | Verified (Jalali) | Note |
|---|---|---|---|---|
| 1 | Vendor master: key | candidate | | |
| 2 | Vendor master: name | candidate | | |
| 3 | Vendor master: active marker | unknown | | No active marker assumed; active vendors come from the header vendor key |
| 4 | Vendor master: change marker | candidate | | Strategy `full` in the candidate map |
| 5 | Product master: key | candidate | | |
| 6 | Product master: name | candidate | | |
| 7 | Product master: change marker | candidate | | Strategy `full` in the candidate map |
| 8 | Purchase invoice header: key | candidate | | |
| 9 | Purchase invoice header: document number | candidate | | |
| 10 | Purchase invoice header: vendor key | candidate | | |
| 11 | Purchase invoice header: document date and format | candidate | | Format: Jalali text (to confirm) |
| 12 | Purchase invoice header: operation code (filter) | candidate | | Selects purchase invoices only |
| 13 | Purchase invoice header: cancelled rule | candidate | | Column and cancelled value |
| 14 | Purchase invoice header: document discount | unknown | | Needed only for basis `lineAndDocument` |
| 15 | Purchase invoice header: change marker | candidate | | Increasing key; header read in full each run (key-diff deletes) |
| 16 | Purchase return header: key | candidate | | |
| 17 | Purchase return header: document number | candidate | | |
| 18 | Purchase return header: vendor key | candidate | | |
| 19 | Purchase return header: document date and format | candidate | | Format: Jalali text (to confirm) |
| 20 | Purchase return header: operation code (filter) | candidate | | Selects purchase returns only |
| 21 | Purchase return header: cancelled rule | candidate | | Column and cancelled value |
| 22 | Purchase return header: document discount | unknown | | Needed only for basis `lineAndDocument` |
| 23 | Purchase return header: change marker | candidate | | Increasing key; header read in full each run (key-diff deletes) |
| 24 | Purchase line: key | candidate | | |
| 25 | Purchase line: document key (link to header) | candidate | | Must join each line to exactly one header type |
| 26 | Purchase line: product key | candidate | | Top products depend on this item (data-model.md, Metric availability) |
| 27 | Purchase line: quantity | candidate | | |
| 28 | Purchase line: gross amount | candidate | | |
| 29 | Purchase line: gross amount sign | candidate | | Sign convention to confirm against a report total |
| 30 | Purchase line: line discount | candidate | | |
| 31 | Purchase line: VAT | candidate | | |
| 32 | Purchase line: change marker | candidate | | Strategy `full` in the candidate map |
| 33 | Discount basis of Mabna's purchase report total | candidate | | Basis `line` in the candidate map; must match `Purchasing__DiscountBasis` |
| 34 | Amount unit | unknown | | Rial to be confirmed by the owner |
| 35 | Vendor master: table (object and entity status) | candidate | | Entity `vendor` in the candidate map; the on-site object name stays in the gitignored source map |
| 36 | Product master: table (object and entity status) | candidate | | Entity `product` in the candidate map; the on-site object name stays in the gitignored source map |
| 37 | Purchase invoice header: table (object, operation filter and entity status) | candidate | | Entity `purchaseInvoice` in the candidate map; filter and object verified together on-site |
| 38 | Purchase return header: table (object, operation filter and entity status) | candidate | | Entity `purchaseReturn` in the candidate map; must not share an object with the invoice header under identical filters (map rule 6) |
| 39 | Purchase line: table (object and entity status) | candidate | | Entity `purchaseLine` in the candidate map; read in full on every run |
| 40 | Vendor code shown to users (column) | candidate | | Phase 2 (spec 004). Optional: without it the vendor code and code search are unavailable; map field `vendor.code` |
| 41 | Product drug group key (column) | candidate | | Phase 2 (spec 004). Optional: without it the by-category report is unavailable; map field `product.categoryKey` |
| 42 | Drug group master: table (object and entity status) | candidate | | Phase 2 (spec 004). Entity `productCategory`; joins the live allowlist only once confirmed or corrected |
| 43 | Drug group master: key (column) | candidate | | Phase 2 (spec 004). Must match the product's drug group key exactly after normalization |
| 44 | Drug group master: name (column) | candidate | | Phase 2 (spec 004). Shown in reports and filters |
| 45 | Drug group master: change marker | candidate | | Phase 2 (spec 004). Strategy `full` in the candidate map; read in full with a key diff on every run |
| 46 | Drug class second level | unknown | | Phase 2 (spec 004). Not built in Phase 2; recorded only so that a later release can verify it |

## On-site reconciliation

Pass or fail only. No amounts, no rows, no real names.

| Month (Jalali label) | Outcome (pass / fail) | Recorded (Jalali) | Note |
|---|---|---|---|
| | | | |
