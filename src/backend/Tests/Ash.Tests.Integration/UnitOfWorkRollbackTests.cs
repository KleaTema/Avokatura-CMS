using Ash.Infrastructure;
using Ash.Infrastructure.Persistence;
using Ash.Modules.Identity.Infrastructure;
using Ash.Modules.Cases.Infrastructure;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

public class UnitOfWorkRollbackTests(MySqlFixture db) : IntegrationTest(db)
{
    [Fact]
    public async Task Write_through_two_module_contexts_that_throws_leaves_no_rows()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var institutionName = $"Rollback {Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddAshInfrastructure(Db.AppConnectionString);
        services.AddScoped<IdentityDbContext>();
        services.AddScoped<CasesDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var cases = scope.ServiceProvider.GetRequiredService<CasesDbContext>();
        var ct = TestContext.Current.CancellationToken;

        await uow.BeginAsync(ct);
        try
        {
            await identity.Database.ExecuteSqlRawAsync(
                "INSERT INTO institutions (name, short_name, institution_type) VALUES ({0}, 'RB', 'MINISTRY')", [institutionName], ct);
            await cases.Database.ExecuteSqlRawAsync(
                "INSERT INTO case_history_events (dossier_id, case_id, event_type, actor_user_id, summary) VALUES ({0}, {1}, 'CASE_REGISTERED', {2}, 'rollback')",
                [seed.ActiveDossierId, seed.ActiveCaseId, seed.User("avokat1").Id], ct);
            throw new InvalidOperationException("handler failed");
        }
        catch (InvalidOperationException)
        {
            await uow.RollbackAsync(CancellationToken.None);
        }

        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(ct);
        await using var count = new MySqlCommand(
            "SELECT (SELECT COUNT(*) FROM institutions WHERE name = @n) + (SELECT COUNT(*) FROM case_history_events WHERE case_id = @c)",
            connection);
        count.Parameters.AddWithValue("@n", institutionName);
        count.Parameters.AddWithValue("@c", seed.ActiveCaseId);
        Convert.ToInt64(await count.ExecuteScalarAsync(ct)).ShouldBe(0);
    }
}
