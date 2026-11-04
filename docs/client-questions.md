# Client questions (Pyetjet për klientin)

One row per question to the client (Avokatura e Shtetit / AKSHI). Until a question is answered the code uses the
**default** and carries the TODO tag shown; when the answer arrives, change the code at that tag, update the row
(status, answer) and remove the TODO.

Keep this file up to date in every task (see `.github/skills/ash-task-done-check/SKILL.md`).

**Task 029** (review of the assignment and case-data screens) raised no new client question.
**Task 030** (hearings) raised no new client question: the rules it follows (an overdue hearing stays the next one
until completed, a warning within ±30 minutes, a description required for HELD and POSTPONED) were set in the task.
**Task 031** (agenda) implements Q4 with its default (only hearings) and adds *agenda-ics* (calendar subscription).
**Task 032** (file storage) moves the storage question into the new "AKSHI questions — storage" section (split into
its parts) and adds the retention period (Q9).
**Task 033** (document upload) adds "AKSHI questions — uploads": the proxy limit, the antivirus, retention on copy.
**Task 034** (document timeline, download, delete) adds *docs-across-levels*, *deleted-docs* and *uploader-delete*.
**Tasks 036/037** (decision, closing, completing later) add *closed-uploads* and *close-hearings*.
**Task 038** (archive the dossier) adds *unarchive*.

**Numbering note.** The task files use "Client question N" inconsistently: *Q1* stands both for the identity-provider
claims (005) and for the registration search fields (018), and *Q6* both for the 30-day reminder after a transfer (024)
and for report access (042/044). The ids below are unique; the old number is kept in brackets for reference.

## Answered

| Id | Question (sq) | Question (en) | Answer | Where in code | Task |
|---|---|---|---|---|---|
| Q1a | Kur vjen fletëthirrja për shkallën e radhës, a mund ta gjejë Protokolli dosjen në çdo zyrë, apo vetëm në zyrën e vet? | When a next-level summons arrives, may Protokoll find the dossier in every office or only in its own? | **Every office** — an appeal summons may arrive at another office than the first level. | `CaseAccessService.GetRegistrationLookupScope` (comment "confirmed by the client (Q1a)") | 018 |
| Q1b | Nëse vjen fletëthirrja e shkallës së radhës, por shkalla e mëparshme nuk është mbyllur në sistem, kush vepron? | A next-level summons arrives but the previous level is not closed in the system — who acts? | **Protokoll waits for the lawyer** and never edits the previous level; it notifies the primary lawyer and the Kryetar (POST notify-not-closed). | `NotifyNotClosedHandler`, `PreviousLevelNotClosed` (comment "client answer Q1b") | 018 |

## Open — implemented with a default

