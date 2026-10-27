using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Modules.Reference.Contracts;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

public class ReferenceDataTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string User = "avokat1@ash.local";

    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _count);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(result);
        }
    }

    private AshApiFactory CreateFactory(CommandCounter? counter = null) =>
        new(Db, configureServices: services =>
        {
            if (counter is not null)
            {
                services.AddSingleton<IInterceptor>(counter);
            }
        });

    private static async Task<JsonElement> GetJson(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private static string[] Strings(JsonElement list, string property) =>
        list.EnumerateArray().Select(x => x.GetProperty(property).GetString()!).ToArray();

    private async Task Execute(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Endpoints_require_authentication()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/reference/courts", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Every_list_returns_seeded_values()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().AsUser(User);

        Strings(await GetJson(client, "/api/reference/court-levels"), "code").ShouldContain("FIRST_INSTANCE");
        var courts = await GetJson(client, "/api/reference/courts");
        Strings(courts, "name").ShouldContain("Gjykata e Lartë");
        var levelByCourt = courts.EnumerateArray().ToDictionary(
            x => x.GetProperty("name").GetString()!, x => x.GetProperty("courtLevelCode").GetString());
        levelByCourt["Gjykata e Shkallës së Parë Tiranë"].ShouldBe("FIRST_INSTANCE");
        levelByCourt["Gjykata e Shkallës së Parë Durrës"].ShouldBe("FIRST_INSTANCE");
        levelByCourt["Gjykata e Apelit Tiranë"].ShouldBe("APPEAL");
        levelByCourt["Gjykata e Apelit Shkodër"].ShouldBe("APPEAL");
        levelByCourt["Gjykata e Lartë"].ShouldBe("HIGH_COURT");
        Strings(await GetJson(client, "/api/reference/party-roles"), "code").ShouldContain("PADITES");
        (await GetJson(client, "/api/reference/summons-types")).ValueKind.ShouldBe(JsonValueKind.Array);
        (await GetJson(client, "/api/reference/hearing-types")).GetArrayLength().ShouldBeGreaterThan(0);
        (await GetJson(client, "/api/reference/document-types")).GetArrayLength().ShouldBeGreaterThan(0);
        Strings(await GetJson(client, "/api/reference/decision-results"), "code").ShouldContain("KTHIM_RIGJYKIM");
        Strings(await GetJson(client, "/api/reference/case-categories"), "code").ShouldNotBeEmpty();
        Strings(await GetJson(client, "/api/reference/case-subcategories"), "code").ShouldNotBeEmpty();
        (await GetJson(client, "/api/reference/allowed-file-types")).GetArrayLength().ShouldBeGreaterThan(0);
        (await GetJson(client, "/api/reference/hearing-types")).EnumerateArray().First().TryGetProperty("isOther", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Inactive_rows_are_hidden_from_lists_but_returned_by_Get()
    {
        await Execute("INSERT INTO courts (name, court_level_id, is_active) " +
                      "SELECT 'Gjykata e Mbyllur Test', id, 0 FROM court_levels WHERE code = 'FIRST_INSTANCE'");
        await Execute("INSERT INTO hearing_types (code, name, is_active) VALUES ('TEST_INACTIVE', 'Seancë e pasaktë', 0)");
        try
        {
            await using var factory = CreateFactory();
            using var client = factory.CreateClient().AsUser(User);

            Strings(await GetJson(client, "/api/reference/courts"), "name").ShouldNotContain("Gjykata e Mbyllur Test");
            Strings(await GetJson(client, "/api/reference/hearing-types"), "code").ShouldNotContain("TEST_INACTIVE");

            await using var scope = factory.Services.CreateAsyncScope();
            var data = scope.ServiceProvider.GetRequiredService<IReferenceData>();
            var hearingType = await data.GetHearingType("TEST_INACTIVE", TestContext.Current.CancellationToken);
            hearingType.ShouldNotBeNull();
            hearingType.IsActive.ShouldBeFalse();
            var court = (await data.ListCourtsAsync(TestContext.Current.CancellationToken)).Single(x => x.Name == "Gjykata e Mbyllur Test");
            (await data.GetCourt(court.Id, TestContext.Current.CancellationToken)).ShouldNotBeNull();
            (await data.GetResultByCode("KTHIM_RIGJYKIM", TestContext.Current.CancellationToken)).ShouldNotBeNull();
            (await data.GetCourtLevelByCode("FIRST_INSTANCE", TestContext.Current.CancellationToken))!.AllowsDelegation.ShouldBeTrue();
        }
        finally
        {
            await Execute("DELETE FROM courts WHERE name = 'Gjykata e Mbyllur Test'");
            await Execute("DELETE FROM hearing_types WHERE code = 'TEST_INACTIVE'");
        }
    }

    [Theory]
    [InlineData("cela", "Çela")]
    [InlineData("Çela", "Çela")]
    [InlineData("deshira", "Dëshira")]
    [InlineData("Dëshira", "Dëshira")]
    [InlineData("klodiana", "Çela")]
    [InlineData("arber", "Dëshira")]
    public async Task Judge_search_works_with_and_without_diacritics(string q, string expectedLastName)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().AsUser(User);

        var result = await GetJson(client, $"/api/reference/judges?q={Uri.EscapeDataString(q)}");

        Strings(result, "lastName").ShouldContain(expectedLastName);
    }

    [Fact]
    public async Task Judge_search_filters_by_court_and_limits_results()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().AsUser(User);

        var courts = await GetJson(client, "/api/reference/courts");
        var tirana = courts.EnumerateArray().Single(x => x.GetProperty("name").GetString() == "Gjykata e Shkallës së Parë Tiranë")
            .GetProperty("id").GetInt32();

        var result = await GetJson(client, $"/api/reference/judges?courtId={tirana}");

        var names = Strings(result, "lastName");
        names.ShouldContain("Gjyqtari");
        names.ShouldContain("Çela");
        names.ShouldContain("Dëshira");
        result.EnumerateArray().All(x => x.GetProperty("courtId").GetInt32() == tirana).ShouldBeTrue();
        (await GetJson(client, "/api/reference/judges")).GetArrayLength().ShouldBeLessThanOrEqualTo(50);
    }

    [Fact]
    public async Task Second_call_within_cache_lifetime_runs_no_sql()
    {
        var counter = new CommandCounter();
        await using var factory = CreateFactory(counter);
        using var client = factory.CreateClient().AsUser(User);

        await GetJson(client, "/api/reference/courts");
        var afterFirst = counter.Count;
        afterFirst.ShouldBeGreaterThan(0);

        await GetJson(client, "/api/reference/courts");
        await GetJson(client, "/api/reference/judges?q=cela");
        await GetJson(client, "/api/reference/judges?q=cela");

        // courts needs court_levels + courts on first call; the judges list loads once, nothing else may hit the database
        var afterJudgesLoad = counter.Count;
        await GetJson(client, "/api/reference/courts");
        await GetJson(client, "/api/reference/judges?q=deshira");
        counter.Count.ShouldBe(afterJudgesLoad);
    }

    [Fact]
    public async Task Invalidate_forces_a_reload()
    {
        var counter = new CommandCounter();
        await using var factory = CreateFactory(counter);
        using var client = factory.CreateClient().AsUser(User);

        await GetJson(client, "/api/reference/courts");
        var before = counter.Count;

        factory.Services.GetRequiredService<IReferenceCache>().Invalidate();
        await GetJson(client, "/api/reference/courts");

        counter.Count.ShouldBeGreaterThan(before);
    }
}
