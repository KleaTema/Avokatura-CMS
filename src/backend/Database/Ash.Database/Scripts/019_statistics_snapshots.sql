-- =====================================================================
-- Task 044: statistics — frozen figures of closed years.
-- Some attributes have no history: the category (Delegim / Relatim) and the suspension are only known as they are
-- NOW. So the figures of a closed year are frozen on 1 January (Tirane) by the statistics-snapshot job: one row per
-- case with the per-case base flags of that year (StatisticsLive.sql), exactly the input of every measure.
-- Why per case and not per measure: the statistics are filtered by office, lawyer, institution and ministry in any
-- combination; per-case rows answer every combination from the same frozen input (per-measure totals would need one
-- row per combination). A closed year is read from here; without a snapshot it is computed live ("jo e ngrirë").
-- TODO(client-q-stat-freeze): confirm that published yearly figures must never change.
-- =====================================================================
USE ash_core;

CREATE TABLE statistics_snapshots (
  year        SMALLINT UNSIGNED NOT NULL,
  case_count  INT UNSIGNED      NOT NULL COMMENT 'rows in statistics_snapshot_cases for the year',
  created_at  DATETIME(3)       NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  created_by  INT UNSIGNED      NULL COMMENT 'NULL = the yearly job; an admin user for a manual re-run',
  PRIMARY KEY (year)
) ENGINE=InnoDB COMMENT='Statistikat e ngrira të një viti të mbyllur';

CREATE TABLE statistics_snapshot_cases (
  year                   SMALLINT UNSIGNED NOT NULL,
  case_id                BIGINT UNSIGNED   NOT NULL,
  office_id              SMALLINT UNSIGNED NOT NULL,
  court_id               SMALLINT UNSIGNED NOT NULL COMMENT 'the court of this LEVEL (an appeal /2 counts at the appeal court)',
  primary_at_end         INT UNSIGNED      NULL COMMENT 'primary lawyer at the end of the year',
  primary_at_close       INT UNSIGNED      NULL COMMENT 'primary lawyer when the level was closed',
  is_delegim             TINYINT(1)        NOT NULL,
  is_relatim             TINYINT(1)        NOT NULL,
  is_suspended           TINYINT(1)        NOT NULL,
  result_id              TINYINT UNSIGNED  NULL,
  currency               CHAR(3)           NULL,
  prevented_amount       DECIMAL(18,2)     NULL,
  prevented_m2           DECIMAL(14,2)     NULL,
  caused_amount          DECIMAL(18,2)     NULL,
  caused_m2              DECIMAL(14,2)     NULL,
  open_start             TINYINT(1)        NOT NULL,
  reg_in_year            TINYINT(1)        NOT NULL,
  open_end               TINYINT(1)        NOT NULL,
  closed_in_year         TINYINT(1)        NOT NULL,
  transferred            TINYINT(1)        NOT NULL,
  appealed               TINYINT(1)        NOT NULL,
  pending_at_start       TINYINT(1)        NOT NULL,
  decision_arrived       TINYINT(1)        NOT NULL,
  PRIMARY KEY (year, case_id),
  KEY ix_ssc_office (year, office_id),
  CONSTRAINT fk_ssc_snapshot FOREIGN KEY (year) REFERENCES statistics_snapshots (year) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='Statistikat e ngrira: një rresht për çështje dhe vit';

-- the API writes them (the job, the admin re-run); the reports read them (ash_reporting: SELECT on ash_core.*)
GRANT SELECT, INSERT, DELETE ON ash_core.statistics_snapshots      TO 'ash_app'@'10.%';
GRANT SELECT, INSERT, DELETE ON ash_core.statistics_snapshot_cases TO 'ash_app'@'10.%';
