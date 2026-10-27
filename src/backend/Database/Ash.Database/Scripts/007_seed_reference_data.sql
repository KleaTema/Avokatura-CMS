-- =====================================================================
-- ASH — 07: reference data (roles, permission matrix, lookup lists)
-- Courts, judges, offices and institutions are NOT seeded here: import
-- them from the official lists (KLGJ judges, court map, institutions) or
-- the legacy system during migration.
-- =====================================================================
USE ash_core;

-- ---------------------------------------------------------------------
-- System roles. Note: "Avokat primar" and "Avokat sekondar" are both the
-- STATE_LAWYER system role (same menu). Primary/secondary is decided per
-- case in case_assignments, because the same lawyer can be primary on one
-- case and secondary on another.
-- "Institucion" and "Institucion me delegim" are both INSTITUTION_USER;
-- the mode comes from case_institutions.link_mode for each case.
-- INSTITUTION_USER assigns the jurist and sees every case linked to its
-- institution; INSTITUTION_JURIST sees only the cases they are assigned to.
-- Inside the case both get the same profile (monitoring or delegated).
-- ---------------------------------------------------------------------
INSERT INTO roles (code, name, description) VALUES
 ('ADMIN',            'Administratori',            'Administrimi i përdoruesve, roleve, listave dhe log-eve'),
 ('PROTOCOL',         'Specialist / Protokoll',    'Regjistron fletëthirrjen, krijon çështjen, regjistron palët'),
 ('OFFICE_HEAD',      'Kryetari i Zyrës',          'Cakton avokatin dhe prioritetin, transferon çështjen; mund të jetë edhe avokat në çështje'),
 ('STATE_LAWYER',     'Avokat Shteti',             'Primar ose sekondar — vendoset për çdo çështje'),
 ('LEGAL_ASSISTANT',  'Ndihmës Ligjor',            'Ndihmon avokatin në çështjet ku caktohet'),
 ('MONITOR',          'Monitoruesi',               'Kontrollon dhe inspekton çështjet'),
 ('INSTITUTION_USER', 'Institucion',               'Përdorues i institucionit: cakton juristin, sheh çështjet e institucionit'),
 ('INSTITUTION_JURIST','Jurist i institucionit',   'Sheh vetëm çështjet ku është caktuar; të drejtat sipas monitorimit ose delegimit');

INSERT INTO permissions (code, scope, description) VALUES
 -- menus
 ('menu.dashboard',       'SYSTEM', 'Dashboard'),
 ('menu.cases',           'SYSTEM', 'Çështje'),
 ('menu.notifications',   'SYSTEM', 'Njoftime'),
 ('menu.profile',         'SYSTEM', 'Profili'),
 ('menu.chat',            'SYSTEM', 'Chat'),
 ('menu.agenda',          'SYSTEM', 'Agjenda'),
 ('menu.reports',         'SYSTEM', 'Raporte dhe statistika'),
 ('menu.admin',           'SYSTEM', 'Administrimi'),
 -- office / global actions
 ('case.create',            'SYSTEM', 'Krijon çështje të re nga fletëthirrja'),
 ('case.link_previous',     'SYSTEM', 'Krijon çështje nga një çështje e mbyllur (shkallë tjetër)'),
 ('case.edit_registration', 'SYSTEM', 'Korrigjon të dhënat e regjistrimit para caktimit të avokatit'),
 ('case.assign_lawyer',     'SYSTEM', 'Cakton avokatin primar/sekondar'),
 ('case.transfer',          'SYSTEM', 'Ndryshon avokatin primar'),
 ('case.set_priority_any',  'SYSTEM', 'Cakton prioritetin për çështjet e zyrës'),
 ('case.view_office',       'SYSTEM', 'Sheh të gjitha çështjet e zyrës së vet'),
 ('case.view_all',          'SYSTEM', 'Sheh të gjitha çështjet (vetëm lexim)'),
 ('report.view_own',        'SYSTEM', 'Raporte për çështjet e veta'),
 ('report.view_office',     'SYSTEM', 'Raporte për zyrën'),
 ('report.view_all',        'SYSTEM', 'Raporte për të gjithë Avokaturën'),
 ('admin.users',            'SYSTEM', 'Menaxhon përdoruesit dhe rolet'),
 ('admin.lookups',          'SYSTEM', 'Menaxhon listat (gjykata, gjyqtarë, tipe ...)'),
 ('admin.case_delete',      'SYSTEM', 'Fshin / modifikon çështje (me arsye, në log)'),
 ('audit.view',             'SYSTEM', 'Sheh log-et e auditimit'),
 -- inside a case (granted through case_profile_permissions)
 ('case.view',               'CASE', 'Sheh çështjen, historikun, dokumentet'),
 ('case.edit_data',          'CASE', 'Plotëson të dhënat: vlera e padisë, tipi i dëmit'),
 ('case.set_status',         'CASE', 'Pezulluar po/jo, masë sigurimi'),
 ('case.set_priority',       'CASE', 'Cakton prioritetin'),
 ('case.set_category',       'CASE', 'Ndjekje (PA KOD / KOD A AVSH / KOD A KM)'),
 ('case.delegate',           'CASE', 'Delegon / relaton te institucioni (vetëm shkalla I dhe II)'),
 ('case.change_judge',       'CASE', 'Ndryshon gjyqtarin (ruhet në historik)'),
 ('case.change_object',      'CASE', 'Ndryshon objektin (ruhet në historik)'),
 ('party.add',               'CASE', 'Shton palë'),
 ('party.edit',              'CASE', 'Ndryshon palë'),
 ('party.delete',            'CASE', 'Fshin palë'),
 ('assistant.manage',        'CASE', 'Shton / heq ndihmësin ligjor'),
 ('hearing.add',             'CASE', 'Shton seancë'),
 ('hearing.edit',            'CASE', 'Ndryshon seancë / përshkrimin'),
 ('document.upload',         'CASE', 'Ngarkon dokument'),
 ('document.delete',         'CASE', 'Heq dokument (soft delete)'),
 ('decision.record',         'CASE', 'Mbyll çështjen dhe regjistron rezultatin/vendimin'),
 ('decision.close_final',    'CASE', 'Mbyll me vendim të formës së prerë (arkivim)'),
 ('chat.case',               'CASE', 'Chat i çështjes'),
 ('institution.assign_jurist','SYSTEM', 'Institucioni cakton juristin (në çështjet e lidhura me institucionin)');

