-- =====================================================================
-- ASH — 05: chat (schema ash_chat)
--
-- Why MySQL and not a separate NoSQL store: realistic volume for this
-- system is in the tens of millions of messages over years, which a
-- partitioned InnoDB table handles easily — and the team then maintains
-- one database technology, one backup procedure, one monitoring setup.
-- Kept in its own schema with NO foreign keys to ash_core, so it can move
-- to a dedicated MySQL server later with zero model changes.
--
-- Who may talk to whom is NOT decided here (open topic). The
-- conversations/participants model supports both "open to all" and
-- "restricted" — the rule lives in the chat service.
-- Real-time delivery: SignalR (with a Redis backplane when the API runs on
-- more than one node). The DB is the source of truth; SignalR only pushes.
-- =====================================================================
USE ash_chat;

CREATE TABLE conversations (
  id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  public_id         BINARY(16)      NOT NULL DEFAULT (UUID_TO_BIN(UUID(), 1)),
  conversation_type VARCHAR(10)     NOT NULL COMMENT 'DIRECT (1:1) | CASE (everyone on a case) | GROUP',
  case_id           BIGINT UNSIGNED NULL COMMENT 'ash_core.cases.id — logical reference, no FK (separate schema)',
  direct_pair_key   VARCHAR(25)     NULL COMMENT '"<smaller user id>:<larger user id>" for DIRECT — guarantees one 1:1 thread per pair',
  title             VARCHAR(200)    NULL,
  is_read_only      TINYINT(1)      NOT NULL DEFAULT 0 COMMENT 'e.g. case chat after the dossier is archived',
  created_by        INT UNSIGNED    NOT NULL,
  created_at        DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  last_message_id   BIGINT UNSIGNED NULL,
  last_message_at   DATETIME(3)     NULL COMMENT 'sorts the conversation list without touching messages',
  -- one CASE conversation per case
  case_slot         BIGINT UNSIGNED AS (IF(conversation_type = 'CASE', case_id, NULL)) STORED,
  PRIMARY KEY (id),
  UNIQUE KEY uq_conv_public (public_id),
  UNIQUE KEY uq_conv_direct (direct_pair_key),
  UNIQUE KEY uq_conv_case (case_slot),
  CONSTRAINT ck_conv_type   CHECK (conversation_type IN ('DIRECT','CASE','GROUP')),
  CONSTRAINT ck_conv_direct CHECK ((conversation_type = 'DIRECT') = (direct_pair_key IS NOT NULL)),
  CONSTRAINT ck_conv_case   CHECK ((conversation_type = 'CASE') = (case_id IS NOT NULL))
) ENGINE=InnoDB;

-- Read state = ONE pointer per participant (last_read_message_id), not one
-- row per message per reader. With 10 readers per message that is the
-- difference between 1 row and 10 rows for every message ever sent.
-- Unread count = messages in the conversation with id > last_read_message_id
-- (index range scan on ix_msg_conv; capped at "99+" in the UI).
CREATE TABLE conversation_participants (
  conversation_id      BIGINT UNSIGNED NOT NULL,
  user_id              INT UNSIGNED    NOT NULL COMMENT 'ash_core.users.id',
  joined_at            DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  left_at              DATETIME(3)     NULL COMMENT 'e.g. lawyer transferred off the case: keeps history, stops new messages',
  last_read_message_id BIGINT UNSIGNED NOT NULL DEFAULT 0,
  last_read_at         DATETIME(3)     NULL,
  is_muted             TINYINT(1)      NOT NULL DEFAULT 0,
  PRIMARY KEY (conversation_id, user_id),
  KEY ix_cp_user (user_id, left_at, conversation_id) COMMENT 'my conversations',
  CONSTRAINT fk_cpart_conv FOREIGN KEY (conversation_id) REFERENCES conversations (id)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- Messages: RANGE-partitioned by month on created_at.
--   * Old months can be archived/exported and dropped in milliseconds
--     (DROP / EXCHANGE PARTITION) instead of slow DELETEs that bloat the table.
--   * Queries carrying a created_at bound touch only the relevant months.
-- MySQL rules for partitioned tables: no foreign keys, and every unique key
-- must contain created_at -> PRIMARY KEY (id, created_at). id stays
-- globally unique because it is AUTO_INCREMENT.
-- Paging (keyset, never OFFSET):
--   WHERE conversation_id = ? AND id < :cursorId AND created_at <= :cursorTs
--   ORDER BY id DESC LIMIT 50
-- Partitions are added ahead of time by ash_ops.ensure_month_partitions.
-- ---------------------------------------------------------------------
CREATE TABLE messages (
  id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  created_at          DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  conversation_id     BIGINT UNSIGNED NOT NULL,
  sender_user_id      INT UNSIGNED    NULL COMMENT 'NULL for SYSTEM messages',
  message_type        VARCHAR(10)     NOT NULL DEFAULT 'TEXT' COMMENT 'TEXT | ATTACHMENT | SYSTEM',
  body                VARCHAR(4000)   NULL COMMENT 'hard limit enforced by the API as well',
  reply_to_message_id BIGINT UNSIGNED NULL,
  edited_at           DATETIME(3)     NULL,
  deleted_at          DATETIME(3)     NULL COMMENT 'soft delete — shown as "mesazh i fshirë"',
  PRIMARY KEY (id, created_at),
  KEY ix_msg_conv (conversation_id, id),
  KEY ix_msg_sender (sender_user_id, created_at),
  CONSTRAINT ck_msg_type CHECK (message_type IN ('TEXT','ATTACHMENT','SYSTEM'))
) ENGINE=InnoDB
  PARTITION BY RANGE COLUMNS (created_at) (
    PARTITION p_old VALUES LESS THAN ('2026-11-01 00:00:00'),
    PARTITION p_max VALUES LESS THAN (MAXVALUE)
  );

-- Chat files go to their own bucket (shorter retention than case documents),
-- same upload/scan pipeline as case documents.
CREATE TABLE message_attachments (
  id                 BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  message_id         BIGINT UNSIGNED NOT NULL,
  message_created_at DATETIME(3)     NOT NULL COMMENT 'with message_id = full key of the partitioned message',
  conversation_id    BIGINT UNSIGNED NOT NULL,
  original_file_name VARCHAR(255)    NOT NULL,
  content_type       VARCHAR(100)    NOT NULL,
  size_bytes         BIGINT UNSIGNED NOT NULL,
  sha256             BINARY(32)      NOT NULL,
  storage_bucket     VARCHAR(63)     NOT NULL,
  storage_key        VARCHAR(200)    NOT NULL,
  scan_status        VARCHAR(10)     NOT NULL DEFAULT 'PENDING',
  created_at         DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_ma_object (storage_bucket, storage_key),
  KEY ix_ma_message (message_id, message_created_at),
  KEY ix_ma_conv (conversation_id, created_at),
  CONSTRAINT ck_ma_scan CHECK (scan_status IN ('PENDING','CLEAN','INFECTED','FAILED'))
) ENGINE=InnoDB;
