using Ash.SharedKernel.Time;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 046 acceptance with 50,000 cases — OPT-IN (category "Perf", explicit: never in the normal suite; seeding alone
/// takes a while). Run:
/// <c>dotnet test Tests/Ash.Tests.Integration --filter "Category=Perf" -- xUnit.Explicit=only</c>
/// </summary>
public class DashboardPerfTests(MySqlFixture db) : IntegrationTest(db)
{
    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    [Fact(Explicit = true, Timeout = 600_000)]
    [Trait("Category", "Perf")]
    public async Task Dashboard_answers_in_under_500_ms_for_kryetar_lawyer_and_monitor_with_50000_cases()
    {
        // 20 offices and hearings over a year, as a real installation spreads them (see DashboardSeed)
        await DashboardSeed.CreateAsync(Db.AppConnectionString, 50_000, DashboardTests.Now, offices: 20, spreadHearings: true);
        var factory = new AshApiFactory(Db, configureServices: services => services.AddSingleton<IClock>(new FixedClock(DashboardTests.Now)));
        await DashboardTiming.AssertFastAsync(factory, 50_000, TestContext.Current.TestOutputHelper, TestContext.Current.CancellationToken);
    }
}
