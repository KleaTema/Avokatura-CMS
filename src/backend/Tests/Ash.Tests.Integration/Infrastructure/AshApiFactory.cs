using Ash.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ash.Tests.Integration.Infrastructure;

/// <summary>The real API host pointed at the test container. Environment variables are used because the host reads them before overrides apply.</summary>
public sealed class AshApiFactory : WebApplicationFactory<Program>
{
    private readonly string _environment;
    private readonly Action<IServiceCollection>? _configureServices;

    public AshApiFactory(
        MySqlFixture db,
        string environment = "Development",
        Action<IServiceCollection>? configureServices = null,
        string? auditConnection = null,
        StorageOptions? storage = null,
        string? scannerMode = null,
        TestIdentity.Providers? providers = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null)
    {
        _environment = environment;
        // task 051: the test identity providers (test signing key, no network) — in every environment
        _configureServices = services =>
        {
            TestIdentity.UseTestKeys(services);
            // task 051: every log event of the run, for the secrets scan (SecretsTests)
            services.AddSingleton<Serilog.Core.ILogEventSink>(SuiteLogSink.Instance);
            configureServices?.Invoke(services);
        };
        TestIdentity.Configure(providers ?? TestIdentity.Providers.Both);
        Environment.SetEnvironmentVariable("DevFeatures__HeaderAuth", null);
        Environment.SetEnvironmentVariable("DevFeatures__JobRunEndpoint", null);
        // task 051: outside Development the API demands encrypted connections (the test MySQL 8 has SSL on)
        string Ssl(string connection) => environment == "Development"
            ? connection
            : new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(connection) { SslMode = MySql.Data.MySqlClient.MySqlSslMode.Required }.ConnectionString;
        Environment.SetEnvironmentVariable("ConnectionStrings__App", Ssl(db.AppConnectionString));
        Environment.SetEnvironmentVariable("ConnectionStrings__Audit", Ssl(auditConnection ?? db.AuditConnectionString));
        // object storage (task 032): a MinIO container when given, otherwise not configured
        Environment.SetEnvironmentVariable("Storage__ServiceUrl", storage?.ServiceUrl ?? string.Empty);
        Environment.SetEnvironmentVariable("Storage__AccessKey", storage?.AccessKey);
        Environment.SetEnvironmentVariable("Storage__SecretKey", storage?.SecretKey);
        // the scan job (task 033) never runs on its own in tests: they call ScanQueueJob.RunOnceAsync
        Environment.SetEnvironmentVariable("Scanner__Job__Enabled", "false");
        Environment.SetEnvironmentVariable("Scanner__Mode", scannerMode);
        // background jobs (task 041) never run on their own in tests: a test runs a job explicitly (JobRunner/RunAsync)
        Environment.SetEnvironmentVariable("Jobs__Enabled", "false");
        // e-mail (task 053c) stays in email_outbox: a test runs EmailDispatcher.RunOnceAsync with a fake sender
        Environment.SetEnvironmentVariable("Email__Enabled", "false");
        Environment.SetEnvironmentVariable("ConnectionStrings__AuditDigest", Ssl(db.AuditDigestConnectionString));
        Environment.SetEnvironmentVariable("ConnectionStrings__Chat", Ssl(db.ChatConnectionString));
        // task 052: outside Development the startup validator wants every required setting — a test host without a
        // MinIO gets a placeholder endpoint (never contacted by those tests) and the reporting connection
        var production = environment != "Development";
        Environment.SetEnvironmentVariable("ConnectionStrings__Reporting", production ? Ssl(db.AppConnectionString) : null);
        if (production && storage is null)
        {
            Environment.SetEnvironmentVariable("Storage__ServiceUrl", "http://storage.test.invalid");
            Environment.SetEnvironmentVariable("Storage__AccessKey", "test");
            Environment.SetEnvironmentVariable("Storage__SecretKey", "test");
        }
        // task 051: tests are not throttled (they run many calls in a second) — HardeningTests sets its own small limits
        foreach (var policy in new[] { "upload", "export", "search", "crossOffice", "notify", "hub" })
        {
            Environment.SetEnvironmentVariable($"RateLimits__{policy}__PermitLimit", "100000");
            Environment.SetEnvironmentVariable($"RateLimits__{policy}__WindowSeconds", "60");
        }

        // a test's own variables last, so they win over the defaults above
        foreach (var (name, value) in environmentVariables ?? new Dictionary<string, string?>())
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    /// <summary>
    /// Every test request gives up after this long (HttpClient's default is 100 s): a request that never completes fails
    /// its test instead of stalling the run (the blame hang timeout is the last resort). Uploads of the largest test
    /// files take a few seconds, so 30 s, not less.
    /// </summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.Timeout = RequestTimeout;
    }

    protected override IHost CreateHost(IHostBuilder builder) =>
        Timing.Measure("WebApplicationFactory host creation", () => base.CreateHost(builder));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        if (_configureServices is not null)
        {
            builder.ConfigureServices(_configureServices);
        }
    }
}
