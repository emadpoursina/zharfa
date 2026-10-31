-- 0002_kpi_views.sql — Semantic KPI views (parameterized functions) for Zharfa Demo 1.
-- Stock PostgreSQL 18 forward-only migration. Read-only; no extensions, no writes.

-- ============================================================================
-- 1. v_sales_overview
-- ============================================================================
-- Approved read-only KPI entry point ("view" in the product sense). Only the API/AI may call it.
-- sales_revenue = SUM(qty * unit_price - line_discount)
-- sales revenue is never cash received (cash comes from receipts; see v_receivables).
CREATE OR REPLACE FUNCTION v_sales_overview(
    p_from date,
    p_to date,
    p_prev_from date,
    p_prev_to date,
    p_category_id bigint DEFAULT NULL
)
RETURNS TABLE(
    period_revenue numeric,
    prev_period_revenue numeric,
    mom_delta numeric,
    mom_pct numeric,
    trend jsonb,
    top_products jsonb,
    top_customers jsonb
)
LANGUAGE plpgsql
SECURITY INVOKER
AS $$
DECLARE
    v_period_rev numeric(18,2);
    v_prev_period_rev numeric(18,2);
    v_mom_delta numeric(18,2);
    v_mom_pct numeric(18,2);
    v_trend jsonb;
    v_top_products jsonb;
    v_top_customers jsonb;
