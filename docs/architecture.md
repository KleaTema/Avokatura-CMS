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
| Reports | none (reads views `v_report_*` and statistics SQL, read-only, replica connection) | — |
| Chat | `ash_chat.*` | — |

## Cross-module communication
- Synchronous: call an interface from the other module's `.Contracts`.
- Events: `IEventPublisher.Publish(new LawyerAssigned(...))` — handlers run in-process, inside the SAME transaction, so a notification is never lost or sent for a rolled-back change.
- Reports is the only module allowed to read other modules' tables, and only through SQL views/queries, read-only.

## Request pipeline
Auth → `CurrentUser` → `IUnitOfWork` opens one MySQL connection + transaction → handler → `SaveChanges` on every touched context → commit → audit row (separate connection, `ash_audit_writer` account) → response.

## Access model (two layers)
- **System permission** (from `user_roles` → `role_permissions`): menus and office-wide actions, e.g. `case.assign_lawyer`, `case.view_office`, `case.view_all`.
- **Case permission** (from view `v_case_permissions`): what a user may do inside one case, decided by their case profile (`PRIMARY_LAWYER`, `SECONDARY_LAWYER`, `LEGAL_ASSISTANT`, `INSTITUTION_MONITORING`, `INSTITUTION_DELEGATED`).
- `ICaseAccessService.CanView(caseId)` = case profile exists OR `case.view_all` OR (`case.view_office` AND case.office_id = user.office_id) OR the user is the Protokoll who registered it and it is still `REGISTERED`.

## Case life cycle
`REGISTERED` (at Kryetar, no lawyer) → `ACTIVE` (primary lawyer assigned) → `CLOSED` (decision recorded). Dossier → `archived_at` set when a level closes with `is_final = 1`. A closed case can continue as X/n+1 (appeal, recourse, retrial).
