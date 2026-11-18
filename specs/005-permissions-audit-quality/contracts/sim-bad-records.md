# Contract: Simulator bad records for data quality (FR-030, SC-006)

Tool: `deploy/mabna-sim/dq_bad_records.py` (tracked, Python 3 standard library only, generic — it contains no table or column name). Output: gitignored `deploy/mabna-sim/generated/dq/dq-apply.sql` and `dq-revert.sql`. The init container never runs files in subdirectories of `generated/`, so these are applied by hand (quickstart §0). Synthetic data only; the scripts run against the simulator as `sa`, never against any company server, and Zharfa itself never runs them.

## CLI

```text
python3 deploy/mabna-sim/dq_bad_records.py \
  --map deploy/mabna-sim/generated/purchase-source-map.json \   # or example/purchase-source-map.example.json
  --date 1405/07/15 \                                           # Jalali date for the bad documents (test month M)
  [--out deploy/mabna-sim/generated/dq] [--database TotalSystem]
```

- Reads from the map only: `entities.vendor|product|productCategory|purchaseInvoice|purchaseReturn|purchaseLine` → `object`, `key.column`, `name.column`, `categoryKey.column`, `number.column`, `vendorKey.column`, `date.column` + `date.format`, `cancelled.column` + `cancelled.value`, `docDiscount.column`, `filter[]`, `documentKey.column`, `productKey.column`, `quantity.column`, `grossAmount.column`, `lineDiscount.column`, `vat.column`. An entity item with `column: null` disables the records that need it (listed as skipped in the manifest).
- `--date` is written in the map's date format: `jalaliText` → `'1405/07/15'`; `jalaliInt` → `14050715`; `gregorian` → the converted Gregorian date `'yyyy-mm-dd'` (the tool carries its own Jalali↔Gregorian conversion, same algorithm as `frontend/src/utils/jalali.ts`).
- Exit code 2 with a message if a required item (document key/date, line key/document key/gross) is missing from the map.

## `dq-apply.sql` behavior

1. `USE <database>; SET XACT_ABORT ON; BEGIN TRAN;`
2. Creates (if missing) `dbo.SimDqSeed (seq int IDENTITY, object sysname, key_value nvarchar(100), kind nvarchar(20), issue_type nvarchar(40), note nvarchar(200))`. If it already has rows, the script stops with `RAISERROR('dq records already applied; run dq-revert.sql first')` (idempotence).
3. Defines a temp procedure `#dq_clone @object, @keyColumn, @templateKey, @newKey` that copies one row: column list from `sys.columns` excluding computed and `rowversion` columns; when the key column is an identity column it wraps the insert in `SET IDENTITY_INSERT <object> ON/OFF`; the key column is set to `@newKey`, every other column copied from the template row. Dynamic SQL uses `QUOTENAME` for every identifier.
4. Disables FK and CHECK constraints on the invoice/return/line objects (`ALTER TABLE … NOCHECK CONSTRAINT ALL`).
5. Picks templates at run time:
   - `@tInv` = smallest invoice key whose row matches the map's `filter` and is not cancelled and that has at least one line; `@tRet` the same for returns; `@tLine(doc)` = smallest line key of a document.
   - `@tProd` = the product key of `@tLine(@tInv)`; `@qProd` = the smallest product key in the product master different from `@tProd` (product for the quantity records, research R17 isolation).
   - `@kDoc` = `MAX(key) + 100000` over invoice/return keys; `@kLine` likewise for lines; `@kProd` for products; `@nNum` = `MAX(TRY_CAST(number AS bigint)) + 100000` over invoice numbers (or 900000 when none is numeric). New documents get `number = @nNum + i` unless the record says otherwise, so they never collide with real numbers.
   - `@noVendor` = `MAX(TRY_CAST(vendor key AS bigint)) + 100000`; `@noProduct` likewise for products; `@noCategory` = `MAX(TRY_CAST(category key AS bigint)) + 100000` over the category master (a category key that is not in the master).
6. Inserts the records below (each document is a clone of `@tInv` or `@tRet` with key `@kDoc + i`, number `@nNum + i` and date `--date`; its one line is a clone of the template document's first line with key `@kLine + i` and the line's `documentKey` column set to the new document key), then applies the listed column updates, and logs every inserted key in `dbo.SimDqSeed`. When invoices and returns live in the same object (same `object` in the map), one key sequence is used for both.
7. Re-enables constraints `WITH NOCHECK CHECK CONSTRAINT ALL`; `COMMIT`.
8. Prints the manifest: `SELECT issue_type, COUNT(*) AS seeded FROM dbo.SimDqSeed WHERE kind = 'flagged' GROUP BY issue_type ORDER BY issue_type;`

