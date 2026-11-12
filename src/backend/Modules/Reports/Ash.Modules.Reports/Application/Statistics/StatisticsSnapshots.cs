using Ash.Infrastructure.Jobs;
using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reports.Infrastructure;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.Extensions.Logging;

namespace Ash.Modules.Reports.Application.Statistics;

/// <summary>
/// Freezes a closed year's statistics (task 044): its per-case base flags (StatisticsLive.sql at the year's end) into
/// statistics_snapshot_cases, in the CORE unit of work (the API's account may write those two tables; the reporting
/// account only reads). A re-run replaces the year's rows in the same transaction. From then on the year reads the
/// frozen rows: a category or a suspension changed later does not change it.
/// TODO(client-q-stat-freeze): confirm that published yearly figures must never change.
/// </summary>
public sealed class StatisticsSnapshots(IUnitOfWork unitOfWork, IAuditLog audit, ILogger<StatisticsSnapshots> logger)
{
    public async Task<int> TakeAsync(int year, long? byUserId, CancellationToken ct)
    {
        var own = !unitOfWork.HasActiveTransaction;
        if (own)
        {
            await unitOfWork.BeginAsync(ct);
        }

        try
        {
            await using (var clear = unitOfWork.CreateCommand("DELETE FROM statistics_snapshots WHERE year = @year"))
            {
                clear.Parameters.AddWithValue("@year", year);
                await clear.ExecuteNonQueryAsync(ct);
            }

            await using (var header = unitOfWork.CreateCommand(
                             "INSERT INTO statistics_snapshots (year, case_count, created_by) VALUES (@year, 0, @by)"))
            {
                header.Parameters.AddWithValue("@year", year);
                header.Parameters.AddWithValue("@by", (object?)byUserId ?? DBNull.Value);
                await header.ExecuteNonQueryAsync(ct);
            }

            int count;
            await using (var insert = unitOfWork.CreateCommand(StatisticsSql.SnapshotInsert))
            {
                var end = TiraneTime.StartOfDayUtc(new DateOnly(year + 1, 1, 1));
                insert.Parameters.AddWithValue("@year", year);
                insert.Parameters.AddWithValue("@y_start", TiraneTime.StartOfDayUtc(new DateOnly(year, 1, 1)));
                insert.Parameters.AddWithValue("@y_end", end);
                insert.Parameters.AddWithValue("@point", end);
                insert.CommandTimeout = 300;
                count = await insert.ExecuteNonQueryAsync(ct);
            }

            await using (var total = unitOfWork.CreateCommand("UPDATE statistics_snapshots SET case_count = @count WHERE year = @year"))
            {
                total.Parameters.AddWithValue("@count", count);
                total.Parameters.AddWithValue("@year", year);
                await total.ExecuteNonQueryAsync(ct);
            }

            if (own)
            {
                await unitOfWork.CommitAsync(ct);
            }

            logger.LogInformation("Statistics of {Year} frozen: {Count} cases.", year, count);
            return count;
        }
        catch
        {
            if (own)
            {
                await unitOfWork.RollbackAsync(CancellationToken.None);
            }

            throw;
        }
    }

    public async Task<bool> ExistsAsync(int year, CancellationToken ct)
    {
        await using var connection = new MySql.Data.MySqlClient.MySqlConnection(unitOfWork.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new MySql.Data.MySqlClient.MySqlCommand("SELECT COUNT(*) FROM statistics_snapshots WHERE year = @year", connection);
        command.Parameters.AddWithValue("@year", year);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct)) > 0;
    }

    public const int MinReasonLength = 10;

    /// <summary>
    /// POST /api/admin/statistics/snapshot/{year} { reason }: admin only, a CLOSED year, with a reason of at least
    /// 10 characters — kept in the audit row with the year and the case count (a re-run replaces the published figures).
    /// </summary>
    public async Task<Result<int>> TakeByAdminAsync(int year, string? reason, ICurrentUser user, IClock clock, CancellationToken ct)
    {
        var thisYear = TiraneTime.Today(clock).Year;
        if (year < 2000 || year >= thisYear)
        {
            return Error.Validation("statistics.snapshot_year", $"Mund të ngrihet vetëm një vit i mbyllur (para {thisYear}).");
        }

        var why = reason?.Trim() ?? string.Empty;
        if (why.Length < MinReasonLength)
        {
            return Error.Validation("statistics.snapshot_reason",
                $"Shkruani arsyen e ngrirjes (të paktën {MinReasonLength} shenja).");
        }

        var replaced = await ExistsAsync(year, ct);
        var count = await TakeAsync(year, user.UserId, ct);
        audit.Record(AuditActions.AdminChange, "STATISTICS_SNAPSHOT", year, details: new
        {
            action = replaced ? "STATISTICS_SNAPSHOT_REPLACED" : "STATISTICS_SNAPSHOT_TAKEN",
            year,
            cases = count,
            reason = why,
        });
        return count;
    }
}

/// <summary>
/// The yearly freeze (task 044, a 041 job): every day at 00:30 Tirane it makes sure the year that just ended has a
/// snapshot — on 1 January it takes it; after downtime it catches up; otherwise it does nothing (idempotent).
/// </summary>
internal sealed class StatisticsSnapshotJob(StatisticsSnapshots snapshots) : IScheduledJob
{
    public string Name => "statistics-snapshot";

    public JobSchedule Schedule => JobSchedule.DailyAtTirane(0, 30);

    public async Task RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        var lastYear = TiraneTime.DateOf(nowUtc).Year - 1;
        if (!await snapshots.ExistsAsync(lastYear, ct))
        {
            await snapshots.TakeAsync(lastYear, null, ct);
        }
    }
}
