-- =====================================================================
-- LOCAL DEVELOPMENT ONLY — never run on the AKSHI servers.
-- The production account ash_chat_app (script 001) only accepts 10.% over SSL. This account has the SAME rights on
-- ash_chat (SELECT, INSERT, UPDATE — no DELETE: messages are deleted softly, participants get left_at) and none on
-- ash_core: the Chat module reads cases and users through the other modules' contracts (the API's own account).
-- Task 049. Connection string (user-secrets ConnectionStrings:Chat):
--   Server=localhost;Port=3306;User=ash_chat_local;Password=ash_chat_local_pw;Database=ash_chat;
-- =====================================================================
CREATE USER IF NOT EXISTS 'ash_chat_local'@'%' IDENTIFIED BY 'ash_chat_local_pw';
GRANT SELECT, INSERT, UPDATE ON ash_chat.* TO 'ash_chat_local'@'%';
