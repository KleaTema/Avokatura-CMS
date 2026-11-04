using Ash.Infrastructure.Persistence;
using Ash.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ash.Infrastructure.Jobs;

/// <summary>Configuration section "Jobs". Enabled false = nothing runs on its own (tests run jobs explicitly).</summary>
public sealed class JobOptions
{
    public const string Section = "Jobs";

    public bool Enabled { get; set; } = true;
}

/// <summary>When the scheduler started in this process (the health check's grace for jobs that never ran).</summary>
public sealed class JobSchedulerState
{
    public DateTime? StartedAtUtc { get; set; }
}

/// <summary>
/// The in-house scheduler (task 041, docs/architecture.md "Background jobs"): one BackgroundService that wakes every
/// 30 seconds, asks <see cref="IClock"/> what time it is and runs each due job through <see cref="JobRunner"/>, one
/// after another. On start every job whose latest scheduled run is newer than its last success runs at once
/// (catch-up after downtime); a daily job that must cover every missed day (the audit digest) loops over them itself.
/// </summary>
public sealed class JobScheduler(
    IServiceScopeFactory scopes,
    JobRunner runner,
    JobOptions options,
    JobSchedulerState state,
    PersistenceOptions persistence,
    IClock clock,
    ILogger<JobScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Background jobs are disabled (Jobs:Enabled = false).");
            return;
        }

        state.StartedAtUtc = clock.UtcNow;
        List<(string Name, JobSchedule Schedule)> jobs;
        await using (var scope = scopes.CreateAsyncScope())
        {
            jobs = scope.ServiceProvider.GetServices<IScheduledJob>().Select(j => (j.Name, j.Schedule)).ToList();
        }

        var next = new Dictionary<string, DateTime>();
        try
        {
            var runs = await JobRunStore.ReadAllAsync(persistence.ConnectionString, stoppingToken);
            var now = clock.UtcNow;
            foreach (var (name, schedule) in jobs)
            {
                var last = runs.TryGetValue(name, out var run) ? run.LastSucceededAt : null;
                next[name] = last is null || last < schedule.LatestAtOrBefore(now) ? now : schedule.NextAfter(now);
            }
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Reading ash_ops.job_runs failed; every job runs now.");
            foreach (var (name, _) in jobs)
            {
                next[name] = clock.UtcNow;
            }
        }

        using var timer = new PeriodicTimer(Tick);
        do
        {
            foreach (var (name, schedule) in jobs)
            {
                var now = clock.UtcNow;
                if (now < next[name])
                {
                    continue;
                }

                try
                {
                    await runner.RunAsync(name, now, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // the lock connection itself failed (database down): try again at the next scheduled time
                    logger.LogError(ex, "Job {Job} could not be started.", name);
                }

                next[name] = schedule.NextAfter(now);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
