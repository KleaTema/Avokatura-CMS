# Task backlog

Each task is one Copilot session. In Visual Studio Copilot Chat (Agent mode):

```
#file:docs/tasks/016-register-new-case.md  Implement this task. Ask before touching files not listed.
```

Rules
- Do tasks in number order inside a phase; a task starts only when its dependencies are done.
- One task = one branch = one pull request. Tick the acceptance criteria in the PR description.
- If Copilot needs more context, attach only what the task's **Also attach** line names (e.g. `docs/architecture.md`).
- "Open question" tasks use the stated default and leave a `// TODO(client-qN)` comment; update the task file when the client answers.
- Keep `Status` here up to date (todo → doing → done).

### Phase 0 — Foundation

| # | Task | Depends on | Open question | Status |
| --- | --- | --- | --- | --- |
| 001 | [Solution skeleton and project references](001-solution-skeleton.md) | — |  | done |
| 002 | [Database project with DbUp](002-database-project-dbup.md) | 001 |  | done |
| 003 | [Shared kernel building blocks](003-shared-kernel.md) | 001 |  | done |
| 004 | [API host, unit of work and error handling](004-api-host-pipeline.md) | 002, 003 |  | done |
| 005 | [Authentication: dev login and production OIDC](005-authentication.md) | 004 | yes | done |
| 006 | [Identity module: current user and menus](006-identity-me-endpoint.md) | 005 |  | done |
| 007 | [Case access service (the single gate to case data)](007-case-access-service.md) | 006 |  | done |
| 008 | [Audit module: append-only audit log](008-audit-log.md) | 005 |  | done |
| 009 | [Case history writer](009-history-writer.md) | 004 |  | done |
| 010 | [Integration test harness](010-integration-test-harness.md) | 002, 004 |  | todo |
| 011 | [Frontend skeleton](011-frontend-skeleton.md) | 006 |  | done |

### Phase 1 — Reference data & administration

| # | Task | Depends on | Open question | Status |
| --- | --- | --- | --- | --- |
| 012 | [Reference data API with caching](012-reference-data-api.md) | 006 |  | done |
| 013 | [Admin: users and roles](013-admin-users-roles.md) | 006, 011 | yes | todo |
| 014 | [Admin: manage lookup lists and settings](014-admin-lookups.md) | 012, 013 |  | todo |
| 015 | [Admin: offices and institutions](015-admin-offices-institutions.md) | 013 |  | todo |

### Phase 2 — Registration (Protokoll)

| # | Task | Depends on | Open question | Status |
| --- | --- | --- | --- | --- |
| 016 | [Register a new case from a summons (X/1)](016-register-new-case.md) | 007, 009, 012 |  | todo |
| 017 | [Parties: add, edit, delete](017-case-parties.md) | 016 |  | todo |
| 018 | [Continue a closed case at the next level (X/n+1)](018-register-next-level.md) | 016, 017 | yes | todo |
| 019 | [Case list with scopes and filters](019-case-list.md) | 007 |  | todo |
| 020 | [Case detail (quick view and tabs)](020-case-detail.md) | 017, 019 |  | todo |
| 021 | [UI: registration form and party editor](021-ui-registration.md) | 011, 016, 017, 018 |  | todo |
| 022 | [UI: case list and case page](022-ui-case-list-detail.md) | 011, 019, 020 |  | todo |

### Phase 3 — Assignment & case data

| # | Task | Depends on | Open question | Status |
| --- | --- | --- | --- | --- |
| 023 | [Kryetar assigns primary and secondary lawyer](023-assign-lawyers.md) | 020 |  | todo |
| 024 | [Transfer the primary lawyer](024-transfer-primary-lawyer.md) | 023 | yes | todo |
| 025 | [Edit case data, status and classification](025-edit-case-data.md) | 023 |  | todo |
| 026 | [Primary lawyer adds or removes the legal assistant](026-legal-assistant.md) | 023 | yes | todo |
| 027 | [Institution: assign jurist; lawyer delegates the case](027-institution-jurist-delegation.md) | 017, 023 | yes | todo |
| 028 | [Change judge and object with history](028-change-judge-object.md) | 020 |  | todo |
| 029 | [UI: assignment, transfer and case data](029-ui-assignment-case-data.md) | 022, 023, 024, 025, 026, 027, 028 |  | todo |

### Phase 4 — Hearings, agenda & documents

| # | Task | Depends on | Open question | Status |
| --- | --- | --- | --- | --- |
| 030 | [Hearings (Seancat)](030-hearings.md) | 020 |  | todo |
| 031 | [Agenda (Agjenda)](031-agenda.md) | 030 | yes | todo |
| 032 | [File storage abstraction (S3)](032-file-storage.md) | 004 | yes | todo |
| 033 | [Document upload pipeline](033-document-upload.md) | 032, 030 |  | todo |
| 034 | [Documents: timeline, download, delete](034-document-list-download-delete.md) | 033 |  | todo |
| 035 | [UI: hearings, agenda and documents](035-ui-hearings-agenda-documents.md) | 022, 030, 031, 034 |  | todo |

### Phase 5 — Decision, closing & archiving

| # | Task | Depends on | Open question | Status |
| --- | --- | --- | --- | --- |
| 036 | [Record the decision and close the case](036-record-decision-close.md) | 030, 034 |  | todo |
| 037 | [Complete the decision later](037-update-decision-later.md) | 036 |  | todo |
| 038 | [Archive the dossier (final decision)](038-archive-dossier.md) | 036 |  | todo |
| 039 | [UI: decision, closing and archiving](039-ui-decision-archive.md) | 022, 036, 037, 038 |  | todo |

### Phase 6 — Notifications & background jobs

| # | Task | Depends on | Open question | Status |
| --- | --- | --- | --- | --- |
| 040 | [Notifications (Njoftimet) with real-time push](040-notifications.md) | 023 | yes | todo |
| 041 | [Background jobs: reminders and maintenance](041-background-jobs.md) | 040, 008 |  | todo |

### Phase 7 — Reports & statistics

| # | Task | Depends on | Open question | Status |
| --- | --- | --- | --- | --- |
| 042 | [Report: active cases (Tabela 1)](042-report-active-cases.md) | 019 | yes | todo |
| 043 | [Report: closed cases (Tabela 3)](043-report-closed-cases.md) | 042 |  | todo |
| 044 | [Statistics (Tabela 5 and 6)](044-statistics.md) | 042 | yes | todo |
| 045 | [UI: reports and statistics](045-ui-reports.md) | 011, 042, 043, 044 |  | todo |

### Phase 8 — Dashboard, search & profile

| # | Task | Depends on | Open question | Status |
| --- | --- | --- | --- | --- |
| 046 | [Dashboard per role](046-dashboard.md) | 019, 031, 040 | yes | todo |
| 047 | [Global search](047-global-search.md) | 019 |  | todo |
| 048 | [Profile page](048-profile.md) | 006 | yes | todo |

### Phase 9 — Chat

| # | Task | Depends on | Open question | Status |
| --- | --- | --- | --- | --- |
| 049 | [Chat backend](049-chat-backend.md) | 040 | yes | todo |
| 050 | [Chat UI](050-chat-ui.md) | 011, 049 |  | todo |

### Phase 10 — Hardening & release

| # | Task | Depends on | Open question | Status |
| --- | --- | --- | --- | --- |
| 051 | [Security hardening](051-security-hardening.md) | 034, 042, 049 |  | todo |
| 052 | [CI pipeline and deployment package](052-deployment-ci.md) | 002, 051 | yes | todo |
