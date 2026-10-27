-- =====================================================================
-- LOCAL DEVELOPMENT ONLY — never run on the AKSHI servers.
-- The production account ash_audit_writer only accepts 10.% over SSL.
-- This account has the SAME rights (INSERT + SELECT, no UPDATE/DELETE).
-- Connection string (user-secrets ConnectionStrings:Audit):
--   Server=localhost;Port=3306;User=ash_audit_local;Password=ash_audit_local_pw;
-- =====================================================================
CREATE USER IF NOT EXISTS 'ash_audit_local'@'%' IDENTIFIED BY 'ash_audit_local_pw';
GRANT INSERT, SELECT ON ash_audit.* TO 'ash_audit_local'@'%';