BEGIN
    -- Period revenue in half-open range [p_from, p_to)
    SELECT COALESCE(SUM(sii.qty * sii.unit_price - sii.line_discount), 0.00)
    INTO v_period_rev
    FROM sales_invoice si
    JOIN sales_invoice_item sii ON si.invoice_id = sii.invoice_id
    JOIN product p ON sii.product_id = p.product_id
    WHERE si.status = 'posted'
      AND si.issued_at >= p_from::timestamptz
      AND si.issued_at < p_to::timestamptz
      AND (p_category_id IS NULL OR p.category_id = p_category_id);

    -- Previous period revenue in half-open range [p_prev_from, p_prev_to)
    SELECT COALESCE(SUM(sii.qty * sii.unit_price - sii.line_discount), 0.00)
    INTO v_prev_period_rev
    FROM sales_invoice si
    JOIN sales_invoice_item sii ON si.invoice_id = sii.invoice_id
    JOIN product p ON sii.product_id = p.product_id
    WHERE si.status = 'posted'
      AND si.issued_at >= p_prev_from::timestamptz
      AND si.issued_at < p_prev_to::timestamptz
      AND (p_category_id IS NULL OR p.category_id = p_category_id);

    -- Month-over-month comparison metrics
    v_mom_delta := v_period_rev - v_prev_period_rev;
    IF v_prev_period_rev = 0 THEN
        v_mom_pct := NULL;
    ELSE
        v_mom_pct := round((v_period_rev - v_prev_period_rev) / v_prev_period_rev * 100, 2);
    END IF;

    -- Trend calculation: day buckets if <= 62 days, Monday-start week buckets if > 62 days
    IF (p_to - p_from) <= 62 THEN
        SELECT COALESCE(
            jsonb_agg(
                jsonb_build_object(
                    'bucket', to_char(b.b_date, 'YYYY-MM-DD'),
                    'revenue', to_char(COALESCE(rev.day_rev, 0.00), 'FM999999999999990.00')
                )
                ORDER BY b.b_date
            ),
            '[]'::jsonb
        )
        INTO v_trend
        FROM (
            SELECT generate_series(p_from, p_to - INTERVAL '1 day', INTERVAL '1 day')::date AS b_date
        ) b
        LEFT JOIN (
            SELECT
                si.issued_at::date AS b_date,
                SUM(sii.qty * sii.unit_price - sii.line_discount) AS day_rev
            FROM sales_invoice si
            JOIN sales_invoice_item sii ON si.invoice_id = sii.invoice_id
            JOIN product p ON sii.product_id = p.product_id
            WHERE si.status = 'posted'
              AND si.issued_at >= p_from::timestamptz
              AND si.issued_at < p_to::timestamptz
              AND (p_category_id IS NULL OR p.category_id = p_category_id)
            GROUP BY si.issued_at::date
        ) rev ON b.b_date = rev.b_date;
    ELSE
        SELECT COALESCE(
            jsonb_agg(
                jsonb_build_object(
                    'bucket', to_char(w.w_date, 'YYYY-MM-DD'),
                    'revenue', to_char(COALESCE(rev.week_rev, 0.00), 'FM999999999999990.00')
                )
                ORDER BY w.w_date
            ),
            '[]'::jsonb
        )
        INTO v_trend
        FROM (
            SELECT generate_series(
                date_trunc('week', p_from::timestamp)::date,
                (p_to - INTERVAL '1 day')::date,
                INTERVAL '1 week'
            )::date AS w_date
        ) w
        LEFT JOIN (
            SELECT
                date_trunc('week', si.issued_at)::date AS w_date,
                SUM(sii.qty * sii.unit_price - sii.line_discount) AS week_rev
            FROM sales_invoice si
            JOIN sales_invoice_item sii ON si.invoice_id = sii.invoice_id
            JOIN product p ON sii.product_id = p.product_id
            WHERE si.status = 'posted'
              AND si.issued_at >= p_from::timestamptz
              AND si.issued_at < p_to::timestamptz
              AND (p_category_id IS NULL OR p.category_id = p_category_id)
            GROUP BY date_trunc('week', si.issued_at)::date
        ) rev ON w.w_date = rev.w_date;
    END IF;

    -- Top 5 products (revenue DESC, name_fa ASC)
    SELECT COALESCE(
        jsonb_agg(
            jsonb_build_object(
                'productId', tp.product_id,
                'name', tp.name_fa,
                'revenue', to_char(tp.prod_rev, 'FM999999999999990.00')
            )
        ),
        '[]'::jsonb
    )
    INTO v_top_products
    FROM (
        SELECT
            p.product_id,
            p.name_fa,
            SUM(sii.qty * sii.unit_price - sii.line_discount) AS prod_rev
        FROM sales_invoice si
        JOIN sales_invoice_item sii ON si.invoice_id = sii.invoice_id
        JOIN product p ON sii.product_id = p.product_id
        WHERE si.status = 'posted'
          AND si.issued_at >= p_from::timestamptz
          AND si.issued_at < p_to::timestamptz
          AND (p_category_id IS NULL OR p.category_id = p_category_id)
        GROUP BY p.product_id, p.name_fa
        ORDER BY prod_rev DESC, p.name_fa ASC
        LIMIT 5
    ) tp;

    -- Top 5 customers (revenue DESC, name_fa ASC)
    SELECT COALESCE(
        jsonb_agg(
            jsonb_build_object(
                'customerId', tc.customer_id,
                'name', tc.name_fa,
                'revenue', to_char(tc.cust_rev, 'FM999999999999990.00')
            )
        ),
        '[]'::jsonb
    )
    INTO v_top_customers
    FROM (
        SELECT
            c.customer_id,
            c.name_fa,
            SUM(sii.qty * sii.unit_price - sii.line_discount) AS cust_rev
        FROM sales_invoice si
        JOIN sales_invoice_item sii ON si.invoice_id = sii.invoice_id
        JOIN customer c ON si.customer_id = c.customer_id
        JOIN product p ON sii.product_id = p.product_id
        WHERE si.status = 'posted'
          AND si.issued_at >= p_from::timestamptz
          AND si.issued_at < p_to::timestamptz
          AND (p_category_id IS NULL OR p.category_id = p_category_id)
        GROUP BY c.customer_id, c.name_fa
        ORDER BY cust_rev DESC, c.name_fa ASC
        LIMIT 5
    ) tc;

    RETURN QUERY SELECT
        v_period_rev,
        v_prev_period_rev,
        v_mom_delta,
        v_mom_pct,
        v_trend,
        v_top_products,
        v_top_customers;
END;
$$;

