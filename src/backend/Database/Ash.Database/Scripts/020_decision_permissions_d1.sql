-- =====================================================================
-- Task 053a — client decision D1 (12.11.2026): who records / completes the decision (decision.record) and archives
-- the dossier (decision.close_final): the primary lawyer (already), the secondary lawyer, the legal assistant, the
-- institution's jurist where the institution is DELEGATED (decision.record already; close_final added), and the
-- Kryetar on the cases of the office (system permission decision.record_office → CaseAccessService). NOT the Protokoll.
-- Idempotent (INSERT IGNORE on the primary keys).
-- =====================================================================
USE ash_core;

INSERT IGNORE INTO case_profile_permissions (access_profile, permission_id)
SELECT prof.p, perm.id
  FROM (SELECT 'SECONDARY_LAWYER' AS p UNION ALL SELECT 'LEGAL_ASSISTANT') prof
  JOIN permissions perm ON perm.code IN ('decision.record', 'decision.close_final');

INSERT IGNORE INTO case_profile_permissions (access_profile, permission_id)
SELECT 'INSTITUTION_DELEGATED', id FROM permissions WHERE code = 'decision.close_final';

INSERT IGNORE INTO permissions (code, scope, description) VALUES
 ('decision.record_office', 'SYSTEM', 'Regjistron / plotëson vendimin dhe arkivon në çështjet e zyrës (Kryetari)');

INSERT IGNORE INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id FROM roles r JOIN permissions p ON p.code = 'decision.record_office'
 WHERE r.code = 'OFFICE_HEAD';
