# On-site Verification Checklist: Mabna Read-Only Connector

**Feature**: specs/002-mabna-accounting-connector | **Date**: 2026-11-14

Short verification sessions against the live Mabna server (`TotalSystem`, read-only login `userds` until `zharfa_ro` exists). Development happens offline against the local SQL Server simulator (schema-only copy of `TotalSystem`, synthetic data); the live server is only reachable on-site. Rules: `AGENTS.md`, FR-01..FR-08 and SC-01..SC-03 in [spec.md](spec.md). Nothing from Mabna is committed; no write is ever attempted.

Scripts (maintained separately, gitignored, not part of this doc): `scratch/mabna-schema-capture.sql`, `scratch/mabna-value-probes.sql`, `scratch/mabna-onsite-run.md`.

Legend: **Who** = Dev (runs it), Owner (decides or watches). **OPEN** = depends on an open question in the spec; do not run the step until the owner answers.

## 1. Before going

- [ ] **Dev** Laptop has the latest build (backend, frontend, desktop shell) and the simulator still starts. Pass: the app runs offline against the simulator.
- [ ] **Dev** The three scripts exist in `scratch/` and have been dry-run against the simulator. Pass: no errors, output folder produced.
- [ ] **Dev** `deploy/.env` holds the live connection string (`ConnectionStrings__...`), is gitignored, and was never committed. Pass: `git check-ignore deploy/.env` prints the path.
- [ ] **Dev** An empty, dated output folder is prepared under `scratch/` (for example `scratch/onsite-YYYY-MM-DD/`).
- [x] **Owner** The company allows schema metadata (catalog, columns, partition row counts) to leave the site. Approved 2026-11-15.
- [x] **Owner** Value probes (`scratch/mabna-value-probes.sql`) approved by the owner on 2026-11-14. P1–P10 were run on-site; they returned aggregates only. Results stay gitignored and are never committed.
- [ ] **Dev** Remember: no write attempts against Mabna, ever. Write-denial is proven by permission metadata and the connector guard (section 3), not by trying.

## 2. Schema drift check

- [ ] **Dev** Rerun `scratch/mabna-schema-capture.sql` against live; save output to `scratch/onsite-YYYY-MM-DD/schema/`.
- [ ] **Dev** Diff against the previous capture folder:
  `git diff --no-index --stat scratch/<previous>/schema scratch/onsite-YYYY-MM-DD/schema` (then without `--stat` for detail).
  Pass: no differences in tables, columns, types, keys, or indexes of the accounting slice (see Key Entities in the spec).
- [ ] **Dev** On drift: update the simulator create script, rerun the connector tests against the updated simulator, and add a dated note to the spec (Clarifications). Pass: simulator matches live again; drift recorded.
- [ ] **Dev** Row counts that differ are expected (live data moves); only schema differences count as drift.

## 3. Connector smoke test against live

- [ ] **Dev** Switch the connection by env only (`deploy/.env`); no code or config change. Pass: `git status` shows no tracked file changed.
- [ ] **Dev** Connector connects and runs one allowlisted metadata-only read. Pass: succeeds, no business rows displayed or stored.
- [ ] **Dev** Write guard: submit an INSERT/UPDATE/DELETE/DDL statement to the connector's guard (unit/guard level, not the live server). Pass: refused before reaching the server.
- [ ] **Dev** Permission metadata for the login (`fn_my_permissions`): SELECT only, no write or DDL. Pass: matches the 2026-11-14 clarification.
- [ ] **Dev** Allowlist: request `scuUser`, `UserRights`, `users`, one `scu*` table and one `msg*` table through the connector. Pass: every one refused by the connector (the DB itself may still allow `scuUser`; see Clarifications 2026-11-14).
- [ ] **Dev** Confirm secrets are absent from logs and from the frontend bundle. Pass: grep finds none.

## 4. API-vs-DB reconciliation (sign-off gate)

Demo 1 has `GET /api/admin/reconciliation` (admin only; query params `from`, `to`; returns `match`, `apiTotals`, `viewTotals`, `signOffBlocked`; mismatch writes an audit entry), implemented in `backend/Zharfa.Api/Analytics/Reconciliation.cs`. It covers synthetic Postgres views only. For Mabna, use the same contract once a Mabna reconciliation exists.

- [ ] **Owner** OPEN — owner decision: first report (spec Open Question 1). Choose one period (one Jalali month) and one filter. Record both before running.
- [ ] **Dev** Run the API figure for that period and filter (admin session, Mabna reconciliation endpoint or the nearest equivalent).
- [ ] **Dev** Run the same query directly against live (read-only, aggregates only). Pass: totals equal for each formula separately.
- [ ] Keep the three formulas separate and compare each on its own:
  - [ ] Sales revenue total.
  - [ ] Cash received total (never treated as, or compared with, sales revenue). Uncleared cheques are excluded and shown separately.
  - [ ] Receivables balance.
