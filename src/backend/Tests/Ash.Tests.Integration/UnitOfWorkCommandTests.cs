using System.Net;
using Ash.Infrastructure;
using Ash.Infrastructure.Persistence;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>IUnitOfWork.CreateCommand: raw SQL inside a request commits or rolls back with the request.</summary>
public class UnitOfWorkCommandTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string CommitPath = "/api/test/uow/command/commit";
    private const string RollbackPath = "/api/test/uow/command/rollback";
    private const string EndpointBuilderKey = "__EndpointRouteBuilder";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Test-only endpoints in the host's real pipeline: insert through CreateCommand, then answer 200 or 400.</summary>
    private sealed class CommandEndpointsStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseRouting();
            var routes = (IEndpointRouteBuilder)app.Properties[EndpointBuilderKey]!;
            routes.MapPost(CommitPath, (string name, IUnitOfWork uow) => Insert(uow, name, StatusCodes.Status200OK)).AllowAnonymous();
            routes.MapPost(RollbackPath, (string name, IUnitOfWork uow) => Insert(uow, name, StatusCodes.Status400BadRequest)).AllowAnonymous();
            next(app);
        };

        private static async Task<IResult> Insert(IUnitOfWork uow, string name, int status)
        {
            await using var command = uow.CreateCommand(
                "INSERT INTO institutions (name, institution_type) VALUES (@n, 'OTHER')");
            command.Parameters.AddWithValue("@n", name);
            await command.ExecuteNonQueryAsync();
            return Results.StatusCode(status);
        }
    }

    private async Task<long> CountInstitutions(string name)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand("SELECT COUNT(*) FROM institutions WHERE name = @n", connection);
        command.Parameters.AddWithValue("@n", name);
        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct));
    }

    private async Task DeleteInstitutions(string name)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = new MySqlCommand("DELETE FROM institutions WHERE name = @n", connection);
        command.Parameters.AddWithValue("@n", name);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Command_insert_is_rolled_back_with_a_failed_request_and_kept_with_a_successful_one()
    {
        var rolledBack = $"UoW rollback {Guid.NewGuid():N}";
        var committed = $"UoW commit {Guid.NewGuid():N}";
        await using var factory = new AshApiFactory(Db, configureServices: services =>
            services.AddSingleton<IStartupFilter, CommandEndpointsStartupFilter>());
        using var client = factory.CreateClient();
        try
        {
            var failed = await client.PostAsync($"{RollbackPath}?name={Uri.EscapeDataString(rolledBack)}", null, Ct);
            failed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await CountInstitutions(rolledBack)).ShouldBe(0);

            var succeeded = await client.PostAsync($"{CommitPath}?name={Uri.EscapeDataString(committed)}", null, Ct);
            succeeded.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await CountInstitutions(committed)).ShouldBe(1);
        }
        finally
        {
            await DeleteInstitutions(rolledBack);
            await DeleteInstitutions(committed);
        }
    }

    [Fact]
    public async Task CreateCommand_without_an_active_transaction_throws()
    {
        var services = new ServiceCollection();
        services.AddAshInfrastructure(Db.AppConnectionString);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Should.Throw<InvalidOperationException>(() => uow.CreateCommand("SELECT 1"));
    }
}
