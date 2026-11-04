using Ash.Infrastructure.Jobs;
using Ash.Infrastructure.Persistence;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Notifications.Contracts;
using Ash.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Notifications.Application.Jobs;

/// <summary>
/// Daily at 03:00 Tirane (task 041), after the partition rotation event (02:00 UTC): for ash_chat.messages and
/// ash_audit.audit_log, alerts when p_max holds rows (the monthly rotation stopped, rows pile up in the catch-all) or
/// when next month has no partition yet. The check runs in ash_ops.partition_status (script 016, SQL SECURITY DEFINER:
/// the API account has no rights on those schemas). An alert is a log error plus one notification to every active
/// administrator, at most once per Tirane day (sent_reminders "partition:&lt;date&gt;").
/// </summary>
internal sealed class PartitionCheckJob(
    IServiceScopeFactory scopes, PersistenceOptions persistence, IUserDirectory users, ILogger<PartitionCheckJob> logger)
    : IScheduledJob
{
    public const string JobName = "partition-check";
    public const string AdminRole = "ADMIN";

    public string Name => JobName;

    public JobSchedule Schedule { get; } = JobSchedule.DailyAtTirane(3, 0);

    public async Task RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        var problems = await ProblemsAsync(nowUtc, ct);
        if (problems.Count == 0)
        {
            return;
        }

        logger.LogError("Partition check: {Problems}", string.Join("; ", problems));

        await using var scope = scopes.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.BeginAsync(ct);
        try
        {
            if (await scope.ServiceProvider.GetRequiredService<SentReminders>()
                    .TryMarkAsync($"partition:{TiraneTime.DateOf(nowUtc):yyyy-MM-dd}", nowUtc, ct))
            {
                var admins = await users.GetUsersByRoleAsync(AdminRole, ct);
                await scope.ServiceProvider.GetRequiredService<INotificationSender>().SendAsync(
                    NotificationTexts.PartitionAlert(problems), NotificationRecipients.OfIds(admins.Select(a => a.Id)), ct);
            }

            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>What is wrong, in Albanian, per table ("ash_audit.audit_log: …"); empty when all is well.</summary>
    private async Task<List<string>> ProblemsAsync(DateTime nowUtc, CancellationToken ct)
    {
        // next month's partition ends on the first day of the month after it (partitions are by UTC month)
        var nextMonthEnd = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(2);
        await using var connection = new MySqlConnection(persistence.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand("CALL ash_ops.partition_status(@end)", connection);
        command.Parameters.AddWithValue("@end", nextMonthEnd.Date);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var problems = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            var table = $"{reader.GetString(0)}.{reader.GetString(1)}";
            if (Convert.ToInt32(reader.GetValue(2)) != 0)
            {
                problems.Add($"{table}: particioni p_max ka rreshta");
            }

            if (Convert.ToInt32(reader.GetValue(3)) == 0)
            {
                problems.Add($"{table}: mungon particioni i muajit të ardhshëm");
            }
        }

        return problems;
    }
}
