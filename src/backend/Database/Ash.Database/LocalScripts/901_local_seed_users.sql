-- =====================================================================
-- LOCAL DEVELOPMENT ONLY — never run on the AKSHI servers.
-- Seeds one office, one institution and one active user per role.
-- Log in locally with the header:  X-Dev-User: admin@ash.local
-- =====================================================================
USE ash_core;

INSERT INTO offices (code, name, office_type)
VALUES ('TR', 'Zyra Qendrore Tiranë', 'CENTRAL');

INSERT INTO institutions (name, institution_type)
VALUES ('Ministria e Financave dhe Ekonomisë', 'MINISTRY');

SET @office_id = (SELECT id FROM offices WHERE code = 'TR');
SET @institution_id = (SELECT id FROM institutions WHERE name = 'Ministria e Financave dhe Ekonomisë');

INSERT INTO users (external_subject, email, first_name, last_name, office_id, institution_id) VALUES
 ('dev-admin',      'admin@ash.local',      'Admin',      'Dev', @office_id, NULL),
 ('dev-protokoll',  'protokoll@ash.local',  'Protokoll',  'Dev', @office_id, NULL),
 ('dev-kryetar',    'kryetar@ash.local',    'Kryetar',    'Dev', @office_id, NULL),
 ('dev-avokat1',    'avokat1@ash.local',    'Avokat1',    'Dev', @office_id, NULL),
 ('dev-avokat2',    'avokat2@ash.local',    'Avokat2',    'Dev', @office_id, NULL),
 ('dev-ndihmes',    'ndihmes@ash.local',    'Ndihmës',    'Dev', @office_id, NULL),
 ('dev-monitor',    'monitor@ash.local',    'Monitor',    'Dev', @office_id, NULL),
 ('dev-institucion','institucion@ash.local','Institucion','Dev', NULL, @institution_id),
 ('dev-jurist',     'jurist@ash.local',     'Jurist',     'Dev', NULL, @institution_id);

UPDATE offices
SET head_user_id = (SELECT id FROM users WHERE email = 'kryetar@ash.local')
WHERE id = @office_id;

INSERT INTO user_roles (user_id, role_id)
SELECT u.id, r.id
FROM (
  SELECT 'admin@ash.local' AS email, 'ADMIN' AS role_code
  UNION ALL SELECT 'protokoll@ash.local',   'PROTOCOL'
  UNION ALL SELECT 'kryetar@ash.local',     'OFFICE_HEAD'
  UNION ALL SELECT 'avokat1@ash.local',     'STATE_LAWYER'
  UNION ALL SELECT 'avokat2@ash.local',     'STATE_LAWYER'
  UNION ALL SELECT 'ndihmes@ash.local',     'LEGAL_ASSISTANT'
  UNION ALL SELECT 'monitor@ash.local',     'MONITOR'
  UNION ALL SELECT 'institucion@ash.local', 'INSTITUTION_USER'
  UNION ALL SELECT 'jurist@ash.local',      'INSTITUTION_JURIST'
) m
JOIN users u ON u.email = m.email
JOIN roles r ON r.code = m.role_code;
