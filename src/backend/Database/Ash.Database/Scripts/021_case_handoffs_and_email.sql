-- =====================================================================
-- Task 053c — client decisions 12.11.2026, section B.3 ("Përcjellja e çështjes", mandatory; default D5).
--
-- office_courts: which office handles which court (admin, Administrimi → Zyrat). A court has at most ONE active
--   office (uq_oc_active_court on a generated column that is NULL for inactive rows).
-- case_handoffs: closing a level that is not final hands the case off to the next court (level + court) and the
--   receiving office. One open handoff per level (accepted_case_id and cancelled_at NULL; uq_cho_open). Correcting
--   the destination cancels the open row and inserts a new one; registering X/n+1 sets accepted_case_id; the
--   decision becoming final cancels it ("Vendimi mori formë të prerë").
-- email_outbox: e-mails written in the action's transaction and sent AFTER the commit by the dispatcher, with
--   retries (a mail failure never fails the action).
--
-- New case_history_events.event_type values: CASE_HANDED_OFF, HANDOFF_CHANGED, HANDOFF_CANCELLED.
-- =====================================================================
USE ash_core;

CREATE TABLE office_courts (
  office_id         SMALLINT UNSIGNED NOT NULL,
  court_id          SMALLINT UNSIGNED NOT NULL,
  is_active         TINYINT(1)        NOT NULL DEFAULT 1,
  active_court_id   SMALLINT UNSIGNED AS (IF(is_active = 1, court_id, NULL)) STORED
                    COMMENT 'court_id while active, NULL otherwise: one active office per court',
  updated_by        INT UNSIGNED      NOT NULL,
  updated_at        DATETIME(3)       NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (office_id, court_id),
  UNIQUE KEY uq_oc_active_court (active_court_id),
  KEY ix_oc_court (court_id),
  CONSTRAINT fk_oc_office  FOREIGN KEY (office_id)  REFERENCES offices (id),
  CONSTRAINT fk_oc_court   FOREIGN KEY (court_id)   REFERENCES courts (id),
  CONSTRAINT fk_oc_updated FOREIGN KEY (updated_by) REFERENCES users (id)
) ENGINE=InnoDB COMMENT='Gjykatat që mbulon zyra';

CREATE TABLE case_handoffs (
  id                BIGINT UNSIGNED   NOT NULL AUTO_INCREMENT,
  public_id         BINARY(16)        NOT NULL DEFAULT (UUID_TO_BIN(UUID(), 1)),
  dossier_id        BIGINT UNSIGNED   NOT NULL,
  from_case_id      BIGINT UNSIGNED   NOT NULL COMMENT 'the closed level X/n',
  to_office_id      SMALLINT UNSIGNED NOT NULL,
  court_level_id    TINYINT UNSIGNED  NOT NULL,
  court_id          SMALLINT UNSIGNED NOT NULL,
  override_reason   VARCHAR(500)      NULL COMMENT 'set when the court has no office in office_courts and the user chose one',
  created_by        INT UNSIGNED      NOT NULL,
  created_at        DATETIME(3)       NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  accepted_case_id  BIGINT UNSIGNED   NULL COMMENT 'X/n+1 once registered',
  accepted_at       DATETIME(3)       NULL,
  cancelled_at      DATETIME(3)       NULL,
  cancelled_by      INT UNSIGNED      NULL COMMENT 'NULL when cancelled by the system (decision became final)',
  cancel_reason     VARCHAR(300)      NULL,
  open_from_case_id BIGINT UNSIGNED AS (IF(accepted_case_id IS NULL AND cancelled_at IS NULL, from_case_id, NULL)) STORED,
  PRIMARY KEY (id),
  UNIQUE KEY uq_cho_public (public_id),
  UNIQUE KEY uq_cho_open (open_from_case_id),
  KEY ix_cho_incoming (to_office_id, accepted_case_id, cancelled_at, created_at),
  KEY ix_cho_from (from_case_id),
  CONSTRAINT fk_cho_dossier   FOREIGN KEY (dossier_id)       REFERENCES dossiers (id),
  CONSTRAINT fk_cho_from      FOREIGN KEY (from_case_id)     REFERENCES cases (id),
  CONSTRAINT fk_cho_office    FOREIGN KEY (to_office_id)     REFERENCES offices (id),
  CONSTRAINT fk_cho_level     FOREIGN KEY (court_level_id)   REFERENCES court_levels (id),
  CONSTRAINT fk_cho_court     FOREIGN KEY (court_id)         REFERENCES courts (id),
  CONSTRAINT fk_cho_created   FOREIGN KEY (created_by)       REFERENCES users (id),
  CONSTRAINT fk_cho_accepted  FOREIGN KEY (accepted_case_id) REFERENCES cases (id),
  CONSTRAINT fk_cho_cancelled FOREIGN KEY (cancelled_by)     REFERENCES users (id),
  CONSTRAINT ck_cho_state CHECK (NOT (accepted_case_id IS NOT NULL AND cancelled_at IS NOT NULL))
) ENGINE=InnoDB COMMENT='Përcjellja e çështjes në shkallën e radhës';

CREATE TABLE email_outbox (
  id              BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  to_address      VARCHAR(254)     NOT NULL,
  subject         VARCHAR(300)     NOT NULL,
  body_text       TEXT             NOT NULL,
  status          VARCHAR(10)      NOT NULL DEFAULT 'PENDING' COMMENT 'PENDING | SENT | FAILED (attempts exhausted)',
  attempts        TINYINT UNSIGNED NOT NULL DEFAULT 0,
  next_attempt_at DATETIME(3)      NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  last_error      VARCHAR(1000)    NULL,
  created_at      DATETIME(3)      NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  sent_at         DATETIME(3)      NULL,
  PRIMARY KEY (id),
  KEY ix_eo_due (status, next_attempt_at),
  CONSTRAINT ck_eo_status CHECK (status IN ('PENDING', 'SENT', 'FAILED'))
) ENGINE=InnoDB COMMENT='E-mailet që dërgohen pas commit-it, me riprovime';
