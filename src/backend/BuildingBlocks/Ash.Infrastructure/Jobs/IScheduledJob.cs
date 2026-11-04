namespace Ash.Infrastructure.Jobs;

/// <summary>
/// A background job (task 041). Registered as scoped (<c>services.AddScheduledJob&lt;T&gt;()</c>); the scheduler and
/// the Development endpoint run it through <see cref="JobRunner"/> (named lock, job_runs state) in a fresh scope. Tests
/// call <see cref="RunAsync"/> — or JobRunner — directly with any "now", so no real clock is needed.
/// A job opens its own transactions (IUnitOfWork.BeginAsync/CommitAsync), one per unit of work it must keep whole
/// (e.g. one case), and must be safe to run again: whatever it sends is deduplicated in the database.
/// </summary>
public interface IScheduledJob
{
    /// <summary>Unique, at most 60 characters (job_runs.job_name; the lock is "job:&lt;name&gt;").</summary>
    string Name { get; }

    JobSchedule Schedule { get; }

    Task RunAsync(DateTime nowUtc, CancellationToken ct);
}
