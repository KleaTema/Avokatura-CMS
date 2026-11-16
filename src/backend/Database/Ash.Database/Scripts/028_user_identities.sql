-- =====================================================================
-- Task 051 — production identity, one or more providers (Azure AD B2C, Entra ID — AKSHI's choice, by configuration).
-- A user (created only by the administrator) may sign in through several providers; each sign-in identity is a row
-- here, keyed by (provider_name, subject). The first login at a provider links by e-mail (an active user, case
-- insensitive) and is audited (IDENTITY_LINKED); later logins find the user by (provider, subject) only, so an e-mail
-- change at the provider does not break the link. The administrator can unlink a row ("Shkëput", IDENTITY_UNLINKED).
-- users.external_subject is DEPRECATED (replaced by this table): kept, no longer read or written; to be dropped by a
-- cleanup script after the first production release. No data is copied: no production data yet, dev users have none.
-- Idempotent (IF NOT EXISTS).
-- =====================================================================
USE ash_core;

CREATE TABLE IF NOT EXISTS user_identities (
  id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  public_id     BINARY(16)      NOT NULL DEFAULT (UUID_TO_BIN(UUID(), 1)),
  user_id       INT UNSIGNED    NOT NULL,
  provider_name VARCHAR(50)     NOT NULL COMMENT 'Auth:Providers[].Name',
  subject       VARCHAR(200)    NOT NULL COMMENT 'the provider''s stable id (oid, else sub)',
  email_at_link VARCHAR(255)    NULL     COMMENT 'the e-mail of the token at the first login (for the record)',
  linked_at     DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  last_login_at DATETIME(3)     NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_ui_public (public_id),
  UNIQUE KEY uq_ui_provider_subject (provider_name, subject),
  KEY ix_ui_user (user_id),
  CONSTRAINT fk_ui_user FOREIGN KEY (user_id) REFERENCES users (id)
) ENGINE=InnoDB COMMENT='Identitetet e hyrjes së përdoruesit (një për ofrues)';
