-- 0006_purchasing_kpis.sql — Purchasing metric functions and the sync fingerprint (Release 1, Phase 1)
-- Forward-only, idempotent migration (CREATE OR REPLACE). Requires 0005_purchasing_sync.sql.

-- 1. mabna.fn_sync_fingerprint(): one row per synced table with a row count and an md5 over the
-- business key and amount columns, in key order. Surrogate ids and synced_at are excluded, so a
-- database filled incrementally and one filled by a full load compare equal when the data is equal
-- (quickstart Flow 6). STABLE, read-only, SECURITY INVOKER.
CREATE OR REPLACE FUNCTION mabna.fn_sync_fingerprint()
RETURNS TABLE (table_name text, row_count bigint, content_hash text)
LANGUAGE sql
STABLE
SECURITY INVOKER
AS $$
    SELECT 'vendor'::text,
           count(*),
           md5(coalesce(string_agg(concat_ws('|', source_key, name_fa, is_active_source::text), E'\n' ORDER BY source_key), ''))
    FROM mabna.vendor
    UNION ALL
    SELECT 'product'::text,
           count(*),
           md5(coalesce(string_agg(concat_ws('|', source_key, name_fa), E'\n' ORDER BY source_key), ''))
    FROM mabna.product
    UNION ALL
    SELECT 'purchase_invoice'::text,
           count(*),
           md5(coalesce(string_agg(concat_ws('|', source_key, doc_number, vendor_source_key, doc_date::text,
                                             cancelled::text, doc_discount::text),
                                   E'\n' ORDER BY source_key), ''))
    FROM mabna.purchase_document
    WHERE doc_type = 'invoice'
    UNION ALL
    SELECT 'purchase_return'::text,
           count(*),
           md5(coalesce(string_agg(concat_ws('|', source_key, doc_number, vendor_source_key, doc_date::text,
                                             cancelled::text, doc_discount::text),
                                   E'\n' ORDER BY source_key), ''))
    FROM mabna.purchase_document
    WHERE doc_type = 'return'
    UNION ALL
    SELECT 'purchase_line'::text,
           count(*),
           md5(coalesce(string_agg(concat_ws('|', d.doc_type, d.source_key, l.source_key, l.product_source_key,
                                             l.quantity::text, l.gross_amount::text, l.line_discount::text,
                                             l.vat_amount::text),
                                   E'\n' ORDER BY d.doc_type, d.source_key, l.source_key), ''))
    FROM mabna.purchase_line l
    JOIN mabna.purchase_document d ON d.document_id = l.document_id
$$;

-- 2. mabna.fn_purchase_line_amounts(): the line amounts of one Jalali month (data-model.md §4, research R9).
-- Documents: non-cancelled purchase documents of (p_year, p_month), optionally one vendor (source key).
-- Line amount = gross_amount − line_discount. Basis lineAndDocument also takes the line's share of the
-- document discount: the discount is allocated over every line of its document by line amount, in units
-- of 0.0001 with exact integer arithmetic (round half away from zero). The rounding remainder goes to
-- the largest line (ties: lowest line key, COLLATE "C"). The product filter applies after allocation.
-- The same rules are implemented independently in backend/Zharfa.Api/Purchasing/DiscountAllocator.cs.
-- Basis other than lineAndDocument is treated as line. STABLE, read-only, SECURITY INVOKER.
CREATE OR REPLACE FUNCTION mabna.fn_purchase_line_amounts(
    p_year smallint,
    p_month smallint,
    p_vendor_key text DEFAULT NULL,
    p_product_key text DEFAULT NULL,
    p_basis text DEFAULT 'line')
RETURNS TABLE (
    doc_type text,
    document_id bigint,
    line_id bigint,
    product_source_key text,
    line_amount numeric,
    vat_amount numeric)
LANGUAGE sql
STABLE
SECURITY INVOKER
AS $$
    WITH lines_all AS (
        SELECT d.document_id,
               d.doc_type,
               l.line_id,
               l.source_key AS line_key,
               l.product_source_key,
               l.vat_amount,
               ((l.gross_amount - l.line_discount) * 10000)::numeric AS amount_units,
               (d.doc_discount * 10000)::numeric AS discount_units
        FROM mabna.purchase_document d
        JOIN mabna.purchase_line l ON l.document_id = d.document_id
        WHERE d.jalali_year = p_year
          AND d.jalali_month = p_month
          AND NOT d.cancelled
          AND (p_vendor_key IS NULL OR d.vendor_source_key = p_vendor_key)
    ),
    ranked AS (
        SELECT la.*,
               sum(la.amount_units) OVER (PARTITION BY la.document_id) AS doc_units,
               row_number() OVER (PARTITION BY la.document_id
                                  ORDER BY la.amount_units DESC, la.line_key COLLATE "C") AS line_rank
        FROM lines_all la
    ),
    raw_shares AS (
        SELECT r.*,
               CASE WHEN p_basis = 'lineAndDocument' AND r.discount_units <> 0 AND r.doc_units <> 0
                    THEN sign(r.discount_units * r.amount_units) * sign(r.doc_units)
                         * div(2 * abs(r.discount_units * r.amount_units) + abs(r.doc_units),
                               2 * abs(r.doc_units))
                    ELSE 0 END AS raw_units
        FROM ranked r
    ),
    shares AS (
        SELECT w.*,
               CASE WHEN p_basis = 'lineAndDocument'
                    THEN w.raw_units
                         + CASE WHEN w.line_rank = 1
                                THEN w.discount_units - sum(w.raw_units) OVER (PARTITION BY w.document_id)
                                ELSE 0 END
                    ELSE 0 END AS share_units
        FROM raw_shares w
    )
    SELECT s.doc_type,
           s.document_id,
           s.line_id,
           s.product_source_key,
           ((s.amount_units - s.share_units) / 10000)::numeric(24,4),
           s.vat_amount
    FROM shares s
    WHERE p_product_key IS NULL OR s.product_source_key = p_product_key
