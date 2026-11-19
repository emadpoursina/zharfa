# Contract: Vendor-financial AI tools (Phase 6)

Does **not** replace `specs/006-local-ai-purchasing/contracts/tools.md`. Phase 4 purchasing allowlist, 20-question fixture, timeouts, and row cap stay. This file is additive.

## Scope `vendor-financials`

`PurchasingQuestionRouter` still emits `scope=vendor-financials` for مانده / بدهی / پرداخت / چک / اسناد پرداختنی (and obvious synonyms already in the router).

| Caller | Tools | User-visible |
|---|---|---|
| no `ai.query` (Admin) | n/a | 403 on `POST /api/ai/chat` |
| `ai.query` but **no** `vendor.financials` (Purchasing) | **none** | 200 «داده در دسترس نیست» / cannot see; **no digits** in facts or answer |
| `ai.query` **and** `vendor.financials` (Management, Finance) | mapped financial tool | cited answer from tools, or «داده در دسترس نیست» |

Purchasing fixtures 15–16 remain no-financials. Do not edit that JSON.

## Allowlist (`VendorFinancialAnalystTools`)

Same limits as purchasing tools: 10 s per call, 100 rows, 3 calls, 30 s answer, read synced copy only, no SQL, no Mabna, no writes.

| Tool | Returns | Visual-capable |
|---|---|---|
| `vendor_financials_list` | per-vendor `vendorBalance`, `outstandingPayments` | yes (ranking / comparison) |
| `vendor_balance` | one vendor signed balance | no |
| `vendor_payments` | payments total for vendor/period | no |
| `vendor_outstanding` | outstanding total | no |
| `vendor_purchases_vs_payments` | net purchases, payments, optional `purchasesMinusPayments` | no (two metrics, not a ranking list) |

Period/filter resolution: same `JalaliPeriod` + vendor name lookup as purchasing tools. Omit period → current Jalali month. `totalRows > 100` on a list tool → metric unavailable, no truncated guess.

Facts cite `period`, filters, and metric id (`vendorBalance` / `payments` / `outstandingPayments` / `netPurchases`). Labels follow [data-model.md](../data-model.md) §8 / `docs/metrics/vendor-financials.md`. Never call purchases payments or a difference profit.

Cloud JSON: names + aggregate amounts only. **Forbidden:** payment history rows, cheque numbers, document lines.

## Visual

Phase 5 `VisualIntent` (or a sibling consulted from chat) treats `vendor_financials_list` as visual-capable when the question has ranking/comparison tokens. Table/chart captions use financial metric labels. Chart only for 2–20 vendors. Purchasing visual rules otherwise unchanged.

## Additional fixture

`specs/008-vendor-financials/fixtures/financial-questions.json` — Management/Finance questions only. Scorer: cited amounts match Financials GET, or unavailable phrase, or no-financials for a Purchasing control row. **Not** a 20/20 Phase 6 exit.
