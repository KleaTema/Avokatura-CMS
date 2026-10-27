-- =====================================================================
-- ASH — 03: dossiers, cases, team, parties, hearings, decisions
-- Schema: ash_core
--
-- Core model (Dosja X -> Çështje X/1, X/2 ... X/n):
--   dossiers  one row per real-world dispute. Holds the UNIQUE CODE
--             (kodi unik), never changes, owns all documents.
--   cases     one row per trial at a court level (shkallë gjykimi).
--             X/1 first instance, X/2 appeal, X/3 High Court, or again
--             first instance after "kthim për rigjykim". level_seq is
--             "the n-th trial in this dossier", NOT the court level,
--             so retrials and returns are naturally supported.
--   Creating X/2 never modifies X/1: X/1 stays closed exactly as it was
--   (statistics per level stay correct), X/2 copies what it needs and
--   points back to it with previous_case_id.
-- =====================================================================
USE ash_core;

-- ---------------------------------------------------------------------
-- Unique code format (Kodi unik). Agreed format: current year + 6 random
-- digits, e.g. 2026-483915. Kept as DATA so it can change without a
-- release: add a new row, mark it active; existing dossiers keep the code
-- (and format) they were given — a code is never rewritten.
-- Template tokens understood by generate_dossier_code (file 04):
--   {YYYY}    current year (UTC)
--   {RAND}    random_digits random digits, zero-padded
--   {OFFICE}  code of the registering office (offices.code)
-- "Never the same" is guaranteed by UNIQUE(unique_code), not by luck:
-- a repeated random number is detected and a new one is drawn.
-- ---------------------------------------------------------------------
CREATE TABLE dossier_code_formats (
  id             TINYINT UNSIGNED NOT NULL AUTO_INCREMENT,
  template       VARCHAR(40)      NOT NULL COMMENT 'e.g. {YYYY}-{RAND}',
  random_digits  TINYINT UNSIGNED NOT NULL DEFAULT 6,
  is_active      TINYINT(1)       NOT NULL DEFAULT 0,
  created_at     DATETIME(3)      NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  note           VARCHAR(300)     NULL,
  -- only ONE active format at a time
  active_slot    TINYINT UNSIGNED AS (IF(is_active = 1, 1, NULL)) STORED,
  PRIMARY KEY (id),
  UNIQUE KEY uq_dcf_active (active_slot),
  CONSTRAINT ck_dcf_digits   CHECK (random_digits BETWEEN 4 AND 12),
  CONSTRAINT ck_dcf_template CHECK (template LIKE '%{RAND}%')
) ENGINE=InnoDB COMMENT='Formati i kodit unik (i ndryshueshëm)';

CREATE TABLE dossiers (
  id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  public_id        BINARY(16)      NOT NULL DEFAULT (UUID_TO_BIN(UUID(), 1)),
  unique_code      VARCHAR(30)     NOT NULL COMMENT 'Kodi unik, e.g. 2026-483915',
  code_format_id   TINYINT UNSIGNED NOT NULL COMMENT 'format used when the code was issued',
  office_id        SMALLINT UNSIGNED NOT NULL COMMENT 'office that registered it',
  opened_at        DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  opened_by        INT UNSIGNED    NOT NULL,
  archived_at      DATETIME(3)     NULL COMMENT 'Arkivuar: closed with a final decision (formë e prerë)',
  archived_by      INT UNSIGNED    NULL,
  created_at       DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at       DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  row_version      INT UNSIGNED    NOT NULL DEFAULT 0,
  PRIMARY KEY (id),
  UNIQUE KEY uq_dossiers_public (public_id),
  UNIQUE KEY uq_dossiers_code (unique_code),
  KEY ix_dossiers_office (office_id, archived_at),
  CONSTRAINT fk_dossiers_format   FOREIGN KEY (code_format_id) REFERENCES dossier_code_formats (id),
  CONSTRAINT fk_dossiers_office   FOREIGN KEY (office_id)   REFERENCES offices (id),
  CONSTRAINT fk_dossiers_opened   FOREIGN KEY (opened_by)   REFERENCES users (id),
  CONSTRAINT fk_dossiers_archived FOREIGN KEY (archived_by) REFERENCES users (id),
  CONSTRAINT ck_dossiers_archive  CHECK ((archived_at IS NULL) = (archived_by IS NULL))
) ENGINE=InnoDB COMMENT='Dosja';

