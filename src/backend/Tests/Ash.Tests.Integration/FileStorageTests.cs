using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Ash.Api.Health;
using Ash.AspNetCore.Http;
using Ash.Infrastructure.Storage;
using Ash.SharedKernel.Time;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>Task 032 — IFileStorage against MinIO (Testcontainers, the buckets of docker-compose.yml).</summary>
[Collection(StorageCollection.Name)]
public class FileStorageTests(MinioFixture minio)
{
    private const string FileName = "Vendim_gjykatë_çështja.pdf";

    /// <summary>filename*: the UTF-8 bytes of ë (C3 AB) and ç (C3 A7) percent-encoded; filename: the ASCII fallback.</summary>
    private const string ExpectedDisposition =
        "attachment; filename=\"Vendim_gjykate_ceshtja.pdf\"; filename*=UTF-8''Vendim_gjykat%C3%AB_%C3%A7%C3%ABshtja.pdf";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IFileStorage Storage => minio.Storage;

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        new Random(length).NextBytes(bytes);
        return bytes;
    }

    private string NewKey() => StorageKeys.New(new SystemClock());

    private async Task<string> PutAsync(string bucket, byte[] content, string contentType = "application/pdf")
    {
        var key = NewKey();
        await Storage.PutAsync(bucket, key, new MemoryStream(content), contentType, Ct);
        return key;
    }

    private async Task<byte[]> ReadAllAsync(string bucket, string key)
    {
        await using var file = await Storage.OpenReadAsync(bucket, key, Ct);
        file.ShouldNotBeNull();
        using var copy = new MemoryStream();
        await file.Content.CopyToAsync(copy, Ct);
        return copy.ToArray();
    }

    // ---------------------------------------------------------------- upload and read

    [Fact]
    public async Task Small_file_reads_back_identical()
    {
        var content = RandomBytes(300 * 1024);
        var key = NewKey();

        var stored = await Storage.PutAsync(minio.Options.Buckets.Quarantine, key, new MemoryStream(content), "application/pdf", Ct);

        stored.Length.ShouldBe(content.Length);
        stored.Sha256.ShouldBe(SHA256.HashData(content));
        await using var file = await Storage.OpenReadAsync(minio.Options.Buckets.Quarantine, key, Ct);
        file.ShouldNotBeNull();
        file.Length.ShouldBe(content.Length);
        file.ContentType.ShouldBe("application/pdf");
        (await SHA256.HashDataAsync(file.Content, Ct)).ShouldBe(SHA256.HashData(content));
    }

    [Fact]
    public async Task Upload_directly_into_the_object_locked_documents_bucket_works_single_and_multipart()
    {
        // some stores demand Content-MD5 on object-locked buckets; checksums are only sent "when required"
        var small = RandomBytes(4096);
        var smallKey = await PutAsync(minio.Options.Buckets.Documents, small);
        (await ReadAllAsync(minio.Options.Buckets.Documents, smallKey)).ShouldBe(small);

        var large = new GeneratedStream(17L * 1024 * 1024);
        var largeKey = NewKey();
        var stored = await Storage.PutAsync(minio.Options.Buckets.Documents, largeKey, large, "application/pdf", Ct);
        stored.Sha256.ShouldBe(large.Sha256);
        (await Storage.ExistsAsync(minio.Options.Buckets.Documents, largeKey, Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task Missing_object_reads_as_null_and_does_not_exist()
    {
        (await Storage.OpenReadAsync(minio.Options.Buckets.Documents, NewKey(), Ct)).ShouldBeNull();
        (await Storage.ExistsAsync(minio.Options.Buckets.Documents, NewKey(), Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_40_MB_stream_of_unknown_length_is_uploaded_in_parts_without_buffering_the_file()
    {
        const long size = 40L * 1024 * 1024;
        var key = NewKey();
        var source = new GeneratedStream(size);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var managedBefore = GC.GetTotalMemory(forceFullCollection: true);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var workingSetBefore = process.WorkingSet64;

        var stored = await Storage.PutAsync(minio.Options.Buckets.Quarantine, key, source, "application/octet-stream", Ct);

        process.Refresh();
        var managedPeakGrowth = source.PeakManagedMemory - managedBefore;
        var workingSetGrowth = process.WorkingSet64 - workingSetBefore;
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"managed peak growth {managedPeakGrowth / 1024 / 1024} MB, working set growth {workingSetGrowth / 1024 / 1024} MB");

        stored.Length.ShouldBe(size);
        stored.Sha256.ShouldBe(source.Sha256);
        // measured while the stream was being read: one 8 MB part buffer plus the SDK's own, never the 40 MB file
        managedPeakGrowth.ShouldBeLessThan(50L * 1024 * 1024);
        workingSetGrowth.ShouldBeLessThan(100L * 1024 * 1024); // generous: JIT, HTTP and SDK buffers on first use
        // a multipart object's ETag is "<md5 of the part md5s>-<number of parts>": 40 MB / 8 MB = 5 parts
        var metadata = await minio.Admin.GetObjectMetadataAsync(minio.Options.Buckets.Quarantine, key, Ct);
        metadata.ETag.Trim('"').ShouldEndWith("-5");

        await using var file = await Storage.OpenReadAsync(minio.Options.Buckets.Quarantine, key, Ct);
        file.ShouldNotBeNull();
        file.Length.ShouldBe(size);
        (await SHA256.HashDataAsync(file.Content, Ct)).ShouldBe(source.Sha256);
    }

    [Fact]
    public async Task A_failed_multipart_upload_is_aborted()
    {
        var key = NewKey();

        await Should.ThrowAsync<IOException>(() =>
            Storage.PutAsync(minio.Options.Buckets.Quarantine, key, new GeneratedStream(20L * 1024 * 1024, failAfter: 12L * 1024 * 1024),
                "application/octet-stream", Ct));

        (await Storage.ExistsAsync(minio.Options.Buckets.Quarantine, key, Ct)).ShouldBeFalse();
        var open = await minio.Admin.ListMultipartUploadsAsync(minio.Options.Buckets.Quarantine, Ct);
        (open.MultipartUploads ?? []).Where(u => u.Key == key).ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- copy and delete

    [Fact]
    public async Task Copy_from_quarantine_to_documents_keeps_the_content()
    {
        var content = RandomBytes(64 * 1024);
        var key = await PutAsync(minio.Options.Buckets.Quarantine, content);
        var target = NewKey();

        await Storage.CopyAsync(minio.Options.Buckets.Quarantine, key, minio.Options.Buckets.Documents, target, Ct);

        (await Storage.ExistsAsync(minio.Options.Buckets.Documents, target, Ct)).ShouldBeTrue();
        (await ReadAllAsync(minio.Options.Buckets.Documents, target)).ShouldBe(content);
    }

    [Fact]
    public async Task Delete_works_in_quarantine_and_throws_for_every_other_bucket()
    {
        var quarantined = await PutAsync(minio.Options.Buckets.Quarantine, RandomBytes(1024));
        await Storage.DeleteAsync(minio.Options.Buckets.Quarantine, quarantined, Ct);
        (await Storage.ExistsAsync(minio.Options.Buckets.Quarantine, quarantined, Ct)).ShouldBeFalse();

        foreach (var bucket in new[] { minio.Options.Buckets.Documents, minio.Options.Buckets.Chat })
        {
            var key = NewKey();
            await Storage.CopyAsync(minio.Options.Buckets.Quarantine, await PutAsync(minio.Options.Buckets.Quarantine, RandomBytes(1024)), bucket, key, Ct);

            var error = await Should.ThrowAsync<InvalidOperationException>(() => Storage.DeleteAsync(bucket, key, Ct));

            error.Message.ShouldContain("only objects of the quarantine bucket may be deleted");
            (await Storage.ExistsAsync(bucket, key, Ct)).ShouldBeTrue(); // nothing was sent to the storage
        }
    }

    // ---------------------------------------------------------------- presigned URL

    [Fact]
    public async Task Presigned_url_downloads_with_the_file_name_and_lives_at_most_two_minutes()
    {
        var content = RandomBytes(10 * 1024);
        var key = await PutAsync(minio.Options.Buckets.Quarantine, content);
        var url = Storage.GetPresignedDownloadUrl(minio.Options.Buckets.Quarantine, key, TimeSpan.FromMinutes(1), FileName);

        using var http = new HttpClient();
        using var response = await http.GetAsync(url, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(content);
        response.Content.Headers.ContentDisposition?.FileNameStar.ShouldBe(FileName);
        Should.Throw<ArgumentOutOfRangeException>(() =>
            Storage.GetPresignedDownloadUrl(minio.Options.Buckets.Quarantine, key, TimeSpan.FromMinutes(3), FileName));
    }

    // ---------------------------------------------------------------- health

    [Fact]
    public async Task Health_check_is_healthy_with_the_three_buckets_and_unhealthy_when_one_is_missing()
    {
        var healthy = await new StorageHealthCheck(Storage, minio.Options).CheckHealthAsync(new HealthCheckContext(), Ct);
        healthy.Status.ShouldBe(HealthStatus.Healthy, healthy.Description);

        var options = minio.NewOptions();
        options.Buckets.Chat = "ash-bucket-that-does-not-exist";
        using var storage = new S3FileStorage(options, new SystemClock());
        var missing = await new StorageHealthCheck(storage, options).CheckHealthAsync(new HealthCheckContext(), Ct);
        missing.Status.ShouldBe(HealthStatus.Unhealthy);
        missing.Description.ShouldBe("Missing bucket(s): ash-bucket-that-does-not-exist.");

        var unconfigured = new StorageOptions();
        using var none = new S3FileStorage(unconfigured, new SystemClock());
        (await new StorageHealthCheck(none, unconfigured).CheckHealthAsync(new HealthCheckContext(), Ct)).Status.ShouldBe(HealthStatus.Unhealthy);
        await Should.ThrowAsync<InvalidOperationException>(() => none.ExistsAsync("any", "key", Ct));
    }

    // ---------------------------------------------------------------- download through the API

    [Fact]
    public async Task Download_helper_streams_the_object_with_the_safe_headers_and_an_encoded_file_name()
    {
        var content = RandomBytes(200 * 1024);
        var key = await PutAsync(minio.Options.Buckets.Quarantine, content);
        var stored = await Storage.OpenReadAsync(minio.Options.Buckets.Quarantine, key, Ct);
        stored.ShouldNotBeNull();
        var closed = new DisposeTracker(stored);
        var file = new StoredObject(stored.Content, stored.Length, stored.ContentType, closed);
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;

        await new StoredFileResult(file, FileName).ExecuteAsync(context);

        var headers = context.Response.Headers;
        context.Response.ContentType.ShouldBe("application/pdf");
        context.Response.ContentLength.ShouldBe(content.Length);
        headers.ContentDisposition.ToString().ShouldBe(ExpectedDisposition);
        System.Net.Http.Headers.ContentDispositionHeaderValue.Parse(headers.ContentDisposition!).FileNameStar.ShouldBe(FileName);
        headers.XContentTypeOptions.ToString().ShouldBe("nosniff");
        headers.CacheControl.ToString().ShouldBe("no-store");
        body.ToArray().ShouldBe(content);
        closed.Disposed.ShouldBeTrue(); // the connection to the storage was released after writing
    }

    private sealed class DisposeTracker(IDisposable inner) : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            inner.Dispose();
            Disposed = true;
        }
    }

    [Theory]
    [InlineData("..\\..\\etc/vendim\".pdf", "attachment; filename=\"vendim_.pdf\"; filename*=UTF-8''vendim%22.pdf")]
    [InlineData("a\r\nSet-Cookie: x.pdf", "attachment; filename=\"aSet-Cookie: x.pdf\"; filename*=UTF-8''aSet-Cookie%3A%20x.pdf")]
    [InlineData("   ", "attachment; filename=\"file\"; filename*=UTF-8''file")]
    public void Content_disposition_drops_paths_quotes_and_control_characters(string fileName, string expected) =>
        ContentDisposition.Attachment(fileName).ShouldBe(expected);

    [Fact]
    public void Keys_are_year_month_uuid_in_utc_never_the_file_name()
    {
        var key = StorageKeys.New(new FixedClock(new DateTime(2026, 12, 31, 23, 30, 0, DateTimeKind.Utc)));
        Regex.IsMatch(key, "^2026/12/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$").ShouldBeTrue(key);
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }
}
