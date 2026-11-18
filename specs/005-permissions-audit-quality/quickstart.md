# Quickstart: Permissions, Audit, and Data Quality (Release 1, Phase 3)

Validation guide for the tester and the PR walkthrough. Endpoints and payloads: [contracts/api.md](contracts/api.md); Persian UI strings are cited by their ids (`S-…`) from contracts/api.md §9. Tables and rules: [data-model.md](data-model.md). Simulator bad records: [contracts/sim-bad-records.md](contracts/sim-bad-records.md). Phase 1/2 setup and helpers are reused from `specs/003-real-purchase-data/quickstart.md` §0 and `specs/004-vendor-purchasing-reports/quickstart.md` §0.

Exit criteria (`zharfa-prd.phases.md` Phase 3) → flows:

| Exit criterion | Flows |
|---|---|
| Roles Admin, Management, Purchasing, Finance exist; Admin assigns them | A1, A8 |
| Purchasing calling a vendor-financial or admin API directly gets 403, logged | A2, B4 |
| A role change takes effect on the user's next request | A3, A4 |
| Sign-in, report opens, admin changes, permission changes in the audit log, filterable by user/action/date, no secrets or result rows | B1–B7 |
| After a sync with seeded bad records, Data quality lists each type with count, examples, affected reports | C0–C5 |
| A report that depends on flagged records shows a data-quality notice | D1–D6 |

## 0. Prerequisites

- Phase 2 quickstart §0 done: simulator up with the Phase 2 scenario, `deploy/.env` with `ZHARFA_DATA_MODE=demo`, dev override compose, and the `generated/purchase-source-map.json` + `allowlist.txt`.
- Rebuild and start the stack (migration `0008` runs at API start):

```bash
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.purchasing-dev.yml up --build -d
curl -s localhost:8080/api/health
```

- On an **existing** Phase 2 demo DB this is the upgrade path (A1 checks `manager` → `management` and the two new demo users). Also run once on a fresh DB (`docker compose … down -v` for the Zharfa stack only, then `up`) to check the seed path.