CREATE TABLE cases (
  id                  BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  public_id           BINARY(16)       NOT NULL DEFAULT (UUID_TO_BIN(UUID(), 1)),
  dossier_id          BIGINT UNSIGNED  NOT NULL,
  level_seq           SMALLINT UNSIGNED NOT NULL COMMENT 'n in X/n',
  case_code           VARCHAR(40)      NOT NULL COMMENT 'X/n, e.g. 2026-483915/2 — set by the API',
  previous_case_id    BIGINT UNSIGNED  NULL COMMENT 'closed case this one continues (apel, rekurs, rigjykim)',
  office_id           SMALLINT UNSIGNED NOT NULL COMMENT 'routes the case to that office''s Kryetar',

  -- Registration (fletëthirrja) — Specialisti/Protokolli
  summons_number      VARCHAR(50)      NOT NULL COMMENT 'Nr. i fletëthirrjes',
  summons_type_id     TINYINT UNSIGNED NULL,
  act_number          VARCHAR(50)      NOT NULL COMMENT 'Nr. i aktit (dhënë nga gjykata)',
  act_date            DATE             NULL     COMMENT 'Data e aktit',
  received_date       DATE             NOT NULL COMMENT 'Data e hyrjes në institucion',
  court_level_id      TINYINT UNSIGNED NOT NULL,
  court_id            SMALLINT UNSIGNED NOT NULL,
  judge_id            INT UNSIGNED     NULL COMMENT 'required by the UI; nullable only because the KLGJ list may lag behind',
  object_text         TEXT             NOT NULL COMMENT 'Objekti — current value; every change goes to case_history_events',

  -- Classification & state
  lifecycle_status    VARCHAR(20)      NOT NULL DEFAULT 'REGISTERED'
                      COMMENT 'REGISTERED (te Kryetari, pa avokat) | ACTIVE | CLOSED',
  category_id         TINYINT UNSIGNED NULL COMMENT 'Ndjekje / Delegim / Relatim',
  subcategory_id      TINYINT UNSIGNED NULL COMMENT 'PA KOD / KOD A AVSH / KOD A KM (only for Ndjekje)',
  priority            VARCHAR(10)      NOT NULL DEFAULT 'NONE' COMMENT 'NONE | LOW | MEDIUM | HIGH',
  is_suspended        TINYINT(1)       NOT NULL DEFAULT 0 COMMENT 'Statusi: Pezulluar',
  suspended_at        DATETIME(3)      NULL,
  has_security_measure TINYINT(1)      NOT NULL DEFAULT 0 COMMENT 'Masë sigurimi po/jo (details: case_security_measures)',

  -- Claim value (Vlera e padisë): money and/or square metres
  claim_amount        DECIMAL(18,2)    NULL,
  claim_currency      CHAR(3)          NULL COMMENT 'ISO 4217, ALL by default',
  claim_area_m2       DECIMAL(14,2)    NULL,

  -- Denormalised for fast lists, agenda and reports (maintained by the API
  -- in the same transaction as the hearing/document change).
  next_hearing_at     DATETIME(3)      NULL COMMENT 'Seanca e radhës',
  last_hearing_id     BIGINT UNSIGNED  NULL COMMENT 'for "përshkrimi i seancës së fundit" in reports',
  first_data_entry_at DATETIME(3)      NULL COMMENT 'first doc/data by the lawyer — stops the 30-day reminder',

  registered_by       INT UNSIGNED     NOT NULL,
  registered_at       DATETIME(3)      NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  assigned_at         DATETIME(3)      NULL COMMENT 'first primary lawyer assigned (start of 30-day window)',
  closed_at           DATETIME(3)      NULL,
  closed_by           INT UNSIGNED     NULL,
  created_at          DATETIME(3)      NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at          DATETIME(3)      NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  row_version         INT UNSIGNED     NOT NULL DEFAULT 0,

  PRIMARY KEY (id),
  UNIQUE KEY uq_cases_public (public_id),
  UNIQUE KEY uq_cases_code (case_code),
  UNIQUE KEY uq_cases_dossier_level (dossier_id, level_seq),
  KEY ix_cases_office_status (office_id, lifecycle_status, registered_at),
  KEY ix_cases_status_closed (lifecycle_status, closed_at),
  KEY ix_cases_act (act_number),
  KEY ix_cases_summons (summons_number),
  KEY ix_cases_court (court_id, lifecycle_status),
  KEY ix_cases_judge (judge_id),
  KEY ix_cases_category (category_id, subcategory_id, lifecycle_status),
  KEY ix_cases_next_hearing (next_hearing_at),
  KEY ix_cases_reminder (lifecycle_status, first_data_entry_at, assigned_at),
  KEY ix_cases_previous (previous_case_id),
  FULLTEXT KEY ft_cases_object (object_text),

  CONSTRAINT ck_cases_status   CHECK (lifecycle_status IN ('REGISTERED','ACTIVE','CLOSED')),
  CONSTRAINT ck_cases_priority CHECK (priority IN ('NONE','LOW','MEDIUM','HIGH')),
  CONSTRAINT ck_cases_closed   CHECK ((lifecycle_status = 'CLOSED') = (closed_at IS NOT NULL)),
  CONSTRAINT ck_cases_claim    CHECK (claim_amount IS NULL OR claim_currency IS NOT NULL),
  CONSTRAINT ck_cases_level    CHECK (level_seq >= 1),

  CONSTRAINT fk_cases_dossier   FOREIGN KEY (dossier_id)       REFERENCES dossiers (id),
  CONSTRAINT fk_cases_previous  FOREIGN KEY (previous_case_id) REFERENCES cases (id),
  CONSTRAINT fk_cases_office    FOREIGN KEY (office_id)        REFERENCES offices (id),
  CONSTRAINT fk_cases_summons_t FOREIGN KEY (summons_type_id)  REFERENCES summons_types (id),
  CONSTRAINT fk_cases_level     FOREIGN KEY (court_level_id)   REFERENCES court_levels (id),
  CONSTRAINT fk_cases_court     FOREIGN KEY (court_id)         REFERENCES courts (id),
  CONSTRAINT fk_cases_judge     FOREIGN KEY (judge_id)         REFERENCES judges (id),
  CONSTRAINT fk_cases_category  FOREIGN KEY (category_id)      REFERENCES case_categories (id),
  -- composite FK: the sub-category must belong to the chosen category
  CONSTRAINT fk_cases_subcat    FOREIGN KEY (subcategory_id, category_id)
                                REFERENCES case_subcategories (id, category_id),
  CONSTRAINT fk_cases_reg_by    FOREIGN KEY (registered_by)    REFERENCES users (id),
  CONSTRAINT fk_cases_closed_by FOREIGN KEY (closed_by)        REFERENCES users (id)
) ENGINE=InnoDB COMMENT='Çështja (një gjykim në një shkallë)';