-- helper: role -> list of permission codes
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id FROM roles r JOIN permissions p
  ON (r.code = 'ADMIN' AND p.scope = 'SYSTEM'
       AND p.code NOT IN ('case.create','case.link_previous','case.edit_registration','case.assign_lawyer',
                          'case.transfer','case.set_priority_any','case.view_office','report.view_own','report.view_office',
                          'institution.assign_jurist'))
  OR (r.code = 'PROTOCOL' AND p.code IN
       ('menu.dashboard','menu.cases','menu.notifications','menu.profile',
        'case.create','case.link_previous','case.edit_registration'))
  OR (r.code = 'OFFICE_HEAD' AND p.code IN
       ('menu.dashboard','menu.cases','menu.notifications','menu.profile','menu.chat','menu.agenda','menu.reports',
        'case.assign_lawyer','case.transfer','case.set_priority_any','case.view_office','report.view_own','report.view_office'))
  OR (r.code IN ('STATE_LAWYER','LEGAL_ASSISTANT','INSTITUTION_USER','INSTITUTION_JURIST') AND p.code IN
       ('menu.dashboard','menu.cases','menu.notifications','menu.profile','menu.chat','menu.agenda','menu.reports',
        'report.view_own'))
  OR (r.code = 'INSTITUTION_USER' AND p.code = 'institution.assign_jurist')
  OR (r.code = 'MONITOR' AND p.code IN
       ('menu.dashboard','menu.cases','menu.notifications','menu.profile','menu.chat','menu.agenda','menu.reports',
        'case.view_all','report.view_all'));

-- Rights inside a case — the matrix from the workflow + Klea's notes.
-- Rule confirmed by the client: whoever may ADD parties may also DELETE them.
INSERT INTO case_profile_permissions (access_profile, permission_id)
SELECT 'PRIMARY_LAWYER', id FROM permissions
 WHERE scope = 'CASE';

INSERT INTO case_profile_permissions (access_profile, permission_id)
SELECT prof.p, perm.id
  FROM (SELECT 'SECONDARY_LAWYER' AS p UNION ALL SELECT 'LEGAL_ASSISTANT') prof
  JOIN permissions perm ON perm.code IN
       ('case.view','case.edit_data','party.add','party.edit','party.delete','case.change_object',
        'hearing.add','hearing.edit','document.upload','chat.case');

INSERT INTO case_profile_permissions (access_profile, permission_id)
SELECT 'INSTITUTION_DELEGATED', id FROM permissions WHERE code IN
       ('case.view','case.edit_data','party.add','party.edit','party.delete','case.change_judge','case.change_object',
        'hearing.add','hearing.edit','document.upload','decision.record','chat.case');

INSERT INTO case_profile_permissions (access_profile, permission_id)
SELECT 'INSTITUTION_MONITORING', id FROM permissions WHERE code IN
       ('case.view','hearing.add','document.upload','chat.case');

-- ---------------------------------------------------------------------
-- Unique code format agreed with the client: current year + 6 random
-- digits (2026-483915). To change it later: INSERT a new row, then in one
-- transaction set the old row is_active = 0 and the new one is_active = 1.
-- ---------------------------------------------------------------------
INSERT INTO dossier_code_formats (template, random_digits, is_active, note)
VALUES ('{YYYY}-{RAND}', 6, 1, 'Viti aktual + 6 shifra të rastësishme');

