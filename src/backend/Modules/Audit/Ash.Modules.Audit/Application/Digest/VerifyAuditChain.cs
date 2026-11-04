using Ash.SharedKernel.Results;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Audit.Application.Digest;

/// <summary>
/// The answer of GET /api/admin/audit/verify: Ok when every day of the range has a digest that equals the one
/// recomputed from today's audit rows; otherwise the first day that does not (a changed, removed or added row, a
/// changed digest, or a missing digest) and why.
/// </summary>
public sealed record AuditChainVerification(bool Ok, DateOnly From, DateOnly To, int CheckedDays, DateOnly? FirstBrokenDay, string? Reason);

/// <summary>
/// Recomputes the chain for a date range (task 041). Each day is recomputed on top of the STORED digest of the day
/// before, so the first broken day is the one whose own rows (or digest) changed — not every later day.
/// </summary>
public sealed class VerifyAuditChain(AuditDigestConnection connectionString)
{
    public const int MaxDays = 366;

    public async Task<Result<AuditChainVerification>> VerifyAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber + 1 > MaxDays)
        {
            return Error.ValidationFields("Periudha nuk është e vlefshme.",
                new Dictionary<string, string[]> { ["to"] = [$"Data e mbarimit duhet të jetë pas fillimit, për jo më shumë se {MaxDays} ditë."] });
        }

        await using var connection = new MySqlConnection(connectionString.Value);
        await connection.OpenAsync(ct);
        var stored = (await AuditChain.StoredAsync(connection, from, to, ct)).ToDictionary(d => d.Day);
        var previous = (await AuditChain.LatestBeforeAsync(connection, from, ct))?.Digest ?? AuditChain.Genesis;

        var checkedDays = 0;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (!stored.TryGetValue(day, out var digest))
            {
                return new AuditChainVerification(false, from, to, checkedDays, day, "Mungon gjurma e ditës.");
            }

            var recomputed = await AuditChain.ComputeAsync(connection, day, previous, ct);
            checkedDays++;
            if (!recomputed.Digest.AsSpan().SequenceEqual(digest.Digest) || recomputed.RowCount != digest.RowCount)
            {
                return new AuditChainVerification(false, from, to, checkedDays, day,
                    "Rreshtat e auditimit të kësaj dite nuk përputhen me gjurmën e ruajtur.");
            }

            previous = digest.Digest;
        }

        return new AuditChainVerification(true, from, to, checkedDays, null, null);
    }
}
