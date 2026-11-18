# Quickstart: Vendor and Purchasing Reports (Release 1, Phase 2)

Validation guide for the tester and the PR walkthrough. Endpoints and payloads: [contracts/api.md](contracts/api.md). Measures and invariants I1–I5: [data-model.md §3](data-model.md). Phase 1 setup and helpers are reused from `specs/003-real-purchase-data/quickstart.md` §0.

## 0. Prerequisites

- Phase 1 quickstart §0 done (simulator up, `deploy/.env` with `ZHARFA_DATA_MODE=demo`, dev override compose, `login`, `A`, `M`, `psql_q` helpers).
- Simulator scenario (gitignored `deploy/mabna-sim/generated/scenario.py`) regenerated with the Phase 2 additions (research R7): `SimDrugGroup` master, `SimDrugGroupID` on products, `SimVendorCode` on vendors; a vendor named with Arabic ي/ك, a vendor name with a ZWNJ, two vendors with the same normalized name and different codes, a product without a category, a product whose category key is missing from the master, at least three years of documents for one test month span, and a return dated in a later month than its invoice.
- `deploy/mabna-sim/generated/purchase-source-map.json` and `allowlist.txt` include the Phase 2 entries ([contracts/source-map-delta.md](contracts/source-map-delta.md)).
- Stack rebuilt (`docker compose ... up --build -d`), migration `0007` applied (API start), then **Sync now** as Admin (Phase 1 Flow 2) until the job is `succeeded`.

```bash
R=localhost:8080/api/purchasing
```

## 1. Validation suite (no DB, no network)

`.ainative/project.yaml` `validation_commands` (frontend build, API and desktop compile, `dotnet test`). Pass: all green, including the new tests of research R17.

## Flow P0 — Phase 1 figures unchanged by the range base (FR-002, research R1)

Before applying `0007` (on the previous build) save `curl -s $M "localhost:8080/api/overview/purchasing?month=<m>" | jq -S '{totals,topVendors,topProducts,trend}'` for two months (one with document discounts if basis `lineAndDocument` is exercised). After `0007` repeat: outputs are identical. Run the Phase 1 reconciliation CLI (spec 003 Flow 7): exit 0, every metric `pass`. `psql_q "select * from mabna.fn_sync_fingerprint()"` lists the new `product_category` row; a second "Sync now" with no source change leaves every hash unchanged.

## Flow P1 — Vendor search (US1 AC1–AC2, FR-009/FR-010, SC-001)

```bash
curl -s $M "$R/vendors?q=<name with ی/ک>" | jq '.totalRows,.rows[].name'
curl -s $M "$R/vendors?q=<same name with ي/ك>" | jq '.totalRows,.rows[].name'
curl -s $M "$R/vendors?q=<name without the ZWNJ, with extra spaces>" | jq '.rows[].name'
curl -s $M "$R/vendors?q=<code in Persian digits>" | jq '.rows[] | {name,code}'
curl -s $M "$R/vendors?q=zzzz-no-match" | jq '.totalRows,.rows'          # 0, []
curl -s $M "$R/vendors?sort=name&dir=asc&page=999" | jq '.page'            # clamped to last page
```

Pass: both spellings return the same vendor(s); the two same-name vendors both appear with different codes; code search works with Persian and Latin digits; no-match is an empty list, not an error. SC-001: a SQL count over the scenario vendors whose name has ی/ک, each searched with ي/ك via the API, finds 100%.

## Flow P2 — Vendor profile equals the Overview (US1 AC3–AC7, FR-011/FR-012, SC-002)

For month *m*, take `topVendors[].key` from the Overview, then for each:

```bash
curl -s $M "$R/vendor-profile?vendor=<key>&month=<m>" | jq '.totals.net'
```

Pass: equals the Overview `net` exactly for all five (I2). The profile shows gross, returns, VAT separately, comparison with the previous month, 12 trend points, ≤ 10 top products, returns, documents with last-sync time. Change `month` → every block changes. A vendor with no purchases in *m* → zeros and an empty document list (not unavailable). `jq 'paths | map(tostring) | join(".")' | grep -Ei 'balance|payment|debt|credit|paid'` → no output (FR-012). `vendor=~unknown` → 400; unknown key → 404.

## Flow P3 — Seven reports, totals and invariants (US2, FR-013/FR-014/FR-019, SC-003)

Each request sends only the parameters that report accepts (data-model §4 catalog, T022/T023): `vendor` on every report except `by-vendor` (which takes `product` instead); `q` on all seven, with text matching that report's search target; `sort` from that report's allowlist.

