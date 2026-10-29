using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>Office administration (task 015). Every test creates its own offices/users and deletes them in finally.</summary>
public class AdminOfficesTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Base = "/api/identity/admin/offices";
    private const string Admin = "admin@ash.local";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly List<string> _codes = [];

    private string NewCode()
    {
        var code = $"T{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        _codes.Add(code);
        return code;
    }

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

    private static async Task<string> Title(HttpResponseMessage response) =>
        (await Json(response)).GetProperty("title").GetString()!;

    private async Task<Guid> CreateOffice(HttpClient client, string? name = null, Guid? parent = null)
    {
        var code = NewCode();
        var response = await Send(client, HttpMethod.Post, Base, Admin,
            new { code, name = name ?? $"Zyra {code}", officeType = "LOCAL", parentOfficeId = parent });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await Json(response)).GetProperty("publicId").GetGuid();
    }

    private static async Task<Guid> CreateUser(HttpClient client, Guid office, params string[] roles)
    {
        var response = await Send(client, HttpMethod.Post, "/api/identity/admin/users", Admin, new
        {
            email = $"office-{Guid.NewGuid():N}@ash.test",
            firstName = "Test",
            lastName = "Kryetar",
            officeId = office,
            institutionId = (Guid?)null,
            roles
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await Json(response)).GetProperty("publicId").GetGuid();
    }

    private static Task<HttpResponseMessage> SetHead(HttpClient client, Guid office, Guid? user) =>
        Send(client, HttpMethod.Put, $"{Base}/{office}/head", Admin, new { userPublicId = user });

    private async Task CleanUp()
    {
        if (_codes.Count == 0)
        {
            return;
        }

        var codes = string.Join(",", _codes.Select(c => $"'{c}'"));
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        foreach (var sql in new[]
                 {
                     $"UPDATE offices SET head_user_id = NULL, parent_office_id = NULL WHERE code IN ({codes})",
                     $"DELETE ur FROM user_roles ur JOIN users u ON u.id = ur.user_id JOIN offices o ON o.id = u.office_id WHERE o.code IN ({codes})",
                     $"DELETE u FROM users u JOIN offices o ON o.id = u.office_id WHERE o.code IN ({codes})",
                     $"DELETE FROM offices WHERE code IN ({codes})"
                 })
        {
            await using var command = new MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Admin_can_create_list_and_update_an_office_but_not_its_code()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        try
        {
            var parent = await CreateOffice(client, "Zyra mëmë test");
            var office = await CreateOffice(client, "Zyra test", parent);

            var list = await Json(await Send(client, HttpMethod.Get, Base, Admin));
            var row = list.EnumerateArray().Single(x => x.GetProperty("publicId").GetGuid() == office);
            row.GetProperty("parent").GetProperty("publicId").GetGuid().ShouldBe(parent);
            row.GetProperty("isActive").GetBoolean().ShouldBeTrue();
            var code = row.GetProperty("code").GetString();

            var update = await Send(client, HttpMethod.Put, $"{Base}/{office}", Admin,
                new { code = "CHANGED", name = "Zyra test e riemërtuar", officeType = "REPRESENTATION", parentOfficeId = (Guid?)null });
            update.StatusCode.ShouldBe(HttpStatusCode.OK);
            var updated = await Json(update);
            updated.GetProperty("name").GetString().ShouldBe("Zyra test e riemërtuar");
            updated.GetProperty("officeType").GetString().ShouldBe("REPRESENTATION");
            updated.GetProperty("code").GetString().ShouldBe(code);
            updated.GetProperty("parent").ValueKind.ShouldBe(JsonValueKind.Null);
        }
        finally
        {
            await CleanUp();
        }
    }

    [Theory]
    [InlineData("T 12")]
    [InlineData("tr12")]
    [InlineData("A")]
    [InlineData("ABCDEFGHIJK")]
    public async Task Invalid_office_code_is_rejected_with_400(string code)
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();

        var response = await Send(client, HttpMethod.Post, Base, Admin,
            new { code, name = "Zyra kod i gabuar", officeType = "LOCAL", parentOfficeId = (Guid?)null });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(response)).GetProperty("code").GetString().ShouldBe("office.code_invalid");
    }

    [Fact]
    public async Task Parent_cycle_is_rejected_with_400()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        try
        {
            var a = await CreateOffice(client);
            var b = await CreateOffice(client, parent: a);

            var cycle = await Send(client, HttpMethod.Put, $"{Base}/{a}", Admin,
                new { name = "A", officeType = "LOCAL", parentOfficeId = b });
            cycle.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await Json(cycle)).GetProperty("code").GetString().ShouldBe("office.parent_cycle");

            var self = await Send(client, HttpMethod.Put, $"{Base}/{a}", Admin,
                new { name = "A", officeType = "LOCAL", parentOfficeId = a });
            self.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        finally
        {
            await CleanUp();
        }
    }

    [Fact]
    public async Task Head_without_OFFICE_HEAD_role_is_rejected_with_400()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        try
        {
            var office = await CreateOffice(client);
            var lawyer = await CreateUser(client, office, "STATE_LAWYER");

            var response = await SetHead(client, office, lawyer);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await Json(response)).GetProperty("code").GetString().ShouldBe("office.head_role_missing");
        }
        finally
        {
            await CleanUp();
        }
    }

    [Fact]
    public async Task Head_from_another_office_is_rejected_with_400()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        try
        {
            var office = await CreateOffice(client);
            var other = await CreateOffice(client);
            var otherHead = await CreateUser(client, other, "OFFICE_HEAD");

            var response = await SetHead(client, office, otherHead);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await Json(response)).GetProperty("code").GetString().ShouldBe("office.head_other_office");
        }
        finally
        {
            await CleanUp();
        }
    }

    [Fact]
    public async Task Head_is_protected_from_deactivation_role_revoke_and_office_move()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        try
        {
            var office = await CreateOffice(client, "Zyra me Kryetar test");
            var other = await CreateOffice(client);
            var head = await CreateUser(client, office, "OFFICE_HEAD", "STATE_LAWYER");
            (await SetHead(client, office, head)).StatusCode.ShouldBe(HttpStatusCode.OK);
            const string expected = "Ky përdorues është Kryetar i zyrës Zyra me Kryetar test. Caktoni fillimisht një Kryetar tjetër.";

            var deactivate = await Send(client, HttpMethod.Post, $"/api/identity/admin/users/{head}/deactivate", Admin);
            deactivate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await Title(deactivate)).ShouldBe(expected);

            var revoke = await Send(client, HttpMethod.Delete, $"/api/identity/admin/users/{head}/roles/OFFICE_HEAD", Admin);
            revoke.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await Title(revoke)).ShouldBe(expected);

            var user = await Json(await Send(client, HttpMethod.Get, $"/api/identity/admin/users?q=Kryetar&limit=200", Admin));
            var email = user.GetProperty("items").EnumerateArray()
                .Single(x => x.GetProperty("publicId").GetGuid() == head).GetProperty("email").GetString();
            var move = await Send(client, HttpMethod.Put, $"/api/identity/admin/users/{head}", Admin,
                new { email, firstName = "Test", lastName = "Kryetar", officeId = other, institutionId = (Guid?)null });
            move.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await Title(move)).ShouldBe(expected);

            // revoking a role other than OFFICE_HEAD is still allowed
            (await Send(client, HttpMethod.Delete, $"/api/identity/admin/users/{head}/roles/STATE_LAWYER", Admin))
                .StatusCode.ShouldBe(HttpStatusCode.OK);

            // after clearing the head, the user can be deactivated
            (await SetHead(client, office, null)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await Send(client, HttpMethod.Post, $"/api/identity/admin/users/{head}/deactivate", Admin))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            await CleanUp();
        }
    }

    [Fact]
    public async Task Office_with_active_users_cannot_be_deactivated()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        try
        {
            var office = await CreateOffice(client);
            await CreateUser(client, office, "STATE_LAWYER");

            var response = await Send(client, HttpMethod.Post, $"{Base}/{office}/deactivate", Admin);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var problem = await Json(response);
            problem.GetProperty("code").GetString().ShouldBe("office.has_active_users");
            problem.GetProperty("title").GetString()!.ShouldContain("1 përdorues aktivë");

            var empty = await CreateOffice(client);
            (await Send(client, HttpMethod.Post, $"{Base}/{empty}/deactivate", Admin)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            await CleanUp();
        }
    }

    [Fact]
    public async Task Non_admin_cannot_manage_offices()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();

        (await Send(client, HttpMethod.Get, Base, "avokat1@ash.local")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Send(client, HttpMethod.Post, Base, "avokat1@ash.local",
                new { code = "NOPE", name = "X", officeType = "LOCAL", parentOfficeId = (Guid?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
