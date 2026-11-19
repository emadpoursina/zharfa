# Research: Vendor Financials (Release 1, Phase 6)

**Feature**: `specs/008-vendor-financials` | **Date**: 2026-11-19 | **Plan**: [plan.md](plan.md) | **Spec**: [spec.md](spec.md)

Skip mode: no questions were asked. Spec Session 2026-11-19 and the skip-mode assumptions are binding. Phases 1–5 specs are **not** edited. This document only fills the reserved financial hole on existing Phase 2–5 surfaces.

Code facts this research relies on (worktree, 2026-11-19):

- `VendorFinancialsController` (`GET /api/vendor-financials/summary`, `GET …/vendors/{vendorKey}`) is `[Authorize(Policy = vendor.financials)]`, returns `{ available: false, reason: "not-available-until-phase-6" }` with no query (`VendorFinancialsTests`).
- `RolePermissions`: Management and Finance hold `vendor.financials`; Admin and Purchasing do not. `PermissionMatrixTests` already lists the two reserved actions.
- `NoVendorFinancialsTests` forbids `balance|payment|debt|credit|paid` on any type in `Zharfa.Api.Purchasing.Reports`. `NoVendorFinancialsInToolsTests` forbids those tokens (plus `cheque`) on `PurchasingAnalystTools` and Phase 5 visual/saved/export DTO **type names**.
- `PurchasingQuestionRouter` maps مانده / بدهی / پرداخت / چک / اسناد پرداختنی → `scope=vendor-financials` with **no tool** for every role (`PurchasingQuestionRouterTests`).
- Sync is purchase-shaped (`SyncRunner` + `PurchaseMapping` + `SourceMap` version 1). Optional `productCategory` is the pattern for extra entities. `LogicalTables.SyncOrder` is vendor → product → invoice → return → lines.
- Reconciliation is `dotnet run -- reconcile purchasing` (`PurchasingReconciliation` / `ReconcileCli`); `reconciliation_run.metric` CHECK is `net|gross|returns|vat` only.
- Saved reports: `chk_saved_report_kind` / `chk_saved_report_permission` (`purchasing.reports` | `ai.query`). Class policy on `SavedReportsController` is `purchasing.reports`; generated kinds add `ai.query` at runtime.
- `SystemModules` has no vendor-financials flag. SPA has no Financials tab (`App.tsx`).
- `docs/metrics/purchasing.md` is the single purchase-formula source. Excel is ClosedXML via `ExcelExportService` (no new NuGet).
- On-site checklist purchasing slice ends at §7.7 (Phase 3). PRD still lists the live balance mix as an open on-site question.

---

## R1. Candidate source mix until accounting confirms (FR-001, FR-011)

**Decision.** One source map file (`MabnaSync__SourceMapFile`, version still **1**). Optional entities (absent → financial figures «داده در دسترس نیست», purchasing sync unchanged):

| Entity id | Simulator object (example only) | Role |
|---|---|---|
| `payableDocument` | `dbo.SimPayAccDoc` | Open vendor payables / credit notes (signed remaining) |
| `payableCheque` | `dbo.SimPayChq` | Payable cheques (type + state) |
| `vendorPayment` | `dbo.SimVendorPay` | Completed payments (cleared cheque, cash, bank transfer, …) |
| `paymentAllocation` | `dbo.SimPayAlloc` | Optional split of one instrument across vendors |

**Candidate balance formula (simulator / demo only):** for vendor *v* at as-of date *T* (end of selected Jalali period):

`balance(v, T) = Σ remaining(payable documents of v with doc_date ≤ T, not cancelled) + Σ remaining(payable cheques of v with issue_date ≤ T, not cancelled, not fully paid/cleared by T)`

Remaining on a document may be negative (credit). Cancelled and already-paid instruments are excluded. This mix is **never** used in production until the map records the balance formula as `confirmed` or `corrected`.

**Production gate.** `VendorFinancialAvailability` mirrors `MetricAvailability`: in production, `candidate` and `unknown` are unusable. Unusable balance source → balance «داده در دسترس نیست». Same for payments / outstanding when their items are unusable. Zero activity with a **usable** formula is **0**, not unavailable.

**Rationale.** Spec skip-mode mix; reports must not guess Mabna. Optional entities follow `productCategory`. One map avoids a second secret file.

**Alternatives.** Second `MabnaSync__FinancialMapFile` (two allowlist reviews, easy to drift). Hard-coded live table names (forbidden). Using Mabna `HOST_ID()` report views (forbidden).

---

## R2. Period semantics (FR-010, FR-011, FR-018, FR-032)

