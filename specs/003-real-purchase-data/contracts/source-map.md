# Contract: Purchasing Source Map (gitignored JSON)

The source map tells the sync which real Mabna tables, columns and codes hold each logical purchasing item, and how far each item is verified. It is **never committed** (spec 002 FR-02): the live file is supplied on the server via `MabnaSync__SourceMapFile` (Docker secret or a mounted, gitignored path); the simulator's file lives in `deploy/mabna-sim/generated/purchase-source-map.json` (gitignored). The repository holds only this contract and `deploy/mabna-sim/example/purchase-source-map.example.json` (made-up names, used by unit tests).

## Shape

```jsonc
{
  "version": 1,
  "verifiedOn": "1405-08-30",            // Jalali date of the last on-site verification
  "discountBasis": { "value": "line", "status": "candidate" },   // line | lineAndDocument
  "entities": {
    "vendor": {
      "object": "dbo.SimVendor",          // must be on the connector allowlist
      "kind": "table",                    // table | view
      "hostFiltered": false,              // MUST be false; true is rejected (HOST_ID() report views)
      "status": "candidate",              // confirmed | corrected | candidate | unknown
      "key":   { "column": "VendorID",  "status": "candidate" },
      "name":  { "column": "VendorName","status": "candidate" },
      "active":{ "column": null,        "status": "unknown" },
      "change": { "strategy": "full" }
    },
    "product": { "...same pattern...": "" },
    "purchaseInvoice": {
      "object": "dbo.SimInvDoc", "kind": "table", "hostFiltered": false, "status": "candidate",
      "filter": [ { "column": "SimOpCode", "op": "Eq", "value": 11, "status": "candidate" } ],
      "key":      { "column": "DocID",   "status": "candidate" },
      "number":   { "column": "SimDocNumber",   "status": "candidate" },
      "vendorKey":{ "column": "VendorID","status": "candidate" },
      "date":     { "column": "SimDocDate", "format": "jalaliText", "status": "candidate" },
      "docDiscount": { "column": null, "status": "unknown" },
      "cancelled":{ "rule": "column", "column": "IsCanceled", "value": 1, "status": "candidate" },
      "change":   { "strategy": "idWatermark", "column": "DocID" }
    },
    "purchaseReturn": { "...same as purchaseInvoice with its own filter/codes...": "" },
    "purchaseLine": {
      "object": "dbo.SimInvDocItem", "kind": "table", "hostFiltered": false, "status": "candidate",
      "key":        { "column": "ItemID", "status": "candidate" },
      "documentKey":{ "column": "DocID",  "status": "candidate" },
      "productKey": { "column": "PrdID",  "status": "candidate" },
      "quantity":   { "column": "SimQuantity",    "status": "candidate" },
      "grossAmount":{ "column": "SimLineAmount", "amountSign": "abs", "status": "candidate" },
      "lineDiscount":{ "column": "Disc",  "status": "candidate" },
      "vat":        { "column": "Vat",    "status": "candidate" }
    }
  }
}
```

## Validation (startup and before every run)

The map is rejected (sync disabled, reason shown on the Data sync page and `POST /api/admin/sync/run` → 503 `source-map-invalid`) when any of these hold:

1. Unknown `version`, malformed JSON, or a missing required entity (`purchaseInvoice`, `purchaseLine`).
2. An `object` or `column` fails `MabnaObjectPolicy` identifier rules, or an `object` is hard-excluded (`scuUser`, `UserRights`, `users`, `scu*`, `msg*`) or **not on the connector allowlist**.
3. `hostFiltered` is `true` or missing for a `view` (Mabna's `HOST_ID()`-filtered report views are never read on live Mabna; FR-005).
4. `filter[].op` is not a `MabnaFilterOp` name; `filter[].value` is not a number or string.
5. `date.format` not in `jalaliText | jalaliInt | gregorian`; `change.strategy` not in `rowversion | modifiedAt | idWatermark | full`; `amountSign` not in `asIs | negate | abs`.
6. `purchaseLine.documentKey` missing, or the invoice and return entities' line linkage is ambiguous.

Items with status `unknown`, or a `column: null`, are **not** errors: they make dependent metrics unavailable (see [data-model.md § Metric availability](../data-model.md#metric-availability)). In production mode `candidate` counts as unusable (research R2).

## Rules

- Only `confirmed`/`corrected` objects are to be added to the live allowlist (FR-005); the allowlist file and this map are reviewed together on-site.
- The map is read through the connector only; it never makes the code accept raw SQL. Filters become `MabnaFilter` parameters.
- Changing the map changes behaviour without a code release; after any change rerun the reconciliation (quickstart Flow 7).
- Change markers (`change`) decide how a table is read, not which rows exist at the source. Header tables (`purchaseInvoice`, `purchaseReturn`): an `idWatermark` header is read in full on every run, and a `rowversion` or `modifiedAt` header reads its changed rows plus a key-only pass over every source key; in both cases a stored header whose key is missing from the source is deleted, with its lines, on that run.
- `change.lookbackDays` is retired. A map that still carries it loads and validates unchanged, and the value is ignored; there is no re-read window.
- Masters (`vendor`, `product`) are read in full with a key diff on every run, whatever `change` says. The marker is ignored (research R5, Phase 14 T105): a renamed or removed master reaches the copy on the nightly run, and a master needs no `change.column`. The `change` block is still validated for its shape.
