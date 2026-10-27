using System.Text;
using System.Threading.Channels;
using Ash.SharedKernel.Ids;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Audit.Infrastructure;

/// <summary>
/// Queues audit entries in a bounded channel and inserts them in batches through its own
/// insert-only connection (ConnectionStrings:Audit). Failures are logged, never thrown to callers.
/// </summary>
public sealed class AuditWriter(IConfiguration configuration, ILogger<AuditWriter> logger) : BackgroundService
{
    private const int Capacity = 10_000;
    private const int MaxBatchSize = 200;
    private static readonly TimeSpan BatchDelay = TimeSpan.FromMilliseconds(250);

    private readonly Channel<AuditEntry> _channel = Channel.CreateBounded<AuditEntry>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public void Enqueue(AuditEntry entry)
    {
        if (!_channel.Writer.TryWrite(entry))
        {
            logger.LogError("Audit queue is full or closed; dropped {Action} entry.", entry.Action);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(stoppingToken))
            {
                await Task.Delay(BatchDelay, stoppingToken);
                await FlushAvailableAsync(CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // host is stopping
        }

        _channel.Writer.TryComplete();
        await FlushAvailableAsync(CancellationToken.None);
    }

    private async Task FlushAvailableAsync(CancellationToken ct)
    {
        var batch = new List<AuditEntry>(MaxBatchSize);
        while (true)
        {
            batch.Clear();
            while (batch.Count < MaxBatchSize && _channel.Reader.TryRead(out var entry))
            {
                batch.Add(entry);
            }

            if (batch.Count == 0)
            {
                return;
            }

            await InsertAsync(batch, ct);
        }
    }

    private async Task InsertAsync(List<AuditEntry> batch, CancellationToken ct)
    {
        try
        {
            var connectionString = configuration.GetConnectionString("Audit");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("ConnectionStrings:Audit is not configured.");
            }

            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();

            var sql = new StringBuilder(
                "INSERT INTO ash_audit.audit_log (occurred_at, user_id, user_display, role_codes, action, outcome, " +
                "entity_type, entity_id, case_id, ip_address, user_agent, correlation_id, details) VALUES ");

            for (var i = 0; i < batch.Count; i++)
            {
                var e = batch[i];
                if (i > 0)
                {
                    sql.Append(',');
                }

                sql.Append($"(@t{i},@u{i},@d{i},@r{i},@a{i},@o{i},@et{i},@ei{i},@c{i},INET6_ATON(@ip{i}),@ua{i},@cid{i},@j{i})");

                command.Parameters.AddWithValue($"@t{i}", e.OccurredAt);
                command.Parameters.AddWithValue($"@u{i}", (object?)e.UserId ?? DBNull.Value);
                command.Parameters.AddWithValue($"@d{i}", (object?)Truncate(e.UserDisplay, 200) ?? DBNull.Value);
                command.Parameters.AddWithValue($"@r{i}", (object?)Truncate(e.RoleCodes, 200) ?? DBNull.Value);
                command.Parameters.AddWithValue($"@a{i}", Truncate(e.Action, 60));
                command.Parameters.AddWithValue($"@o{i}", Truncate(e.Outcome, 10));
                command.Parameters.AddWithValue($"@et{i}", (object?)Truncate(e.EntityType, 40) ?? DBNull.Value);
                command.Parameters.AddWithValue($"@ei{i}", (object?)e.EntityId ?? DBNull.Value);
                command.Parameters.AddWithValue($"@c{i}", (object?)e.CaseId ?? DBNull.Value);
                command.Parameters.AddWithValue($"@ip{i}", (object?)e.IpAddress ?? DBNull.Value);
                command.Parameters.AddWithValue($"@ua{i}", (object?)Truncate(e.UserAgent, 300) ?? DBNull.Value);
                command.Parameters.AddWithValue($"@cid{i}", e.CorrelationId is { } id ? PublicId.ToBytes(id) : DBNull.Value);
                command.Parameters.AddWithValue($"@j{i}", (object?)e.DetailsJson ?? DBNull.Value);
            }

            command.CommandText = sql.ToString();
            await command.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write {Count} audit entries; they were dropped.", batch.Count);
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is { Length: > 0 } && value.Length > max ? value[..max] : value;
}
