# Architecture — module map

Attach this file to a Copilot chat only when a task says so; `.github/copilot-instructions.md` is always loaded.

## Modules and the tables they own

| Module | Owns (schema `ash_core` unless noted) | Exposes in `.Contracts` |
| --- | --- | --- |
| Identity | `users`, `roles`, `permissions`, `role_permissions`, `user_roles`, `case_profile_permissions`, `offices`, `institutions` | `ICurrentUser` data, `IUserDirectory`, `IPermissionService` |
| Reference | `court_levels`, `courts`, `judges`, `party_roles`, `summons_types`, `hearing_types`, `document_types`, `decision_results`, `case_categories`, `case_subcategories`, `allowed_file_types`, `dossier_code_formats` | `IReferenceData` (cached lookups) |
| Cases | `dossiers`, `cases`, `case_assignments`, `case_institutions`, `case_parties`, `case_hearings`, `case_security_measures`, `case_decisions`, `case_history_events`, `case_reminders` | `ICaseAccessService`, `IHistoryWriter`, `ICaseLookup`, integration events |
| Documents | `documents` + object storage buckets | `IDocumentService` (attach decision PDF, list) |
| Notifications | `notifications`, `user_notification_prefs` (task 048: the sender skips an optional type the user turned off) | `INotificationSender`, `INotificationPreferences`, `NotificationTypes.Catalog` |
| Audit | `ash_audit.audit_log`, `ash_audit.audit_daily_digests` | `IAuditLog` |
| (building block `Ash.Infrastructure.Jobs`) | `ash_ops.job_runs`, `ash_core.sent_reminders` | `IScheduledJob`, `JobRunner`, `SentReminders` |
| Reports | none (reads views `v_report_*` and statistics SQL, read-only, replica connection) | — |
| Chat | `ash_chat.*` — own connection `ConnectionStrings:Chat` (account `ash_chat_app`) and own transaction; follows the case team after the core commit (task 049) | — |

## Cross-module communication
- Synchronous: call an interface from the other module's `.Contracts`.
- Events: `IEventPublisher.Publish(new LawyerAssigned(...))` — handlers run in-process, inside the SAME transaction, so a notification is never lost or sent for a rolled-back change.
- Reports is the only module allowed to read other modules' tables, and only through SQL views/queries, read-only.

## Request pipeline
Auth → `CurrentUser` → `IUnitOfWork` opens one MySQL connection + transaction → handler → `SaveChanges` on every touched context → commit → audit row (separate connection, `ash_audit_writer` account) → response.

## Background jobs (task 041)
**Decision: a small in-house scheduler, not Quartz.NET.** Five jobs on two simple schedules do not need Quartz's
tables, its clustering setup or a licence check; the one thing Quartz would add — running a job on one instance only —
is a MySQL named lock here.
- A job implements `IScheduledJob` (`Name`, `Schedule` = `JobSchedule.Hourly` or `JobSchedule.DailyAtTirane(hh, mm)`,
  `RunAsync(DateTime nowUtc, CancellationToken)`) and is registered with `services.AddScheduledJob<T>()` in its module.
  "Now" is a parameter: tests run a job with any time, no clock to fake.
- `JobScheduler` (a `BackgroundService`, off when `Jobs:Enabled` is false — always in the integration tests) wakes every
  30 s, asks `IClock` for the time and runs each due job through `JobRunner`, one after another. On start a job whose
  latest scheduled run is newer than its last success runs at once (catch-up); the audit digest then covers every missed
  day itself.
- `JobRunner`: `GET_LOCK('job:<name>', 0)` on ONE dedicated, unpooled connection held for the whole run (a named lock
  belongs to the session; the unit of work's connection is opened and closed per transaction, and a pooled connection
  could go back to the pool still holding it). Not obtained → another instance runs it → skipped silently.
  `RELEASE_LOCK` in `finally`; the state goes to `ash_ops.job_runs` (last start, success, error).
- Daily times are Tirane wall-clock times: a time skipped when clocks go forward runs at the first valid minute after
  it; a time repeated when they go back runs once, at its first occurrence.
- A job opens its own transactions, one per unit it must keep whole (one case, one reminder). Whatever it sends is
  deduplicated in the database in the same transaction (`case_reminders` uq_cr_once, `sent_reminders.dedupe_key`), so
  a second run — or a second instance — sends nothing twice.