-- ---------------------------------------------------------------------
-- Who works on the case. Rows are never deleted: a transfer ends the old
-- row (ended_at, end_reason='TRANSFERRED') and inserts a new one, which is
-- exactly what the history and the "Transferuar si çështje" statistic need.
-- ---------------------------------------------------------------------
CREATE TABLE case_assignments (
  id           BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  case_id      BIGINT UNSIGNED NOT NULL,
  user_id      INT UNSIGNED    NOT NULL,
  case_role    VARCHAR(30)     NOT NULL COMMENT 'PRIMARY_LAWYER | SECONDARY_LAWYER | LEGAL_ASSISTANT | INSTITUTION_JURIST',
  assigned_by  INT UNSIGNED    NOT NULL,
  assigned_at  DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  ended_at     DATETIME(3)     NULL,
  ended_by     INT UNSIGNED    NULL,
  end_reason   VARCHAR(20)     NULL COMMENT 'TRANSFERRED | REMOVED | CASE_CLOSED',

  -- MySQL has no partial unique index; these generated columns are NULL for
  -- ended rows (NULLs never collide), so the UNIQUE keys below enforce:
  --   * at most ONE current primary and ONE current secondary per case
  --   * a user holds at most one current role in the same case
  active_lawyer_slot VARCHAR(60) AS (
      IF(ended_at IS NULL AND case_role IN ('PRIMARY_LAWYER','SECONDARY_LAWYER'),
         CONCAT(case_id, ':', case_role), NULL)) STORED,
  active_user_slot   VARCHAR(40) AS (
      IF(ended_at IS NULL, CONCAT(case_id, ':', user_id), NULL)) STORED,

  PRIMARY KEY (id),
  UNIQUE KEY uq_ca_lawyer_slot (active_lawyer_slot),
  UNIQUE KEY uq_ca_user_slot   (active_user_slot),
  KEY ix_ca_user_current (user_id, ended_at, case_id) COMMENT '"Çështjet e mia"',
  KEY ix_ca_case_current (case_id, ended_at),
  KEY ix_ca_transfers (case_role, end_reason, ended_at),
  CONSTRAINT ck_ca_role   CHECK (case_role IN ('PRIMARY_LAWYER','SECONDARY_LAWYER','LEGAL_ASSISTANT','INSTITUTION_JURIST')),
  CONSTRAINT ck_ca_end    CHECK ((ended_at IS NULL) = (end_reason IS NULL)),
  CONSTRAINT ck_ca_reason CHECK (end_reason IS NULL OR end_reason IN ('TRANSFERRED','REMOVED','CASE_CLOSED')),
  CONSTRAINT fk_ca_case     FOREIGN KEY (case_id)     REFERENCES cases (id),
  CONSTRAINT fk_ca_user     FOREIGN KEY (user_id)     REFERENCES users (id),
  CONSTRAINT fk_ca_assigned FOREIGN KEY (assigned_by) REFERENCES users (id),
  CONSTRAINT fk_ca_ended    FOREIGN KEY (ended_by)    REFERENCES users (id)
) ENGINE=InnoDB COMMENT='Avokati primar/sekondar, ndihmësi ligjor, juristi i institucionit';

