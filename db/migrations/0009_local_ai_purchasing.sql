-- 0009_local_ai_purchasing.sql — Local AI over purchasing (Release 1, Phase 4)
-- Forward-only, idempotent migration. Requires 0008.
-- Spec: specs/006-local-ai-purchasing/data-model.md §1.

INSERT INTO install_setting (key, value, set_at)
SELECT 'ai.cloud.enabled', 'false', now()
WHERE NOT EXISTS (SELECT 1 FROM install_setting WHERE key = 'ai.cloud.enabled');
