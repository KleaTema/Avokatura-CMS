#!/bin/bash
# Negative tests: each statement MUST be rejected by the database.
# Usage: ./run_tests.sh   (after 01..08 and tests/test_scenario.sql)
M="mysql --default-character-set=utf8mb4 -N ash_core"
pass=0; fail=0
expect_fail () {
  local name="$1"; local sql="$2"
  if out=$($M -e "$sql" 2>&1); then
    echo "FAIL  $name (statement was accepted)"; fail=$((fail+1))
  else
    echo "ok    $name -> ${out#ERROR }" | cut -c1-150; pass=$((pass+1))
  fi
}

expect_fail "second current primary lawyer on a case" \
  "INSERT INTO case_assignments (case_id,user_id,case_role,assigned_by) VALUES (1,(SELECT id FROM users WHERE external_subject='sub-head'),'PRIMARY_LAWYER',1)"
expect_fail "same user twice on the same case" \
  "INSERT INTO case_assignments (case_id,user_id,case_role,assigned_by) VALUES (1,(SELECT id FROM users WHERE external_subject='sub-av1'),'LEGAL_ASSISTANT',1)"
expect_fail "history row cannot be updated" \
  "UPDATE case_history_events SET summary='x'"
expect_fail "history row cannot be deleted" \
  "DELETE FROM case_history_events"
expect_fail "sub-category must belong to its category (KOD A AVSH under DELEGIM)" \
  "UPDATE cases SET category_id=(SELECT id FROM case_categories WHERE code='DELEGIM'), subcategory_id=(SELECT id FROM case_subcategories WHERE code='KOD_A_AVSH') WHERE id=2"
expect_fail "decision cannot be final AND appealed" \
  "UPDATE case_decisions SET is_final=1 WHERE case_id=1"
expect_fail "CLOSED case must have closed_at" \
  "UPDATE cases SET lifecycle_status='CLOSED' WHERE id=2"
expect_fail "institution party must reference an institution" \
  "INSERT INTO case_parties (case_id,party_role_id,party_kind,display_name,created_by) VALUES (2,1,'INSTITUTION','Ministria X',1)"
expect_fail "duplicate X/n in the same dossier" \
  "INSERT INTO cases (dossier_id,level_seq,case_code,office_id,summons_number,act_number,received_date,court_level_id,court_id,object_text,registered_by) VALUES (1,2,'dup',1,'a','b','2027-01-01',1,1,'o',1)"
expect_fail "one CASE chat per case" \
  "INSERT INTO ash_chat.conversations (conversation_type,case_id,created_by) VALUES ('CASE',2,1)"
expect_fail "invalid priority value" \
  "UPDATE cases SET priority='URGENT' WHERE id=2"

echo "---- $pass passed, $fail failed"
