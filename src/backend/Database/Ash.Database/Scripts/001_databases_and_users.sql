-- =====================================================================
-- ASH Case Management System — 01: databases and DB users
-- Target: MySQL 8.0.30+ (tested on 8.0.46). Run as a DBA account.
--
-- Four schemas, kept apart on purpose:
--   ash_core   case data (dossiers, cases, parties, hearings, decisions,
--              document METADATA, case history, notifications)
--   ash_chat   chat conversations and messages (high volume, partitioned)
--   ash_audit  technical audit log (append-only, partitioned)
--   ash_ops    maintenance procedures (partition rotation)
-- No foreign keys cross schemas, so ash_chat or ash_audit can be moved to
-- their own server later without changing the application model.
-- =====================================================================

-- utf8mb4_0900_ai_ci = accent-insensitive: a search for "ceshtje" matches
-- "çështje", "Permet" matches "Përmet", ë = e, ç = c. Right default for
-- Albanian text typed with and without diacritics.
CREATE DATABASE IF NOT EXISTS ash_core  CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE DATABASE IF NOT EXISTS ash_chat  CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE DATABASE IF NOT EXISTS ash_audit CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE DATABASE IF NOT EXISTS ash_ops   CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;

-- ---------------------------------------------------------------------
-- Least-privilege accounts. Passwords are placeholders: set them from the
-- secrets store (Vault / AKSHI secrets service) at deploy time, never commit.
-- Restrict the host part ('10.%' below) to the application subnet.
-- REQUIRE SSL: no plaintext connections from the application servers.
-- ---------------------------------------------------------------------

-- 1. Schema migrations only (DbUp / Flyway runs as this user). Not used at runtime.
CREATE USER IF NOT EXISTS 'ash_migrator'@'10.%' IDENTIFIED BY '__SET_FROM_SECRETS__' REQUIRE SSL;
GRANT ALL PRIVILEGES ON ash_core.*  TO 'ash_migrator'@'10.%';
GRANT ALL PRIVILEGES ON ash_chat.*  TO 'ash_migrator'@'10.%';
GRANT ALL PRIVILEGES ON ash_audit.* TO 'ash_migrator'@'10.%';
GRANT ALL PRIVILEGES ON ash_ops.*   TO 'ash_migrator'@'10.%';

-- 2. Main API: DML only on case data, no DDL, no access to audit rows it cannot rewrite.
CREATE USER IF NOT EXISTS 'ash_app'@'10.%' IDENTIFIED BY '__SET_FROM_SECRETS__' REQUIRE SSL;
GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON ash_core.* TO 'ash_app'@'10.%';

-- 3. Chat service (can be the same API process, but a separate account
--    keeps a chat bug from touching case data).
CREATE USER IF NOT EXISTS 'ash_chat_app'@'10.%' IDENTIFIED BY '__SET_FROM_SECRETS__' REQUIRE SSL;
GRANT SELECT, INSERT, UPDATE ON ash_chat.* TO 'ash_chat_app'@'10.%';
-- Its read-only access to the few core tables it needs is granted in
-- 08_table_grants.sql (table-level grants need the tables to exist first).

-- 4. Audit writer: INSERT + SELECT only. No UPDATE, no DELETE => the
--    application cannot alter or erase audit history even if compromised.
CREATE USER IF NOT EXISTS 'ash_audit_writer'@'10.%' IDENTIFIED BY '__SET_FROM_SECRETS__' REQUIRE SSL;
GRANT INSERT, SELECT ON ash_audit.* TO 'ash_audit_writer'@'10.%';

-- 5. Reports/statistics: read-only. Point it at the read replica so heavy
--    statistics never slow down daily work on the primary.
CREATE USER IF NOT EXISTS 'ash_reporting'@'10.%' IDENTIFIED BY '__SET_FROM_SECRETS__' REQUIRE SSL;
GRANT SELECT ON ash_core.* TO 'ash_reporting'@'10.%';

-- 6. Backup account (mysqldump / xtrabackup / mysqlsh dump).
CREATE USER IF NOT EXISTS 'ash_backup'@'localhost' IDENTIFIED BY '__SET_FROM_SECRETS__';
GRANT SELECT, SHOW VIEW, TRIGGER, LOCK TABLES, EVENT, RELOAD, PROCESS, REPLICATION CLIENT, BACKUP_ADMIN
  ON *.* TO 'ash_backup'@'localhost';