```bash
for id in by-vendor by-product by-category trend top-products returns transactions; do
  case $id in
    by-vendor)    f="product=<p>";  q="<vendor name or code>";      s="name"   ;;
    by-product)   f="vendor=<key>"; q="<product name>";             s="name"   ;;
    by-category)  f="vendor=<key>"; q="<category name>";            s="name"   ;;
    trend)        f="vendor=<key>"; q="<vendor or product name>";   s="net"    ;;
    top-products) f="vendor=<key>"; q="<product name>";             s="quantity" ;;
    returns)      f="vendor=<key>"; q="<vendor or product name>";   s="vendor" ;;
    transactions) f="vendor=<key>"; q="<document number>";          s="number" ;;
  esac
  curl -s $M "$R/reports/$id?month=<m>" | jq '{totalRows,totals,first:.rows[0]}'
  curl -s $M "$R/reports/$id?month=<m>&$f&q=$q&sort=$s&dir=asc&page=2&pageSize=25" | jq '{page,totalRows,totals}'
done
curl -s $M "$R/reports/trend?period=custom&from=<y-2>-01-01&to=<y>-12-29&q=<product name>&page=2&pageSize=25" | jq '{page,totalRows,n:(.rows|length),totals}'   # 36 buckets (<y> = last complete synced year): page 2 holds 11
curl -s -o /dev/null -w '%{http_code}\n' $M "$R/reports/by-vendor?month=<m>&vendor=<key>"                              # 400: vendor not applicable
curl -s -o /dev/null -w '%{http_code}\n' $M "$R/reports/transactions?month=<m>&compare=true"                           # 400: no comparison on transactions
curl -s -o /dev/null -w '%{http_code}\n' $M "$R/reports/by-category?month=<m>&category=x&vendor=<key>&product=<p>"   # 400: product not applicable
curl -s -o /dev/null -w '%{http_code}\n' $M "$R/reports/by-vendor?month=<m>&sort=drop_table"                         # 400
```

Pass:
- `totals.net` of by-vendor, by-product, by-category = Overview `totals.net` for *m* (I1); with a vendor filter, by-product `totals.net` = that vendor's profile net.
- Direct SQL agrees: `psql_q "select sum(sign*line_amount) from mabna.fn_purchase_lines(<from>,<to>,'line') where vendor_source_key='<key>'"` = filtered `totals.net`.
- `totals`/`totalRows` are identical across pages 1 and 2 of the same filter; sorting by each allowed key changes order only.
- Trend: 12 rows ending at *m*; months before the first synced month `available:false`. With `q`, each bucket and `totals.net` = direct SQL over `fn_purchase_lines` restricted to lines whose vendor or product `search_key` contains `fn_search_key(q)`; the 36-month custom range pages 25 + 11 rows with identical `totals`/`totalRows` on both pages.
- Top-products: default ranked by net; `sort=quantity` re-ranks.
- Returns: return dated in a later month than its invoice is counted in the return's month.
- Transactions: every non-cancelled invoice and return of *m*; `preDiscount − discount = net` per row (I5); cancelled scenario documents absent.
- «نامشخص» rows appear for the scenario's unknown vendor/product/category and their amounts are included in totals.
- Quantity headers state the recorded unit; `quantityMissingLines` reported.

## Flow P4 — Drill-down sums (US3, FR-015, SC-004)

