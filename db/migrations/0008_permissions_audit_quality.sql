-- 0008_permissions_audit_quality.sql — Roles, sign-in state, audit scope and data quality (Release 1, Phase 3)
-- Forward-only, idempotent migration. Requires 0007.
-- Spec: specs/005-permissions-audit-quality/data-model.md §1.4, §2.1, §3.1.

-- ============================================================================
-- 1. Users and roles
-- ============================================================================

-- 1.1 Replace the Demo 1 role check (manager, admin) with the four Release 1 roles.
ALTER TABLE app_user DROP CONSTRAINT IF EXISTS app_user_role_check;

-- 1.2 Demo 1 "manager" becomes "management" (same people, same rights).
UPDATE app_user SET role = 'management' WHERE role = 'manager';

-- 1.3 New columns. username_key is the normalized login key (see UsernameRules.Key).
ALTER TABLE app_user
    ADD COLUMN IF NOT EXISTS username_key text,
    ADD COLUMN IF NOT EXISTS failed_signins integer NOT NULL DEFAULT 0 CHECK (failed_signins >= 0),
    ADD COLUMN IF NOT EXISTS locked_until timestamptz NULL,
    ADD COLUMN IF NOT EXISTS must_change_password boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now(),
    ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT now();

UPDATE app_user SET username_key = lower(translate(btrim(username), 'يىك', 'ییک')) WHERE username_key IS NULL;

ALTER TABLE app_user ALTER COLUMN username_key SET NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_app_user_username_key ON app_user (username_key);

-- 1.4 Role check for the four Release 1 roles (same guard pattern as 0005).
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_app_user_role') THEN
        ALTER TABLE app_user ADD CONSTRAINT chk_app_user_role CHECK (role IN ('admin', 'management', 'purchasing', 'finance'));
    END IF;
END $$;

-- 1.5 Keep the identity sequence ahead of any explicitly seeded user_id.
SELECT setval(pg_get_serial_sequence('app_user', 'user_id'), (SELECT COALESCE(MAX(user_id), 0) FROM app_user) + 1, false);

-- 1.6 Demo upgrade: add the two demonstration role accounts to an existing demo install.
-- On a fresh install install_setting has no data_mode row yet, so seed 0003 creates them instead.
-- Demonstration accounts only (purchasing: purchasing123, finance: finance123).
INSERT INTO app_user (username, username_key, password_hash, role, is_active)
SELECT v.username, v.username_key, v.password_hash, v.role, true
FROM (VALUES
    ('purchasing', 'purchasing', 'pbkdf2-sha256$100000$WmhhcmZhUHVyY2hTYWx0MQ==$GcSbv/NL8ilbD4SKyUiLBkS964PwXec46MhnAlnk5ak=', 'purchasing'),
    ('finance', 'finance', 'pbkdf2-sha256$100000$WmhhcmZhRmluYW5jU2FsdA==$RwBAESa/61z54EPl6WzeWRxvcXfcgeGKwbkxECut+TY=', 'finance')
) AS v (username, username_key, password_hash, role)
WHERE EXISTS (SELECT 1 FROM install_setting WHERE key = 'data_mode' AND value = 'demo')
  AND EXISTS (SELECT 1 FROM app_user)
ON CONFLICT (username_key) DO NOTHING;

-- ============================================================================
-- 2. Audit log scope (append-only trigger trg_audit_log_append_only is unchanged)
-- ============================================================================

ALTER TABLE audit_log
    ADD COLUMN IF NOT EXISTS target text NULL,
    ADD COLUMN IF NOT EXISTS actor_user_id bigint NULL;

-- No foreign key from actor_user_id to app_user: deleted or renamed users keep their history.

CREATE INDEX IF NOT EXISTS idx_audit_log_at ON audit_log (at DESC, audit_id DESC);
CREATE INDEX IF NOT EXISTS idx_audit_log_actor_at ON audit_log (actor, at DESC);
CREATE INDEX IF NOT EXISTS idx_audit_log_action_at ON audit_log (action, at DESC);

-- ============================================================================
-- 3. Data quality
-- ============================================================================

CREATE TABLE IF NOT EXISTS data_quality_run (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    sync_job_id bigint NOT NULL REFERENCES sync_job(id),
    started_at timestamptz NOT NULL,
    checked_at timestamptz NULL,
    status text NOT NULL CHECK (status IN ('running', 'succeeded', 'failed')),
    error_code text NULL
);

CREATE INDEX IF NOT EXISTS idx_data_quality_run_status ON data_quality_run (status, id DESC);

CREATE TABLE IF NOT EXISTS data_quality_issue (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    run_id bigint NOT NULL REFERENCES data_quality_run(id) ON DELETE CASCADE,
    issue_type text NOT NULL CHECK (issue_type IN (
        'missing-values', 'duplicate-records', 'broken-relationships', 'inconsistent-product-ids',
        'invalid-dates', 'unexpected-quantities', 'inconsistent-amounts')),
    record_kind text NOT NULL CHECK (record_kind IN ('document', 'line', 'product')),
    record_ref text NOT NULL,
    field text NOT NULL,
    reason text NULL,
    -- No foreign keys on the three ids below: the mabna.* copy is replaced by each sync.
    document_id bigint NULL,
    line_id bigint NULL,
    product_id bigint NULL,
    CONSTRAINT uq_data_quality_issue_record UNIQUE (run_id, issue_type, record_ref, field)
);

CREATE INDEX IF NOT EXISTS idx_data_quality_issue_type ON data_quality_issue (run_id, issue_type);
CREATE INDEX IF NOT EXISTS idx_data_quality_issue_document ON data_quality_issue (run_id, document_id);
CREATE INDEX IF NOT EXISTS idx_data_quality_issue_line ON data_quality_issue (run_id, line_id);
CREATE INDEX IF NOT EXISTS idx_data_quality_issue_product ON data_quality_issue (run_id, product_id);
