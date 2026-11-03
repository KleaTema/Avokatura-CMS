using Ash.Infrastructure.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ash.Api.Health;

/// <summary>"storage": HeadBucket on the quarantine, documents and chat buckets; unhealthy if any is missing or unreachable.</summary>
public sealed class StorageHealthCheck(IFileStorage storage, StorageOptions options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!options.IsConfigured)
        {
            return HealthCheckResult.Unhealthy("Storage is not configured (Storage:ServiceUrl, Storage:AccessKey, Storage:SecretKey).");
        }

        try
        {
            var missing = new List<string>();
            foreach (var bucket in storage.Buckets.All)
            {
                if (!await storage.BucketExistsAsync(bucket, cancellationToken))
                {
                    missing.Add(string.IsNullOrWhiteSpace(bucket) ? "(not configured)" : bucket);
                }
            }

            return missing.Count == 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Missing bucket(s): {string.Join(", ", missing)}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Storage is not reachable.", ex);
        }
    }
}