-- ============================================================================
-- 2. v_sales_lines
-- ============================================================================
-- Approved read-only KPI entry point ("view" in the product sense). Only the API/AI may call it.
-- sales_revenue = SUM(qty * unit_price - line_discount)
-- Supporting rows behind every sales figure (FR-005).
CREATE OR REPLACE FUNCTION v_sales_lines(
    p_from date,
    p_to date,
    p_category_id bigint DEFAULT NULL
)
RETURNS TABLE(
    invoice_id bigint,
    customer_id bigint,
    product_id bigint,
    date date,
    customer text,
    product text,
    qty numeric,
    unit_price numeric,
    line_discount numeric,
    net numeric
)
LANGUAGE sql
SECURITY INVOKER
AS $$
    SELECT
        si.invoice_id,
        si.customer_id,
        sii.product_id,
        si.issued_at::date AS date,
        c.name_fa AS customer,
        p.name_fa AS product,
        sii.qty,
        sii.unit_price,
        sii.line_discount,
        (sii.qty * sii.unit_price - sii.line_discount) AS net
    FROM sales_invoice si
    JOIN sales_invoice_item sii ON si.invoice_id = sii.invoice_id
    JOIN customer c ON si.customer_id = c.customer_id
    JOIN product p ON sii.product_id = p.product_id
    WHERE si.status = 'posted'
      AND si.issued_at >= p_from::timestamptz
      AND si.issued_at < p_to::timestamptz
      AND (p_category_id IS NULL OR p.category_id = p_category_id)
    ORDER BY si.issued_at::date DESC, si.invoice_id DESC, sii.item_id DESC;
$$;

-- ============================================================================
-- 3. v_inventory_alerts
-- ============================================================================
-- Approved read-only KPI entry point ("view" in the product sense). Only the API/AI may call it.
-- Single server-side threshold, documented default 10, same for every product; batch is a flag only (has_batch), there is no expiry workflow.
-- warehouse = the warehouse holding the largest share of that product's net stock (tie: lowest warehouse_id); Demo 1 seeds exactly one warehouse, so this is unambiguous in practice.
CREATE OR REPLACE FUNCTION v_inventory_alerts(
    p_category_id bigint DEFAULT NULL,
    p_low_stock_threshold numeric DEFAULT 10
)
RETURNS TABLE(
    product_id bigint,
    name_fa text,
    category text,
    warehouse text,
    on_hand numeric,
    has_batch boolean,
    state text
)
LANGUAGE sql
SECURITY INVOKER
AS $$
    WITH product_stock AS (
        SELECT
            p.product_id,
            p.name_fa,
            pc.name_fa AS category,
            p.has_batch,
            COALESCE(SUM(sm.qty_delta), 0)::numeric(18,3) AS on_hand
        FROM product p
        JOIN product_category pc ON p.category_id = pc.category_id
        LEFT JOIN stock_movement sm ON p.product_id = sm.product_id
        WHERE (p_category_id IS NULL OR p.category_id = p_category_id)
        GROUP BY p.product_id, p.name_fa, pc.name_fa, p.has_batch
    ),
    primary_warehouse AS (
        SELECT DISTINCT ON (sm.product_id)
            sm.product_id,
            w.name_fa AS warehouse_name
        FROM stock_movement sm
        JOIN warehouse w ON sm.warehouse_id = w.warehouse_id
        GROUP BY sm.product_id, sm.warehouse_id, w.name_fa
        ORDER BY sm.product_id, SUM(sm.qty_delta) DESC, sm.warehouse_id ASC
    ),
    classified AS (
        SELECT
            ps.product_id,
            ps.name_fa,
            ps.category,
            COALESCE(pw.warehouse_name, (SELECT w2.name_fa FROM warehouse w2 ORDER BY w2.warehouse_id ASC LIMIT 1), 'انبار مرکزی') AS warehouse,
            ps.on_hand,
            ps.has_batch,
            CASE
                WHEN ps.on_hand <= 0 THEN 'out'
                WHEN ps.on_hand <= p_low_stock_threshold THEN 'low'
                ELSE 'healthy'
            END AS state
        FROM product_stock ps
        LEFT JOIN primary_warehouse pw ON ps.product_id = pw.product_id
    )
    SELECT
        c.product_id,
        c.name_fa,
        c.category,
        c.warehouse,
        c.on_hand,
        c.has_batch,
        c.state
    FROM classified c
    WHERE c.state IN ('low', 'out')
    ORDER BY CASE c.state WHEN 'out' THEN 1 ELSE 2 END, c.on_hand ASC, c.name_fa ASC;
$$;

