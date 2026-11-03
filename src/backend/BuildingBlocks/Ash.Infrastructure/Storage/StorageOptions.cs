namespace Ash.Infrastructure.Storage;

/// <summary>
/// Configuration section "Storage". AccessKey and SecretKey come from user-secrets locally (environment variables or
/// the vault in production) — never from appsettings, and never written to logs (see <see cref="ToString"/>).
/// </summary>
public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>The S3 endpoint the API talks to, e.g. http://localhost:9000 (MinIO) or AKSHI's endpoint.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// Optional: the endpoint presigned URLs are signed against when it differs from <see cref="ServiceUrl"/> (the
    /// browser cannot reach an internal host name). Only for presigned URLs, which are not used by default.
    /// </summary>
    public string? PublicServiceUrl { get; set; }

    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    public string Region { get; set; } = "us-east-1";

    /// <summary>http://host/bucket/key instead of http://bucket.host/key: what MinIO and most S3-compatible stores need.</summary>
    public bool ForcePathStyle { get; set; } = true;

    public StorageBuckets Buckets { get; set; } = new();

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServiceUrl) && !string.IsNullOrWhiteSpace(AccessKey) && !string.IsNullOrWhiteSpace(SecretKey);

    /// <summary>Safe to log: the keys are never shown.</summary>
    public override string ToString() =>
        $"ServiceUrl={ServiceUrl}, PublicServiceUrl={PublicServiceUrl}, Region={Region}, ForcePathStyle={ForcePathStyle}, " +
        $"AccessKey={(string.IsNullOrEmpty(AccessKey) ? "(not set)" : "***")}, SecretKey={(string.IsNullOrEmpty(SecretKey) ? "(not set)" : "***")}";
}

/// <summary>"Storage:Buckets" — the bucket names come from configuration, never from strings in the modules.</summary>
public sealed class StorageBuckets
{
    /// <summary>Uploads land here first (1-day expiry); the only bucket whose objects may be deleted.</summary>
    public string Quarantine { get; set; } = string.Empty;

    /// <summary>Case documents (object lock): legal records, never deleted.</summary>
    public string Documents { get; set; } = string.Empty;

    /// <summary>Chat attachments.</summary>
    public string Chat { get; set; } = string.Empty;

    public IReadOnlyList<string> All => [Quarantine, Documents, Chat];
}
