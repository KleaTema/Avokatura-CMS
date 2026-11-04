using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ash.SharedKernel.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Audit.Application.Digest;

/// <summary>
/// The connection of the digest job and the chain check: ConnectionStrings:AuditDigest — its own least-privilege account
/// (ash_audit_digest: SELECT on audit_log, SELECT + INSERT on audit_daily_digests; script 016 / LocalScripts 905). In
/// Development only, without it, the App connection (ash_dev has every right locally).
/// </summary>
public sealed class AuditDigestConnection(IConfiguration configuration, IHostEnvironment environment)
{
    public const string Name = "AuditDigest";

    public string Value =>
        configuration.GetConnectionString(Name) is { Length: > 0 } own ? own
        : environment.IsDevelopment() && configuration.GetConnectionString("App") is { Length: > 0 } app ? app
        : throw new InvalidOperationException($"ConnectionStrings:{Name} is not configured.");
}

/// <summary>One day's digest as stored in ash_audit.audit_daily_digests.</summary>
public sealed record DayDigest(DateOnly Day, long RowCount, long? FirstId, long? LastId, byte[] Digest);

/// <summary>
/// The daily audit digest (task 041). A day is a Tirane calendar day: its rows are those with occurred_at (UTC) in
/// [00:00 Tirane, next 00:00 Tirane). Digest = SHA-256( previous day's digest (32 zero bytes for the first day) ‖ the
/// day's rows ), the rows in canonical form: ORDER BY id; one line per row, UTF-8, ending in "\n"; the fields in this
/// fixed order, tab-separated: id, occurred_at (UTC ISO-8601 with milliseconds, "Z"), user_id, user_display,
/// role_codes, action, outcome, entity_type, entity_id, case_id, ip_address (lower hex), user_agent, correlation_id
/// (lower hex), details (the JSON text as MySQL returns it). NULL is "\N"; in text, \ → \\, tab → \t, LF → \n, CR → \r.
/// Changing ANY of this makes every stored digest unverifiable — never change it.
/// </summary>
public static class AuditChain
{
    public static readonly byte[] Genesis = new byte[32];

    public static (DateTime FromUtc, DateTime ToUtc) Range(DateOnly day) =>
        (TiraneTime.StartOfDayUtc(day), TiraneTime.StartOfDayUtc(day.AddDays(1)));

    /// <summary>Reads the day's rows in order and computes its digest on top of <paramref name="previous"/>.</summary>
    public static async Task<DayDigest> ComputeAsync(MySqlConnection connection, DateOnly day, byte[] previous, CancellationToken ct)
    {
        var (from, to) = Range(day);
        await using var command = new MySqlCommand("""
            SELECT id, occurred_at, user_id, user_display, role_codes, action, outcome, entity_type, entity_id, case_id,
                   ip_address, user_agent, correlation_id, details
            FROM ash_audit.audit_log
            WHERE occurred_at >= @from AND occurred_at < @to
            ORDER BY id
            """, connection);
        command.Parameters.AddWithValue("@from", from);
        command.Parameters.AddWithValue("@to", to);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(previous);
        long count = 0;
        long? first = null, last = null;
        var line = new StringBuilder();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = Convert.ToInt64(reader.GetValue(0));
            first ??= id;
            last = id;
            count++;
            line.Clear();
            line.Append(id.ToString(CultureInfo.InvariantCulture));
            line.Append('\t').Append(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc)
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            for (var i = 2; i < reader.FieldCount; i++)
            {
                line.Append('\t').Append(Field(reader, i));
            }

            line.Append('\n');
            hash.AppendData(Encoding.UTF8.GetBytes(line.ToString()));
        }

        return new DayDigest(day, count, first, last, hash.GetHashAndReset());
    }

    private static string Field(System.Data.Common.DbDataReader reader, int i)
    {
        if (reader.IsDBNull(i))
        {
            return "\\N";
        }

        return reader.GetValue(i) switch
        {
            byte[] bytes => Convert.ToHexStringLower(bytes),
            string text => Escape(text),
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            var other => Escape(other.ToString() ?? string.Empty),
        };
    }

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");

    public static Task<IReadOnlyList<DayDigest>> StoredAsync(MySqlConnection connection, DateOnly from, DateOnly to, CancellationToken ct) =>
        ReadAsync(connection, """
            SELECT audit_day, row_count, first_id, last_id, digest FROM ash_audit.audit_daily_digests
            WHERE audit_day BETWEEN @from AND @to ORDER BY audit_day
            """, from, to, ct);

    /// <summary>The newest stored digest before <paramref name="day"/>, or null (then the chain starts at Genesis).</summary>
    public static async Task<DayDigest?> LatestBeforeAsync(MySqlConnection connection, DateOnly day, CancellationToken ct) =>
        (await ReadAsync(connection, """
            SELECT audit_day, row_count, first_id, last_id, digest FROM ash_audit.audit_daily_digests
            WHERE audit_day < @to ORDER BY audit_day DESC LIMIT 1
            """, day, day, ct)).SingleOrDefault();

    private static async Task<IReadOnlyList<DayDigest>> ReadAsync(
        MySqlConnection connection, string sql, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@from", from.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@to", to.ToDateTime(TimeOnly.MinValue));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var days = new List<DayDigest>();
        while (await reader.ReadAsync(ct))
        {
            days.Add(new DayDigest(
                DateOnly.FromDateTime(reader.GetDateTime(0)),
                Convert.ToInt64(reader.GetValue(1)),
                reader.IsDBNull(2) ? null : Convert.ToInt64(reader.GetValue(2)),
                reader.IsDBNull(3) ? null : Convert.ToInt64(reader.GetValue(3)),
                (byte[])reader.GetValue(4)));
        }

        return days;
    }
}
