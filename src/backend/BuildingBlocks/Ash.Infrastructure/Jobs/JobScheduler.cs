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

    /// <summary>Jobs that cannot run because the database lacks a table or procedure (not migrated): job → error.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, string> SchemaMissing { get; } = new();

    /// <summary>MySQL "table doesn't exist" (1146) or "procedure does not exist" (1305): the migrator has not run.</summary>
    public static bool IsSchemaMissing(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is MySql.Data.MySqlClient.MySqlException { Number: 1146 or 1305 })
            {
                return true;
            }
        }

        return false;
    }
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

    /// <summary>How often a job blocked by a missing table is tried again (without logging).</summary>
    public static readonly TimeSpan SchemaRetry = TimeSpan.FromMinutes(10);

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
                    if (state.SchemaMissing.TryRemove(name, out _))
                    {
                        logger.LogInformation("Job {Job} runs again: the database now has what it needs.", name);
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested && JobSchedulerState.IsSchemaMissing(ex))
                {
                    // not migrated (e.g. a new script not applied locally): say it ONCE, report it in the health check,
                    // and look again quietly every SchemaRetry — it recovers by itself once the migrator has run
                    if (state.SchemaMissing.TryAdd(name, ex.Message))
                    {
                        logger.LogError(ex, "Job {Job} cannot run: the database is missing a table or procedure. Run the migrator " +
                                            "(dotnet run --project src/backend/Database/Ash.Database). Retrying every {Minutes} minutes, silently.",
                            name, SchemaRetry.TotalMinutes);
                    }

                    next[name] = now + SchemaRetry;
                    continue;
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
