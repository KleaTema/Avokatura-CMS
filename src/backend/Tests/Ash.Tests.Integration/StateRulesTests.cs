using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 029 — the state rules of CaseAccessService.ApplyStateRules, seen as the UI sees them (permissions[] of
/// GET /api/cases/{id}): each state-dependent action is present in its state and absent in the opposite one.
/// </summary>
public class StateRulesTests(MySqlFixture db) : IntegrationTest(db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(Ct);
        return value is DBNull ? null : value;
    }

    private async Task<Guid> PublicIdOf(string table, long id) =>
        Ash.SharedKernel.Ids.PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM {table} WHERE id = {id}"))!);

    private async Task<List<string>> Permissions(AshApiFactory factory, string user, long caseId)
    {
        using var client = factory.CreateClient().AsUser(user);
        var response = await client.GetAsync($"/api/cases/{await PublicIdOf("cases", caseId)}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, user);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("permissions")
            .EnumerateArray().Select(p => p.GetString()!).ToList();
    }

    private Task SetStatus(long caseId, string status) => Scalar($"UPDATE cases SET lifecycle_status = '{status}' WHERE id = {caseId}");

    [Fact]
    public async Task Assign_lawyer_only_while_registered()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);

        (await Permissions(factory, "kryetar@ash.local", seed.RegisteredCaseId)).ShouldContain("case.assign_lawyer");
        (await Permissions(factory, "kryetar@ash.local", seed.ActiveCaseId)).ShouldNotContain("case.assign_lawyer");
    }

    [Fact]
    public async Task Transfer_only_while_active()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);

        (await Permissions(factory, "kryetar@ash.local", seed.ActiveCaseId)).ShouldContain("case.transfer");
        (await Permissions(factory, "kryetar@ash.local", seed.RegisteredCaseId)).ShouldNotContain("case.transfer");
    }

    [Fact]
    public async Task Delegate_only_while_active()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);

        // the primary lawyer holds case.delegate through the PRIMARY_LAWYER profile; the state decides
        (await Permissions(factory, "avokat1@ash.local", seed.ActiveCaseId)).ShouldContain("case.delegate");
        await SetStatus(seed.ActiveCaseId, "REGISTERED");
        var registered = await Permissions(factory, "avokat1@ash.local", seed.ActiveCaseId);
        registered.ShouldNotContain("case.delegate");
        registered.ShouldContain("case.set_category"); // only the state-dependent actions go
    }

    [Fact]
    public async Task Set_category_not_while_delegim_or_relatim()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);
        string Category(string code) => $"UPDATE cases SET category_id = (SELECT id FROM case_categories WHERE code = '{code}'), subcategory_id = NULL WHERE id = {seed.ActiveCaseId}";

        await Scalar(Category("NDJEKJE"));
        (await Permissions(factory, "avokat1@ash.local", seed.ActiveCaseId)).ShouldContain("case.set_category");

        foreach (var delegation in new[] { "DELEGIM", "RELATIM" })
        {
            await Scalar(Category(delegation));
            var permissions = await Permissions(factory, "avokat1@ash.local", seed.ActiveCaseId);
            permissions.ShouldNotContain("case.set_category", delegation);
            permissions.ShouldContain("case.delegate", delegation); // undelegating stays possible
        }
    }
}