$$;

-- 3. mabna.fn_purchase_totals(): gross purchases, returns, net purchases and VAT for one Jalali month and
-- optional vendor and product filters (data-model.md §4, FR-014/FR-015). One definition per metric:
--   gross = Σ line amount of invoices; returns = Σ line amount of returns;
--   net = gross − returns (excl. VAT, by document date; a return counts in its own month);
--   vat_invoice / vat_return = Σ VAT of invoices / returns; vat = vat_invoice − vat_return.
-- document_count counts the non-cancelled documents of the month that match the vendor filter.
-- STABLE, read-only, SECURITY INVOKER.
CREATE OR REPLACE FUNCTION mabna.fn_purchase_totals(
    p_year smallint,
    p_month smallint,
    p_vendor_key text DEFAULT NULL,
    p_product_key text DEFAULT NULL,
    p_basis text DEFAULT 'line')
RETURNS TABLE (
    gross numeric,
    returns numeric,
    net numeric,
    vat_invoice numeric,
    vat_return numeric,
    vat numeric,
    document_count bigint)
LANGUAGE sql
STABLE
SECURITY INVOKER
AS $$
    WITH amounts AS (
        SELECT * FROM mabna.fn_purchase_line_amounts(p_year, p_month, p_vendor_key, p_product_key, p_basis)
    )
    SELECT coalesce(sum(a.line_amount) FILTER (WHERE a.doc_type = 'invoice'), 0) AS gross,
           coalesce(sum(a.line_amount) FILTER (WHERE a.doc_type = 'return'), 0) AS returns,
           coalesce(sum(a.line_amount) FILTER (WHERE a.doc_type = 'invoice'), 0)
             - coalesce(sum(a.line_amount) FILTER (WHERE a.doc_type = 'return'), 0) AS net,
           coalesce(sum(a.vat_amount) FILTER (WHERE a.doc_type = 'invoice'), 0) AS vat_invoice,
           coalesce(sum(a.vat_amount) FILTER (WHERE a.doc_type = 'return'), 0) AS vat_return,
           coalesce(sum(a.vat_amount) FILTER (WHERE a.doc_type = 'invoice'), 0)
             - coalesce(sum(a.vat_amount) FILTER (WHERE a.doc_type = 'return'), 0) AS vat,
           (SELECT count(*)
            FROM mabna.purchase_document d
            WHERE d.jalali_year = p_year
              AND d.jalali_month = p_month
              AND NOT d.cancelled
              AND (p_vendor_key IS NULL OR d.vendor_source_key = p_vendor_key)) AS document_count
    FROM amounts a
$$;

-- 4. mabna.fn_first_data_month(): the earliest Jalali month with synced purchase documents (no row when the
-- copy is empty). Used by the Overview for "data unavailable" before the first synced month. STABLE.
CREATE OR REPLACE FUNCTION mabna.fn_first_data_month()
RETURNS TABLE (jalali_year smallint, jalali_month smallint)
LANGUAGE sql
STABLE
SECURITY INVOKER
AS $$
    SELECT d.jalali_year, d.jalali_month
    FROM mabna.purchase_document d
    ORDER BY d.jalali_year, d.jalali_month
    LIMIT 1
$$;

-- 5. mabna.fn_active_vendors(): distinct vendors with at least one non-cancelled document (invoice or return)
-- in the Jalali month. A vendor is identified by its source key, so a document whose key is missing from the
-- vendor master still counts as an active vendor; a document with no vendor key (the unknown bucket) does not.
-- STABLE, read-only, SECURITY INVOKER.
CREATE OR REPLACE FUNCTION mabna.fn_active_vendors(p_year smallint, p_month smallint)
RETURNS integer
LANGUAGE sql
STABLE
SECURITY INVOKER
AS $$
    SELECT count(DISTINCT d.vendor_source_key)::integer
    FROM mabna.purchase_document d
    WHERE d.jalali_year = p_year
      AND d.jalali_month = p_month
      AND NOT d.cancelled
      AND d.vendor_source_key IS NOT NULL
