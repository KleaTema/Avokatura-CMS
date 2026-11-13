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
                PrintReports(serverConnectionString, result.Scripts.Select(s => s.Name));
            }
        }

        return new DatabaseUpgradeResult(true, null);
    }

    /// <summary>
    /// What the data migrations applied in this run did (ash_ops.migration_reports, script 023): a count per kind, then
    /// each line. Scripts that write no report print nothing.
    /// </summary>
    private static void PrintReports(string serverConnectionString, IEnumerable<string> appliedResources)
    {
        // resource "Ash.Database.Scripts.024_assistant_links_migration.sql" → file "024_assistant_links_migration.sql"
        var files = appliedResources.Select(r => r.Split('.')[^2] + ".sql").ToList();
        if (files.Count == 0)
        {
            return;
        }

        using var connection = new MySqlConnection(serverConnectionString);
        connection.Open();
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'ash_ops' AND table_name = 'migration_reports'";
            if (Convert.ToInt64(exists.ExecuteScalar()) == 0)
            {
                return;
            }
        }

        foreach (var file in files)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT kind, detail FROM ash_ops.migration_reports WHERE script = @script ORDER BY kind, id";
            command.Parameters.AddWithValue("@script", file);
            var lines = new List<(string Kind, string Detail)>();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    lines.Add((reader.GetString(0), reader.GetString(1)));
                }
            }

            if (lines.Count == 0)
            {
                continue;
            }

            Console.WriteLine($"--- {file}: " + string.Join(", ", lines.GroupBy(l => l.Kind).Select(g => $"{g.Key} {g.Count()}")));
            foreach (var (kind, detail) in lines)
            {
                Console.WriteLine($"    {kind,-14} {detail}");
            }
        }
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
