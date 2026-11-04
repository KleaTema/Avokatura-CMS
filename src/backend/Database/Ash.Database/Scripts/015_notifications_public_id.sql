USE ash_core;

-- Task 040: notifications are addressed by the API (POST /api/notifications/{id}/read) and pushed over SignalR,
-- so they need a public id; the internal BIGINT never leaves the server.
-- Three steps, as 009, 012 and 013: a one-step NOT NULL DEFAULT (UUID…) fails with error 1674 under binary logging.

-- 1. add the column as nullable
ALTER TABLE notifications
  ADD COLUMN public_id BINARY(16) NULL AFTER id;

-- 2. give every existing notification its own id
UPDATE notifications SET public_id = UUID_TO_BIN(UUID(), 1) WHERE public_id IS NULL;

-- 3. make it required, with a default for new rows, and unique
ALTER TABLE notifications
  MODIFY COLUMN public_id BINARY(16) NOT NULL DEFAULT (UUID_TO_BIN(UUID(), 1)) COMMENT 'Identifikues publik i njoftimit',
  ADD UNIQUE KEY uq_notif_public (public_id);
