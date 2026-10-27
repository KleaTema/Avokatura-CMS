-- =====================================================================
-- Test scenario (NOT for production): a full life cycle of one dossier
--   X/1 first instance -> closed, appealed -> X/2 appeal -> final -> archived
-- Run after 01..08. Expected-failure checks are in run_tests.sh.
-- =====================================================================
USE ash_core;

INSERT INTO offices (code, name, office_type) VALUES ('TR', 'Zyra Qendrore Tiranë', 'CENTRAL');
SET @office = LAST_INSERT_ID();

INSERT INTO institutions (name, short_name, institution_type) VALUES
 ('Ministria e Financave dhe Ekonomisë', 'MFE', 'MINISTRY');
SET @inst = LAST_INSERT_ID();

INSERT INTO users (external_subject, email, first_name, last_name, office_id) VALUES
 ('sub-prot', 'protokoll@ash.gov.al', 'Arta',  'Protokolli', @office),
 ('sub-head', 'kryetari@ash.gov.al',  'Besnik','Kryetari',   @office),
 ('sub-av1',  'avokat1@ash.gov.al',   'Drita', 'Avokatja',   @office),
 ('sub-av2',  'avokat2@ash.gov.al',   'Ermal', 'Avokati',    @office),
 ('sub-nd',   'ndihmes@ash.gov.al',   'Fiona', 'Ndihmësja',  @office);
INSERT INTO users (external_subject, email, first_name, last_name, institution_id) VALUES
 ('sub-instu','institucioni@financa.gov.al','Hana','Institucioni', @inst),
 ('sub-jur',  'jurist@financa.gov.al','Gent',  'Juristi',    @inst),
 ('sub-jur2', 'jurist2@financa.gov.al','Ilda', 'Juristja',   @inst);

SELECT id INTO @prot FROM users WHERE external_subject='sub-prot';
SELECT id INTO @head FROM users WHERE external_subject='sub-head';
SELECT id INTO @av1  FROM users WHERE external_subject='sub-av1';
SELECT id INTO @av2  FROM users WHERE external_subject='sub-av2';
SELECT id INTO @nd   FROM users WHERE external_subject='sub-nd';
SELECT id INTO @jur  FROM users WHERE external_subject='sub-jur';
SELECT id INTO @instu FROM users WHERE external_subject='sub-instu';
SELECT id INTO @jur2 FROM users WHERE external_subject='sub-jur2';
UPDATE offices SET head_user_id = @head WHERE id = @office;

INSERT INTO user_roles (user_id, role_id)
SELECT @prot, id FROM roles WHERE code='PROTOCOL' UNION ALL
SELECT @head, id FROM roles WHERE code='OFFICE_HEAD' UNION ALL
SELECT @head, id FROM roles WHERE code='STATE_LAWYER' UNION ALL
SELECT @av1,  id FROM roles WHERE code='STATE_LAWYER' UNION ALL
SELECT @av2,  id FROM roles WHERE code='STATE_LAWYER' UNION ALL
SELECT @nd,   id FROM roles WHERE code='LEGAL_ASSISTANT' UNION ALL
SELECT @instu, id FROM roles WHERE code='INSTITUTION_USER' UNION ALL
SELECT @jur,  id FROM roles WHERE code='INSTITUTION_JURIST' UNION ALL
SELECT @jur2, id FROM roles WHERE code='INSTITUTION_JURIST';

INSERT INTO courts (name, court_level_id, city)
SELECT 'Gjykata e Shkallës së Parë Tiranë (test)', id, 'Tiranë' FROM court_levels WHERE code='FIRST_INSTANCE';
SET @court1 = LAST_INSERT_ID();
INSERT INTO courts (name, court_level_id, city)
SELECT 'Gjykata e Apelit Tiranë (test)', id, 'Tiranë' FROM court_levels WHERE code='APPEAL';
SET @court2 = LAST_INSERT_ID();
INSERT INTO judges (first_name, last_name, court_id) VALUES ('Ilir','Gjyqtari', @court1), ('Klodiana','Gjyqtarja', @court2);
SELECT id INTO @judge1 FROM judges WHERE last_name='Gjyqtari';
SELECT id INTO @judge2 FROM judges WHERE last_name='Gjyqtarja';

-- ---- 1. Fletëthirrja arrives: new dossier + X/1 (one transaction in the API)
START TRANSACTION;
CALL create_dossier(@office, @prot, @dossier, @code);
INSERT INTO cases (dossier_id, level_seq, case_code, office_id, summons_number, act_number, received_date,
                   court_level_id, court_id, judge_id, object_text, registered_by)
SELECT @dossier, 1, CONCAT(@code,'/1'), @office, 'FL-778', '3312-2026', '2026-10-20',
       id, @court1, @judge1, 'Detyrim pagimi i shpërblimit për pronën', @prot
  FROM court_levels WHERE code='FIRST_INSTANCE';
SET @case1 = LAST_INSERT_ID();
INSERT INTO case_parties (case_id, party_role_id, party_kind, display_name, created_by)
SELECT @case1, id, 'NATURAL', 'Petrit Hoxha', @prot FROM party_roles WHERE code='PADITES';
INSERT INTO case_parties (case_id, party_role_id, party_kind, institution_id, display_name, created_by)
SELECT @case1, id, 'INSTITUTION', @inst, 'Ministria e Financave dhe Ekonomisë', @prot FROM party_roles WHERE code='I_PADITUR';
INSERT INTO case_institutions (case_id, institution_id, link_mode, started_by) VALUES (@case1, @inst, 'MONITORING', @prot);
INSERT INTO case_history_events (dossier_id, case_id, event_type, actor_user_id, summary)
VALUES (@dossier, @case1, 'CASE_REGISTERED', @prot, 'Çështja u regjistrua dhe kaloi te Kryetari i Zyrës');
COMMIT;

