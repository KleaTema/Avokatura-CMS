-- =====================================================================
-- Task 053e — client decision C (12.11.2026), default D4: "Ndihmësit ligjorë". The Kryetar attaches a legal assistant
-- to one or more lawyers of the office (assistant_lawyers); the assistant then gets EVERY case where such a lawyer is
-- the current primary. The access code stays as it is: AssistantSync keeps LEGAL_ASSISTANT rows in case_assignments
-- (case_assignments.assistant_link_id = the link that made the row) — on link / unlink, LawyerAssigned / Transferred,
-- and in the nightly reconcile job.
--
-- assistant.manage moves from the primary lawyer (CASE permission, task 026) to the Kryetar (SYSTEM permission of
-- OFFICE_HEAD); menu.assistants opens the Kryetar's screen.
-- ash_ops.migration_reports: what a data migration did, printed by the migrator (script 024 is the first user).
-- =====================================================================
USE ash_core;

CREATE TABLE assistant_lawyers (
  id                BIGINT UNSIGNED   NOT NULL AUTO_INCREMENT,
  public_id         BINARY(16)        NOT NULL DEFAULT (UUID_TO_BIN(UUID(), 1)),
  assistant_user_id INT UNSIGNED      NOT NULL,
  lawyer_user_id    INT UNSIGNED      NOT NULL,
  office_id         SMALLINT UNSIGNED NOT NULL COMMENT 'the office of both (the Kryetar''s)',
  created_by        INT UNSIGNED      NOT NULL,
  created_at        DATETIME(3)       NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  ended_at          DATETIME(3)       NULL,
  ended_by          INT UNSIGNED      NULL,
  active_pair       VARCHAR(30) AS (IF(ended_at IS NULL, CONCAT(assistant_user_id, ':', lawyer_user_id), NULL)) STORED,
  PRIMARY KEY (id),
  UNIQUE KEY uq_al_public (public_id),
  UNIQUE KEY uq_al_active_pair (active_pair),
  KEY ix_al_lawyer (lawyer_user_id, ended_at),
  KEY ix_al_assistant (assistant_user_id, ended_at),
  KEY ix_al_office (office_id, ended_at),
  CONSTRAINT fk_al_assistant FOREIGN KEY (assistant_user_id) REFERENCES users (id),
  CONSTRAINT fk_al_lawyer    FOREIGN KEY (lawyer_user_id)    REFERENCES users (id),
  CONSTRAINT fk_al_office    FOREIGN KEY (office_id)         REFERENCES offices (id),
  CONSTRAINT fk_al_created   FOREIGN KEY (created_by)        REFERENCES users (id),
  CONSTRAINT fk_al_ended     FOREIGN KEY (ended_by)          REFERENCES users (id)
) ENGINE=InnoDB COMMENT='Ndihmësi ligjor i lidhur me avokatët e zyrës';

ALTER TABLE case_assignments
  ADD COLUMN assistant_link_id BIGINT UNSIGNED NULL COMMENT 'LEGAL_ASSISTANT rows kept by AssistantSync: the link (task 053e)',
  ADD KEY ix_ca_link (assistant_link_id, ended_at),
  ADD CONSTRAINT fk_ca_link FOREIGN KEY (assistant_link_id) REFERENCES assistant_lawyers (id);

-- ---------------------------------------------------------------- permissions
UPDATE permissions SET scope = 'SYSTEM', description = 'Lidh ndihmësin ligjor me avokatët e zyrës (Kryetari)'
 WHERE code = 'assistant.manage';

DELETE cpp FROM case_profile_permissions cpp JOIN permissions p ON p.id = cpp.permission_id
 WHERE p.code = 'assistant.manage';

INSERT IGNORE INTO permissions (code, scope, description) VALUES ('menu.assistants', 'SYSTEM', 'Ndihmësit ligjorë');

INSERT IGNORE INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id FROM roles r JOIN permissions p ON p.code IN ('assistant.manage', 'menu.assistants')
 WHERE r.code = 'OFFICE_HEAD';

-- ---------------------------------------------------------------- the report the migrator prints
CREATE TABLE IF NOT EXISTS ash_ops.migration_reports (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  script     VARCHAR(100)    NOT NULL COMMENT 'the script file, e.g. 024_assistant_links_migration.sql',
  kind       VARCHAR(40)     NOT NULL COMMENT 'one summary line per kind: LINK_CREATED, ROW_KEPT, ROW_ENDED …',
  detail     VARCHAR(500)    NOT NULL,
  created_at DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY ix_mr_script (script, kind)
) ENGINE=InnoDB COMMENT='Çfarë bëri një skript migrimi me të dhënat (printohet nga migratori)';
