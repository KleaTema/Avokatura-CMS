-- =====================================================================
-- Task 041: background jobs (reminders, cleanup, audit digest, partition check).
-- =====================================================================

-- ---------------------------------------------------------------------
-- The last start / success / error of each job. The scheduler reads it on start (catch-up after downtime) and the
-- "jobs" health check reads it (stale = no success within 2x the interval + 15 minutes). One row per job name.
-- ---------------------------------------------------------------------
USE ash_ops;

CREATE TABLE job_runs (
  job_name          VARCHAR(60)   NOT NULL,
  last_started_at   DATETIME(3)   NULL,
  last_succeeded_at DATETIME(3)   NULL,
  last_error        VARCHAR(1000) NULL COMMENT 'cleared by the next success',
  PRIMARY KEY (job_name)
) ENGINE=InnoDB COMMENT='Gjendja e punëve në sfond';

GRANT SELECT, INSERT, UPDATE ON ash_ops.job_runs TO 'ash_app'@'10.%';

DELIMITER $$
-- ---------------------------------------------------------------------
-- (Comments go after DELIMITER: DbUp's splitter must see "DELIMITER" right after the previous statement's end.)
-- The partition check job (daily): for messages and audit_log, whether p_max holds rows (the monthly rotation event
-- stopped) and whether next month already has its own partition. SQL SECURITY DEFINER: the API account has no
-- privilege on ash_chat / ash_audit and must not get one; it only gets EXECUTE on this procedure.
-- p_next_month_end = the first day of the month AFTER next month (next month's partition bound, UTC).
-- ---------------------------------------------------------------------
CREATE PROCEDURE partition_status(IN p_next_month_end DATE)
  SQL SECURITY DEFINER
  READS SQL DATA
BEGIN
  SELECT 'ash_chat' AS table_schema, 'messages' AS table_name,
         EXISTS (SELECT 1 FROM ash_chat.messages PARTITION (p_max)) AS p_max_has_rows,
         EXISTS (SELECT 1 FROM information_schema.partitions
                  WHERE table_schema = 'ash_chat' AND table_name = 'messages'
                    AND partition_description <> 'MAXVALUE'
                    AND CAST(TRIM(BOTH '''' FROM partition_description) AS DATETIME) >= p_next_month_end) AS next_month_ready
  UNION ALL
  SELECT 'ash_audit', 'audit_log',
         EXISTS (SELECT 1 FROM ash_audit.audit_log PARTITION (p_max)),
         EXISTS (SELECT 1 FROM information_schema.partitions
                  WHERE table_schema = 'ash_audit' AND table_name = 'audit_log'
                    AND partition_description <> 'MAXVALUE'
                    AND CAST(TRIM(BOTH '''' FROM partition_description) AS DATETIME) >= p_next_month_end);
END$$
DELIMITER ;

GRANT EXECUTE ON PROCEDURE ash_ops.partition_status TO 'ash_app'@'10.%';

-- ---------------------------------------------------------------------
-- Reminders sent once, by a key the job builds: "hearing:<id>:<date>:tomorrow", "pending:<historyId>:3d",
-- "partition:<date>". Inserted in the same transaction as the notifications (INSERT IGNORE: a duplicate = sent).
-- The data-entry reminders keep their own table (case_reminders, uq_cr_once).
-- ---------------------------------------------------------------------
USE ash_core;

CREATE TABLE sent_reminders (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  dedupe_key VARCHAR(120)    NOT NULL,
  sent_at    DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_sr_key (dedupe_key)
) ENGINE=InnoDB COMMENT='Kujtesat e dërguara (seancat, fletëthirrjet, kontrolli i particioneve)';

-- ---------------------------------------------------------------------
-- The audit digest job and the chain check (GET /api/admin/audit/verify) use their OWN account
-- (ConnectionStrings:AuditDigest): it reads audit_log and writes only digests. The request-time audit writer
-- (ash_audit_writer) keeps its grants; the API account still has none on ash_audit.
-- ---------------------------------------------------------------------
CREATE USER IF NOT EXISTS 'ash_audit_digest'@'10.%' IDENTIFIED BY '__SET_FROM_SECRETS__' REQUIRE SSL;
GRANT SELECT ON ash_audit.audit_log TO 'ash_audit_digest'@'10.%';
GRANT SELECT, INSERT ON ash_audit.audit_daily_digests TO 'ash_audit_digest'@'10.%';
