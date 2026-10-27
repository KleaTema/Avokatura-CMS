---
name: ash-task-done-check
description: Final self-review before declaring an ASH task finished — acceptance criteria, hard rules, tests, scope, backlog status. Use at the end of every task from docs/tasks, or when asked to review, verify or finish a task.
---

# Definition of done (ASH)

Before saying a task is finished, go through every item and report the result as a checklist in the chat. If an item fails, fix it first or say clearly why it cannot be done.

## 1. The task file
- [ ] Every acceptance criterion in `docs/tasks/<NNN>-*.md` is met. Quote each one and say how it was verified (test name, command, manual step).
- [ ] Nothing from "Out of scope" was implemented.
- [ ] Only files listed in the task were created/changed, or each extra file is named with the reason.
- [ ] Open questions: default applied and `// TODO(client-qN)` left in the code.

## 2. Hard rules (`.github/copilot-instructions.md`)
- [ ] No module references another module's internals (architecture test passes).
- [ ] No internal BIGINT id in any request/response DTO.
- [ ] Every case read/write goes through `ICaseAccessService`; no-access returns 404.
- [ ] Every case change writes a history event in the same transaction.
- [ ] No hard deletes of legal records. No `DateTime.Now`.
- [ ] row_version checked where the table has it.
- [ ] No EF migrations; schema changes are new DbUp scripts only.
- [ ] No secrets in committed files.

## 3. Build and tests
- [ ] `dotnet build` with zero warnings; `dotnet test` all green (state the count).
- [ ] New write use cases have integration tests incl. the 404 and 409 paths.
- [ ] Frontend tasks: `npm run lint` and `npm run build` pass.

## 4. Hand-off
- [ ] Set the task's Status to `done` in `docs/tasks/README.md`.
- [ ] Propose a commit message: `NNN <task title>` + a 3–5 line summary of what changed.