Helpers (extend Phase 1's):

```bash
login() { curl -s -o /dev/null -w '%{http_code}\n' -c /tmp/z_$1 -H 'Content-Type: application/json' -d "{\"username\":\"$1\",\"password\":\"$2\"}" localhost:8080/api/auth/login; }
login admin admin123; login manager manager123; login purchasing purchasing123; login finance finance123   # all 200
A="-b /tmp/z_admin"; M="-b /tmp/z_manager"; P="-b /tmp/z_purchasing"; F="-b /tmp/z_finance"
code() { curl -s -o /dev/null -w '%{http_code}' "$@"; }
psql_q() { docker compose -f deploy/docker-compose.yml exec -T db psql -U zharfa -d zharfa -At -c "$1"; }
R=localhost:8080/api/purchasing
START_AUDIT=$(psql_q "select coalesce(max(audit_id),0) from audit_log")   # rows written by this session are audit_id > START_AUDIT
```

Pick a test month `m` (Jalali `yyyy-mm`) that has synced purchases; the bad records in §C are dated in it (`--date <yyyy>/<mm>/15`).

## 1. Validation suite (no DB, no network)

`.ainative/project.yaml` `validation_commands` (frontend build, API and desktop compile, `dotnet test`). Pass: all green, including the new suites of research R18 (`Security/*`, `Audit/*`, `DataQuality/*`, updated `Platform/*`).

---

## A. Roles and permissions

### A1 — Four roles; Admin assigns (US1 AC1–AC3, AC9, FR-001/FR-002/FR-006/FR-011, SC-001)

```bash
curl -s $A localhost:8080/api/admin/roles | jq '[.roles[].id]'                    # ["admin","management","purchasing","finance"]
curl -s $A localhost:8080/api/admin/users | jq '.users[] | {username,role}'        # manager → "management"; purchasing, finance present
psql_q "select username, role from app_user order by user_id"
curl -s $A -H 'Content-Type: application/json' -d '{"username":"Ali.K","password":"pass1234","role":"purchasing"}' localhost:8080/api/admin/users | jq '{id,username,role}'
curl -s $A -H 'Content-Type: application/json' -d '{"username":"ali.k","password":"pass1234","role":"finance"}' -w '%{http_code}\n' localhost:8080/api/admin/users   # 400 duplicate (case)
curl -s $A -H 'Content-Type: application/json' -d '{"username":"علي","password":"pass1234","role":"finance"}' localhost:8080/api/admin/users >/dev/null
curl -s $A -H 'Content-Type: application/json' -d '{"username":"علی","password":"pass1234","role":"finance"}' -w '%{http_code}\n' localhost:8080/api/admin/users    # 400 duplicate (ي/ی)
curl -s $A -H 'Content-Type: application/json' -d '{"username":"short","password":"1234567","role":"finance"}' -w '%{http_code}\n' localhost:8080/api/admin/users # 400 «حداقل ۸ نویسه»
login Ali.K pass1234                                                                  # 200
curl -s $A localhost:8080/api/admin/users | jq '.users[0] | keys'                     # no password_hash / failed_signins
```

Pass: exactly four roles with Persian permission descriptions; Demo 1 accounts kept (old passwords still work); created user signs in; duplicates refused after normalization; short password refused. UI (Settings → کاربران و نقش‌ها): create a user in < 1 minute (SC-001).

### A2 — Direct API matrix, 401/403 (US2 AC1–AC5, AC7, FR-003/FR-004/FR-005, SC-002)

```bash
for u in A M P F; do
  eval c=\$$u; echo "== $u"
  for ep in /api/overview/purchasing "$R/vendors" "$R/reports/by-vendor?month=$m" "$R/reports/transactions?month=$m" "$R/documents?month=$m" \
            /api/vendor-financials/summary /api/vendor-financials/vendors/1 \
            /api/admin/users /api/admin/roles /api/admin/audit /api/admin/data-quality /api/admin/sync/jobs /api/admin/provider "/api/admin/reconciliation"; do
    printf '%-55s %s\n' "$ep" "$(code $c "localhost:8080${ep#localhost:8080}")"
  done
done
for u in M P F; do eval c=\$$u; printf '%s POST /api/admin/sync/run %s\n' $u "$(code $c -X POST localhost:8080/api/admin/sync/run)"; done   # 403 ×3
curl -s $M localhost:8080/api/vendor-financials/summary | jq .        # {available:false, message:"داده در دسترس نیست.", …} — no figures
code localhost:8080/api/purchasing/vendors; echo                      # 401 (no cookie)
```

Pass (compare with contracts/api.md §0): Purchasing → 403 on both vendor-financial and every admin endpoint (POST sync/run included; it is not called as Admin here so no sync starts); Admin → 403 on vendor-financials and 200 on admin endpoints; Management/Finance → 200 «داده در دسترس نیست» on vendor-financials, 403 on admin; all four → 200 on overview/reports/documents. Every 403 body is the Persian message with `code:"forbidden"` and contains no data. `provider`/`reconciliation` are demo-only: 200 for Admin in demo. Then:

```bash
psql_q "select actor, target, detail from audit_log where action='access.denied' order by audit_id desc limit 40"
```

Pass: one row per 403 above (count them), actor = the caller, target = method + route pattern, detail = `permission=…`. Repeat one forbidden call 5 times quickly → 5 more rows (nothing dropped).

### A3 — Role change applies on the next request (US1 AC4, FR-007, SC-003)

```bash
PID=$(curl -s $A localhost:8080/api/admin/users | jq '.users[] | select(.username=="purchasing") | .id')
code $P localhost:8080/api/vendor-financials/summary; echo                     # 403
curl -s $A -H 'Content-Type: application/json' -d '{"role":"finance"}' localhost:8080/api/admin/users/$PID/role | jq .role
code $P localhost:8080/api/vendor-financials/summary; echo                     # 200 — same cookie, no re-login
curl -s $P localhost:8080/api/auth/me | jq '{role,permissions}'                # finance permissions
curl -s $A -H 'Content-Type: application/json' -d '{"role":"purchasing"}' localhost:8080/api/admin/users/$PID/role >/dev/null
code $P localhost:8080/api/vendor-financials/summary; echo                     # 403 again
psql_q "select detail from audit_log where action='user.role_changed' order by audit_id desc limit 2"   # from=finance;to=purchasing / from=purchasing;to=finance
```

UI: signed in as purchasing in one browser, change the role in another as Admin; the next page load of the purchasing browser shows Finance's navigation; a page that became forbidden shows «شما به این بخش دسترسی ندارید.».

### A4 — Deactivate / reactivate (US1 AC5, FR-007)

```bash
curl -s $A -X POST localhost:8080/api/admin/users/$PID/deactivate | jq .active   # false
code $P localhost:8080/api/overview/purchasing; echo                             # 401 (session ended)
login purchasing purchasing123                                                   # 401
curl -s $A -X POST localhost:8080/api/admin/users/$PID/reactivate | jq .active   # true
login purchasing purchasing123                                                   # 200
```

### A5 — Last active Admin (US1 AC6, FR-008)

```bash
AID=$(curl -s $A localhost:8080/api/admin/users | jq '.users[] | select(.username=="admin") | .id')
curl -s $A -X POST -w '%{http_code}\n' localhost:8080/api/admin/users/$AID/deactivate                                              # 409 last-admin
curl -s $A -H 'Content-Type: application/json' -d '{"role":"management"}' -w '%{http_code}\n' localhost:8080/api/admin/users/$AID/role  # 409
```

Create a second Admin, then demoting the first succeeds (200); restore afterwards.

### A6 — Lockout (US1 AC8, FR-033, SC-009)

```bash
for i in 1 2 3 4; do login finance wrong-pass; done      # 401 ×4
login finance wrong-pass                                  # 423 (5th)
login finance finance123                                  # 423 — correct password still refused
curl -s $A localhost:8080/api/admin/users | jq '.users[] | select(.username=="finance") | {locked,lockedUntilJalali}'
FID=$(curl -s $A localhost:8080/api/admin/users | jq '.users[] | select(.username=="finance") | .id')
curl -s $A -X POST localhost:8080/api/admin/users/$FID/unlock | jq .locked   # false
login finance finance123                                  # 200
psql_q "select action, detail from audit_log where actor='finance' and action in ('auth.signin.failed','auth.locked','user.unlocked','auth.signin.succeeded') order by audit_id desc limit 8"
```

Also: lock again and wait 15 minutes (or set `locked_until` to the past with `psql_q "update app_user set locked_until=now()-interval '1 second' where username='finance'"`) → sign-in 200. `login nobody xyz` → 401 and an `auth.signin.failed` row with `reason=unknown-user`.

### A7 — First Admin on a fresh production install (US1 AC10, FR-034, SC-010)

Throwaway production stack (Phase 1 Flow 10 setup: separate compose project and volume, `ZHARFA_DATA_MODE=production`, verified-status source map not needed for this flow):

1. Start **without** `Bootstrap__Admin*` → the API refuses to start; `docker compose logs api` names the missing keys and contains no value; `app_user` empty.
2. Put the password in a file and set `Bootstrap__AdminUsername=owner`, `Bootstrap__AdminPasswordFile=/run/secrets/zharfa_admin_password` (mounted file, ≥ 12 chars) → starts; exactly one user, role `admin`, `must_change_password = t`.
3. Sign in as `owner` → 200 with `mustChangePassword:true`; `GET /api/overview/purchasing` → 403 `password-change-required`; `POST /api/auth/change-password` (8+ chars, different) → 204; overview → 200.
4. Restart with the same secret → no new user, password unchanged (the new password still works).
5. `docker compose logs api | grep -F "<the secret value>"` → no output; `psql … "select count(*) from audit_log where detail like '%<secret>%'"` → 0. No `manager` account exists.

### A8 — Navigation and "no access" (US2 AC6, FR-012)

Sign in in the browser as each demo user. Pass: tabs match the role (Admin: overview, vendors, reports, comparison, Settings → همگام‌سازی، کاربران و نقش‌ها، گزارش رویدادها، کیفیت داده, demo pages; others: no Settings tabs). As purchasing open `#/settings/users` and `#/settings/data-quality` directly → «شما به این بخش دسترسی ندارید.» and no data request succeeds (Network tab: 403). Header shows the Persian role label. Keyboard: every new control reachable by Tab, focus visible.

---

## B. Audit log

### B1 — Sign-in and sign-out entries (US3 AC1)

After §A, as Admin:

```bash
curl -s $A "localhost:8080/api/admin/audit?action=auth.signin.succeeded" | jq '.total, .rows[0] | {actor,atJalali,actionLabel}'
curl -s $A "localhost:8080/api/admin/audit?action=auth.signin.failed" | jq '.rows[] | {actor,detail}'      # reasons; never a password
```

### B2 — Report opens with period and filters; reloads count, paging/sorting add none (US3 AC2, FR-013/FR-014, Clarify Q2, owner decision 2026-11-18)

```bash
before=$(psql_q "select count(*) from audit_log where actor='manager' and action like 'report.%'")
curl -s $M "localhost:8080/api/overview/purchasing?month=$m" >/dev/null                          # +1 overview
curl -s $M "$R/reports/by-vendor?month=$m" >/dev/null                                            # +1
curl -s $M "$R/reports/by-vendor?month=$m&page=2" >/dev/null                                     # +0 (paging only)
curl -s $M "$R/reports/by-vendor?month=$m&sort=name&dir=asc" >/dev/null                         # +0 (sorting only)
curl -s $M "$R/reports/by-vendor?month=$m&sort=name&dir=asc" >/dev/null                         # +1 (reload, identical params)
curl -s $M "$R/reports/by-vendor?month=$m&product=<p>" >/dev/null                                # +1 (filter change)
curl -s $M "$R/vendor-profile?vendor=<key>&month=$m" >/dev/null                                  # +1
curl -s $M "$R/vendors/compare?vendors=<K1>,<K2>&month=$m" >/dev/null                            # +1
curl -s $M "$R/documents?month=$m&vendor=<key>" >/dev/null                                       # +1 drill-down
curl -s $M "$R/reports/by-vendor/print?month=$m" >/dev/null                                      # +1 report.printed
curl -s $M "$R/reports/by-vendor/print?month=$m" >/dev/null                                      # +1 (prints always)
curl -s $M "$R/lookup/vendors?q=a" >/dev/null                                                    # +0
psql_q "select action, target, detail from audit_log where actor='manager' and action like 'report.%' order by audit_id desc limit 10"
```

Pass: exactly 9 new rows; targets `overview`, `by-vendor` (open), `by-vendor` (reload), `by-vendor` (filter change), `vendor-profile`, `vendors-compare`, `drill-down`, `by-vendor` ×2 (printed); details carry period/month and filters, never `page`/`pageSize`/`sort`/`dir`; no amounts, names of result rows or row data. There is no time window: wait 1 minute, repeat the `month=$m` by-vendor call → +1.

UI: open by-vendor for month `m` in the browser → +1; move to page 2 and change the sort column → +0; press the browser reload → +1 (and only +1: a single page load must not issue the same report request twice); change the period → +1.

### B3 — Administrative and permission changes (US3 AC3–AC4)

```bash
for a in user.created user.role_changed user.deactivated user.reactivated user.password_reset user.unlocked sync.requested; do
  printf '%-22s %s\n' $a "$(curl -s $A "localhost:8080/api/admin/audit?action=$a" | jq -c '[.total, .rows[0].target, .rows[0].detail]')"
done
```

(Reset a password once first: `curl -s $A -H 'Content-Type: application/json' -d '{"password":"newpass123"}' -X POST localhost:8080/api/admin/users/<id>/password`; run "Sync now" once as Admin.) Pass: each present with user, time, target; role change has `from`/`to`; password reset detail empty.

### B4 — Filters (US3 AC5, FR-017, SC-004)

```bash
today=$(curl -s $A localhost:8080/api/system/status | jq -r .serverJalaliToday)
curl -s $A "localhost:8080/api/admin/audit?user=purchasing" | jq '[.rows[].actor] | unique'                          # ["purchasing"]
curl -s $A "localhost:8080/api/admin/audit?user=purchasing&action=access.denied&from=$today&to=$today" | jq '.total'   # = 403 count from A2 for P (+5 repeats)
curl -s $A "localhost:8080/api/admin/audit?from=۱۴۰۵-۰۸-۰۱&to=۱۴۰۵-۰۸-۳۰" | jq '.total'                            # Persian digits accepted
curl -s $A -w '%{http_code}\n' "localhost:8080/api/admin/audit?from=$today&to=1400-01-01"                            # 400
curl -s $A "localhost:8080/api/admin/audit?user=nobody" | jq '{total,rows}'                                          # 0, [] (200)
curl -s $A "localhost:8080/api/admin/audit?pageSize=10&page=2" | jq '{total,page,n:(.rows|length)}'
```

Cross-check each count with SQL on `audit_log` (same predicate). Rows newest first. UI: filter by user, action (Persian labels) and Jalali dates; empty result shows an empty state.

### B5 — No secrets or result rows (US3 AC6, FR-015, SC-005)

```bash
psql_q "select count(*) from audit_log where detail ~* '(admin123|manager123|purchasing123|finance123|pass1234|newpass123|pbkdf2|zharfa_session|password=|Password=)'"   # 0
docker compose -f deploy/docker-compose.yml logs api | grep -Eic 'admin123|manager123|purchasing123|finance123|pass1234|newpass123|pbkdf2-sha256\$|zharfa_session=|ConnectionStrings|MABNA_SIM_RO_PASSWORD'   # 0
psql_q "select max(length(detail)) from audit_log"                                                                # ≤ 500
# No figures in report-open details: every key is in the AuditDetail allowlist (+ documentId)
psql_q "select count(*) from audit_log, regexp_split_to_table(detail, '; ') kv where audit_id > $START_AUDIT and action like 'report.%' and split_part(kv,'=',1) not in ('period','month','quarter','year','from','to','vendor','vendors','product','category','docType','q','match','compare','all','documentId')"   # 0
# No amounts in reconciliation details (I9): run the Phase 1 reconciliation CLI once (spec 003 Flow 7) and, in demo, GET /api/admin/reconciliation as Admin, then
psql_q "select count(*) from audit_log where audit_id > $START_AUDIT and action like 'reconciliation.%' and detail ~* '(\"(system|source|difference)\"|apiSales|viewSales|apiReceivables|viewReceivables)'"   # 0
```

Also open a report with `q=<120 characters>`: its entry's `q` is cut to 100 characters.

### B6 — Append-only (US3 AC7, FR-016)

```bash
psql_q "update audit_log set action='x' where audit_id=(select max(audit_id) from audit_log)"   # ERROR: audit_log is append-only
psql_q "delete from audit_log where audit_id=(select max(audit_id) from audit_log)"             # ERROR
code $A -X DELETE localhost:8080/api/admin/audit/1; echo                                        # 404/405: no such endpoint
code $M localhost:8080/api/admin/audit; echo                                                    # 403 (US3 AC8)
```

### B7 — Page speed with one year of entries (SC-008, FR-032)

On a throwaway demo DB, after at least one succeeded sync: `psql … -f db/perf/audit-1y.sql` (inserts ~100 000 synthetic entries spread over 365 days, actors from the demo users, labelled `perf` in `target`) and `psql … -f db/perf/data-quality-50k.sql` (adds a new succeeded `data_quality_run` for the latest succeeded sync job with ~50 000 synthetic `data_quality_issue` rows spread over the seven types, attached to real synced line/document/product ids so report notices hit them). Then:

```bash
for i in 1 2 3 4 5; do curl -s -o /dev/null -w '%{time_total}\n' $A "localhost:8080/api/admin/audit?action=report.opened&from=1405-01-01&to=1405-12-29"; done
for i in 1 2 3 4 5; do curl -s -o /dev/null -w '%{time_total}\n' $A localhost:8080/api/admin/users; done
for i in 1 2 3 4 5; do curl -s -o /dev/null -w '%{time_total}\n' $A localhost:8080/api/admin/data-quality; done
for i in 1 2 3 4 5; do curl -s -o /dev/null -w '%{time_total}\n' $M "$R/reports/by-vendor?month=$m&compare=true"; done   # report with a notice (current + comparison scope)
curl -s $M "$R/reports/by-vendor?month=$m&compare=true" | jq -c '.dataQuality | {unavailable, affectedRecords}'           # unavailable false, affectedRecords > 0
```

Pass: every time < 2 s; the three pages and the report with its notice render within 2 s in the browser; the Data quality counts are exact (compare with `select issue_type, count(distinct record_ref) …` on the new run) and each type still lists ≤ 10 examples. Afterwards drop the throwaway DB (or run a sync, which replaces the synthetic run's issues).

---

## C. Data quality

### C0 — Before any check, and the baseline (US4 AC3–AC4, FR-023)

1. On the fresh DB (before the first sync): `curl -s $A localhost:8080/api/admin/data-quality | jq '{check, latestSync, counts:[.issueTypes[].count]}'` → `check: null`, counts null; the page says no check has run; reports show no notice (`dataQuality: null`).
2. Run "Sync now" as Admin, wait for `succeeded`, then save the baseline:

```bash
curl -s $A localhost:8080/api/admin/data-quality | jq -c '[.issueTypes[] | {type,count}]' | tee /tmp/dq_base.json
curl -s $A localhost:8080/api/admin/data-quality | jq '.check, .latestSync.status'     # checked time present; succeeded
```

Every type whose baseline count is 0 shows S-DQ-NONE («مشکلی یافت نشد.») on the page. Record also, before seeding, `dataQuality` of: `by-vendor?month=$m`, `trend?month=$m&product=<qProd>`, `by-vendor?month=$m&product=<qProd>` (all expected null; if not, note which baseline issue caused it — §D compares against these).

### C1 — Seed bad records and sync (FR-030)

```bash
python3 deploy/mabna-sim/dq_bad_records.py --map deploy/mabna-sim/generated/purchase-source-map.json --date <yyyy>/<mm>/15
dq_sql() { docker compose -f deploy/docker-compose.mabna-sim.yml exec -T mabna-sim bash -c 'S=$(ls /opt/mssql-tools*/bin/sqlcmd | head -n1); $S -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -b -f 65001 -i /dev/stdin'; }
dq_sql < deploy/mabna-sim/generated/dq/dq-apply.sql | tee /tmp/dq_manifest.txt      # manifest: issue_type, seeded
```

Then "Sync now" as Admin until `succeeded`.

### C2 — Counts, examples, affected reports (US4 AC1–AC2, FR-020/FR-023/FR-024, SC-006)

```bash
curl -s $A localhost:8080/api/admin/data-quality | jq -c '[.issueTypes[] | {type,count}]'
curl -s $A localhost:8080/api/admin/data-quality | jq '.issueTypes[] | {type, n:(.examples|length), ex:.examples[0], reports:[.affectedReports[].key], notChecked}'
psql_q "select issue_type, count(distinct record_ref) from data_quality_issue where run_id=(select id from data_quality_run where status='succeeded' order by id desc limit 1) group by 1 order by 1"
```

Pass: for each of the seven types, the count increases by exactly the seeded number for that type (count − baseline = manifest count; with the example map: missing-values +4, duplicate-records +1, broken-relationships +3, inconsistent-product-ids +1, invalid-dates +2, unexpected-quantities +2, inconsistent-amounts +3); the SQL agrees; each type lists ≤ 10 examples and at least one matches a seeded record (seeded document numbers are ≥ the manifest's number base; dates `<yyyy>/<mm>/15`, `1499/01/01`, `1350/01/01`; product `SimDqProduct-BR3`; field labels as seeded); affected reports equal research R13; `inconsistent-amounts` lists the two deferred sub-checks under S-DQ-NOT-CHECKED with S-DQ-NET / S-DQ-TOTAL; `inconsistent-product-ids` carries `heuristic` = S-DQ-HEURISTIC; `latestCheckFailed: false`. `rejectedAtSync` (box S-DQ-REJECTED) shows only Phase 2 baseline rejects (none added by the seed). The page shows the same in Persian with Jalali dates and the demo label; every type whose count is 0 shows S-DQ-NONE.

### C3 — A failed sync keeps the last result (US4 AC5, FR-022)

Set `Sync__FaultInjection__FailAfterTables=2` for the api in the dev override (demo + Development only), restart `api`, run "Sync now" → `failed`. Pass: Data quality still shows the C2 counts and `check` time, `latestSyncFailed: true` with S-DQ-SYNC-FAILED and the Persian failure text; `latestCheckFailed: false`.

Check failure (I6): with a succeeded sync in place, make the next validation fail without touching the sync — e.g. `psql_q "alter table data_quality_issue add constraint dq_tmp_fail check (false) not valid"` — run "Sync now" → sync `succeeded`, newest `data_quality_run` `failed`. Pass: `latestCheckFailed: true`, the page shows S-DQ-CHECK-FAILED and still the previous `check` and counts; the api log line names only the exception type. Then `psql_q "alter table data_quality_issue drop constraint dq_tmp_fail"` and sync again → `latestCheckFailed: false`. Remove the setting, restart `api`, sync again → `succeeded`, `latestSyncFailed: false`.

### C4 — Corrected records disappear (US4 AC6)

```bash
dq_sql < deploy/mabna-sim/generated/dq/dq-revert.sql
```

"Sync now" → counts equal `/tmp/dq_base.json` again; the seeded examples are gone. Re-apply `dq-apply.sql` and sync once more before §D.

### C5 — Admin only (US4 AC7, FR-025)

`code $M localhost:8080/api/admin/data-quality` → 403; `code $P …` → 403; both audited (`access.denied`). Admin open → `data_quality.viewed` entry.

### C6 — Figures unchanged by flags (US5 AC5, FR-026)

With bad records applied and synced: rerun Phase 2 quickstart Flow P3 bullets 1–2 for month `m` (by-vendor/by-product/by-category `totals.net` = Overview `totals.net` = direct SQL over `fn_purchase_lines`) and the Phase 1 reconciliation CLI (spec 003 Flow 7) → exit 0. The «نامشخص» rows include the amounts of seeded records 2, 3, 6, 7.

---

## D. Data-quality notices

### D1 — Notice when the scope includes flagged records (US5 AC1, FR-027, SC-007)

```bash
for ep in "/api/overview/purchasing?month=$m" "$R/reports/by-vendor?month=$m" "$R/reports/by-product?month=$m" "$R/reports/by-category?month=$m" \
          "$R/reports/top-products?month=$m" "$R/reports/returns?month=$m" "$R/reports/transactions?month=$m" "$R/reports/trend?month=$m" \
          "$R/vendors?month=$m" "$R/vendor-profile?vendor=<template vendor>&month=$m" "$R/vendors/compare?vendors=<template vendor>,<K2>&month=$m" \
          "$R/documents?month=$m&vendor=<template vendor>"; do
  printf '%-70s %s\n' "$ep" "$(curl -s $M "localhost:8080${ep#localhost:8080}" | jq -c '.dataQuality | if . == null then null else [.affectedRecords, [.issueTypes[].type]] end')"
done
curl -s $M "$R/documents/<a seeded documentId>/lines" | jq -c .dataQuality
```

Pass: every response has a non-null `dataQuality` whose types are a subset of that report's mapped types (research R13) — e.g. `trend` never lists `unexpected-quantities` or `broken-relationships`; `overview` never lists `unexpected-quantities`; counts match a SQL check of the seeded records in that scope. UI: each page shows the Persian notice S-NOTICE above the figures, followed by each issue label and count.

### D2 — No notice outside the scope (US5 AC2, edge "flagged outside the period")

Same endpoints for a month whose baseline notice was null in C0 (e.g. the month before `m` if clean), and `by-vendor?month=$m&vendor=<a vendor with no flagged records>` → `dataQuality: null`. Records 9–10 (dated 1499 and 1350) never create a notice for month `m`.

### D3 — Only mapped types trigger a notice (US5 AC6)

```bash
curl -s $M "$R/reports/trend?month=$m&product=<qProd>" | jq -c .dataQuality       # null (only unexpected-quantities in scope; not mapped to trend)
curl -s $M "$R/reports/by-vendor?month=$m&product=<qProd>" | jq -c .dataQuality   # [unexpected-quantities] count 2
```

(`<qProd>` = the product key used by records 11–12; find it with `psql_q "select product_source_key from mabna.purchase_line l join data_quality_issue i on i.line_id=l.line_id where i.issue_type='unexpected-quantities'"`.)

### D3b — Comparison period is in scope (US5 AC7, FR-027)

Let `m1` be the month after `m` (with synced purchases, and whose baseline notice in C0-style checks is null for the by-vendor report).

```bash
curl -s $M "$R/reports/by-vendor?month=$m1" | jq -c .dataQuality                  # null (no flagged record in m1)
curl -s $M "$R/reports/by-vendor?month=$m1&compare=true" | jq -c .dataQuality     # non-null: previous period m holds the seeded records
curl -s $M "localhost:8080/api/overview/purchasing?month=$m1" | jq -c '{c:(.comparison != null), dq:.dataQuality}'   # comparison shown → notice non-null
```

### D4 — Print carries the notice (US5 AC4, FR-028)

```bash
curl -s $M "$R/reports/by-vendor/print?month=$m" | jq -c .dataQuality              # same as the screen response
```

UI: Print the by-vendor report and the vendor comparison for month `m` → the printout shows the notice under the header.

### D5 — Link for Admin only (US5 AC3)

UI as Admin: the notice has a link S-NOTICE-LINK («مشاهده در کیفیت داده») that opens `#/settings/data-quality?types=…` with those types highlighted. As management: same notice text, no link.

### D5b — Notice check unavailable (US5 AC8, FR-027)

Hold a lock that blocks the notice query past its 15 s timeout:

```bash
docker compose -f deploy/docker-compose.yml exec -T db psql -U zharfa -d zharfa -c "begin; lock table data_quality_issue in access exclusive mode; select pg_sleep(25); commit;" &
sleep 1; time curl -s $M "$R/reports/by-vendor?month=$m" | jq -c '{rows:(.rows|length), dq:.dataQuality}'
wait
```

Pass: 200 with the usual rows and `dataQuality = {"unavailable":true,"issueTypes":[],"affectedRecords":0}`; the page shows the neutral note S-NOTICE-UNAVAILABLE (no amber warning, no link); the api log has one warning naming only the exception type. After the lock ends the same request shows the normal notice again.

### D6 — Demo label and Persian (FR-029)

Data quality, Users and roles, Audit log pages and the notice show RTL Persian, Jalali dates, Persian digits and the demo-data label/banner.

---

## E. Regression

- Phase 2 quickstart P1–P8 as `manager` (now `management`) and once as `purchasing`: unchanged results (plus `dataQuality`).
- Phase 1 quickstart sync flows (Sync now, scheduled run, failure) unchanged; a sync with zero changed rows still creates a new `data_quality_run` (`psql_q "select id, sync_job_id, status from data_quality_run order by id desc limit 3"`).
- Demo pages: visible for every role in demo; the AI chat is hidden for Admin (no `ai.query`) and works for management (demo).
- Production-mode stack: `/api/ai/chat` → 403 for Admin, 404 for management (DemoOnly after authorization).