A record whose column is not mapped, or whose column is NOT NULL when the record needs NULL (checked at run time via `sys.columns.is_nullable`), is skipped and logged with `kind='skipped'`; the manifest therefore always equals what was really seeded.

## Records (`kind='flagged'` rows are the expected counts; supporting rows are `kind='support'`)

| # | Issue type | Document (clone of) | Line | Changes after cloning | Flagged record |
|---|---|---|---|---|---|
| 1 | missing-values | invoice | 1 line | number = NULL | document (number) |
| 2 | missing-values | invoice | 1 line | vendor key = NULL | document (vendor) |
| 3 | missing-values | invoice | 1 line | line product key = NULL | line (product) |
| 4 | missing-values | invoice | 1 line | line quantity = NULL | line (quantity) |
| 5 | duplicate-records | invoice | 1 line | number = the template invoice's number | document (number) |
| 6 | broken-relationships | invoice | 1 line | vendor key = `@noVendor` | document (vendor) |
| 7 | broken-relationships | invoice | 1 line | line product key = `@noProduct` | line (product) |
| 8 | inconsistent-product-ids | – | – | new product row cloned from `@tProd` with key `@kProd` (same name) + a support invoice (record 8s) whose line uses `@kProd` | product |
| 9 | invalid-dates | invoice | 1 line | date = `1499/01/01` (future) | document (date) |
| 10 | invalid-dates | invoice | 1 line | date = `1350/01/01` (before default earliest 1390-01-01) | document (date) |
| 11 | unexpected-quantities | invoice | 1 line | line product = `@qProd`, quantity = 0 | line (quantity) |
| 12 | unexpected-quantities | return | 1 line | line product = `@qProd`, quantity = -3 | line (quantity) |
| 13 | inconsistent-amounts | invoice | 1 line | line discount = gross + 1 (only when line discount is mapped) | line (discount) |
| 14 | inconsistent-amounts | invoice | 1 line | line VAT = -500 (only when VAT is mapped) | line (vat) |
| 15 | broken-relationships | – | – | new product row cloned from `@tProd` with key `@kProd + 1`, name = `SimDqProduct-BR3` (a distinct name, so record 8's duplicate-name rule does not also flag it) and category key = `@noCategory` (only when the product's `categoryKey` and the `productCategory` master are mapped) + a support invoice (record 15s) whose line uses `@kProd + 1` | product (category) — rule BR3 |
| 16 | inconsistent-amounts | invoice | 1 line | document discount (`docDiscount`) = the cloned line's (gross − line discount) + 1000 (only when `docDiscount` is mapped; when the line discount column is not mapped use gross + 1000) | document (document-discount) — rule AM3 |

Expected seeded counts with the example map (all items mapped, nullable columns): missing-values 4, duplicate-records 1, broken-relationships 3, inconsistent-product-ids 1, invalid-dates 2, unexpected-quantities 2, inconsistent-amounts 3. Every data-model §3.2 rule has at least one seeded record (MV1–MV4: 1–4; DU1: 5; BR1: 6; BR2: 7; BR3: 15; PI1: 8; DT1: 9; DT2: 10; QT1: 11–12; AM1: 13; AM2: 14; AM3: 16).

Notes for the tester:

- Every flagged document except 9–10 is dated `--date` in month M and belongs to the template vendor (except 2 and 6), so the notice shows on month-M reports; 9–10 are outside every report period (no notice expected from them).
- Record 5 keeps the template invoice's vendor, so it is a duplicate under the type + number + vendor rule (DU1).
- Record 15's support invoice gives the product with the unknown category a line in month M, so `by-category` for month M shows `broken-relationships` (its amount falls into the «نامشخص» category row).
- Record 16's line is internally consistent (only the document discount is wrong), so it raises only `inconsistent-amounts` (AM3).
- Records 11–12 use `@qProd` only, so a trend report filtered to `@qProd` in month M sees only `unexpected-quantities` (not mapped to `trend`) → no notice, while `by-vendor` with the same product filter shows the notice (US5 AC6).
- Record 8 makes the product master contain two products with the same normalized name; the support invoice gives the duplicate product a line in month M so a `by-product` report shows the notice.
- The clones keep the template's other column values (filter column such as `SimOpCode`, non-cancelled flag), so the sync reads them as purchase documents.

## `dq-revert.sql` behavior

`BEGIN TRAN`; for each `dbo.SimDqSeed` row in reverse `seq` order delete the row by key from its object (lines first, then documents, then products — records 8 and 15); `DROP TABLE dbo.SimDqSeed`; `COMMIT`. After the next sync, Data quality returns to the baseline counts (US4 AC6).

## Guard rails

- The scripts reference only objects named in the map; no `EXEC` of simulator procedures; no change to logins or permissions (`zharfa_ro` stays read-only).
- They are never committed (output directory is gitignored) and never mounted into the Zharfa API.
