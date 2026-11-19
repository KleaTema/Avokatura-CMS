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
| institucion@ash.local | INSTITUTION_USER ("Institucioni") | Ministria e Financave dhe Ekonomisë |
| jurist@ash.local | INSTITUTION_JURIST ("Juristi i institucionit") | Ministria e Financave dhe Ekonomisë |
| delegim@ash.local | INSTITUTION_DELEGATE ("Institucioni me delegim", task 053k) | Ministria e Financave dhe Ekonomisë |

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
2. As avokat1 (primary, ACTIVE first-instance case with the ministry monitoring): *Delego / Relato*, choose the ministry and Delegim. → Chip *Delegim*; *Kategoria* card shows Delegim read-only with the hint. Task 053k: **delegim@ash.local** can now record the decision and change the judge; jurist@ash.local loses the case (404; history "Juristi i institucionit u hoq"); institucion@ash.local only views it (no *Cakto juristin*).
3. *Anulo delegimin* with a reason. → Chip *Monitorim*; category empty; the lawyer can set Ndjekje again. institucion@ash.local assigns a jurist again; delegim@ash.local only views it.
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

## 030 — Hearings
**User:** avokat1 (primary of an ACTIVE case), tab *Seancat*.
1. The card *Seanca e radhës* shows the earliest planned hearing (date and time, type, court); below, *Të gjitha
   seancat* lists every hearing newest first with its chip (E planifikuar / U zhvillua / U shty / U anulua), the
   description ("Pa përshkrim." when none) and "Shtuar nga … · ndryshuar nga …".
2. *Shto seancë*: a time next week, type *Seancë gjyqësore*. → Toast "Seanca u shtua."; the case header and the case
   list show it as *Seanca e radhës* if it is the earliest.
3. *Shto seancë* again, 20 minutes later. → Saved, with a yellow box on the tab: "Kjo çështje ka një seancë tjetër të
   planifikuar brenda 30 minutave (…)". Type *Tjetër* → a text field appears; empty → "Plotësoni llojin e seancës.".
4. *Shto seancë* with yesterday's date. → Saved with the warning about the past. It becomes *Seanca e radhës*, shown in
   red with "(kaloi)" in the card, the case header, *Të dhënat → Datat* and the case list; the card has the red banner
   "Seanca kaloi më {data} – përditësoni statusin" with the gold *Përfundo seancën* (the only gold button of the tab).
5. *Përfundo seancën*: choose *U zhvillua*. → *Cakto seancën e radhës* is switched on with date, time and type (the
   type of the completed hearing); *U anulua* switches it off and makes the description optional. Without a
   description for *U zhvillua* / *U shty* → "Përshkrimi i seancës është i detyrueshëm.". If the hearing had no
   description, the latest written one is filled in with the hint "Plotësuar nga seanca e mëparshme; përditësojeni.".
6. Confirm with a next date. → One request; toast "Seanca u përfundua; seanca e radhës: …"; the banner is gone; the
   hearing shows *U zhvillua*, the new one *E planifikuar*; *Historiku*: "Seanca e … u zhvillua; seanca e radhës: …".
7. *U shty* without a next hearing (switch off). → Saved with the warning "Shtyrja zakonisht ka datë të re.".
8. *U anulua*: the row is struck through and grey; the chip still says "U anulua".
9. *Ndrysho* on a planned hearing: date, time, type and notes, pre-filled in Tirane time. *Ndrysho* on a completed one:
   only the description ("Seanca është përfunduar: ndryshohet vetëm përshkrimi.").
10. Open *Ndrysho* on the same hearing in two tabs; save in one, then in the other. → "Të dhënat u ndryshuan nga një
    përdorues tjetër." with *Rifresko*; the typed values stay; after *Rifresko* the save goes through.
11. As institucion (the ministry monitors the case): *Shto seancë* is there, no *Ndrysho* and no *Përfundo seancën*.
    As monitor: no buttons at all.
12. After a hearing change, editing the claim (task 025) still works without a conflict: hearings never change the
    case's rowVersion.
13. At 390 px: the dialogs fill the screen, no horizontal scrolling; keyboard: the focus starts in the first field,
    Esc closes, Enter in a one-line field does not submit.

## 031 — Agenda
**Prepare:** as avokat1, two ACTIVE cases with hearings this week (*Seancat → Shto seancë*), two of them 30 minutes
apart; complete one as *U zhvillua*, one as *U shty*, one as *U anulua*; leave one with a time in the past.
**User:** avokat1, menu *Agjenda* (`/agenda`).
1. On a computer the week opens (*Java*, Monday–Sunday, 07:00–19:00, 24h times); below 900 px the list (*Listë*).
   The URL keeps the view and the date (`?view=…&date=…`): reload, Back/Forward and a shared link show the same week.
2. Each hearing shows time, case code, court and type. Colours: planned blue, held green, postponed orange,
   cancelled grey and struck through; a planned hearing whose time has passed has a red outline.
3. The two hearings 30 minutes apart have ⚠; hovering it shows "Përplasje me {kodi i çështjes}". With the keyboard
   (Tab to the hearing) a screen reader reads the same text, and "Seanca kaloi…" for an overdue one.
4. ‹ › move by a day / week / month; *Sot* returns to today; *Dita*, *Muaji*, *Listë* switch the view. The month
   shows six weeks and loads them in one request.
5. Change the computer's time zone (e.g. to New York) and reload. → The times are still Tirane's (10:00 stays 10:00).
6. Click a hearing. → The case opens on *Seancat*; Ctrl+click opens it in a new tab; Back returns to the same week.
7. *Shfaq të anuluarat* off (default) → cancelled hearings are hidden; on → shown struck through.
8. A week without hearings (e.g. `?date=2030-06-03`). → "Nuk ka seanca në këtë periudhë."
9. *Printo*. → Print preview: A4 portrait, no header or sidebar, only "Agjenda", the period and a table per day
   (time, court, case code, type, lawyer); a cancelled hearing is struck through with "(e anuluar)".
10. After completing or rescheduling a hearing on the case page, the agenda shows the change without a reload.
11. As kryetar: the hearings of all TR cases; *Avokati* lists the office's lawyers (the Kryetar too); choosing one
    narrows the agenda. No *Zyra* filter.
12. As monitor: *Zyra* and *Avokati*; choosing an office narrows the lawyer list to its lawyers (a chosen lawyer of
    another office is cleared). Also on *Çështje → Të gjitha → Filtra*: *Zyra* and *Avokati*, same behaviour.
13. As institucion: only the hearings of the cases the ministry is actively linked to; no filters.
14. As protokoll: no *Agjenda* in the menu; `/agenda` shows "no access".
15. API checks (Swagger, `X-Dev-User`): 63 days (`from=2026-11-01&to=2027-01-02`) → 400 "Periudha mund të jetë deri
    në 62 ditë."; as kryetar `officeId` / `lawyerId` of another office → 403; as institucion any `lawyerId` → 403.

