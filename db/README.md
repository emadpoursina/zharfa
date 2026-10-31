# Database — PostgreSQL (synthetic demonstration data only)

Own database, independent of Mabna. Nothing in this repo writes to, or
connects to, any legacy system; Demo 1 has no Mabna access at all.

## Layout

```
db/
  migrations/   numbered SQL files, applied in ascending order
  seed/         synthetic demonstration data (labeled as demo data everywhere)
```

## Migration convention

- `NNNN_short_name.sql` (e.g. `0001_init_canonical.sql`), forward-only.
- One migration owns the canonical entities, the semantic views for the
  three Demo 1 metric sets (sales, inventory alerts, receivables), and the
  reserved-but-empty `SourceRecord` / `SyncJob` / `SyncLog` tables.
- Analytical KPIs are computed in SQL views only; the API and the AI agent
  read those approved views, never base tables directly.
- Financial formulas stay documented next to the view that implements them
  (sales revenue, cash received, receivables balance — never equated).
- Seed data is demonstration data and must be labeled as such.

Migrations are executed on API startup by `backend/Zharfa.Api/Data/MigrationRunner.cs`.
The runner already exists, applies pending files forward-only under `db/migrations/` in sequential transactions tracked in `schema_migrations`, and applies seeds under `db/seed/` only on an empty `app_user`.
The six KPI entry points are parameterized functions named `v_*` (`v_sales_overview`, `v_sales_lines`, `v_inventory_alerts`, `v_receivables`, `v_receivable_rows`, and `v_categories` serving the filter; called via `SELECT * FROM v_sales_overview(p_from => ..., p_to => ..., p_prev_from => ..., p_prev_to => ...)`).
