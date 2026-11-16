using System.Net.Sockets;
using Ash.Infrastructure.Scanning;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MySql.Data.MySqlClient;

namespace Ash.Api.Health;

/// <summary>Task 052: a database the API needs besides the core one (audit, audit digest, chat, reporting) answers.</summary>
public sealed class ConnectionHealthCheck(IConfiguration configuration, string name) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var value = configuration.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            // Reporting falls back to the App connection (locally); the others are required outside Development
            return name == "Reporting" ? HealthCheckResult.Healthy("uses the App connection") : HealthCheckResult.Unhealthy($"{name}: not configured");
        }

        try
        {
            await using var connection = new MySqlConnection(new MySqlConnectionStringBuilder(value) { ConnectionTimeout = 3 }.ConnectionString);
            await connection.OpenAsync(ct);
            return await connection.PingAsync() ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy($"{name}: ping failed");
        }
        catch (Exception ex) when (ex is MySqlException or InvalidOperationException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy($"{name}: not reachable");
        }
    }
}

/// <summary>Task 052: the virus scanner (clamd) answers PING with PONG; NoOp (Development) is always healthy.</summary>
public sealed class ScannerHealthCheck(ScannerOptions options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (options.Mode == ScannerMode.NoOp)
        {
            return HealthCheckResult.Healthy("scanner mode NoOp (Development)");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var client = new TcpClient();
            await client.ConnectAsync(options.ClamAv.Host, options.ClamAv.Port, timeout.Token);
            await using var stream = client.GetStream();
            await stream.WriteAsync("zPING\0"u8.ToArray(), timeout.Token);
            var buffer = new byte[16];
            var read = await stream.ReadAsync(buffer, timeout.Token);
            return System.Text.Encoding.ASCII.GetString(buffer, 0, read).StartsWith("PONG", StringComparison.Ordinal)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("clamd did not answer PONG");
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException)
        {
            return HealthCheckResult.Unhealthy("clamd is not reachable");
        }
    }
}

/// <summary>
/// Task 052, item 6 — the MySQL server settings the system relies on (deploy/mysql/mysql-production.cnf):
/// <list type="bullet">
/// <item>innodb_ft_enable_stopword = OFF, ngram_token_size = 2: the case search (task 047) finds short words;</item>
/// <item>event_scheduler = ON: the partition rotation events;</item>
/// <item>time_zone = '+00:00': every DATETIME is UTC;</item>
/// <item>character_set_server = utf8mb4: Albanian text;</item>
/// <item>require_secure_transport = ON (Production): no unencrypted connection at all.</item>
/// </list>
/// Wrong settings: a readiness failure and a startup warning (MySqlSettingsStartupCheck) — never a crash.
/// </summary>
public sealed class MySqlSettingsHealthCheck(Ash.Infrastructure.Persistence.PersistenceOptions persistence, IHostEnvironment environment) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var problems = await ProblemsAsync(persistence.ConnectionString, environment.IsProduction(), ct);
            return problems.Count == 0 ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy(string.Join("; ", problems));
        }
        catch (Exception ex) when (ex is MySqlException or InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy("MySQL settings could not be read");
        }
    }

    public static async Task<List<string>> ProblemsAsync(string connectionString, bool production, CancellationToken ct)
    {
        await using var connection = new MySqlConnection(new MySqlConnectionStringBuilder(connectionString) { ConnectionTimeout = 3 }.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand("""
            SELECT @@GLOBAL.innodb_ft_enable_stopword, @@GLOBAL.ngram_token_size, @@GLOBAL.event_scheduler,
                   @@GLOBAL.time_zone, @@GLOBAL.character_set_server, @@GLOBAL.require_secure_transport
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var problems = new List<string>();
        if (Convert.ToInt32(reader.GetValue(0)) != 0) problems.Add("innodb_ft_enable_stopword must be OFF");
        if (Convert.ToInt32(reader.GetValue(1)) != 2) problems.Add("ngram_token_size must be 2");
        if (!string.Equals(reader.GetString(2), "ON", StringComparison.OrdinalIgnoreCase)) problems.Add("event_scheduler must be ON");
        if (reader.GetString(3) is not ("+00:00" or "UTC")) problems.Add("time_zone must be '+00:00'");
        if (!reader.GetString(4).StartsWith("utf8mb4", StringComparison.OrdinalIgnoreCase)) problems.Add("character_set_server must be utf8mb4");
        if (production && Convert.ToInt32(reader.GetValue(5)) != 1) problems.Add("require_secure_transport must be ON in Production");
        return problems;
    }
}

/// <summary>Task 052: at start, a wrong MySQL setting is a WARNING in the log (and a readiness failure), never a crash.</summary>
public sealed class MySqlSettingsStartupCheck(
    Ash.Infrastructure.Persistence.PersistenceOptions persistence, IHostEnvironment environment, ILogger<MySqlSettingsStartupCheck> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            foreach (var problem in await MySqlSettingsHealthCheck.ProblemsAsync(persistence.ConnectionString, environment.IsProduction(), ct))
            {
                logger.LogWarning("MySQL server setting: {Problem} (deploy/mysql/mysql-production.cnf)", problem);
            }
        }
        catch (Exception ex) when (ex is MySqlException or InvalidOperationException)
        {
            logger.LogWarning("MySQL server settings could not be read at start: {Reason}", ex.Message);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
