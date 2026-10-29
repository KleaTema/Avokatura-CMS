using Ash.Infrastructure;
using Ash.Infrastructure.Persistence;
using Ash.Modules.Identity.Infrastructure;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>Rows added through IdentityDbContext must get the AUTO_INCREMENT id back. Each test rolls its row back.</summary>
public class IdentityDbContextTests(MySqlFixture db) : IntegrationTest(db)
{
    private async Task InsideRolledBackTransaction(Func<IdentityDbContext, CancellationToken, Task> act)
    {
        var services = new ServiceCollection();
        services.AddAshInfrastructure(Db.AppConnectionString);
        services.AddScoped<IdentityDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var ct = TestContext.Current.CancellationToken;

        await uow.BeginAsync(ct);
        try
        {
            await act(identity, ct);
        }
        finally
        {
            await uow.RollbackAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Office_added_through_the_DbContext_gets_its_AUTO_INCREMENT_id() =>
        await InsideRolledBackTransaction(async (identity, ct) =>
        {
            var office = new OfficeRecord
            {
                PublicId = Guid.CreateVersion7(),
                Code = $"T{Guid.NewGuid():N}"[..20],
                Name = "Zyrë test",
                OfficeType = "LOCAL"
            };
            identity.Offices.Add(office);
            await identity.SaveChangesAsync(ct);

            office.Id.ShouldBeGreaterThan((ushort)0);
        });

    [Fact]
    public async Task Role_added_through_the_DbContext_gets_its_AUTO_INCREMENT_id() =>
        await InsideRolledBackTransaction(async (identity, ct) =>
        {
            var role = new RoleRecord { Code = $"TEST_{Guid.NewGuid():N}"[..30], Name = "Rol test" };
            identity.Roles.Add(role);
            await identity.SaveChangesAsync(ct);

            role.Id.ShouldBeGreaterThan((byte)0);
        });
}
