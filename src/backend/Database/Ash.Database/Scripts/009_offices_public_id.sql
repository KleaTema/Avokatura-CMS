USE ash_core;

-- 1. add the column as nullable
ALTER TABLE offices
  ADD COLUMN public_id BINARY(16) NULL AFTER id;

-- 2. give every existing office its own id
UPDATE offices SET public_id = UUID_TO_BIN(UUID(), 1) WHERE public_id IS NULL;

-- 3. make it required, with a default for new rows, and unique
ALTER TABLE offices
  MODIFY COLUMN public_id BINARY(16) NOT NULL DEFAULT (UUID_TO_BIN(UUID(), 1)) COMMENT 'Identifikues publik i zyrës',
  ADD UNIQUE KEY uq_offices_public (public_id);
