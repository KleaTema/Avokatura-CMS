-- =====================================================================
-- Task 053n: "Ndryshimet e fundit administrative" on the administrator's dashboard (the last 10 ADMIN_CHANGE rows).
--   The API account still has NO grant on ash_audit.audit_log; it reads this view only: a plain projection (no
--   subqueries, so MySQL merges it and the LIMIT reaches ix_audit_action (action, occurred_at), script 006), the
--   columns the widget needs, ADMIN_CHANGE rows only, from details only the action's code. The app resolves the
--   entities' public ids / codes for the 10 rows it reads (entity_id never leaves the server). The view runs as its
--   definer (the migrator), SQL SECURITY DEFINER. No UPDATE / DELETE anywhere; ash_audit_digest stays dedicated to the
--   chain check. Every name is schema-qualified (no USE: the journal insert and later scripts keep their schema).
-- =====================================================================
CREATE OR REPLACE SQL SECURITY DEFINER VIEW ash_audit.v_admin_changes AS
SELECT a.id,
       a.occurred_at,
       a.user_id      AS actor_user_id,
       a.user_display AS actor_display,
       a.entity_type,
       a.entity_id,
       LEFT(JSON_UNQUOTE(JSON_EXTRACT(a.details, '$.action')), 60) AS change_action
  FROM ash_audit.audit_log a
 WHERE a.action = 'ADMIN_CHANGE';

-- the same account form as every ash_app grant (001, 016, 017, 019)
GRANT SELECT ON ash_audit.v_admin_changes TO 'ash_app'@'10.%';
