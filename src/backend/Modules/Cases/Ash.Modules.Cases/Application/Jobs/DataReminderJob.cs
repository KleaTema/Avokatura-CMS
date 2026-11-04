using Ash.Infrastructure.Jobs;
using Ash.Infrastructure.Persistence;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ash.Modules.Cases.Application.Jobs;

/// <summary>
/// Hourly (task 041): an ACTIVE case of a dossier that is not archived, still without its first data entry
/// (cases.first_data_entry_at), gets DATA_MISSING_30D 30 days and DATA_MISSING_6M 6 months after the assigned_at of its
/// CURRENT primary assignment — a transfer starts a new assignment, so the timer restarts.
/// TODO(client-q6): restarting after a transfer is the default (also TransferLawyerHandler).
/// TODO(client-q-suspended-reminders): a suspended case is still reminded (default).
/// Per case ONE transaction: the case_reminders row (uq_cr_once (assignment_id, reminder_type): a duplicate means
/// already sent → skipped) and the notifications (DataEntryReminderDue → Notifications). A job that was down past both
/// thresholds sends both, once each.
/// </summary>
internal sealed class DataReminderJob(
    IServiceScopeFactory scopes, CasesDbContext db, IUserDirectory users, IOfficeDirectory offices, ILogger<DataReminderJob> logger)
    : IScheduledJob
{
    public const string JobName = "data-reminders";

    public string Name => JobName;

    public JobSchedule Schedule => JobSchedule.Hourly;

    public async Task RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        var thirtyDaysAgo = nowUtc.AddDays(-30);
        var candidates = await (
                from c in db.Cases.AsNoTracking()
                join d in db.Dossiers.AsNoTracking() on c.DossierId equals d.Id
                join a in db.CaseAssignments.AsNoTracking() on c.Id equals a.CaseId
                where c.LifecycleStatus == Case.Active && c.FirstDataEntryAt == null && d.ArchivedAt == null
                      && a.CaseRole == CaseAssignmentRow.PrimaryLawyer && a.EndedAt == null && a.AssignedAt <= thirtyDaysAgo
                select new Candidate((long)c.Id, c.PublicId, c.OfficeId, (long)a.Id, a.UserId, a.AssignedAt))
            .ToListAsync(ct);
        if (candidates.Count == 0)
        {
            return;
        }

        // what was sent already (most candidates after 6 months): skipped without opening a transaction
        var assignmentIds = candidates.Select(c => (ulong)c.AssignmentId).ToList();
        var sent = await db.CaseReminders.AsNoTracking()
            .Where(r => assignmentIds.Contains(r.AssignmentId))
            .Select(r => new { r.AssignmentId, r.ReminderType })
            .ToListAsync(ct);
        var already = sent.Select(r => $"{r.AssignmentId}:{r.ReminderType}").ToHashSet();

        var failures = 0;
        foreach (var candidate in candidates)
        {
            var due = Due(candidate.AssignedAt, nowUtc).Where(t => !already.Contains($"{candidate.AssignmentId}:{t}")).ToList();
            if (due.Count == 0)
            {
                continue;
            }

            try
            {
                await RemindAsync(candidate, due, nowUtc, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                failures++;
                logger.LogError(ex, "Data reminder for case {CaseId} failed; the next run tries again.", candidate.CasePublicId);
            }
        }

        if (failures > 0)
        {
            throw new InvalidOperationException($"{failures} data reminder(s) failed; see the log.");
        }
    }

    /// <summary>The reminder types due for an assignment made at <paramref name="assignedAt"/>.</summary>
    public static IEnumerable<string> Due(DateTime assignedAt, DateTime nowUtc)
    {
        if (assignedAt.AddDays(30) <= nowUtc)
        {
            yield return DataEntryReminderDue.ThirtyDays;
        }

        if (assignedAt.AddMonths(6) <= nowUtc)
        {
            yield return DataEntryReminderDue.SixMonths;
        }
    }

    private async Task RemindAsync(Candidate candidate, List<string> due, DateTime nowUtc, CancellationToken ct)
    {
        var lawyer = (await users.GetPublicIdsAsync([candidate.PrimaryUserId], ct)).TryGetValue(candidate.PrimaryUserId, out var id) ? id : (Guid?)null;
        var head = (await offices.GetRoutingAsync(candidate.OfficeId, ct))?.Head is { IsActive: true } h ? h.PublicId : (Guid?)null;

        await using var scope = scopes.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var events = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        await unitOfWork.BeginAsync(ct);
        try
        {
            foreach (var type in due)
            {
                await using var insert = unitOfWork.CreateCommand("""
                    INSERT IGNORE INTO case_reminders (case_id, assignment_id, reminder_type, sent_at)
                    VALUES (@case, @assignment, @type, @at)
                    """);
                insert.Parameters.AddWithValue("@case", candidate.CaseId);
                insert.Parameters.AddWithValue("@assignment", candidate.AssignmentId);
                insert.Parameters.AddWithValue("@type", type);
                insert.Parameters.AddWithValue("@at", nowUtc);
                if (await insert.ExecuteNonQueryAsync(ct) == 1)
                {
                    await events.PublishAsync(new DataEntryReminderDue(candidate.CasePublicId, type, candidate.AssignedAt, lawyer, head), ct);
                }
            }

            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private sealed record Candidate(long CaseId, Guid CasePublicId, long OfficeId, long AssignmentId, long PrimaryUserId, DateTime AssignedAt);
}