| Id | Question (sq) | Question (en) | Default implemented | Where in code (TODO tag) | Task |
|---|---|---|---|---|---|
| auth-claims (Q1) | Cilin ofrues identiteti dhe cilat atribute (email, NID…) përdor AKSHI për punonjësit e shtetit? | Which identity provider and claims (email, NID…) does AKSHI use for government employees? | The `sub` claim identifies the user; the first login links the account by (lower-case) email. | `TODO(client-q1)` in `Host/Ash.Api/Auth/CurrentUserMiddleware.cs` | 005 |
| user-provisioning | A e krijon administratori përdoruesin para hyrjes së parë, apo krijohet automatikisht në hyrjen e parë? | Does the admin create a user before the first login, or is it created automatically at the first login? | The admin creates the user first; `external_subject` stays empty until the first login links it by email. | `TODO(client-q)` in `Identity/Application/Users/CreateUserHandler.cs` | 013 |
| institution-hierarchy | Sa nivele ka hierarkia e institucioneve (p.sh. ministri → agjenci → drejtori)? | How deep is the institution hierarchy (e.g. ministry → agency → directorate)? | One level: only a MINISTRY can be a parent, and a ministry has no parent ("per ministry" statistics roll up one level). | `TODO(client-q-institution-hierarchy)` in `Identity/Application/Institutions/InstitutionAdminSupport.cs` | 015 |
| max-file-size | Sa është madhësia maksimale e një dokumenti që ngarkohet? | What is the maximum size of an uploaded document? | 50 MB per file type (seed placeholder); the admin may set up to 100 MB. | `TODO(client-max-file-size)` in `Reference/Application/Admin/FileTypeAdminSupport.cs`; seed `007_seed_reference_data.sql` | 014 |
| office-without-head | Çfarë ndodh me regjistrimin kur zyra nuk ka Kryetar aktiv? | What happens at registration when the office has no active head? | Registration is refused with a clear message until the admin sets a head. | `TODO(client-q-office-without-head)` in `RegisterCase/CaseRegistrationSupport.cs` and `Identity/Application/Offices/SetOfficeHeadHandler.cs` | 015, 016 |
| min-parties | A duhet të ketë çështja të paktën një palë në regjistrim? | Must a case have at least one party at registration? | Yes, at least one party. | `TODO(client-q-min-parties)` in `RegisterCase/RegisterCaseValidator.cs` | 016 |
| search-fields (Q1) | Me cilat fusha e kërkon Protokolli dosjen ekzistuese kur vjen fletëthirrja e re? | Which fields does Protokoll search by to find the existing dossier for a new summons? | Unique code, case code, act number, party name, NIPT; optional court filter. | `TODO(client-q1)` in `RegistrationSearch/RegistrationSearchHandler.cs` | 018 |
| cross-office-lawyers | A mund të caktojë Kryetari avokatë të zyrave të tjera? | May the Kryetar assign lawyers of other offices? | No — only the active STATE_LAWYERs of the case's office (and the Kryetar himself). | `TODO(client-q-cross-office)` in `AssignLawyers/OfficeLawyers.cs` | 023 |
| reminder-after-transfer (Q6) | Kur transferohet avokati primar, a rifillon afati 30-ditor i kujtesës për avokatin e ri? | When the primary lawyer is transferred, does the 30-day reminder restart for the new lawyer? | Yes: `assigned_at` = transfer time; `first_data_entry_at` is not touched. | `TODO(client-q6)` in `TransferLawyer/TransferLawyerHandler.cs` | 024 |
| assistants-on-transfer | Kur transferohet avokati primar, a mbeten ndihmësit ligjorë në çështje? | When the primary lawyer is transferred, do the legal assistants stay on the case? | They stay; the new primary may remove them (task 026). | `TODO(client-q-assistants-on-transfer)` in `TransferLawyer/TransferLawyerHandler.cs` | 024 |
| assistants-count (Q5) | Një ndihmës ligjor për çështje apo disa? | One legal assistant per case or several? | Several. | `TODO(client-q5)` in `Assistants/AssistantHandlers.cs`, `Assistants/AssistantRequests.cs` | 026 |
| relatim-rights | A i jep RELATIMI institucionit të njëjtat të drejta si DELEGIMI? | Does RELATIM give the institution the same rights as DELEGIM? | Yes, the same rights (INSTITUTION_DELEGATED profile). | `TODO(client-q-relatim-rights)` in `DelegateCase/DelegateCaseHandlers.cs` | 027 |
| agenda-content (Q4) | Çfarë tjetër duhet të ketë agjenda (afate, kujtesa, ngjarje personale)? | What else belongs in the agenda (deadlines, reminders, personal events)? | Only hearings. | `TODO(client-q4)` in `Agenda/GetAgendaHandler.cs` | 031 |
| agenda-ics | A duhet që agjenda të shihet edhe në Outlook ose në telefon (abonim kalendari)? Nëse po, si mbrohet lidhja (token personal, skadim)? | Should the agenda also show in Outlook or on the phone (calendar subscription)? If so, how is the link protected (personal token, expiry)? | Not built; the agenda is only in the application. | `TODO(client-q-ics)` in `Agenda/GetAgendaHandler.cs` | 031 |
| docs-across-levels | A i sheh avokati i apelit dokumentet e shkallës së parë (dhe anasjelltas)? | Does the appeal lawyer see the first-instance documents (and the other way round)? | Yes: whoever can view ANY level of the dossier sees the documents of ALL its levels — one dossier timeline (unlike the history, which skips levels the user cannot view). | `TODO(client-q-docs-across-levels)` in `Cases.Contracts/ICaseAccessService.cs` (CanViewDossierDocumentsAsync) | 034 |
| deleted-docs | A duhet që Monitoruesi / Administratori të shohin ose shkarkojnë dokumentet e hequra? | Should the Monitor / Admin see or download removed documents? | No: a removed document is 404 for everyone; the row and the file stay (history shows the removal). | `TODO(client-q-deleted-docs)` in `Documents/Application/Download/DownloadDocumentHandler.cs` | 034 |
| uploader-delete | A mund ta heqë ngarkuesi dokumentin e vet menjëherë pas ngarkimit (p.sh. brenda 15 minutave), edhe pa qenë avokat primar? | May the uploader remove their own upload shortly after (e.g. within 15 minutes) without being the primary lawyer? | No: only document.delete (the primary lawyer). | `TODO(client-q-uploader-delete)` in `Documents/Application/Delete/DeleteDocumentHandler.cs` | 034 |
| closed-uploads | Pas mbylljes së çështjes, kush mund të ngarkojë vendimin e arsyetuar (Vendim)? Tani: kushdo që kishte të drejtë ngarkimi (edhe avokati dytësor, ndihmësi, institucioni monitorues); dokumente të tjera jo. | After closing, who may upload the reasoned decision (Vendim)? Now: whoever could upload (also the secondary lawyer, the assistant, the monitoring institution); no other documents. | `document.upload` stays on a CLOSED case for type VENDIM only (409 "Në një çështje të mbyllur ngarkohet vetëm vendimi." otherwise); linking it to the decision needs `decision.record`. | `TODO(client-q-closed-uploads)` in `Documents/Application/Upload/UploadDocumentHandler.cs` | 036 |
| close-hearings | Kur mbyllet çështja me seanca ende të planifikuara, a anulohen ato automatikisht? | When a case is closed while hearings are still scheduled, are they cancelled automatically? | Not silently: the API answers 409 with the list; the user confirms (`cancelOpenHearings`) and they become CANCELLED with "Anuluar në mbyllje të çështjes" (a history row each). | `TODO(client-q-close-hearings)` in `Cases/Application/RecordDecision/RecordDecisionHandler.cs` | 036 |
| unarchive | A mund të zhbëhet arkivimi i një dosjeje (p.sh. arkivuar gabimisht), dhe nga kush? | Can archiving a dossier be undone (e.g. archived by mistake), and by whom? | Not possible now: an archived dossier is read-only on every level (view and document download only). Later, if confirmed: admin only, with a reason and an audit row. | `TODO(client-q-unarchive)` in `Cases/Infrastructure/Access/CaseAccessService.cs` and `Cases/Application/ArchiveDossier/ArchiveDossierHandler.cs` | 038 |
| retention (Q9) | Sa kohë duhet të ruhen dokumentet e çështjeve (afati ligjor i ruajtjes)? | How long must case documents be kept (legal retention period)? | The application never deletes documents (only quarantined uploads); any retention period is left to the bucket's object-lock configuration. | `TODO(client-q9)` in `Ash.Infrastructure/Storage/IFileStorage.cs` | 032 |

