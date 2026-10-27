-- =====================================================================
-- LOCAL DEVELOPMENT ONLY — never run on the AKSHI servers.
-- The real accounts from 01 only accept the application subnet (10.%)
-- over SSL, so your PC cannot use them. This one account lets the .NET
-- app on your PC connect to the Docker MySQL.
-- Connection string:
--   Server=localhost;Port=3306;User=ash_dev;Password=ash_dev_local;Database=ash_core;
-- =====================================================================
CREATE USER IF NOT EXISTS 'ash_dev'@'%' IDENTIFIED BY 'ash_dev_local';
GRANT ALL PRIVILEGES ON ash_core.*  TO 'ash_dev'@'%';
GRANT ALL PRIVILEGES ON ash_chat.*  TO 'ash_dev'@'%';
GRANT ALL PRIVILEGES ON ash_audit.* TO 'ash_dev'@'%';
GRANT ALL PRIVILEGES ON ash_ops.*   TO 'ash_dev'@'%';
