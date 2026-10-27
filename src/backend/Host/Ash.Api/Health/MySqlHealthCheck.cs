using Ash.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MySql.Data.MySqlClient;

namespace Ash.Api.Health;

public sealed class MySqlHealthCheck(PersistenceOptions options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var builder = new MySqlConnectionStringBuilder(options.ConnectionString) { ConnectionTimeout = 3 };
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            var ok = await connection.PingAsync();
            return ok ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("MySQL ping failed.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MySQL is not reachable.", ex);
        }
    }
}
