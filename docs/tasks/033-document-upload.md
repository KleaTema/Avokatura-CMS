# 033 — Document upload pipeline

**Phase:** 4 · Hearings, agenda & documents  
**Depends on:** [032](032-file-storage.md), [030](030-hearings.md)

## Goal
Upload a case document safely: type check by content, quarantine, hash, scan, then move to permanent storage.

## Context
- `documents(dossier_id, case_id, document_type_id, other_type_text, title, original_file_name, content_type, size_bytes, sha256, storage_bucket, storage_key, scan_status PENDING|CLEAN|INFECTED|FAILED, uploaded_by, uploaded_at)`.
- `allowed_file_types` decides extension + magic bytes + max size (applies_to DOCUMENT or BOTH).
- Permission `document.upload`. Documents belong to the dossier (one timeline for all levels).

## Files to create or change
- Ash.Modules.Documents/Application/Upload/*
- Ash.Modules.Documents/Infrastructure/Scanning/IVirusScanner.cs, ClamAvScanner.cs, NoOpScanner.cs (dev)
- Ash.Modules.Documents/Infrastructure/Jobs/ScanQueueJob.cs
- Ash.Modules.Documents/Endpoints/DocumentEndpoints.cs

## Steps
1. `POST /api/cases/{id}/documents` multipart (type, title, file). Stream to quarantine while computing SHA-256 (no full file in memory). Check magic bytes on the first bytes.
2. Row saved with PENDING; job scans, copies to `ash-documents`, sets CLEAN (or INFECTED + notify uploader and admin).
3. History DOCUMENT_UPLOADED; set `first_data_entry_at`; audit DOCUMENT_UPLOAD.
4. Request size limit = max of allowed types + margin (Kestrel + endpoint).

## Acceptance criteria
- [ ] A renamed .exe as .pdf → 400 'Lloji i skedarit nuk lejohet'.
- [ ] A 40 MB PDF uploads without the API using >100 MB RAM.
- [ ] PENDING documents are not downloadable.

## Out of scope
- Anything not listed above.
