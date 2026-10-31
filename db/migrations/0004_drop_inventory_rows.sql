-- Convergence C1: remove unused helper (never referenced by API, AI tools, contracts or data-model).
DROP FUNCTION IF EXISTS v_inventory_rows(bigint, numeric);
