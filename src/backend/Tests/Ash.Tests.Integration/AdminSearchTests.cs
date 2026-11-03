using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Substring search on the server-side admin lists and the institution picker (ngram FULLTEXT, script 014), the new
/// filters (judge court; institution type, status, parent) and the list total. Rows a test adds are deleted in
/// finally; other rows may exist, so searches assert "contains" / "does not contain".
/// </summary>
public class AdminSearchTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Admin = "admin@ash.local";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task Execute(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<JsonElement> Get(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{url}\n{text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static List<string> Names(JsonElement page, params string[] fields) => page.GetProperty("items").EnumerateArray()
        .Select(i => string.Join(' ', fields.Select(f => i.GetProperty(f).GetString()))).ToList();

    [Theory]
    [InlineData("hoxha", "Besnik Hoxha")] // seeded judge, any case
    [InlineData("oxh", "Besnik Hoxha")] // 3 letters inside the last name
    [InlineData("deshira", "Arbër Dëshira")] // accent-insensitive
    [InlineData("rbë dësh", "Arbër Dëshira")] // parts of first and last name
    public async Task Judges_are_found_by_any_part_of_the_name(string q, string judge)
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Admin);

        var page = await Get(client, $"/api/reference/admin/judges?q={Uri.EscapeDataString(q)}");
        Names(page, "firstName", "lastName").ShouldContain(judge, q);
        page.GetProperty("total").GetInt32().ShouldBe(page.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Judges_filter_by_court()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Admin);
        var durres = await Get(client, "/api/reference/courts");
        var court = durres.EnumerateArray().First(c => c.GetProperty("name").GetString() == "Gjykata e Shkallës së Parë Durrës");

        var page = await Get(client, $"/api/reference/admin/judges?courtId={court.GetProperty("id").GetInt32()}&limit=100");
        Names(page, "firstName", "lastName").ShouldContain("Besnik Hoxha");
        page.GetProperty("items").EnumerateArray()
            .ShouldAllBe(j => j.GetProperty("courtName").GetString() == "Gjykata e Shkallës së Parë Durrës");
    }

    [Theory]
    [InlineData("minist")]
    [InlineData("inan")] // Fin-an-cave: needs the stopwords off (script 014)
    [InlineData("stri")] // M-in-ist-ria
    [InlineData("ekonomise")] // accent-insensitive
    public async Task Institutions_are_found_by_any_part_of_the_name_in_the_admin_list_and_the_picker(string q)
    {
        await using var factory = new AshApiFactory(Db);
        using var admin = factory.CreateClient().AsUser(Admin);
        using var protokoll = factory.CreateClient().AsUser("protokoll@ash.local");

        Names(await Get(admin, $"/api/identity/admin/institutions?q={q}"), "name").ShouldContain("Ministria e Financave dhe Ekonomisë", q);
        (await Get(protokoll, $"/api/identity/institutions?q={q}")).EnumerateArray()
            .Select(i => i.GetProperty("name").GetString()).ShouldContain("Ministria e Financave dhe Ekonomisë", q);
    }

    [Fact]
    public async Task Institutions_filter_by_type_status_and_parent_and_carry_the_total()
    {
        const string name = "AdminSearchAgjenci";
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Admin);
        try
        {
            await Execute($"""
                INSERT INTO institutions (name, short_name, institution_type, parent_institution_id, nipt, is_active)
                SELECT '{name} Një', 'ASA1', 'AGENCY', id, 'L99887766A', 1 FROM institutions WHERE name = 'Ministria e Financave dhe Ekonomisë';
                INSERT INTO institutions (name, short_name, institution_type, is_active) VALUES ('{name} Dy', 'ASA2', 'AGENCY', 0);
                """);
            var ministry = (await Get(client, "/api/identity/admin/institutions?q=Ministria%20Financave")).GetProperty("items")
                .EnumerateArray().First().GetProperty("publicId").GetGuid();

            Names(await Get(client, $"/api/identity/admin/institutions?q={name}&type=AGENCY"), "name")
                .ShouldBe([$"{name} Një", $"{name} Dy"], ignoreOrder: true);
            Names(await Get(client, $"/api/identity/admin/institutions?q={name}&active=false"), "name").ShouldBe([$"{name} Dy"]);
            Names(await Get(client, $"/api/identity/admin/institutions?q={name}&parentId={ministry}"), "name").ShouldBe([$"{name} Një"]);
            Names(await Get(client, $"/api/identity/admin/institutions?q={name}&type=MINISTRY"), "name").ShouldBeEmpty();
            Names(await Get(client, "/api/identity/admin/institutions?q=887766"), "name").ShouldBe([$"{name} Një"]); // inside the NIPT

            var paged = await Get(client, $"/api/identity/admin/institutions?q={name}&limit=1");
            paged.GetProperty("items").GetArrayLength().ShouldBe(1);
            paged.GetProperty("total").GetInt32().ShouldBe(2);
        }
        finally
        {
            await Execute($"DELETE FROM institutions WHERE name LIKE '{name}%'");
        }
    }

    [Theory]
    [InlineData("okat1")] // inside the first name "Avokat1"
    [InlineData("vokat1 dev")] // first and last name
    [InlineData("okat1@ash")] // inside the email
    public async Task Users_are_found_by_any_part_of_the_name_or_email(string q)
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Admin);

        var page = await Get(client, $"/api/identity/admin/users?q={Uri.EscapeDataString(q)}");
        page.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("email").GetString()).ShouldContain("avokat1@ash.local", q);
        page.GetProperty("total").GetInt32().ShouldBe(page.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Users_with_an_accented_name_are_found_without_the_accents()
    {
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        await seed.UserAsync("hoxhaj", "Arbër", "Hoxhaj", null, null);
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Admin);
        try
        {
            foreach (var q in new[] { "hoxha", "arber", "rbë hox" })
            {
                Names(await Get(client, $"/api/identity/admin/users?q={Uri.EscapeDataString(q)}"), "firstName", "lastName")
                    .ShouldContain("Arbër Hoxhaj", q);
            }
        }
        finally
        {
            await Execute($"DELETE FROM users WHERE email = 'hoxhaj-{seed.Suffix}@ash.test'");
        }
    }
}
