-- db/perf/data-quality-50k.sql — synthetic data quality run with ~50 000 issues (spec 005, T111, quickstart Flow B7, SC-008).
--
-- !! NEVER RUN THIS AGAINST A REAL INSTALL. Throwaway database only. !!
-- Needs at least one succeeded sync job in the database.
--
-- What it does: inserts one data_quality_run (status='succeeded', checked_at=now(),
-- sync_job_id = newest succeeded sync_job) and ~50 000 data_quality_issue rows via
-- generate_series, spread over seven issue types and three record kinds, cycling
-- real ids from mabna.purchase_line / purchase_document / product with row_number()
-- and modulo so unique (run_id, issue_type, record_ref, field) holds.
-- Field and reason codes follow data-model.md §3.1.
-- No real names are included.
--
-- Used by quickstart Flow B7 to time /api/admin/data-quality and reports with a notice
-- (compare=true) under load.

WITH target_sync AS (
    SELECT id AS sync_job_id
    FROM sync_job
    WHERE status = 'succeeded'
    ORDER BY id DESC
    LIMIT 1
),
new_run AS (
    INSERT INTO data_quality_run (sync_job_id, started_at, checked_at, status)
    SELECT sync_job_id, now(), now(), 'succeeded'
    FROM target_sync
    RETURNING id
),
doc_pool AS (
    SELECT
        document_id,
        row_number() OVER (ORDER BY document_id) - 1 AS rn,
        count(*) OVER () AS total
    FROM mabna.purchase_document
),
line_pool AS (
    SELECT
        line_id,
        document_id,
        product_id,
        row_number() OVER (ORDER BY line_id) - 1 AS rn,
        count(*) OVER () AS total
    FROM mabna.purchase_line
),
prod_pool AS (
    SELECT
        product_id,
        row_number() OVER (ORDER BY product_id) - 1 AS rn,
        count(*) OVER () AS total
    FROM mabna.product
),
series AS (
    SELECT g FROM generate_series(1, 50000) AS g
),
items AS (
    SELECT
        r.id AS run_id,
        s.g,
        (s.g - 1) % 14 AS slot,
        (s.g - 1) / 14 AS slot_idx
    FROM series s
    CROSS JOIN new_run r
),
mapped AS (
    SELECT
        i.run_id,
        CASE i.slot
            WHEN 0 THEN 'missing-values'
            WHEN 1 THEN 'missing-values'
            WHEN 2 THEN 'duplicate-records'
            WHEN 3 THEN 'broken-relationships'
            WHEN 4 THEN 'broken-relationships'
            WHEN 5 THEN 'broken-relationships'
            WHEN 6 THEN 'inconsistent-product-ids'
            WHEN 7 THEN 'invalid-dates'
            WHEN 8 THEN 'unexpected-quantities'
            WHEN 9 THEN 'inconsistent-amounts'
            WHEN 10 THEN 'inconsistent-amounts'
            WHEN 11 THEN 'inconsistent-amounts'
            WHEN 12 THEN 'missing-values'
            WHEN 13 THEN 'missing-values'
        END AS issue_type,
        CASE i.slot
            WHEN 0 THEN 'line'
            WHEN 1 THEN 'document'
            WHEN 2 THEN 'document'
            WHEN 3 THEN 'line'
            WHEN 4 THEN 'document'
            WHEN 5 THEN 'product'
            WHEN 6 THEN 'product'
            WHEN 7 THEN 'document'
            WHEN 8 THEN 'line'
            WHEN 9 THEN 'line'
            WHEN 10 THEN 'line'
            WHEN 11 THEN 'document'
            WHEN 12 THEN 'line'
            WHEN 13 THEN 'document'
        END AS record_kind,
        CASE i.slot
            WHEN 0 THEN 'quantity'
            WHEN 1 THEN 'number'
            WHEN 2 THEN 'number'
            WHEN 3 THEN 'product'
            WHEN 4 THEN 'vendor'
            WHEN 5 THEN 'category'
            WHEN 6 THEN 'name'
            WHEN 7 THEN 'date'
            WHEN 8 THEN 'quantity'
            WHEN 9 THEN 'discount'
            WHEN 10 THEN 'vat'
            WHEN 11 THEN 'document-discount'
            WHEN 12 THEN 'product'
            WHEN 13 THEN 'vendor'
        END AS field,
        CASE i.slot
            WHEN 0 THEN 'missing'
            WHEN 1 THEN 'missing'
            WHEN 2 THEN 'duplicate-number'
            WHEN 3 THEN 'not-in-master'
            WHEN 4 THEN 'not-in-master'
            WHEN 5 THEN 'not-in-master'
            WHEN 6 THEN 'duplicate-name'
            WHEN 7 THEN 'future'
            WHEN 8 THEN 'non-positive'
            WHEN 9 THEN 'discount-exceeds-gross'
            WHEN 10 THEN 'negative-vat'
            WHEN 11 THEN 'doc-discount-exceeds-lines'
            WHEN 12 THEN 'missing'
            WHEN 13 THEN 'missing'
        END AS reason,
        i.slot_idx
    FROM items i
)
INSERT INTO data_quality_issue (
    run_id,
    issue_type,
    record_kind,
    record_ref,
    field,
    reason,
    document_id,
    line_id,
    product_id
)
SELECT
    m.run_id,
    m.issue_type,
    m.record_kind,
    CASE m.record_kind
        WHEN 'line' THEN
            'line:' || l.line_id || CASE WHEN m.slot_idx / GREATEST(l.total, 1) > 0 THEN '-' || (m.slot_idx / GREATEST(l.total, 1))::text ELSE '' END
        WHEN 'document' THEN
            'document:' || d.document_id || CASE WHEN m.slot_idx / GREATEST(d.total, 1) > 0 THEN '-' || (m.slot_idx / GREATEST(d.total, 1))::text ELSE '' END
        WHEN 'product' THEN
            'product:' || p.product_id || CASE WHEN m.slot_idx / GREATEST(p.total, 1) > 0 THEN '-' || (m.slot_idx / GREATEST(p.total, 1))::text ELSE '' END
    END AS record_ref,
    m.field,
    m.reason,
    CASE m.record_kind
        WHEN 'line' THEN l.document_id
        WHEN 'document' THEN d.document_id
        ELSE NULL
    END AS document_id,
    CASE m.record_kind
        WHEN 'line' THEN l.line_id
        ELSE NULL
    END AS line_id,
    CASE m.record_kind
        WHEN 'line' THEN l.product_id
        WHEN 'product' THEN p.product_id
        ELSE NULL
    END AS product_id
FROM mapped m
LEFT JOIN doc_pool d ON m.record_kind = 'document' AND d.rn = (m.slot_idx % GREATEST(d.total, 1))
LEFT JOIN line_pool l ON m.record_kind = 'line' AND l.rn = (m.slot_idx % GREATEST(l.total, 1))
LEFT JOIN prod_pool p ON m.record_kind = 'product' AND p.rn = (m.slot_idx % GREATEST(p.total, 1))
ON CONFLICT (run_id, issue_type, record_ref, field) DO NOTHING;

ANALYZE data_quality_run;
ANALYZE data_quality_issue;
