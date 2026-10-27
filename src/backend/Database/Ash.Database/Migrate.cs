using System.Reflection;
using Ash.Database;
using Microsoft.Extensions.Configuration;

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

var result = DatabaseUpgrader.Upgrade(connectionString, local);
if (!result.Successful)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine(result.Error);
    Console.ResetColor();
    return 1;
}

return 0;