## AKSHI questions — storage (task 032)

Questions for AKSHI's infrastructure team. Until answered, the code uses the default and carries `TODO(akshi-q-storage)`
(in `Ash.Infrastructure/Storage/IFileStorage.cs` and `S3FileStorage.cs`). Locally, MinIO (docker-compose) stands in.

| Id | Question (sq) | Question (en) | Default implemented |
|---|---|---|---|
| storage-endpoint | Cila është adresa (endpoint) e ruajtjes S3, rajoni, dhe a kërkon stilin e adresës me rrugë (path-style)? Si jepen çelësat e qasjes (vault, variabla mjedisi)? | What is the S3 endpoint, the region, and does it need path-style addressing? How are the access keys delivered (vault, environment variables)? | `Storage:ServiceUrl` from configuration, region us-east-1, path-style on; keys from user-secrets / environment variables, never in appsettings or logs. |
| storage-buckets | A i krijon AKSHI kovat (buckets) `ash-uploads-quarantine`, `ash-documents`, `ash-chat-files`, apo emrat i jep AKSHI? | Does AKSHI create the buckets `ash-uploads-quarantine`, `ash-documents`, `ash-chat-files`, or does AKSHI name them? | Names from `Storage:Buckets` (any names work); the app never creates buckets; the "storage" health check reports a missing one. |
| storage-object-lock | A mbështetet object lock (governance/compliance) dhe me çfarë afati të paracaktuar ruajtjeje? Nëse ka afat të paracaktuar, a kërkon Content-MD5 në ngarkim? | Is object lock supported (governance/compliance), with which default retention? With a default retention, is Content-MD5 required on upload? | Object lock on the documents bucket as configured by the bucket; the app sets no retention per object. Checksums are only sent where the S3 API requires them (some S3-compatible stores reject the SDK's newer default checksums). |
| storage-versioning | A mbështetet versionimi i objekteve? | Is object versioning supported? | Left to the bucket configuration (enabled for chat files locally). |
| storage-encryption | A enkriptohen objektet në ruajtje (SSE-S3, SSE-KMS)? Kush i menaxhon çelësat? | Are objects encrypted at rest (SSE-S3, SSE-KMS)? Who manages the keys? | Left to the bucket's default encryption; the app sends no SSE headers. |
| storage-presigned | A mbështeten URL-të e nënshkruara (presigned) dhe cila adresë publike arrihet nga shfletuesi? | Are presigned URLs supported, and which public endpoint can the browser reach? | Implemented (at most 2 minutes, signed against `Storage:PublicServiceUrl` when set) but NOT used: downloads go through the API (access check and audit at download time, links cannot be forwarded). |
| storage-lifecycle | A mbështeten rregullat e ciklit të jetës (fshirja automatike pas 1 dite në kovën e karantinës)? | Are lifecycle rules supported (automatic expiry after 1 day in the quarantine bucket)? | Expected on the quarantine bucket (docker-compose sets it locally); the app may delete from the quarantine bucket only. An upload whose database part fails, and a rejected (INFECTED) file, are left there to expire. |

## AKSHI questions — uploads (task 033)

| Id | Question (sq) | Question (en) | Default implemented | Where in code (TODO tag) |
|---|---|---|---|---|
| proxy-limit | A lejon proxy-ja (reverse proxy / load balancer) para API-së kërkesa deri në madhësinë më të madhe të lejuar të skedarit + 1 MB (aktualisht 51 MB), dhe pa i ruajtur në disk/memorie para se t'i kalojë? | Does the reverse proxy / load balancer in front of the API allow request bodies up to the largest allowed file size + 1 MB (51 MB today), streamed through without buffering them? | The API sets its own per-request limit to the largest active DOCUMENT/BOTH `max_size_bytes` + 1 MB (Kestrel's default is 30 MB) and streams the file. | `TODO(akshi-q-proxy)` in `Documents/Application/Upload/UploadDocumentHandler.cs` |
| antivirus | Cilin antivirus ofron AKSHI për skanimin e skedarëve (ClamAV clamd, ICAP, tjetër), në cilën adresë, dhe cila është madhësia maksimale që skanon? | Which antivirus does AKSHI provide for scanning files (ClamAV clamd, ICAP, other), at which address, and what is the largest file it scans? | ClamAV clamd over TCP (INSTREAM), `Scanner:ClamAv:Host/Port`; scanner down → the document stays PENDING, after 5 failed attempts FAILED. Locally: `docker compose --profile av up -d clamav`, or NoOp (Development only). | `TODO(akshi-q-av)` in `Ash.Infrastructure/Scanning/IVirusScanner.cs` and `Documents/Infrastructure/Jobs/ScanQueueJob.cs` |
| retention-on-copy | Kur dokumenti i pastër kopjohet në kovën `ash-documents`, a vendos AKSHI afatin e ruajtjes (object lock) në nivel kove, apo duhet ta vendosë aplikacioni për çdo objekt? | When a clean document is copied to `ash-documents`, does AKSHI set the retention (object lock) at bucket level, or must the application set it per object? | The bucket's default retention applies; the app sets none per object (see *storage-object-lock* and the retention period Q9). | `TODO(akshi-q-storage, client-q9)` in `Documents/Infrastructure/Jobs/ScanQueueJob.cs` |

