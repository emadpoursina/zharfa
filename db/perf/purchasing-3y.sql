-- db/perf/purchasing-3y.sql — synthetic load generator for the Overview latency check (spec 003, T066, quickstart Flow 8).
--
-- !! NEVER RUN THIS AGAINST A REAL INSTALL. Throwaway database only. !!
--
-- What it does: fills 36 Jalali months (1402-09 .. 1405-08) of purchase invoices and returns into the
-- mabna.* synced copy (product_category, vendor, product, purchase_document, purchase_line). Nothing else is
-- touched: no sync_job, no source_record, no audit_log, no install_setting.
-- Synthetic data only: every key starts with 'PERF-' and every name is generic ("تأمین‌کننده نمونه 12").
-- It contains no source data. Dates are approximate (±1 day from the Jalali day); the Overview functions
-- use the jalali_year / jalali_month / jalali_day columns, so the figures do not depend on doc_date.
-- Phase 2 (spec 004, T072): also fills mabna.product_category (12 synthetic drug groups), product.drug_group_key
-- (about 1 in 25 products left uncategorized, about 1 in 40 pointing at a key missing from the master, so both
-- fall into the unknown category bucket) and vendor.code ('PERF-C' + number, every vendor, so the vendor
-- directory search has a code to match).
--
-- Guard: the script refuses to run when the mabna tables already hold rows, so it cannot double-load.
-- It does not write sync_job. To see Overview figures, insert one succeeded job on the throwaway database, e.g.
--   INSERT INTO sync_job (job_name, status, started_at, finished_at, trigger, mode, duration_ms)
--   VALUES ('purchasing', 'succeeded', now(), now(), 'manual', 'full', 0);
-- Run (throwaway database only):
--   docker exec -i <postgres-container> psql -v ON_ERROR_STOP=1 -U <user> -d <throwaway-db> < db/perf/purchasing-3y.sql
-- Volume: 36 months x (150 invoices + 15 returns) x 3 lines, about 5,900 documents and 17,800 lines.

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM mabna.purchase_document) OR EXISTS (SELECT 1 FROM mabna.vendor)
       OR EXISTS (SELECT 1 FROM mabna.product) OR EXISTS (SELECT 1 FROM mabna.product_category) THEN
        RAISE EXCEPTION 'purchasing-3y: mabna tables already hold rows; run only on an empty throwaway database';
    END IF;
END $$;

-- 1. Drug groups, vendors and products (synthetic master data).
INSERT INTO mabna.product_category (source_key, name_fa, synced_at)
SELECT 'PERF-G' || lpad(g::text, 2, '0'), 'گروه نمونه ' || g, now()
FROM generate_series(1, 12) AS g;

INSERT INTO mabna.vendor (source_key, name_fa, code, is_active_source, synced_at)
SELECT 'PERF-V' || lpad(g::text, 4, '0'), 'تأمین‌کننده نمونه ' || g, 'PERF-C' || lpad(g::text, 4, '0'), true, now()
FROM generate_series(1, 120) AS g;

-- drug_group_key: 1 in 25 products has no group (NULL, unknown bucket); 1 in 40 names a key that is not in the
-- master (falls into the unknown bucket, as research R7 requires); the rest map to one of the 12 groups.
INSERT INTO mabna.product (source_key, name_fa, drug_group_key, synced_at)
SELECT 'PERF-P' || lpad(g::text, 4, '0'),
       'کالای نمونه ' || g,
       CASE WHEN g % 25 = 0 THEN NULL
            WHEN g % 40 = 0 THEN 'PERF-G99'
            ELSE 'PERF-G' || lpad((1 + (g * 5) % 12)::text, 2, '0')
       END,
       now()
FROM generate_series(1, 400) AS g;

