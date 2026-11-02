USE ash_core;

-- Task 030: hearings are addressed by the API (PUT /api/cases/{id}/hearings/{hearingId}, …/complete),
-- so they need a public id; the internal BIGINT never leaves the server.
-- Three steps, as 009 and 012: a one-step NOT NULL DEFAULT (UUID…) fails with error 1674 under binary logging.

-- 1. add the column as nullable
ALTER TABLE case_hearings
  ADD COLUMN public_id BINARY(16) NULL AFTER id;

-- 2. give every existing hearing its own id
UPDATE case_hearings SET public_id = UUID_TO_BIN(UUID(), 1) WHERE public_id IS NULL;

-- 3. make it required, with a default for new rows, and unique
ALTER TABLE case_hearings
  MODIFY COLUMN public_id BINARY(16) NOT NULL DEFAULT (UUID_TO_BIN(UUID(), 1)) COMMENT 'Identifikues publik i seancës',
  ADD UNIQUE KEY uq_ch_public (public_id);
