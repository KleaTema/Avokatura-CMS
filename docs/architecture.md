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
| `chat-reconcile` | Chat | 01:30 Tirane | every case chat's participants recomputed from the case (repairs a missed event), task 049 |
| `partition-check` | Notifications | 03:00 Tirane | p_max holding rows / next month's partition missing → admins |
| `audit-digest` | Audit | 02:00 Tirane | SHA-256 chain per Tirane day into `audit_daily_digests` (own account `ash_audit_digest`) |

## Access model (two layers)
- **System permission** (from `user_roles` → `role_permissions`): menus and office-wide actions, e.g. `case.assign_lawyer`, `case.view_office`, `case.view_all`.
- **Case permission** (from view `v_case_permissions`): what a user may do inside one case, decided by their case profile (`PRIMARY_LAWYER`, `SECONDARY_LAWYER`, `LEGAL_ASSISTANT`, `INSTITUTION_MONITORING`, `INSTITUTION_DELEGATED`).
- `ICaseAccessService.CanView(caseId)` = case profile exists OR `case.view_all` OR (`case.view_office` AND case.office_id = user.office_id) OR the user is the Protokoll who registered it and it is still `REGISTERED`.

## Case life cycle
`REGISTERED` (at Kryetar, no lawyer) → `ACTIVE` (primary lawyer assigned) → `CLOSED` (decision recorded). Dossier → `archived_at` set by "Arkivo" only when the latest level is closed, final, and its reasoned decision has arrived with a clean document (task 053a, `ArchiveRule`). A closed case can continue as X/n+1 at any court level, a lower one too (appeal, recourse, retrial). X/n+1 copies only the parties and the object; each institution party gets a MONITORING link. Category, subcategory and delegation are not copied (client decision B.1).

## Handoff to the next level — "Përcjellja e çështjes" (task 053c)
- **Court → office.** `office_courts` (Identity; admin: Administrimi → Zyrat → "Gjykatat që mbulon") says which office handles a court. A court has at most one active office (unique index on a generated column).
- **Closing.** `POST /api/cases/{id}/decision` with `isFinal = false` requires the destination, `nextCourtLevelCode` + `nextCourtId` (a court of that level; any level, a lower one too). The office comes from the court. When no office covers the court, `receivingOfficePublicId` + `handoffReason` (at least 10 characters) are required. With `isFinal = true` the fields are refused.
- **What it writes.** A `case_handoffs` row and history `CASE_HANDED_OFF`, in the closing's transaction (`HandoffService`). The `CaseHandedOff` event makes the Notifications module tell every active Protokoll of the office: a mandatory notification `CASE_HANDED_OFF` and an e-mail.
- **Receiving office.** It sees the case under "Çështjet e ardhura" (`GET /api/cases/handoffs/incoming`, an extra tab in `/api/cases/tabs`). It gets registration-level data only, never `case.view`: no documents, no chat. "Vazhdo" opens the next-level form with level and court filled in. Registering X/n+1 sets `accepted_case_id`.
- **Correction.** `PUT /api/cases/{id}/handoff` is open to whoever has `decision.record` on the closed level, until the handoff is accepted. The open row is cancelled and a new one written (`HANDOFF_CHANGED`). The old office gets `HANDOFF_WITHDRAWN`; the new one gets `CASE_HANDED_OFF`.
- **Final later.** `PUT /decision` that makes the decision final cancels the open handoff ("Vendimi mori formë të prerë", `HANDOFF_CANCELLED`), and the office gets `HANDOFF_WITHDRAWN`.

## E-mail (task 053c)
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
  - no filter at all is a 400.

**To add a filter:**
1. Add one class (e.g. `internal sealed class SummonsNumberSearchFilter : TextFilter` with `Name`, `Order` and `Predicate`). Nothing to register.
2. Restart the API and run `npm run gen:api`.
3. Add one UI entry: its label in `sq.cases.continue.filters` (src/frontend/src/i18n/sq.ts). Only if it needs a new `Input` kind, add a renderer for that kind in `pages/cases/ContinueCase.tsx`.
4. Add a test case in `NextLevelTests.B2_each_search_field…`, and update `B2_the_filter_list…`.
