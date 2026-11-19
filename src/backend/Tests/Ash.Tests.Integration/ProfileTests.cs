using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Tasks 048 / 053g — GET /api/identity/profile, read-only: name, position (Organika), role(s), e-mail, office or
/// institution. Client decision E: no phone, no notification switches (every notification is mandatory).
/// </summary>
public class ProfileTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Profile = "/api/identity/profile";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> GetProfile(HttpClient client)
    {
        var response = await client.GetAsync(Profile, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static List<string> Roles(JsonElement profile) =>
        profile.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("name").GetString()!).ToList();

    private async Task Execute(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    [Fact]
    public async Task Profile_per_role_lawyer_office_head_and_institution_user()
    {
        await using var factory = new AshApiFactory(Db);

        using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");
        var p = await GetProfile(lawyer);
        p.GetProperty("email").GetString().ShouldBe("avokat1@ash.local");
        Roles(p).ShouldBe(["Avokat Shteti (primar)"]); // task 053i: the role's new name
        p.GetProperty("office").GetProperty("name").GetString().ShouldNotBeNullOrEmpty();
        p.GetProperty("institution").ValueKind.ShouldBe(JsonValueKind.Null);
        p.GetProperty("headOf").ValueKind.ShouldBe(JsonValueKind.Null);
        p.GetProperty("position").ValueKind.ShouldBe(JsonValueKind.Null);
        // client decision E: gone
        p.TryGetProperty("phone", out _).ShouldBeFalse();
        p.TryGetProperty("notifications", out _).ShouldBeFalse();

        using var head = factory.CreateClient().AsUser("kryetar@ash.local");
        var h = await GetProfile(head);
        Roles(h).ShouldContain("Kryetari i Zyrës");
        h.GetProperty("headOf").GetString().ShouldBe(h.GetProperty("office").GetProperty("name").GetString());

        using var institution = factory.CreateClient().AsUser("institucion@ash.local");
        var i = await GetProfile(institution);
        Roles(i).ShouldBe(["Institucioni"]); // task 053k: the client's name
        i.GetProperty("office").ValueKind.ShouldBe(JsonValueKind.Null);
        i.GetProperty("institution").GetProperty("name").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task The_profile_shows_the_position_and_unit_from_Organika_and_no_manager()
    {
        await Execute("""
            UPDATE users SET position = 'Avokat i Shtetit, specialist', org_unit = 'Sektori i Gjyqësorit'
             WHERE email = 'avokat1@ash.local'
            """);
        await using var factory = new AshApiFactory(Db);
        using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");

        var p = await GetProfile(lawyer);

        p.GetProperty("position").GetString().ShouldBe("Avokat i Shtetit, specialist");
        p.GetProperty("orgUnit").GetString().ShouldBe("Sektori i Gjyqësorit");
        p.TryGetProperty("manager", out _).ShouldBeFalse(); // task 053p: "Eprori" is gone
    }

    [Fact]
    public async Task Nothing_on_the_profile_can_be_changed_any_more()
    {
        await using var factory = new AshApiFactory(Db);
        using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");

        (await lawyer.PutAsJsonAsync(Profile, new { phone = "069 123 4567" }, Ct)).StatusCode
            .ShouldBeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
        (await lawyer.PutAsJsonAsync($"{Profile}/notifications", new Dictionary<string, bool> { ["HEARING_TOMORROW"] = false }, Ct)).StatusCode
            .ShouldBeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
    }
}
