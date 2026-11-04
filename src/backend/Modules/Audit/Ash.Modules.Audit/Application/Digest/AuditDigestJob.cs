using Ash.Infrastructure.Jobs;
using Ash.SharedKernel.Time;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Audit.Application.Digest;

/// <summary>
/// Daily at 02:00 Tirane (task 041): digests every Tirane day not digested yet, up to yesterday — after downtime all
/// missed days, in order, each chained to the one before (<see cref="AuditChain"/>). The first day is the day after the
/// newest stored digest, or, with none, the day of the oldest audit row. A day without rows is digested too (row_count
/// 0), so the chain has no gaps. Each day is one INSERT (autocommit) on the digest account's connection.
/// </summary>
internal sealed class AuditDigestJob(AuditDigestConnection connectionString, ILogger<AuditDigestJob> logger) : IScheduledJob
{
    public const string JobName = "audit-digest";

    public string Name => JobName;

    public JobSchedule Schedule { get; } = JobSchedule.DailyAtTirane(2, 0);

    public async Task RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        var yesterday = TiraneTime.DateOf(nowUtc).AddDays(-1);
        await using var connection = new MySqlConnection(connectionString.Value);
        await connection.OpenAsync(ct);

        var latest = await AuditChain.LatestBeforeAsync(connection, yesterday.AddDays(1), ct);
        DateOnly day;
        if (latest is not null)
        {
            day = latest.Day.AddDays(1);
        }
        else if (await OldestRowAsync(connection, ct) is { } oldest)
        {
            day = TiraneTime.DateOf(oldest);
        }
        else
        {
            return; // nothing audited yet
        }

        var previous = latest?.Digest ?? AuditChain.Genesis;
        var written = 0;
        for (; day <= yesterday; day = day.AddDays(1))
        {
            var digest = await AuditChain.ComputeAsync(connection, day, previous, ct);
            await using var insert = new MySqlCommand("""
                INSERT INTO ash_audit.audit_daily_digests (audit_day, row_count, first_id, last_id, digest, computed_at)
                VALUES (@day, @count, @first, @last, @digest, @at)
                """, connection);
            insert.Parameters.AddWithValue("@day", day.ToDateTime(TimeOnly.MinValue));
            insert.Parameters.AddWithValue("@count", digest.RowCount);
            insert.Parameters.AddWithValue("@first", (object?)digest.FirstId ?? DBNull.Value);
            insert.Parameters.AddWithValue("@last", (object?)digest.LastId ?? DBNull.Value);
            insert.Parameters.AddWithValue("@digest", digest.Digest);
            insert.Parameters.AddWithValue("@at", nowUtc);
            await insert.ExecuteNonQueryAsync(ct);
            previous = digest.Digest;
            written++;
        }

        logger.LogInformation("Audit digest: {Count} day(s) digested, up to {Day:yyyy-MM-dd}.", written, yesterday);
    }

    private static async Task<DateTime?> OldestRowAsync(MySqlConnection connection, CancellationToken ct)
    {
        await using var command = new MySqlCommand("SELECT MIN(occurred_at) FROM ash_audit.audit_log", connection);
        return await command.ExecuteScalarAsync(ct) is DateTime at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null;
    }
}