-- ============================================================================
-- 4. v_inventory_rows
-- ============================================================================
-- Approved read-only KPI entry point ("view" in the product sense). Only the API/AI may call it.
-- Supporting rows behind inventory alerts and full catalog stock state.
-- Single server-side threshold, documented default 10, same for every product; batch is a flag only (has_batch), there is no expiry workflow.
-- warehouse = the warehouse holding the largest share of that product's net stock (tie: lowest warehouse_id); Demo 1 seeds exactly one warehouse, so this is unambiguous in practice.
CREATE OR REPLACE FUNCTION v_inventory_rows(
    p_category_id bigint DEFAULT NULL,
    p_low_stock_threshold numeric DEFAULT 10
)
RETURNS TABLE(
    product_id bigint,
    name_fa text,
    category text,
    warehouse text,
    on_hand numeric,
    has_batch boolean,
    state text
)
LANGUAGE sql
SECURITY INVOKER
AS $$
    WITH product_stock AS (
        SELECT
            p.product_id,
            p.name_fa,
            pc.name_fa AS category,
            p.has_batch,
            COALESCE(SUM(sm.qty_delta), 0)::numeric(18,3) AS on_hand
        FROM product p
        JOIN product_category pc ON p.category_id = pc.category_id
        LEFT JOIN stock_movement sm ON p.product_id = sm.product_id
        WHERE (p_category_id IS NULL OR p.category_id = p_category_id)
        GROUP BY p.product_id, p.name_fa, pc.name_fa, p.has_batch
    ),
    primary_warehouse AS (
        SELECT DISTINCT ON (sm.product_id)
            sm.product_id,
            w.name_fa AS warehouse_name
        FROM stock_movement sm
        JOIN warehouse w ON sm.warehouse_id = w.warehouse_id
        GROUP BY sm.product_id, sm.warehouse_id, w.name_fa
        ORDER BY sm.product_id, SUM(sm.qty_delta) DESC, sm.warehouse_id ASC
    ),
    classified AS (
        SELECT
            ps.product_id,
            ps.name_fa,
            ps.category,
            COALESCE(pw.warehouse_name, (SELECT w2.name_fa FROM warehouse w2 ORDER BY w2.warehouse_id ASC LIMIT 1), 'انبار مرکزی') AS warehouse,
            ps.on_hand,
            ps.has_batch,
            CASE
                WHEN ps.on_hand <= 0 THEN 'out'
                WHEN ps.on_hand <= p_low_stock_threshold THEN 'low'
                ELSE 'healthy'
            END AS state
        FROM product_stock ps
        LEFT JOIN primary_warehouse pw ON ps.product_id = pw.product_id
    )
    SELECT
        c.product_id,
        c.name_fa,
        c.category,
        c.warehouse,
        c.on_hand,
        c.has_batch,
        c.state
    FROM classified c
    ORDER BY CASE c.state WHEN 'out' THEN 1 WHEN 'low' THEN 2 ELSE 3 END, c.on_hand ASC, c.name_fa ASC;
$$;

-- ============================================================================
-- 5. v_receivables
-- ============================================================================
-- Approved read-only KPI entry point ("view" in the product sense). Only the API/AI may call it.
-- cash_received = SUM(receipts.amount) in range
-- receivables_balance = SUM(posted invoice net) + opening_balance - SUM(receipts) lifetime-to-date
-- the period filter scopes only cash_received; sales revenue is never equated with cash received.
CREATE OR REPLACE FUNCTION v_receivables(
    p_from date,
    p_to date
)
RETURNS TABLE(
    total_receivables numeric,
    payments_received numeric,
    top_debtors jsonb
)
LANGUAGE plpgsql
SECURITY INVOKER
AS $$
DECLARE
    v_total_receivables numeric(18,2);
    v_payments_received numeric(18,2);
    v_top_debtors jsonb;
