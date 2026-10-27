USE ash_core;

-- Users are created by the administrator before their first login, so the identity
-- provider subject is unknown until then. It is filled on first login (matched by email).
-- The UNIQUE key uq_users_subject stays: MySQL allows many NULLs in a unique index.
ALTER TABLE users
  MODIFY COLUMN external_subject VARCHAR(255) NULL COMMENT 'sub/oid claim from the gov identity provider; NULL until first login';