## 032 — File storage (backend; documents use it from task 034)
Nothing to click yet: the storage is checked through the health endpoint and the MinIO console. The automated part is
`FileStorageTests` (MinIO in Testcontainers: upload, 40 MB multipart, copy, delete rule, presigned URL, health,
download headers).
**Setup:** `docker compose up -d` (MinIO on :9000, console on :9001, `minio-init` creates the buckets), then once:
`dotnet user-secrets set "Storage:AccessKey" "ash_minio" --project src/backend/Host/Ash.Api` and
`dotnet user-secrets set "Storage:SecretKey" "ash_minio_local_pw" --project src/backend/Host/Ash.Api`; restart the API.
1. http://localhost:5004/health → "Healthy" (mysql and storage).
2. Stop MinIO (`docker stop ash-minio`) → /health "Unhealthy"; start it again → "Healthy".
3. In the MinIO console (http://localhost:9001, user `ash_minio`) delete the bucket `ash-chat-files` → /health
   "Unhealthy"; run `docker compose up minio-init` → the bucket is back and /health "Healthy".
4. Remove the user-secrets keys → the API still starts; /health "Unhealthy" (storage not configured).
5. The API log (logs/ash-*.log) and the console never show the access or secret key.

## 033 — Document upload (backend; the screen comes with task 035)
Until the screen exists, upload with curl (fields BEFORE the file). The automated part is `DocumentUploadTests` and
`ClamAvScannerTests`. **Setup:** as for 032 (MinIO + user-secrets). Scanner: Development uses NoOp (every file CLEAN);
for a real scan run `docker compose --profile av up -d clamav` (the first start downloads the signatures for several
minutes) and start the API with `Scanner__Mode=ClamAv`.
Upload as avokat1 to an ACTIVE case of theirs (`{id}` = the case's public id from the address bar):
`curl -H "X-Dev-User: avokat1@ash.local" -F documentTypeCode=PADI_PROVA -F title=Padia -F "file=@padia.pdf" http://localhost:5004/api/cases/{id}/documents`
1. → 201 `{ documentPublicId, scanStatus: "PENDING", warnings: [] }`. Within ~5 s the row in `documents` becomes CLEAN,
   `storage_bucket` = ash-documents; the MinIO console shows the object in `ash-documents` and no longer in
   `ash-uploads-quarantine`. *Historiku* shows "Dokumenti u ngarkua: Padia (Padi / Prova, … KB)." (no storage key).
2. The same file again → 201 with the warning "Ky dokument është ngarkuar më parë më {data} si "Padia"."
3. An .exe renamed to .pdf → 400 "Lloji i skedarit nuk lejohet"; a .txt → the same.
4. The file before the fields (`-F "file=@padia.pdf" -F documentTypeCode=PADI_PROVA`) → 400 "Fushat duhet të
   dërgohen para skedarit."
5. `documentTypeCode=TJETER` without `otherTypeText` → 400 with the field; a title of 256 characters → 400.
6. A PDF over 50 MB → 413 "Skedari kalon madhësinë maksimale prej 50 MB."; nothing new in the quarantine bucket.
7. As institucion (the ministry monitors the case) → 201; as a user of another office → 404; on a CLOSED case → 409.
8. With ClamAV and a PDF that ClamAV flags (the upload must start with %PDF, so the plain EICAR file is refused at
   step 3 already; a PDF test sample from your security team, or `clamscan` first to confirm ClamAV flags it) → the
   row becomes INFECTED, the file stays in quarantine, *Historiku* "Dokumenti "…" u refuzua: u gjet virus (…)". The
   automated test covers this path with a fake infected scanner.
9. A .jar (or any zip without [Content_Types].xml) renamed to .docx → INFECTED, "… u refuzua: format i pavlefshëm."
10. Stop ClamAV while a document is PENDING → it stays PENDING (warnings in the log, attempt n of 5); after 5 runs →
    FAILED. Start ClamAV again: new uploads are scanned.
11. Production / Staging with `Scanner__Mode=NoOp` → the API does not start ("NoOp is allowed only in Development").
12. PENDING, INFECTED and FAILED documents cannot be downloaded (task 034: 409, see below).

## 034 — Documents: timeline, download, delete (the API; the screen: 035 below)
With the API (curl / Swagger, `X-Dev-User`); upload a few documents first (033). The automated part is
`DocumentTimelineTests`. `{case}` = a case's public id, `{doc}` = a documentPublicId from the upload or the list.
1. `GET /api/cases/{case}/documents` → newest first: title, type, level (X/n), uploader, date, size, scanStatus,
   canPreview (CLEAN pdf/png/jpg); no storage key, no numeric id. `?type=ANKIM`, `?level=1`, `?limit=2` + `after`
   (nextCursor) page through without repeating a row.
2. A dossier with X/1 and X/2: the lawyer who only works on X/2 lists X/2 and sees the X/1 documents too (one
   timeline); `GET /api/cases/{X/1}/documents` stays 404 for them, but `…/{doc of X/1}/download` works.
3. `GET /api/documents/{doc}/download` → the file as an attachment (Albanian file name intact), nosniff, no-store.
   A PENDING document → 409 "Dokumenti po kontrollohet ende"; INFECTED / FAILED → 409 "Dokumenti u refuzua nga
   kontrolli antivirus".
4. `GET /api/documents/{doc}/preview` of a PDF in the browser → shown inline; response headers include
   `Content-Security-Policy: sandbox; default-src 'none'; …`. A .docx → 400 (download it instead).
5. `curl -H "Range: bytes=0-99" …/download -i` → 206, `Content-Range: bytes 0-99/{size}`, 100 bytes; a range past the
   end → 416.
6. Every download and every preview (also each Range request) adds a DOCUMENT_DOWNLOAD row to ash_audit.audit_log
   with `details.Mode` = download / preview.
7. As the primary lawyer: `DELETE /api/documents/{doc}` with `{"reason":"U ngarkua gabim."}` → 200; the document
   leaves the list, download → 404, *Historiku* "Dokumenti u hoq: … Arsyeja: …"; the row keeps deleted_at /
   deleted_by / delete_reason and the object stays in MinIO.
8. Without a reason, or with more than 300 characters → 400. As the secondary lawyer → 403. As a user without access
   to the dossier → 404. On a CLOSED level → 409.
9. A document linked as the decision (`case_decisions.decision_document_id`) or the security measure
   (`case_security_measures.document_id`) → 409 "Dokumenti është i lidhur me vendimin / masën e sigurimit…".

## 035 — The Dokumentet tab (and the acceptance flow with Seancat)
**Setup:** as 032/033 (MinIO, user-secrets); Development scans with NoOp, so a file becomes *Gati* within ~5 s.
**User:** avokat1, an ACTIVE case of theirs.
1. Acceptance: on *Seancat* add a hearing → the tab reads *Seancat (n+1)*; open *Dokumentet* (no page reload),
   upload a PDF → *Dokumentet (n+1)*; both without leaving the case page.
2. Drag two PDFs and an .exe onto the dashed area (or *Zgjidh skedarë*, several at once). → Three rows; the .exe shows
   "Lloji i skedarit nuk lejohet" at once (nothing is sent). A file above 50 MB → "Skedari kalon madhësinë maksimale
   prej 50 MB." at once.
3. *Ngarko (2)* without choosing a type → "Zgjidhni llojin e dokumentit." under the row. Choose the types (*Tjetër* →
   a text field appears, required), edit a title, *Ngarko (2)*: the files go one after another, each with its progress
   bar ("Po ngarkohet… n%"), the second "Në radhë…". In the browser's DevTools (Network → Slow 3G) the bars are
   visible; the ✕ on an uploading row cancels it ("Ngarkimi u anulua."), ↻ uploads it again.
4. Each row then says "U ngarkua — po kontrollohet"; in the list below the document is *Në kontroll*, then *Gati*
   without a reload (polled every 3 s). If it takes over 2 minutes: "Kontrolli po zgjat — rifreskoni më vonë".
5. The same file again → a yellow note "Ky dokument është ngarkuar më parë më {data} si "…"." (still uploaded).
6. The list: newest first, grouped by level (on an X/2 case: X/2 above X/1, the X/1 documents too); each row has
   the type icon, title, type · uploader · date · size and the chip *Në kontroll* / *Gati* / *Refuzuar*. The filters
   *Lloji*, *Shkalla* and *Statusi* (always shown — see "Fixes after browser testing") narrow it.
7. *Shiko* on a PDF or image → full-screen preview; *Shkarko* there or on the row saves it under its original name
   (Albanian letters intact). ✕ / Esc closes it. Each *Shiko* / *Shkarko* writes a DOCUMENT_DOWNLOAD audit row.
8. *Hiq* (only the primary lawyer, only on documents of this level): without a reason → "Shkruani arsyen e heqjes.";
   with one → toast, the row disappears, *Dokumentet (n-1)*, *Historiku* shows the removal. A document linked as the
   decision → the dialog shows "Dokumenti është i lidhur me vendimin dhe nuk mund të hiqet."
9. As avokat2 (secondary): upload yes, *Hiq* no. As monitor: the list and *Shiko* / *Shkarko*, no upload area.
10. At 390 px: the actions sit under each document's text; the queue rows stack their fields; no side scrolling.

## Fixes after browser testing (after 035)
**Setup:** the migrator applied script 014 (`dotnet run --project src/backend/Database/Ash.Database -- --local`; a
second run says "Scripts applied: 0"). The local MySQL runs with `--innodb-ft-enable-stopword=OFF`
(docker-compose.yml; after pulling, `docker compose up -d` recreates the container once).
1. **Back link.** From *Çështjet* page 2 (filters set) open a case, switch two tabs, click
   "← Kthehu te lista e çështjeve" → the list on page 2 with the same filters and search. Open a case from a link or
   after F5 → the link goes to */cases*.
2. **Selects.** Every filter starts with *Të gjitha*; an optional field (e.g. *Tipi i fletëthirrjes*, *Ministria* of an
   institution, the new primary in *Transfero*) has "— Asnjë —" first and the × of the picker clears it; a required
   field (e.g. *Shkalla e gjykimit* at registration, *Roli* of a party) has no empty option but can still be changed.
3. **Paging.** *Çështjet*, *Përdoruesit*, *Institucionet*, *Gjyqtarët*, *Vazhdo çështje*, *Dokumentet*, *Historiku*:
   10 rows, under the table "1–10 nga N", *Rreshta për faqe* 10 / 25 / 50, "‹ Para" / "Pas ›" (disabled at the ends).
   *Pas ›* → "11–20 nga N" and `page=2` in the URL; F5 keeps page 2; changing a filter goes back to page 1. Lookup
   lists, courts, *Zyrat*, *Llojet e skedarëve*, *Formati i kodit*: numbered pages 1 2 3 … with the same size select.
   No list loads more rows when scrolled.
4. **Substring search.** As monitor, *Çështjet*: "640" finds 2026-…640/…, "86640" too; four digits of an act
   number; three letters inside a party name ("rbe" → Arben); "minist", "stri", "inan" (Financave), "tiran" (Tiranë)
   and "an" (2 letters) find the cases with those letters; "shendetesise" finds "Shëndetësisë", "HOXHA" finds "Hoxha".
   The same in the header search, *Vazhdo çështje* (also inside a NIPT), *Gjyqtarët* ("oxh" → Besnik Hoxha,
   "deshira" → Dëshira), *Institucionet* and *Përdoruesit* ("okat1" → avokat1, also inside the email). Lookup lists
   and *Zyrat* filter in the browser with "contains" (accent-insensitive).
5. **Filters.** *Çështjet* → *Filtra*: on every tab *Statusi*, *Shkalla e gjykimit*, *Gjykata* (only the chosen
   level's courts), *Gjyqtari*, *Avokati* (Kryetar / Monitor), *Zyra* (Monitor), *Institucioni*, *Kategoria*,
   *Prioriteti*, *Pezulluar*, *Seanca nga – deri*, *Data e regjistrimit nga – deri*; each shows a chip, "Pastro filtrat"
   clears all. Other pages: *Përdoruesit* (Rolet, Zyra, Institucioni, Statusi), *Institucionet* (Lloji, Ministria,
   Statusi), *Gjyqtarët* (Gjykata, Statusi), lookup lists (Statusi; Grupi in the lists that have one), *Gjykatat*
   (Shkalla, Jashtë vendit, Statusi), *Zyrat* (Lloji, Zyra mëmë, Statusi), *Agjenda* (Zyra, Avokati, Gjykata, Lloji,
   të anuluarat), *Dokumentet* (Lloji, Shkalla, Statusi), *Vazhdo çështje* (Gjykata, Statusi).
6. **Dashboard:** no "Regjistro çështje të re" button (it stays on *Çështjet*).
7. **Notify a not-closed level.** As protokoll, *Vazhdo çështje*, search an open case:
   - with a primary lawyer: the button reads "Njofto avokatin dhe Kryetarin"; the dialog "Do të njoftohen: Avokat1 Dev
     (avokati), Kryetar Dev (Kryetari)."; after sending "Avokati dhe Kryetari u njoftuan: …".
   - without one (REGISTERED): "Njofto Kryetarin"; the dialog lists only the Kryetar and warns "Çështja nuk ka avokat
     të caktuar; Kryetari i zyrës duhet ta caktojë."; after sending "Kryetari u njoftua: Kryetar Dev (Kryetari).".
   - the same summons again within 24 h: "Janë njoftuar më …: …" (no new history row). *Historiku* of the case shows
     the row with "U njoftuan: …".
8. **Closed cases.** Close one of avokat1's cases with the SQL command of 018. Then: avokat1 *Çështjet e mia* →
   not in *Të hapura*, found with *Të mbyllura* and *Të gjitha*, and by a search (no status chosen); kryetar *Të zyrës*
   and monitor *Të gjitha* the same; the header search finds it for each of them (it opens the user's widest tab);
   protokoll does not see it in *Të regjistruara nga unë* but *Vazhdo çështje* finds it with "Vazhdo në shkallën e
   radhës". Choosing *Të hapura* explicitly and then searching keeps the search to open cases.

## 036 / 037 — Record the decision and close; complete it later (API; the screens come with 039)
**User:** avokat1, an ACTIVE first-instance case of theirs (appeal kind ANKIM). Calls with the dev header
`X-Dev-User: avokat1@ash.local`; `rowVersion` from `GET /api/cases/{id}` (`header.rowVersion`).
1. `GET /api/cases/{id}` → `data.appealKind` = "ANKIM" (an appeal level: "REKURS"); `decision` null.
2. `POST /api/cases/{id}/decision` `{ "resultCode": "HUMBUR", "decisionReceived": false, "rowVersion": n }` → 200,
   `caseRowVersion` n+1, `suggestions` []. The case is CLOSED (header and list); `next_hearing_at` NULL; the team is
   unchanged — avokat1 finds it under *Çështjet e mia* with *Të mbyllura* / *Të gjitha*; *Historiku*:
   DECISION_RECORDED ("… (pa vendimin e arsyetuar).") and CASE_CLOSED. In the database `decision_received = 0`
   (statistics: "mbartur në pritje të vendimit të arsyetuar").
3. Refusals (nothing changes): `isFinal` + `isAppealed` → 400 "Një vendim në formë të prerë nuk mund të jetë i
   ankimuar/rekursuar."; `isRecursed` on a first-instance level → 400 (`isRecursed`); "TJETER" without
   `resultOtherText` → 400; `decisionReceived: true` without a document, or with a document that is still *Në
   kontroll*, of another type or another dossier → 400 (`decisionDocumentPublicId`); a decision date in the future or
   before the act date → 400; a negative or "1,5" amount → 400; as avokat2 (secondary) → 403; an old `rowVersion` →
   409; on a REGISTERED case → 409 "Vendimi regjistrohet vetëm për një çështje aktive …".
4. With a SCHEDULED hearing: the POST → 409 `case.open_hearings` with `hearings` (time, type); again with
   `"cancelOpenHearings": true` → 200, the hearings are *Anuluar* with "Anuluar në mbyllje të çështjes" (one
   history row each), the agenda no longer shows them.
5. Money: `"wonAmount": "9999999999999999.99"`, `"preventedDamageAmount": "1250000.5"`, `"currency": "EUR"` →
   GET returns exactly "9999999999999999.99", "1250000.50", "EUR".
6. Result "KTHIM_RIGJYKIM" → `suggestions` ["RETRIAL_EXPECTED"]; `isFinal: true` → "ARCHIVE_SUGGESTED".
7. On the CLOSED case: upload a PADI_PROVA document → 409 "Në një çështje të mbyllur ngarkohet vetëm vendimi.";
   upload a VENDIM PDF → 201 (*Gati* after the scan). Other actions (hearings, parties, data) stay 409.
8. `PUT /api/cases/{id}/decision` with the decision's `rowVersion` (`decision.rowVersion`), `"decisionReceived": true`
   and that document's `decisionDocumentPublicId` → 200; `decision.decisionReceivedAt` set, `decision.document` shows
   its title; *Historiku* DECISION_UPDATED shows only *decisionReceived* and *decisionDocument*. The same PUT again →
   400 "Nuk ka ndryshime."; `"decisionReceived": false` → 400 (cannot be undone); an old `rowVersion` → 409.
9. Appeal later: PUT `"isAppealed": true` → 200. Continue the dossier at X/2 (*Vazhdo çështje*); then PUT
   `"isFinal": true` (or `"isAppealed": false`) on X/1 → 409 "Kjo çështje ka vazhduar në …/2". On a case without a
   newer level, `"isFinal": true` → 200 with `suggestions` ["ARCHIVE_SUGGESTED"].
10. A case whose ministry link is DELEGATED: "Institucioni me delegim" (`delegim@ash.local`, task 053k) sees
    `decision.record` in `permissions` and may close it; the jurist of a monitoring case never does.

## 038 — Archive the dossier (API; the screen comes with 039)
**User:** avokat1, an ACTIVE case of theirs. `GET /api/cases/{id}` → `header.dossierPublicId`, `header.rowVersion`,
`header.isArchived` (false).
1. Before a final decision `permissions` has no `decision.close_final`; `POST /api/dossiers/{dossierPublicId}/archive`
   `{ "rowVersion": n }` → 409 `dossier.latest_not_closed`. Close it with `isFinal: false` → archive → 409
   `dossier.not_final` ("… nuk ka marrë formë të prerë …").
2. `PUT /decision` with `"isFinal": true` (or close a fresh case with `isFinal: true`) → `permissions` now has
   `decision.close_final` (only on the dossier's latest level). As avokat2 (secondary) the archive → 403; with an old
   `rowVersion` → 409; as avokat1 → 200 (`archivedAt`, `caseRowVersion` n+1). *Historiku*: DOSSIER_ARCHIVED.
3. Afterwards on EVERY level of the dossier: `header.isArchived` true, `permissions` = ["case.view"]. Adding a hearing,
   a party, the claim, a document upload, removing a document, `PUT /decision`, notify-not-closed → 409 "Dosja është e
   arkivuar." (code `dossier.archived`). *Shkarko* / *Shiko* of its documents still work. The archive again → 409.
4. *Vazhdo çështje*: the dossier shows "Dosja është arkivuar" and cannot be continued (task 018).
5. Undoing an archiving is not possible (client question *unarchive*).

## 039 — The Vendimi tab: close, complete later, archive (screens)
**User:** avokat1, an ACTIVE first-instance case of theirs with a scheduled hearing. Every step happens on the case
page (the address stays `/cases/{id}/vendimi`).
1. *Vendimi* without a decision: "Nuk është regjistruar vendim …" and the gold "Regjistro vendimin dhe mbyll
   çështjen". As avokat2 (secondary) or the Kryetar: no button.
2. The dialog's stepper:
   - *Rezultati*: the result (required; *Tjetër* asks for its text), number, date (not in the future, not before the
     act date), dispositive. "Vazhdo" stops on a missing result.
   - *Vlerat*: four rows (Vlera e fituar / e humbur / Dëmi i parandaluar / i shkaktuar), each with Shuma and Sipërfaqja
     side by side, then *Shpenzimet gjyqësore*; one *Monedha*. "1250000,5" shows as 1.250.000,5.
   - *Statusi*: on a first-instance level "Ankimuar" (an appeal level: "Rekursuar"), plus "Formë e prerë"; ticking one
     disables the other.
   - *Vendimi i arsyetuar*: without the switch → "Çështja mbyllet edhe pa vendimin e arsyetuar …". With it: drop or
     choose the PDF (type Vendim fixed) and *Ngarko*; "Po kontrollohet…" until it is *Gati*, then it is selected by
     itself. Or pick an earlier Vendim of the dossier (only *Gati* ones can be picked).
3. *Mbyll çështjen* → "Çështja ka seanca të planifikuara" with the hearing; *Anuloji dhe mbyll* → closed: toast
   "Vendimi u regjistrua dhe çështja u mbyll.", the header chip "E mbyllur më dd.MM.yyyy", *Seancat* shows the hearing
   *Anuluar*, the list shows the case under *Të mbyllura*.
4. With "Formë e prerë": "Arkivo çështjen?" appears (the whole dossier becomes read-only; "Arkivimi nuk zhbëhet.").
   *Jo tani* closes it — "Arkivo dosjen" stays on the tab. *Arkivo* → the banner "Dosja është e arkivuar – vetëm për
   lexim", no more buttons on any tab; *Shiko* of the decision still opens the PDF.
5. Result *Kthim për rigjykim* → "Kur të vijë fletëthirrja, Protokolli e vazhdon çështjen si rigjykim." (only
   "Në rregull").
6. Closed without the reasoned decision: the orange notice "Vendimi i arsyetuar nuk ka ardhur ende" with *Shto
   vendimin e arsyetuar* → the dialog opens at step 4; after saving the notice disappears and *Ardhur vendimi i
   arsyetuar* reads "Po" with *Shiko*. The switch cannot be turned off again.
7. Errors: two users completing the decision at once → the second gets the shared conflict message with *Rifresko*;
   a server field error opens the step that has the field.
8. At 390 px the dialog fills the screen, the money and m² fields stack, no side scrolling.

## 040 — Notifications (API and SignalR)
**Setup:** run the migrator (`dotnet run --project src/backend/Database/Ash.Database -- --local`; script 015 adds
`notifications.public_id`), then restart the API. Calls with Scalar or curl and the header
`X-Dev-User`. Each step: check `GET /api/notifications` (newest first, `items`, `nextCursor`, `total`) and
`GET /api/notifications/unread-count` as the RECIPIENT; the actor never gets a row.
1. As kryetar assign a REGISTERED case: avokat1 primary, avokat2 secondary → each has one "Ju u caktua një çështje"
   ("U caktuat avokat kryesor/dytësor në çështjen X/1 (Gjykata …)."), `linkPath` `/cases/{publicId}/te-dhenat`; the
   Kryetar has none; the users of an institution linked to the case have "Çështjes iu caktua avokati". No text shows
   the object of the case.
2. Kryetar assigns himself as primary → no row for him.
3. Register a case as protokoll → the office head gets "Çështje e re për caktim".
4. Transfer (primary avokat1 → avokat2) → avokat2 "Ju u caktua një çështje", avokat1 "Çështja u transferua … u caktua
   Avokat2 Dev."; an unchanged lawyer gets nothing.
5. As the primary add / remove a legal assistant → the assistant gets "U shtuat / U hoqët si ndihmës ligjor".
6. As institucion assign the jurist, then a second one → the first "U caktuat jurist", then "U zëvendësuat si jurist";
   the new one "U caktuat jurist".
7. As protokoll add an institution party to a case → the institution's users get "Çështje e re për institucionin".
   Delegate / end the delegation (primary lawyer) → institution users and the case's jurist: "Çështja u delegua /
   relatua", "Delegimi përfundoi".
8. Notify-not-closed on an open level → primary lawyer and Kryetar: "Shkalla nuk është mbyllur" with the hearing date
   and time of the new summons in Tirana time; link to the *Vendimi* tab.
9. Close a case (avokat1) → avokat2, the assistant and the jurist get "Çështja u mbyll" ("Avokat1 Dev mbylli …").
10. With the ClamAV profile upload the EICAR test file → the uploader and every active admin get "Dokumenti u refuzua"
    (no title, no virus name in the text).
11. A deactivated user (Admin → users) gets no new rows.
12. `POST /api/notifications/{id}/read` → `unread` one less (again: same `readAt`); another user's id → 404.
    `POST /api/notifications/read-all` → `marked`, `unread` 0; the other users' rows unchanged.
13. Paging: `?limit=5`, then `?after={nextCursor}` — no repeats, no gaps; `?unreadOnly=true`; `limit` above 50 gives 50.
14. SignalR (browser console on the app, dev login): connect to `/hubs/notifications?dev_user=avokat1%40ash.local`
    (`@microsoft/signalr`, `withUrl(...)`), `on('notification.new')`, `on('notification.count')`,
    `on('document.status')`. Assign a case to avokat1 in another window → both arrive right after the response, never
    for a request that failed. Upload a file → `document.status` `{ documentPublicId, scanStatus: "CLEAN" }` when the
    scan finishes. Without `dev_user` (or an unknown / inactive user) the negotiate answers 401; `dev_user` on any
    other URL is ignored (401).
15. Logs (`Host/Ash.Api/logs/ash-*.log` and the console): no line contains `access_token=` or `dev_user=` with a
    value — only `***` where a URL with them was logged; the request lines show the path only.

## 040 — Notifications (screens)
**Setup:** API and `npm run dev` (the Vite proxy forwards `/hubs` with WebSockets). Two browsers (or a normal and a
private window): avokat1 in one, kryetar in the other.
1. avokat1 on the dashboard: the bell in the header has no badge, the browser tab reads "ASH".
2. kryetar assigns a REGISTERED case to avokat1 (*Pa avokat* → *Cakto avokatin*). In avokat1's window, without
   reloading, within about 2 seconds: the badge shows 1, the tab title "(1) ASH", and a toast "Ju u caktua një
   çështje" with *Hap* (bottom right). A screen reader announces "Njoftim i ri: Ju u caktua një çështje". Reloading
   the page shows the badge again but no toast.
3. The bell opens "Njoftimet e fundit": the newest 10, unread in bold, each with its text and a relative time
   ("Tani", "5 minuta më parë"); hovering the time shows dd.MM.yyyy HH:mm. A click: the popover closes, the case opens
   on *Të dhënat*, the badge goes down, the title back to "ASH".
4. *Shëno të gjitha si të lexuara* (popover or page): every row loses the bold, the badge disappears; disabled when
   nothing is unread. In a second tab of the same user the badge follows at once (the count is pushed).
5. *Shiko të gjitha* → *Njoftimet* (also from the sidebar's *Njoftime*): 10 per page with "1–10 nga N", *Pas ›* /
   *‹ Para*, the page size; *Shfaq*: *Të gjitha* / *Të palexuara* (in the URL as `?show=unread`; empty: "Nuk keni
   njoftime të palexuara."). An icon per type (assignment, transfer, institution, delegation, closed case, rejected
   document, level not closed).
6. More than 99 unread: the badge reads "99+".
7. A notification of a case the user can no longer see (e.g. avokat1 after a transfer away): the click opens the case
   page with "Çështja nuk u gjet ose nuk keni akses."
8. Documents (035): upload a file on *Dokumentet* — "Në kontroll" turns *Gati* by itself when the scan finishes, with
   no request every 3 s in the browser's network tab (one GET after the `document.status` push). The same in the
   close-case dialog's *Vendimi i arsyetuar* step.
9. Stop the API for a moment, then start it again: the app reconnects by itself and fetches the count, the newest
   notifications and the documents once. With the hub unreachable, the count still refreshes every 60 s while the tab
   is visible (not in a hidden tab).
10. At 390 px: the popover fills the width, the page shows one column (text with its time under it), no side
    scrolling.

## 041 — Background jobs
**Setup:** run the migrator (`dotnet run --project src/backend/Database/Ash.Database -- --local`: script 016 and
LocalScripts 905), restart the API. Optional: `dotnet user-secrets set "ConnectionStrings:AuditDigest"
"Server=localhost;Port=3306;User=ash_audit_digest_local;Password=ash_audit_digest_local_pw;"` in `Host/Ash.Api` (without
it, Development uses the App connection). The scheduler runs by itself (`Jobs:Enabled` true); to run a job NOW, as
admin (header `X-Dev-User: admin@ash.local`), Development only:
`POST /api/admin/jobs/{name}/run` → `{ "job", "outcome": "Succeeded" | "Skipped" | "Failed", "error" }`.
Names: `data-reminders`, `hearing-reminders`, `notification-cleanup`, `audit-digest`, `partition-check`. As avokat1 → 403;
an unknown name → 404. The job's state: `SELECT * FROM ash_ops.job_runs;`. A job runs with the real time, so make the data
old with SQL first.
1. **data-reminders** — on an ACTIVE case of avokat1 with no data entered:
   `UPDATE case_assignments SET assigned_at = UTC_TIMESTAMP() - INTERVAL 31 DAY WHERE case_id = … AND case_role = 'PRIMARY_LAWYER' AND ended_at IS NULL;`
   Run → avokat1 and kryetar get "Të dhënat e çështjes mungojnë" ("Kanë kaluar 30 ditë …"); run again → nothing new.
   With `INTERVAL 7 MONTH` (on another case) → the 30-day and the 6-month one, the 6-month one also to monitor.
   After entering data on the case (first_data_entry_at set), a CLOSED case, an archived dossier → none.
2. **hearing-reminders** — add a hearing for tomorrow; after 08:00 run → the team (primary, secondary, assistant,
   jurist) gets "Seancë nesër" with the date and time, once; move it to the day after tomorrow → tomorrow after 08:00 a
   new reminder. Notify-not-closed (018) with a hearing within 3 days → avokat1 and kryetar get "Seanca e shkallës
   tjetër afron", once.
3. **notification-cleanup** — `UPDATE notifications SET created_at = UTC_TIMESTAMP() - INTERVAL 13 MONTH, read_at = UTC_TIMESTAMP() WHERE id = …;`
   run → deleted; an unread one just as old stays.
4. **audit-digest** — run → `SELECT audit_day, row_count, HEX(digest) FROM ash_audit.audit_daily_digests;` has every day
   from the first audit row up to yesterday (Tirane); run again → nothing new. `GET /api/admin/audit/verify?from=…&to=…`
   (admin) → `ok: true`. Change one row (`UPDATE ash_audit.audit_log SET action = 'X' WHERE id = …;`) → `ok: false`,
   `firstBrokenDay` = that row's Tirane day. As avokat1 → 403.
5. **partition-check** — normally nothing. Insert a row far in the future
   (`INSERT INTO ash_audit.audit_log (occurred_at, action) VALUES ('2040-01-01', 'TEST');`), run → every active admin
   gets "Problem me particionet e bazës së të dhënave" and the API log has an error; run again the same day → no second
   notification. Delete the row afterwards.
6. `GET /health` → `jobs: Healthy`; stop the API for more than 2 h 15 min (or set `last_succeeded_at` of
   `data-reminders` 3 hours back and wait for nothing to run) → `Unhealthy` naming the job. Restarting the API runs every
   job whose run was missed (the digest fills every missed day).
7. Two API instances (e.g. `dotnet run` twice on different ports): each job runs on one of them only
   (`SELECT IS_USED_LOCK('job:data-reminders');` while it runs), nobody gets a reminder twice.

## 046 — Dashboard per role
**Setup:** restart the API; `npm run dev`. Log in as each user and open *Dashboard*. Rule for every step: **the number
on a card = the total ("1–10 nga N") of the list it opens**, and every card, row and bar opens that list or the case.
"Sot" and "këtë javë" are Tirane days; the week starts on Monday.
1. **protokoll**: *Regjistruar sot* (still waiting, registered today — opens the list with today's registration date),
   *Në pritje të Kryetarit*, *Regjistrimet e mia të fundit* (8 rows, *Shiko të gjitha →*). No "Regjistro" button.
2. **kryetar**: *Pa avokat* with "Më e vjetra pret N ditë" (orange; green at 0), *Pa të dhëna mbi 30 ditë*, *Seanca të
   pa përditësuara* (red), *Në pritje të vendimit të arsyetuar*; the chart *Ngarkesa e avokatëve* (avokat kryesor /
   dytësor, numbers on the bars, legend on top; a lawyer's name or bar opens the office list for that lawyer);
   *Seancat e zyrës këtë javë* (date and time, case, court). The list opened from a card shows the chip "Filtri: …";
   removing it shows the whole tab.
3. **avokat1** (also **ndihmes**, **jurist**): *Çështjet e mia të hapura*, *Seanca për t’u përditësuar*, *Pa të dhëna
   mbi 30 ditë*, *Në pritje të vendimit të arsyetuar*, *Seancat e mia këtë javë*, *Aktiviteti i fundit* (8 history
   rows of the cases they are on now, "2 orë më parë" with the exact time on hover; a row opens the case's *Historiku*).
4. **institucion**: *Çështjet e institucionit*, *Të deleguara*, *Pa juristin tonë* (assign a jurist → one less after a
   reload), *Seancat e ardhshme*.
5. **monitor**: *Seanca këtë javë*, *Pa të dhëna mbi 6 muaj*, *Në pritje të vendimit të arsyetuar*, the chart
   *Çështjet sipas zyrës* (të hapura / të mbyllura; each part opens that office's list, open or closed).
6. **admin**: the Monitor's widgets plus *Dokumente të pakontrolluara*, *Punët në sfond* (each job, last success,
   "Në rregull" / "Me vonesë" with its error) and *Particionet e bazës së të dhënave*. These three link nowhere: there is
   no admin page for jobs, documents or partitions yet.
7. Refresh: another user assigns a case to avokat1 while avokat1's dashboard is open → the toast appears and the
   numbers update without a reload. Switching to another window and back after more than a minute reloads it; within a
   minute it does not. No request repeats on its own (network tab).
8. A widget that fails shows "Ky element nuk u ngarkua." with *Provo përsëri* in its own place; every other card
   renders. Hard to provoke by hand — the integration test `DashboardTests.A_widget_that_fails_…` covers it. A query
   that waits more than 5 s fails its widget the same way (MySQL stops it: max_execution_time / lock_wait_timeout are
   set on the request's connection while the dashboard is built).
9. Widths: 4 cards per row from 1200 px, 2 on a tablet, 1 at 390 px; charts and tables fit, no side scrolling.
11. Query time limit (all endpoints): every read query is stopped by MySQL after `Database:QueryTimeLimitSeconds`
    (default 5). To see it, set it to `0.001` in user-secrets and restart: lists answer 503 "Kërkimi zgjati shumë.
    Provoni me më shumë filtra." at once (writes still work). Put it back afterwards.
10. Speed: `GET /api/dashboard` (network tab) well under 500 ms; a second load within a minute is faster still (office
    widgets cached 60 s, Monitor/admin 5 minutes).

## 047 — Global search (header typeahead)
**Setup:** restart the API; `npm run dev`. Log in as kryetar.
1. `Ctrl+K` (or `/` when not typing) puts the cursor in the header search. Type one letter: nothing. Two or more: after
   a short pause a list opens (a small spinner on the right while it loads; the box does not move).
2. Part of a case number (e.g. the last 4 digits): the case appears under *Kodi* with the digits in bold, its status
   chip, court and primary lawyer. ↑/↓ move the highlight; Enter opens the case; Esc closes the list.
3. A party's name ("Hoxha"): under *Pala*, "Pala: Petrit Hoxha" with the name in bold. "ceshtje" finds objects with
   "çështje" (*Objekti*, a short piece of the object around it). A judge's surname: *Gjyqtari: …* — that judge's cases.
   An institution's name: *Institucioni: …*.
4. The exact case code ranks first, above other cases that only mention it.
5. Closed and archived cases are found too (status chip; "Arkivuar").
6. Only cases you may see: as kryetar of TR, a party that also appears in a Durrës case shows only the TR case.
7. The last row "Shiko të gjitha rezultatet (N) →" — and Enter with nothing highlighted — opens the case list with
   the same text and *Të gjitha* statuses; it lists the same cases (N = its "nga N", up to 1,000).
8. Typing fast: only the last text's answer is shown (the network tab shows earlier requests cancelled).
9. Nothing found: "Nuk u gjet asnjë çështje për „…“."
10. At 390 px the header shows a search icon; it opens a full-width search sheet (the same list); choosing a case
    closes it. No side scrolling.
11. Logs (`Host/Ash.Api/logs`): no search text — `q=***` where a URL is logged.

## 048 — Profile
**Setup:** run the migrator (`dotnet run --project src/backend/Database/Ash.Database -- --local`, applies 017; a second
run applies nothing); restart the API; `npm run dev`.
1. Avatar menu → *Profili* (or the *Profili* menu): *Të dhënat* shows name, surname, email and last login, read-only,
   with the note "Emri dhe email-i menaxhohen nga sistemi i identifikimit". No way to edit them.
2. *Rolet dhe zyra*: as kryetar — the office, the chip "Kryetari i Zyrës" and "Kryetar i zyrës …". As institucion —
   the institution instead of an office, no "Kryetar" chip.
3. *Kontakti* → pencil: type `069 123 4567` → *Ruaj*: shown as "+355 69 123 4567", toast "Telefoni u ruajt.".
   Also accepted: `+355 69 123 4567`, `+355 4 222 3333`, `+39 333 123 4567`. Refused with a message under the field:
   `12345`, `069 123 45`, `+355 1 234 5678`. Esc cancels. Empty + *Ruaj* removes the number.
4. Audit (admin → audit log or `ash_audit.audit_log`): a `PROFILE_UPDATED` row whose details show only the last 3
   digits (`+*********567`).
5. *Njoftimet*: the mandatory ones (assignment, missing data, infected document…) are on, greyed, with a lock; hover or
   Tab onto one: "Ky njoftim është i detyrueshëm". The partition alert is listed only for the admin.
6. Turn off *Seancë nesër* as avokat2; reload: still off. With a hearing tomorrow, run the job (as admin
   `POST /api/admin/jobs/hearing-reminders/run` after 08:00): the rest of the team gets the reminder, avokat2 does not.
7. `PUT /api/identity/profile` with `{"firstName":"X"}` (Swagger) → 400 "Vetëm telefoni mund të ndryshohet…";
   `PUT /api/identity/profile/notifications` with `{"LAWYER_ASSIGNED": false}` → 400.
8. Admin → *Përdoruesit*: the *Telefoni* column shows the number set in step 3 (formatted). Other users never see it.
9. 390 px: the cards stack, the switches and locks do not cover the labels, no side scrolling.

## 049 — Chat backend (API only; the screens come with 050)
**Setup:** run the migrator (`dotnet run --project src/backend/Database/Ash.Database -- --local`: script 018 and
LocalScripts 906, the local chat account). Then, once — the API does not start without it:
`dotnet user-secrets set "ConnectionStrings:Chat" "Server=localhost;Port=3306;User=ash_chat_local;Password=ash_chat_local_pw;Database=ash_chat;" --project src/backend/Host/Ash.Api`
and restart the API. Use Scalar (`/scalar`) with the header `X-Dev-User`. Take an ACTIVE case with avokat1 primary,
avokat2 secondary, an assistant and a linked institution (its public id = `{case}`).
1. As avokat1: `POST /api/chat/cases/{case}/conversation` → 200 with `id`, `title` "<case code> · <court>". Again →
   the same `id`. As avokat2 → the same `id`.
2. As kryetar (not on the team) → 403 "Nuk jeni pjesë e ekipit…"; as protokoll → 404. As institucion (linked) → 200.
3. As avokat1: `POST /api/chat/conversations/{id}/messages` `{"body":"  Tung  "}` → 201, body "Tung". Empty or more than
   4,000 characters → 400. 31 messages within a minute → the 31st is 429.
4. As avokat2: `GET /api/chat/unread-total` → 1; `GET /api/chat/conversations` → unread 1, preview "Tung".
   `POST /api/chat/conversations/{id}/read` `{"lastMessageId":"<message id>"}` → 204; unread-total → 0. An older id
   never brings unread messages back.
5. `GET /api/chat/conversations/{id}/messages?limit=2` → newest first, `nextCursor`; with `before=<nextCursor>` the
   older ones, no repeats. No numeric ids anywhere in the answers.
6. `PUT /api/chat/messages/{id}` (own, within 15 minutes) → 200 with `editedAt`; someone else's → 403; after 15 minutes
   → 409. `DELETE` (own) → 200; others see "Mesazh i fshirë"; audit log: `CHAT_MESSAGE_DELETED`.
7. As kryetar: transfer the primary to another lawyer. → In the chat: "<new> u shtua në bisedë", "<old> doli nga
   biseda"; the old lawyer gets 403 on the messages and 404 on opening the case chat. Audit: `CHAT_PARTICIPANT_ADDED`
   / `CHAT_PARTICIPANT_REMOVED`. Same for adding / removing an assistant.
8. `POST /api/chat/direct` `{"userId":"…"}` → 403 "Biseda lejohet vetëm brenda çështjes."
9. Close the case (final): sending still works. Archive the dossier: sending → 409 "Biseda është vetëm për lexim.";
   the history still loads.
10. Live (browser console or a SignalR test client on `/hubs/chat?dev_user=avokat2@ash.local`): `message.new`,
    `message.edited`, `message.deleted`, `conversation.read`, `participants.changed` arrive; after the transfer in step
    7 the old lawyer's connection receives nothing more from that chat.
11. Repair: `POST /api/admin/jobs/chat-reconcile/run` as admin → `Succeeded`; a participant row changed by hand in
    `ash_chat.conversation_participants` is put right (with its system message).
12. Logs: no `access_token` / `dev_user` values in the request lines of `/hubs/chat`.

## 050 — Chat (screens)
**Setup:** 049's setup (ConnectionStrings:Chat), restart the API, `npm run dev`. An ACTIVE case with avokat1 primary,
avokat2 secondary and ndihmes as assistant. Two browsers (or a normal and a private window): avokat1 and avokat2.
1. Case page as avokat1: the tab *Chat* is there (not for kryetar or protokoll). It opens the case's conversation;
   empty: "Nuk ka ende mesazhe. Shkruani i pari."
2. Type two lines (Shift+Enter between them), Enter: the message appears at once (light, "Po dërgohet…"), then normal;
   the line break is kept. In avokat2's window, on the same tab, it appears without a reload — once.
3. A message with `https://example.com` becomes a link (new tab); `<b>x</b>` and `javascript:alert(1)` stay plain text.
4. Day separators "Sot" / "Dje" / dd.MM.yyyy; the time under each message, the full date and time on hover. A run of
   messages of one person shows the name once.
5. Hover a message (or Tab onto it): *Përgjigju* — the answer shows "Përgjigje për …" above it. Own message within 15
   minutes: *Ndrysho* (Enter saves, Esc cancels; "(ndryshuar)" appears) and *Fshi* (asks first; everyone sees
   "Mesazh i fshirë"). After 15 minutes those two actions are gone.
6. avokat2 scrolls up in a long chat; avokat1 writes: no jump, a pill "1 mesazh i ri ↓" — clicking it goes down.
   At the bottom new messages scroll into view by themselves.
7. Scroll up: older messages load at the top and the view stays where it was (no jump). In a chat with more than 300
   messages, going back down drops the oldest from view; scrolling up loads them again.
8. 31 messages within a minute: "Po dërgoni shumë shpejt, prisni pak. Nuk u dërgua —" with *Provo përsëri* on it.
   Stop the API, send: *Nuk u dërgua — Provo përsëri*; start the API, *Provo përsëri* sends it.
9. Leave a draft, reload the tab: the draft is still there (per conversation).
10. Menu *Chat*: the badge counts unread messages, live. As avokat2 on the dashboard, avokat1 writes → badge 1; reload
    → still 1; the tab title adds it to the notifications ("(n) ASH"). No bell notification for chat messages.
11. *Chat* page: conversations newest first — case code · court, preview, time, unread badge (99+ above 99). Choose one
    → the thread on the right; reading it clears its badge (only when the newest message is on screen and the window
    has focus — read it in a background window: the badge stays).
12. *Pjesëmarrësit*: name, role on the case (Avokat primar, Avokat dytësor, Ndihmës ligjor, Jurist i institucionit,
    Institucioni) and the institution; no e-mail, no phone.
13. As ndihmes with the conversation open, avokat1 removes the assistant: at once "Nuk jeni më pjesë e kësaj
    bisede.", no message box; the conversation leaves the list. Reload with the same address: the same message (the
    API answers 403).
14. Archive the dossier: the thread shows "Biseda është vetëm për lexim." instead of the message box; history readable.
15. 390 px: the *Chat* page shows the list, then the thread with a back arrow; no side scrolling.
16. Keyboard only: Tab reaches the message list, the actions of each message and the box; a screen reader announces
    new messages (the list is a live log).

## 042 / 043 — Reports: active and closed cases (API only; the screens come with 045)
**Setup:** restart the API. Use Scalar (`/scalar`) with the header `X-Dev-User`. Optional: `ConnectionStrings:Reporting`
(otherwise the App connection is used).
1. As avokat1: `GET /api/reports/active-cases` → 10 rows a page, `total`, `nextCursor`, `columns` (Albanian headers).
   Only avokat1's open cases — the same ones as *Çështje* (all tabs, open). As kryetar: the office; as institucion: the
   linked cases; as monitor / admin: all. As protokoll → 403.
2. Filters: `officeId`, `lawyerId`, `courtId`, `categoryCode`, `courtLevelCode`, `priority`, `suspended`, `q` (the same
   search as the header). Each narrows the rows; `total` follows.
3. Columns: Paditësi / I padituri (with role in brackets) list every party; Avokati "primary / secondary"; Seanca e
   radhës; Përshkrimi i seancës së fundit; claim amount, currency and m² separate.
4. `GET /api/reports/active-cases/export` (same filters) → downloads `raporti-ceshtjet-aktive-YYYY-MM-DD.xlsx`. In
   Excel: header row frozen with filters, long Objekti wrapped; dates are real dates (sortable), hearings with the time
   (Tirane); amounts are numbers with 2 decimals; a party named `=1+1` shows `=1+1` (text, not 2). Sheet *Filtrat*: the
   report, when (Tirane), by whom, the filters in Albanian, the row count.
5. Audit log: one `REPORT_EXPORT` row per export with the report, the filters and the row count.
6. `GET /api/reports/closed-cases?closedFrom=2026-01-01&closedTo=2026-12-31` → one row per closed LEVEL, levels of a
   dossier together (/1 then /2), each with its own result, decision number and date, values per currency, the flags
   Ankimuar / Rekursuar / Formë e prerë / Vendimi i arsyetuar ardhur. Avokati = the primary when the level was closed
   (a later transfer does not change it). Filters: `resultCode`, `outcomeGroup` (WIN / LOSS / PARTIAL / PROCEDURAL /
   OTHER), `courtId`, `lawyerId`, `officeId`. `closedFrom` after `closedTo` → 400.
7. `GET /api/reports/closed-cases/export` → `raporti-ceshtjet-mbyllura-YYYY-MM-DD.xlsx`, same checks as step 4.
8. An export of more than 200,000 rows → 400 "… Ngushtoni filtrat." (to try it: set `Reports:MaxExportRows` to 1 in
   user-secrets, restart, export; put it back).
9. A large export (a whole Monitor's report) downloads in a few seconds; the API's memory does not climb with it.

## 044 — Statistics (Tabela 5 and 6; API only, the screens come with 045)
**Setup:** run the migrator (script 019), restart the API. Scalar with `X-Dev-User`. Definitions:
`docs/statistics-definitions.md`.
1. As monitor: `GET /api/reports/statistics?year=<this year>` → `courts` (one row per court of the LEVEL, the last row
   "Gjithsej"), `measures` (the Albanian names), `money` (one row per court and currency, plus a total per currency —
   never ALL and EUR together), `results`, `inHand` (per primary lawyer), `filters`.
2. Point in time: a case registered last year and closed this year counts in last year's *Aktive*. A case registered on
   31.12. late in the evening counts in that year.
3. Last year without a snapshot: `"frozen": false` and the warning "Jo e ngrirë…". As admin:
   `POST /api/admin/statistics/snapshot/<last year>` with `{"reason":"…"}` (at least 10 characters; shorter → 400)
   → `{ year, cases }`; the same GET now says `"frozen": true`.
   Change a case's category (Delegim) — last year's figures stay; this year's change. Audit: `ADMIN_CHANGE` with
   `STATISTICS_SNAPSHOT_TAKEN` / `_REPLACED` and the reason. The current year → 400; as kryetar → 403.
4. Drill-down: for any number, `GET /api/reports/statistics/cases?year=…&measure=<code>&courtId=…` (and `resultCode`,
   `rowLawyerId` for the Rezultati / Në dorë rows) → exactly that many cases (`total`), 10 a page.
5. Filters: `officeId`, `lawyerId` (open figures by the primary at year end / today, closed figures by the primary at
   closing), `table=6&institutionId=` (party or linked), `table=6&ministryId=` (the ministry and its agencies).
6. Access: kryetar sees only their office (another `officeId` → 403); a lawyer only their own cases (another
   `lawyerId` → 403); institucion only Tabela 6 of their institution (`table=5` → 403); protokoll → 403.
7. `GET /api/reports/statistics/export?year=…` → `raporti-statistikat-<year>-<date>.xlsx`: sheets *Tabela 5*,
   *Rezultati*, *Në dorë*, *Filtrat*; numbers are numbers; one damage column per currency, m² separate. Audit
   `REPORT_EXPORT`.
8. The current year's figures are cached for 5 minutes: a change shows within 5 minutes.

## 045 — Reports and statistics (screens)
**Setup:** restart the API (042–044), `npm run dev`.
1. As kryetar: menu *Raporte* → tab *Çështjet aktive* (the default). Click *Eksporto në Excel* → the file
   `raporti-ceshtjet-aktive-<date>.xlsx` downloads (two clicks from the menu). While it downloads: "Po shkarkohet… 0,4 MB"
   and *Anulo* (cancels; "Eksporti u anulua.").
2. Below the button: "Do të eksportohen N rreshta" = the table footer's total. Above 200.000 rows the button is disabled
   with "… Ngushtoni filtrat."
3. Filters (*Filtra*): kryetar has the lawyer filter but no office filter; monitor / admin both; a lawyer neither (only
   their own cases). Every filter goes into the address (reload keeps it), shows as a chip; *Pastro filtrat* clears all.
4. Tables: 10 rows a page with *Para* / *Pas* and "1–10 nga N"; wide tables scroll sideways inside the card, the case
   code column stays; the case code opens the case; dates dd.MM.yyyy (hearing with the time); amounts "1.500,50 ALL"; m²
   separate.
5. *Çështjet e mbyllura*: date range (*Mbyllur nga / deri*), result, result group, court, lawyer; the levels of one
   dossier sit together in one shaded / unshaded block (/1 then /2).
6. *Statistikat*: year, *Tabela 5* / *Tabela 6* (Tabela 6 asks for an institution or a ministry first). The badge: "E
   ngrirë më …" (a frozen year), "Viti nuk është ngrirë ende — shifrat mund të ndryshojnë", or "Viti aktual — shifra të
   përditësuara". As institucion: only Tabela 6 of their institution, no switch.
7. The table: the header row and the *Gjykata* column stay while scrolling; *Gjithsej* bold at the bottom; numbers
   right-aligned "1.234"; damage per currency, m² separate. Hover (or Tab onto) a column name: its definition.
8. Click any number above 0: a drawer "<measure> — <court>" lists exactly that many cases, 10 a page, each a link.
   The same from *Rezultati* and *Në dorë* (with its bar chart).
9. *Printo*: A4 landscape — the title, the year, the filters, the table, "Gjeneruar më … nga …"; no menu, no buttons.
10. A query stopped by the time limit: "Raporti zgjati shumë. Ngushtoni filtrat."; an export: "Eksporti zgjati
    shumë. Ngushtoni filtrat."
11. 390 px: filters in the *Filtra* panel, tables scroll sideways, no page-wide side scrolling.

## 053a — Client decisions 12.11.2026: closing, archiving, who records the decision
**Setup:** run the migrator (script 020), restart the API, `npm run dev`. An ACTIVE case: avokat1 primary, avokat2
secondary, ndihmes assistant, the ministry linked MONITORING.
1. Who records (tab *Vendimi*, "Regjistro vendimin dhe mbyll çështjen"): avokat1, avokat2, ndihmes and kryetar see the
   button; protokoll does not see the case; jurist (monitoring) does not see the button. Link the ministry as DELEGATED
   (delegation): now jurist sees it too. On a REGISTERED case nobody sees it (no lawyer yet).
2. Record a decision WITHOUT "Ka ardhur vendimi i arsyetuar": the case is CLOSED, the warning "pa vendimin e arsyetuar"
   shows, no "Arkivo çështjen?" — even with "Formë e prerë".
3. *Plotëso / Ndrysho vendimin* (avokat2 or kryetar): "Ka ardhur vendimi i arsyetuar" + upload the PDF; once it is
   checked ("Gati") and saved, with "Formë e prerë": "Arkivo çështjen?" appears; the *Arkivo* button too.
4. Archiving refused with the reason (Scalar, `POST /api/dossiers/{id}/archive`): not final → "nuk ka marrë formë të
   prerë"; reasoned decision not arrived → "Vendimi i arsyetuar … nuk ka ardhur ende"; no document → "Dokumenti i
   vendimit … nuk është ngarkuar"; document not yet clean / infected → "… nuk ka kaluar kontrollin e sigurisë".
5. Statistics (*Raporte → Statistikat*): next year, a level closed without the reasoned decision is NOT in "Mbartur
   aktive" (it is in "Mbartur në pritje të vendimit të arsyetuar"); a level without a decision is. Hover the column
   names: the client's wording.

## 053b — Client decisions 12.11.2026: next level and the registration search
**Setup:** restart the API, `npm run dev`, log in as protokoll. A closed X/1 with: a plaintiff, the ministry as
defendant, category, a recorded decision, and the ministry DELEGATED.
1. *Vazhdo çështje ekzistuese*: ten fields plus Statusi. Each one alone finds the dossier: kodi (also the end of the
   number), nr. i aktit, gjykata, gjyqtari (lists the chosen court's judges; changing the court clears it), paditësi,
   i padituri, objekti (a word of it), kategoria → nënkategoria (only after a category; changing it clears it),
   rezultati. Two fields together narrow the results (plaintiff + a different result → nothing).
2. The URL keeps every field (copy it into a new tab: same results). "Pastro fushat" empties them.
3. Each result shows "Objekti: …" (at most ~120 characters, cut at a word). *Vazhdo* still shows the whole object in
   the preview (audited).
4. Preview of X/2: the ministry is listed as "monitorim" (not delegim), with the note that category and delegation
   are not copied. Register X/2 at Gjykata e Apelit: no warning; the new case has no category, and the ministry is
   linked as MONITORING; X/1 keeps its DELEGATED link and category.
5. Retrial: close X/2 and X/3 (Gjykata e Lartë), then continue X/3 at a first-instance court → X/4 is created.
6. From a new registration with an act number that exists (duplicate dialog) → "Vazhdo…" opens the search with the
   act number filled.

## 051 — Security hardening: sign-in (phases 1–2)
**Setup:** run the migrator (script 028), restart the API, `npm run dev`.
1. **Development:**
   - The login page is the dev login (choose a user).
   - `GET /api/auth/config` answers `"mode":"dev"`.
2. **Production configuration check (no real tenant needed):** start the API with `ASPNETCORE_ENVIRONMENT=Production`.
   - Without an enabled `Auth:Providers` entry → it refuses to start and names the missing settings.
   - With `DevFeatures:HeaderAuth=true` or `Scanner:Mode=NoOp` → it refuses to start ("Development-only features…").
3. **With a real tenant (when AKSHI sends the configuration, docs/deploy/identity.md):**
   - The login page shows one button per enabled provider. With one provider it goes straight to its page.
   - Sign in with an e-mail an administrator created: you are in. *Administrimi → Përdoruesit → Identitetet* shows the
     provider and "Lidhur më …".
   - Sign in with an e-mail nobody created → you stay out (401); no user appears.
   - Same person through the other provider → a second identity row.
   - "Dil" → the provider's sign-out page, then the login; Back does not return into the app.
   - "Shkëput" an identity → the next sign-in links it again by e-mail.
4. **Idle logout:**
   - Leave the app 30 minutes untouched → "Jeni ende aty?" with a countdown from 60.
   - "Vazhdo punën" keeps you in.
   - Let it reach 0 → logged out, through the provider when one is used.
5. **Headers (phase 3):** browser dev tools → Network → any `/api/...` response:
   - `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `Cache-Control: no-store`.
   - `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`.
6. **Frontend CSP:**
   - `npm run build`, then `npx vite preview` → open http://localhost:4173.
   - The page's response has the `Content-Security-Policy` header.
   - Go through the screens (open a PDF preview, the agenda, the chat, an export): the console shows no "Refused to …".
7. **Rate limits (phase 4):**
   - In the header search, type quickly more than 60 searches in a minute → the message "Shumë kërkesa. Provoni pas pak.".
   - After a minute it works again; another user is not affected.
8. **"Shkëput" / "Lejo sërish"** (needs a real provider):
   - *Përdoruesit → Identitetet → Shkëput* on one account → that account cannot sign in any more ("E shkëputur më …").
   - The user's other account still works.
   - "Lejo sërish" asks for a reason; afterwards the account signs in again.
9. **Immediate effect:**
   - Remove a role, or deactivate a user, while that user has a page open.
   - Their next click → no access (no waiting a minute).
10. **Dependencies (phase 5):**
    - `.\tools\security\scan-deps.ps1` → "Dependency scan passed." (needs the network).
    - `node tools\security\third-party-notices.mjs` regenerates THIRD-PARTY-NOTICES.md; review the "Review needed" list.
11. **ZAP baseline:** API on :5004 (Development), then in `src/frontend`: `npm run build` and `npx vite preview --port 4173`.
    Run `.\tools\security\zap-baseline.ps1`; the report is `docs/security/zap-baseline.html`.

## 053h — Report builder (F, P6/P9): scopes, filters, exports, "Raportet e mia"
**Setup:** run the migrator (script 027), restart the API, `npm run dev`. The ACTIVE test case: avokat1 primary,
sekondar secondary, ndihmes assistant; the ministry linked MONITORING. Kryetar → *Ndihmësit ligjorë*: link ndihmes
to avokat1.
1. **Nothing runs before "Gjenero":**
   - *Raporte* opens on "Tabela 1 — Aktive" with Zyra, Avokati and Gjykata on top and the note "Zgjidhni filtrat…".
   - "Gjenero" shows the rows, 25 a page, and a "Gjithsej: N rreshta" row with the claim per currency.
   - Change a filter → the note "Filtrat kanë ndryshuar…" until "Gjenero" again.
2. **"+ Shto filtër":**
   - It lists every other column of the table (Tabela 3: Ankimuar, Dëm i shkaktuar, Data e vendimit…).
   - An added filter has an ✕ that removes it again.
3. **Dependent filters (Monitor):**
   - Choose Shkalla "Apel": Gjykata lists only appeal courts. Choose a court: Gjyqtari lists only its judges.
   - Change Shkalla: Gjykata and Gjyqtari are cleared.
   - Zyra → Avokati, Ministria → Institucioni and Kategoria → Lloji work the same way.
4. **Scope per role:**
   - avokat1: Zyra lists only TR; the rows are his cases; "Roli im" shows "Avokat primar".
   - sekondar: Tabela 1/3 show his case with "Avokat sekondar"; Tabela 5/6 → "Nuk keni të drejtë…".
   - ndihmes: Tabela 5 Avokati lists only avokat1; the numbers equal avokat1's own. Unlink → he disappears.
   - institucion and jurist: Tabela 1 lists every case where the ministry is a party or linked (its agencies too).
     A case where the ministry is only a party shows its code without a link. In Tabela 6 the Ministria filter is fixed
     (🔒). Both users see the same numbers.
   - kryetar: his office + supervised + own; Monitor/Admin: everything.
5. **Columns:**
   - "Kolonat" → untick, reorder with the arrows, "Zbato": the table shows only those, in that order.
   - The URL holds ?c=; a refresh keeps them.
6. **Exports ("Eksporto"):**
   - Excel opens with the header and filters.
   - CSV opens in Excel with ë/ç correct (UTF-8 BOM, ';', decimal comma).
   - PDF is A4 landscape with the filters, "Gjeneruar më … nga …", the chosen columns, the totals and page numbers.
   - More than 5,000 rows to PDF → "…përdorni Excel/CSV".
   - Each export has a REPORT_EXPORT audit row with its format.
7. **Shareable link:**
   - "Kopjo lidhjen" → open it in another browser as another user: the same table and filters, already generated,
     but only that user's cases.
8. **"Raportet e mia":**
   - "Ruaj raportin" with a name → the tab lists it; "Hap" reruns it at once.
   - Opened from there, "Ruaj raportin" offers "Mbishkruaj „…”" or a new one; the same name twice → "Keni tashmë…".
   - "Fshi" asks first. Another user never sees it.
9. **Drill-down (Tabela 5):** click a number → the cases; a case the user cannot open shows its code without a link.

## 053i — Two lawyer roles: "Avokat Shteti (primar)" and "Avokat sekondar"
**Setup:** run the migrator (script 026 and LocalScripts 907), restart the API, `npm run dev`. Dev login now also
offers sekondar@ash.local (SECONDARY_LAWYER only); avokat2 holds both roles; avokat1 only STATE_LAWYER; kryetar only
OFFICE_HEAD.
1. **Kryetar → assign a REGISTERED case:**
   - "Avokati primar" lists avokat1 and avokat2, not sekondar and not the Kryetar.
   - "Avokati sekondar" lists avokat2 and sekondar, not avokat1.
   - Assign avokat1 + sekondar → the case is ACTIVE; sekondar sees it in *Të miat*.
2. **Kryetar → *Ndrysho avokatin* on that case:**
   - The quick entry "Avokati sekondar bëhet primar (Sekondar Dev)" is missing (sekondar is not primary-capable).
   - "Avokati aktual primar bëhet sekondar" is missing (avokat1 is not secondary-capable).
   - In Scalar, `POST /api/cases/{id}/transfer` with `newPrimaryUserPublicId` = sekondar → 400 "Ky përdorues nuk ka
     rolin Avokat Shteti (primar)."; with secondary set to avokat1 → 400 "Ky përdorues nuk ka rolin Avokat sekondar."
3. **Admin → *Përdoruesit*:**
   - The roles read "Avokat Shteti (primar)" and "Avokat sekondar".
   - Remove SECONDARY_LAWYER from sekondar → 400 "Ky përdorues është avokat sekondar në 1 çështje të hapura…".
   - Transfer the secondary away first, then the removal works (give the role back afterwards).
4. **As sekondar:**
   - *Paneli* shows "Si avokat sekondar", "Seancat e mia këtë javë" and "Seanca për t'u përditësuar"; no statistics.
   - *Raporte → Statistikat* → the "Nuk keni të drejtë…" message.
   - *Raportet* (Tabela 1/3) lists their case.
5. **Kryetar → *Ndihmësit ligjorë*:** the lawyer list has avokat1 and avokat2, not sekondar.

## 053g — Profile, chat, Organika (E)
**Setup:** run the migrator (script 025), restart the API, `npm run dev`.
1. **Any user → *Profili*:**
   - No phone, no notification switches.
   - Shows name, e-mail, position and unit ("—" until Organika sets them), roles, office. No "Eprori" (task 053p).
2. **Chat as the Protokoll** who registered an ACTIVE case: *Chat* lists that case's conversation; they read and write
   there. In its participants they show as "Protokolli (regjistroi çështjen)".
3. **Chat as admin:**
   - Open any case → *Chat* tab: the messages are readable, the admin is not in the participants.
   - Send a message → "Admin … u shtua në bisedë", then the message; the participants list shows "Administratori".
   - The nightly chat-reconcile keeps them.
4. **Admin → *Administrimi → Organika*** (since 053l on the shared import pipeline — see 053l below):
   - "Shkarko shabllonin" → an .xlsx: sheet "Organika" with the three columns (email, pozicioni, njesia) and an example row, and the sheet "Udhëzime".
   - Fill in: a real user, an unknown e-mail, the same e-mail twice, a row without position.
     Upload → the counts and each row's status, the reason on its cell; nothing changed yet (open the user's profile).
   - "Apliko" → the toast shows the count; the profile now shows the position; *Përdoruesit* has a "Pozicioni" column.
   - The import is in the list ("Aplikuar më … nga …"); applying again is refused.
   - A .pdf or a sheet without the headers → the error message.

## 053l — Imports from Excel / CSV (Organika, users)
**Setup:** run the migrator (script 032) and restart the API.

1. **The instructions** (Organika and *Përdoruesit → Ngarko përdorues nga Excel*): the card "Si duhet të jetë skedari"
   shows the purpose, the formats (.xlsx first sheet, CSV UTF-8 with ; or ,), every column with "E detyrueshme",
   its content and an example, and the rules. "Shkarko shabllonin": the second sheet "Udhëzime" has the SAME text.
2. **Users, valid file** (from the template): a new office user (zyra_kodi TR, rolet "STATE_LAWYER;Avokat sekondar"),
   a new institution user (institucioni = the ministry's name or NIPT, rolet "Juristi i institucionit"), an existing
   e-mail without perditeso → "Ekziston", an existing one with perditeso = PO and a new pozicioni → "Ndryshon".
   - The preview writes nothing (*Përdoruesit* unchanged).
   - "Apliko" → the users exist with their roles. The new users sign in through B2C with that e-mail (linked at the
     first login).
   - "Shkarko raportin" → every row with its status and message.
3. **Users, errors** — one row each, the rest import:
   - no e-mail, a bad e-mail, the same e-mail twice;
   - no emri;
   - zyra_kodi AND institucioni, or neither;
   - an unknown office / institution / role;
   - INSTITUTION_USER for an office user;
   - aktiv "ndoshta";
   - the Kryetar with aktiv JO + perditeso PO;
   - avokat1 without STATE_LAWYER while primary on open cases.

   Each one shows the reason on its cell.
4. **Headers** "EMAIL", "Përditëso", "Zyra kodi" are read; a CSV with "," and one with ";" give the same preview.
5. **Re-import** the same file with perditeso = PO everywhere: every row "Pa ndryshim", nothing to apply.
6. A file over 5 MB or 5,000 rows → the error before any preview.

## 053f — Earlier levels read-only (D2)
**Setup:**
- A dossier with X/1 closed and X/2 registered from it.
- X/2 assigned to a lawyer who has NO role on X/1 (e.g. a new lawyer created in Administrimi).
- Restart the API, `npm run dev`.

**Steps:**
1. **The X/2 lawyer on X/2:**
   - The level switcher shows X/1 as a normal link with a lock and the tooltip "… Vetëm lexim".
   - *Historiku* shows X/1's rows labelled X/1.
2. **Open X/1:**
   - The banner reads "Shkallë e mëparshme — vetëm për lexim".
   - Every tab shows its data (parties, hearings, documents, decision, team names).
   - No action anywhere: no "Ndrysho", upload, decision or transfer buttons; *Chat* says it is not allowed.
   - Download a document of X/1 → it opens; the audit log has DOCUMENT_DOWNLOAD for it.
3. **The header search:** search X/1's code → found; it opens read-only.
4. **The agenda:** X/1's hearings are not in the agenda (only in X/1's history).
5. **The X/1-only lawyer (avokat1):**
   - X/2 in the switcher is disabled ("Nuk keni akses…"); opening its link → not found.
   - X/1's documents list has no X/2 documents.
6. **The X/1 lawyer who is also on X/2:** X/1 shows no banner and keeps "Plotëso / Ndrysho vendimin".

## 053e — "Ndihmësit ligjorë" (C, D4)
**Setup:** run the migrator (scripts 023–024; it prints what 024 converted: LINK_CREATED / ROW_KEPT / ROW_ENDED),
restart the API, `npm run dev`.
1. **avokat1:** a case's "Ekipi" has no "Shto ndihmës"; assistants show read-only with the note.
2. **Kryetar → *Ndihmësit ligjorë*:**
   - Every assistant of the office is listed with their lawyers.
   - "Lidh me avokat" → Ndihmës Dev → Avokat1 Dev: the toast says how many cases the link brings.
   - Ndihmës Dev's bell shows "U lidhët me një avokat…" once. Their "Çështjet e mia" now holds every avokat1 primary
     case, and each case's history shows "Ndihmësi ligjor u shtua".
3. **New case:** the Kryetar assigns avokat1 as primary of a new case → Ndihmës Dev sees it at once.
4. **Transfer:** transfer one of avokat1's cases to Avokat2 Dev → it leaves Ndihmës Dev.
   - If another assistant is linked to Avokat2 Dev, it appears there.
   - The chat of the case follows.
5. **Closing:** close one of the cases → Ndihmës Dev still has it (assignments are kept).
6. **Unlink:** remove the chip (×) → confirm → Ndihmës Dev loses every avokat1 case and is told once.
7. **Drift** (SQL): end one link row by hand → run the job (`POST /api/admin/jobs/assistant-reconcile/run` as admin,
   Development) → the row is back; running it again changes nothing.

## 053d — "Mbikëqyrja" (C, D3)
**Setup:**
- Run the migrator (script 022), restart the API, `npm run dev`.
- As admin, create a second office "Zyra Test" with a STATE_LAWYER and one case of it where that lawyer is primary.
  The local seed has one office only.

**Steps:**
1. **Monitor** (*Mbikëqyrja* in the menu; the Kryetar and lawyers have no such menu):
   - "Shto mbikëqyrje" → Kryetar Dev, "Një avokat", the Zyra Test lawyer → the row shows the number of their open
     primary cases.
   - Add a single case of Zyra Test by its code.
   - A lawyer or case of the Kryetar's own office, an unknown code, or the same supervision twice → the error on the
     field / 409.
2. **Kryetar:**
   - The bell shows "Ju u ngarkua një mbikëqyrje" (twice).
   - *Çështjet* has a tab "Në mbikëqyrje (n)" with exactly those cases.
   - The case page opens with "Transfero", priority and "Regjistro vendimin". The transfer picker lists Zyra Test's
     lawyers + "(Unë)".
   - The single case's history shows "Në mbikëqyrje të Kryetarit".
3. **Same cases everywhere:**
   - *Agjenda* (their hearings, also with the supervised lawyer as filter).
   - *Dashboard* ("Në mbikëqyrje").
   - The header search (by code).
   - *Raporte* (Tabela 1) and *Statistikat* (Tabela 5, drill-down "Aktive").
4. **Follows the primary:** give the supervised lawyer a new case → it appears at once. Transfer one of their cases to
   another lawyer → it disappears.
5. **Monitor "Përfundo":**
   - The Kryetar is told, and the cases leave every surface (the case page → 404).
   - "Shfaq edhe të përfunduarat" lists the ended row.

## 053c — "Përcjellja e çështjes" — REMOVED by task 053j
Only "Gjykatat që mbulon" stays (used by 053j below). The handoff step, the case card, "Çështjet e ardhura" and the
handoff e-mail are gone; the e-mail outbox stays without a producer.

## 053j — The Protokoll is central; the office of the court
**Setup:** run the migrator (script 030) and restart the API, then `npm run dev`. Closed cases of at least two offices
(close some through *Vendimi*, one of them with "Formë e prerë").

1. **Closing** (avokat1, *Vendimi → Regjistro vendimin*), "Formë e prerë" unchecked: four steps only — no
   "Përcjellja". The case list has no "Çështjet e ardhura" tab.
2. **"Vazhdo çështje ekzistuese"** (Protokoll):
   - A table of every office's dossiers, 10 rows, "‹ Para / Pas ›" with "1–10 nga N"; the code column stays when
     scrolling sideways.
   - Default "Statusi: Mund të vazhdohen": only closed, not final, not archived levels. "Të gjitha" adds the open ones
     (reason "E pambyllur në sistem" and "Njofto…" instead of "Vazhdo"), the final ones and the archived ones
     ("Dosja është arkivuar").
   - The filters: Kodi unik, Nr. i aktit (a part of them is enough), Shkalla → Gjykata → Gjyqtari (the court list
     follows the level), Paditësi, I padituri, Objekti (without diacritics too: "shperblim"), Kategoria → Lloji,
     Rezultati.
   - "+ Shto filtër": Zyra, Avokati, Data e regjistrimit, Data e vendimit, Formë e prerë, Ankimuar, … (the report
     builder's filters); each one can be removed with ×. The URL keeps every choice; "Pastro filtrat" resets.
   - Admin → audit: one REGISTRATION_LOOKUP row per search, with the filters.
   - Opening `/cases/<id>` of another office's case as the Protokoll: "Çështja nuk u gjet".
3. **Continue** ("Vazhdo" on a row): the preview lists the object, the plaintiff(s) and the defendant(s) only. A third
   party is not copied; "Palë të tjera (nëse ka)" adds one. Category, delegation and lawyers stay empty on X/n+1.
4. **Office of the court** (both *Regjistro çështje të re* and the continuation):
   - Admin → *Zyrat → Gjykatat që mbulon*: give "Gjykata e Apelit Tiranë" to an office B with a Kryetar.
   - Choose that court: "Zyra që e ndjek" is filled with B ("Zyra që mbulon këtë gjykatë."). Register: the case is in
     B; B's Kryetar gets the notification and sees it under "Pa avokat"; the Protokoll's own Kryetar does not.
   - Choose another office: "Keni zgjedhur një zyrë tjetër …"; after registering, the audit shows CASE_OFFICE_OVERRIDE.
   - A court no office covers: the field stays empty with "Kjo gjykatë nuk i është caktuar asnjë zyre …" and is required.
   - An office without a Kryetar: "Zyra nuk ka Kryetar. Kontaktoni administratorin."
5. **"Çështjet e mia"** (Protokoll): the cases it registered for office B are listed while they wait for B's Kryetar.
6. **Admin** (*Administrimi → Listat → Lloji i palës*): "Kopjohet në shkallën e radhës" is Po for Paditës and I paditur,
   Jo for Person i tretë; changing it changes what the next continuation copies.

## 053k — Roles (the matrix: docs/permissions-matrix.md)
**Setup:** run the migrator (script 031, LocalScripts 908) and restart the API.

1. **Admin** → *Përdoruesit* → *Rolet* of an office user: the dropdown lists Specialist / Protokoll, Kryetari i Zyrës,
   Avokat Shteti (primar), Avokat sekondar, Ndihmës Ligjor, Monitoruesi, Administratori (those not held yet). An
   institution user: Institucioni, Juristi i institucionit, Institucioni me delegim.
2. **Monitoring case** (the ministry monitors, jurist assigned):
   - institucion: views, adds a hearing, uploads, chats, *Cakto juristin*;
   - jurist: the same without *Cakto juristin*, and no decision;
   - delegim: views only (no buttons, not in the chat).
3. **Delegate it** (avokat1): delegim records the decision, changes the judge, edits parties and data, and chats;
   the jurist gets 404 (history "Juristi i institucionit u hoq"); institucion views only. delegim gets the "delegated"
   notification.
4. **Primar vs sekondar** on the same case: avokat2 (secondary) has no *Fshi* on documents, no *Ndrysho gjyqtarin*,
   no category / priority / status / *Delego*; both record the decision. sekondar@ash.local → *Statistikat* 403.
5. **Protokoll**: in the chat of the cases it registered; its corrections only while REGISTERED.

## 052 — Deployment (images, configuration, health)

Build the images from the repository root (`deploy/docker/*.Dockerfile`). The full smoke checklist for a real
environment is in `deploy/README.md`.

1. **Footer:** in `npm run dev`, the bottom of every page reads "Versioni dev · Mjedisi: Development".
   - In a Production image, only "Versioni 1.2.3" shows.
2. **CSP guard:** start `ash-frontend` with `ASH_IDP_HOSTS=other.b2clogin.com` while the API's B2C authority is on
   another host. The login page shows "Konfigurimi i hyrjes nuk përputhet me CSP" and names the missing host.
   - With the right host, the warning is gone.
3. **Missing configuration:** start `ash-api` with `ASPNETCORE_ENVIRONMENT=Production` and no settings.
   - It exits.
   - The log lists every missing setting in one message (connections, storage, ClamAV, e-mail, identity).
4. **Dev login refused:** run `ash-api` in `Uat` with `DevFeatures__HeaderAuth=true`. It refuses to start.
5. **Health:**
   - `/health/live` returns 200 even with MySQL stopped.
   - `/health/ready` returns 503 with MySQL stopped and 200 again once it is back.
   - `/api/version` without login returns 401; after login it returns the version and environment.
6. **MySQL settings:** run the API against a MySQL with `innodb_ft_enable_stopword=ON`.
   - The API still starts.
   - The log warns "MySQL server setting: innodb_ft_enable_stopword must be OFF".
   - `/health/ready` returns 503.
7. **Real client IP:**
   - behind the proxy listed in `ReverseProxy__KnownNetworks__0`, the audit log shows the browser's IP;
   - remove the setting and it shows the proxy's IP.
8. **Containers:**
   - `docker exec <api> id -u` is not 0;
   - writing to `/app` fails (read-only);
   - `date` follows Europe/Tirane in the app (case deadlines);
   - Albanian month names appear in the PDF export.

## 053m — Filters in every table (the shared FilterBar)
Supersedes the *Filtra* panel steps of 031, 045 and the case list above: no table has a *Filtra* button any more.
1. **Every table** (cases, *Vazhdo çështje ekzistuese*, reports, users, lists, offices, institutions, judges, file types, code format, documents, notifications, Organika / users import history, *Raportet e mia*, *Mbikëqyrja*): 2–3 filters on top, *+ Shto filtër* lists every other column; an added filter has ×.
2. **Kinds:** text, select, dependent select, date range (nga / deri), number range, Po / Jo — each with its own control.
3. **Dependent:** *Çështje → Shto filtër → Shkalla e gjykimit*: *Gjykata* offers only that level's courts; a new level clears the court; *Gjyqtari* follows the court; *Zyra → Avokati* the same (monitor).
4. **Scope:** a lawyer has no *Zyra* / *Avokati*; the Kryetar has *Avokati* only; the monitor both. Options list only what exists in the user's cases.
5. **URL:** add *Nr. i aktit*, type a number, open a row, Back → the filter and its value are still there. Refresh keeps them.
6. **Menu clears:** go to *Agjenda* through the menu, then *Çështje* → no added filter, no value.
7. **Unused filter:** add a filter, leave it empty, click a menu item → "Keni krijuar një filtër që nuk e keni përdorur. Dëshironi të dilni nga faqja?". *Anulo* stays, *Po* leaves. With a value: no dialog. Report builder: also after changing filters without *Gjenero*.
8. **Server lists** (users: *Pozicioni*, *Hyrja e fundit*; notifications: *Lloji*, title, date; documents: title, upload date) return only matching rows, with the right total.
9. 390 px: the bar wraps, no page-wide side scroll.

## 053n — Agenda print; the administrator's dashboard
**Agenda print**
1. *Agjenda → Printo* opens a dialog: *Nga data* / *Deri më* (the calendar's current period to start), *Sot*, *Kjo javë*, *Java tjetër*, *Ky muaj*, the office / lawyer for who has those filters (Monitor; Kryetar: the lawyer), *Përfshi të anuluarat*.
2. A period over 3 months (e.g. 01.11 – 01.02) → "Periudha mund të jetë deri në 3 muaj."; *Printo* disabled. To before from → its message.
3. In the month view choose *Kjo javë* + *Printo* → the print shows the WHOLE week's hearings (not the month on screen), grouped by day: date, time, court, case code, type, lawyer.
4. The header: "Agjenda e seancave", the period, the filters (or "Të gjitha seancat e periudhës"), "Printuar më … nga …", the number of hearings. Every page: "Faqja n nga m" bottom right. A 3-month period runs over several pages; a day's table header repeats on a new page.
5. *Përfshi të anuluarat* on: cancelled hearings appear struck through with "(e anuluar)".
6. More than 2,000 hearings → "Zgjidhni një periudhë më të shkurtër." in the dialog.

**The administrator's dashboard** (admin@ash.local; works the same with no office set on the admin)
1. No *Çështjet sipas zyrës*, *Punët në sfond*, *Particionet*, and no case cards. (The Monitor still has the case cards and the chart.) Job and partition health: `/health/ready` and the logs.
2. Cards: *Përdorues aktivë / joaktivë*, *Pa asnjë rol*, *Nuk kanë hyrë asnjëherë*, *Krijuar këtë muaj*, *Me llogari hyrjeje të bllokuar*, *Pa llogari hyrjeje*, *Zyra pa Kryetar*, *Gjykata pa zyrë*, *Dokumente të pakontrolluara*.
3. Each card opens its admin page with the filter applied, and the list's total equals the card: *Përdoruesit* (filters *Aktiv*, *Pa asnjë rol*, *Nuk ka hyrë asnjëherë*, *Krijuar më*, *Llogaria e hyrjes*), *Zyrat* (*Aktive* + *Ka Kryetar: Jo*), *Listat → Gjykatat* (*Aktive* + *Mbulohet nga një zyrë: Jo*).
4. *Ndryshimet e fundit administrative*: the last 10 — what (in Albanian), who, when (Tirane). Deactivate a user → its row links to *Përdoruesit* showing only that user ("Vetëm: …", × shows all), inactive. An office / institution likewise. A court, judge, list item, import … : plain text, no link.
5. *Importet me gabime*: upload a users CSV with a bad e-mail → the row (file, *Përdoruesit*, errors, when) opens *Përdoruesit* with the import dialog and *Me gabime: Po*.
6. Database: the API account reads `ash_audit.v_admin_changes` only; `SELECT … FROM ash_audit.audit_log` as `ash_app` is denied.

## 053o — Raporte & Statistika (the client's tables)
Supersedes the "Gjenero" steps of 045 / 053h. The client's files: `docs/client/Tabelat e raportimit.xlsx`, `docs/client/Tabelat e statistikave.xlsx`.
1. **Menu and tabs:** the menu says *Raporte & Statistika*. Below the header two tabs: *Raportet* (sub-tabs *Tabela 1 — Aktive*, *Tabela 2 — Të mbyllura*, *Raportet e mia*) and *Statistikat* (*Tabela 3 — Çështje aktive*, *Tabela 4 — Institucionale*); the sub-tab is in the URL (`?tab=aktive|mbyllura|mine|statistika|institucionale`; old `?tab=tabela1/3/5/6` links still open the same table).
2. **No table before a filter:** on entering a sub-tab every filter shows *Të gjitha* and the page says *Zgjidhni të paktën një filtër për të shfaqur të dhënat*; no *Gjenero* button anywhere.
3. **Real time:** change any filter → the table appears; every further change updates it ≈ 0.4 s after the last keystroke; the previous rows stay visible with a thin bar on top; typing fast in *Kodi unik* does not send a request per key (DevTools → Network). A failure shows inline above the table.
4. **Helper texts:** no "Zgjidhni fillimisht: …" / "Zgjidhni më parë: …" under any field in the system; a child filter still offers only its parent's options.
5. **Date ranges:** every date range (reports, case list, users, documents, notifications, agenda print …) is one box with its label on the border (*Data e mbylljes*) and *Nga* / *Deri* inside.
6. **Columns (Tabela 1 / 2):** the default view is exactly the client's columns in the client's order (Kodi unik … *Vlera e padisë* with *M2* | *Monedha* under one merged header … *Kategoria dhe lloji i çështjes*; Tabela 2 with *Data e regjistrimit*, *Vlera* M2 | Monedha, *Ankimuar/ Rekursuar*). Our other columns are in *Kolonat*, off by default. Money shows "8.000,00 ALL"; the totals row has one sum per currency.
7. **Statistikat:** the client's table — the title, the grouped header rows (groups centered over their columns), the letter row (A, B, C …) and the formula row (A+B, C-(D+E)), a row per court and *TOTALI* in bold; the result columns *Fituar* … *J.juridik* (+ *Tjetër*), each *Avokat* | *Delegim/Relatim*; *Ankimuar* and *Rekurs* apart; one *MONEDHË* column per currency. An ⓘ beside a header shows its definition (no dotted underline). A number opens its cases.
8. **Pagination (Tabela 1):** *Rreshta për faqe* 10 / 25 / 50 changes the rows and goes back to page 1; *Para* / *Pas* move; "1–10 nga N" follows.
9. **Print (every print: reports, statistics, agenda):** *Printo* prints only the table's title and the table — no filters line, no "Gjeneruar / Printuar më … nga …", no page numbers, no menu, header, filters, buttons or pagination (and never "Tabela 4 kërkon një institucion…"). Tabela 1 / 2: EVERY row of the filters (not only the page on screen; the button says *Po ngarkohen rreshtat…* meanwhile) and the totals row; above 2.000 rows a message asks to narrow the filters or export to Excel. Landscape for a wide table, the header rows on every page. Statistics: a dialog first — the table, and *Rezultati* / *Në dorë* only when ticked. Agenda: one title line "Agjenda e seancave · dd.mm.yyyy – dd.mm.yyyy", then the hearings by day. (The browser's own date / URL margins: untick "Headers and footers" in its print dialog.)
10. **Tables everywhere:** a navy header band with white bold text, vertical and horizontal cell lines, zebra rows, sticky header; the same on paper.
11. **Excel (every export):** header rows bold white on navy with borders, grouped headers merged and centered, thin borders on all cells, frozen header, autofilter, thousands separators, money / m² with 2 decimals (money with its code), totals bold, column widths, the *Filtrat* sheet. The statistics workbook: *AVOKATURA E SHTETIT* / *STATISTIKA* / the title, the same merged header as the screen. CSV stays plain. Import templates and result reports use the same template.
12. **Provisional rules** (data, not code — script 034; `docs/statistics-definitions.md` "Rregulla në pritje"): change `report_settings.statistics.appeal_columns` to COMBINED → one column *Ankimuar / Rekursuar*; `reports.closed_value` to DECISION → *Vlera* shows the decision's won value; a row of `statistics_result_mapping` moves a result to another column. No restart is needed (read per request).

## 053p — "Eprori" removed
**Setup:** run the migrator (script 035) and restart the API.

1. **Profili → Pozicioni:** only *Pozicioni* and *Njësia*; the note says "Pozicioni dhe njësia vendosen nga administratori përmes Organikës."; the word "Eprori" is nowhere on the page.
2. **Administrimi → Organika:** "Shkarko shabllonin" → the sheet *Organika* has three columns (email, pozicioni, njesia); *Udhëzime* and the "Si duhet të jetë skedari" card say nothing about an eprori. Upload a sheet → the preview, the result report and "Apliko" show / write only position and unit.
3. **Database:** `SHOW COLUMNS FROM ash_core.users LIKE 'manager%'` and the same on `user_org_history` return nothing.