- [ ] **Dev** OPEN — owner decision: which `ChqStateValue` values count as cash received (Open Question 4) and how cancelled/returned invoices are treated (Open Question 5). Pass: the rule used is the one the owner stated; record it in the log.
- [ ] **Dev** Any mismatch blocks sign-off. Pass: `signOffBlocked: false`; on mismatch, log it (section 6), do not adjust the formula on-site.
- [ ] **Dev** Check that Arabic/Persian character normalization (ي/ك to ی/ک) and Jalali-to-Gregorian conversion do not shift period edges (first and last day of the month).

## 5. Comparison with Mabna's own reports

- [ ] **Owner** Open the matching Mabna report (for example the receivables report `darusaleprj.txt` for receivables) for the same period and filter.
- [ ] **Dev** Compare Zharfa's total with the report total. Record only: report name, period, filter, pass/fail, and the totals difference.
- [ ] **Owner** OPEN — owner decision: whether aggregate totals (and the totals difference) may leave the site. If not allowed, record pass/fail only, and keep totals on-site on paper or on the owner's screen.
- [ ] **Dev** Do not copy, photograph, export, or paste business rows (customers, invoices, cheque rows) off-site. Pass: nothing but pass/fail and allowed totals in the log.
- [ ] **Dev** Explain any difference with a documented rule (cancelled invoices, uncleared cheques, date edges). An unexplained difference counts as a mismatch.

## 6. Before leaving

- [ ] **Dev** `git status` shows no Mabna output, no `deploy/.env`, no tracked changes beyond intended docs. Pass: clean.
- [ ] **Dev** `git check-ignore scratch/` prints `scratch/` (it is gitignored); `deploy/.env` is ignored too.
- [ ] **Dev** Output folders under `scratch/` hold metadata only (schema capture; value-probe aggregates, approved 2026-11-14). No row extracts anywhere on the laptop. Pass: owner agrees on a quick listing.
- [ ] **Dev** Close the live connection and remove the live string from the shell history/environment; keep `deploy/.env` only if the owner allows it on the laptop.
- [ ] **Dev** Session log written to `scratch/onsite-YYYY-MM-DD/log.md` (gitignored; also see `scratch/mabna-onsite-run.md`): date, who attended, checks run, pass/fail per check, drift found, reconciliation period and filter, totals difference (only if allowed), owner decisions made, follow-ups.
- [ ] **Owner** What may leave the site: schema metadata (approved), the log with pass/fail, and aggregates only if the owner allowed them. Nothing else.
- [ ] **Dev** After the visit: update the spec Clarifications with decisions and results, and the simulator script if drift was found.

## 7. Purchasing slice

Release 1 Phase 1 (spec 003, `specs/003-real-purchase-data/`). Run after sections 1–3 pass on the same visit. Only catalog metadata (names, types, keys, nullability) and owner-approved aggregate probes are used; no business rows. **Who** = Dev for every step; the Owner approves any aggregate probe before it runs. Record each item's status in the on-site source map (gitignored, `MabnaSync__SourceMapFile`) and the logical status only in `specs/003-real-purchase-data/source-verification.md`.

Status vocabulary: `unknown` (not checked) | `candidate` (off-site candidate, unusable in production) | `confirmed` | `corrected`. Only `confirmed`/`corrected` count in production and only those objects join the live allowlist.

### 7.1 Master data

- [ ] **Dev** Vendor master: key column, name column, and whether an active marker exists (or none). Pass: each identified by catalog metadata; the key is unique (a duplicate-count aggregate, owner-approved).
- [ ] **Dev** Product master: key column and name column. Pass: identified by catalog metadata; the key is unique (same rule as the vendor key).

### 7.2 Invoice and return headers

- [ ] **Dev** Header key, document number, vendor key and document date. Pass: each identified by catalog metadata. Confirm the date format (Jalali text, Jalali integer, or Gregorian) with one owner-approved aggregate (min and max per format, not rows).
- [ ] **Dev** Operation code that selects purchase invoices, and the one that selects purchase returns. Pass: each code confirmed with an owner-approved count per code for one period; no other code is used.
- [ ] **Dev** Cancelled rule: a flag column with its cancelled value, or a zero-header rule. Pass: rule identified; the owner-approved count of cancelled documents per rule matches the owner's expectation.
- [ ] **Dev** Document-level discount column on invoice and return headers. Pass: mapped, or recorded as absent (`unknown`). This decides whether the `lineAndDocument` basis below is possible.

### 7.3 Lines

