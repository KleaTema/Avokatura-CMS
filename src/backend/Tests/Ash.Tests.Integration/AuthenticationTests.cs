using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Identity;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>Runs against the test container, where the migrator seeded the @ash.local users.</summary>
public class AuthenticationTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string WhoAmIPath = "/api/test/whoami";
    private const string EndpointBuilderKey = "__EndpointRouteBuilder";

    private AshApiFactory CreateFactory(
        string environment, string authority = "https://idp.example.test", string audience = "ash-api") =>
        new(Db, environment, services => services.AddSingleton<IStartupFilter, WhoAmIStartupFilter>(),
            authority: authority, audience: audience);

    private static HttpRequestMessage Request(string? devUser)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, WhoAmIPath);
        if (devUser is not null)
        {
            request.Headers.Add("X-Dev-User", devUser);
        }

        return request;
    }

    [Fact]
    public async Task No_header_returns_401()
    {
        await using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Request(null), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Seeded_admin_returns_200_with_email_and_role()
    {
        await using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Request("admin@ash.local"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("email").GetString().ShouldBe("admin@ash.local");
        body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldContain("ADMIN");
    }

    [Fact]
    public async Task Unknown_email_returns_401()
    {
        await using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Request("nobody@ash.local"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Inactive_user_returns_401()
    {
        const string email = "inactive-test@ash.local";
        await ExecuteAsync(
            "INSERT INTO users (external_subject, email, first_name, last_name, is_active) " +
            $"VALUES ('dev-inactive-test', '{email}', 'Inactive', 'Test', 0)");
        try
        {
            await using var factory = CreateFactory("Development");
            using var client = factory.CreateClient();

            var response = await client.SendAsync(Request(email), TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await ExecuteAsync($"DELETE FROM users WHERE email = '{email}'");
        }
    }

    [Fact]
    public async Task Production_ignores_dev_header_and_returns_401()
    {
        await using var factory = CreateFactory("Production");
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Request("admin@ash.local"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void Production_without_auth_configuration_fails_at_startup()
    {
        using var factory = CreateFactory("Production", authority: "", audience: "");

        Should.Throw<InvalidOperationException>(() => factory.CreateClient());
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Maps the test-only endpoint into the host's global route builder, so it runs through the real pipeline.</summary>
    private sealed class WhoAmIStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseRouting();
            var routes = (IEndpointRouteBuilder)app.Properties[EndpointBuilderKey]!;
            routes.MapGet(WhoAmIPath, async (ICurrentUser user, IdentityDbContext db, CancellationToken ct) =>
            {
                var publicId = user.PublicId;
                var email = await db.Users.AsNoTracking()
                    .Where(u => u.PublicId == publicId)
                    .Select(u => u.Email)
                    .SingleAsync(ct);
                return Results.Ok(new { email, roles = user.Roles });
            }).RequireAuthorization();
            next(app);
        };
    }
}
