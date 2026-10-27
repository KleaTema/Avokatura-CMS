# 034 — Documents: timeline, download, delete

**Phase:** 4 · Hearings, agenda & documents  
**Depends on:** [033](033-document-upload.md)

## Goal
List the dossier's documents as one chronological timeline, download them safely, and hide wrong uploads.

## Context
- Index `ix_documents_timeline(dossier_id, deleted_at, uploaded_at)`.
- Download: check `case.view`, audit DOCUMENT_DOWNLOAD, then stream or return a ≤2-minute signed URL.
- Delete = soft (`deleted_at`, `deleted_by`, `delete_reason`), permission `document.delete` (primary). The object stays until retention ends; early physical removal is admin-only (governance mode bypass) with audit.

## Files to create or change
- Ash.Modules.Documents/Application/List/*, Download/*, Delete/*
- Ash.Modules.Documents.Contracts/IDocumentService.cs

## Steps
1. `GET /api/cases/{id}/documents?type=&level=&after=` (newest first, shows level X/n and uploader).
2. `GET /api/documents/{docId}/download`, `DELETE /api/documents/{docId}` { reason }. History DOCUMENT_REMOVED.

## Acceptance criteria
- [ ] Every download leaves an audit row.
- [ ] Deleted documents vanish from the list but remain in history.

## Out of scope
- Anything not listed above.
