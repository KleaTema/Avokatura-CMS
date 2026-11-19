-- =====================================================================
-- Task 053p: "Eprori" (the manager of a user, task 053g / script 025) is removed completely — from the profile, the
-- Organika import and the API. Its columns go: users.manager_user_id (with its foreign key) and the before / after
-- pair of user_org_history. No production data yet: nothing to keep.
--
-- Every name is schema-qualified (no USE: the journal insert and later scripts keep their schema).
-- =====================================================================

ALTER TABLE ash_core.users
  DROP FOREIGN KEY fk_users_manager;

ALTER TABLE ash_core.users
  DROP COLUMN manager_user_id;

ALTER TABLE ash_core.user_org_history
  DROP COLUMN old_manager_user_id,
  DROP COLUMN new_manager_user_id,
  COMMENT = 'Ndryshimet e pozicionit / njësisë për çdo përdorues';
