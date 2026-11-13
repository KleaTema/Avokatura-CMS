-- =====================================================================
-- Task 053d — client decision C (12.11.2026), default D3: "Mbikëqyrja". The Monitor puts under a Kryetar's
-- supervision either a LAWYER (every case where that lawyer is the CURRENT primary — now and later, decided at read
-- time) or a single CASE. The Kryetar then sees and acts on those cases as on the cases of their own office
-- (CaseAccessService; the scope branch in CaseScopeQuery / ReportScope / the statistics).
--
-- One active row per (Kryetar, lawyer) and per (Kryetar, case): generated columns that are NULL once ended.
-- New case_history_events.event_type values: SUPERVISION_STARTED, SUPERVISION_ENDED (CASE kind only).
-- Permissions: supervision.manage + menu.supervision (Monitor), case.view_supervised (Kryetar).
-- =====================================================================
USE ash_core;

CREATE TABLE supervisions (
  id                  BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  public_id           BINARY(16)       NOT NULL DEFAULT (UUID_TO_BIN(UUID(), 1)),
  head_user_id        INT UNSIGNED     NOT NULL COMMENT 'the supervising Kryetar',
  kind                VARCHAR(10)      NOT NULL COMMENT 'LAWYER | CASE',
  lawyer_user_id      INT UNSIGNED     NULL COMMENT 'kind LAWYER: the cases where this user is the current primary',
  case_id             BIGINT UNSIGNED  NULL COMMENT 'kind CASE',
  created_by          INT UNSIGNED     NOT NULL COMMENT 'the Monitor',
  created_at          DATETIME(3)      NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  ended_at            DATETIME(3)      NULL,
  ended_by            INT UNSIGNED     NULL,
  active_lawyer_key   VARCHAR(30) AS (IF(ended_at IS NULL AND kind = 'LAWYER', CONCAT(head_user_id, ':', lawyer_user_id), NULL)) STORED,
  active_case_key     VARCHAR(30) AS (IF(ended_at IS NULL AND kind = 'CASE', CONCAT(head_user_id, ':', case_id), NULL)) STORED,
  PRIMARY KEY (id),
  UNIQUE KEY uq_sv_public (public_id),
  UNIQUE KEY uq_sv_active_lawyer (active_lawyer_key),
  UNIQUE KEY uq_sv_active_case (active_case_key),
  KEY ix_sv_head (head_user_id, ended_at, kind),
  KEY ix_sv_lawyer (lawyer_user_id, ended_at),
  KEY ix_sv_case (case_id, ended_at),
  CONSTRAINT fk_sv_head    FOREIGN KEY (head_user_id)   REFERENCES users (id),
  CONSTRAINT fk_sv_lawyer  FOREIGN KEY (lawyer_user_id) REFERENCES users (id),
  CONSTRAINT fk_sv_case    FOREIGN KEY (case_id)        REFERENCES cases (id),
  CONSTRAINT fk_sv_created FOREIGN KEY (created_by)     REFERENCES users (id),
  CONSTRAINT fk_sv_ended   FOREIGN KEY (ended_by)       REFERENCES users (id),
  CONSTRAINT ck_sv_kind CHECK ((kind = 'LAWYER' AND lawyer_user_id IS NOT NULL AND case_id IS NULL)
                            OR (kind = 'CASE' AND case_id IS NOT NULL AND lawyer_user_id IS NULL))
) ENGINE=InnoDB COMMENT='Mbikëqyrja: Kryetari mbikëqyr një avokat ose një çështje';

INSERT IGNORE INTO permissions (code, scope, description) VALUES
 ('supervision.manage',   'SYSTEM', 'Cakton dhe përfundon mbikëqyrjen e Kryetarëve (Monitoruesi)'),
 ('menu.supervision',     'SYSTEM', 'Mbikëqyrja'),
 ('case.view_supervised', 'SYSTEM', 'Sheh dhe vepron në çështjet në mbikëqyrje si në ato të zyrës (Kryetari)');

INSERT IGNORE INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id FROM roles r JOIN permissions p ON p.code IN ('supervision.manage', 'menu.supervision')
 WHERE r.code = 'MONITOR';

INSERT IGNORE INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id FROM roles r JOIN permissions p ON p.code = 'case.view_supervised'
 WHERE r.code = 'OFFICE_HEAD';
