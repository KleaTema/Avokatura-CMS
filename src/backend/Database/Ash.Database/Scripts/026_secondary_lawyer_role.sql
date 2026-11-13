-- =====================================================================
-- Task 053i — client correction (13.11.2026): "Avokat Shteti (primar)" and "Avokat sekondar" are two SEPARATE system
-- roles of the office (a user may hold both). STATE_LAWYER keeps its code, gets the new display name; SECONDARY_LAWYER
-- is new, with the same menus and system permissions as STATE_LAWYER (dashboard, çështje, njoftime, profili, chat,
-- agjenda, raporte dhe statistika) — whether the statistics answer is decided per scope (StatisticsScope: a
-- SECONDARY_LAWYER without STATE_LAWYER → 403). The case-level rights stay per CASE role (the SECONDARY_LAWYER case
-- profile, incl. D1). Primary on a case only with STATE_LAWYER, secondary only with SECONDARY_LAWYER (the handlers).
-- Idempotent (INSERT IGNORE on the unique keys).
-- =====================================================================
USE ash_core;

UPDATE roles SET name = 'Avokat Shteti (primar)', description = 'Avokati primar i çështjeve — caktohet nga Kryetari'
 WHERE code = 'STATE_LAWYER';

INSERT IGNORE INTO roles (code, name, description) VALUES
 ('SECONDARY_LAWYER', 'Avokat sekondar', 'Avokati sekondar i çështjeve — caktohet nga Kryetari');

-- the same system permissions as STATE_LAWYER (menus + report.view_own)
INSERT IGNORE INTO role_permissions (role_id, permission_id)
SELECT s.id, rp.permission_id
  FROM roles s
  JOIN roles l ON l.code = 'STATE_LAWYER'
  JOIN role_permissions rp ON rp.role_id = l.id
 WHERE s.code = 'SECONDARY_LAWYER';
