-- 0003_categories.sql — Active product categories semantic view for Zharfa Demo 1.
-- Stock PostgreSQL forward-only migration. Read-only; no extensions, no writes.

CREATE OR REPLACE FUNCTION v_categories()
RETURNS TABLE(
    category_id bigint,
    name_fa text
)
LANGUAGE sql
SECURITY INVOKER
AS $$
    SELECT
        pc.category_id,
        pc.name_fa
    FROM product_category pc
    WHERE pc.is_active = true
    ORDER BY pc.category_id ASC;
$$;

CREATE OR REPLACE VIEW v_categories AS
    SELECT * FROM v_categories();
