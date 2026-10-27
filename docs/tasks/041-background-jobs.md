# 041 — Background jobs: reminders and maintenance

**Phase:** 6 · Notifications & background jobs  
**Depends on:** [040](040-notifications.md), [008](008-audit-log.md)

## Goal
Run scheduled jobs: 30-day and 6-month data reminders, notification cleanup, audit digest, partition check.

## Context
- 30 days after `assigned_at` with `first_data_entry_at IS NULL` → notify primary lawyer + Kryetar (DATA_MISSING_30D). 6 months → also the Monitors (DATA_MISSING_6M). `case_reminders` unique (assignment_id, reminder_type) guarantees once.
- Cleanup: delete READ notifications older than 12 months.
- Audit digest: SHA-256 over yesterday's audit rows chained with the previous digest → `audit_daily_digests`.
- Alert if partition `p_max` of messages/audit_log holds rows (rotation event stopped).

## Files to create or change
- Ash.Infrastructure/Jobs/ (simple `BackgroundService` scheduler or Quartz.NET — pick one, document it)
- Ash.Modules.Cases/Application/Jobs/DataReminderJob.cs
- Ash.Modules.Notifications/Application/Jobs/CleanupJob.cs
- Ash.Modules.Audit/Application/Jobs/DailyDigestJob.cs

## Steps
1. Jobs must be safe if two API instances run: take a MySQL named lock (`GET_LOCK('job:<name>', 0)`).
2. Times: reminders hourly; cleanup and digest nightly 02:00 Europe/Tirane.

## Acceptance criteria
- [ ] Running the reminder job twice sends each reminder once.
- [ ] Integration test with a fake clock.

## Out of scope
- Anything not listed above.
