using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.SharedKernel.Ids;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 031 — GET /api/cases/filter-options: the office and lawyer filters of the case list and the agenda, per role.
/// Uses <see cref="CaseSeed"/>: office TR (kryetar, avokat1, avokat2; ndihmes is an assistant) and office DR with
/// its own lawyer.
/// </summary>
public class FilterOptionsTests(MySqlFixture db) : IntegrationTest(db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Options(List<(Guid Id, string Name)> Offices, List<(Guid Id, string Name, Guid Office)> Lawyers);

    private static async Task<Options> Get(HttpClient client)
    {
        var response = await client.GetAsync("/api/cases/filter-options", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        JsonAssert.NoNumericIds(json);
        return new Options(
            json.GetProperty("offices").EnumerateArray().Select(o => (o.GetProperty("publicId").GetGuid(), o.GetProperty("name").GetString()!)).ToList(),
            json.GetProperty("lawyers").EnumerateArray().Select(l => (l.GetProperty("publicId").GetGuid(),
                l.GetProperty("fullName").GetString()!, l.GetProperty("officePublicId").GetGuid())).ToList());
    }

    private async Task<Guid> PublicIdOf(string table, long id)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand($"SELECT public_id FROM {table} WHERE id = {id}", connection);
        return PublicId.FromBytes((byte[])(await command.ExecuteScalarAsync(Ct))!);
    }

    [Fact]
    public async Task Monitor_gets_every_active_office_and_every_lawyer_with_their_office()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);
        var tr = await PublicIdOf("offices", s.OfficeTrId);
        var dr = await PublicIdOf("offices", s.OfficeDrId);

        var options = await Get(factory.CreateClient().AsUser("monitor@ash.local"));

        options.Offices.Select(o => o.Id).ShouldContain(tr);
        options.Offices.Select(o => o.Id).ShouldContain(dr);
        options.Offices.Select(o => o.Name).ShouldBe(options.Offices.Select(o => o.Name).Order(StringComparer.InvariantCultureIgnoreCase));
        var lawyers = options.Lawyers.ToDictionary(l => l.Id);
        lawyers[await PublicIdOf("users", s.User("avokat1").Id)].Office.ShouldBe(tr);
        lawyers[await PublicIdOf("users", s.User("drlawyer").Id)].Office.ShouldBe(dr);
        lawyers.ShouldNotContainKey(await PublicIdOf("users", s.User("ndihmes").Id)); // not a STATE_LAWYER
        options.Lawyers.Select(l => l.Name).ShouldBe(options.Lawyers.Select(l => l.Name).Order(StringComparer.InvariantCultureIgnoreCase));
    }

    [Fact]
    public async Task Kryetar_gets_only_the_own_office_and_its_lawyers()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);
        var tr = await PublicIdOf("offices", s.OfficeTrId);

        var options = await Get(factory.CreateClient().AsUser("kryetar@ash.local"));

        options.Offices.Select(o => o.Id).ShouldBe([tr]);
        options.Lawyers.ShouldAllBe(l => l.Office == tr);
        var ids = options.Lawyers.Select(l => l.Id).ToList();
        ids.ShouldContain(await PublicIdOf("users", s.User("avokat1").Id));
        ids.ShouldContain(await PublicIdOf("users", s.User("avokat2").Id));
        ids.ShouldContain(await PublicIdOf("users", s.User("kryetar").Id)); // he may assign himself
        ids.ShouldNotContain(await PublicIdOf("users", s.User("drlawyer").Id));
    }

    [Theory]
    [InlineData("avokat1@ash.local")]
    [InlineData("protokoll@ash.local")]
    [InlineData("institucion@ash.local")]
    [InlineData("jurist@ash.local")]
    public async Task Users_without_office_or_all_view_get_empty_lists(string email)
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);

        var options = await Get(factory.CreateClient().AsUser(email));

        options.Offices.ShouldBeEmpty();
        options.Lawyers.ShouldBeEmpty();
    }
}
