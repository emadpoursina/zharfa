-- db/perf/audit-1y.sql — synthetic audit log of one year (spec 005, T073, quickstart Flow B7, SC-008).
--
-- !! NEVER RUN THIS AGAINST A REAL INSTALL. Throwaway database only. !!
--
-- What it does: inserts about 100 000 audit_log rows spread over the last 365 days (generate_series), with the
-- demonstration usernames as actors, a mix of the catalog actions (report opens dominate), report keys as targets and
-- synthetic period filters. Nothing else is touched.
-- The audit log is append-only (trg_audit_log_append_only), so the rows cannot be deleted: to remove them, drop and
-- recreate the throwaway database.
-- Used by quickstart Flow B7: the audit query must answer in under 2 s p95 with the three audit_log indexes in place.

INSERT INTO audit_log (at, actor, actor_user_id, action, target, detail)
SELECT
    now() - (random() * interval '365 days'),
    (ARRAY['admin', 'manager', 'purchasing', 'finance'])[1 + g % 4],
    (ARRAY[1, 2, 3, 4])[1 + g % 4],
    (ARRAY[
        'report.opened', 'report.opened', 'report.opened', 'report.opened', 'report.printed',
        'auth.signin.succeeded', 'auth.signout', 'audit.viewed', 'data_quality.viewed',
        'access.denied', 'user.role_changed', 'reconciliation.run'
    ])[1 + g % 12],
    CASE
        WHEN g % 12 IN (0, 1, 2, 3, 4) THEN
            (ARRAY['overview', 'by-vendor', 'by-product', 'trend', 'vendors', 'transactions', 'drill-down'])[1 + g % 7]
        WHEN g % 12 IN (5, 6) THEN 'user:' || (ARRAY['admin', 'manager', 'purchasing', 'finance'])[1 + g % 4]
        WHEN g % 12 = 7 THEN 'audit-log'
        WHEN g % 12 = 8 THEN 'data-quality'
        WHEN g % 12 = 9 THEN 'GET api/vendor-financials/summary'
        ELSE NULL
    END,
    CASE
        WHEN g % 12 IN (0, 1, 2, 3, 4) THEN
            'period=1405-' || lpad((1 + g % 12)::text, 2, '0') || '; vendor=PERF-V' || (g % 40)::text
        WHEN g % 12 = 9 THEN 'permission=vendor.financials'
        ELSE NULL
    END
FROM generate_series(1, 100000) AS g;

ANALYZE audit_log;
