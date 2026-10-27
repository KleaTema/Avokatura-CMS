# 032 — File storage abstraction (S3)

**Phase:** 4 · Hearings, agenda & documents  
**Depends on:** [004](004-api-host-pipeline.md)

## Goal
One `IFileStorage` used by documents and chat, working with MinIO locally and AKSHI's S3-compatible storage in production.

## Context
- Buckets: `ash-uploads-quarantine` (1-day expiry), `ash-documents` (object lock, governance mode), `ash-chat-files`.
- Keys are generated (`yyyy/MM/<uuid>`), never from user file names. Bucket is never public.

## Files to create or change
- Ash.Infrastructure/Storage/IFileStorage.cs, S3FileStorage.cs (AWSSDK.S3, ForcePathStyle = true)
- appsettings: `Storage:ServiceUrl`, `Storage:AccessKey`, `Storage:SecretKey` (user-secrets)

## Steps
1. Methods: `PutAsync(bucket, key, stream, contentType)`, `OpenReadAsync`, `CopyAsync`, `DeleteAsync` (quarantine only), `GetDownloadUrl(bucket, key, ttl ≤ 2 min, fileName)`.
2. Health check for storage reachability.

## Acceptance criteria
- [ ] Integration test against MinIO (Testcontainers or the compose instance) uploads, copies and downloads a file.

## Out of scope
- Anything not listed above.

## Open questions (use the default, mark TODO in code)
- Confirm with AKSHI: object lock, versioning, encryption, signed URLs support.