- [ ] **Dev** Line key, document key (link to the header), product key and quantity. Pass: identified by catalog metadata; the document key joins each line to exactly one header type (owner-approved aggregate: count of unlinked lines is zero).
- [ ] **Dev** Gross line amount column and its sign convention (as stored, negated, or absolute). Pass: the sign is confirmed against the owner's report total for one period (aggregate only).
- [ ] **Dev** Line discount column and VAT column. Pass: each mapped, or recorded as absent (`unknown`).

### 7.4 Change markers, units and discount basis

- [ ] **Dev** Change marker per table (row version, modified time, increasing key, or `full`). Header tables with an increasing key are read in full on every run; row-version and modified-time header tables also need a key-only read to find deletions (spec 003 Phase 13). Pass: each table has a recorded strategy; no table is left without one.
- [ ] **Owner** Amount unit: confirm the stored amounts are Rial. Pass: the owner confirms against a report figure (aggregate only).
- [ ] **Dev** Discount basis of Mabna's own purchase report total: `line` only, or `line` plus document discount. Pass: the basis that reproduces the owner's report total for one period. Set the map's `discountBasis` and `Purchasing__DiscountBasis` to the same value.

### 7.5 Allowlist and recording rules

- [ ] **Dev** Every purchasing item in the on-site source map is `confirmed` or `corrected` before any sync is run against live. Pass: no item left as `candidate` or `unknown` on the items a figure needs.
- [ ] **Dev** The live allowlist holds only the objects whose items are `confirmed` or `corrected`. Pass: no `candidate` or `unknown` object on the live allowlist.
- [ ] **Dev** Mabna's report views that filter on `HOST_ID()` are never read: every entity in the map sets `hostFiltered: false` and none of those views is on the allowlist. Pass: grep of the map and allowlist finds none.
- [ ] **Dev** Copy the logical statuses and the verification date into `specs/003-real-purchase-data/source-verification.md`, and the on-site reconciliation pass or fail (no amounts). Pass: the file holds logical item names only; no real names and no rows.
- [ ] **Dev** Nothing from this section is committed. Pass: `git status` clean (section 6).

### 7.6 Phase 2 items

Release 1 Phase 2 (spec 004, `specs/004-vendor-purchasing-reports/`). Run with 7.1–7.5 on the same visit. Same rules: catalog metadata only, owner-approved aggregates only, statuses recorded in the gitignored map and, as logical names only, in `specs/003-real-purchase-data/source-verification.md` (items 40–46).

- [ ] **Dev** Item 40, vendor code shown to users: identify the column by catalog metadata and confirm with the owner which code users see. Pass: the column is mapped as `vendor.code` (`candidate` until confirmed); no code value is copied into the repository.
- [ ] **Dev** Item 41, product drug group key: identify the product column that holds the group key. Pass: identified by catalog metadata; nullability recorded (NULL means uncategorized).
- [ ] **Dev** Items 42–45, drug group master: object, key column, name column and change marker. Pass: each identified by catalog metadata; the key is unique (owner-approved duplicate-count aggregate). The object joins the live allowlist only once its items are `confirmed` or `corrected`; until then the category figures stay unavailable.
- [ ] **Dev** Item 46, drug class second level: recorded as `unknown`. Not built in Phase 2; no check is run.
- [ ] **Dev** The views that filter on `HOST_ID()` are never read for these items either, and none of them is added to the allowlist. Pass: grep of the map and allowlist finds none.

### 7.7 Phase 3 items (Permissions, Audit, Data Quality)

Release 1 Phase 3 (spec 005, `specs/005-permissions-audit-quality/`). Run with 7.1–7.6 on the same visit. Same rules: catalog metadata only, owner-approved aggregates only.

- [ ] **Dev** First fiscal year start date: verify the start date of Mabna's first fiscal year and set `DataQuality__EarliestJalaliDate` (e.g. `1390-01-01`). Purchase documents dated earlier are flagged as invalid dates (rule DT2).
- [ ] **Dev** Line net-amount and document-total columns: verify whether a line net-amount column and a document-total column exist in Mabna's purchase tables. Confirming and mapping these columns will enable the deferred `line-net-mismatch` and `document-total-mismatch` sub-checks in data quality (currently listed under «بررسی نشده»).
- [ ] **Dev** Product code column(s): verify the product code column(s) in the product master and whether one code can map to several products. This will enable replacing the Release 1 name-based heuristic for `inconsistent-product-ids` (rule PI1) with a code-level verification rule.
- [ ] **Dev** Purchase document number uniqueness: confirm whether purchase document numbers in Mabna are unique per document type alone or per vendor and document type. Release 1 duplicate rule DU1 partitions by `(doc_type, vendor_source_key, doc_number)`. Pass: confirmed with the owner / verified by catalog metadata and aggregates.
