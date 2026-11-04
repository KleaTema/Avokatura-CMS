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
10. A case whose ministry link is DELEGATED: the jurist (`jurist@ash.local`) sees `decision.record` in `permissions`
    and may close it.

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
