using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Ash.Infrastructure.Storage;
using Ash.SharedKernel.Time;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Ash.Tests.Integration.Infrastructure;

/// <summary>
/// One MinIO container for the storage tests (task 032), with the buckets of docker-compose.yml: quarantine, documents
/// (object lock) and chat (versioning). Docker Hub deleted minio/minio; Chainguard's build has only the free
/// "latest" tag. The image runs as UID 65532, so the data directory is under /tmp.
/// </summary>
public sealed class MinioFixture : IAsyncLifetime
{
    private const string User = "ash_minio_test";
    private const string Password = "ash_minio_test_pw";

    private readonly IContainer _container = new ContainerBuilder("cgr.dev/chainguard/minio:latest")
        .WithCommand("server", "/tmp/data")
        .WithEnvironment("MINIO_ROOT_USER", User)
        .WithEnvironment("MINIO_ROOT_PASSWORD", Password)
        .WithPortBinding(9000, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9000).ForPath("/minio/health/ready")))
        .Build();

    public StorageOptions Options { get; private set; } = new();

    public S3FileStorage Storage { get; private set; } = null!;

    /// <summary>A plain SDK client for checks the abstraction does not offer (ETag of a multipart upload).</summary>
    public AmazonS3Client Admin { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Timing.MeasureAsync("minio start", async () =>
        {
            await _container.StartAsync();
            return 0;
        });

        Options = NewOptions();
        Storage = new S3FileStorage(Options, new SystemClock());
        Admin = new AmazonS3Client(new BasicAWSCredentials(User, Password), new AmazonS3Config
        {
            ServiceURL = Options.ServiceUrl,
            ForcePathStyle = true,
            AuthenticationRegion = Options.Region,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        });

        await Admin.PutBucketAsync(new PutBucketRequest { BucketName = Options.Buckets.Quarantine });
        await Admin.PutBucketAsync(new PutBucketRequest { BucketName = Options.Buckets.Documents, ObjectLockEnabledForBucket = true });
        await Admin.PutBucketAsync(new PutBucketRequest { BucketName = Options.Buckets.Chat });
        await Admin.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = Options.Buckets.Chat,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled },
        });
    }

    /// <summary>A fresh options object for this container (tests change copies of it, never the shared one).</summary>
    public StorageOptions NewOptions() => new()
    {
        ServiceUrl = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(9000)}",
        AccessKey = User,
        SecretKey = Password,
        Buckets = new StorageBuckets { Quarantine = "ash-uploads-quarantine", Documents = "ash-documents", Chat = "ash-chat-files" },
    };

    public async ValueTask DisposeAsync()
    {
        Storage?.Dispose();
        Admin?.Dispose();
        await _container.DisposeAsync();
    }
}

/// <summary>
/// The storage tests run alone: the memory measurement of the large upload is process-wide and must not see the
/// allocations of other tests running in parallel.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StorageCollection : ICollectionFixture<MinioFixture>
{
    public const string Name = "Storage";
}
