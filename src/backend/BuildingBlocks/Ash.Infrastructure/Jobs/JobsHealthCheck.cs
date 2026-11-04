using Ash.Infrastructure.Persistence;
using Ash.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ash.Infrastructure.Jobs;

/// <summary>
/// Health check "jobs" (task 041): unhealthy when a job has not succeeded within twice its interval plus a grace of
/// 15 minutes (an hourly job: 2 h 15 min; a daily one: 48 h 15 min). A job that never ran counts from the scheduler's
/// start. With Jobs:Enabled = false nothing is expected to run: healthy, saying so.
/// </summary>
public sealed class JobsHealthCheck(
    IServiceScopeFactory scopes, JobOptions options, JobSchedulerState state, PersistenceOptions persistence, IClock clock)
    : IHealthCheck
{
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(15);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            return HealthCheckResult.Healthy("Background jobs are disabled.");
        }

        List<(string Name, JobSchedule Schedule)> jobs;
        await using (var scope = scopes.CreateAsyncScope())
        {
            jobs = scope.ServiceProvider.GetServices<IScheduledJob>().Select(j => (j.Name, j.Schedule)).ToList();
        }

        IReadOnlyDictionary<string, JobRunState> runs;
        try
        {
            runs = await JobRunStore.ReadAllAsync(persistence.ConnectionString, cancellationToken);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("ash_ops.job_runs cannot be read.", ex);
        }

        var now = clock.UtcNow;
        var stale = new List<string>();
        foreach (var (name, schedule) in jobs)
        {
            var since = runs.TryGetValue(name, out var run) && run.LastSucceededAt is { } last ? last : state.StartedAtUtc ?? now;
            if (now - since > 2 * schedule.Interval + Grace)
            {
                stale.Add(run?.LastError is { } error ? $"{name} ({error})" : name);
            }
        }

        return stale.Count == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"Jobs without a recent success: {string.Join(", ", stale)}.");
    }
}