-- ---- 2. Kryetari assigns primary + secondary, lawyer adds assistant
INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by) VALUES
 (@case1, @av1, 'PRIMARY_LAWYER',   @head),
 (@case1, @av2, 'SECONDARY_LAWYER', @head),
 (@case1, @nd,  'LEGAL_ASSISTANT',  @av1),
 (@case1, @jur, 'INSTITUTION_JURIST', @instu);  -- the INSTITUTION_USER assigns the jurist
UPDATE cases SET lifecycle_status='ACTIVE', assigned_at=NOW(3), priority='HIGH',
       category_id=(SELECT id FROM case_categories WHERE code='NDJEKJE'),
       subcategory_id=(SELECT id FROM case_subcategories WHERE code='KOD_A_AVSH'),
       claim_amount=2500000, claim_currency='ALL', row_version=row_version+1
 WHERE id=@case1;

-- ---- 3. Hearing + document
INSERT INTO case_hearings (case_id, hearing_type_id, scheduled_at, status, description, created_by)
SELECT @case1, id, '2026-11-12 08:30:00', 'SCHEDULED', 'Seanca e parë përgatitore', @av1 FROM hearing_types WHERE code='PERGATITORE';
SET @h1 = LAST_INSERT_ID();
UPDATE cases SET next_hearing_at='2026-11-12 08:30:00', last_hearing_id=@h1, first_data_entry_at=NOW(3) WHERE id=@case1;
INSERT INTO documents (dossier_id, case_id, document_type_id, title, original_file_name, content_type,
                       size_bytes, sha256, storage_bucket, storage_key, scan_status, uploaded_by)
SELECT @dossier, @case1, id, 'Padia', 'padia.pdf', 'application/pdf', 482113,
       UNHEX(SHA2('padia-content',256)), 'ash-documents', CONCAT('2026/10/', UUID()), 'CLEAN', @av1
  FROM document_types WHERE code='PADI_PROVA';

-- ---- 4. Decision at first instance: lost, appealed, reasoned decision not yet arrived
INSERT INTO case_decisions (case_id, result_id, decision_number, decision_date, dispositive,
                            lost_amount, caused_damage_amount, court_costs_amount, is_appealed, recorded_by)
SELECT @case1, id, '1450', '2027-03-02', 'Pranimi i padisë', 2500000, 2500000, 30000, 1, @av1
  FROM decision_results WHERE code='HUMBUR';
UPDATE cases SET lifecycle_status='CLOSED', closed_at=NOW(3), closed_by=@av1, next_hearing_at=NULL WHERE id=@case1;

-- ---- 5. New summons from the Court of Appeal -> X/2 continues the dossier
INSERT INTO cases (dossier_id, level_seq, case_code, previous_case_id, office_id, summons_number, act_number,
                   received_date, court_level_id, court_id, judge_id, object_text, registered_by)
SELECT c.dossier_id, c.level_seq + 1, CONCAT(d.unique_code, '/', c.level_seq + 1), c.id, c.office_id,
       'FL-1021', '88-2027', '2027-05-10', cl.id, @court2, @judge2, c.object_text, @prot
  FROM cases c JOIN dossiers d ON d.id = c.dossier_id JOIN court_levels cl ON cl.code = 'APPEAL'
 WHERE c.id = @case1;
SET @case2 = LAST_INSERT_ID();
INSERT INTO case_parties (case_id, party_role_id, party_kind, institution_id, display_name, identifier, created_by)
SELECT @case2, party_role_id, party_kind, institution_id, display_name, identifier, @prot
  FROM case_parties WHERE case_id = @case1 AND deleted_at IS NULL;
-- institution links continue to the new level too
INSERT INTO case_institutions (case_id, institution_id, link_mode, started_by)
SELECT @case2, institution_id, link_mode, @prot
  FROM case_institutions WHERE case_id = @case1 AND ended_at IS NULL;

-- ---- 6. Transfer: Kryetari replaces the primary lawyer on X/2
INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by) VALUES (@case2, @av1, 'PRIMARY_LAWYER', @head);
UPDATE case_assignments SET ended_at=NOW(3), ended_by=@head, end_reason='TRANSFERRED'
 WHERE case_id=@case2 AND case_role='PRIMARY_LAWYER' AND ended_at IS NULL;
INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by) VALUES (@case2, @av2, 'PRIMARY_LAWYER', @head);
UPDATE cases SET lifecycle_status='ACTIVE', assigned_at=NOW(3) WHERE id=@case2;

-- ---- 7. Chat + audit sanity
INSERT INTO ash_chat.conversations (conversation_type, case_id, title, created_by) VALUES ('CASE', @case2, CONCAT(@code,'/2'), @av2);
SET @conv = LAST_INSERT_ID();
INSERT INTO ash_chat.conversation_participants (conversation_id, user_id) VALUES (@conv, @av2), (@conv, @jur);
INSERT INTO ash_chat.messages (conversation_id, sender_user_id, body, created_at) VALUES
 (@conv, @av2, 'Ju lutem dërgoni vendimin e shkallës së parë.', '2026-11-15 09:00:00'),
 (@conv, @jur, 'Dërguar sot.', '2026-12-02 10:00:00');
INSERT INTO ash_audit.audit_log (user_id, user_display, action, entity_type, entity_id, case_id, ip_address)
VALUES (@av2, 'Ermal Avokati', 'CASE_VIEW', 'CASE', @case2, @case2, INET6_ATON('10.20.1.15'));

SELECT @dossier AS dossier_id, @case1 AS case1, @case2 AS case2, @code AS unique_code;
