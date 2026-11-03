namespace Ash.Infrastructure.Storage;

/// <summary>
/// Object storage for documents and chat files (task 032): MinIO locally, AKSHI's S3-compatible storage in production.
/// Keys come from <see cref="StorageKeys.New"/>, never from user file names; buckets are never public. Downloads go
/// through the API by default (access check and audit at download time), see Ash.AspNetCore StoredFileResult.
/// TODO(akshi-q-storage): object lock / retention period, versioning and server-side encryption (SSE) are left to the
/// bucket configuration (defaults), to be confirmed with AKSHI. TODO(client-q9): the retention period of documents.
/// </summary>
public interface IFileStorage
{
    /// <summary>The configured bucket names.</summary>
    StorageBuckets Buckets { get; }

    /// <summary>
    /// Stores the stream (from its current position) without buffering the whole file: a single request for a seekable
    /// stream of at most 16 MB, otherwise a multipart upload in 8 MB parts (also for a stream of unknown length),
    /// aborted on failure. Returns the stored length and its SHA-256, computed while uploading.
    /// </summary>
    Task<StoredFileInfo> PutAsync(string bucket, string key, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>The object as a stream with its length and content type, or null when it does not exist.</summary>
    Task<StoredObject?> OpenReadAsync(string bucket, string key, CancellationToken ct = default);

    /// <summary>Server-side copy (e.g. quarantine → documents). Fails for an object above the 5 GB single-copy limit.</summary>
    Task CopyAsync(string sourceBucket, string sourceKey, string destinationBucket, string destinationKey, CancellationToken ct = default);

    Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default);

    /// <summary>
    /// Deletes an object of the QUARANTINE bucket only (a rejected or expired upload). Any other bucket throws: legal
    /// records are never deleted (rule 6).
    /// </summary>
    Task DeleteAsync(string bucket, string key, CancellationToken ct = default);

    /// <summary>
    /// A presigned GET URL valid for at most 2 minutes, downloading as <paramref name="fileName"/>. Implemented but NOT
    /// used by default (a link can be forwarded and is not audited at download time).
    /// </summary>
    Uri GetPresignedDownloadUrl(string bucket, string key, TimeSpan ttl, string fileName);

    /// <summary>Whether the bucket exists and is reachable with the configured credentials (health check).</summary>
    Task<bool> BucketExistsAsync(string bucket, CancellationToken ct = default);
}

/// <summary>What was stored: the length and the SHA-256 of the content (computed while uploading).</summary>
public sealed record StoredFileInfo(long Length, byte[] Sha256);

/// <summary>An object being read; dispose it to close the connection to the storage.</summary>
public sealed class StoredObject(Stream content, long length, string contentType, IDisposable? owner = null) : IAsyncDisposable, IDisposable
{
    public Stream Content { get; } = content;

    public long Length { get; } = length;

    public string ContentType { get; } = contentType;

    public void Dispose()
    {
        Content.Dispose();
        owner?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
