-- purchasing: purchasing123   finance: finance123   (demonstration accounts only)
SELECT setval(pg_get_serial_sequence('app_user','user_id'), (SELECT COALESCE(MAX(user_id),0) FROM app_user) + 1, false);
INSERT INTO app_user (username, username_key, password_hash, role, is_active) VALUES
 ('purchasing','purchasing','pbkdf2-sha256$100000$WmhhcmZhUHVyY2hTYWx0MQ==$GcSbv/NL8ilbD4SKyUiLBkS964PwXec46MhnAlnk5ak=','purchasing',true),
 ('finance','finance','pbkdf2-sha256$100000$WmhhcmZhRmluYW5jU2FsdA==$RwBAESa/61z54EPl6WzeWRxvcXfcgeGKwbkxECut+TY=','finance',true);
