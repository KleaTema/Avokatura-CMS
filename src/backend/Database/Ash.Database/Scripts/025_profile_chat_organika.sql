-- =====================================================================
-- Task 053g — client decisions 12.11.2026, section E.
--   * Profile: no phone, no notification preferences — every notification is mandatory (users.phone and
--     user_notification_prefs are dropped; tasks 048's columns).
--   * Chat: chat.admin — the administrator opens any case chat from the case page and joins with the first message.
--   * Organika: the position, the unit (text, as the HR sheet writes it — NOT the office, which decides access) and
--     the manager of every user, imported from an Excel/CSV sheet (Administrimi → Organika). Every import is kept
--     (the file, by, at, summary) and every change written per user (user_org_history).
--     TODO(akshi-q-hrmis): an integration with the public administration's HR system instead of the sheet.
-- =====================================================================
USE ash_core;

ALTER TABLE users
  DROP COLUMN phone,
  ADD COLUMN position        VARCHAR(200) NULL COMMENT 'Pozicioni (Organika, task 053g)',
  ADD COLUMN org_unit        VARCHAR(200) NULL COMMENT 'Njësia / zyra as the HR sheet names it (text; not users.office_id)',
  ADD COLUMN manager_user_id INT UNSIGNED NULL COMMENT 'Eprori (Organika)',
  ADD CONSTRAINT fk_users_manager FOREIGN KEY (manager_user_id) REFERENCES users (id);

DROP TABLE user_notification_prefs;

CREATE TABLE org_imports (
  id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  public_id     BINARY(16)      NOT NULL DEFAULT (UUID_TO_BIN(UUID(), 1)),
  file_name     VARCHAR(255)    NOT NULL,
  content_type  VARCHAR(100)    NOT NULL,
  file_content  MEDIUMBLOB      NOT NULL COMMENT 'the uploaded sheet, kept with the import (≤ 2 MB)',
  summary       JSON            NOT NULL COMMENT 'the preview counts: matched, changed, unchanged, emailNotFound, duplicate …',
  uploaded_by   INT UNSIGNED    NOT NULL,
  uploaded_at   DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  applied_by    INT UNSIGNED    NULL,
  applied_at    DATETIME(3)     NULL,
  applied_count INT UNSIGNED    NULL COMMENT 'users changed by "Apliko"',
  PRIMARY KEY (id),
  UNIQUE KEY uq_oi_public (public_id),
  KEY ix_oi_uploaded (uploaded_at),
  CONSTRAINT fk_oi_uploaded FOREIGN KEY (uploaded_by) REFERENCES users (id),
  CONSTRAINT fk_oi_applied  FOREIGN KEY (applied_by)  REFERENCES users (id)
) ENGINE=InnoDB COMMENT='Importet e Organikës (skedari, kush, kur, përmbledhja)';

CREATE TABLE user_org_history (
  id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  user_id             INT UNSIGNED    NOT NULL,
  import_id           BIGINT UNSIGNED NOT NULL,
  old_position        VARCHAR(200)    NULL,
  new_position        VARCHAR(200)    NULL,
  old_org_unit        VARCHAR(200)    NULL,
  new_org_unit        VARCHAR(200)    NULL,
  old_manager_user_id INT UNSIGNED    NULL,
  new_manager_user_id INT UNSIGNED    NULL,
  changed_by          INT UNSIGNED    NOT NULL,
  changed_at          DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY ix_uoh_user (user_id, changed_at),
  KEY ix_uoh_import (import_id),
  CONSTRAINT fk_uoh_user    FOREIGN KEY (user_id)    REFERENCES users (id),
  CONSTRAINT fk_uoh_import  FOREIGN KEY (import_id)  REFERENCES org_imports (id),
  CONSTRAINT fk_uoh_changed FOREIGN KEY (changed_by) REFERENCES users (id)
) ENGINE=InnoDB COMMENT='Ndryshimet e pozicionit / njësisë / eprorit për çdo përdorues';

INSERT IGNORE INTO permissions (code, scope, description) VALUES
 ('chat.admin', 'SYSTEM', 'Hap bisedën e çdo çështjeje; bëhet pjesëmarrës me mesazhin e parë (Administratori)');

INSERT IGNORE INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id FROM roles r JOIN permissions p ON p.code = 'chat.admin' WHERE r.code = 'ADMIN';