## AKSHI questions — database server (fixes after browser testing, script 014)

| Id | Question (sq) | Question (en) | Default implemented | Where in code (TODO tag) |
|---|---|---|---|---|
| mysql-search-setting | Kërkimi në pjesë fjalësh kërkon këtë konfigurim të serverit MySQL (primar dhe replikë): `innodb_ft_enable_stopword = OFF` dhe `ngram_token_size = 2` (vlera e paracaktuar). A mund ta vendosë AKSHI në konfigurimin e serverit? | MySQL server setting needed for search: substring search needs `innodb_ft_enable_stopword = OFF` and `ngram_token_size = 2` (the default) on the MySQL server (primary and replica). Can AKSHI set them in the server configuration? | Script 014 turns stopwords off for its own session, so the indexes are built right; `docs/sql/mysql_server.cnf` and the local docker-compose set both values. Without them on the server, a later index rebuild (OPTIMIZE, ALTER … FORCE) drops 2-letter tokens such as "in", "an", and searches like "stri" (Ministria) or "inan" (Financave) stop finding. | `docs/sql/mysql_server.cnf`, `Database/Ash.Database/Scripts/014_ngram_search_indexes.sql` |

## Open — for tasks not built yet

No code yet; the default is decided when the task is implemented.

| Id | Question (sq) | Question (en) | Planned default | Task |
|---|---|---|---|---|
| notifications-email | A duhet që njoftimet të dërgohen edhe me email? | Should notifications also be sent by e-mail? | — | 040 |
| report-access (Q6) | Cilat raporte dhe statistika mund të shohë secili rol? | Which reports and statistics may each role see? | — | 042, 044 |
| dashboard-widgets | Cilat elemente do të shohë secili rol në faqen kryesore? | Which widgets does each role want on the dashboard? | — | 046 |
| profile-fields (Q3) | Cilat fusha të profilit ka secili rol dhe cilat mund të ndryshohen? | Which profile fields per role, and which are editable? | — | 048 |
| chat-scope | Kush mund të bisedojë me kë (i hapur për të gjithë apo i kufizuar)? | Who may chat with whom (open to all or restricted)? | — | 049 |
| ci-hosting | Cilin sistem CI dhe çfarë hostimi (VM, Kubernetes) ofron AKSHI? | Which CI system and hosting (VMs, Kubernetes) does AKSHI provide? | — | 052 |
