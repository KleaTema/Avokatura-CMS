-- =====================================================================
-- Task 053e — the per-case assistants of task 026 become links (decision of 13.11.2026). After script 023.
--   * assistant and current primary in the SAME office → a link (assistant, primary); the row is kept and from now
--     on belongs to that link (assistant_link_id set, AssistantSync keeps it);
--   * another office, or no current primary → the row is ended (REMOVED) and reported with the reason;
--   * one history row per affected row; every step in ash_ops.migration_reports (the migrator prints the summary).
-- IDEMPOTENT: only current LEGAL_ASSISTANT rows WITHOUT a link are looked at; after one run there are none, so a
-- second run finds nothing and changes nothing (AssistantMigrationTests runs it twice).
-- =====================================================================
USE ash_core;

DROP TEMPORARY TABLE IF EXISTS tmp_legacy_assistants;
CREATE TEMPORARY TABLE tmp_legacy_assistants AS
SELECT a.id AS assignment_id, a.case_id, c.dossier_id, c.case_code, a.user_id AS assistant_id, a.assigned_by,
       au.office_id AS assistant_office, p.user_id AS primary_id,
       CONCAT(au.first_name, ' ', au.last_name) AS assistant_name,
       CONCAT(pu.first_name, ' ', pu.last_name) AS primary_name,
       (p.user_id IS NOT NULL AND au.office_id IS NOT NULL AND au.office_id = pu.office_id) AS linkable
  FROM case_assignments a
  JOIN cases c ON c.id = a.case_id
  JOIN users au ON au.id = a.user_id
  LEFT JOIN case_assignments p ON p.case_id = a.case_id AND p.case_role = 'PRIMARY_LAWYER' AND p.ended_at IS NULL
  LEFT JOIN users pu ON pu.id = p.user_id
 WHERE a.case_role = 'LEGAL_ASSISTANT' AND a.ended_at IS NULL AND a.assistant_link_id IS NULL;

-- 1. one link per (assistant, primary) of the same office
INSERT INTO ash_ops.migration_reports (script, kind, detail)
SELECT DISTINCT '024_assistant_links_migration.sql', 'LINK_CREATED', CONCAT(t.assistant_name, ' → ', t.primary_name)
  FROM tmp_legacy_assistants t
 WHERE t.linkable = 1
   AND NOT EXISTS (SELECT 1 FROM assistant_lawyers l
                    WHERE l.assistant_user_id = t.assistant_id AND l.lawyer_user_id = t.primary_id AND l.ended_at IS NULL);

INSERT INTO assistant_lawyers (assistant_user_id, lawyer_user_id, office_id, created_by)
SELECT t.assistant_id, t.primary_id, t.assistant_office, MIN(t.assigned_by)
  FROM tmp_legacy_assistants t
 WHERE t.linkable = 1
   AND NOT EXISTS (SELECT 1 FROM assistant_lawyers l
                    WHERE l.assistant_user_id = t.assistant_id AND l.lawyer_user_id = t.primary_id AND l.ended_at IS NULL)
 GROUP BY t.assistant_id, t.primary_id, t.assistant_office;

-- 2. the row is kept and belongs to its link from now on
INSERT INTO ash_ops.migration_reports (script, kind, detail)
SELECT '024_assistant_links_migration.sql', 'ROW_KEPT', CONCAT(t.case_code, ': ', t.assistant_name, ' (avokati ', t.primary_name, ')')
  FROM tmp_legacy_assistants t
 WHERE t.linkable = 1;

INSERT INTO case_history_events (dossier_id, case_id, event_type, summary, details)
SELECT t.dossier_id, t.case_id, 'ASSISTANT_ADDED', 'Ndihmësi u kalua në lidhje me avokatin.',
       JSON_OBJECT('new', JSON_OBJECT('Assistant', t.assistant_name, 'Lawyer', t.primary_name, 'Migration', '053e'))
  FROM tmp_legacy_assistants t
 WHERE t.linkable = 1;

UPDATE case_assignments a
  JOIN tmp_legacy_assistants t ON t.assignment_id = a.id
  JOIN assistant_lawyers l ON l.assistant_user_id = t.assistant_id AND l.lawyer_user_id = t.primary_id AND l.ended_at IS NULL
   SET a.assistant_link_id = l.id
 WHERE t.linkable = 1;

-- 3. another office, or no current primary: the row is ended and reported
INSERT INTO ash_ops.migration_reports (script, kind, detail)
SELECT '024_assistant_links_migration.sql', 'ROW_ENDED',
       CONCAT(t.case_code, ': ', t.assistant_name, ' — ',
              IF(t.primary_id IS NULL, 'Migrim 053e: pa avokat primar', 'Migrim 053e: zyrë tjetër'))
  FROM tmp_legacy_assistants t
 WHERE t.linkable = 0;

INSERT INTO case_history_events (dossier_id, case_id, event_type, summary, details)
SELECT t.dossier_id, t.case_id, 'ASSISTANT_REMOVED',
       CONCAT('Ndihmësi ', t.assistant_name, ' u hoq: ',
              IF(t.primary_id IS NULL, 'Migrim 053e: pa avokat primar', 'Migrim 053e: zyrë tjetër'), '.'),
       JSON_OBJECT('old', JSON_OBJECT('Assistant', t.assistant_name, 'Migration', '053e'))
  FROM tmp_legacy_assistants t
 WHERE t.linkable = 0;

UPDATE case_assignments a
  JOIN tmp_legacy_assistants t ON t.assignment_id = a.id
   SET a.ended_at = UTC_TIMESTAMP(3), a.end_reason = 'REMOVED'
 WHERE t.linkable = 0;

DROP TEMPORARY TABLE tmp_legacy_assistants;
