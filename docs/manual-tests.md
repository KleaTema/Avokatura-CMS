# Manual browser tests

Per task (013 onward): what to click in the browser, as whom, and what must happen. These complement the automated
tests (integration, Vitest); they are run before a task is shown to the client.

**Setup.** API on http://localhost:5004, frontend on http://localhost:5173, local database with the dev seed
(`LocalScripts/900–904`). Log in at `/login` with a dev user (no password):

| User | Role | Office / institution |
|---|---|---|
| admin@ash.local | ADMIN | TR |
| protokoll@ash.local | PROTOCOL | TR |
| kryetar@ash.local | OFFICE_HEAD (Kryetar) | TR |
| avokat1@ash.local, avokat2@ash.local | STATE_LAWYER | TR |
| ndihmes@ash.local | LEGAL_ASSISTANT | TR |
| monitor@ash.local | MONITOR | TR |
| institucion@ash.local | INSTITUTION_USER | Ministria e Financave dhe Ekonomisë |
| jurist@ash.local | INSTITUTION_JURIST | Ministria e Financave dhe Ekonomisë |

Keep this file up to date in every task (see `.github/skills/ash-task-done-check/SKILL.md`).
"Expected" lists what to check; any other behaviour is a bug.

---

## 013 — Admin: users and roles
**User:** admin
1. Go to *Administrimi → Përdoruesit* (`/admin/users`). Search "avokat". → Only matching users; search ignores accents.
2. *Shto përdorues*: email `test.avokat@ash.local`, Emri/Mbiemri, belongs to *Zyra* TR, role STATE_LAWYER. Save.
   → User appears; no error.
3. Log out, log in as `test.avokat@ash.local`. → Lawyer menu (Dashboard, Çështje, Chat, Njoftime, Profili, Agjenda, Raporte).
4. As admin, open the user's *Rolet* and grant INSTITUTION_USER. → Albanian 400 message (an office user cannot have an institution role).
5. *Çaktivizo* the user. → Dialog shows the number of active case assignments; after confirming, the user cannot log in.

## 014 — Admin: lookup lists and settings
**User:** admin
1. *Listat* (`/admin/lists`) → hearing types: deactivate one. → It disappears from the hearing-type pickers; existing data still shows its name.
2. *Formati i kodit* (`/admin/code-format`): create `ASH-{YYYY}-{RAND}` and activate it. → As Protokoll, the next registered case gets a code like `ASH-2026-123456/1`. (Set the original format back afterwards.)
3. *Llojet e skedarëve* (`/admin/file-types`): set a max size above 100 MB. → Albanian 400 message.

## 015 — Admin: offices and institutions
**User:** admin
1. *Zyrat* (`/admin/offices`): set as head a user without OFFICE_HEAD. → 400 with message.
2. Set kryetar@ash.local as head of TR. → Saved.
3. *Institucionet* (`/admin/institutions`): search `financ`. → "Ministria e Financave dhe Ekonomisë" is found.
4. Try to give a ministry a parent. → Refused (only one level: a ministry has no parent).

## 016 / 021 — Register a new case (X/1)
**User:** protokoll
1. *Çështje → Regjistro çështje të re*. Leave everything empty and submit. → Each required field shows an Albanian error; the page scrolls to the first.
2. Fill: nr. fletëthirrjes, nr. i aktit, data e aktit, data e hyrjes (today), Shkalla e parë, Gjykata e Shkallës së Parë Tiranë, judge Ilir Gjyqtari, object, first hearing (date + time), a party *Paditës* (person fizik) and *I paditur* = Ministria e Financave. Submit.
   → Success page with a code like `2026-483915/1` and *Kopjo*; the case is REGISTERED with one scheduled hearing.
3. Register again with the same summons number and court. → "possible duplicate" warning listing the existing case; *Regjistro gjithsesi* registers it.
4. Pick a judge of another court with *Shfaq të gjithë gjyqtarët*. → Registration succeeds with a warning that the judge is listed at another court.
5. Leave the form with unsaved input. → The browser asks before leaving.

## 017 — Parties
**User:** protokoll (REGISTERED case), then avokat2 (case where avokat2 is secondary)
1. Open the case → *Palët*. Add *Ministria e Financave* as *I paditur*. → Party added; *Institucionet* on *Të dhënat* shows the ministry as *Monitorim*.
2. Edit a natural person's name. → Saved; history shows *Pala u ndryshua*.
3. Remove the ministry's last party. → Asked whether to end the institution link too; both answers work.
4. As monitor@ash.local open the same case. → No add/edit/remove buttons.

