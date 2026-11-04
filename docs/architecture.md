# Architecture — module map

Attach this file to a Copilot chat only when a task says so; `.github/copilot-instructions.md` is always loaded.

## Modules and the tables they own

| Module | Owns (schema `ash_core` unless noted) | Exposes in `.Contracts` |
| --- | --- | --- |
| Identity | `users`, `roles`, `permissions`, `role_permissions`, `user_roles`, `case_profile_permissions`, `offices`, `institutions` | `ICurrentUser` data, `IUserDirectory`, `IPermissionService` |
| Reference | `court_levels`, `courts`, `judges`, `party_roles`, `summons_types`, `hearing_types`, `document_types`, `decision_results`, `case_categories`, `case_subcategories`, `allowed_file_types`, `dossier_code_formats` | `IReferenceData` (cached lookups) |
| Cases | `dossiers`, `cases`, `case_assignments`, `case_institutions`, `case_parties`, `case_hearings`, `case_security_measures`, `case_decisions`, `case_history_events`, `case_reminders` | `ICaseAccessService`, `IHistoryWriter`, `ICaseLookup`, integration events |
| Documents | `documents` + object storage buckets | `IDocumentService` (attach decision PDF, list) |
| Notifications | `notifications` | `INotificationSender` |
| Audit | `ash_audit.audit_log`, `ash_audit.audit_daily_digests` | `IAuditLog` |
| (building block `Ash.Infrastructure.Jobs`) | `ash_ops.job_runs`, `ash_core.sent_reminders` | `IScheduledJob`, `JobRunner`, `SentReminders` |
| Reports | none (reads views `v_report_*` and statistics SQL, read-only, replica connection) | — |
| Chat | `ash_chat.*` | — |

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
| `partition-check` | Notifications | 03:00 Tirane | p_max holding rows / next month's partition missing → admins |
| `audit-digest` | Audit | 02:00 Tirane | SHA-256 chain per Tirane day into `audit_daily_digests` (own account `ash_audit_digest`) |

## Access model (two layers)
- **System permission** (from `user_roles` → `role_permissions`): menus and office-wide actions, e.g. `case.assign_lawyer`, `case.view_office`, `case.view_all`.
- **Case permission** (from view `v_case_permissions`): what a user may do inside one case, decided by their case profile (`PRIMARY_LAWYER`, `SECONDARY_LAWYER`, `LEGAL_ASSISTANT`, `INSTITUTION_MONITORING`, `INSTITUTION_DELEGATED`).
- `ICaseAccessService.CanView(caseId)` = case profile exists OR `case.view_all` OR (`case.view_office` AND case.office_id = user.office_id) OR the user is the Protokoll who registered it and it is still `REGISTERED`.

## Case life cycle
`REGISTERED` (at Kryetar, no lawyer) → `ACTIVE` (primary lawyer assigned) → `CLOSED` (decision recorded). Dossier → `archived_at` set when a level closes with `is_final = 1`. A closed case can continue as X/n+1 (appeal, recourse, retrial).
