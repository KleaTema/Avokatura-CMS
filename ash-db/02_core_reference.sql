-- =====================================================================
-- ASH — 02: organisation, users, roles/permissions, lookup lists
-- Schema: ash_core
--
-- Conventions (apply to every file):
--   * Table/column names in English, Albanian term in COMMENT.
--   * All DATETIME values are UTC (server runs with default-time-zone='+00:00');
--     the API converts to Europe/Tirane for display.
--   * public_id = UUID (time-ordered) used in URLs/API. Internal BIGINT ids
--     never leave the server, so case/document ids cannot be guessed or enumerated.
--   * Lists the business may extend (court list, document types, results...)
--     are lookup TABLES. Values the code branches on (lifecycle states,
--     case roles) are VARCHAR codes guarded by CHECK constraints.
--   * Nothing that is a legal record is hard-deleted: deleted_at / ended_at.
--   * row_version = optimistic concurrency token (EF Core IsConcurrencyToken),
--     so two users editing the same case cannot silently overwrite each other.
-- =====================================================================
USE ash_core;

-- ---------------------------------------------------------------------
-- Organisation
-- ---------------------------------------------------------------------
CREATE TABLE offices (
  id               SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code             VARCHAR(30)  NOT NULL,
  name             VARCHAR(200) NOT NULL COMMENT 'Zyra (qendrore / vendore / e përfaqësimit)',
  office_type      VARCHAR(20)  NOT NULL COMMENT 'CENTRAL | LOCAL | REPRESENTATION',
  parent_office_id SMALLINT UNSIGNED NULL,
  is_active        TINYINT(1)   NOT NULL DEFAULT 1,
  created_at       DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_offices_code (code),
  CONSTRAINT ck_offices_type CHECK (office_type IN ('CENTRAL','LOCAL','REPRESENTATION')),
  CONSTRAINT fk_offices_parent FOREIGN KEY (parent_office_id) REFERENCES offices (id)
) ENGINE=InnoDB COMMENT='Zyrat e Avokaturës së Shtetit';

CREATE TABLE institutions (
  id                    INT UNSIGNED NOT NULL AUTO_INCREMENT,
  public_id             BINARY(16)   NOT NULL DEFAULT (UUID_TO_BIN(UUID(), 1)),
  name                  VARCHAR(300) NOT NULL,
  short_name            VARCHAR(60)  NULL,
  institution_type      VARCHAR(20)  NOT NULL COMMENT 'MINISTRY | AGENCY | LOCAL_GOV | INDEPENDENT | OTHER',
  parent_institution_id INT UNSIGNED NULL COMMENT 'e.g. agency under a ministry — used by statistics per ministry',
  nipt                  VARCHAR(20)  NULL,
  is_active             TINYINT(1)   NOT NULL DEFAULT 1,
  created_at            DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at            DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_institutions_public (public_id),
  UNIQUE KEY uq_institutions_nipt (nipt),
  KEY ix_institutions_parent (parent_institution_id),
  FULLTEXT KEY ft_institutions_name (name, short_name),
  CONSTRAINT ck_institutions_type CHECK (institution_type IN ('MINISTRY','AGENCY','LOCAL_GOV','INDEPENDENT','OTHER')),
  CONSTRAINT fk_institutions_parent FOREIGN KEY (parent_institution_id) REFERENCES institutions (id)
) ENGINE=InnoDB COMMENT='Institucionet shtetërore (palë / me delegim / monitoruese)';

-- ---------------------------------------------------------------------
-- Users. Identity comes from the government-employee identity provider;
-- we store its stable subject id, never a password.
-- ---------------------------------------------------------------------
CREATE TABLE users (
  id               INT UNSIGNED NOT NULL AUTO_INCREMENT,
  public_id        BINARY(16)   NOT NULL DEFAULT (UUID_TO_BIN(UUID(), 1)),
  external_subject VARCHAR(255) NOT NULL COMMENT 'sub/oid claim from the gov identity provider',
  email            VARCHAR(254) NOT NULL COMMENT 'Email zyrtar',
  first_name       VARCHAR(100) NOT NULL,
  last_name        VARCHAR(100) NOT NULL,
  phone            VARCHAR(30)  NULL,
  office_id        SMALLINT UNSIGNED NULL COMMENT 'set for ASH staff',
  institution_id   INT UNSIGNED NULL COMMENT 'set for institution users',
  is_active        TINYINT(1)   NOT NULL DEFAULT 1,
  last_login_at    DATETIME(3)  NULL,
  created_at       DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at       DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  row_version      INT UNSIGNED NOT NULL DEFAULT 0,
  PRIMARY KEY (id),
  UNIQUE KEY uq_users_public (public_id),
  UNIQUE KEY uq_users_subject (external_subject),
  UNIQUE KEY uq_users_email (email),
  KEY ix_users_office (office_id, is_active),
  KEY ix_users_institution (institution_id, is_active),
  FULLTEXT KEY ft_users_name (first_name, last_name),
  CONSTRAINT ck_users_one_org CHECK (office_id IS NULL OR institution_id IS NULL),
  CONSTRAINT fk_users_office      FOREIGN KEY (office_id)      REFERENCES offices (id),
  CONSTRAINT fk_users_institution FOREIGN KEY (institution_id) REFERENCES institutions (id)
) ENGINE=InnoDB COMMENT='Përdoruesit';

