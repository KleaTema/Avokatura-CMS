using System.Net;
using Ash.Tests.Integration.Infrastructure;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>Task 051, item 4 — the API's security headers, HSTS only over HTTPS outside Development, CORS only for the configured origin.</summary>
public class SecurityHeadersTests(MySqlFixture db) : IntegrationTest(db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_api_answer_carries_the_security_headers_and_no_store()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("kryetar@ash.local");

        foreach (var path in new[] { "/api/identity/me", "/api/cases?limit=5", "/api/does-not-exist" })
        {
            var response = await client.GetAsync(path, Ct);
            response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"], path);
            response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"], path);
            response.Headers.GetValues("Permissions-Policy").Single().ShouldBe("camera=(), microphone=(), geolocation=()", path);
            response.Headers.GetValues("Content-Security-Policy").Single().ShouldBe("default-src 'none'; frame-ancestors 'none'", path);
            response.Headers.CacheControl!.NoStore.ShouldBeTrue(path);
        }

        // an anonymous 401 too
        using var anonymous = factory.CreateClient();
        var unauthorized = await anonymous.GetAsync("/api/identity/me", Ct);
        unauthorized.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unauthorized.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        // Development over http: no HSTS
        unauthorized.Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
    }

    [Fact]
    public async Task Production_sends_HSTS_over_https_only()
    {
        await using var factory = new AshApiFactory(Db, environment: "Production");
        using var https = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://ash.example.test"),
        });
        (await https.GetAsync("/api/auth/config", Ct)).Headers.GetValues("Strict-Transport-Security").Single().ShouldContain("max-age=31536000");

        using var http = factory.CreateClient();
        (await http.GetAsync("/api/auth/config", Ct)).Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
    }

    [Fact]
    public async Task Cors_answers_only_the_configured_origin_and_nothing_when_none_is_configured()
    {
        await using (var none = new AshApiFactory(Db))
        {
            using var client = none.CreateClient();
            var preflight = Preflight("https://evil.example.test");
            (await client.SendAsync(preflight, Ct)).Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        }

        try
        {
            await using var configured = new AshApiFactory(Db, environmentVariables: new Dictionary<string, string?>
            {
                ["Cors__AllowedOrigins__0"] = "https://ash.example.test",
            });
            using var client = configured.CreateClient();
            var allowed = await client.SendAsync(Preflight("https://ash.example.test"), Ct);
            allowed.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["https://ash.example.test"]);
            var other = await client.SendAsync(Preflight("https://evil.example.test"), Ct);
            other.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", null);
        }
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/identity/me");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");
        return request;
    }
}
