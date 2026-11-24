using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.DemoSeed;
using Ash.Infrastructure.Jobs;
using Ash.SharedKernel.Time;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// The dev-only demo seed (src/backend/Tools/Ash.DemoSeed): on an empty database it makes the presentation's data
/// through the API only — the expected counts, a history on every case, every bell at 0, an audit chain that verifies;
/// and it refuses to run outside Development, off localhost or on a database that has cases.
/// </summary>
public class DemoSeedTests(MySqlFixture db, MinioFixture minio) : IntegrationTest(db), IClassFixture<MinioFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<long> Count(string sql)
    {
        await using var connection = new MySqlConnection(Db.RootConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand($"SELECT COUNT(*) {sql}", connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct));
    }

    [Fact]
    public async Task On_an_empty_database_the_seed_makes_the_presentation_data_and_the_audit_chain_verifies()
    {
        (await Count("FROM ash_core.cases")).ShouldBe(0L);
        (await DemoSeedGuard.EmptyAsync(Db.AppConnectionString)).ShouldBeNull();

        var clock = new DemoClock();
        await using var factory = new AshApiFactory(Db, storage: minio.Options,
            configureServices: services => DemoSeedServices.Configure(services, clock));
        var result = await DemoSeedProgram.SeedAsync(factory, clock, TextWriter.Null);

        // 14 first registrations (10 TR, 4 DR) + the continued X/2
        result.Cases.Count.ShouldBe(15);
        (await Count("FROM ash_core.cases")).ShouldBe(15L);
        (await Count("FROM ash_core.cases c JOIN ash_core.offices o ON o.id = c.office_id WHERE o.code = 'TR'")).ShouldBe(11L);
        (await Count("FROM ash_core.cases c JOIN ash_core.offices o ON o.id = c.office_id WHERE o.code = 'DR'")).ShouldBe(4L);
        (await Count("FROM ash_core.cases WHERE lifecycle_status = 'CLOSED'")).ShouldBe(3L);
        (await Count("FROM ash_core.cases WHERE lifecycle_status = 'REGISTERED'")).ShouldBe(2L);
        (await Count("FROM ash_core.dossiers WHERE archived_at IS NOT NULL")).ShouldBe(1L);
        (await Count("FROM ash_core.cases WHERE level_seq = 2")).ShouldBe(1L);
        (await Count("FROM ash_core.case_institutions WHERE link_mode = 'DELEGATED' AND ended_at IS NULL")).ShouldBe(1L);
        (await Count("FROM ash_core.case_assignments WHERE case_role = 'LEGAL_ASSISTANT' AND ended_at IS NULL")).ShouldBe(2L);
        (await Count("FROM ash_core.case_assignments WHERE case_role = 'INSTITUTION_JURIST' AND ended_at IS NULL")).ShouldBe(1L);
        (await Count("FROM ash_core.case_decisions")).ShouldBe(3L);
        (await Count("FROM ash_core.documents WHERE scan_status = 'CLEAN'")).ShouldBe(await Count("FROM ash_core.documents"));
        (await Count("FROM ash_core.documents WHERE content_type LIKE '%wordprocessingml%'")).ShouldBe(1L);

        // registrations spread over Aug–Nov 2026 on the controlled clock
        (await Count("FROM ash_core.cases WHERE registered_at < '2026-09-01'")).ShouldBeGreaterThan(0L);
        (await Count("FROM ash_core.dossiers WHERE opened_at < '2026-09-01'")).ShouldBeGreaterThan(0L); // a column default: SET TIMESTAMP

        // every case has history; every bell starts at 0; no statistics year frozen
        (await Count("FROM ash_core.cases c WHERE NOT EXISTS (SELECT 1 FROM ash_core.case_history_events h WHERE h.case_id = c.id)")).ShouldBe(0L);
        (await Count("FROM ash_core.notifications")).ShouldBeGreaterThan(0L);
        (await Count("FROM ash_core.notifications WHERE read_at IS NULL")).ShouldBe(0L);
        (await Count("FROM ash_core.statistics_snapshot_cases")).ShouldBe(0L);

        // the prepared cases of the live demo
        var byLabel = result.Cases.ToDictionary(c => c.Label);
        byLabel["E"].Primary.ShouldBe("avokat1@ash.local");
        byLabel["F"].Primary.ShouldBe("avokat1@ash.local");
        byLabel["D"].Office.ShouldBe("DR");
        byLabel["X/2"].Code.ShouldEndWith("/2");
        DemoSeedTable.Render(result).ShouldContain(byLabel["DELEGATED"].Code);

        // the audit chain: the digests of every day, then verify = ok
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<JobRunner>();
            (await runner.RunAsync("audit-digest", result.FinishedUtc.AddDays(1), Ct)).ShouldBe(JobOutcome.Succeeded);
        }

        using var admin = factory.CreateClient().AsUser("admin@ash.local");
        var from = TiraneTime.DateOf(result.StartedUtc);
        var to = TiraneTime.DateOf(result.FinishedUtc);
        var verify = await admin.GetAsync($"/api/admin/audit/verify?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", Ct);
        verify.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await verify.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("ok").GetBoolean().ShouldBeTrue(body.ToString());
    }

    [Fact]
    public async Task The_seed_refuses_outside_Development_off_localhost_and_on_a_database_with_cases()
    {
        foreach (var environment in new[] { "Production", "Staging", null })
        {
            var output = new StringWriter();
            (await DemoSeedProgram.RunAsync(environment, output)).ShouldBe(1);
            output.ToString().ShouldContain("ASPNETCORE_ENVIRONMENT=Development");
        }

        DemoSeedGuard.Connection("Server=db.ash.gov.al;Database=ash_core;User=x;Password=y").ShouldNotBeNull();
        DemoSeedGuard.Connection("Server=localhost;Database=ash_core;User=x;Password=y").ShouldBeNull();

        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        (await DemoSeedGuard.EmptyAsync(Db.AppConnectionString)).ShouldNotBeNull().ShouldContain(DemoSeedProgram.EmptyDatabaseHint);
    }
}
