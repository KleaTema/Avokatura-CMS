-- =====================================================================
-- Task 053j — the Protokoll logic (client decisions, docs/client/decisions-2026-11-12.md, 053j a–d).
--
-- (a) "Përcjellja e çështjes" (task 053c) is REMOVED: case_handoffs is dropped (no production data yet). office_courts
--     (court → office) STAYS: it now pre-fills "Zyra që e ndjek" at registration and continuation. email_outbox STAYS
--     (unused for now). The history codes CASE_HANDED_OFF / HANDOFF_CHANGED / HANDOFF_CANCELLED are no longer written.
-- (c) Continuing a case copies only the object and the parties whose role has copy_to_next_level = 1 (the plaintiff
--     and the defendant); editable in Administrimi → Listat → Lloji i palës.
-- Expand-then-contract: the 053c API version still running during an upgrade would fail on the dropped table; there
--     is no production yet (decision 053j), so the drop is done now.
-- =====================================================================
USE ash_core;

DROP TABLE case_handoffs;

ALTER TABLE party_roles
  ADD COLUMN copy_to_next_level TINYINT(1) NOT NULL DEFAULT 0
      COMMENT 'Kopjohet në shkallën e radhës: 1 = the party is copied when the case continues (task 053j)' AFTER report_group;

UPDATE party_roles SET copy_to_next_level = 1 WHERE code IN ('PADITES', 'I_PADITUR');