## 018 — Continue a closed case at the next level
**User:** protokoll (a case X/1 closed in the database: `UPDATE cases SET lifecycle_status='CLOSED', closed_at=UTC_TIMESTAMP(3), closed_by=… WHERE …`)
1. *Çështje → Vazhdo çështje ekzistuese*. Search by unique code, act number, party name or NIPT (2+ characters). → The dossier is shown by its latest level.
2. Choose it. → Preview shows object, parties and institution links that will be copied.
3. Fill the appeal summons (Gjykata e Apelit Tiranë) and submit. → `…/2` created; parties and link copied; `/1` unchanged.
4. Try to continue `/1` again. → 409 "Kjo çështje ka vazhduar tashmë në …/2".
5. With an X/1 that is NOT closed: the dialog *Njofto avokatin* sends the notice (once a day for the same summons).

## 019 / 022 — Case list
1. As avokat1: *Çështje*. → Only *Çështjet e mia*.
2. As kryetar: tabs *Pa avokat* and *Të zyrës* with counts; default *Pa avokat* when it has cases.
3. As institucion: *Të institucionit* shows the cases linked to the ministry.
4. Filters (status, court, *Filtra*: category, priority, suspended, hearing range) are in the URL; Back/Forward keep them; *Pastro filtrat* clears them.
5. Scroll a long list. → More cases load; open a case and press Back. → Same list, same scroll position.

## 020 / 022 — Case page
1. Open a case as avokat1. → Header with code (*Kopjo*), status and priority chips; tabs Të dhënat, Palët, Seancat, Dokumentet, Vendimi, Historiku; `/cases/{id}/historiku` opens the tab directly.
2. As avokat2 (secondary) on the same case. → No decision actions.
3. A case id you cannot see (e.g. as drlawyer of another office). → "Çështja nuk u gjet ose nuk keni akses." — never says which.
4. *Historiku* after a transfer. → The previous primary lawyer is shown with old → new names.

## 023 — Assign lawyers
**User:** kryetar
1. *Çështje → Pa avokat*. → Each row has *Cakto avokatin* (an icon on a phone).
2. Click it. → Dialog with case code, court, object; lawyers show "N primare · M sekondare"; the Kryetar is marked "(Unë)".
3. Pick a primary; open the secondary list. → The primary is disabled there ("Është avokati primar").
4. Confirm. → Toast "Çështja iu caktua {emri}"; the case leaves *Pa avokat* and appears in the lawyer's *Çështjet e mia*.
5. Open the same dialog in a second browser tab before the first one confirms, then confirm in both. → The second shows the conflict message with *Rifresko*.
6. *Të zyrës → Filtra → Avokati*. → Filters by the lawyer; chip shown.

## 024 — Transfer the primary lawyer
**User:** kryetar (an ACTIVE case with primary + secondary)
1. *Të dhënat → Ekipi → Transfero*. → Dialog; the current primary is not offered; first option "Avokati sekondar bëhet primar (…)".
2. Choose a new primary, tick "Avokati aktual primar bëhet sekondar", add a reason. → Summary "Avokati primar: A → B", "Avokati sekondar: … → A".
3. Confirm. → Toast; Ekipi updated; history *Avokati primar u ndryshua* with the reason.
4. Log in as the old primary (if no longer on the case). → The case gives 404 and is not in *Çështjet e mia*.

## 025 — Case data
**User:** avokat1 (primary of an ACTIVE case)
1. *Të dhënat → Vlera e padisë → Ndrysho*. Type `1250000,5`. → Shown as `1.250.000,5` while typing; saved and shown as `1.250.000,50 ALL`.
2. Type `12,345`. → Error under the field; nothing saved.
3. *Kategoria*: Ndjekje + KOD A AVSH. *Statusi*: suspend with a reason. → "Pezulluar që nga {data}"; header shows *Pezulluar*.
4. *Masa e sigurimit*: switch on, nr. vendimi, data (calendar), dispozitivi; save; then switch off. → History has SECURITY_MEASURE_SET and *Masa e sigurimit u hoq*.
5. Make several saves in a row without reloading. → No conflict between your own saves.
6. Change the claim in a second tab, then save in the first. → Conflict message with *Rifresko*; your typed values stay in the fields.
7. As avokat2 (secondary): only *Vlera e padisë* and *Masa e sigurimit* have *Ndrysho*. As kryetar: priority select in the header.

## 026 — Legal assistants
**User:** avokat1 (primary)
1. *Ekipi → Shto ndihmës*. → Picker of the office's legal assistants with "N çështje aktive"; people already on the case are not offered.
2. Add ndihmes (if not on the case). → Toast; Ekipi lists the assistant; ndihmes@ash.local sees the case in *Çështjet e mia*.
3. Remove the assistant (red icon → confirm). → ndihmes gets 404 on the case.
4. As avokat2 (secondary). → No *Shto ndihmës*, no remove icon.