- Health check `jobs`: unhealthy when a job has not succeeded within 2 × its interval + 15 minutes.
- Development only: `POST /api/admin/jobs/{name}/run` (administrators) runs a job now.

| Job | Module | When | What |
| --- | --- | --- | --- |
| `data-reminders` | Cases | hourly | DATA_MISSING_30D / _6M from the current primary assignment |
| `hearing-reminders` | Cases | hourly | HEARING_TOMORROW (from 08:00 Tirane), NEXT_LEVEL_PENDING_3D |
| `notification-cleanup` | Notifications | 02:00 Tirane | read notifications older than 12 months, 5,000 per statement |
| `statistics-snapshot` | Reports | 00:30 Tirane | freezes the year that just ended (per-case statistics flags) if not yet frozen, task 044 |
| `assistant-reconcile` | Cases | 01:45 Tirane | every case with an assistant link row or a linked primary: its LEGAL_ASSISTANT rows brought in line with the links (`AssistantSync`), task 053e |
| `chat-reconcile` | Chat | 01:30 Tirane | every case chat's participants recomputed from the case (repairs a missed event), task 049 |
| `partition-check` | Notifications | 03:00 Tirane | p_max holding rows / next month's partition missing → admins |
| `audit-digest` | Audit | 02:00 Tirane | SHA-256 chain per Tirane day into `audit_daily_digests` (own account `ash_audit_digest`) |

## Authentication (task 051)
- **Providers:** production signs in through one or more providers (`Auth:Providers[]`: Azure AD B2C and / or Entra ID).
  - One JwtBearer scheme per enabled provider; a policy scheme picks it by the token's `iss`.
  - Entra ID tokens only from `AllowedTenantIds`.
  - Setup: docs/deploy/identity.md.
- **Users:** a user is found through `user_identities` (provider, subject).
  - The first login at a provider links by e-mail.
  - At most one identity per provider; never auto-created.
- **`users.external_subject` is deprecated**, replaced by `user_identities` (task 051): kept in the table, no longer read
  or written; drop it in a cleanup script only after the first production release.
- **Development-only features:** the X-Dev-User header, the job-run endpoint and the NoOp scanner exist only in
  Development; `DevFeatures.AssertSafe` refuses to start otherwise.
- **Security items:** docs/security-checklist.md.

## Access model (two layers)
- **System permission** (from `user_roles` → `role_permissions`): menus and office-wide actions, e.g. `case.assign_lawyer`, `case.view_office`, `case.view_all`.
- **Case permission** (from view `v_case_permissions`): what a user may do inside one case, decided by their case profile (`PRIMARY_LAWYER`, `SECONDARY_LAWYER`, `LEGAL_ASSISTANT`, `INSTITUTION_MONITORING`, `INSTITUTION_DELEGATED`).
- `ICaseAccessService.CanView(caseId)` = case profile exists OR `case.view_all` OR (`case.view_office` AND case.office_id = user.office_id) OR (`case.view_supervised` AND the case is under the user's supervision) OR the user is the Protokoll who registered it and it is still `REGISTERED`.
- **Report builder (task 053h, decisions F, P6/P9).**
  - **Who sees what:** `ReportAudience` (Reports module) decides it in ONE place for Tabela 1/3 and the statistics.
    - Kind All / Head (the 053d case scope) / Institution / Lawyers / SecondaryOnly.
    - `CasesSql` gives the reports' WHERE; StatisticsScope maps the reader to `@lawyer_ids` (a set: one lawyer, or an assistant's current links) and `@scope_inst`.
    - The institution predicate (`InstitutionSql`) is shared with StatisticsFlags.sql through a placeholder.
  - **Links:** `CaseLinks` sets `canOpen` per page row with `ICaseAccessService.CanViewAsync`.
  - **Filters:** `Builder/CaseReportFilters.cs` defines every filter of Tabela 1/3: a predicate over c / d, an options query, a parent.
    - `CaseReportBuilder` runs the options inside the reader's scope under the ancestors' choices.
    - `StatisticsBuilder` does the same for Tabela 5/6 (year, zyra → avokati, shkalla → gjykata, ministria → institucioni; locked for an institution).
    - Values are bound parameters; the SQL text is fixed per filter.
  - **Endpoints:** `/api/reports/builder/{t}/filters`, `/builder/active|closed`, `/builder/statistics/{5|6}`, `/builder/{t}/export?format=xlsx|csv|pdf&columns=`.
    - Rows come with a totals row (money per currency, m² apart) and the "Roli im" column.
    - The old 042–044 endpoints stay.
  - **Exports:** `CsvReportWriter` (UTF-8 BOM, ';') and `PdfReportWriter` (MigraDoc/PDFsharp 6, MIT, A4 landscape, at most 5,000 rows) next to the xlsx writer; every export is audited with its format.
    - The PDF needs a TrueType font: Arial on Windows, `fonts-liberation` or `fonts-dejavu-core` on a Linux image.
  - **Saved reports:** "Raportet e mia" in `saved_reports` (script 027), private to the owner, written through the app connection; audited as REPORT_SAVED.
  - **UI:** the builder keeps table, filters (?f= JSON), columns (?c=) and "generated" (?run=1) in the URL — the shareable link.