-- Office head (Kryetari i Zyrës) is a property of the office, not only a role,
-- so "send new case to the head of THIS office" is one indexed lookup.
ALTER TABLE offices
  ADD COLUMN head_user_id INT UNSIGNED NULL COMMENT 'Kryetari i Zyrës' AFTER parent_office_id,
  ADD CONSTRAINT fk_offices_head FOREIGN KEY (head_user_id) REFERENCES users (id);

-- ---------------------------------------------------------------------
-- Roles & permissions (RBAC).
-- Two layers:
--   1. system roles (user_roles)  -> what menus/modules a user sees and
--      office-wide actions (create case, assign lawyer, reports...)
--   2. case roles (case_assignments / case_institutions, file 03) -> what
--      a user may do INSIDE one case. A lawyer can be PRIMARY on case A and
--      SECONDARY on case B, so this cannot be a user-level role.
-- Both layers map to rows in `permissions`, so a change in the client's
-- rights matrix is a data change, not a code release.
-- ---------------------------------------------------------------------
CREATE TABLE roles (
  id          TINYINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code        VARCHAR(40)  NOT NULL,
  name        VARCHAR(100) NOT NULL,
  description VARCHAR(500) NULL,
  is_system   TINYINT(1)   NOT NULL DEFAULT 1 COMMENT 'system roles cannot be deleted from the admin UI',
  PRIMARY KEY (id),
  UNIQUE KEY uq_roles_code (code)
) ENGINE=InnoDB COMMENT='Rolet e sistemit';

CREATE TABLE permissions (
  id          SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code        VARCHAR(60)  NOT NULL COMMENT 'e.g. case.assign_lawyer, menu.chat',
  scope       VARCHAR(10)  NOT NULL COMMENT 'SYSTEM = granted via role; CASE = granted via case role',
  description VARCHAR(300) NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_permissions_code (code),
  CONSTRAINT ck_permissions_scope CHECK (scope IN ('SYSTEM','CASE'))
) ENGINE=InnoDB;

CREATE TABLE role_permissions (
  role_id       TINYINT UNSIGNED  NOT NULL,
  permission_id SMALLINT UNSIGNED NOT NULL,
  PRIMARY KEY (role_id, permission_id),
  CONSTRAINT fk_rp_role FOREIGN KEY (role_id)       REFERENCES roles (id),
  CONSTRAINT fk_rp_perm FOREIGN KEY (permission_id) REFERENCES permissions (id)
) ENGINE=InnoDB;

CREATE TABLE user_roles (
  user_id    INT UNSIGNED     NOT NULL,
  role_id    TINYINT UNSIGNED NOT NULL,
  granted_by INT UNSIGNED     NULL,
  granted_at DATETIME(3)      NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (user_id, role_id),
  KEY ix_user_roles_role (role_id),
  CONSTRAINT fk_ur_user    FOREIGN KEY (user_id)    REFERENCES users (id),
  CONSTRAINT fk_ur_role    FOREIGN KEY (role_id)    REFERENCES roles (id),
  CONSTRAINT fk_ur_granted FOREIGN KEY (granted_by) REFERENCES users (id)
) ENGINE=InnoDB COMMENT='Administratori shton role';

