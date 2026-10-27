using System.Reflection;
using DbUp;
using DbUp.Engine;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

var local = args.Contains("--local");

var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
    .Build();

var connectionString = configuration["ConnectionStrings:Migrator"]
    ?? configuration["ConnectionStrings__Migrator"]
    ?? (local ? "Server=localhost;Port=3306;User=root;Password=ash_local_root;" : null);

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("ConnectionStrings:Migrator is not set (env var ConnectionStrings__Migrator or user-secrets).");
    return 1;
}

// Connect to the server without a default database; scripts use USE themselves.
var builder = new MySqlConnectionStringBuilder(connectionString) { Database = string.Empty, AllowUserVariables = true };
var serverConnectionString = builder.ConnectionString;

try
{
    // The journal table lives in ash_ops, so the schema must exist before DbUp starts.
    using (var connection = new MySqlConnection(serverConnectionString))
    {
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE DATABASE IF NOT EXISTS ash_ops CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci";
        command.ExecuteNonQuery();
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Cannot prepare database server: {ex.Message}");
    return 1;
}

var assembly = Assembly.GetExecutingAssembly();

var scriptSets = new List<(string Name, string Marker)> { ("Scripts", ".Scripts.") };
if (local)
{
    scriptSets.Add(("LocalScripts", ".LocalScripts."));
}

foreach (var (name, marker) in scriptSets)
{
    Console.WriteLine($"=== {name} ===");

    var upgrader = DeployChanges.To
        .MySqlDatabase(serverConnectionString)
        .WithScriptsEmbeddedInAssembly(assembly, resource =>
            resource.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)
            && resource.Contains(marker, StringComparison.Ordinal))
        .JournalToMySqlTable("ash_ops", "schema_versions")
        .WithTransactionPerScript()
        .LogToConsole()
        .Build();

    var result = upgrader.PerformUpgrade();

    if (!result.Successful)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"Database upgrade FAILED in {name}: {result.Error?.Message}");
        Console.ResetColor();
        return 1;
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"{name}: upgrade succeeded. Scripts applied: {result.Scripts.Count()}");
    Console.ResetColor();
}

return 0;