**Decision.** Same `JalaliPeriod` as Phase 2.

| Figure | Period rule |
|---|---|
| Vendor balance | Signed amount **as of period end** (last Jalali day of a month preset; `to` of a custom range; leap Esfand last day) |
| Outstanding payments / instruments | Unpaid remainder **still open as of that same as-of date** |
| Payment history / payments total | Completed (or confirmed paid portion) whose **payment or clear date** falls **in** the period |
| Purchases in purchases-vs-payments | Phase 1 **net purchases**, document date in the period (`docs/metrics/purchasing.md`) |

Comparison toggle: **on** Financials list (balance + outstanding) and purchases vs payments (previous Jalali unit / equal-day custom). **Off** on payment history and outstanding-instruments; server **400** if `compare=1` is sent to those document lists (same exclusion as purchase transaction history).

**Rationale.** Spec Session 2026-11-19. Purchase vs payment dates may differ; that is not an error.

**Alternatives.** Balance as-of last successful sync (rejected by skip-mode). Applying the period filter to balance as a flow in the month (would not match “what we owed at month end”).

---

## R3. Signed credit and outstanding non-negative (Session Q1)

**Decision.** Balance is a signed `numeric(24,4)` string: positive = company owes the vendor; negative = credit in the vendor’s favour. Never floored at zero. Label stays «مانده تأمین‌کننده», never سود. Outstanding payments stay a **non-negative** unpaid-instrument total. List, profile, export, AI, reconciliation all use the signed balance.

**Rationale.** Skip-mode recommended option. Reconciliation would lie if credits were clamped.

**Alternatives.** Absolute value + a “credit” flag (two fields, easy to mis-label as profit). Floor at zero (hides overpayment).

---

## R4. History vs outstanding vs partial pay (FR-009, FR-010, FR-033)

**Decision.** One instrument must not appear as both a full history amount and a full outstanding amount.

- **History / payments:** paid portion whose payment/clear date is in the period.
- **Outstanding:** remaining unpaid as of period end, listed on the outstanding table (Jalali date, type/reference, unpaid amount). Rows sum to the list’s outstanding column.
- **Cancelled / voided:** excluded from all three formulas, following the on-site cancelled rule.
- **Uncleared payable cheque:** outstanding (and in balance only if the confirmed/candidate mix includes it); not in history.
- **Partial-payment rule:** if the map’s `partialPayment` item is not `confirmed`/`corrected` (production) or `candidate` (demo), figures that would need a split are «داده در دسترس نیست». Simulator seeds instruments that are fully open or fully cleared so demo Financials can show numbers without a guessed split; one seeded partial row exists only for unit tests of the split once the candidate rule is documented.

**Rationale.** Spec Session Q2/Q4/Q5. Avoids double-count. Unavailable beats a wrong split.

**Alternatives.** Treat any uncleared cheque as fully outstanding even when a payment row exists (double-count). Guess a 50/50 split (forbidden).

---

## R5. Keep purchasing DTOs clean; fill the reserved API (F5 AC3)

**Decision.** Do **not** add `balance` (or any financial property) to `VendorProfileResponse` or any type in `Zharfa.Api.Purchasing.Reports`. `NoVendorFinancialsTests` stays. New types live in `Zharfa.Api.VendorFinancials`.

F5 AC3: the SPA, when `modules.vendorFinancials` is true, also calls `GET /api/vendor-financials/vendors/{vendorKey}` and renders a **balance block** on the profile. Purchasing never makes that call; a direct request is still 403. Purchasing tables on the profile are unchanged.

Keep Phase 3 routes and add list query params + paged children:

| Method | Path | Role |
|---|---|---|
| GET | `/api/vendor-financials/summary` | Financials list (was reserved stub) |
| GET | `/api/vendor-financials/vendors/{vendorKey}` | Detail header: signed balance, outstanding total, purchases vs payments |
| GET | `/api/vendor-financials/vendors/{vendorKey}/history` | Paged payment history |
| GET | `/api/vendor-financials/vendors/{vendorKey}/outstanding` | Paged outstanding instruments |
| GET | `…/summary/export`, `…/vendors/{key}/history/export`, `…/outstanding/export`, `…/purchases-vs-payments/export` | Excel twins |

`[Authorize(Policy = vendor.financials)]` on the controller; export actions also require `export` (same dual-check as purchasing export).

**Rationale.** Smallest change that satisfies F5 AC3 without breaking Phase 2/3/5 scans. Reserved paths stay so `PermissionMatrixTests` keys `VendorFinancialsController.Summary` / `Vendor` remain.