-- Rights inside a case, per access profile (the client's matrix from the workflow).
-- access_profile: PRIMARY_LAWYER | SECONDARY_LAWYER | LEGAL_ASSISTANT |
--                 INSTITUTION_MONITORING | INSTITUTION_DELEGATED
CREATE TABLE case_profile_permissions (
  access_profile VARCHAR(30)       NOT NULL,
  permission_id  SMALLINT UNSIGNED NOT NULL,
  PRIMARY KEY (access_profile, permission_id),
  CONSTRAINT ck_cpp_profile CHECK (access_profile IN
    ('PRIMARY_LAWYER','SECONDARY_LAWYER','LEGAL_ASSISTANT','INSTITUTION_MONITORING','INSTITUTION_DELEGATED')),
  CONSTRAINT fk_cpp_perm FOREIGN KEY (permission_id) REFERENCES permissions (id)
) ENGINE=InnoDB COMMENT='Të drejtat brenda çështjes sipas rolit në çështje';

-- ---------------------------------------------------------------------
-- Courts & judges
-- ---------------------------------------------------------------------
CREATE TABLE court_levels (
  id                TINYINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code              VARCHAR(30)  NOT NULL,
  name              VARCHAR(150) NOT NULL COMMENT 'Shkalla e gjykimit',
  sort_order        TINYINT UNSIGNED NOT NULL,
  allows_delegation TINYINT(1)   NOT NULL DEFAULT 0 COMMENT 'Delegim/relatim lejohet vetëm në shkallë I dhe II',
  appeal_kind       VARCHAR(10)  NULL COMMENT 'ANKIM (shkalla I) | REKURS (shkalla II) | NULL — drives the checkbox shown on the decision',
  PRIMARY KEY (id),
  UNIQUE KEY uq_court_levels_code (code),
  CONSTRAINT ck_court_levels_appeal CHECK (appeal_kind IS NULL OR appeal_kind IN ('ANKIM','REKURS'))
) ENGINE=InnoDB;

CREATE TABLE courts (
  id             SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
  name           VARCHAR(250) NOT NULL,
  court_level_id TINYINT UNSIGNED NOT NULL,
  city           VARCHAR(100) NULL,
  is_foreign     TINYINT(1)   NOT NULL DEFAULT 0 COMMENT 'gjykatë e huaj / arbitrazh ndërkombëtar',
  is_active      TINYINT(1)   NOT NULL DEFAULT 1,
  PRIMARY KEY (id),
  UNIQUE KEY uq_courts_name (name),
  KEY ix_courts_level (court_level_id),
  CONSTRAINT fk_courts_level FOREIGN KEY (court_level_id) REFERENCES court_levels (id)
) ENGINE=InnoDB COMMENT='Lista e gjykatave';

CREATE TABLE judges (
  id           INT UNSIGNED NOT NULL AUTO_INCREMENT,
  first_name   VARCHAR(100) NOT NULL,
  last_name    VARCHAR(100) NOT NULL,
  court_id     SMALLINT UNSIGNED NULL COMMENT 'current court; history of a case keeps the judge id',
  external_ref VARCHAR(50)  NULL COMMENT 'id in the KLGJ list, for re-sync',
  is_active    TINYINT(1)   NOT NULL DEFAULT 1,
  PRIMARY KEY (id),
  UNIQUE KEY uq_judges_external (external_ref),
  KEY ix_judges_court (court_id, is_active),
  FULLTEXT KEY ft_judges_name (first_name, last_name),
  CONSTRAINT fk_judges_court FOREIGN KEY (court_id) REFERENCES courts (id)
) ENGINE=InnoDB COMMENT='Gjyqtarët (lista sipas KLGJ)';

-- ---------------------------------------------------------------------
-- Business lookups (admin-extensible). is_other = show the "Tjetër" textbox.
-- ---------------------------------------------------------------------
CREATE TABLE party_roles (
  id         TINYINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code       VARCHAR(30)  NOT NULL,
  name       VARCHAR(100) NOT NULL COMMENT 'Paditës / I paditur / Person i tretë / ...',
  report_group VARCHAR(20) NOT NULL COMMENT 'PLAINTIFF | DEFENDANT_OR_THIRD — which report column it lands in',
  sort_order TINYINT UNSIGNED NOT NULL DEFAULT 0,
  is_active  TINYINT(1)   NOT NULL DEFAULT 1,
  PRIMARY KEY (id),
  UNIQUE KEY uq_party_roles_code (code),
  CONSTRAINT ck_party_roles_group CHECK (report_group IN ('PLAINTIFF','DEFENDANT_OR_THIRD'))
) ENGINE=InnoDB COMMENT='Lloji i palës (mund të shtohen të reja)';

CREATE TABLE summons_types (
  id         TINYINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code       VARCHAR(30)  NOT NULL,
  name       VARCHAR(100) NOT NULL COMMENT 'Tipi i fletëthirrjes — list to be confirmed by the client',
  is_active  TINYINT(1)   NOT NULL DEFAULT 1,
  PRIMARY KEY (id),
  UNIQUE KEY uq_summons_types_code (code)
) ENGINE=InnoDB;

CREATE TABLE hearing_types (
  id         TINYINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code       VARCHAR(30)  NOT NULL,
  name       VARCHAR(100) NOT NULL COMMENT 'Tipi i seancës',
  is_other   TINYINT(1)   NOT NULL DEFAULT 0,
  sort_order TINYINT UNSIGNED NOT NULL DEFAULT 0,
  is_active  TINYINT(1)   NOT NULL DEFAULT 1,
  PRIMARY KEY (id),
  UNIQUE KEY uq_hearing_types_code (code)
) ENGINE=InnoDB;

CREATE TABLE document_types (
  id         TINYINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code       VARCHAR(30)  NOT NULL,
  name       VARCHAR(100) NOT NULL COMMENT 'Tipi i dokumentit',
  is_other   TINYINT(1)   NOT NULL DEFAULT 0,
  sort_order TINYINT UNSIGNED NOT NULL DEFAULT 0,
  is_active  TINYINT(1)   NOT NULL DEFAULT 1,
  PRIMARY KEY (id),
  UNIQUE KEY uq_document_types_code (code)
) ENGINE=InnoDB;

CREATE TABLE decision_results (
  id                    TINYINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code                  VARCHAR(40)  NOT NULL,
  name                  VARCHAR(150) NOT NULL COMMENT 'Rezultati i çështjes',
  outcome_group         VARCHAR(20)  NOT NULL COMMENT 'WIN | LOSS | PARTIAL | PROCEDURAL | OTHER — stable grouping for statistics',
  sends_back_for_retrial TINYINT(1)  NOT NULL DEFAULT 0 COMMENT 'Kthim për rigjykim: dossier gets a new case at a lower level',
  is_other              TINYINT(1)   NOT NULL DEFAULT 0,
  sort_order            TINYINT UNSIGNED NOT NULL DEFAULT 0,
  is_active             TINYINT(1)   NOT NULL DEFAULT 1,
  PRIMARY KEY (id),
  UNIQUE KEY uq_decision_results_code (code),
  CONSTRAINT ck_decision_results_group CHECK (outcome_group IN ('WIN','LOSS','PARTIAL','PROCEDURAL','OTHER'))
) ENGINE=InnoDB;

-- Category of the case (Kategoria dhe lloji): NDJEKJE (with sub-categories), DELEGIM, RELATIM.
CREATE TABLE case_categories (
  id         TINYINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code       VARCHAR(30)  NOT NULL,
  name       VARCHAR(100) NOT NULL,
  is_delegation TINYINT(1) NOT NULL DEFAULT 0 COMMENT '1 for DELEGIM/RELATIM — statistics split "ndjekur nga ASH" vs "deleguar/relatuar"',
  sort_order TINYINT UNSIGNED NOT NULL DEFAULT 0,
  PRIMARY KEY (id),
  UNIQUE KEY uq_case_categories_code (code)
) ENGINE=InnoDB;

CREATE TABLE case_subcategories (
  id          TINYINT UNSIGNED NOT NULL AUTO_INCREMENT,
  category_id TINYINT UNSIGNED NOT NULL,
  code        VARCHAR(30)  NOT NULL,
  name        VARCHAR(100) NOT NULL COMMENT 'PA KOD / KOD A AVSH / KOD A KM',
  sort_order  TINYINT UNSIGNED NOT NULL DEFAULT 0,
  PRIMARY KEY (id),
  UNIQUE KEY uq_case_subcategories_code (code),
  -- (id, category_id) unique so cases can FK the PAIR and never mix a
  -- sub-category with the wrong category.
  UNIQUE KEY uq_case_subcategories_pair (id, category_id),
  CONSTRAINT fk_case_subcategories_cat FOREIGN KEY (category_id) REFERENCES case_categories (id)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- Allowed upload types (changeable from the admin UI, no release needed).
-- The API checks BOTH the extension and the real content (magic bytes),
-- so a renamed .exe cannot pass as a .pdf.
-- applies_to: DOCUMENT (case documents) | CHAT (chat attachments) | BOTH
-- ---------------------------------------------------------------------
CREATE TABLE allowed_file_types (
  id              TINYINT UNSIGNED NOT NULL AUTO_INCREMENT,
  extension       VARCHAR(10)      NOT NULL COMMENT 'lower case, without the dot',
  content_type    VARCHAR(100)     NOT NULL COMMENT 'MIME type stored with the file',
  magic_hex       VARCHAR(32)      NOT NULL COMMENT 'first bytes the file must start with',
  max_size_bytes  BIGINT UNSIGNED  NOT NULL COMMENT 'limit for this type — size still to be confirmed by the client',
  applies_to      VARCHAR(10)      NOT NULL DEFAULT 'BOTH',
  is_active       TINYINT(1)       NOT NULL DEFAULT 1,
  updated_at      DATETIME(3)      NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_aft_ext (extension, applies_to),
  CONSTRAINT ck_aft_applies CHECK (applies_to IN ('DOCUMENT','CHAT','BOTH')),
  CONSTRAINT ck_aft_ext     CHECK (extension = LOWER(extension) AND extension NOT LIKE '.%'),
  CONSTRAINT ck_aft_size    CHECK (max_size_bytes > 0)
) ENGINE=InnoDB COMMENT='Llojet e lejuara të skedarëve';
