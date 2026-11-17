-- =====================================================================
-- Task 053k — the institution roles (client decisions, docs/client/decisions-2026-11-12.md §5; docs/permissions-matrix.md).
--
--   "Institucioni"            (INSTITUTION_USER)     every case linked to its institution. MONITORING link: the
--                             monitoring profile (view, add hearing, upload, chat) and it assigns the jurist;
--                             DELEGATED link: view only (INSTITUTION_VIEW).
--   "Juristi i institucionit" (INSTITUTION_JURIST)   the MONITORING cases it is assigned to (monitoring profile); no
--                             decision. Nothing on a delegated case.
--   "Institucioni me delegim" (INSTITUTION_DELEGATE) NEW. Every active user with the role in the institution acts on the
--                             cases DELEGATED to it (INSTITUTION_DELEGATED profile, incl. D1's decision); a MONITORING
--                             case of its institution: view only (INSTITUTION_VIEW).
-- New history code JURIST_REMOVED (also written when a case is delegated from now on).
-- Delegation itself (task 027) is unchanged: a link mode. Jurists still assigned on delegated cases are ended here, with a
-- JURIST_REMOVED history row (no production data).
-- Expand-then-contract: the 053j API reads v_case_access the same way; the new profile only adds rows.
-- =====================================================================
USE ash_core;

UPDATE roles SET name = 'Institucioni' WHERE code = 'INSTITUTION_USER';
UPDATE roles SET name = 'Juristi i institucionit' WHERE code = 'INSTITUTION_JURIST';

INSERT IGNORE INTO roles (code, name, description) VALUES
 ('INSTITUTION_DELEGATE', 'Institucioni me delegim',
  'Përdorues i institucionit që vepron në çështjet e deleguara institucionit të tij');

-- the same system permissions as the jurist (menus + report.view_own); never institution.assign_jurist
INSERT IGNORE INTO role_permissions (role_id, permission_id)
SELECT d.id, rp.permission_id
  FROM roles d
  JOIN roles j ON j.code = 'INSTITUTION_JURIST'
  JOIN role_permissions rp ON rp.role_id = j.id
 WHERE d.code = 'INSTITUTION_DELEGATE';

-- a view-only profile: Institucioni on a delegated case, Institucioni me delegim on a monitoring case
ALTER TABLE case_profile_permissions DROP CHECK ck_cpp_profile;
ALTER TABLE case_profile_permissions ADD CONSTRAINT ck_cpp_profile CHECK (access_profile IN
  ('PRIMARY_LAWYER','SECONDARY_LAWYER','LEGAL_ASSISTANT','INSTITUTION_MONITORING','INSTITUTION_DELEGATED','INSTITUTION_VIEW'));

INSERT IGNORE INTO case_profile_permissions (access_profile, permission_id)
SELECT 'INSTITUTION_VIEW', id FROM permissions WHERE code = 'case.view';

-- who holds which profile on a case — the ONE definition (CaseScopeQuery writes the same branches for the case lists)
CREATE OR REPLACE VIEW v_case_access AS
SELECT ca.user_id, ca.case_id, ca.case_role AS access_profile
  FROM case_assignments ca
 WHERE ca.ended_at IS NULL
   AND ca.case_role IN ('PRIMARY_LAWYER','SECONDARY_LAWYER','LEGAL_ASSISTANT')
UNION ALL
-- Institucioni: monitoring profile on a MONITORING link, view only on a DELEGATED one
SELECT u.id, ci.case_id, IF(ci.link_mode = 'MONITORING', 'INSTITUTION_MONITORING', 'INSTITUTION_VIEW')
  FROM case_institutions ci
  JOIN users u       ON u.institution_id = ci.institution_id AND u.is_active = 1
  JOIN user_roles ur ON ur.user_id = u.id
  JOIN roles r       ON r.id = ur.role_id AND r.code = 'INSTITUTION_USER'
 WHERE ci.ended_at IS NULL
UNION ALL
-- Institucioni me delegim: the delegated profile on a DELEGATED link, view only on a MONITORING one
SELECT u.id, ci.case_id, IF(ci.link_mode = 'DELEGATED', 'INSTITUTION_DELEGATED', 'INSTITUTION_VIEW')
  FROM case_institutions ci
  JOIN users u       ON u.institution_id = ci.institution_id AND u.is_active = 1
  JOIN user_roles ur ON ur.user_id = u.id
  JOIN roles r       ON r.id = ur.role_id AND r.code = 'INSTITUTION_DELEGATE'
 WHERE ci.ended_at IS NULL
UNION ALL
-- Juristi: the monitoring profile, only where assigned AND the link is MONITORING
SELECT ca.user_id, ca.case_id, 'INSTITUTION_MONITORING'
  FROM case_assignments ca
  JOIN users u ON u.id = ca.user_id AND u.is_active = 1
  JOIN case_institutions ci ON ci.case_id = ca.case_id
                           AND ci.institution_id = u.institution_id
                           AND ci.ended_at IS NULL
                           AND ci.link_mode = 'MONITORING'
 WHERE ca.ended_at IS NULL
   AND ca.case_role = 'INSTITUTION_JURIST';

-- jurists still assigned on a delegated case of their institution: ended, with the history row
INSERT INTO case_history_events (dossier_id, case_id, event_type, actor_user_id, summary, details)
SELECT c.dossier_id, c.id, 'JURIST_REMOVED', NULL,
       CONCAT('Juristi ', u.first_name, ' ', u.last_name, ' u hoq: çështja është e deleguar (task 053k).'),
       JSON_OBJECT('old', JSON_OBJECT('name', CONCAT(u.first_name, ' ', u.last_name)), 'new', NULL)
  FROM case_assignments ca
  JOIN users u ON u.id = ca.user_id
  JOIN cases c ON c.id = ca.case_id
  JOIN case_institutions ci ON ci.case_id = ca.case_id AND ci.institution_id = u.institution_id
                           AND ci.ended_at IS NULL AND ci.link_mode = 'DELEGATED'
 WHERE ca.ended_at IS NULL AND ca.case_role = 'INSTITUTION_JURIST';

UPDATE case_assignments ca
  JOIN users u ON u.id = ca.user_id
  JOIN case_institutions ci ON ci.case_id = ca.case_id AND ci.institution_id = u.institution_id
                           AND ci.ended_at IS NULL AND ci.link_mode = 'DELEGATED'
   SET ca.ended_at = CURRENT_TIMESTAMP(3), ca.ended_by = NULL, ca.end_reason = 'REMOVED'
 WHERE ca.ended_at IS NULL AND ca.case_role = 'INSTITUTION_JURIST';
