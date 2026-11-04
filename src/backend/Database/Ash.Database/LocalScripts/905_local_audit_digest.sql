-- =====================================================================
-- LOCAL DEVELOPMENT ONLY — never run on the AKSHI servers.
-- The production account ash_audit_digest (script 016) only accepts 10.% over SSL.
-- This account has the SAME rights: SELECT on audit_log, SELECT + INSERT on audit_daily_digests (task 041: the audit
-- digest job and GET /api/admin/audit/verify). Connection string (user-secrets ConnectionStrings:AuditDigest):
--   Server=localhost;Port=3306;User=ash_audit_digest_local;Password=ash_audit_digest_local_pw;
-- =====================================================================
CREATE USER IF NOT EXISTS 'ash_audit_digest_local'@'%' IDENTIFIED BY 'ash_audit_digest_local_pw';
GRANT SELECT ON ash_audit.audit_log TO 'ash_audit_digest_local'@'%';
GRANT SELECT, INSERT ON ash_audit.audit_daily_digests TO 'ash_audit_digest_local'@'%';