-- Allowed upload types agreed with the client: PDF, DOCX, JPG, PNG.
-- 50 MB is a placeholder until the client confirms the maximum size.
INSERT INTO allowed_file_types (extension, content_type, magic_hex, max_size_bytes) VALUES
 ('pdf',  'application/pdf',                                                         '25504446', 52428800),
 ('docx', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', '504B0304', 52428800),
 ('jpg',  'image/jpeg',                                                              'FFD8FF',   52428800),
 ('jpeg', 'image/jpeg',                                                              'FFD8FF',   52428800),
 ('png',  'image/png',                                                               '89504E47', 52428800);

-- ---------------------------------------------------------------------
-- Lookups
-- ---------------------------------------------------------------------
INSERT INTO court_levels (code, name, sort_order, allows_delegation, appeal_kind) VALUES
 ('FIRST_INSTANCE',  'Shkalla e parë',            1, 1, 'ANKIM'),
 ('APPEAL',          'Apel (shkalla e dytë)',     2, 1, 'REKURS'),
 ('HIGH_COURT',      'Gjykata e Lartë',           3, 0, NULL),
 ('CONSTITUTIONAL',  'Gjykata Kushtetuese',       4, 0, NULL),
 ('FOREIGN',         'Gjykatë e huaj',            5, 0, NULL),
 ('INTL_ARBITRATION','Arbitrazh ndërkombëtar',    6, 0, NULL);

INSERT INTO party_roles (code, name, report_group, sort_order) VALUES
 ('PADITES',        'Paditës',         'PLAINTIFF',          1),
 ('I_PADITUR',      'I paditur',       'DEFENDANT_OR_THIRD', 2),
 ('PERSON_I_TRETE', 'Person i tretë',  'DEFENDANT_OR_THIRD', 3);

INSERT INTO hearing_types (code, name, is_other, sort_order) VALUES
 ('PERGATITORE',   'Seancë përgatitore',        0, 1),
 ('GJYQESORE',     'Seancë gjyqësore',          0, 2),
 ('DHOME_KESHILLIMI','Dhomë këshillimi',        0, 3),
 ('KONKLUZIONE',   'Konkluzione përfundimtare', 0, 4),
 ('SHPALLJE',      'Shpallje vendimi',          0, 5),
 ('TJETER',        'Tjetër',                    1, 9);

INSERT INTO document_types (code, name, is_other, sort_order) VALUES
 ('PADI_PROVA',      'Padi / Prova',                 0, 1),
 ('AKT_PROCEDURAL',  'Akte procedurale në gjykim',   0, 2),
 ('ANKIM',           'Ankim',                        0, 3),
 ('REKURS',          'Rekurs',                       0, 4),
 ('VENDIM',          'Vendim',                       0, 5),
 ('TJETER',          'Tjetër',                       1, 9);

INSERT INTO decision_results (code, name, outcome_group, sends_back_for_retrial, is_other, sort_order) VALUES
 ('FITUAR',               'Fituar',                         'WIN',        0, 0, 1),
 ('HUMBUR',               'Humbur',                         'LOSS',       0, 0, 2),
 ('PJESERISHT_FITUAR',    'Pjesërisht fituar',              'PARTIAL',    0, 0, 3),
 ('PJESERISHT_HUMBUR',    'Pjesërisht humbur',              'PARTIAL',    0, 0, 4),
 ('MOSKOMPETENCE_LENDORE','Moskompetencë lëndore',          'PROCEDURAL', 0, 0, 5),
 ('MOSKOMPETENCE_TOKESORE','Moskompetencë tokësore',        'PROCEDURAL', 0, 0, 6),
 ('PUSHIM_GJYKIMI',       'Pushim gjykimi',                 'PROCEDURAL', 0, 0, 7),
 ('KTHIM_RIGJYKIM',       'Kthim për rigjykim',             'PROCEDURAL', 1, 0, 8),
 ('MOSPRANIM_PADIE',      'Mospranim padie',                'PROCEDURAL', 0, 0, 9),
 ('JASHTE_JURIDIKSIONIT', 'Nxjerrje jashtë juridiksionit',  'PROCEDURAL', 0, 0, 10),
 ('KTHIM_AKTESH',         'Kthim aktesh',                   'PROCEDURAL', 0, 0, 11),
 ('TJETER',               'Tjetër',                         'OTHER',      0, 1, 99);

INSERT INTO case_categories (code, name, is_delegation, sort_order) VALUES
 ('NDJEKJE', 'Në ndjekje', 0, 1),
 ('DELEGIM', 'Delegim',    1, 2),
 ('RELATIM', 'Relatim',    1, 3);

INSERT INTO case_subcategories (category_id, code, name, sort_order)
SELECT id, s.code, s.name, s.so FROM case_categories c
  JOIN (SELECT 'PA_KOD' code, 'PA KOD' name, 1 so
        UNION ALL SELECT 'KOD_A_AVSH', 'KOD A AVSH', 2
        UNION ALL SELECT 'KOD_A_KM',   'KOD A KM',   3) s
 WHERE c.code = 'NDJEKJE';
