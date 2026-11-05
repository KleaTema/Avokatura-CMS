using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MySql.Data.MySqlClient;

namespace Ash.Infrastructure.Persistence;

/// <summary>
/// The time limit of read queries (task 046 follow-up): every SELECT that EF sends gets the optimizer hint
/// <c>/*+ MAX_EXECUTION_TIME(ms) */</c>, so MySQL itself stops it (error 3024) — never a request that hangs: MySql.Data
/// does not interrupt a running statement when a CancellationToken fires. Writes (INSERT / UPDATE / DELETE) are never
/// limited: MySQL applies the hint to read-only SELECTs only, and this adds it to nothing else. EF's CommandTimeout
/// (the limit + 5 s) is the backstop. A query may ask for more (reports, exports): <see cref="WithTimeLimit{T}"/>.
/// The API answers a stopped query with 503 "Kërkimi zgjati shumë. Provoni me më shumë filtra." (ApiExceptionHandler).
/// </summary>
public sealed partial class QueryTimeLimitInterceptor(TimeSpan limit) : DbCommandInterceptor
{
    public const string TagPrefix = "ash:max_execution_time=";

    /// <summary>MySQL's error: "maximum statement execution time exceeded".</summary>
    public const int TimeoutErrorNumber = 3024;

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Limit(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Limit(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Limit(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Limit(command);
        return ValueTask.FromResult(result);
    }

    /// <summary>The hint after the first SELECT keyword — only when the statement IS a SELECT (EF's tags are "-- …" lines before it).</summary>
    public void Limit(DbCommand command) => command.CommandText = Apply(command.CommandText, limit);

    public static string Apply(string sql, TimeSpan limit)
    {
        var milliseconds = TagRegex().Match(sql) is { Success: true } tag ? long.Parse(tag.Groups[1].Value) : (long)limit.TotalMilliseconds;
        var select = LeadingSelectRegex().Match(sql);
        // an existing hint (not the tag, which also says "max_execution_time")
        if (!select.Success || ExistingHintRegex().IsMatch(sql))
        {
            return sql;
        }

        var at = select.Index + select.Length;
        return string.Concat(sql.AsSpan(0, at), $" /*+ MAX_EXECUTION_TIME({milliseconds}) */", sql.AsSpan(at));
    }

    /// <summary>True for the error of a stopped query (3024) or the client-side backstop timing out.</summary>
    public static bool IsTimeout(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is MySqlException { Number: TimeoutErrorNumber } || e is TimeoutException
                || e is MySqlException mysql && mysql.Message.Contains("Timeout expired", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // "-- comment" lines (EF tags) and blank lines, then SELECT as the first keyword
    [GeneratedRegex(@"\A(?:\s*--[^\n]*\n)*\s*SELECT\b", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingSelectRegex();

    [GeneratedRegex(@"/\*\+[^*]*MAX_EXECUTION_TIME\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex ExistingHintRegex();

    [GeneratedRegex(@"--\s*ash:max_execution_time=(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex TagRegex();
}

public static class QueryTimeLimitExtensions
{
    /// <summary>A longer (or shorter) limit for this one query, e.g. a report or an export.</summary>
    public static IQueryable<T> WithTimeLimit<T>(this IQueryable<T> query, TimeSpan limit) =>
        query.TagWith($"{QueryTimeLimitInterceptor.TagPrefix}{(long)limit.TotalMilliseconds}");
}
