-- =====================================================================
-- ASH — 08: access view, report views, agenda, table-level grants
-- =====================================================================
USE ash_core;

-- ---------------------------------------------------------------------
-- v_case_access: who has a case-level profile on which case.
-- MySQL has no row-level security, so ALL case queries in the API go
-- through one access service that uses this view (plus the system
-- permissions case.view_office / case.view_all for Kryetar, Monitorues,
-- Admin). One place to audit, one place to change.
--   * lawyers / assistants: their current assignment
--   * INSTITUTION_USER: every case linked to their institution
--   * INSTITUTION_JURIST: only cases they are assigned to (case_assignments)
--   both get the link's profile (INSTITUTION_MONITORING / INSTITUTION_DELEGATED)
-- ---------------------------------------------------------------------
CREATE OR REPLACE VIEW v_case_access AS
SELECT ca.user_id, ca.case_id, ca.case_role AS access_profile
  FROM case_assignments ca
 WHERE ca.ended_at IS NULL
   AND ca.case_role IN ('PRIMARY_LAWYER','SECONDARY_LAWYER','LEGAL_ASSISTANT')
UNION ALL
SELECT u.id, ci.case_id, CONCAT('INSTITUTION_', ci.link_mode)
  FROM case_institutions ci
  JOIN users u       ON u.institution_id = ci.institution_id AND u.is_active = 1
  JOIN user_roles ur ON ur.user_id = u.id
  JOIN roles r       ON r.id = ur.role_id AND r.code = 'INSTITUTION_USER'
 WHERE ci.ended_at IS NULL
UNION ALL
SELECT ca.user_id, ca.case_id, CONCAT('INSTITUTION_', ci.link_mode)
  FROM case_assignments ca
  JOIN users u ON u.id = ca.user_id AND u.is_active = 1
  JOIN case_institutions ci ON ci.case_id = ca.case_id
                           AND ci.institution_id = u.institution_id
                           AND ci.ended_at IS NULL
 WHERE ca.ended_at IS NULL
   AND ca.case_role = 'INSTITUTION_JURIST';

-- Effective case permissions (user, case, permission code).
-- API check: SELECT 1 FROM v_case_permissions WHERE user_id=? AND case_id=? AND permission_code=?
CREATE OR REPLACE VIEW v_case_permissions AS
SELECT a.user_id, a.case_id, a.access_profile, p.code AS permission_code
  FROM v_case_access a
  JOIN case_profile_permissions cpp ON cpp.access_profile = a.access_profile
  JOIN permissions p ON p.id = cpp.permission_id;

-- Current team of each case (for lists and reports).
CREATE OR REPLACE VIEW v_case_team AS
SELECT c.id AS case_id,
       (SELECT CONCAT(u.first_name, ' ', u.last_name) FROM case_assignments a JOIN users u ON u.id = a.user_id
         WHERE a.case_id = c.id AND a.ended_at IS NULL AND a.case_role = 'PRIMARY_LAWYER')   AS primary_lawyer,
       (SELECT CONCAT(u.first_name, ' ', u.last_name) FROM case_assignments a JOIN users u ON u.id = a.user_id
         WHERE a.case_id = c.id AND a.ended_at IS NULL AND a.case_role = 'SECONDARY_LAWYER') AS secondary_lawyer
  FROM cases c;

-- ---------------------------------------------------------------------
-- Tabela 1 — Raporti i çështjeve aktive (one row per open case).
-- Filter in the API by office / lawyer / institution / dates.
-- ---------------------------------------------------------------------
CREATE OR REPLACE VIEW v_report_active_cases AS
SELECT c.id                       AS case_id,
       c.office_id,
       c.case_code                AS kodi_unik,
       c.act_number               AS numri_aktit,
       (SELECT GROUP_CONCAT(p.display_name ORDER BY p.id SEPARATOR '; ')
          FROM case_parties p JOIN party_roles r ON r.id = p.party_role_id
         WHERE p.case_id = c.id AND p.deleted_at IS NULL AND r.report_group = 'PLAINTIFF') AS paditesi,
       (SELECT GROUP_CONCAT(CONCAT(p.display_name, ' (', r.name, ')') ORDER BY p.id SEPARATOR '; ')
          FROM case_parties p JOIN party_roles r ON r.id = p.party_role_id
         WHERE p.case_id = c.id AND p.deleted_at IS NULL AND r.report_group = 'DEFENDANT_OR_THIRD') AS i_padituri_person_i_trete,
       c.object_text              AS objekti,
       ct.name                    AS gjykata,
       CONCAT(j.first_name, ' ', j.last_name) AS gjyqtari,
       c.next_hearing_at          AS seanca_e_radhes,
       CONCAT_WS(' / ', t.primary_lawyer, t.secondary_lawyer) AS avokati,
       h.description              AS pershkrimi_seances_se_fundit,
       c.has_security_measure     AS masa_sigurimit,
       c.claim_amount, c.claim_currency, c.claim_area_m2,
       cat.name                   AS kategoria,
       sub.name                   AS lloji,
       c.priority, c.is_suspended
  FROM cases c
  JOIN courts ct              ON ct.id = c.court_id
  LEFT JOIN judges j          ON j.id = c.judge_id
  LEFT JOIN v_case_team t     ON t.case_id = c.id
  LEFT JOIN case_hearings h   ON h.id = c.last_hearing_id
  LEFT JOIN case_categories cat    ON cat.id = c.category_id
  LEFT JOIN case_subcategories sub ON sub.id = c.subcategory_id
 WHERE c.lifecycle_status <> 'CLOSED';

