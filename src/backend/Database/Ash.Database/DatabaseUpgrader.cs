using System.Reflection;
using DbUp;
using MySqlConnector;

namespace Ash.Database;

public sealed record DatabaseUpgradeResult(bool Successful, string? Error);

/// <summary>Applies the embedded DbUp scripts (Scripts, plus LocalScripts when local) to a MySQL server.</summary>
public static class DatabaseUpgrader
{
    public static DatabaseUpgradeResult Upgrade(string connectionString, bool local, bool quiet = false)
    {
        // Connect to the server without a default database; scripts use USE themselves.
        var builder = new MySqlConnectionStringBuilder(connectionString) { Database = string.Empty, AllowUserVariables = true };
        var serverConnectionString = builder.ConnectionString;

        try
        {
            // The journal table lives in ash_ops, so the schema must exist before DbUp starts.
            using var connection = new MySqlConnection(serverConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE DATABASE IF NOT EXISTS ash_ops CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci";
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            return new DatabaseUpgradeResult(false, $"Cannot prepare database server: {ex.Message}");
        }

        var assembly = typeof(DatabaseUpgrader).Assembly;

        var scriptSets = new List<(string Name, string Marker)> { ("Scripts", ".Scripts.") };
        if (local)
        {
            scriptSets.Add(("LocalScripts", ".LocalScripts."));
        }

        foreach (var (name, marker) in scriptSets)
        {
            if (!quiet)
            {
                Console.WriteLine($"=== {name} ===");
            }

            var result = Build(serverConnectionString, assembly, marker, quiet).PerformUpgrade();

            if (!result.Successful)
            {
                return new DatabaseUpgradeResult(false, $"Database upgrade FAILED in {name}: {result.Error?.Message}");
            }

            if (!quiet)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"{name}: upgrade succeeded. Scripts applied: {result.Scripts.Count()}");
                Console.ResetColor();
            }
        }

        return new DatabaseUpgradeResult(true, null);
    }

    private static DbUp.Engine.UpgradeEngine Build(string serverConnectionString, Assembly assembly, string marker, bool quiet)
    {
        var builder = DeployChanges.To
            .MySqlDatabase(serverConnectionString)
            .WithScriptsEmbeddedInAssembly(assembly, resource =>
                resource.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)
                && resource.Contains(marker, StringComparison.Ordinal))
            .JournalToMySqlTable("ash_ops", "schema_versions")
            .WithTransactionPerScript();

        return (quiet ? builder.LogToNowhere() : builder.LogToConsole()).Build();
    }
}
