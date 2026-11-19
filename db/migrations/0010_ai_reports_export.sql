-- 0010_ai_reports_export.sql — AI Reports, Saved Reports, and Excel Export (Release 1, Phase 5)
-- Forward-only, idempotent migration. Requires 0009.
-- Spec: specs/007-ai-reports-export/data-model.md §1.

CREATE TABLE IF NOT EXISTS saved_report (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id bigint NOT NULL REFERENCES app_user(user_id) ON DELETE CASCADE,
    name text NOT NULL,
    name_key text NOT NULL,
    kind text NOT NULL,
    required_permission text NOT NULL,
    period_json jsonb NOT NULL,
    filters_json jsonb NOT NULL,
    generated_json jsonb NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_saved_report_user_name_key
    ON saved_report (user_id, name_key);

CREATE INDEX IF NOT EXISTS idx_saved_report_user_updated
    ON saved_report (user_id, updated_at DESC);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_saved_report_kind') THEN
        ALTER TABLE saved_report ADD CONSTRAINT chk_saved_report_kind CHECK (
            kind IN (
                'by-vendor',
                'by-product',
                'by-category',
                'trend',
                'top-products',
                'returns',
                'transactions',
                'vendors',
                'vendors-compare',
                'drill-down',
                'vendor-profile',
                'generated'
            )
        );
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_saved_report_permission') THEN
        ALTER TABLE saved_report ADD CONSTRAINT chk_saved_report_permission CHECK (
            required_permission IN ('purchasing.reports', 'ai.query')
        );
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_saved_report_generated') THEN
        ALTER TABLE saved_report ADD CONSTRAINT chk_saved_report_generated CHECK (
            (kind = 'generated' AND generated_json IS NOT NULL) OR
            (kind <> 'generated' AND generated_json IS NULL)
        );
    END IF;
END $$;
