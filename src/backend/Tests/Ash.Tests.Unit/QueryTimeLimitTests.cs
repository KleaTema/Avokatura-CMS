using Ash.Infrastructure.Persistence;
using Shouldly;

namespace Ash.Tests.Unit;

/// <summary>Task 046 follow-up: which statements get MySQL's MAX_EXECUTION_TIME hint.</summary>
public class QueryTimeLimitTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    [Fact]
    public void A_select_gets_the_hint_right_after_the_keyword()
    {
        QueryTimeLimitInterceptor.Apply("SELECT `c`.`id` FROM `cases` AS `c`", Limit)
            .ShouldBe("SELECT /*+ MAX_EXECUTION_TIME(5000) */ `c`.`id` FROM `cases` AS `c`");
        QueryTimeLimitInterceptor.Apply("  select 1", Limit).ShouldBe("  select /*+ MAX_EXECUTION_TIME(5000) */ 1");
    }

    [Fact]
    public void Writes_and_other_statements_are_never_limited()
    {
        foreach (var sql in new[]
                 {
                     "INSERT INTO notifications (title) VALUES (@p0); SELECT LAST_INSERT_ID();",
                     "UPDATE cases SET priority = 'HIGH' WHERE id = 1",
                     "DELETE FROM notifications WHERE id = 1",
                     "WITH x AS (SELECT 1) SELECT * FROM x",
                     "CALL ash_ops.partition_status(@end)",
                 })
        {
            QueryTimeLimitInterceptor.Apply(sql, Limit).ShouldBe(sql);
        }
    }

    [Fact]
    public void A_tagged_query_gets_its_own_limit_and_a_hint_is_never_added_twice()
    {
        var tagged = "-- ash:max_execution_time=60000\n\nSELECT COUNT(*) FROM cases";
        QueryTimeLimitInterceptor.Apply(tagged, Limit).ShouldBe("-- ash:max_execution_time=60000\n\nSELECT /*+ MAX_EXECUTION_TIME(60000) */ COUNT(*) FROM cases");
        var limited = "SELECT /*+ MAX_EXECUTION_TIME(100) */ 1";
        QueryTimeLimitInterceptor.Apply(limited, Limit).ShouldBe(limited);
    }
}
