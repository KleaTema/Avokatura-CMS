-- =====================================================================
-- Task 049: chat backend — messages get a public id (the API never shows internal message ids).
-- The usual 3 steps (nullable column, fill, NOT NULL + keys). messages is RANGE-partitioned on created_at, and MySQL
-- requires the partition column in EVERY unique key of such a table: so the unique key is (public_id, created_at).
-- public_id is a time-ordered UUID (UUID_TO_BIN(UUID(), 1)), unique on its own in practice; the key with created_at
-- is what MySQL allows. Lookups by public id from the API go through ix_msg_conv_public (conversation_id, public_id):
-- the conversation is always known (it is in the URL or found first), so one index range per partition.
-- =====================================================================
USE ash_chat;

ALTER TABLE messages ADD COLUMN public_id BINARY(16) NULL AFTER id;

UPDATE messages SET public_id = UUID_TO_BIN(UUID(), 1) WHERE public_id IS NULL;

ALTER TABLE messages
  MODIFY COLUMN public_id BINARY(16) NOT NULL DEFAULT (UUID_TO_BIN(UUID(), 1)),
  ADD UNIQUE KEY uq_msg_public (public_id, created_at),
  ADD KEY ix_msg_conv_public (conversation_id, public_id);

-- the page query "WHERE conversation_id = ? AND (created_at, id) < (?, ?) AND created_at <= ?
-- ORDER BY created_at DESC, id DESC": one index serves the filter and the order (no filesort)
ALTER TABLE messages ADD KEY ix_msg_conv_time (conversation_id, created_at, id);
