-- =====================================================================
-- Task 053o: the client's statistics table (Tabelat e statistikave.xlsx) has open points the client has not confirmed
-- yet. None is hard-coded: each one is DATA here, so a later client answer changes a seed row or a setting, never code.
-- Every provisional rule is marked "NË PRITJE TË KONFIRMIMIT NGA KLIENTI" in docs/statistics-definitions.md and asked
-- in docs/client-questions.md (Q-053o-*).
--
--   ash_core.statistics_result_columns   the result columns of "Rezultati i çështjeve të përfunduara" (client's order)
--   ash_core.statistics_result_mapping   decision result (+ the State's position in the case) → one of those columns
--   ash_core.report_settings             one row per other open point: its value, the allowed values, the question
--   ash_core.statistics_snapshot_cases   + state_position, recursed (a frozen year keeps the same shape as live)
--
-- Every name is schema-qualified (no USE: the journal insert and later scripts keep their schema).
-- =====================================================================

-- ---------------------------------------------------------------- result columns
CREATE TABLE ash_core.statistics_result_columns (
  code        VARCHAR(30)      NOT NULL,
  label       VARCHAR(60)      NOT NULL COMMENT 'the header text in the web table and the Excel file',
  sort_order  TINYINT UNSIGNED NOT NULL,
  is_active   TINYINT(1)       NOT NULL DEFAULT 1 COMMENT '0 = not shown (no row maps to it any more)',
  PRIMARY KEY (code)
) ENGINE=InnoDB COMMENT='Kolonat e "Rezultati i çështjeve të përfunduara" (tabelat e statistikave)';

-- the client's eight, in the client's order, + "Tjetër" for the results the client's table has no column for
-- (Q-053o-2: NË PRITJE TË KONFIRMIMIT NGA KLIENTI)
INSERT INTO ash_core.statistics_result_columns (code, label, sort_order) VALUES
 ('FITUAR',          'Fituar',          1),
 ('F_PJESERISHT',    'F.Pjesër',        2),
 ('HUMBUR',          'Humbur',          3),
 ('PUSHIM_GJYKIMI',  'Pushim gjykimi',  4),
 ('KTHIM_AKTESH',    'Kthim aktesh',    5),
 ('KTHIM_RIGJYKIM',  'Kthim rigj.',     6),
 ('MOS_KOMPETENCE',  'Mos kompetencë',  7),
 ('J_JURIDIK',       'J.juridik',       8),
 ('TJETER',          'Tjetër',          9);

-- ---------------------------------------------------------------- result mapping
-- state_position: the State's side in the case (an institution party's role group) — ANY = whatever the side.
-- The most specific row wins: (result, PLAINTIFF / DEFENDANT) before (result, ANY). A result with no row lands in TJETER.
CREATE TABLE ash_core.statistics_result_mapping (
  decision_result_code VARCHAR(40) NOT NULL,
  state_position       VARCHAR(10) NOT NULL DEFAULT 'ANY' COMMENT 'ANY | PLAINTIFF | DEFENDANT',
  column_code          VARCHAR(30) NOT NULL,
  PRIMARY KEY (decision_result_code, state_position),
  CONSTRAINT ck_srm_position CHECK (state_position IN ('ANY', 'PLAINTIFF', 'DEFENDANT')),
  CONSTRAINT fk_srm_result FOREIGN KEY (decision_result_code) REFERENCES ash_core.decision_results (code),
  CONSTRAINT fk_srm_column FOREIGN KEY (column_code) REFERENCES ash_core.statistics_result_columns (code)
) ENGINE=InnoDB COMMENT='Rezultati i vendimit (dhe pozita e shtetit) → kolona e statistikave';

-- provisional (Q-053o-2): the same column whatever the State's side
INSERT INTO ash_core.statistics_result_mapping (decision_result_code, state_position, column_code) VALUES
 ('FITUAR',                 'ANY', 'FITUAR'),
 ('PJESERISHT_FITUAR',      'ANY', 'F_PJESERISHT'),
 ('PJESERISHT_HUMBUR',      'ANY', 'F_PJESERISHT'),
 ('HUMBUR',                 'ANY', 'HUMBUR'),
 ('PUSHIM_GJYKIMI',         'ANY', 'PUSHIM_GJYKIMI'),
 ('KTHIM_AKTESH',           'ANY', 'KTHIM_AKTESH'),
 ('KTHIM_RIGJYKIM',         'ANY', 'KTHIM_RIGJYKIM'),
 ('MOSKOMPETENCE_LENDORE',  'ANY', 'MOS_KOMPETENCE'),
 ('MOSKOMPETENCE_TOKESORE', 'ANY', 'MOS_KOMPETENCE'),
 ('JASHTE_JURIDIKSIONIT',   'ANY', 'J_JURIDIK'),
 ('MOSPRANIM_PADIE',        'ANY', 'TJETER'),
 ('TJETER',                 'ANY', 'TJETER');

-- ---------------------------------------------------------------- settings
CREATE TABLE ash_core.report_settings (
  setting_key    VARCHAR(60)  NOT NULL,
  setting_value  VARCHAR(40)  NOT NULL,
  allowed_values VARCHAR(200) NOT NULL COMMENT 'comma-separated; the app refuses anything else and uses the first',
  description    VARCHAR(300) NOT NULL,
  client_question VARCHAR(500) NULL COMMENT 'set while the value is provisional (docs/client-questions.md)',
  PRIMARY KEY (setting_key)
) ENGINE=InnoDB COMMENT='Rregullat e raporteve / statistikave që klienti i konfirmon';

INSERT INTO ash_core.report_settings (setting_key, setting_value, allowed_values, description, client_question) VALUES
 ('statistics.appeal_columns', 'SPLIT', 'SPLIT,COMBINED',
  'SPLIT: "Ankimuar" dhe "Rekurs" në dy kolona (si në skedarin e klientit); COMBINED: një kolonë "Ankimuar / Rekursuar".',
  'Q-053o-3a: "Ankimuar" dhe "Rekurs" janë dy kolona të veçanta (si në tabelë), apo një masë e vetme?'),
 ('statistics.carried_orphan', 'HIDE', 'HIDE,SHOW',
  'Qeliza pa grup "Mbartur nga vitet e kaluara ardhur vendimi" pranë formulës (kolonat AJ/AK të tabelës së klientit): HIDE = nuk shfaqet (e njëjta masë është kolona e fundit), SHOW = shfaqet si kolonë më vete.',
  'Q-053o-3b: Qeliza "Mbartur nga vitet e kaluara ardhur vendimi" pranë formulës, pa titull grupi, është mbetje apo një kolonë më vete?'),
 ('reports.closed_value', 'CLAIM', 'CLAIM,DECISION',
  'Kolona "Vlera" (M2 | Monedha) e raportit të çështjeve të mbyllura: CLAIM = vlera e padisë; DECISION = vlera e fituar sipas vendimit.',
  'Q-053o-5: Në raportin e çështjeve të mbyllura, "Vlera" (M2 | Monedha) është vlera e padisë apo vlera sipas vendimit?');

-- ---------------------------------------------------------------- the frozen years keep the same shape
-- state_position: PLAINTIFF / DEFENDANT / NULL (no institution party); recursed: the decision was recursed (rekurs),
-- apart from appealed (ankim). Years frozen before this script: NULL / 0 (their appealed keeps both).
ALTER TABLE ash_core.statistics_snapshot_cases
  ADD COLUMN state_position VARCHAR(10) NULL COMMENT 'PLAINTIFF | DEFENDANT — the State''s side (task 053o)' AFTER result_id,
  ADD COLUMN recursed       TINYINT(1)  NOT NULL DEFAULT 0 COMMENT 'rekurs, apart from ankim (task 053o)' AFTER appealed,
  ALGORITHM=INPLACE, LOCK=NONE;

-- no new grant: the API account already reads ash_core.* (script 001); the app only reads these three tables — a
-- client answer is a new migration script (seed rows / a setting's value)
