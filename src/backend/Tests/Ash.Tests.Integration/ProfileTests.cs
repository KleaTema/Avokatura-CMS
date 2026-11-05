using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>Task 048 — GET/PUT /api/identity/profile and the notification switches.</summary>
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

    private async Task<object?> Scalar(string sql, string? connectionString = null)
    {
        await using var connection = new MySqlConnection(connectionString ?? Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(Ct);
        return value is DBNull ? null : value;
    }

    [Fact]
    public async Task Profile_per_role_lawyer_office_head_and_institution_user()
    {
        await using var factory = new AshApiFactory(Db);

        using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");
        var p = await GetProfile(lawyer);
        p.GetProperty("email").GetString().ShouldBe("avokat1@ash.local");
        Roles(p).ShouldBe(["Avokat Shteti"]);
        p.GetProperty("office").GetProperty("name").GetString().ShouldNotBeNullOrEmpty();
        p.GetProperty("institution").ValueKind.ShouldBe(JsonValueKind.Null);
        p.GetProperty("headOf").ValueKind.ShouldBe(JsonValueKind.Null);
        p.GetProperty("phone").ValueKind.ShouldBe(JsonValueKind.Null);
        var types = p.GetProperty("notifications").EnumerateArray().ToList();
        types.Count.ShouldBe(20);
        types.ShouldAllBe(t => t.GetProperty("enabled").GetBoolean());
        types.Single(t => t.GetProperty("type").GetString() == "HEARING_TOMORROW").GetProperty("mandatory").GetBoolean().ShouldBeFalse();
        types.Single(t => t.GetProperty("type").GetString() == "LAWYER_ASSIGNED").GetProperty("mandatory").GetBoolean().ShouldBeTrue();

        using var head = factory.CreateClient().AsUser("kryetar@ash.local");
        var h = await GetProfile(head);
        Roles(h).ShouldContain("Kryetari i Zyrës");
        h.GetProperty("headOf").GetString().ShouldBe(h.GetProperty("office").GetProperty("name").GetString());

        using var institution = factory.CreateClient().AsUser("institucion@ash.local");
        var i = await GetProfile(institution);
        Roles(i).ShouldBe(["Institucion"]);
        i.GetProperty("office").ValueKind.ShouldBe(JsonValueKind.Null);
        i.GetProperty("institution").GetProperty("name").GetString().ShouldNotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("069 123 4567", "+355691234567")]
    [InlineData("0691234567", "+355691234567")]
    [InlineData("+355 69 123 4567", "+355691234567")]
    [InlineData("00355 68-123-4567", "+355681234567")]
    [InlineData("+355 4 222 3333", "+35542223333")]
    [InlineData("+39 333 123 4567", "+393331234567")]
    [InlineData("(+44) 20 7946 0958", "+442079460958")]
    public async Task Phone_is_stored_in_E164(string input, string expected)
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");

        var response = await client.PutAsJsonAsync(Profile, new { phone = input }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("phone").GetString().ShouldBe(expected);
        (await Scalar("SELECT phone FROM users WHERE email = 'avokat1@ash.local'")).ShouldBe(expected);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("069 123 45")]
    [InlineData("+355 1 234 5678")]
    [InlineData("+355 69 123 45678")]
    [InlineData("0711234567")]
    [InlineData("+abc")]
    public async Task Invalid_phone_is_400(string input)
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");

        var response = await client.PutAsJsonAsync(Profile, new { phone = input }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("profile.phone_invalid");
    }

    [Fact]
    public async Task Other_fields_are_refused_and_empty_clears_the_phone()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");

        var refused = await client.PutAsJsonAsync(Profile, new { phone = "0691234567", firstName = "Tjetër", email = "x@y.al" }, Ct);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("profile.field_read_only");
        (await Scalar("SELECT first_name FROM users WHERE email = 'avokat1@ash.local'")).ShouldNotBe("Tjetër");
        (await Scalar("SELECT phone FROM users WHERE email = 'avokat1@ash.local'")).ShouldBeNull();

        (await client.PutAsJsonAsync(Profile, new { phone = "0691234567" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var cleared = await client.PutAsJsonAsync(Profile, new { phone = "" }, Ct);
        cleared.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Scalar("SELECT phone FROM users WHERE email = 'avokat1@ash.local'")).ShouldBeNull();
    }

    [Fact]
    public async Task Phone_change_is_audited_masked_except_the_last_3_digits()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");
        (await client.PutAsJsonAsync(Profile, new { phone = "069 123 4567" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var userId = Convert.ToInt64(await Scalar("SELECT id FROM users WHERE email = 'avokat1@ash.local'"));
        var sql = $"SELECT details FROM ash_audit.audit_log WHERE action = 'PROFILE_UPDATED' AND entity_id = {userId} LIMIT 1";
        var deadline = DateTime.UtcNow.AddSeconds(15);
        object? details;
        while ((details = await Scalar(sql, Db.AuditConnectionString)) is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, Ct);
        }

        details.ShouldNotBeNull();
        var text = (string)details;
        text.ShouldNotContain("691234");
        using var doc = JsonDocument.Parse(text);
        doc.RootElement.GetProperty("new").GetProperty("Phone").GetString().ShouldBe("+*********567");
        doc.RootElement.GetProperty("old").GetProperty("Phone").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_mandatory_type_cannot_be_turned_off_an_optional_one_can()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");

        var mandatory = await client.PutAsJsonAsync($"{Profile}/notifications",
            new Dictionary<string, bool> { ["LAWYER_ASSIGNED"] = false, ["HEARING_TOMORROW"] = false }, Ct);
        mandatory.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await mandatory.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("profile.notification_mandatory");
        (await Scalar("SELECT COUNT(*) FROM user_notification_prefs")).ShouldBe(0L);

        (await client.PutAsJsonAsync($"{Profile}/notifications",
            new Dictionary<string, bool> { ["UNKNOWN_TYPE"] = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var off = await client.PutAsJsonAsync($"{Profile}/notifications",
            new Dictionary<string, bool> { ["HEARING_TOMORROW"] = false, ["LAWYER_ASSIGNED"] = true }, Ct);
        off.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = (await off.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().ToList();
        list.Single(t => t.GetProperty("type").GetString() == "HEARING_TOMORROW").GetProperty("enabled").GetBoolean().ShouldBeFalse();
        (await GetProfile(client)).GetProperty("notifications").EnumerateArray()
            .Single(t => t.GetProperty("type").GetString() == "HEARING_TOMORROW").GetProperty("enabled").GetBoolean().ShouldBeFalse();

        // back on: the row goes (on is the default)
        (await client.PutAsJsonAsync($"{Profile}/notifications",
            new Dictionary<string, bool> { ["HEARING_TOMORROW"] = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Scalar("SELECT COUNT(*) FROM user_notification_prefs")).ShouldBe(0L);
    }

    [Fact]
    public async Task The_phone_shows_on_the_admin_users_list()
    {
        await using var factory = new AshApiFactory(Db);
        using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");
        (await lawyer.PutAsJsonAsync(Profile, new { phone = "0691234567" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var admin = factory.CreateClient().AsUser("admin@ash.local");
        var page = await admin.GetFromJsonAsync<JsonElement>("/api/identity/admin/users?q=avokat1", Ct);
        page.GetProperty("items").EnumerateArray()
            .Single(u => u.GetProperty("email").GetString() == "avokat1@ash.local")
            .GetProperty("phone").GetString().ShouldBe("+355691234567");
    }
}