## 027 — Institution jurist and delegation
1. As institucion on a case linked to the ministry: *Institucionet → Cakto juristin*. → Picker of the ministry's jurists, the current one marked and disabled; choosing another shows "Juristi aktual do të zëvendësohet". Confirm → the old jurist gets 404, the new one sees the case.
2. As avokat1 (primary, ACTIVE first-instance case with the ministry monitoring): *Delego / Relato*, choose the ministry and Delegim. → Chip *Delegim*; *Kategoria* card shows Delegim read-only with the hint; jurist@ash.local can now record the decision and change the judge.
3. *Anulo delegimin* with a reason. → Chip *Monitorim*; category empty; the lawyer can set Ndjekje again.
4. On a case at Gjykata e Lartë: *Delego*. → The API message "Delegimi lejohet vetëm në shkallën e parë dhe të dytë." in the dialog.

## 028 — Judge and object
1. As avokat1 (primary): *Të dhënat → Gjyqtari → Ndrysho*. → Judges of the case's court; *Shfaq të gjithë gjyqtarët* lists all (court beside the name).
2. Pick a judge of the same court; save. → Card shows the new judge; *Historiku*: "Gjyqtari u ndryshua: Ilir Gjyqtari → Klodiana Çela".
3. Pick a judge of another court. → Saved, with a warning in the card that the judge is listed at another court.
4. *Objekti → Ndrysho*: the counter shows "N / 4000"; save a change. → *Historiku*: "Objekti u ndryshua" with *Shiko objektin para dhe pas* showing both texts.
5. As avokat2 (secondary): *Objekti* has *Ndrysho*, *Gjyqtari* does not.
6. As protokoll on a REGISTERED case of TR: both cards have *Ndrysho*. After the Kryetar assigns a lawyer: the case gives 404 to protokoll.

## 029 — Review of the assignment and case-data screens
Automated part: `npm run e2e` (docs/e2e.md) — assign in four interactions, the concurrent-edit conflict, and the
buttons of five users on a REGISTERED and an ACTIVE case. By hand:
1. As kryetar, on a REGISTERED case: header *Cakto avokatin*; no *Transfero*. On an ACTIVE case: *Transfero*, no
   *Cakto avokatin*. (The buttons come from `permissions[]` only; the API drops the actions outside their state.)
2. As avokat1 (primary), on a delegated case: *Kategoria* shows Delegim with the hint and has no *Ndrysho*; after
   *Anulo delegimin* the *Ndrysho* is back.
3. Any 409 from another user (edit the same data in two tabs) shows the same warning everywhere — inline editors and
   all dialogs: "Të dhënat u ndryshuan nga një përdorues tjetër." with *Rifresko*; the typed values stay.
4. Keyboard, in every dialog (assign, transfer, add/remove assistant, jurist, delegate, undelegate): the focus starts
   in the first field (on *Anulo* in the remove confirmation); Esc closes; the focus returns to the button that opened
   it; Enter in a one-line field does not submit.
5. At 390 px width every dialog fills the screen; no horizontal scrolling anywhere on the case page.
6. Every successful save shows a toast (also the Kryetar's priority in the header: "Prioriteti u ndryshua: …").

## 030 — Hearings (backend; the screens come with task 035)
Until 035 the hearings are checked through what already shows them — the case list (*Seanca e radhës*) and the case
page header — after calling the API (e.g. from Swagger at http://localhost:5004/swagger with the `X-Dev-User` header).
**User:** avokat1 (primary of an ACTIVE case)
1. `POST /api/cases/{id}/hearings` with a time next week. → 201; the case list and the case header show it as *Seanca
   e radhës*.
2. Add one 20 minutes later. → 201 with the warning "Kjo çështje ka një seancë tjetër të planifikuar brenda 30
   minutave (…)"; *Seanca e radhës* stays the earlier one.
3. Add one with a time yesterday. → 201 with the warning about the past; it becomes *Seanca e radhës* and the API marks
   it overdue (`nextHearingIsOverdue`) until it is completed.
4. `POST …/hearings/{hearingId}/complete` with HELD, a description and `next` in a month. → *Seanca e radhës* moves to
   the new one; *Historiku* shows "Seanca e … u zhvillua; seanca e radhës: …".
5. HELD without a description → 400; type *Tjetër* without its text → 400 "Plotësoni llojin e seancës.".
6. Complete the same hearing again. → 409 "Seanca është përfunduar tashmë; statusi i saj nuk ndryshon më.".
7. As institucion (the ministry monitors the case): adding works, editing or completing → 403.
8. After any hearing change, editing the claim (task 025) with the case's rowVersion still works: hearings never
   change the case's rowVersion.
