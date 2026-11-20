-- =====================================================================
-- Task 053q — client correction of 20.11.2026 to decision C (task 053e, script 023): a legal assistant is a user with
-- the LEGAL_ASSISTANT role who belongs to an office; the PRIMARY LAWYER of a case picks among the active assistants
-- of the case's office and adds / removes them on that case (Ekipi). The Kryetar no longer links assistants to
-- lawyers, and there is no automatic rule any more (AssistantSync and the nightly assistant-reconcile job are gone).
--
-- * assistant.manage is a CASE permission of the PRIMARY_LAWYER profile again (as in script 007, task 026);
--   the Kryetar (OFFICE_HEAD) loses it and the menu "Ndihmësit ligjorë" (menu.assistants) is deleted.
-- * Existing data: every LEGAL_ASSISTANT row now on a case STAYS, as an ordinary per-case assignment (nobody loses a
--   case): its assistant_link_id is cleared. Every active link of assistant_lawyers is ended (kept as history).
--   The table and the column stay unused (not dropped: the ended links are the record of what the Kryetar did).
-- * On a transfer the assistants stay on the case; the new primary lawyer may remove them.
--
-- Every name is schema-qualified (no USE: the journal insert and later scripts keep their schema).
-- =====================================================================

-- ---------------------------------------------------------------- the report the migrator prints
INSERT INTO ash_ops.migration_reports (script, kind, detail)
SELECT '036_assistants_per_case.sql', 'ROW_KEPT',
       CONCAT(COUNT(*), ' caktime të ndihmësve ligjorë mbeten në çështje si caktime të zakonshme')
  FROM ash_core.case_assignments
 WHERE case_role = 'LEGAL_ASSISTANT' AND ended_at IS NULL AND assistant_link_id IS NOT NULL;

INSERT INTO ash_ops.migration_reports (script, kind, detail)
SELECT '036_assistants_per_case.sql', 'LINK_ENDED',
       CONCAT(COUNT(*), ' lidhje ndihmës–avokat të Kryetarit u mbyllën')
  FROM ash_core.assistant_lawyers
 WHERE ended_at IS NULL;

-- ---------------------------------------------------------------- data
UPDATE ash_core.case_assignments
   SET assistant_link_id = NULL
 WHERE assistant_link_id IS NOT NULL;

-- ended in the name of the Kryetar who made the link (ended_by must name a user; a script has none)
UPDATE ash_core.assistant_lawyers
   SET ended_at = UTC_TIMESTAMP(3), ended_by = created_by
 WHERE ended_at IS NULL;

-- ---------------------------------------------------------------- permissions
-- a single-table DELETE (a multi-table DELETE resolves its alias in the default database, and there is none here)
DELETE FROM ash_core.role_permissions
 WHERE permission_id IN (SELECT id FROM ash_core.permissions WHERE code IN ('assistant.manage', 'menu.assistants'));

DELETE FROM ash_core.permissions WHERE code = 'menu.assistants';

UPDATE ash_core.permissions SET scope = 'CASE', description = 'Shton / heq ndihmësin ligjor në çështje (avokati primar)'
 WHERE code = 'assistant.manage';

INSERT IGNORE INTO ash_core.case_profile_permissions (access_profile, permission_id)
SELECT 'PRIMARY_LAWYER', id FROM ash_core.permissions WHERE code = 'assistant.manage';
