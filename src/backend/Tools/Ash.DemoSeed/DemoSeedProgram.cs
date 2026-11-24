using Ash.Modules.Documents.Infrastructure.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;

namespace Ash.DemoSeed;

/// <summary>
/// The entry point: dotnet run --project src/backend/Tools/Ash.DemoSeed. Refuses outside Development, on a connection
/// that is not localhost, and on a database that already has cases; then seeds through the API and prints the table.
/// </summary>
public static class DemoSeedProgram
{
    public const string EmptyDatabaseHint = "run docker compose down -v + migrator first";

    public static async Task<int> Main() =>
        await RunAsync(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), Console.Out);

    public static async Task<int> RunAsync(string? environment, TextWriter output)
    {
        if (DemoSeedGuard.Environment(environment) is { } notDevelopment)
        {
            await output.WriteLineAsync(notDevelopment);
            return 1;
        }

        // background jobs, the scan loop and e-mail stay off while the seed moves the clock
        System.Environment.SetEnvironmentVariable("Jobs__Enabled", "false");
        System.Environment.SetEnvironmentVariable("Scanner__Job__Enabled", "false");
        System.Environment.SetEnvironmentVariable("Email__Enabled", "false");

        var clock = new DemoClock();
        await using var host = new DemoSeedHost(clock);
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        var connection = configuration.GetConnectionString("App");
        if (DemoSeedGuard.Connection(connection) is { } notLocal)
        {
            await output.WriteLineAsync(notLocal);
            return 1;
        }

        if (await DemoSeedGuard.EmptyAsync(connection!) is { } notEmpty)
        {
            await output.WriteLineAsync(notEmpty);
            return 1;
        }

        var result = await SeedAsync(host, clock, output);
        await output.WriteLineAsync();
        await output.WriteLineAsync(DemoSeedTable.Render(result));
        return 0;
    }

    /// <summary>The seed on an API host (the program's own, or a test's).</summary>
    public static Task<DemoSeedResult> SeedAsync(WebApplicationFactory<Program> host, DemoClock clock, TextWriter output)
    {
        var api = new DemoApi(host.CreateClient);
        var scan = host.Services.GetRequiredService<ScanQueueJob>();
        return new DemoSeeder(api, clock, async () => await scan.RunOnceAsync(), output).RunAsync();
    }
}

/// <summary>The real API host (Development, the developer's user secrets: localhost MySQL and MinIO) with the demo clock.</summary>
internal sealed class DemoSeedHost(DemoClock clock) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services => DemoSeedServices.Configure(services, clock));
    }
}

/// <summary>The three refusals, each with its message (tested on their own).</summary>
public static class DemoSeedGuard
{
    public static string? Environment(string? environment) =>
        string.Equals(environment, "Development", StringComparison.Ordinal)
            ? null
            : $"The demo seed runs only with ASPNETCORE_ENVIRONMENT=Development (now: {environment ?? "not set"}).";

    public static string? Connection(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "The demo seed needs ConnectionStrings:App (the API's user secrets).";
        }

        var server = new MySqlConnectionStringBuilder(connectionString).Server;
        return server is "localhost" or "127.0.0.1" or "::1"
            ? null
            : $"The demo seed runs only against localhost (ConnectionStrings:App points to {server}).";
    }

    public static async Task<string?> EmptyAsync(string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand("SELECT COUNT(*) FROM ash_core.cases", connection);
        var count = Convert.ToInt64(await command.ExecuteScalarAsync());
        return count == 0 ? null : $"ash_core.cases is not empty ({count} cases): {DemoSeedProgram.EmptyDatabaseHint}.";
    }
}

/// <summary>The table printed at the end: label → case code → office → primary lawyer.</summary>
public static class DemoSeedTable
{
    private static readonly string[] Labels = ["D", "E", "F", "ARCHIVED", "DELEGATED", "X/2"];

    public static string Render(DemoSeedResult result)
    {
        var rows = Labels.Select(l => result.Cases.Single(c => c.Label == l)).ToList();
        var lines = new List<string> { $"{"Label",-10} {"Kodi",-16} {"Zyra",-5} Avokati primar", new string('-', 60) };
        lines.AddRange(rows.Select(r => $"{r.Label,-10} {r.Code,-16} {r.Office,-5} {r.Primary ?? "—"}"));
        lines.Add(string.Empty);
        lines.Add($"{result.Cases.Count} çështje (nivele) u krijuan.");
        return string.Join(System.Environment.NewLine, lines);
    }
}
