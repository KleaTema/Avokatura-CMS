-- =====================================================================
-- LOCAL DEVELOPMENT ONLY — never run on the AKSHI servers.
-- Task 053i: the role "Avokat sekondar" (script 026).
--   sekondar@ash.local — office TR, SECONDARY_LAWYER only (the pure-secondary scenarios);
--   avokat2@ash.local  — STATE_LAWYER and ALSO SECONDARY_LAWYER, so the existing scenarios (avokat2 as secondary) work.
-- =====================================================================
USE ash_core;

SET @office_id = (SELECT id FROM offices WHERE code = 'TR');

INSERT IGNORE INTO users (external_subject, email, first_name, last_name, office_id, institution_id)
VALUES ('dev-sekondar', 'sekondar@ash.local', 'Sekondar', 'Dev', @office_id, NULL);

INSERT IGNORE INTO user_roles (user_id, role_id)
SELECT u.id, r.id
  FROM (SELECT 'sekondar@ash.local' AS email UNION ALL SELECT 'avokat2@ash.local') m
  JOIN users u ON u.email = m.email
  JOIN roles r ON r.code = 'SECONDARY_LAWYER';