**Alternatives.** Optional balance on `VendorProfileResponse` (fails `NoVendorFinancialsTests`; Purchasing JSON would grow a null field). New `/api/purchasing/vendor-profile` field gated in the service (easy to leak). Replace `/summary` with `/` (churns matrix + audit tests).

---

## R6. Sync in the same job, optional tables (FR-004, FR-005)

**Decision.** After purchase lines (and category), `SyncRunner` syncs mapped financial entities with the same transaction, watermarks, reject budget, and “failure keeps last good copy” rule. Per-table counts appear on Data sync. Reports never read Mabna per request.

Financial logical tables are **optional** (like `product_category`): not in `LogicalTables.SyncOrder` completeness. If an entity is present in the map, its object **must** be on the allowlist and the table **must** finish or the whole run fails.

PostgreSQL (migration `0011`): `mabna.payable_document`, `mabna.payable_cheque`, `mabna.vendor_payment`, `mabna.payment_allocation` (allocation only when mapped). Indexes on vendor key + as-of/payment dates. SQL functions `mabna.fn_vendor_balance`, `mabna.fn_outstanding_payments`, `mabna.fn_vendor_payments` are the reporting copy of the formulas.

Mapper: `VendorFinancialMapping` + `VendorFinancialRowMapper` next to `PurchaseRowMapper` (date parser, cancelled rule, amount sign reused).

**Rationale.** Spec: same job model. Optional tables do not block a purchasing-only install whose map has no Phase 6 entities yet.

**Alternatives.** A second nightly job (two failure modes, two last-sync times). Live Mabna on each Financials request (forbidden).

---

## R7. One metrics document for payables; purchases stay cited (FR-007–FR-012)

**Decision.** Add `docs/metrics/vendor-financials.md` (implementation task). It defines **payments**, **outstanding payments**, and **vendor balance** once. **Purchases** is a pointer to `docs/metrics/purchasing.md` net purchases — that file is not rewritten. Consumers: Financials, profile balance, export, saved reports, AI tools, `reconcile vendor-balance`.

Persian UI labels (not English keys):

| Key | Label | Must not be called |
|---|---|---|
| `vendorBalance` | مانده تأمین‌کننده | خرید، پرداخت، سود |
| `outstandingPayments` | پرداخت‌های معوق | مانده، خرید |
| `payments` | پرداخت‌ها | خرید، مانده |
| `netPurchases` | خالص خرید | پرداخت |
| `purchasesMinusPayments` | خرید منهای پرداخت | سود، حاشیه، مانده، وجه نقد |

**Rationale.** FR-007. Avoids reopening the Phase 1 metrics file except a one-line “also consumed by Phase 6 purchases vs payments” note if needed; prefer the pointer from the new file only.

**Alternatives.** One combined metrics file (mixes domains). Inline comments in SQL only (AI/export would drift).

---

## R8. Permissions on every new surface (FR-023–FR-029)

**Decision.** Enforcement is the `vendor.financials` policy (existing handler → 403 + `access.denied`). Navigation hiding is `SystemModules.VendorFinancials = permissions.Contains(VendorFinancials)` and a `#/financials` tab. Admin and Purchasing: tab hidden **and** 403.

| Surface | Extra check |
|---|---|
| Pages / JSON | `vendor.financials` |
| Export | `vendor.financials` **and** `export` |
| Save financial kind | `vendor.financials` (Finance/Management also have `purchasing.reports` for the saved-reports controller) |
| Reopen financial saved report | **current** role has `vendor.financials`; else Persian reason naming that permission, no query, `access.denied` |
| AI financial tools | `vendor.financials` **and** `ai.query`; Purchasing: router still `vendor-financials` but **no tools**, no digits |
| Admin | 403 on financial APIs; no AI-queries so no financial chat |

Audit: `[AuditReportOpen]` on Financials list, detail, history, outstanding (Phase 3 scope-vs-paging rule). Export: `report.exported` without amounts. Result rows never in `detail`.

**Rationale.** Phase 3 already reserved the gate; Phase 6 must not weaken it once numbers exist. Saved-report reopen matches Phase 5 generated+`ai.query` pattern.

**Alternatives.** Client-only hide (forbidden). Putting financial save on a new controller (duplicate of saved_report).

---

## R9. AI: new tool class, do not rewrite the 20-question set (FR-028)

**Decision.** `VendorFinancialAnalystTools` (separate type so `NoVendorFinancialsInToolsTests` on `PurchasingAnalystTools` stays green). Allowlist (same 10 s / 100 rows / 30 s / max 3 calls):

