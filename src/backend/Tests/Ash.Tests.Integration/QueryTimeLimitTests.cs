using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Modules.Cases.Infrastructure;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 046 follow-up: every EF SELECT carries MySQL's MAX_EXECUTION_TIME (5 s); a query MySQL stops is a 503 with an
/// Albanian title — within the limit, never a hang.
/// </summary>
public class QueryTimeLimitTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string SlowPath = "/__test/slow-query";

    /// <summary>
    /// TEST-ONLY endpoint: a SELECT through EF that runs far longer than 5 s (a triple cross join of
    /// information_schema.columns). SLEEP() would not do: MySQL ends it at the limit and returns 1 without an error.
    /// </summary>
    private sealed class SlowQueryEndpoint : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            // after the app's pipeline (inside its exception handler): only a request no endpoint took gets here
            app.Use(async (context, following) =>
            {
                if (context.Request.Path != SlowPath)
                {
                    await following(context);
                    return;
                }

                var cases = context.RequestServices.GetRequiredService<CasesDbContext>();
                var count = await cases.Database.SqlQueryRaw<long>(
                        "SELECT COUNT(*) AS Value FROM information_schema.columns a, information_schema.columns b, information_schema.columns c")
                    .SingleAsync(context.RequestAborted);
                await context.Response.WriteAsJsonAsync(new { count });
            });
        };
    }

    [Fact]
    public async Task A_select_that_runs_too_long_is_stopped_by_MySQL_and_answered_503_within_the_limit()
    {
        await using var factory = new AshApiFactory(Db, configureServices: services => services.AddSingleton<IStartupFilter, SlowQueryEndpoint>());
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");
        // warm up (host start, first-request JIT, the user's login): the timer measures the slow query only
        (await client.GetAsync("/api/notifications/unread-count", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var watch = Stopwatch.StartNew();
        // on another thread with its own limit: whatever happens, this test cannot hang
        var response = await Task.Run(() => client.GetAsync(SlowPath, TestContext.Current.CancellationToken))
            .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        watch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"stopped after {watch.ElapsedMilliseconds} ms");
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(7));
        watch.Elapsed.ShouldBeGreaterThan(TimeSpan.FromSeconds(4)); // the 5 s limit, not some other failure
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("title").GetString().ShouldBe("Kërkimi zgjati shumë. Provoni me më shumë filtra.");
        problem.GetProperty("code").GetString().ShouldBe("query.timeout");

        // the connection and the next requests are fine
        (await client.GetAsync("/api/notifications/unread-count", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
