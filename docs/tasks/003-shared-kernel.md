# 003 — Shared kernel building blocks

**Phase:** 0 · Foundation  
**Depends on:** [001](001-solution-skeleton.md)

## Goal
Provide the small shared types every module uses, so handlers look the same everywhere.

## Context
- Rules 2, 3, 7, 10, 11 in copilot-instructions.
- Public ids are `BINARY(16)` created with `UUID_TO_BIN(UUID(), 1)` (time-swapped). In .NET they are `Guid`; conversion must match MySQL's swap flag.

## Files to create or change
- Ash.SharedKernel/Results/Result.cs, Error.cs (Code, Message, Type: Validation|NotFound|Conflict|Forbidden)
- Ash.SharedKernel/Time/IClock.cs + SystemClock (UtcNow)
- Ash.SharedKernel/Identity/ICurrentUser.cs (UserId, PublicId, OfficeId?, InstitutionId?, Roles, HasPermission)
- Ash.SharedKernel/Ids/PublicId.cs (Guid ⇄ byte[16] with MySQL swap flag 1)
- Ash.SharedKernel/Paging/KeysetPage.cs (Items, NextCursor) + Cursor encode/decode (base64 of sort key + id)
- Ash.SharedKernel/Concurrency/RowVersionMismatchException.cs
- Ash.Infrastructure/Persistence/PublicIdConverter.cs (EF ValueConverter)

## Steps
1. Implement the types; keep them free of ASP.NET and EF dependencies (except the converter in Infrastructure).
2. PublicId: implement the same byte reordering as MySQL `UUID_TO_BIN(x, 1)` / `BIN_TO_UUID(x, 1)`.
3. Unit tests for PublicId round-trip and Cursor encode/decode.

## Acceptance criteria
- [ ] A Guid converted to bytes in .NET equals `UUID_TO_BIN('<same guid>', 1)` in MySQL (test with a known value).
- [ ] All unit tests pass.

## Out of scope
- Persistence, endpoints.