-- 2. Document headers: 36 months ending 1405-08, invoices and returns, with an approximate Gregorian date.
CREATE TEMP TABLE perf_docs AS
WITH periods AS (
    SELECT (1405 * 12 + 7 - (35 - m)) / 12 AS jy,
           (1405 * 12 + 7 - (35 - m)) % 12 + 1 AS jm
    FROM generate_series(0, 35) AS m
),
kinds AS (
    SELECT 'invoice'::text AS doc_type, 150 AS n
    UNION ALL
    SELECT 'return'::text, 15
)
SELECT p.jy::smallint AS jy,
       p.jm::smallint AS jm,
       (1 + (g.i % 28))::smallint AS jday,
       k.doc_type,
       g.i,
       'PERF-' || k.doc_type || '-' || p.jy || '-' || p.jm || '-' || g.i AS source_key,
       CASE WHEN (g.i + p.jm) % 10 = 0 THEN NULL
            ELSE 'PERF-V' || lpad((1 + (g.i * 7 + p.jm * 3 + p.jy) % 120)::text, 4, '0')
       END AS vendor_key,
       (make_date(p.jy + 621, 3, 21)
         + (31 * least(p.jm - 1, 6) + 30 * greatest(least(p.jm - 1, 11) - 6, 0))
         + (g.i % 28))::date AS doc_date,
       (g.i % 50 = 0) AS cancelled
FROM periods p
CROSS JOIN kinds k
CROSS JOIN LATERAL generate_series(1, k.n) AS g(i);

INSERT INTO mabna.purchase_document (source_key, doc_type, doc_number, vendor_source_key, vendor_id,
                                     doc_date, jalali_year, jalali_month, jalali_day, cancelled,
                                     doc_discount, synced_at)
SELECT d.source_key,
       d.doc_type,
       d.i::text,
       d.vendor_key,
       v.vendor_id,
       d.doc_date,
       d.jy,
       d.jm,
       d.jday,
       d.cancelled,
       0,
       now()
FROM perf_docs d
LEFT JOIN mabna.vendor v ON v.source_key = d.vendor_key;

-- 3. Lines: three per document. Amounts in Rial; 5 % line discount; 9 % VAT on the net amount.
-- About 2 % of lines have no product (the unknown product bucket).
INSERT INTO mabna.purchase_line (document_id, source_key, product_source_key, product_id, quantity,
                                 gross_amount, line_discount, vat_amount)
SELECT pd.document_id,
       'L' || x.j,
       x.product_key,
       pr.product_id,
       x.quantity,
       x.amount,
       x.discount,
       round((x.amount - x.discount) * 0.09, 4)
FROM (
    SELECT s.doc_type, s.source_key, s.j, s.product_key, s.quantity, s.amount,
           round(s.amount * 0.05, 4) AS discount
    FROM (
        SELECT d.doc_type,
               d.source_key,
               l.j,
               CASE WHEN (d.i + l.j) % 50 = 0 THEN NULL
                    ELSE 'PERF-P' || lpad((1 + (d.i * 13 + l.j * 29 + d.jm) % 400)::text, 4, '0')
               END AS product_key,
               (1 + d.i % 9)::numeric(24,4) AS quantity,
               (1000000 + (d.i * 7919 + l.j * 104729 + d.jm * 31) % 900000)::numeric(24,4) AS amount
        FROM perf_docs d
        CROSS JOIN LATERAL generate_series(1, 3) AS l(j)
    ) s
) x
JOIN mabna.purchase_document pd ON pd.doc_type = x.doc_type
                               AND pd.source_key = x.source_key
LEFT JOIN mabna.product pr ON pr.source_key = x.product_key;

ANALYZE mabna.product_category;
ANALYZE mabna.vendor;
ANALYZE mabna.product;
ANALYZE mabna.purchase_document;
ANALYZE mabna.purchase_line;

SELECT count(*) AS drug_groups FROM mabna.product_category;
SELECT count(*) AS vendors FROM mabna.vendor;
SELECT count(*) AS products FROM mabna.product;
SELECT count(*) AS documents FROM mabna.purchase_document;
SELECT count(*) AS lines FROM mabna.purchase_line;

DROP TABLE perf_docs;