$$;

-- 6. mabna.fn_top_vendors() / mabna.fn_top_products(): net purchases (invoice line amounts minus return line
-- amounts, data-model.md §4) of one Jalali month grouped by vendor (document header) or product (line), highest
-- first, at most p_limit rows. A NULL vendor_id / product_id is the unknown bucket: key NULL, name «نامشخص»,
-- is_unknown true, and it can appear in the list. Groups sum to fn_purchase_totals().net. STABLE, SECURITY INVOKER.
CREATE OR REPLACE FUNCTION mabna.fn_top_vendors(
    p_year smallint,
    p_month smallint,
    p_limit int DEFAULT 5,
    p_basis text DEFAULT 'line')
RETURNS TABLE (
    key text,
    name_fa text,
    net numeric,
    is_unknown boolean)
LANGUAGE sql
STABLE
SECURITY INVOKER
AS $$
    WITH amounts AS (
        SELECT a.doc_type, a.line_amount, d.vendor_id
        FROM mabna.fn_purchase_line_amounts(p_year, p_month, NULL, NULL, p_basis) a
        JOIN mabna.purchase_document d ON d.document_id = a.document_id
    ),
    grouped AS (
        SELECT am.vendor_id,
               sum(CASE WHEN am.doc_type = 'invoice' THEN am.line_amount ELSE -am.line_amount END) AS group_net
        FROM amounts am
        GROUP BY am.vendor_id
    )
    SELECT v.source_key,
           coalesce(v.name_fa, 'نامشخص'),
           g.group_net,
           g.vendor_id IS NULL
    FROM grouped g
    LEFT JOIN mabna.vendor v ON v.vendor_id = g.vendor_id
    ORDER BY g.group_net DESC, v.source_key COLLATE "C" NULLS LAST
    LIMIT p_limit
$$;

CREATE OR REPLACE FUNCTION mabna.fn_top_products(
    p_year smallint,
    p_month smallint,
    p_limit int DEFAULT 5,
    p_basis text DEFAULT 'line')
RETURNS TABLE (
    key text,
    name_fa text,
    net numeric,
    is_unknown boolean)
LANGUAGE sql
STABLE
SECURITY INVOKER
AS $$
    WITH amounts AS (
        SELECT a.doc_type, a.line_amount, l.product_id
        FROM mabna.fn_purchase_line_amounts(p_year, p_month, NULL, NULL, p_basis) a
        JOIN mabna.purchase_line l ON l.line_id = a.line_id
    ),
    grouped AS (
        SELECT am.product_id,
               sum(CASE WHEN am.doc_type = 'invoice' THEN am.line_amount ELSE -am.line_amount END) AS group_net
        FROM amounts am
        GROUP BY am.product_id
    )
    SELECT p.source_key,
           coalesce(p.name_fa, 'نامشخص'),
           g.group_net,
           g.product_id IS NULL
    FROM grouped g
    LEFT JOIN mabna.product p ON p.product_id = g.product_id
    ORDER BY g.group_net DESC, p.source_key COLLATE "C" NULLS LAST
    LIMIT p_limit
$$;

-- 7. mabna.fn_purchase_trend(): the p_months Jalali months ending at (p_year, p_month), oldest first.
-- net = fn_purchase_totals().net for the month; a month with no purchases is 0. A month before the first month
-- with synced purchase documents (fn_first_data_month) has has_data = false and net NULL ("data unavailable").
-- STABLE, read-only, SECURITY INVOKER.
CREATE OR REPLACE FUNCTION mabna.fn_purchase_trend(
    p_year smallint,
    p_month smallint,
    p_months int DEFAULT 12,
    p_basis text DEFAULT 'line')
RETURNS TABLE (
    jalali_year smallint,
    jalali_month smallint,
    net numeric,
    has_data boolean)
LANGUAGE sql
STABLE
SECURITY INVOKER
AS $$
    WITH first_month AS (
        SELECT f.jalali_year * 12 + f.jalali_month - 1 AS first_index
        FROM mabna.fn_first_data_month() f
    ),
    window_months AS (
        SELECT s.idx / 12 AS y,
               s.idx % 12 + 1 AS m,
               s.i
        FROM (
            SELECT p_year * 12 + p_month - 1 - (p_months - 1 - g.i) AS idx,
                   g.i
            FROM generate_series(0, p_months - 1) AS g(i)
        ) s
    )
    SELECT w.y::smallint,
           w.m::smallint,
           CASE WHEN EXISTS (SELECT 1 FROM first_month fm WHERE fm.first_index <= w.y * 12 + w.m - 1)
                THEN (SELECT t.net FROM mabna.fn_purchase_totals(w.y::smallint, w.m::smallint, NULL, NULL, p_basis) t)
           END,
           EXISTS (SELECT 1 FROM first_month fm WHERE fm.first_index <= w.y * 12 + w.m - 1)
    FROM window_months w
    ORDER BY w.i
$$;
