-- =====================================================================
-- Task 053l — ONE import pipeline for the Excel / CSV imports (the 053g Organika design): upload → validation →
-- preview → "Apliko" in one transaction → a downloadable result report. org_imports keeps every import of every kind:
--   kind        ORGANIKA (positions, units, managers) | USERS (create / update users)
--   result      the rows as "Apliko" left them (status + message each) — the result report; NULL until applied
-- The file limit grows from 2 MB to 5 MB (MEDIUMBLOB holds 16 MB). Existing rows are Organika imports.
-- =====================================================================
USE ash_core;

ALTER TABLE org_imports
  ADD COLUMN kind   VARCHAR(20) NOT NULL DEFAULT 'ORGANIKA' COMMENT 'ORGANIKA | USERS — which import' AFTER public_id,
  ADD COLUMN result JSON        NULL     COMMENT 'the rows after "Apliko": line, key, status, message (the result report)' AFTER applied_count,
  ADD CONSTRAINT ck_oi_kind CHECK (kind IN ('ORGANIKA', 'USERS')),
  ADD KEY ix_oi_kind (kind, uploaded_at),
  MODIFY COLUMN file_content MEDIUMBLOB NOT NULL COMMENT 'the uploaded sheet, kept with the import (≤ 5 MB)',
  COMMENT = 'Importet nga Excel / CSV (Organika, përdoruesit): skedari, kush, kur, përmbledhja, rezultati';
