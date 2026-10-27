using Ash.Infrastructure;
using Ash.Modules.Cases;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Cases.Infrastructure.Access;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>Needs the local MySQL (docker) migrated with --local. Override with env var ASH_TEST_CONNECTION.</summary>
public sealed class CaseAccessFixture : IAsyncLifetime
{
    public CaseSeed Seed { get; private set; } = null!;
    public ServiceProvider Provider { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Seed = await CaseSeed.CreateAsync();
        var services = new ServiceCollection();
        services.AddAshInfrastructure(CaseSeed.ConnectionString);
        services.AddCasesModule(new ConfigurationBuilder().Build());
        services.AddScoped<TestCurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<TestCurrentUser>());
        Provider = services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await Provider.DisposeAsync();
        await Seed.DisposeAsync();
    }

    public async Task<T> RunAsync<T>(string userKey, Func<CaseAccessService, CasesDbContext, Task<T>> action)
    {
        await using var scope = Provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>().Set(Seed.User(userKey));
        return await action(
            scope.ServiceProvider.GetRequiredService<CaseAccessService>(),
            scope.ServiceProvider.GetRequiredService<CasesDbContext>());
    }

    public sealed class TestCurrentUser : ICurrentUser
    {
        private SeedUser? _user;

        public void Set(SeedUser user) => _user = user;

        public long UserId => _user!.Id;
        public Guid PublicId => Guid.Empty;
        public long? OfficeId => _user!.OfficeId;
        public long? InstitutionId => _user!.InstitutionId;
        public IReadOnlyCollection<string> Roles => _user!.Roles;
        public bool HasPermission(string permission) => _user!.Permissions.Contains(permission);
    }
}

public class CaseAccessServiceTests(CaseAccessFixture fixture) : IClassFixture<CaseAccessFixture>
{
    private long Active => fixture.Seed.ActiveCaseId;
    private long Registered => fixture.Seed.RegisteredCaseId;

    private Task<bool> Has(string user, string permission) =>
        fixture.RunAsync(user, async (s, _) => await s.HasAsync(Active, permission));

    private Task<bool> CanView(string user, long caseId) =>
        fixture.RunAsync(user, async (s, _) => await s.CanViewAsync(caseId));

    private async Task ShouldRequireFail(string user, string permission, ErrorType expected)
    {
        var ex = await Should.ThrowAsync<CaseAccessException>(
            fixture.RunAsync(user, async (s, _) =>
            {
                await s.RequireAsync(Active, permission);
                return 0;
            }));
        ex.Error.Type.ShouldBe(expected);
    }

    private async Task ShouldRequirePass(string user, string permission) =>
        await fixture.RunAsync(user, async (s, _) =>
        {
            await s.RequireAsync(Active, permission);
            return 0;
        });

    [Fact]
    public async Task Primary_lawyer_can_record_decision()
    {
        (await Has("avokat1", "decision.record")).ShouldBeTrue();
        await ShouldRequirePass("avokat1", "decision.record");
    }

    [Fact]
    public async Task Secondary_lawyer_cannot_record_decision_but_can_delete_party()
    {
        (await Has("avokat2", "decision.record")).ShouldBeFalse();
        await ShouldRequireFail("avokat2", "decision.record", ErrorType.Forbidden);
        await ShouldRequirePass("avokat2", "party.delete");
    }

    [Fact]
    public async Task Legal_assistant_can_add_hearing_but_not_change_judge()
    {
        await ShouldRequirePass("ndihmes", "hearing.add");
        await ShouldRequireFail("ndihmes", "case.change_judge", ErrorType.Forbidden);
    }

    [Fact]
    public async Task Monitoring_institution_user_can_add_hearing_and_upload_but_not_change_judge()
    {
        await ShouldRequirePass("institucion", "hearing.add");
        await ShouldRequirePass("institucion", "document.upload");
        await ShouldRequireFail("institucion", "case.change_judge", ErrorType.Forbidden);
    }

    [Fact]
    public async Task Assigned_jurist_can_add_hearing()
    {
        await ShouldRequirePass("jurist", "hearing.add");
    }

    [Fact]
    public async Task Unassigned_jurist_gets_not_found()
    {
        (await CanView("jurist2", Active)).ShouldBeFalse();
        await ShouldRequireFail("jurist2", "hearing.add", ErrorType.NotFound);
    }

    [Fact]
    public async Task Office_head_can_view_but_not_record_decision()
    {
        (await CanView("kryetar", Active)).ShouldBeTrue();
        await ShouldRequireFail("kryetar", "decision.record", ErrorType.Forbidden);
    }

    [Theory]
    [InlineData("decision.record")]
    [InlineData("hearing.add")]
    [InlineData("party.delete")]
    public async Task Monitor_can_view_but_has_no_case_permission(string permission)
    {
        (await CanView("monitor", Active)).ShouldBeTrue();
        await ShouldRequireFail("monitor", permission, ErrorType.Forbidden);
    }

    [Fact]
    public async Task Admin_can_view()
    {
        (await CanView("admin", Active)).ShouldBeTrue();
    }

    [Fact]
    public async Task Lawyer_of_another_office_gets_not_found()
    {
        (await CanView("drlawyer", Active)).ShouldBeFalse();
        await ShouldRequireFail("drlawyer", "decision.record", ErrorType.NotFound);
    }

    [Fact]
    public async Task Protocol_sees_own_registered_case_only()
    {
        (await CanView("protokoll", Registered)).ShouldBeTrue();
        (await CanView("protokoll", Active)).ShouldBeFalse();
        await ShouldRequireFail("protokoll", "case.view", ErrorType.NotFound);
    }

    [Fact]
    public async Task Unknown_case_gets_not_found()
    {
        (await CanView("admin", long.MaxValue / 2)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("avokat1", new[] { "active" })]
    [InlineData("kryetar", new[] { "active", "registered" })]
    [InlineData("institucion", new[] { "active" })]
    [InlineData("drlawyer", new string[0])]
    public async Task ApplyScope_returns_exactly_the_visible_cases(string user, string[] expected)
    {
        var seeded = new[] { (ulong)Active, (ulong)Registered };
        var ids = await fixture.RunAsync(user, async (s, db) =>
            await s.ApplyScope(db.Cases).Where(c => seeded.Contains(c.Id)).Select(c => c.Id).ToListAsync());

        var expectedIds = expected.Select(e => e == "active" ? (ulong)Active : (ulong)Registered);
        ids.OrderBy(i => i).ShouldBe(expectedIds.OrderBy(i => i));
    }
}