-- Institutions linked to a case and HOW:
--   MONITORING = the case is followed by the State Advocate; the institution
--                assigns its jurist, adds documents and hearings ("Institucion")
--   DELEGATED  = the case was delegated to the institution ("Institucion me delegim")
-- An institution user's rights in a case = the profile of this link.
CREATE TABLE case_institutions (
  id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  case_id        BIGINT UNSIGNED NOT NULL,
  institution_id INT UNSIGNED    NOT NULL,
  link_mode      VARCHAR(20)     NOT NULL COMMENT 'MONITORING | DELEGATED',
  started_at     DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  started_by     INT UNSIGNED    NOT NULL,
  ended_at       DATETIME(3)     NULL,
  ended_by       INT UNSIGNED    NULL,
  active_slot    VARCHAR(40) AS (IF(ended_at IS NULL, CONCAT(case_id, ':', institution_id), NULL)) STORED,
  PRIMARY KEY (id),
  UNIQUE KEY uq_ci_active (active_slot),
  KEY ix_ci_institution (institution_id, ended_at, case_id),
  KEY ix_ci_case (case_id, ended_at),
  CONSTRAINT ck_ci_mode CHECK (link_mode IN ('MONITORING','DELEGATED')),
  CONSTRAINT fk_ci_case    FOREIGN KEY (case_id)        REFERENCES cases (id),
  CONSTRAINT fk_ci_inst    FOREIGN KEY (institution_id) REFERENCES institutions (id),
  CONSTRAINT fk_ci_started FOREIGN KEY (started_by)     REFERENCES users (id),
  CONSTRAINT fk_ci_ended   FOREIGN KEY (ended_by)       REFERENCES users (id)
) ENGINE=InnoDB COMMENT='Institucioni në çështje (monitorues / me delegim)';