- **Two lawyer roles (task 053i, client correction 13.11.2026).**
  - "Avokat Shteti (primar)" (`STATE_LAWYER`) and "Avokat sekondar" (`SECONDARY_LAWYER`, script 026, same system permissions) are separate roles; a user may hold both.
  - `OfficeLawyers.ListAsync` is the single list of the pickers and of the assignment / transfer checks; each candidate carries `CanBePrimary` / `CanBeSecondary`, and `OfficeLawyers.PositionError` gives the 400 ("Ky përdorues nuk ka rolin …"), also for the swap. The current user (Kryetar, Monitor) is offered only with one of the roles. Workload stays per position.
  - Assistant links (053e) only to `STATE_LAWYER`. Statistics: `SECONDARY_LAWYER` without `STATE_LAWYER` → 403 (`StatisticsScope`); a `STATE_LAWYER` is counted as primary at the measure's time. Reports stay open to both.
  - `RevokeRoleHandler` refuses to remove `STATE_LAWYER` / `SECONDARY_LAWYER` while the user holds that position on open cases (`user.role_in_use`, with the count; `ICaseAssignmentCounts.CountOpenPositionsAsync`).
  - Dashboard: a holder of `SECONDARY_LAWYER` gets "Si avokat sekondar" next to the "Të miat" widgets; no statistics widgets.
- **Profile, chat and Organika (task 053g, client decision E).**
  - **Profile:** read-only (name, e-mail, position, unit, manager, roles, office / institution). The phone and the notification switches are gone (script 025 drops `users.phone` and `user_notification_prefs`), and every notification is mandatory.
  - **Chat:** the Protokoll who registered a case is in its chat for the life of the case (`ICaseLookup.GetChatParticipantIdsAsync` adds `registered_by`; sync and reconcile as in 049). An administrator (`chat.admin`) opens any case chat from the case page, reads it, and joins with the first message; the reconcile never removes them. Both show with their role in the participants list (PROTOCOL / ADMIN).
  - **Organika** (Administrimi → Organika, `admin.org`):
    - an .xlsx template (email, pozicioni, njesia, eprori_email); an .xlsx or .csv upload is validated into a preview (CHANGED / UNCHANGED / EMAIL_NOT_FOUND / DUPLICATE / MANAGER_NOT_FOUND / INVALID) without applying anything;
    - "Apliko" recomputes it against the current users and writes `users.position`, `org_unit` and `manager_user_id` for the CHANGED rows in ONE transaction, with a `user_org_history` row each;
    - every import is kept in `org_imports` (file, by, at, summary);
    - `org_unit` is text, not the office, so access is unchanged. `TODO(akshi-q-hrmis)`.
- **Earlier levels (task 053f, client decision D2).**
  - Whoever can view X/n also views every EARLIER level X/1 … X/n-1 of the dossier completely, but read-only: effective permissions are `case.view` only, so no chat, upload, decision or edit.
  - It is never backwards: X/1 gives nothing on X/2.
  - Own access to the earlier level wins: the X/1 lawyer keeps X/1's rights. `IsReadOnlyEarlierLevelAsync` marks the rest; the case header (`readOnlyEarlierLevel`) and `levels[].readOnly` carry the mark.
  - Decided in ONE place, `CaseAccessService.LoadAsync` (`ViewableLaterLevelAsync`), so `levels[]`, the history (X/n's history holds the earlier levels' rows with their level labels) and the documents follow by themselves. A level's document is visible exactly when its level is (`DocumentAccess`); dossier-wide documents stay with "any level".
  - The header search reaches the earlier levels too (`CaseScopeQuery.ApplyWithEarlierLevels`). The case lists, the dashboard, the agenda, the reports and the statistics keep the user's own cases: earlier levels' hearings show in the history, not in the agenda.
