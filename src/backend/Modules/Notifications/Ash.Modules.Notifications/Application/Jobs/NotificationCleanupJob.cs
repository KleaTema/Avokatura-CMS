using Ash.Infrastructure.Jobs;
using Ash.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ash.Modules.Notifications.Application.Jobs;

/// <summary>
/// Daily at 02:00 Tirane (task 041): deletes READ notifications created more than 12 months ago, 5,000 rows per
/// statement, each in its own short transaction, until none are left (locks stay short; ix_notif_cleanup). Unread ones
/// are never deleted, however old. TODO(client-q9): 12 months is the default retention.
/// </summary>
internal sealed class NotificationCleanupJob(IServiceScopeFactory scopes, ILogger<NotificationCleanupJob> logger) : IScheduledJob
{
    public const string JobName = "notification-cleanup";
    public const int BatchSize = 5000;
    public const int RetentionMonths = 12;

    public string Name => JobName;

    public JobSchedule Schedule { get; } = JobSchedule.DailyAtTirane(2, 0);

    public async Task RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        var cutoff = nowUtc.AddMonths(-RetentionMonths);
        var total = 0;
        int deleted;
        do
        {
            await using var scope = scopes.CreateAsyncScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await unitOfWork.BeginAsync(ct);
            try
            {
                await using var command = unitOfWork.CreateCommand(
                    $"DELETE FROM notifications WHERE read_at IS NOT NULL AND created_at < @cutoff LIMIT {BatchSize}");
                command.Parameters.AddWithValue("@cutoff", cutoff);
                deleted = await command.ExecuteNonQueryAsync(ct);
                await unitOfWork.CommitAsync(ct);
            }
            catch
            {
                await unitOfWork.RollbackAsync(CancellationToken.None);
                throw;
            }

            total += deleted;
        }
        while (deleted == BatchSize);

        logger.LogInformation("Notification cleanup: {Count} read notifications older than {Cutoff:O} deleted.", total, cutoff);
    }
}
