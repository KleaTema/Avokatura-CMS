USE ash_core;

-- Task 017: parties are addressed by the API (PUT/DELETE /api/cases/{caseId}/parties/{partyId}),
-- so they need a public id; the internal BIGINT never leaves the server.
-- Three steps (rule 13): a one-step NOT NULL DEFAULT (UUID…) fails with error 1674 under binary logging.

-- 1. add the column as nullable
ALTER TABLE case_parties
  ADD COLUMN public_id BINARY(16) NULL AFTER id;

-- 2. give every existing party its own id
UPDATE case_parties SET public_id = UUID_TO_BIN(UUID(), 1) WHERE public_id IS NULL;

-- 3. make it required, with a default for new rows, and unique
ALTER TABLE case_parties
  MODIFY COLUMN public_id BINARY(16) NOT NULL DEFAULT (UUID_TO_BIN(UUID(), 1)) COMMENT 'Identifikues publik i palës',
  ADD UNIQUE KEY uq_cp_public (public_id);
