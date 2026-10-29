using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Modules.Identity.Application.ResolveUser;
using Ash.Modules.Identity.Contracts;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>Admin user management (task 013) against the real host and the test container.</summary>
public class AdminUsersTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Base = "/api/identity/admin/users";
    private const string Admin = "admin@ash.local";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewEmail() => $"adm-{Guid.NewGuid():N}@ash.test";

    private static async Task<Guid> OfficeIdAsync(HttpClient client)
    {
        var me = await Get(client, "/api/identity/me", "avokat1@ash.local");
        return me.GetProperty("office").GetProperty("publicId").GetGuid();
    }

    private static async Task<Guid> InstitutionIdAsync(HttpClient client)
    {
        var me = await Get(client, "/api/identity/me", "institucion@ash.local");
        return me.GetProperty("institution").GetProperty("publicId").GetGuid();
    }

    private static async Task<Guid> PublicIdOfAsync(HttpClient client, string user) =>
        (await Get(client, "/api/identity/me", user)).GetProperty("publicId").GetGuid();

    private static async Task<JsonElement> Get(HttpClient client, string path, string user)
    {
        var response = await Send(client, HttpMethod.Get, path, user);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await Json(response);
    }

    private static async Task<HttpResponseMessage> Send(
        HttpClient client, HttpMethod method, string path, string user, object? body = null)
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

    private static async Task<Guid> CreateAsync(
        HttpClient client, string email, Guid? office, Guid? institution, params string[] roles)
    {
        var response = await Send(client, HttpMethod.Post, Base, Admin,
            new { email, firstName = "Test", lastName = "User", officeId = office, institutionId = institution, roles });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await Json(response)).GetProperty("publicId").GetGuid();
    }

    private static async Task<object?> ScalarAsync(string sql, string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    [Fact]
    public async Task User_directory_filters_by_role()
    {
        await using var factory = new AshApiFactory(Db);
        await using var scope = factory.Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        // "@ash.local" keeps it to the seeded dev accounts; other tests add STATE_LAWYER users under @ash.test
        var lawyers = await directory.FindUsers("@ash.local", "STATE_LAWYER", Ct);

        lawyers.Select(u => u.Email).OrderBy(e => e).ShouldBe(["avokat1@ash.local", "avokat2@ash.local"]);
    }

    [Fact]
    public async Task Admin_permission_is_required()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();

        (await Send(client, HttpMethod.Get, Base, "protokoll@ash.local")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Send(client, HttpMethod.Get, Base, Admin)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Roles_list_returns_all_roles_with_scope_and_requires_admin_permission()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();

        (await Send(client, HttpMethod.Get, "/api/identity/admin/roles", "avokat1@ash.local"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var roles = (await Get(client, "/api/identity/admin/roles", Admin)).EnumerateArray().ToList();
        roles.Count.ShouldBe(8);
        var scopes = roles.ToDictionary(r => r.GetProperty("code").GetString()!, r => r.GetProperty("scope").GetString());
        scopes["STATE_LAWYER"].ShouldBe("OFFICE");
        scopes["ADMIN"].ShouldBe("OFFICE");
        scopes["INSTITUTION_USER"].ShouldBe("INSTITUTION");
        scopes["INSTITUTION_JURIST"].ShouldBe("INSTITUTION");
        roles.ShouldAllBe(r => !string.IsNullOrWhiteSpace(r.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Create_stores_lower_case_email_and_rejects_duplicates()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        var office = await OfficeIdAsync(client);
        var email = NewEmail();

        var response = await Send(client, HttpMethod.Post, Base, Admin,
            new { email = email.ToUpperInvariant(), firstName = "A", lastName = "B", officeId = office, roles = new[] { "MONITOR" } });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Json(response)).GetProperty("email").GetString().ShouldBe(email);

        var duplicate = await Send(client, HttpMethod.Post, Base, Admin,
            new { email = email.ToUpperInvariant(), firstName = "A", lastName = "B", officeId = office });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Exactly_one_of_office_or_institution_is_required(bool office, bool institution)
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        Guid? officeId = office ? await OfficeIdAsync(client) : null;
        Guid? institutionId = institution ? await InstitutionIdAsync(client) : null;

        var response = await Send(client, HttpMethod.Post, Base, Admin,
            new { email = NewEmail(), firstName = "A", lastName = "B", officeId, institutionId });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Roles_must_match_the_kind_of_user()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        var office = await OfficeIdAsync(client);
        var institution = await InstitutionIdAsync(client);

        var officeWithInstitutionRole = await Send(client, HttpMethod.Post, Base, Admin,
            new { email = NewEmail(), firstName = "A", lastName = "B", officeId = office, roles = new[] { "INSTITUTION_USER" } });
        var institutionWithAshRole = await Send(client, HttpMethod.Post, Base, Admin,
            new { email = NewEmail(), firstName = "A", lastName = "B", institutionId = institution, roles = new[] { "STATE_LAWYER" } });
        officeWithInstitutionRole.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        institutionWithAshRole.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var officeUser = await CreateAsync(client, NewEmail(), office, null);
        var institutionUser = await CreateAsync(client, NewEmail(), null, institution);
        var grantInstitutionRole = await Send(client, HttpMethod.Post, $"{Base}/{officeUser}/roles", Admin, new { role = "INSTITUTION_JURIST" });
        var grantAshRole = await Send(client, HttpMethod.Post, $"{Base}/{institutionUser}/roles", Admin, new { role = "MONITOR" });
        grantInstitutionRole.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        grantAshRole.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_filters_and_pages_with_keyset_and_public_ids()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        var office = await OfficeIdAsync(client);
        var marker = Guid.NewGuid().ToString("N")[..8];
        var first = await CreateAsync(client, $"lst-a-{marker}@ash.test", office, null, "MONITOR");
        var second = await CreateAsync(client, $"lst-b-{marker}@ash.test", office, null, "MONITOR");
        await Send(client, HttpMethod.Post, $"{Base}/{second}/deactivate", Admin);

        var page1 = await Json(await Send(client, HttpMethod.Get, $"{Base}?q={marker}&limit=1", Admin));
        page1.GetProperty("items").GetArrayLength().ShouldBe(1);
        page1.GetProperty("items")[0].GetProperty("publicId").GetGuid().ShouldBe(first);
        page1.GetProperty("items")[0].TryGetProperty("id", out _).ShouldBeFalse();
        var cursor = page1.GetProperty("nextCursor").GetString();
        cursor.ShouldNotBeNull();

        var page2 = await Json(await Send(client, HttpMethod.Get,
            $"{Base}?q={marker}&limit=1&after={Uri.EscapeDataString(cursor)}", Admin));
        page2.GetProperty("items")[0].GetProperty("publicId").GetGuid().ShouldBe(second);

        var inactive = await Json(await Send(client, HttpMethod.Get, $"{Base}?q={marker}&active=false", Admin));
        inactive.GetProperty("items").GetArrayLength().ShouldBe(1);
        var byRole = await Json(await Send(client, HttpMethod.Get, $"{Base}?q={marker}&role=STATE_LAWYER", Admin));
        byRole.GetProperty("items").GetArrayLength().ShouldBe(0);
        var byOffice = await Json(await Send(client, HttpMethod.Get, $"{Base}?q={marker}&officeId={office}", Admin));
        byOffice.GetProperty("items").GetArrayLength().ShouldBe(2);
        var byInstitution = await Json(await Send(client, HttpMethod.Get,
            $"{Base}?q={marker}&institutionId={await InstitutionIdAsync(client)}", Admin));
        byInstitution.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Admin_cannot_deactivate_self_or_remove_own_admin_role()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        var me = await PublicIdOfAsync(client, Admin);

        var deactivate = await Send(client, HttpMethod.Post, $"{Base}/{me}/deactivate", Admin);
        var revoke = await Send(client, HttpMethod.Delete, $"{Base}/{me}/roles/ADMIN", Admin);

        deactivate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(deactivate)).GetProperty("code").GetString().ShouldBe("user.cannot_deactivate_self");
        revoke.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(revoke)).GetProperty("code").GetString().ShouldBe("user.cannot_remove_own_admin");
    }

    [Fact]
    public async Task Last_active_admin_cannot_be_deactivated_or_lose_the_role()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        var office = await OfficeIdAsync(client);
        var actor = NewEmail();
        var targetEmail = NewEmail();
        await CreateAsync(client, actor, office, null, "ADMIN");
        var target = await CreateAsync(client, targetEmail, office, null, "ADMIN");

        // Warm the actor's 60 s access cache, then make the target the only ACTIVE admin in the database.
        await Get(client, "/api/identity/me", actor);
        await ExecuteAsync("UPDATE users SET is_active = 0 WHERE email = 'admin@ash.local'");
        await ExecuteAsync($"DELETE ur FROM user_roles ur JOIN users u ON u.id = ur.user_id WHERE u.email = '{actor}'");
        try
        {
            var deactivate = await Send(client, HttpMethod.Post, $"{Base}/{target}/deactivate", actor);
            var revoke = await Send(client, HttpMethod.Delete, $"{Base}/{target}/roles/ADMIN", actor);

            deactivate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await Json(deactivate)).GetProperty("code").GetString().ShouldBe("user.last_admin");
            revoke.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await Json(revoke)).GetProperty("code").GetString().ShouldBe("user.last_admin");
        }
        finally
        {
            await ExecuteAsync("UPDATE users SET is_active = 1 WHERE email = 'admin@ash.local'");
            await ExecuteAsync($"UPDATE users SET is_active = 0 WHERE email IN ('{actor}', '{targetEmail}')");
        }
    }

    [Fact]
    public async Task Deactivation_response_contains_the_current_assignment_count()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        var lawyer = await PublicIdOfAsync(client, "avokat1@ash.local");

        try
        {
            var response = await Send(client, HttpMethod.Post, $"{Base}/{lawyer}/deactivate", Admin);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await Json(response);
            body.GetProperty("isActive").GetBoolean().ShouldBeFalse();
            body.GetProperty("currentAssignments").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        }
        finally
        {
            await Send(client, HttpMethod.Post, $"{Base}/{lawyer}/activate", Admin);
        }
    }

    [Fact]
    public async Task Role_and_status_changes_apply_immediately_despite_the_access_cache()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        var office = await OfficeIdAsync(client);
        var email = NewEmail();
        var id = await CreateAsync(client, email, office, null, "MONITOR");

        var before = await Get(client, "/api/identity/me", email);
        before.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldBe(["MONITOR"]);

        (await Send(client, HttpMethod.Post, $"{Base}/{id}/roles", Admin, new { role = "STATE_LAWYER" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var granted = await Get(client, "/api/identity/me", email);
        granted.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldContain("STATE_LAWYER");

        (await Send(client, HttpMethod.Delete, $"{Base}/{id}/roles/STATE_LAWYER", Admin)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var revoked = await Get(client, "/api/identity/me", email);
        revoked.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldNotContain("STATE_LAWYER");

        (await Send(client, HttpMethod.Post, $"{Base}/{id}/deactivate", Admin)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Send(client, HttpMethod.Get, "/api/identity/me", email)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Every_change_writes_admin_change_audit_with_old_and_new_values()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        var office = await OfficeIdAsync(client);
        var email = NewEmail();
        var id = await CreateAsync(client, email, office, null, "MONITOR");
        (await Send(client, HttpMethod.Post, $"{Base}/{id}/roles", Admin, new { role = "STATE_LAWYER" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Send(client, HttpMethod.Put, $"{Base}/{id}", Admin,
            new { email, firstName = "New", lastName = "Name", officeId = office })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Send(client, HttpMethod.Post, $"{Base}/{id}/deactivate", Admin)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var userId = Convert.ToInt64(await ScalarAsync($"SELECT id FROM users WHERE email = '{email}'", Db.AppConnectionString));
        var countSql = $"SELECT COUNT(*) FROM ash_audit.audit_log WHERE action = 'ADMIN_CHANGE' AND entity_type = 'USER' AND entity_id = {userId}";
        var deadline = DateTime.UtcNow.AddSeconds(15);
        long count;
        while ((count = Convert.ToInt64(await ScalarAsync(countSql, Db.AuditConnectionString))) < 4 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, Ct);
        }

        count.ShouldBe(4);
        var details = (string)(await ScalarAsync(
            $"SELECT details FROM ash_audit.audit_log WHERE action = 'ADMIN_CHANGE' AND entity_id = {userId} " +
            "AND JSON_UNQUOTE(JSON_EXTRACT(details, '$.action')) = 'USER_DEACTIVATED'",
            Db.AuditConnectionString))!;
        using var doc = JsonDocument.Parse(details);
        doc.RootElement.GetProperty("old").GetProperty("IsActive").GetBoolean().ShouldBeTrue();
        doc.RootElement.GetProperty("new").GetProperty("IsActive").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task First_login_links_by_email_when_subject_is_null_then_resolves_by_subject()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();
        var office = await OfficeIdAsync(client);
        var email = NewEmail();
        await CreateAsync(client, email, office, null, "MONITOR");
        var subject = $"sub-{Guid.NewGuid():N}";
        var subjectSql = $"SELECT external_subject FROM users WHERE email = '{email}'";

        (await ScalarAsync(subjectSql, Db.AppConnectionString)).ShouldBe(DBNull.Value);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<ResolveUserHandler>();
            (await handler.Handle(new ResolveUserRequest(subject, email.ToUpperInvariant()), Ct)).IsSuccess.ShouldBeTrue();
        }

        (await ScalarAsync(subjectSql, Db.AppConnectionString)).ShouldBe(subject);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<ResolveUserHandler>();
            (await handler.Handle(new ResolveUserRequest(subject, null), Ct)).IsSuccess.ShouldBeTrue();
            (await handler.Handle(new ResolveUserRequest("other-subject", email), Ct)).IsSuccess.ShouldBeFalse();
        }
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }
}
