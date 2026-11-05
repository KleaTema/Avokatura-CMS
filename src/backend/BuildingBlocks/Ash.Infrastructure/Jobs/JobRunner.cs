using Ash.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace Ash.Infrastructure.Jobs;

public enum JobOutcome
{
    /// <summary>It ran to the end; job_runs.last_succeeded_at was set.</summary>
    Succeeded,

    /// <summary>Another instance holds the job's lock: it is running there, nothing done here.</summary>
    Skipped,

    /// <summary>It threw; job_runs.last_error holds the message.</summary>
    Failed,
}

/// <summary>One job's row of ash_ops.job_runs.</summary>
public sealed record JobRunState(string JobName, DateTime? LastStartedAt, DateTime? LastSucceededAt, string? LastError);

/// <summary>
/// Runs one job exactly once across every API instance (task 041): <c>GET_LOCK('job:&lt;name&gt;', 0)</c> — not
/// obtained means another instance is running it, so this one skips silently. Records the run in ash_ops.job_runs.
/// </summary>
public sealed class JobRunner(IServiceScopeFactory scopes, PersistenceOptions persistence, ILogger<JobRunner> logger)
{
    private const int MaxErrorLength = 1000;

    public async Task<JobOutcome> RunAsync(string name, DateTime nowUtc, CancellationToken ct)
    {
        // A MySQL named lock belongs to the SESSION that took it. So the lock lives on ONE dedicated connection, open
        // for the whole run and never shared: not the unit of work's connection (EF opens and closes it per
        // transaction and the job opens several), and not pooled — a pooled connection goes back to the pool still
        // holding the lock if RELEASE_LOCK fails, and the next borrower would "own" it. Without pooling, closing the
        // connection ends the session and the server frees the lock whatever happened.
        var lockConnectionString = new MySqlConnectionStringBuilder(persistence.ConnectionString) { Pooling = false }.ConnectionString;
        await using var lockConnection = new MySqlConnection(lockConnectionString);
        await lockConnection.OpenAsync(ct);
        var lockName = $"job:{name}";
        if (!await TryLockAsync(lockConnection, lockName, ct))
        {
            logger.LogInformation("Job {Job} is running on another instance; skipped.", name);
            return JobOutcome.Skipped;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var job = scope.ServiceProvider.GetServices<IScheduledJob>().SingleOrDefault(j => j.Name == name)
                      ?? throw new InvalidOperationException($"No job named '{name}'.");
            await JobRunStore.MarkStartedAsync(lockConnection, name, nowUtc, ct);
            try
            {
                await job.RunAsync(nowUtc, ct);
            }
            // a missing table / procedure goes up to the scheduler, which says it once (not every run)
            catch (Exception ex) when (!JobSchedulerState.IsSchemaMissing(ex))
            {
                logger.LogError(ex, "Job {Job} failed.", name);
                var message = $"{ex.GetType().Name}: {ex.Message}";
                await JobRunStore.MarkFailedAsync(lockConnection, name, message[..Math.Min(message.Length, MaxErrorLength)], CancellationToken.None);
                return JobOutcome.Failed;
            }

            await JobRunStore.MarkSucceededAsync(lockConnection, name, nowUtc, ct);
            return JobOutcome.Succeeded;
        }
        finally
        {
            await using var release = new MySqlCommand("SELECT RELEASE_LOCK(@name)", lockConnection);
            release.Parameters.AddWithValue("@name", lockName);
            await release.ExecuteScalarAsync(CancellationToken.None);
        }
    }

    private static async Task<bool> TryLockAsync(MySqlConnection connection, string lockName, CancellationToken ct)
    {
        await using var command = new MySqlCommand("SELECT GET_LOCK(@name, 0)", connection);
        command.Parameters.AddWithValue("@name", lockName);
        return await command.ExecuteScalarAsync(ct) is { } value and not DBNull && Convert.ToInt32(value) == 1;
    }
}

/// <summary>ash_ops.job_runs (script 016): the last start, success and error of each job.</summary>
public static class JobRunStore
{
    public static async Task<IReadOnlyDictionary<string, JobRunState>> ReadAllAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand(
            "SELECT job_name, last_started_at, last_succeeded_at, last_error FROM ash_ops.job_runs", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var states = new Dictionary<string, JobRunState>();
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(0);
            states[name] = new JobRunState(name, Utc(reader, 1), Utc(reader, 2), reader.IsDBNull(3) ? null : reader.GetString(3));
        }

        return states;
    }

    internal static Task MarkStartedAsync(MySqlConnection connection, string name, DateTime nowUtc, CancellationToken ct) =>
        ExecuteAsync(connection, """
            INSERT INTO ash_ops.job_runs (job_name, last_started_at) VALUES (@name, @at)
            ON DUPLICATE KEY UPDATE last_started_at = @at
            """, name, nowUtc, null, ct);

    internal static Task MarkSucceededAsync(MySqlConnection connection, string name, DateTime nowUtc, CancellationToken ct) =>
        ExecuteAsync(connection,
            "UPDATE ash_ops.job_runs SET last_succeeded_at = @at, last_error = NULL WHERE job_name = @name", name, nowUtc, null, ct);

    internal static Task MarkFailedAsync(MySqlConnection connection, string name, string error, CancellationToken ct) =>
        ExecuteAsync(connection, "UPDATE ash_ops.job_runs SET last_error = @error WHERE job_name = @name", name, null, error, ct);

    private static async Task ExecuteAsync(
        MySqlConnection connection, string sql, string name, DateTime? at, string? error, CancellationToken ct)
    {
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@at", (object?)at ?? DBNull.Value);
        command.Parameters.AddWithValue("@error", (object?)error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static DateTime? Utc(System.Data.Common.DbDataReader reader, int i) =>
        reader.IsDBNull(i) ? null : DateTime.SpecifyKind(reader.GetDateTime(i), DateTimeKind.Utc);
}
