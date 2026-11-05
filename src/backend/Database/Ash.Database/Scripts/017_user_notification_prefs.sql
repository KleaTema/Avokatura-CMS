-- =====================================================================
-- Task 048: profile — the notification types a user turned off.
-- Owned by the Notifications module (docs/architecture.md): NotificationSender reads it to skip a recipient.
-- A row exists only for a choice the user made; no row = the type's default (on). Mandatory types never get a row
-- (the API refuses to turn them off). users.phone already exists (002, VARCHAR(30)): no change there.
-- =====================================================================
USE ash_core;

CREATE TABLE IF NOT EXISTS user_notification_prefs (
  user_id           INT UNSIGNED NOT NULL,
  notification_type VARCHAR(40)  NOT NULL COMMENT 'NotificationTypes code, as notifications.notification_type',
  enabled           TINYINT(1)   NOT NULL,
  updated_at        DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (user_id, notification_type),
  CONSTRAINT fk_unp_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='Preferencat e njoftimeve të përdoruesit';

GRANT SELECT, INSERT, UPDATE, DELETE ON ash_core.user_notification_prefs TO 'ash_app'@'10.%';
