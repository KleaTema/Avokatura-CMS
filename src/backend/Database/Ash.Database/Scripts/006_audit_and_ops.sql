-- =====================================================================
-- ASH — 06: technical audit log (ash_audit) + partition maintenance (ash_ops)
--
-- Audit log = EVERY security-relevant action, including reads:
-- login / failed login, case opened, document downloaded, report exported,
-- access denied, admin changes. This is what the memo calls "log" and what
-- the administrator inspects. It is high-volume, so:
--   * own schema, monthly partitions, no foreign keys
--   * written by an account with INSERT + SELECT only (file 01): the
--     application can never modify or erase it
--   * daily digest (SHA-256 over each day's rows) stored separately makes
--     later tampering by a DBA detectable
-- The user-facing "Historiku" of a case is ash_core.case_history_events.
-- =====================================================================
USE ash_audit;

CREATE TABLE audit_log (
  id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  occurred_at    DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  user_id        INT UNSIGNED    NULL COMMENT 'NULL for anonymous (failed login)',
  user_display   VARCHAR(200)    NULL COMMENT 'snapshot of the name — survives user changes',
  role_codes     VARCHAR(200)    NULL COMMENT 'roles held at that moment',
  action         VARCHAR(60)     NOT NULL COMMENT 'LOGIN, LOGIN_FAILED, CASE_VIEW, CASE_UPDATE, DOCUMENT_DOWNLOAD, REPORT_EXPORT, ACCESS_DENIED, ADMIN_ROLE_GRANTED ...',
  outcome        VARCHAR(10)     NOT NULL DEFAULT 'SUCCESS' COMMENT 'SUCCESS | DENIED | ERROR',
  entity_type    VARCHAR(40)     NULL COMMENT 'CASE, DOCUMENT, USER, REPORT ...',
  entity_id      BIGINT UNSIGNED NULL,
  case_id        BIGINT UNSIGNED NULL COMMENT 'denormalised: "who touched this case" in one index',
  ip_address     VARBINARY(16)   NULL COMMENT 'INET6_ATON() — IPv4 and IPv6',
  user_agent     VARCHAR(300)    NULL,
  correlation_id BINARY(16)      NULL COMMENT 'request id, links to application logs',
  details        JSON            NULL COMMENT 'changed fields, filters of an export ... never file contents or secrets',
  PRIMARY KEY (id, occurred_at),
  KEY ix_audit_user (user_id, occurred_at),
  KEY ix_audit_case (case_id, occurred_at),
  KEY ix_audit_entity (entity_type, entity_id, occurred_at),
  KEY ix_audit_action (action, occurred_at),
  CONSTRAINT ck_audit_outcome CHECK (outcome IN ('SUCCESS','DENIED','ERROR'))
) ENGINE=InnoDB
  PARTITION BY RANGE COLUMNS (occurred_at) (
    PARTITION p_old VALUES LESS THAN ('2026-11-01 00:00:00'),
    PARTITION p_max VALUES LESS THAN (MAXVALUE)
  );

CREATE TABLE audit_daily_digests (
  audit_day    DATE            NOT NULL,
  row_count    BIGINT UNSIGNED NOT NULL,
  first_id     BIGINT UNSIGNED NULL,
  last_id      BIGINT UNSIGNED NULL,
  digest       BINARY(32)      NOT NULL COMMENT 'SHA-256 chained with the previous day''s digest',
  computed_at  DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (audit_day)
) ENGINE=InnoDB COMMENT='Copy each digest outside the DB too (e.g. signed daily e-mail / WORM bucket)';

-- ---------------------------------------------------------------------
-- ash_ops: keeps future monthly partitions in place for both partitioned
-- tables. Idempotent — safe to run any number of times.
-- It splits the empty p_max partition, so it is instant (no data copied).
-- ---------------------------------------------------------------------
USE ash_ops;

DELIMITER $$
CREATE PROCEDURE ensure_month_partitions(
  IN p_schema       VARCHAR(64),
  IN p_table        VARCHAR(64),
  IN p_months_ahead TINYINT UNSIGNED)
BEGIN
  DECLARE v_i       INT DEFAULT 0;
  DECLARE v_start   DATE;
  DECLARE v_end     DATE;
  DECLARE v_pname   VARCHAR(20);
  DECLARE v_highest DATE;

  WHILE v_i <= p_months_ahead DO
    SET v_start = DATE_FORMAT(UTC_DATE() + INTERVAL v_i MONTH, '%Y-%m-01');
    SET v_end   = v_start + INTERVAL 1 MONTH;
    SET v_pname = CONCAT('p', DATE_FORMAT(v_start, '%Y%m'));

    -- highest bound already covered by a named (non-MAXVALUE) partition
    SELECT MAX(CAST(TRIM(BOTH '''' FROM partition_description) AS DATE))
      INTO v_highest
      FROM information_schema.partitions
     WHERE table_schema = p_schema
       AND table_name   = p_table
       AND partition_description <> 'MAXVALUE';

    IF v_highest IS NULL OR v_end > v_highest THEN
      SET @ddl = CONCAT(
        'ALTER TABLE `', p_schema, '`.`', p_table, '` REORGANIZE PARTITION p_max INTO (',
        'PARTITION ', v_pname, ' VALUES LESS THAN (''', DATE_FORMAT(v_end, '%Y-%m-%d'), ' 00:00:00''), ',
        'PARTITION p_max VALUES LESS THAN (MAXVALUE))');
      PREPARE stmt FROM @ddl;
      EXECUTE stmt;
      DEALLOCATE PREPARE stmt;
    END IF;
    SET v_i = v_i + 1;
  END WHILE;
END$$

CREATE PROCEDURE rotate_all_partitions()
BEGIN
  CALL ensure_month_partitions('ash_chat',  'messages',  3);
  CALL ensure_month_partitions('ash_audit', 'audit_log', 3);
END$$
DELIMITER ;

-- Runs daily; creates the next 3 months if missing. Requires event_scheduler=ON
-- (see 09_mysql_server.cnf). Monitoring should alert if p_max ever holds rows.
CREATE EVENT IF NOT EXISTS ev_rotate_partitions
  ON SCHEDULE EVERY 1 DAY STARTS (UTC_DATE() + INTERVAL 1 DAY + INTERVAL 2 HOUR)
  DO CALL ash_ops.rotate_all_partitions();

-- create the first months right away at install time
CALL ash_ops.rotate_all_partitions();
