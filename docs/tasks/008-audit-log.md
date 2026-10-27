# 008 — Audit module: append-only audit log

**Phase:** 0 · Foundation  
**Depends on:** [005](005-authentication.md)

## Goal
Record every security-relevant action (including reads) in `ash_audit.audit_log` through a separate, insert-only connection.

## Context
- Table `ash_audit.audit_log` (partitioned by month): occurred_at, user_id, user_display, role_codes, action, outcome, entity_type, entity_id, case_id, ip_address (VARBINARY via INET6_ATON), user_agent, correlation_id, details JSON.
- Account `ash_audit_writer` has INSERT+SELECT only. Connection string `ConnectionStrings:Audit`.
- Audit must not fail the user's request: on audit failure log an error and continue.

## Files to create or change
- Ash.Modules.Audit.Contracts/IAuditLog.cs (`Record(action, entityType?, entityId?, caseId?, details?, outcome)`)
- Ash.Modules.Audit/Infrastructure/AuditWriter.cs (Dapper or raw ADO, own connection, batched via Channel)
- Ash.Api/Middleware/AuditMiddleware.cs (ACCESS_DENIED for 401/403, LOGIN on first request of a session)

## Steps
1. Write through a bounded `Channel<AuditEntry>` + background consumer that inserts in batches every ≤1 s (fast requests, no lost rows on normal shutdown — flush on stop).
2. Action names as constants: LOGIN, LOGIN_FAILED, ACCESS_DENIED, CASE_VIEW, CASE_UPDATE, DOCUMENT_DOWNLOAD, DOCUMENT_UPLOAD, REPORT_EXPORT, ADMIN_CHANGE.
3. Never put file contents, passwords or tokens in `details`.

## Acceptance criteria
- [ ] After a request to a case endpoint a CASE_VIEW row exists with user, case_id, IP and correlation id.
- [ ] Stopping MySQL audit connection does not break the request (error logged).
- [ ] Integration test.

## Out of scope
- Daily digest job (task 041).
