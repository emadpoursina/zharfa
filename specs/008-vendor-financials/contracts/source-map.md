# Contract: Vendor-financial source map (Phase 6)

Additive to `specs/003-real-purchase-data/contracts/source-map.md`. Same file (`MabnaSync__SourceMapFile`), **version 1**. Purchasing required entities are unchanged. Phase 6 keys are **optional**: omit them and purchasing sync still runs; financial figures are unavailable.

Live maps stay gitignored. Tests use `deploy/mabna-sim/example/purchase-source-map.example.json` (`Sim*` names only).

## Additional top-level keys

```jsonc
{
  "balanceFormula": {
    "status": "candidate",                 // confirmed | corrected | candidate | unknown
    "mix": "openPayablesPlusUnpaidCheques" // candidate mix id; ignored in production until status confirmed/corrected
  },
  "partialPayment": { "status": "unknown" }
}
```

Unknown `mix` → map problem `balance-mix-unknown` (sync disabled) **only if** `balanceFormula` is present. Absent `balanceFormula` → balance unavailable, map still valid.

## Additional `entities`

Each follows the purchasing entity shape (`object`, `kind`, `hostFiltered: false`, `status`, `key`, `vendorKey`, `date`, `cancelled`, `change`, optional `filter`). Extra fields:

### `payableDocument`

- `remainingAmount` — `SourceMapAmountField` (`asIs | negate | abs`)
- `number` — document / reference column

Date = document date (as-of inclusion). Remaining is **signed**.

### `payableCheque`

- `amount`, `remainingAmount` — amount fields
- `instrumentType` — `{ "column", "status" }` (cheque type)
- `state` — `{ "column", "status" }`
- `clearedAt` — date field (`format` as other dates)
- `number` — cheque reference
- `filter` — restrict to payable types (simulator may Eq a `SimChqClass` / type list)

`hostFiltered: true` rejected. Type/state values used by a formula MUST have status recorded (FR-001).

### `vendorPayment`

- `amount` — paid portion
- `instrumentType`
- `number`
- `date` — payment or clear date
- `instrumentKey` — optional link to cheque/document

### `paymentAllocation`

- `instrumentKey`, `vendorKey`, `amount`
- Optional; if present and unusable, allocation-dependent figures unavailable

## Validation additions

1. Any listed Phase 6 `object` must pass `MabnaObjectPolicy` and sit on the connector allowlist.
2. `hostFiltered` true → rejected (never read `HOST_ID()` report views on live Mabna).
3. In **production**, sync **may** load `candidate` rows into PostgreSQL, but **reporting** treats `candidate`/`unknown` as unusable (`VendorFinancialAvailability`). Live allowlist still should hold only confirmed/corrected objects (FR-004); demo/simulator allowlist may include candidate `Sim*` objects.
4. Missing optional entity ≠ invalid map.
5. If `payableCheque` is mapped, `instrumentType` and `state` must be present (column or explicit unknown).

## Production vs demo

| Mode | Usable statuses | Balance numbers |
|---|---|---|
| demo / simulator | confirmed, corrected, **candidate** | candidate mix allowed |
| production | confirmed, corrected only | «داده در دسترس نیست» until accounting confirms `balanceFormula` |
