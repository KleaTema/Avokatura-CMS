using Ash.Infrastructure;
using Ash.Modules.Cases;
using Ash.Modules.Cases.Infrastructure;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ash.Tests.Integration;

public class CasesDbContextTests(MySqlFixture db) : IntegrationTest(db)
{
    /// <summary>DATE columns map to DateOnly through a converter: the MySQL provider alone cannot read them as DateOnly.</summary>
    [Fact]
    public async Task Case_with_calendar_dates_can_be_read_back()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var services = new ServiceCollection();
        services.AddAshInfrastructure(Db.AppConnectionString);
        services.AddCasesModule(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var cases = scope.ServiceProvider.GetRequiredService<CasesDbContext>();
        var id = (ulong)seed.RegisteredCaseId;

        var c = await cases.Cases.AsNoTracking().SingleAsync(x => x.Id == id, TestContext.Current.CancellationToken);

        // CaseSeed inserts received_date = CURDATE() (server date) and no act_date
        c.ReceivedDate.ShouldBeInRange(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1));
        c.ActDate.ShouldBeNull();
    }
}
