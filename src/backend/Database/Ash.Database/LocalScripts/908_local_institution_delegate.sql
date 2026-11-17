-- =====================================================================
-- LOCAL DEVELOPMENT ONLY — never run on the AKSHI servers.
-- Task 053k: the role "Institucioni me delegim" (script 031).
--   delegim@ash.local — Ministria e Financave dhe Ekonomisë, INSTITUTION_DELEGATE only: acts on the cases DELEGATED to
--   the ministry, sees its monitoring cases read-only.
-- =====================================================================
USE ash_core;

SET @institution_id = (SELECT id FROM institutions WHERE name = 'Ministria e Financave dhe Ekonomisë');

INSERT IGNORE INTO users (external_subject, email, first_name, last_name, office_id, institution_id)
VALUES ('dev-delegim', 'delegim@ash.local', 'Delegim', 'Dev', NULL, @institution_id);

INSERT IGNORE INTO user_roles (user_id, role_id)
SELECT u.id, r.id
  FROM users u JOIN roles r ON r.code = 'INSTITUTION_DELEGATE'
 WHERE u.email = 'delegim@ash.local';
