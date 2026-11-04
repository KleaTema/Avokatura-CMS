using Ash.SharedKernel.Time;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ash.Infrastructure.Jobs;

/// <summary>
/// Health check "jobs" (task 041): unhealthy when a job has not succeeded within twice its interval plus a grace of
/// 15 minutes (an hourly job: 2 h 15 min; a daily one: 48 h 15 min) — <see cref="JobStatusReader"/>. A job that never
/// ran counts from the scheduler's start. With Jobs:Enabled = false nothing is expected to run: healthy, saying so.
/// </summary>
public sealed class JobsHealthCheck(JobStatusReader reader, JobOptions options, IClock clock) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            return HealthCheckResult.Healthy("Background jobs are disabled.");
        }

        IReadOnlyList<JobStatus> jobs;
        try
        {
            jobs = await reader.ReadAsync(clock.UtcNow, cancellationToken);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("ash_ops.job_runs cannot be read.", ex);
        }

        var stale = jobs.Where(j => j.Stale).Select(j => j.LastError is { } error ? $"{j.Name} ({error})" : j.Name).ToList();
        return stale.Count == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"Jobs without a recent success: {string.Join(", ", stale)}.");
    }
}
