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
        string authority = "https://idp.example.test",
        string audience = "ash-api",
        StorageOptions? storage = null,
        string? scannerMode = null)
    {
        _environment = environment;
        _configureServices = configureServices;
        Environment.SetEnvironmentVariable("ConnectionStrings__App", db.AppConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__Audit", auditConnection ?? db.AuditConnectionString);
        Environment.SetEnvironmentVariable("Auth__Authority", authority);
        Environment.SetEnvironmentVariable("Auth__Audience", audience);
        // object storage (task 032): a MinIO container when given, otherwise not configured
        Environment.SetEnvironmentVariable("Storage__ServiceUrl", storage?.ServiceUrl ?? string.Empty);
        Environment.SetEnvironmentVariable("Storage__AccessKey", storage?.AccessKey);
        Environment.SetEnvironmentVariable("Storage__SecretKey", storage?.SecretKey);
        // the scan job (task 033) never runs on its own in tests: they call ScanQueueJob.RunOnceAsync
        Environment.SetEnvironmentVariable("Scanner__Job__Enabled", "false");
        Environment.SetEnvironmentVariable("Scanner__Mode", scannerMode);
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