-- ---------------------------------------------------------------------
-- Tabela 3 — Raporti i çështjeve të mbyllura: one row per closed level,
-- so a dossier shows up once per court level it passed through.
-- ---------------------------------------------------------------------
CREATE OR REPLACE VIEW v_report_closed_cases AS
SELECT c.id AS case_id, c.office_id, c.dossier_id,
       c.case_code AS kodi_unik, c.act_number AS numri_aktit,
       cl.name AS shkalla, ct.name AS gjykata,
       CONCAT(j.first_name, ' ', j.last_name) AS gjyqtari,
       (SELECT GROUP_CONCAT(p.display_name ORDER BY p.id SEPARATOR '; ')
          FROM case_parties p JOIN party_roles r ON r.id = p.party_role_id
         WHERE p.case_id = c.id AND p.deleted_at IS NULL AND r.report_group = 'PLAINTIFF') AS paditesi,
       (SELECT GROUP_CONCAT(p.display_name ORDER BY p.id SEPARATOR '; ')
          FROM case_parties p JOIN party_roles r ON r.id = p.party_role_id
         WHERE p.case_id = c.id AND p.deleted_at IS NULL AND r.report_group = 'DEFENDANT_OR_THIRD') AS i_padituri_person_i_trete,
       c.object_text AS objekti,
       -- lawyer at closing time = last primary assignment of that case
       (SELECT CONCAT(u.first_name, ' ', u.last_name) FROM case_assignments a JOIN users u ON u.id = a.user_id
         WHERE a.case_id = c.id AND a.case_role = 'PRIMARY_LAWYER' ORDER BY a.assigned_at DESC LIMIT 1) AS avokati,
       dr.name AS rezultati, d.result_other_text,
       d.decision_number, d.decision_date, d.dispositive,
       d.currency, d.won_amount, d.won_area_m2, d.lost_amount, d.lost_area_m2,
       d.prevented_damage_amount, d.prevented_damage_area_m2, d.caused_damage_amount, d.caused_damage_area_m2,
       d.court_costs_amount,
       c.has_security_measure AS masa_sigurimit,
       d.is_appealed, d.is_recursed, d.is_final, d.decision_received,
       c.closed_at
  FROM cases c
  JOIN court_levels cl ON cl.id = c.court_level_id
  JOIN courts ct       ON ct.id = c.court_id
  LEFT JOIN judges j   ON j.id = c.judge_id
  JOIN case_decisions d   ON d.case_id = c.id
  JOIN decision_results dr ON dr.id = d.result_id
 WHERE c.lifecycle_status = 'CLOSED';

-- ---------------------------------------------------------------------
-- Agjenda: hearings of the cases a user has a case role on.
-- (Kryetar/Monitor see office/all hearings through a separate API query.)
-- ---------------------------------------------------------------------
CREATE OR REPLACE VIEW v_user_agenda AS
SELECT a.user_id, h.id AS hearing_id, h.case_id, c.case_code, h.scheduled_at,
       ht.name AS hearing_type, h.other_type_text, h.status, ct.name AS court
  FROM v_case_access a
  JOIN case_hearings h  ON h.case_id = a.case_id
  JOIN cases c          ON c.id = h.case_id
  JOIN hearing_types ht ON ht.id = h.hearing_type_id
  JOIN courts ct        ON ct.id = c.court_id;

-- ---------------------------------------------------------------------
-- Table-level grants (need the tables to exist)
-- ---------------------------------------------------------------------
GRANT SELECT ON ash_core.users             TO 'ash_chat_app'@'10.%';
GRANT SELECT ON ash_core.cases             TO 'ash_chat_app'@'10.%';
GRANT SELECT ON ash_core.case_assignments  TO 'ash_chat_app'@'10.%';
GRANT SELECT ON ash_core.case_institutions TO 'ash_chat_app'@'10.%';
GRANT SELECT ON ash_core.v_case_access     TO 'ash_chat_app'@'10.%';
