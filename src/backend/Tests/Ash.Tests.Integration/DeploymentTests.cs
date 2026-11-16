using System.Net.Http.Json;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>Task 052 — what the deployment relies on: the public login configuration, the reverse-proxy headers.</summary>
public class DeploymentTests(MySqlFixture db) : IntegrationTest(db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_anonymous_login_configuration_holds_public_values_only()
    {
        await using var factory = new AshApiFactory(Db, environment: "Production");
        using var client = factory.CreateClient();

        var config = await client.GetFromJsonAsync<JsonElement>("/api/auth/config", Ct);

        var b2c = config.GetProperty("providers").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "b2c");
        b2c.EnumerateObject().Select(p => p.Name).Order().ShouldBe(
            ["authority", "clientId", "displayName", "kind", "knownAuthorities", "name", "policy", "scopes"]);
        b2c.GetProperty("policy").GetString().ShouldBe("B2C_1_signin");
        b2c.GetProperty("knownAuthorities").EnumerateArray().Select(h => h.GetString()).ShouldBe(["ashtest.b2clogin.com"]);
        // the API's own validation settings are never sent (the scope legitimately names the API, the authority the tenant)
        var raw = config.GetRawText();
        foreach (var hidden in new[] { "\"audience\"", "\"validIssuers\"", "\"allowedTenantIds\"", "\"claimMap\"" })
        {
            raw.ShouldNotContain(hidden, Case.Insensitive);
        }
    }

    [Fact]
    public async Task Forwarded_headers_are_used_from_a_known_proxy_and_ignored_from_anyone_else()
    {
        try
        {
            await using var factory = new AshApiFactory(Db,
                configureServices: services => services.AddSingleton<IStartupFilter, RemoteIpStartupFilter>(),
                environmentVariables: new Dictionary<string, string?> { ["ReverseProxy__KnownProxies__0"] = "10.1.1.1" });
            using var client = factory.CreateClient();

            async Task<(string Ip, string Scheme)> From(string remote)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, RemoteIpStartupFilter.Path);
                request.Headers.Add(RemoteIpStartupFilter.Header, remote);
                request.Headers.Add("X-Forwarded-For", "203.0.113.5");
                request.Headers.Add("X-Forwarded-Proto", "https");
                var body = await (await client.SendAsync(request, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
                return (body.GetProperty("ip").GetString()!, body.GetProperty("scheme").GetString()!);
            }

            (await From("10.1.1.1")).ShouldBe(("203.0.113.5", "https")); // the known proxy: the client's address
            (await From("10.9.9.9")).ShouldBe(("10.9.9.9", "http"));     // anyone else: the headers are ignored
        }
        finally
        {
            Environment.SetEnvironmentVariable("ReverseProxy__KnownProxies__0", null);
        }
    }

    [Fact]
    public void The_startup_validator_lists_every_missing_setting_at_once()
    {
        var empty = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ash:Instances"] = "2" })
            .Build();
        var production = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Production" };

        var problems = Ash.Api.Configuration.StartupValidator.Problems(empty, production);

        foreach (var key in new[]
                 {
                     "ConnectionStrings:App", "ConnectionStrings:Audit", "ConnectionStrings:AuditDigest", "ConnectionStrings:Chat",
                     "ConnectionStrings:Reporting", "Storage:ServiceUrl", "Storage:SecretKey", "Scanner:ClamAv:Host",
                     "Email:SmtpHost", "Ash:Redis:ConnectionString",
                 })
        {
            problems.ShouldContain(p => p.StartsWith(key + " ", StringComparison.Ordinal));
        }

        Should.Throw<InvalidOperationException>(() => Ash.Api.Configuration.StartupValidator.Assert(empty, production))
            .Message.ShouldContain($"({problems.Count} problem(s))");
    }

    [Fact]
    public void AppSettings_Production_holds_no_secret()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "backend", "Host", "Ash.Api", "appsettings.Production.json")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("the repository root was not found");
        var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir.FullName, "src", "backend", "Host", "Ash.Api", "appsettings.Production.json")));

        static IEnumerable<string> Keys(JsonElement e, string prefix) => e.ValueKind == JsonValueKind.Object
            ? e.EnumerateObject().SelectMany(p => Keys(p.Value, prefix + p.Name + ":").Prepend(prefix + p.Name))
            : [];

        var secretWords = new[] { "connectionstrings", "password", "secret", "accesskey", "key", "token", "clientsecret" };
        Keys(json.RootElement, "").Where(k => secretWords.Any(w => k.Split(':')[^1].Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Version_needs_a_login_and_the_health_probes_do_not()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/version", Ct)).StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);
        (await client.GetAsync("/health/live", Ct)).StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        // ready: 200 or 503 depending on the test's dependencies — never a login prompt
        (await client.GetAsync("/health/ready", Ct)).StatusCode.ShouldBeOneOf(System.Net.HttpStatusCode.OK, System.Net.HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// Plays the network: the connection's address comes from a test header (set BEFORE the app's pipeline, where
    /// UseForwardedHeaders runs), and a test endpoint reports what the app then sees.
    /// </summary>
    private sealed class RemoteIpStartupFilter : IStartupFilter
    {
        public const string Path = "/api/test/remote";
        public const string Header = "X-Test-Remote-Ip";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(Header, out var ip))
                {
                    context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip.ToString());
                }

                await nextMiddleware(context);
            });
            app.UseRouting();
            var routes = (IEndpointRouteBuilder)app.Properties["__EndpointRouteBuilder"]!;
            routes.MapGet(Path, (HttpContext context) => Results.Ok(new
            {
                ip = context.Connection.RemoteIpAddress?.ToString(),
                scheme = context.Request.Scheme,
            })).AllowAnonymous();
            next(app);
        };
    }
}
