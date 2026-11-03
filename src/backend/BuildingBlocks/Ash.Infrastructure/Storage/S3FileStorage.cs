using System.Net;
using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Ash.SharedKernel.Time;

namespace Ash.Infrastructure.Storage;

/// <summary>
/// <see cref="IFileStorage"/> over the AWS SDK (any S3-compatible store). One client for the API's own calls and one
/// for presigned URLs (signed against PublicServiceUrl when set). Created on first use: without configuration the API
/// still starts, the "storage" health check reports it, and every call fails with a clear message.
/// </summary>
public sealed class S3FileStorage : IFileStorage, IDisposable
{
    /// <summary>Above this size (or when the length is unknown) the upload is multipart.</summary>
    public const long SinglePutLimit = 16L * 1024 * 1024;

    /// <summary>The part size of a multipart upload (S3 minimum: 5 MB, except the last part).</summary>
    public const int PartSize = 8 * 1024 * 1024;

    /// <summary>The single CopyObject limit of S3; documents are far below it, so multipart copy is not built.</summary>
    public const long SingleCopyLimit = 5L * 1024 * 1024 * 1024;

    /// <summary>The longest a presigned download link may live.</summary>
    public static readonly TimeSpan PresignedTtlLimit = TimeSpan.FromMinutes(2);

    private readonly StorageOptions _options;
    private readonly IClock _clock;
    private readonly Lazy<AmazonS3Client> _client;
    private readonly Lazy<AmazonS3Client> _presignClient;

    public S3FileStorage(StorageOptions options, IClock clock)
    {
        _options = options;
        _clock = clock;
        _client = new Lazy<AmazonS3Client>(() => CreateClient(options.ServiceUrl));
        _presignClient = new Lazy<AmazonS3Client>(() =>
            string.IsNullOrWhiteSpace(options.PublicServiceUrl) ? _client.Value : CreateClient(options.PublicServiceUrl));
    }

    public StorageBuckets Buckets => _options.Buckets;

    private AmazonS3Client Client => _client.Value;

    public async Task<StoredFileInfo> PutAsync(string bucket, string key, Stream content, string contentType, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var remaining = content.CanSeek ? content.Length - content.Position : -1;
        return remaining is >= 0 and <= SinglePutLimit
            ? await PutSingleAsync(bucket, key, content, remaining, contentType, ct)
            : await PutMultipartAsync(bucket, key, content, contentType, ct);
    }