- `vendor_financials_list` — per-vendor balance + outstanding (comparison optional)
- `vendor_balance` — one vendor signed balance
- `vendor_payments` — payments total (+ optional history summary, still capped)
- `vendor_outstanding` — outstanding total (+ cap)
- `vendor_purchases_vs_payments` — net purchases, payments, optional labeled difference

Router: `scope=vendor-financials` **unchanged as a scope id**. If the caller **has** `vendor.financials`, map a tool (same keyword table, plus ranking/comparison via existing `VisualIntent` tokens). If not, **no tool**, empty facts, «داده در دسترس نیست» / cannot-see — **no digits** (Purchasing fixtures 15–16 stay).

Phase 4 `purchasing-questions.json` is **not** rewritten and is **not** a Phase 6 exit. Additional Persian financial questions for Management/Finance live in `specs/008-vendor-financials/fixtures/financial-questions.json` (cited vs Financials GET, or unavailable). Cloud payloads: **aggregates only** (vendor name + amounts), never payment-line dumps.

`VisualIntent` (or a tiny sibling) treats `vendor_financials_list` as visual-capable for ranking/comparison of balances; chart 2–20 categories; labels from R7.

**Rationale.** Spec: tools additional; 20-set not rewritten. Separate class avoids poisoning purchasing tool JSON with `payment`/`cheque` tokens.

**Alternatives.** Register financial methods on `PurchasingAnalystTools` (breaks `NoVendorFinancialsInToolsTests`). Let the model invent SQL (forbidden).

---

## R10. Saved reports and Excel (FR-026, FR-027)

**Decision.** Migration `0011` widens:

- `kind` += `vendor-financials`, `vendor-financials-history`, `vendor-financials-outstanding`, `vendor-financials-purchases-vs-payments`
- `required_permission` += `vendor.financials` (set **by the server** from kind, never from the client)

Open/export of those kinds: require `vendor.financials` (and `export` for export). Admin opening a Management-saved Financials report → 403 Persian missing permission. Purchasing: 403. Generated **purchasing** visuals stay `ai.query`; a generated visual from a financial tool uses `vendor.financials` **and** `ai.query` on reopen (both missing → refuse).

Excel: reuse `ExcelExportService` (RTL, Persian headers, period, filters, export time, last-sync, 10k cap, demo label, DQ notice). Negative balance cells go through existing `EscapeFormula` (`-` prefix). Print: existing browser-print 1,000-row cap; no PDF pipeline. Demo files labeled demonstration data.

**Rationale.** Phase 5 already built the writer and reopen gate. Widening CHECKs is the smallest schema change.

**Alternatives.** Client-side SheetJS (skips authz). New export stack. Snapshotting amounts into `saved_report` (spec: reopen on current data).

---

## R11. Reconciliation CLI (FR-021, FR-022)

**Decision.** Same host, second command:

```bash
dotnet run --project backend/Zharfa.Api -- reconcile vendor-balance --month 1405-08 [--vendor <sourceKey>]
```

Compares `mabna.fn_vendor_balance` (synced copy, signed) with a guarded re-read of the source using the **same** map formula. Writes `reconciliation_run` with `metric=vendor_balance` (CHECK widened; `discount_basis` unused → store `-`). Mismatch → `reconciliation.mismatch` audit, exit 1, `signOffBlocked: true`. Production stdout omits amounts unless `--show-amounts`. Purchasing reconcile **unchanged**. On-site: one real month; only pass/fail may be committed (`specs/008-vendor-financials/source-verification.md`). Three-month real reconcile is Phase 7.

**Rationale.** Spec: same shape as Phase 1; do not replace purchase reconciliation.

**Alternatives.** Admin UI button (out of scope). Reusing `GET /api/admin/reconciliation` Demo 1 synthetic views (wrong source).

---

## R12. Simulator and on-site recording (FR-002, FR-003)

**Decision.** Extend `deploy/mabna-sim/` generators with `Sim*` payable/cheque/payment tables and synthetic rows (including at least one credit balance and one vendor with purchases and payments in the same month). Example map: `deploy/mabna-sim/example/purchase-source-map.example.json` gains optional entities (still made-up names). Example allowlist lines for those objects.

On-site: add **§7.8 Phase 6 payables** to `specs/002-mabna-accounting-connector/onsite-checklist.md` (catalog + owner-approved aggregates only). Logical statuses in **new** `specs/008-vendor-financials/source-verification.md` — do not edit `specs/003-real-purchase-data/source-verification.md`.

