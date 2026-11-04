using Ash.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;

namespace Ash.Infrastructure.Jobs;

/// <summary>A job as the health check and the admin dashboard see it: its last success and whether it is overdue.</summary>
public sealed record JobStatus(string Name, DateTime? LastSucceededAt, string? LastError, bool Stale);

/// <summary>
/// Reads ash_ops.job_runs for every registered job (task 041). Stale = no success within 2 × the job's interval + 15
/// minutes; a job that never ran counts from the scheduler's start in this process.
/// </summary>
public sealed class JobStatusReader(IServiceScopeFactory scopes, JobSchedulerState state, PersistenceOptions persistence)
{
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(15);

    public async Task<IReadOnlyList<JobStatus>> ReadAsync(DateTime nowUtc, CancellationToken ct)
    {
        List<(string Name, JobSchedule Schedule)> jobs;
        await using (var scope = scopes.CreateAsyncScope())
        {
            jobs = scope.ServiceProvider.GetServices<IScheduledJob>().Select(j => (j.Name, j.Schedule)).ToList();
        }

        var runs = await JobRunStore.ReadAllAsync(persistence.ConnectionString, ct);
        return jobs
            .Select(j =>
            {
                var run = runs.GetValueOrDefault(j.Name);
                var since = run?.LastSucceededAt ?? state.StartedAtUtc ?? nowUtc;
                return new JobStatus(j.Name, run?.LastSucceededAt, run?.LastError, nowUtc - since > 2 * j.Schedule.Interval + Grace);
            })
            .OrderBy(j => j.Name, StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>One partitioned table's state (ash_ops.partition_status, script 016).</summary>
public sealed record PartitionState(string Table, bool PMaxHasRows, bool NextMonthReady)
{
    public bool Ok => !PMaxHasRows && NextMonthReady;
}

/// <summary>
/// The partition check of ash_chat.messages and ash_audit.audit_log through ash_ops.partition_status (SQL SECURITY
/// DEFINER: the API account has no rights on those schemas). Used by the partition check job and the admin dashboard.
/// </summary>
public static class PartitionStatus
{
    public static async Task<IReadOnlyList<PartitionState>> ReadAsync(string connectionString, DateTime nowUtc, CancellationToken ct)
    {
        // next month's partition ends on the first day of the month after it (partitions are by UTC month)
        var nextMonthEnd = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(2);
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand("CALL ash_ops.partition_status(@end)", connection);
        command.Parameters.AddWithValue("@end", nextMonthEnd.Date);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var states = new List<PartitionState>();
        while (await reader.ReadAsync(ct))
        {
            states.Add(new PartitionState($"{reader.GetString(0)}.{reader.GetString(1)}",
                Convert.ToInt32(reader.GetValue(2)) != 0, Convert.ToInt32(reader.GetValue(3)) != 0));
        }

        return states;
    }
}