- **Supervision, "Mbikëqyrja" (task 053d, client decision C, D3).**
  - The Monitor (`supervision.manage`, menu `supervision`) gives a Kryetar either a LAWYER or one CASE (table `supervisions`). A LAWYER covers every case where that lawyer is the CURRENT primary. This is decided at read time, so the lawyer's later cases join and transferred ones leave.
  - The Kryetar's scope is the own office + the supervised cases + the cases where they have a current assignment.
  - On supervised cases they get the same office-head actions as on office cases: transfer, priority, assign, the decision rights. The registration-desk corrections stay with the own office. The lawyer pickers offer the case office's lawyers + the Kryetar.
  - The rule is written once per rendering:
    - EF: `CaseScopeQuery.Supervised`, used by `Apply`, by the "Në mbikëqyrje" tab and by `CaseAccessService`;
    - SQL: `CaseScopeSpec.SupervisedSql`, used by `ReportScope` and by the statistics' `StatisticsFlags.sql` placeholder.
  - So the case list, case page, agenda, dashboard, header search, reports and statistics all agree. `SupervisionTests` compares them.
  - The Kryetar gets a mandatory `SUPERVISION_ASSIGNED` / `SUPERVISION_ENDED` notification. A CASE supervision also writes `SUPERVISION_STARTED` / `SUPERVISION_ENDED` on the case.

- **Legal assistants, "Ndihmësit ligjorë" (task 053e, client decision C, D4).**
  - The Kryetar (`assistant.manage`, now a SYSTEM permission of OFFICE_HEAD; menu `assistants`) links an assistant to lawyers of the office (`assistant_lawyers`).
  - The access code is unchanged. `AssistantSync` keeps a `LEGAL_ASSISTANT` row in `case_assignments`, with `assistant_link_id` set, on every case where a linked lawyer is the CURRENT primary.
  - It runs on link / unlink, on `LawyerAssigned` / `LawyerTransferred` (the assistant rows follow the new primary), and in the nightly `assistant-reconcile` job. It is idempotent.
  - Closing a case keeps the rows, like every other assignment. The case's chat follows the `AssistantAssigned` / `AssistantRemoved` events. The assistant gets one notification per link, not per case.
  - Task 026's per-case "Shto ndihmës" and its endpoints are gone; "Ekipi" shows assistants read-only.
  - Script 024 turned the old per-case rows into links: same office as the current primary → linked; otherwise ended. It is reported in `ash_ops.migration_reports`, which the migrator prints after a run.

## Case life cycle
`REGISTERED` (at Kryetar, no lawyer) → `ACTIVE` (primary lawyer assigned) → `CLOSED` (decision recorded). Dossier → `archived_at` set by "Arkivo" only when the latest level is closed, final, and its reasoned decision has arrived with a clean document (task 053a, `ArchiveRule`). A closed case can continue as X/n+1 at any court level, a lower one too (appeal, recourse, retrial). X/n+1 copies only the object and the parties whose role has `party_roles.copy_to_next_level` (the plaintiff and the defendant, task 053j); each institution among THOSE gets a MONITORING link. The Protokoll may add parties in the form. Category, subcategory, delegation and other parties are not copied (client decisions B.1, 053j c).

