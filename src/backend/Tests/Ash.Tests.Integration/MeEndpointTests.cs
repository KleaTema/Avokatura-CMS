using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Api.Auth;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>Runs against the test container (seeded @ash.local users).</summary>
public class MeEndpointTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string AdminOnlyPath = "/api/test/admin-only";
    private const string EndpointBuilderKey = "__EndpointRouteBuilder";

    private AshApiFactory CreateFactory() =>
        new(Db, configureServices: services => services.AddSingleton<IStartupFilter, AdminOnlyStartupFilter>());

    private static HttpRequestMessage Request(string path, string? devUser)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (devUser is not null)
        {
            request.Headers.Add("X-Dev-User", devUser);
        }

        return request;
    }

    public static TheoryData<string, string[]> MenusPerUser() => new()
    {
        { "protokoll", ["dashboard", "cases", "notifications", "profile"] },
        { "kryetar", ["dashboard", "cases", "chat", "notifications", "profile", "agenda", "reports"] },
        { "avokat1", ["dashboard", "cases", "chat", "notifications", "profile", "agenda", "reports"] },
        { "ndihmes", ["dashboard", "cases", "chat", "notifications", "profile", "agenda", "reports"] },
        { "monitor", ["dashboard", "cases", "chat", "notifications", "profile", "agenda", "reports"] },
        { "institucion", ["dashboard", "cases", "chat", "notifications", "profile", "agenda", "reports"] },
        { "jurist", ["dashboard", "cases", "chat", "notifications", "profile", "agenda", "reports"] },
        { "admin", ["dashboard", "cases", "chat", "notifications", "profile", "agenda", "reports", "admin"] }
    };

    [Theory]
    [MemberData(nameof(MenusPerUser))]
    public async Task Me_returns_expected_menus(string user, string[] expectedMenus)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(
            Request("/api/identity/me", $"{user}@ash.local"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("email").GetString().ShouldBe($"{user}@ash.local");
        body.GetProperty("publicId").GetGuid().ShouldNotBe(Guid.Empty);
        body.GetProperty("menus").EnumerateArray().Select(m => m.GetString()).ShouldBe(expectedMenus);
        body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .ShouldContain($"menu.{expectedMenus[0]}");
        body.GetProperty("roles").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Me_returns_office_for_staff_and_institution_for_institution_users()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var staff = await GetMe(client, "avokat1@ash.local");
        staff.GetProperty("office").GetProperty("publicId").GetGuid().ShouldNotBe(Guid.Empty);
        staff.GetProperty("institution").ValueKind.ShouldBe(JsonValueKind.Null);

        var inst = await GetMe(client, "institucion@ash.local");
        inst.GetProperty("institution").GetProperty("publicId").GetGuid().ShouldNotBe(Guid.Empty);
        inst.GetProperty("office").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Seeded_kryetar_gets_200_from_identity_me()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().AsUser("kryetar@ash.local");

        var response = await client.GetAsync("/api/identity/me", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Me_without_header_returns_401()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Request("/api/identity/me", null), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("admin@ash.local", HttpStatusCode.OK)]
    [InlineData("protokoll@ash.local", HttpStatusCode.Forbidden)]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    public async Task RequirePermission_admin_users(string? user, HttpStatusCode expected)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Request(AdminOnlyPath, user), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(expected);
    }

    private static async Task<JsonElement> GetMe(HttpClient client, string user)
    {
        var response = await client.SendAsync(Request("/api/identity/me", user), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private sealed class AdminOnlyStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseRouting();
            var routes = (IEndpointRouteBuilder)app.Properties[EndpointBuilderKey]!;
            routes.MapGet(AdminOnlyPath, () => Results.Ok()).RequirePermission("admin.users");
            next(app);
        };
    }
}