**Rationale.** Spec FR-002/003. New verification file respects “do not reopen Phase 1–5 specs”.

**Alternatives.** Commit live column names (forbidden). Skip simulator until on-site (blocks off-site Finance demo).

---

## R13. Data quality notice (FR-031)

**Decision.** Do not invent a second DQ product. Add financial report keys to `ReportKeys` / `DataQualityCatalog.AffectedReports` for existing types that can flag payable/cheque/payment rows (missing values, duplicates, broken vendor link, invalid dates, inconsistent amounts). Financial views whose scope includes flagged rows show the existing notice. Unverified sources stay «داده در دسترس نیست», not a silent zero.

Seeded bad financial rows in the simulator (optional extra in `dq_bad_records.py`) so Admin Data quality lists them; Financials still compute as specified.

**Rationale.** Spec: Admin still owns Data quality; Phase 3 notice reused.

**Alternatives.** A Finance-only quality page (out of scope).

---

## R14. Tests that currently freeze “no financials”

**Decision.**

| Test | Phase 6 fate |
|---|---|
| `NoVendorFinancialsTests` | **Keep.** Financial DTOs are not in `Purchasing.Reports`. |
| `NoVendorFinancialsInToolsTests` (PurchasingAnalystTools + Phase 5 DTO type names) | **Keep.** New tools/DTOs are in `VendorFinancials` / `VendorFinancialAnalystTools`. |
| `VendorFinancialsTests` (always unavailable) | **Replace** with availability + formula tests; 403 paths stay in matrix/handler tests. |
| `PurchasingQuestionRouterTests` Management/Finance financial questions | **Update in code**: permitted role + financial intent → mapped financial tool. Purchasing rows stay no-tool. Do not edit `specs/006-*`. |
| Phase 4 fixtures 15–16 `no-financials` | **Unchanged** (`askAs: purchasing`). |

**Rationale.** Filling a reserved hole is not reopening Phase 4’s spec; tests that encoded the stub must move with the product.

---

## R15. SPA Journey 2 (FR-014–FR-017)

**Decision.** New hash `#/financials` (tab «اطلاعات مالی تأمین‌کنندگان») when `modules.vendorFinancials`. Reuse `FilterBar` / `Pager` / `useReportQuery` / ی/ک / print / Save / Export. Opening a row shows detail (history, outstanding, purchases vs payments) in place, like the vendor profile. Vendor profile (`VendorProfile.tsx`) receives `canSeeFinancials` and fetches the balance endpoint; a link opens Financials detail without duplicating purchasing tables.

**Rationale.** Spec Journey 2 starts at Vendors → Financials. A first-class tab is visible in nav (hidden without permission) and matches Overview/Reports patterns.

**Alternatives.** Only a sub-mode inside `#/vendors` (easy to miss; Purchasing still uses that page). English routes (out of scope).

---

## R16. Performance and reuse (SC-008)

**Decision.** Server-side page/sort/search like Phase 2 (`fn_search_key`, bound parameters). List and history p95 target: existing report data-view **< 2 s** on synced data. No new indexes beyond vendor + date on the three financial tables. No new npm/NuGet. No fourth host.

**Rationale.** Same scale (~20 users, three years of documents). ClosedXML already present.

**Alternatives.** Client-side full download then page (breaks 2 s and export cap semantics).

---

## R17. Docs that **may** change vs Phase 1–5 specs that **must not**

**May update (behavior change):** `README.md`, `deploy/README.md`, `docs/metrics/vendor-financials.md` (new), `specs/002-mabna-accounting-connector/onsite-checklist.md` §7.8, `deploy/mabna-sim/README.md` + example map/allowlist, `.ainative/project.yaml` validation_commands if a new test project is **not** added (none planned).

**Must not edit:** `specs/003`–`specs/007` spec/plan/tasks/contracts. Phase 6 contracts **extend** those HTTP shapes additively.

---

## R18. Closed skip-mode items (no remaining NEEDS CLARIFICATION)

| Item | Resolution |
|---|---|
| Live Mabna balance mix | On-site; production unavailable until confirmed (R1) |
| Period as-of | Period end (R2) |
| Signed credit | Signed total (R3) |
| Outstanding instrument table | Yes (R4) |
| Comparison toggle | List + purchases vs payments only (R2) |
| Completed instrument types | Every verified type; else payments unavailable (R4) |
| Partial pay | Split if confirmed; else unavailable (R4) |
| Profile vs list | Parallel financial GET; purchasing DTO unchanged (R5) |

Owner may later change as-of, cheque-type mix, or signed-credit convention; until then production stays unavailable without a confirmed formula.
