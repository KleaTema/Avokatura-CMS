using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.SharedKernel.Ids;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Institution administration and the party-registration search (task 015). Each test seeds its own institutions
/// through the admin API and deletes them in finally. Other institutions may exist, so searches assert "contains".
/// </summary>
public class AdminInstitutionsTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Base = "/api/identity/admin/institutions";
    private const string Search = "/api/identity/institutions";
    private const string Admin = "admin@ash.local";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly List<Guid> _created = [];

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string user, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Dev-User", user);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static async Task<string?> ProblemCode(HttpResponseMessage response) =>
        (await Json(response)).GetProperty("code").GetString();

    private static object Body(string name, string type = "AGENCY", string? shortName = null, Guid? parent = null, string? nipt = null) =>
        new { name, shortName, institutionType = type, parentInstitutionId = parent, nipt };

    private async Task<Guid> Create(HttpClient client, string name, string type = "AGENCY", string? shortName = null, Guid? parent = null, string? nipt = null)
    {
        var response = await Send(client, HttpMethod.Post, Base, Admin, Body(name, type, shortName, parent, nipt));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await Json(response)).GetProperty("publicId").GetGuid();
        _created.Add(id);
        return id;
    }

    private static async Task<List<JsonElement>> SearchFor(HttpClient client, string q, string user = "avokat1@ash.local")
    {
        var response = await Send(client, HttpMethod.Get, $"{Search}?q={Uri.EscapeDataString(q)}", user);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await Json(response)).EnumerateArray().ToList();
    }

    private static bool Contains(List<JsonElement> rows, Guid id) =>
        rows.Any(r => r.GetProperty("publicId").GetGuid() == id);

    private async Task CleanUp()
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        // children first: clear every parent link before deleting
        foreach (var sql in new[]
                 {
                     "UPDATE institutions SET parent_institution_id = NULL WHERE public_id = @p",
                     "DELETE FROM institutions WHERE public_id = @p"
                 })
        {
            foreach (var id in _created)
            {
                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@p", PublicId.ToBytes(id));
                await command.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task Search_finds_by_word_prefix_short_name_and_without_diacritics()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        try
        {
            var finance = await Create(client, "Ministria e Financave", "MINISTRY", shortName: "MFT");
            var health = await Create(client, "Ministria e Shëndetësisë dhe Mbrojtjes Sociale", "MINISTRY", shortName: "MSHMS");
            var agency = await Create(client, "Agjencia Kombëtare Test", shortName: "AKTZQ", parent: finance);

            Contains(await SearchFor(client, "financ"), finance).ShouldBeTrue();
            Contains(await SearchFor(client, "aktzq"), agency).ShouldBeTrue();
            Contains(await SearchFor(client, "shendetesise"), health).ShouldBeTrue();

            var row = (await SearchFor(client, "Agjencia Kombëtare Test")).Single(r => r.GetProperty("publicId").GetGuid() == agency);
            row.GetProperty("parentName").GetString().ShouldBe("Ministria e Financave");
            row.GetProperty("institutionType").GetString().ShouldBe("AGENCY");
            row.TryGetProperty("nipt", out _).ShouldBeFalse();
            row.TryGetProperty("id", out _).ShouldBeFalse();
        }
        finally
        {
            await CleanUp();
        }
    }

    [Fact]
    public async Task Search_escapes_wildcards_and_validates_the_query()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        try
        {
            var plain = await Create(client, "Institucion pa përqindje");

            // '%%' and '__' are literal text after escaping, so they do not match every row
            var percent = await SearchFor(client, "%%");
            Contains(percent, plain).ShouldBeFalse();
            (await SearchFor(client, "__")).ShouldBeEmpty();

            foreach (var q in new[] { "%", " a ", new string('a', 101) })
            {
                var response = await Send(client, HttpMethod.Get, $"{Search}?q={Uri.EscapeDataString(q)}", "avokat1@ash.local");
                response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, q);
            }

            (await Send(client, HttpMethod.Get, $"{Search}?q=ministria&limit=500", "avokat1@ash.local"))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
            (await Json(await Send(client, HttpMethod.Get, $"{Search}?q=ministria&limit=500", "avokat1@ash.local")))
                .GetArrayLength().ShouldBeLessThanOrEqualTo(20);
        }
        finally
        {
            await CleanUp();
        }
    }

    [Fact]
    public async Task Inactive_institution_is_not_returned_by_search()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        try
        {
            var inactive = await Create(client, "Institucioni Joaktiv Testzq");
            (await Send(client, HttpMethod.Post, $"{Base}/{inactive}/deactivate", Admin)).StatusCode.ShouldBe(HttpStatusCode.OK);

            Contains(await SearchFor(client, "Joaktiv Testzq"), inactive).ShouldBeFalse();
        }
        finally
        {
            await CleanUp();
        }
    }

    [Fact]
    public async Task Institution_users_and_unauthenticated_requests()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();

        (await Send(client, HttpMethod.Get, $"{Search}?q=ministria", "institucion@ash.local")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var anonymous = await client.GetAsync($"{Search}?q=ministria", Ct);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Parent_must_be_an_active_ministry_and_a_ministry_has_no_parent()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        try
        {
            var ministry = await Create(client, "Ministria Test Prind", "MINISTRY");
            var agency = await Create(client, "Agjencia Test Prind");

            var notMinistry = await Send(client, HttpMethod.Post, Base, Admin, Body("Nën-agjenci", parent: agency));
            notMinistry.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ProblemCode(notMinistry)).ShouldBe("institution.parent_not_ministry");

            var ministryWithParent = await Send(client, HttpMethod.Post, Base, Admin, Body("Ministria e dytë", "MINISTRY", parent: ministry));
            ministryWithParent.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ProblemCode(ministryWithParent)).ShouldBe("institution.ministry_has_parent");

            (await Send(client, HttpMethod.Put, $"{Base}/{agency}", Admin, Body("Agjencia Test Prind", parent: ministry)))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            await CleanUp();
        }
    }

    [Fact]
    public async Task Nipt_is_normalized_validated_and_unique()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        try
        {
            foreach (var bad in new[] { "K1234567A", "112345678A", "K12345678", "K1234 5678A" })
            {
                var response = await Send(client, HttpMethod.Post, Base, Admin, Body("NIPT i gabuar", nipt: bad));
                response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, bad);
                (await ProblemCode(response)).ShouldBe("institution.nipt_invalid");
            }

            var nipt = $"Z{Random.Shared.Next(10_000_000, 99_999_999)}Q";
            var first = await Send(client, HttpMethod.Post, Base, Admin, Body("NIPT i parë", nipt: $"  {nipt.ToLowerInvariant()} "));
            first.StatusCode.ShouldBe(HttpStatusCode.Created);
            var created = await Json(first);
            _created.Add(created.GetProperty("publicId").GetGuid());
            created.GetProperty("nipt").GetString().ShouldBe(nipt);

            var duplicate = await Send(client, HttpMethod.Post, Base, Admin, Body("NIPT i dytë", nipt: nipt));
            duplicate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ProblemCode(duplicate)).ShouldBe("institution.nipt_exists");
        }
        finally
        {
            await CleanUp();
        }
    }

    [Fact]
    public async Task Admin_list_pages_with_keyset_cursor_and_requires_admin_org()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                await Create(client, $"Faqosje Testzq {i}");
            }

            var first = await Json(await Send(client, HttpMethod.Get, $"{Base}?q=Faqosje%20Testzq&limit=2", Admin));
            var firstIds = first.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("publicId").GetGuid()).ToList();
            firstIds.Count.ShouldBe(2);
            var cursor = first.GetProperty("nextCursor").GetString()!;

            var second = await Json(await Send(client, HttpMethod.Get, $"{Base}?q=Faqosje%20Testzq&limit=2&after={Uri.EscapeDataString(cursor)}", Admin));
            var secondIds = second.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("publicId").GetGuid()).ToList();
            secondIds.Count.ShouldBe(1);
            secondIds.ShouldNotContain(x => firstIds.Contains(x));

            (await Send(client, HttpMethod.Get, Base, "avokat1@ash.local")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        finally
        {
            await CleanUp();
        }
    }

    [Fact]
    public async Task Institution_with_active_users_cannot_be_deactivated()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();

        // the seeded ministry has the dev institution users
        var seeded = (await SearchFor(client, "Ministria e Financave dhe Ekonomisë"))
            .First(r => r.GetProperty("name").GetString() == "Ministria e Financave dhe Ekonomisë")
            .GetProperty("publicId").GetGuid();

        var response = await Send(client, HttpMethod.Post, $"{Base}/{seeded}/deactivate", Admin);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCode(response)).ShouldBe("institution.has_active_users");
    }
}
