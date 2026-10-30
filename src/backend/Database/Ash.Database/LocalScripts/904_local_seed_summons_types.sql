-- =====================================================================
-- LOCAL DEVELOPMENT ONLY — never run on the AKSHI servers.
-- Test data only: summons types (Tipi i fletëthirrjes) so the registration
-- form can be tried locally. The real list comes from the client and is
-- entered by an admin. WHERE NOT EXISTS keeps it safe when an admin (or a
-- test) has already added a type with the same code.
-- =====================================================================
USE ash_core;

INSERT INTO summons_types (code, name)
SELECT v.code, v.name
FROM (
  SELECT 'SEANCE_PERGATITORE' AS code, 'Fletëthirrje për seancë përgatitore' AS name
  UNION ALL SELECT 'SEANCE_GJYQESORE', 'Fletëthirrje për seancë gjyqësore'
  UNION ALL SELECT 'NJOFTIM_VENDIMI',  'Njoftim për vendimin'
  UNION ALL SELECT 'NJOFTIM_ANKIMI',   'Njoftim për ankimin'
  UNION ALL SELECT 'TJETER',           'Tjetër'
) v
WHERE NOT EXISTS (SELECT 1 FROM summons_types s WHERE s.code = v.code);