-- ---------------------------------------------------------------------
-- Parties (Palët) — per case, copied into X/2 when the next level is
-- registered, so each level keeps the parties as they were in that trial.
-- ---------------------------------------------------------------------
CREATE TABLE case_parties (
  id             BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  case_id        BIGINT UNSIGNED  NOT NULL,
  party_role_id  TINYINT UNSIGNED NOT NULL COMMENT 'Paditës / I paditur / Person i tretë ...',
  party_kind     VARCHAR(15)      NOT NULL COMMENT 'NATURAL | LEGAL | INSTITUTION',
  institution_id INT UNSIGNED     NULL COMMENT 'set when party_kind = INSTITUTION (suggested from the list)',
  display_name   VARCHAR(300)     NOT NULL COMMENT 'Emri; for institutions a snapshot of the name at the time',
  identifier     VARCHAR(30)      NULL COMMENT 'NIPT for legal entities — helps tell apart parties with the same name',
  created_by     INT UNSIGNED     NOT NULL,
  created_at     DATETIME(3)      NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  deleted_at     DATETIME(3)      NULL,
  deleted_by     INT UNSIGNED     NULL,
  PRIMARY KEY (id),
  KEY ix_cp_case (case_id, deleted_at),
  KEY ix_cp_institution (institution_id),
  KEY ix_cp_identifier (identifier),
  FULLTEXT KEY ft_cp_name (display_name),
  CONSTRAINT ck_cp_kind CHECK (party_kind IN ('NATURAL','LEGAL','INSTITUTION')),
  CONSTRAINT ck_cp_institution CHECK ((party_kind = 'INSTITUTION') = (institution_id IS NOT NULL)),
  CONSTRAINT fk_cp_case    FOREIGN KEY (case_id)        REFERENCES cases (id),
  CONSTRAINT fk_cp_role    FOREIGN KEY (party_role_id)  REFERENCES party_roles (id),
  CONSTRAINT fk_cp_inst    FOREIGN KEY (institution_id) REFERENCES institutions (id),
  CONSTRAINT fk_cp_created FOREIGN KEY (created_by)     REFERENCES users (id),
  CONSTRAINT fk_cp_deleted FOREIGN KEY (deleted_by)     REFERENCES users (id)
) ENGINE=InnoDB COMMENT='Palët e çështjes';

-- ---------------------------------------------------------------------
-- Hearings (Seancat). One row per hearing. "Seanca e radhës" is simply the
-- next SCHEDULED row; its date is mirrored to cases.next_hearing_at.
-- The agenda (Agjenda) is a query over this table + the user's cases.
-- ---------------------------------------------------------------------
CREATE TABLE case_hearings (
  id               BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
  case_id          BIGINT UNSIGNED  NOT NULL,
  hearing_type_id  TINYINT UNSIGNED NOT NULL,
  other_type_text  VARCHAR(200)     NULL COMMENT 'when type = Tjetër',
  scheduled_at     DATETIME(3)      NOT NULL COMMENT 'data + ora e seancës (UTC)',
  status           VARCHAR(15)      NOT NULL DEFAULT 'SCHEDULED' COMMENT 'SCHEDULED | HELD | POSTPONED | CANCELLED',
  description      TEXT             NULL COMMENT 'Përshkrimi i seancës',
  created_by       INT UNSIGNED     NOT NULL,
  created_at       DATETIME(3)      NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_by       INT UNSIGNED     NULL,
  updated_at       DATETIME(3)      NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  row_version      INT UNSIGNED     NOT NULL DEFAULT 0,
  PRIMARY KEY (id),
  KEY ix_ch_case_time (case_id, scheduled_at),
  KEY ix_ch_agenda (scheduled_at, status),
  CONSTRAINT ck_ch_status CHECK (status IN ('SCHEDULED','HELD','POSTPONED','CANCELLED')),
  CONSTRAINT fk_ch_case    FOREIGN KEY (case_id)         REFERENCES cases (id),
  CONSTRAINT fk_ch_type    FOREIGN KEY (hearing_type_id) REFERENCES hearing_types (id),
  CONSTRAINT fk_ch_created FOREIGN KEY (created_by)      REFERENCES users (id),
  CONSTRAINT fk_ch_updated FOREIGN KEY (updated_by)      REFERENCES users (id)
) ENGINE=InnoDB COMMENT='Seancat';

ALTER TABLE cases
  ADD CONSTRAINT fk_cases_last_hearing FOREIGN KEY (last_hearing_id) REFERENCES case_hearings (id);

-- Security measure (Masa e sigurimit) details, 1:1 with the case.
CREATE TABLE case_security_measures (
  case_id          BIGINT UNSIGNED NOT NULL,
  decision_number  VARCHAR(50)     NOT NULL,
  decision_date    DATE            NOT NULL,
  dispositive      TEXT            NULL,
  recorded_by      INT UNSIGNED    NOT NULL,
  recorded_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at       DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (case_id),
  CONSTRAINT fk_csm_case FOREIGN KEY (case_id)     REFERENCES cases (id),
  CONSTRAINT fk_csm_user FOREIGN KEY (recorded_by) REFERENCES users (id)
) ENGINE=InnoDB COMMENT='Masa e sigurimit';

