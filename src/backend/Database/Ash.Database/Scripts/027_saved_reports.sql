-- =====================================================================
-- Task 053h — "Raportet e mia": a user saves the report builder's current table, filters and columns under a name and
-- reruns it in one click. Private to its owner (no sharing table: a shareable URL carries the filters instead, and
-- whoever opens it sees only their own scope). Owned by the Reports module; written through the app connection (the
-- ash_reporting account stays read-only).
--   report_table: 1 | 3 | 5 | 6 (the client's table numbers)
--   filters:      the builder's filter values (JSON object, as GET /api/reports/tables/{t}?filters= takes them)
--   columns:      the chosen columns in their order (JSON array of keys)
-- Idempotent (IF NOT EXISTS).
-- =====================================================================
USE ash_core;

CREATE TABLE IF NOT EXISTS saved_reports (
  id            BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  public_id     BINARY(16)       NOT NULL DEFAULT (UUID_TO_BIN(UUID(), 1)),
  owner_user_id INT UNSIGNED     NOT NULL,
  name          VARCHAR(120)     NOT NULL COMMENT 'Emri i raportit',
  report_table  TINYINT UNSIGNED NOT NULL COMMENT 'Tabela 1 | 3 | 5 | 6',
  filters       JSON             NOT NULL,
  columns       JSON             NOT NULL,
  created_at    DATETIME(3)      NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at    DATETIME(3)      NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_sr_public (public_id),
  UNIQUE KEY uq_sr_owner_name (owner_user_id, name),
  CONSTRAINT fk_sr_owner FOREIGN KEY (owner_user_id) REFERENCES users (id),
  CONSTRAINT ck_sr_table CHECK (report_table IN (1, 3, 5, 6))
) ENGINE=InnoDB COMMENT='Raportet e ruajtura të përdoruesit (Raportet e mia)';