    /// <summary>A seekable stream of at most 16 MB: hashed in one pass, rewound, sent in one request.</summary>
    private async Task<StoredFileInfo> PutSingleAsync(string bucket, string key, Stream content, long length, string contentType, CancellationToken ct)
    {
        var start = content.Position;
        var sha256 = await SHA256.HashDataAsync(content, ct);
        content.Position = start;
        await Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        }, ct);
        return new StoredFileInfo(length, sha256);
    }

    /// <summary>
    /// Multipart in 8 MB parts: one part buffer at a time, never the whole file; the SHA-256 is computed part by part.
    /// Any failure (also cancellation) aborts the upload, so no orphan parts stay in the bucket.
    /// </summary>
    private async Task<StoredFileInfo> PutMultipartAsync(string bucket, string key, Stream content, string contentType, CancellationToken ct)
    {
        var upload = await Client.InitiateMultipartUploadAsync(
            new InitiateMultipartUploadRequest { BucketName = bucket, Key = key, ContentType = contentType }, ct);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[PartSize];
            var parts = new List<PartETag>();
            long total = 0;
            while (true)
            {
                var filled = await FillAsync(content, buffer, ct);
                if (filled == 0 && parts.Count > 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, filled);
                total += filled;
                using var part = new MemoryStream(buffer, 0, filled, writable: false);
                var response = await Client.UploadPartAsync(new UploadPartRequest
                {
                    BucketName = bucket,
                    Key = key,
                    UploadId = upload.UploadId,
                    PartNumber = parts.Count + 1,
                    InputStream = part,
                    PartSize = filled,
                }, ct);
                parts.Add(new PartETag(parts.Count + 1, response.ETag));
                if (filled < buffer.Length)
                {
                    break; // the stream ended inside this part: it was the last one (an empty stream gives one empty part)
                }
            }

            await Client.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
            {
                BucketName = bucket,
                Key = key,
                UploadId = upload.UploadId,
                PartETags = parts,
            }, ct);
            return new StoredFileInfo(total, hash.GetHashAndReset());
        }
        catch
        {
            // not with the caller's token: a cancelled upload must still be aborted
            await Client.AbortMultipartUploadAsync(
                new AbortMultipartUploadRequest { BucketName = bucket, Key = key, UploadId = upload.UploadId }, CancellationToken.None);
            throw;
        }
    }

    /// <summary>Reads until the buffer is full or the stream ends (every part but the last must be full size).</summary>
    private static async Task<int> FillAsync(Stream content, byte[] buffer, CancellationToken ct)
    {
        var filled = 0;
        while (filled < buffer.Length)
        {
            var read = await content.ReadAsync(buffer.AsMemory(filled), ct);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        return filled;
    }

    public Task<StoredObject?> OpenReadAsync(string bucket, string key, CancellationToken ct = default) =>
        GetAsync(new GetObjectRequest { BucketName = bucket, Key = key }, ct);

    public Task<StoredObject?> OpenReadAsync(string bucket, string key, long from, long to, CancellationToken ct = default)
    {
        if (from < 0 || to < from)
        {
            throw new ArgumentOutOfRangeException(nameof(from), $"Invalid byte range {from}-{to}.");
        }

        return GetAsync(new GetObjectRequest { BucketName = bucket, Key = key, ByteRange = new ByteRange(from, to) }, ct);
    }

    private async Task<StoredObject?> GetAsync(GetObjectRequest request, CancellationToken ct)
    {
        try
        {
            var response = await Client.GetObjectAsync(request, ct);
            var contentType = string.IsNullOrEmpty(response.Headers.ContentType) ? "application/octet-stream" : response.Headers.ContentType;
            return new StoredObject(response.ResponseStream, response.ContentLength, contentType, response);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task CopyAsync(string sourceBucket, string sourceKey, string destinationBucket, string destinationKey, CancellationToken ct = default)
    {
        var source = await Client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = sourceBucket, Key = sourceKey }, ct);
        if (source.ContentLength > SingleCopyLimit)
        {
            throw new InvalidOperationException(
                $"{sourceBucket}/{sourceKey} has {source.ContentLength} bytes; a server-side copy is limited to 5 GB (multipart copy is not implemented).");
        }

        await Client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = sourceBucket,
            SourceKey = sourceKey,
            DestinationBucket = destinationBucket,
            DestinationKey = destinationKey,
        }, ct);
    }

    public async Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default)
    {
        try
        {
            await Client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = bucket, Key = key }, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task DeleteAsync(string bucket, string key, CancellationToken ct = default)
    {
        // rule 6: legal records are never deleted — only a quarantined upload may go
        if (string.IsNullOrEmpty(Buckets.Quarantine) || !string.Equals(bucket, Buckets.Quarantine, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Deleting from bucket '{bucket}' is not allowed: only objects of the quarantine bucket may be deleted.");
        }

        await Client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucket, Key = key }, ct);
    }

    /// <remarks>TODO(akshi-q-storage): confirm that AKSHI's storage supports presigned URLs and which public endpoint the browser reaches.</remarks>
    public Uri GetPresignedDownloadUrl(string bucket, string key, TimeSpan ttl, string fileName)
    {
        if (ttl <= TimeSpan.Zero || ttl > PresignedTtlLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "A download link may be valid for at most 2 minutes.");
        }

        var endpoint = new Uri(string.IsNullOrWhiteSpace(_options.PublicServiceUrl) ? _options.ServiceUrl! : _options.PublicServiceUrl);
        var url = _presignClient.Value.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = _clock.UtcNow.Add(ttl),
            Protocol = endpoint.Scheme == Uri.UriSchemeHttp ? Protocol.HTTP : Protocol.HTTPS,
            ResponseHeaderOverrides = new ResponseHeaderOverrides { ContentDisposition = ContentDisposition.Attachment(fileName) },
        });
        return new Uri(url);
    }

    public async Task<bool> BucketExistsAsync(string bucket, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bucket))
        {
            return false;
        }

        try
        {
            await Client.HeadBucketAsync(new HeadBucketRequest { BucketName = bucket }, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private AmazonS3Client CreateClient(string? serviceUrl)
    {
        if (!_options.IsConfigured || string.IsNullOrWhiteSpace(serviceUrl))
        {
            throw new InvalidOperationException(
                "Storage is not configured: set Storage:ServiceUrl in appsettings and Storage:AccessKey / Storage:SecretKey " +
                "with dotnet user-secrets (environment variables in production).");
        }

        var config = new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = _options.ForcePathStyle,
            AuthenticationRegion = _options.Region,
            // The SDK (since 3.7.400 / 4.x) adds CRC checksums to every request and validates them on every response by
            // default. Several S3-compatible stores (older MinIO, Ceph RGW and others) reject or do not return them, so
            // checksums are only sent and checked where the S3 API requires them.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        return new AmazonS3Client(new BasicAWSCredentials(_options.AccessKey, _options.SecretKey), config);
    }

    public void Dispose()
    {
        if (_presignClient.IsValueCreated && !ReferenceEquals(_presignClient.Value, _client.Value))
        {
            _presignClient.Value.Dispose();
        }

        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }
}
