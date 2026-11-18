# Contract delta: Purchasing Source Map (Phase 2 additions)

Extends `specs/003-real-purchase-data/contracts/source-map.md`. The map stays gitignored (`MabnaSync__SourceMapFile`); only the made-up example `deploy/mabna-sim/example/purchase-source-map.example.json` is committed and gains the entries below with `Sim*` names. `version` stays `1`: every addition is optional, so a Phase 1 map loads unchanged and the new figures show "data unavailable" (`source-item-unmapped`).

## Additions

```jsonc
{
  "entities": {
    "vendor": {
      "...Phase 1 fields": "",
      "code": { "column": "SimVendorCode", "status": "candidate" }          // new, optional
    },
    "product": {
      "...Phase 1 fields": "",
      "categoryKey": { "column": "SimDrugGroupID", "status": "candidate" }  // new, optional
    },
    "productCategory": {                                                     // new, optional entity
      "object": "dbo.SimDrugGroup",
      "kind": "table",
      "hostFiltered": false,
      "status": "candidate",
      "key":  { "column": "DrugGroupID",   "status": "candidate" },
      "name": { "column": "DrugGroupName", "status": "candidate" },
      "change": { "strategy": "full" }
    }
  }
}
```

## Validation (added to the Phase 1 rules)

- `productCategory`, when present, is validated exactly like `vendor`/`product`: identifier policy, hard exclusions, **on the connector allowlist**, `hostFiltered` false (and explicit for views), `change.strategy` valid. Its `key` and `name` columns pass identifier rules.
- `vendor.code` and `product.categoryKey`, when present with a non-null column, pass identifier rules.
- `product.categoryKey` without a `productCategory` entity is **not** an error: the category figure is unavailable (`source-item-unmapped`).
- The sync reads `productCategory` in full with a key diff on every run (master rule of Phase 1, `change` ignored beyond shape validation), inside the run's single data transaction, and logs it under `table_logical = product_category`.

## Availability (enforced by `MetricAvailability`)

| Item | Needed by |
|---|---|
| `vendor.code` | `vendorCode` (directory code column, code search) |
| `product.categoryKey` + `productCategory` entity, `key`, `name` | `category` (by-category report, category filter/columns) |
| `purchaseLine.quantity` (Phase 1 item) | `quantity` columns |

Usable = `confirmed`/`corrected`; `candidate` is also usable in demo mode only (Phase 1 R2).

## Allowlist

The simulator allowlist (`deploy/mabna-sim/generated/allowlist.txt`, gitignored) and the example allowlist (`deploy/mabna-sim/example/purchase-allowlist.example.txt`) gain the drug group object. On-site, the real object is added to the live allowlist only once `confirmed`/`corrected`.