BEGIN
    -- Cash received in range [p_from, p_to) only
    SELECT COALESCE(SUM(r.amount), 0.00)
    INTO v_payments_received
    FROM receipt r
    WHERE r.received_at >= p_from::timestamptz
      AND r.received_at < p_to::timestamptz;

    -- Per-customer balance: lifetime posted invoice net + opening_balance - lifetime receipts
    WITH customer_invoiced AS (
        SELECT
            si.customer_id,
            COALESCE(SUM(sii.qty * sii.unit_price - sii.line_discount), 0.00) AS total_invoiced
        FROM sales_invoice si
        JOIN sales_invoice_item sii ON si.invoice_id = sii.invoice_id
        WHERE si.status = 'posted'
        GROUP BY si.customer_id
    ),
    customer_receipts AS (
        SELECT
            r.customer_id,
            COALESCE(SUM(r.amount), 0.00) AS total_received
        FROM receipt r
        GROUP BY r.customer_id
    ),
    customer_balances AS (
        SELECT
            c.customer_id,
            c.name_fa,
            (c.opening_balance + COALESCE(ci.total_invoiced, 0.00) - COALESCE(cr.total_received, 0.00)) AS balance
        FROM customer c
        LEFT JOIN customer_invoiced ci ON c.customer_id = ci.customer_id
        LEFT JOIN customer_receipts cr ON c.customer_id = cr.customer_id
    )
    SELECT
        COALESCE(SUM(cb.balance), 0.00),
        COALESCE(
            (
                SELECT jsonb_agg(
                    jsonb_build_object(
                        'customerId', td.customer_id,
                        'name', td.name_fa,
                        'balance', to_char(td.balance, 'FM999999999999990.00')
                    )
                )
                FROM (
                    SELECT cb2.customer_id, cb2.name_fa, cb2.balance
                    FROM customer_balances cb2
                    WHERE cb2.balance > 0
                    ORDER BY cb2.balance DESC, cb2.name_fa ASC
                    LIMIT 5
                ) td
            ),
            '[]'::jsonb
        )
    INTO v_total_receivables, v_top_debtors
    FROM customer_balances cb;

    RETURN QUERY SELECT
        v_total_receivables,
        v_payments_received,
        v_top_debtors;
END;
$$;

-- ============================================================================
-- 6. v_receivable_rows
-- ============================================================================
-- Approved read-only KPI entry point ("view" in the product sense). Only the API/AI may call it.
-- cash_received = SUM(receipts.amount) in range
-- receivables_balance = SUM(posted invoice net) + opening_balance - SUM(receipts) lifetime-to-date
-- sales revenue is never equated with cash received.
-- Supporting rows behind customer receivables (FR-005).
CREATE OR REPLACE FUNCTION v_receivable_rows(
    p_from date,
    p_to date
)
RETURNS TABLE(
    customer_id bigint,
    name_fa text,
    opening_balance numeric,
    invoiced_net numeric,
    received_total numeric,
    received_in_period numeric,
    balance numeric
)
LANGUAGE sql
SECURITY INVOKER
AS $$
    WITH customer_invoiced AS (
        SELECT
            si.customer_id,
            COALESCE(SUM(sii.qty * sii.unit_price - sii.line_discount), 0.00) AS total_invoiced
        FROM sales_invoice si
        JOIN sales_invoice_item sii ON si.invoice_id = sii.invoice_id
        WHERE si.status = 'posted'
        GROUP BY si.customer_id
    ),
    customer_receipts_lifetime AS (
        SELECT
            r.customer_id,
            COALESCE(SUM(r.amount), 0.00) AS total_received
        FROM receipt r
        GROUP BY r.customer_id
    ),
    customer_receipts_period AS (
        SELECT
            r.customer_id,
            COALESCE(SUM(r.amount), 0.00) AS period_received
        FROM receipt r
        WHERE r.received_at >= p_from::timestamptz
          AND r.received_at < p_to::timestamptz
        GROUP BY r.customer_id
    )
    SELECT
        c.customer_id,
        c.name_fa,
        c.opening_balance,
        COALESCE(ci.total_invoiced, 0.00) AS invoiced_net,
        COALESCE(crl.total_received, 0.00) AS received_total,
        COALESCE(crp.period_received, 0.00) AS received_in_period,
        (c.opening_balance + COALESCE(ci.total_invoiced, 0.00) - COALESCE(crl.total_received, 0.00)) AS balance
    FROM customer c
    LEFT JOIN customer_invoiced ci ON c.customer_id = ci.customer_id
    LEFT JOIN customer_receipts_lifetime crl ON c.customer_id = crl.customer_id
    LEFT JOIN customer_receipts_period crp ON c.customer_id = crp.customer_id
    ORDER BY (c.opening_balance + COALESCE(ci.total_invoiced, 0.00) - COALESCE(crl.total_received, 0.00)) DESC, c.name_fa ASC;
$$;
