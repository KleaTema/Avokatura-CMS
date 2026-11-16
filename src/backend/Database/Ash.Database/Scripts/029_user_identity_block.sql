-- =====================================================================
-- Task 051 (item 8, decision 16.11.2026): "Shkëput" BLOCKS one sign-in account instead of deleting its link.
--   The row stays as a tombstone of (provider_name, subject): from the next request that account gets 401 and it is
--   never re-linked by e-mail. The administrator can lift the block ("Lejo sërish", with a reason) — the row is then
--   removed and the next login links again by e-mail. Both actions are audited (IDENTITY_UNLINKED / IDENTITY_ALLOWED).
--   To shut a person out completely, the administrator deactivates the user instead.
-- =====================================================================
USE ash_core;

ALTER TABLE user_identities
  ADD COLUMN blocked_at DATETIME(3)  NULL COMMENT 'Shkëputur: this account may not sign in (tombstone)' AFTER last_login_at,
  ADD COLUMN blocked_by INT UNSIGNED NULL COMMENT 'the administrator who unlinked it' AFTER blocked_at,
  ADD CONSTRAINT fk_ui_blocked_by FOREIGN KEY (blocked_by) REFERENCES users (id);
