using System.Text.Json.Nodes;
using Ash.Infrastructure;
using Ash.Infrastructure.Persistence;
using Ash.Modules.Cases;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Time;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// case_history_events is append-only (triggers), so these tests NEVER commit: every test begins the unit of work,
/// writes and reads inside the transaction, then rolls back. Runs against the test container.
/// </summary>
public sealed class HistoryWriterFixture(string connectionString) : IAsyncLifetime
{
    public string ConnectionString { get; } = connectionString;
    public CaseSeed Seed { get; private set; } = null!;
    public ServiceProvider Provider { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Seed = await CaseSeed.CreateAsync(ConnectionString);
        var services = new ServiceCollection();
        services.AddAshInfrastructure(ConnectionString);
        services.AddSingleton<IClock, SystemClock>();
        services.AddCasesModule(new ConfigurationBuilder().Build());
        services.AddScoped<CaseAccessFixture.TestCurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CaseAccessFixture.TestCurrentUser>());
        Provider = services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await Provider.DisposeAsync();
        await Seed.DisposeAsync();
    }
}

public class HistoryWriterTests(MySqlFixture db) : IntegrationTest(db)
{
    private HistoryWriterFixture fixture = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        fixture = new HistoryWriterFixture(Db.AppConnectionString);
        await fixture.InitializeAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await fixture.DisposeAsync();
        await base.DisposeAsync();
    }

    private long DossierId => fixture.Seed.ActiveDossierId;
    private long CaseId => fixture.Seed.ActiveCaseId;

    private AsyncServiceScope NewScope(string userKey)
    {
        var scope = fixture.Provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CaseAccessFixture.TestCurrentUser>().Set(fixture.Seed.User(userKey));
        return scope;
    }

    [Fact]
    public async Task Written_row_has_dossier_case_type_actor_summary_and_details()
    {
        await using var scope = NewScope("avokat1");
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<CasesDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<IHistoryWriter>();
        const string summary = "Avokati primar u ndryshua: Drita Avokatja → Ermal Avokati";

        await uow.BeginAsync(TestContext.Current.CancellationToken);
        try
        {
            writer.Add(DossierId, CaseId, HistoryEventType.LawyerTransferred, summary,
                old: new { lawyer = "Drita Avokatja", role = "PRIMARY" },
                @new: new { lawyer = "Ermal Avokati", role = "PRIMARY" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var row = await db.CaseHistoryEvents.AsNoTracking()
                .SingleAsync(x => x.CaseId == (ulong)CaseId, TestContext.Current.CancellationToken);

            row.DossierId.ShouldBe((ulong)DossierId);
            row.EventType.ShouldBe("LAWYER_TRANSFERRED");
            row.ActorUserId.ShouldBe((uint)fixture.Seed.User("avokat1").Id);
            row.Summary.ShouldBe(summary);
            (DateTime.UtcNow - row.OccurredAt).Duration().ShouldBeLessThan(TimeSpan.FromMinutes(5));

            var details = JsonNode.Parse(row.Details!)!;
            details["old"]!.ToJsonString().ShouldBe("""{"lawyer":"Drita Avokatja"}""");
            details["new"]!.ToJsonString().ShouldBe("""{"lawyer":"Ermal Avokati"}""");
        }
        finally
        {
            await uow.RollbackAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Handler_that_fails_after_writing_history_leaves_no_row()
    {
        await using var scope = NewScope("avokat1");
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<CasesDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<IHistoryWriter>();

        await uow.BeginAsync(TestContext.Current.CancellationToken);
        try
        {
            writer.Add(DossierId, CaseId, HistoryEventType.ObjectChanged, "Objekti u ndryshua.");
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            throw new InvalidOperationException("handler failed");
        }
        catch (InvalidOperationException)
        {
            // what UnitOfWorkMiddleware does on an exception
            await uow.RollbackAsync(CancellationToken.None);
        }

        await using var connection = new MySqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var count = new MySqlCommand("SELECT COUNT(*) FROM case_history_events WHERE case_id = @c", connection);
        count.Parameters.AddWithValue("@c", CaseId);
        Convert.ToInt64(await count.ExecuteScalarAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task Updating_a_history_row_fails_with_append_only_trigger_error()
    {
        await using var scope = NewScope("avokat1");
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<CasesDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<IHistoryWriter>();

        await uow.BeginAsync(TestContext.Current.CancellationToken);
        try
        {
            writer.Add(DossierId, CaseId, HistoryEventType.ObjectChanged, "Objekti u ndryshua.");
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var ex = await Should.ThrowAsync<Exception>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE case_history_events SET summary = 'changed' WHERE case_id = {0}", [CaseId], TestContext.Current.CancellationToken));
            ex.ToString().ShouldContain("append-only");
        }
        finally
        {
            await uow.RollbackAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Summary_longer_than_500_characters_is_rejected()
    {
        await using var scope = NewScope("avokat1");
        var writer = scope.ServiceProvider.GetRequiredService<IHistoryWriter>();

        Should.Throw<ArgumentException>(() =>
            writer.Add(DossierId, CaseId, HistoryEventType.ObjectChanged, new string('x', 501)));
        Should.NotThrow(() =>
            writer.Add(DossierId, CaseId, HistoryEventType.ObjectChanged, new string('x', 500)));
    }
}
