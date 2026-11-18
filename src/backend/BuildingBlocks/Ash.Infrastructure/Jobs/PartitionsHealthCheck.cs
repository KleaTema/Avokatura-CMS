using Ash.Infrastructure.Persistence;
using Ash.SharedKernel.Time;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ash.Infrastructure.Jobs;

/// <summary>
/// Health check "partitions" (task 053n: moved off the administrator's dashboard to IT operations): unhealthy when
/// ash_chat.messages or ash_audit.audit_log has rows in p_max (the monthly rotation stopped) or no partition for next
/// month — <see cref="PartitionStatus"/>, the same check as the daily PartitionCheckJob (which also logs and notifies).
/// Readiness is polled often: the answer is kept for <see cref="CacheFor"/>. A singleton (registered by the host).
/// </summary>
public sealed class PartitionsHealthCheck(PersistenceOptions persistence, IClock clock) : IHealthCheck
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTime At, HealthCheckResult Result)? _last;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = clock.UtcNow;
            if (_last is { } last && now - last.At < CacheFor)
            {
                return last.Result;
            }

            var result = await CheckAsync(now, cancellationToken);
            _last = (now, result);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HealthCheckResult> CheckAsync(DateTime now, CancellationToken ct)
    {
        IReadOnlyList<PartitionState> tables;
        try
        {
            tables = await PartitionStatus.ReadAsync(persistence.ConnectionString, now, ct);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("ash_ops.partition_status cannot be called (run the migrator?).", ex);
        }

        var problems = tables.Where(t => !t.Ok)
            .Select(t => $"{t.Table}: {(t.PMaxHasRows ? "P_MAX_ROWS" : "")}{(t.PMaxHasRows && !t.NextMonthReady ? ", " : "")}{(t.NextMonthReady ? "" : "NEXT_MONTH_MISSING")}")
            .ToList();
        return problems.Count == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"Partitions: {string.Join("; ", problems)}.");
    }
}