## The Protokoll is central; the office of the court (task 053j)
The handoff of task 053c ("Përcjellja", `case_handoffs`, "Çështjet e ardhura") is removed (script 030 drops the table; its history codes stay readable). The decision no longer asks for the next court.
- **Court → office.** `office_courts` (Identity; admin: Administrimi → Zyrat → "Gjykatat që mbulon") says which office covers a court; at most one active office per court.
- **Routing a registration.** Registration (016) and continuation (018) carry `officePublicId` ("Zyra që e ndjek"). `CaseRegistrationSupport.ResolveOfficeAsync` takes it, or the office covering the summons court; a court no office covers needs it (400 on `officePublicId`). `cases.office_id` = that office — NOT the Protokoll's — and `SENT_TO_HEAD` / `CaseRegistered` go to ITS Kryetar (no Kryetar → the old refusal). Another office than the court's is audited (`CASE_OFFICE_OVERRIDE`). `GET /api/cases/registration/offices?courtId=` pre-fills the form.
- **The Protokoll's own list.** "Të regjistruarat nga unë" (`registered` tab) filters by `registered_by`, whatever the office; the desk's corrections follow the same rule while the case is REGISTERED.
- **Country-wide lookup.** `GET /api/cases/registration-search` is a table of every dossier (latest level), registration-level data only — never `case.view` (TODO(client-q-protokoll-full-view)). Status `CONTINUABLE` (default: closed, not final, not archived) | `ALL` | `OPEN` | `CLOSED`. Keyset by `(closed_at, id)` on `ix_cases_status_closed` (CONTINUABLE / CLOSED) or by `id` (ALL / OPEN); "is the latest level" through `uq_cases_dossier_level` (EXPLAIN-checked, `ProtokollCentralTests`). Every request is audited (`REGISTRATION_LOOKUP`, the filters used); the `crossOffice` rate limit applies.
- **"+ Shto filtër".** The report builder's registry (task 053h, `CaseReportFilters.Lookup`: Tabela 3's and Tabela 1's columns plus the registration date, minus the lookup's own filters) is offered through `ICaseFilterCatalog` (Reports.Contracts): `GET …/registration-search/more-filters?more=` gives the filters with their options, and `more=` (the builder's JSON object) on the search adds their predicates over the shown level `c` and its decision `d`. One definition, two uses.

## E-mail (task 053c; no producer since task 053j)
- **Use.** Its only producer was the handoff; since task 053j nothing queues mail. The outbox, the dispatcher and the configuration stay, ready for TODO(client-q-email).
- **Queuing.** `IEmailOutbox.EnqueueAsync` writes `email_outbox` rows in the caller's transaction. Nothing is sent for an action that rolls back.
- **Sending.** `EmailDispatcher` (a `BackgroundService` in Notifications) sends them through `IEmailSender` (`SmtpEmailSender`, MailKit). It runs every `Email:IntervalSeconds` and is woken after each commit that queued mail. Each mail is claimed with `FOR UPDATE SKIP LOCKED`, one transaction per mail.
- **Retries.** A failed mail waits 1, 2, 4 … minutes (at most an hour) and is `FAILED` after `Email:MaxAttempts`, with its last error kept. A mail failure never fails the action.
- **Configuration** (section `Email`): `Enabled`, `SmtpHost`, `SmtpPort`, `SmtpSecurity` (None | StartTls | SslOnConnect), `SmtpUser`, `SmtpPassword`, `FromAddress`, `FromName`. Defaults: `localhost:1025`, no TLS, which is Mailpit from docker-compose. Production uses AKSHI's relay (`TODO(akshi-q-smtp)`).
- **Tests.** The integration tests set `Email:Enabled=false` and run `EmailDispatcher.RunOnceAsync` with a fake `IEmailSender`.

## Registration search filters (task 053b)
The "Vazhdo çështje ekzistuese" search (`GET /api/cases/registration-search`) is built from a registry of filters in `Modules/Cases/.../Application/RegistrationSearch/Filters`:

- **One class per filter.** Each derives from `RegistrationSearchFilter` (directly or through `TextFilter`, `PartyFilter`, `IdFilter`, `CodeFilter`) and gives:
  - `Name`: the query parameter, also the field key of a 400 error;
  - `Order`;
  - the UI metadata: `Input` (which control) and `DependsOn` (the filter that narrows its choices);
  - the SQL predicate over one level `a` of the dossier. Values always go in parameters named after the filter.
- **Registry.** `RegistrationSearchFilters` finds every subclass by reflection. The endpoint reads them from the query string, the OpenAPI document lists them as typed query parameters (so `gen:api` types them), and `GET …/registration-search/filters` returns `{ name, input, dependsOn }` for the page.
- **Semantics:**
  - every given filter must match, each on any level of the dossier, and the dossier is shown by its latest level;
  - unknown parameters are ignored;
  - no filter at all lists every dossier (task 053j: a table, default status "Mund të vazhdohen").

**To add a filter:**
1. Add one class (e.g. `internal sealed class SummonsNumberSearchFilter : TextFilter` with `Name`, `Order` and `Predicate`). Nothing to register.
2. Restart the API and run `npm run gen:api`.
3. Add one UI entry: its label in `sq.cases.continue.filters` (src/frontend/src/i18n/sq.ts). Only if it needs a new `Input` kind, add a renderer for that kind in `pages/cases/ContinueCase.tsx`.
4. Add a test case in `NextLevelTests.B2_each_search_field…`, and update `B2_the_filter_list…`.