-- ---------------------------------------------------------------------
-- Decision of the case at this level (Vendimi). 1:1 with the case.
-- Can be saved before the reasoned decision arrives (decision_received = 0):
-- the case counts as CLOSED for statistics, the rest is filled later.
-- Money and m² are separate columns (never one "value" + "unit" column) so
-- statistics can SUM them directly.
-- ---------------------------------------------------------------------
CREATE TABLE case_decisions (
  case_id                    BIGINT UNSIGNED  NOT NULL,
  result_id                  TINYINT UNSIGNED NOT NULL,
  result_other_text          VARCHAR(300)     NULL,
  decision_number            VARCHAR(50)      NULL,
  decision_date              DATE             NULL,
  dispositive                TEXT             NULL COMMENT 'Dispozitivi',
  currency                   CHAR(3)          NOT NULL DEFAULT 'ALL',
  won_amount                 DECIMAL(18,2)    NULL COMMENT 'Vlera e fituar',
  won_area_m2                DECIMAL(14,2)    NULL,
  lost_amount                DECIMAL(18,2)    NULL COMMENT 'Vlera e humbur',
  lost_area_m2               DECIMAL(14,2)    NULL,
  prevented_damage_amount    DECIMAL(18,2)    NULL COMMENT 'Dëmi i parandaluar',
  prevented_damage_area_m2   DECIMAL(14,2)    NULL,
  caused_damage_amount       DECIMAL(18,2)    NULL COMMENT 'Dëmi i shkaktuar',
  caused_damage_area_m2      DECIMAL(14,2)    NULL,
  court_costs_amount         DECIMAL(18,2)    NULL COMMENT 'Vlera e shpenzimeve gjyqësore',
  decision_received          TINYINT(1)       NOT NULL DEFAULT 0 COMMENT 'Ardhur vendimi (i arsyetuar) PO/JO',
  decision_received_at       DATETIME(3)      NULL,
  is_appealed                TINYINT(1)       NOT NULL DEFAULT 0 COMMENT 'Ankimuar (shkalla I)',
  is_recursed                TINYINT(1)       NOT NULL DEFAULT 0 COMMENT 'Rekursuar (shkalla II)',
  is_final                   TINYINT(1)       NOT NULL DEFAULT 0 COMMENT 'Marrë formë të prerë / përfundimtare',
  recorded_by                INT UNSIGNED     NOT NULL,
  recorded_at                DATETIME(3)      NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_by                 INT UNSIGNED     NULL,
  updated_at                 DATETIME(3)      NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  row_version                INT UNSIGNED     NOT NULL DEFAULT 0,
  PRIMARY KEY (case_id),
  KEY ix_cd_result (result_id),
  KEY ix_cd_pending (decision_received, recorded_at) COMMENT '"mbartur në pritje të vendimit të arsyetuar"',
  CONSTRAINT ck_cd_final_vs_appeal CHECK (NOT (is_final = 1 AND (is_appealed = 1 OR is_recursed = 1))),
  CONSTRAINT ck_cd_received CHECK (decision_received = 0 OR decision_received_at IS NOT NULL),
  CONSTRAINT ck_cd_nonneg CHECK (COALESCE(won_amount,0) >= 0 AND COALESCE(lost_amount,0) >= 0
                                 AND COALESCE(prevented_damage_amount,0) >= 0 AND COALESCE(caused_damage_amount,0) >= 0
                                 AND COALESCE(court_costs_amount,0) >= 0),
  CONSTRAINT fk_cd_case    FOREIGN KEY (case_id)     REFERENCES cases (id),
  CONSTRAINT fk_cd_result  FOREIGN KEY (result_id)   REFERENCES decision_results (id),
  CONSTRAINT fk_cd_rec_by  FOREIGN KEY (recorded_by) REFERENCES users (id),
  CONSTRAINT fk_cd_upd_by  FOREIGN KEY (updated_by)  REFERENCES users (id)
) ENGINE=InnoDB COMMENT='Vendimi dhe rezultati i çështjes';