For one row each of by-vendor, by-product, by-category, trend (with and without `q`; a searched trend row's `drill` carries `match`) and returns (and the «نامشخص» rows):

```bash
curl -s $M "$R/documents?month=<m>&<row.drill as params>&pageSize=25&page=1" | jq '{totalRows,totals}'
curl -s $M "$R/documents?month=<m>&<row.drill>&page=2" | jq '.totals'                       # same totals
curl -s $M "$R/documents/<documentId>/lines?<row.drill product/category>" | jq '.totals'
```

Pass: row `net` = drill-down `totals.net` and row `netQuantity` = `totals.netQuantity` exactly (I3); a returns row's `returns` = `totals.returnAmount`; a document's in-scope `signedNet` = its lines' `totals.signedAmount` (I4); the unknown rows list the lines with missing/unmatched references.

## Flow P5 — Vendor comparison (US4, FR-016/FR-017, SC-005)

```bash
curl -s $M "$R/vendors/compare?vendors=K1,K2,K3&period=custom&from=<6 months>&to=<…>" | jq '.months|length, (.vendors[]|{key,net,netQuantity,documentCount,returns,t:(.trend|length)})'
curl -s $M "$R/vendor-profile?vendor=K1&period=custom&from=…&to=…" | jq '.totals'          # equal measures
curl -s -w '%{http_code}\n' $M "$R/vendors/compare?vendors=K1,K2,K3,K4,K5,K6&month=<m>"    # 400, limit message
curl -s -w '%{http_code}\n' $M "$R/vendors/compare?vendors=K1,K1&month=<m>"                 # 400, minimum message (dedupe)
```

UI: the add control is disabled at five with the limit stated; one vendor → run button disabled with the minimum stated.

## Flow P6 — Periods and comparison (US5 AC1–AC2, FR-018/FR-020, SC-006)

```bash
curl -s $M "$R/reports/by-vendor?period=month&month=1405-01&compare=true" | jq '.period,.previousPeriod'     # 1404-12
curl -s $M "$R/reports/by-vendor?period=quarter&quarter=1405-Q1&compare=true" | jq '.previousPeriod'         # 1404-Q4
curl -s $M "$R/reports/by-vendor?period=year&year=1404&compare=true" | jq '.previousPeriod'                  # 1403 (1403 is leap: Esfand 30)
curl -s $M "$R/reports/by-vendor?period=custom&from=1405-07-10&to=1405-08-24&compare=true" | jq '.period,.previousPeriod'  # 45 days; prev = 1405-05-27..1405-07-09
curl -s -w '%{http_code}\n' $M "$R/reports/by-vendor?period=custom&from=1405-08-10&to=1405-08-01"          # 400
curl -s -w '%{http_code}\n' $M "$R/reports/by-vendor?period=custom&from=1404-12-30&to=1404-12-30"          # 400 (1404 not leap)
curl -s $M "$R/reports/by-vendor?period=custom&from=<today-5>&to=<today+10>" | jq '.period.trimmedTo'        # today
```

Pass: previous periods exactly as commented; rows carry `previousNet`, `delta`, `percent` (`percent: null` for a zero base); a period whose previous period starts before the first synced month reports `no-previous-data`. Unit tests in `JalaliPeriodTests` cover the same cases without a DB.

## Flow P7 — Availability and data modes (FR-004/FR-005/FR-008)

1. Demo mode with the category items `candidate`: by-category works (no «داده در دسترس نیست»).
2. Set `productCategory.status` to `unknown` in the dev map, restart `api`: by-category returns `unavailable:[{figure:"category",reason:"source-item-unverified"}]`, category filter/lookup unavailable, other reports unaffected.
3. Production-mode throwaway stack (Phase 1 Flow 10 setup) with `candidate` items: category and vendor code unavailable, directory searches by name only.
4. Fresh DB, no successful sync: every report and the profile return `no-successful-sync`; UI says no sync has completed.
5. Every response carries `dataLabel` (`demo` here) and `lastSuccessfulSync`; pages show the demo banner/label.

## Flow P8 — Printing (US5 AC3–AC4, FR-021, SC-007)

UI (Chrome and the WebView2 desktop shell on Windows): open by-product for a quarter with a vendor filter, comparison on, result > 25 and < 1,000 rows → Print. Pass: Persian RTL A4 portrait, no clipped columns, header repeated per page, every row of the filtered result in the current sort, totals, report name, both periods, every active filter, last-sync time, data label; no navigation chrome. Transactions report prints in landscape. Unfiltered transactions for a year with > 1,000 documents:

```bash
curl -s $M "$R/reports/transactions/print?period=year&year=1404" | jq '{totalRows,truncated,limit,n:(.rows|length),totals}'
```

Pass: `n` = 1000, `truncated: true`, `totals` cover all `totalRows`; the printout states «۱٬۰۰۰ از N ردیف» and asks to narrow filters.

Vendor comparison print (FR-021): on the comparison page with three vendors over six months → Print.

```bash
curl -s $M "$R/vendors/compare/print?vendors=K1,K2,K3&period=custom&from=<6 months>&to=<…>" | jq '{truncated,v:(.vendors|length),m:(.months|length)}'
```

Pass: same figures as `vendors/compare`, `truncated: false`; the printout shows the five measures per vendor and the monthly trend table, the period, the selected vendors, last-sync time and data label, Persian RTL, no clipped columns.

## Flow P9 — Performance (FR-022, SC-008)

Throwaway database filled with `db/perf/purchasing-3y.sql` (extended for categories and vendor codes). For the directory, profile, each report (year preset and a 1,098-day custom range), documents and compare: 20 requests each, p95 < 2 s.

```bash
for i in $(seq 20); do curl -s -o /dev/null -w '%{time_total}\n' $M "$R/reports/by-product?period=year&year=1404"; done | sort -n | sed -n 19p
```

Timed task (SC-008): a purchasing user produces a filtered vendor purchase report and prints it in under 1 minute from sign-in.

## Flow P10 — Authorization and keyboard (FR-007, FR-023)

`curl -s -o /dev/null -w '%{http_code}' "$R/vendors"` → 401 without a cookie; 200 as manager and admin. Keyboard-only: reach every filter, sort header (`aria-sort` updates), page control, drill-down (opens a dialog, Escape closes, focus returns), print. Contrast spot-checked with the browser accessibility checker (AA).

## Exit-criteria map (`zharfa-prd.phases.md#2`)

| Exit criterion | Flows |
|---|---|
| Vendor search finds vendors regardless of ی/ک spelling | P1 |
| Vendor profile figures equal the Overview for the same vendor and month | P2, P0 |
| All standard purchasing reports open, filter, sort, search and paginate | P3 |
| Drill-down from a report row reaches the documents and the sum equals the row | P4 |
| Vendor comparison of up to five vendors | P5 |
| Period comparison shows the previous period (clarified rule) | P6 |
| Browser-print yields a readable Persian RTL page stating period and filters | P8 |
