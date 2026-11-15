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
- [ ] **Owner** The company allows schema metadata (catalog, columns, partition row counts) to leave the site. Pass: confirmed before the visit.
- [ ] **Owner** OPEN — owner decision: whether any value probe (`scratch/mabna-value-probes.sql`) may run at all. Pass: explicit yes per probe set; otherwise skip section "value probes" and record "not approved".
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
- [ ] **Dev** Output folders under `scratch/` hold metadata only (schema capture; probe results only if approved). No row extracts anywhere on the laptop. Pass: owner agrees on a quick listing.
- [ ] **Dev** Close the live connection and remove the live string from the shell history/environment; keep `deploy/.env` only if the owner allows it on the laptop.
- [ ] **Dev** Session log written to `scratch/onsite-YYYY-MM-DD/log.md` (gitignored; also see `scratch/mabna-onsite-run.md`): date, who attended, checks run, pass/fail per check, drift found, reconciliation period and filter, totals difference (only if allowed), owner decisions made, follow-ups.
- [ ] **Owner** What may leave the site: schema metadata (approved), the log with pass/fail, and aggregates only if the owner allowed them. Nothing else.
- [ ] **Dev** After the visit: update the spec Clarifications with decisions and results, and the simulator script if drift was found.
