using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.S3.Model;
using Ash.Infrastructure.Scanning;
using Ash.Modules.Documents.Contracts.Events;
using Ash.Modules.Documents.Infrastructure.Jobs;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Ids;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 033 — POST /api/cases/{id}/documents and the scan job, against MySQL and MinIO. Uses <see cref="CaseSeed"/>'s
/// ACTIVE case: avokat1 primary, the ministry MONITORING (institucion), DR's lawyer without access.
/// </summary>
public class DocumentUploadTests(MySqlFixture db, MinioFixture minio) : IntegrationTest(db), IClassFixture<MinioFixture>
{
    private const string Primary = "avokat1@ash.local";
    private static readonly byte[] PdfMagic = "%PDF-1.7\n"u8.ToArray();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Ctx(CaseSeed Seed, Guid Case, AshApiFactory Factory) : IAsyncDisposable
    {
        public HttpClient As(string email) => Factory.CreateClient().AsUser(email);

        public ScanQueueJob Job => Factory.Services.GetRequiredService<ScanQueueJob>();

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            await Seed.DisposeAsync();
        }
    }

    private async Task<Ctx> SetupAsync(Action<IServiceCollection>? services = null)
    {
        var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var factory = new AshApiFactory(Db, configureServices: services, storage: minio.Options);
        return new Ctx(seed, await PublicIdOf("cases", seed.ActiveCaseId), factory);
    }

    // ---------------------------------------------------------------- request helpers

    private static byte[] Pdf(int size, int seed = 1)
    {
        var bytes = new byte[size];
        new Random(seed).NextBytes(bytes);
        PdfMagic.CopyTo(bytes, 0);
        return bytes;
    }

    /// <summary>Fields first (as the API requires), then the file — or the file first with <paramref name="fileFirst"/>.</summary>
    private static MultipartFormDataContent Form(HttpContent file, string fileName, string type = "PADI_PROVA",
        string? title = null, string? other = null, bool fileFirst = false)
    {
        var form = new MultipartFormDataContent();
        if (fileFirst)
        {
            form.Add(file, "file", fileName);
        }

        form.Add(new StringContent(type), "documentTypeCode");
        if (other is not null)
        {
            form.Add(new StringContent(other), "otherTypeText");
        }

        if (title is not null)
        {
            form.Add(new StringContent(title), "title");
        }

        if (!fileFirst)
        {
            form.Add(file, "file", fileName);
        }

        return form;
    }

    private static Task<HttpResponseMessage> Upload(HttpClient client, Guid caseId, MultipartFormDataContent form) =>
        client.PostAsync($"/api/cases/{caseId}/documents", form, Ct);

    private static async Task<JsonElement> Created(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<string> Problem(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, text);
        return text;
    }

    // ---------------------------------------------------------------- database helpers

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(Ct);
        return value is DBNull ? null : value;
    }

    private async Task Execute(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private async Task<Guid> PublicIdOf(string table, long id) =>
        PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM {table} WHERE id = {id}"))!);

    private sealed record Doc(string Status, string Bucket, string Key, long Size, byte[] Sha256, string Title);

    private async Task<Doc> DocumentAsync(Guid publicId)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(
            "SELECT scan_status, storage_bucket, storage_key, size_bytes, sha256, title FROM documents WHERE public_id = @p", connection);
        command.Parameters.AddWithValue("@p", PublicId.ToBytes(publicId));
        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).ShouldBeTrue();
        return new Doc(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), (byte[])reader[4], reader.GetString(5));
    }

    private async Task<List<string>> QuarantineKeysAsync()
    {
        var response = await minio.Admin.ListObjectsV2Async(new ListObjectsV2Request { BucketName = minio.Options.Buckets.Quarantine }, Ct);
        return (response.S3Objects ?? []).Select(o => o.Key).ToList();
    }

    // ---------------------------------------------------------------- the happy path

    [Fact]
    public async Task Pdf_upload_is_pending_then_the_job_moves_it_clean_to_the_documents_bucket()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);
        var content = Pdf(300 * 1024);

        var body = await Created(await Upload(client, s.Case,
            Form(new ByteArrayContent(content), "Padia e paditësit.pdf", title: "Padia")));

        JsonAssert.NoNumericIds(body);
        body.GetProperty("scanStatus").GetString().ShouldBe("PENDING");
        body.GetProperty("warnings").GetArrayLength().ShouldBe(0);
        var id = body.GetProperty("documentPublicId").GetGuid();
        var pending = await DocumentAsync(id);
        pending.Status.ShouldBe("PENDING");
        pending.Bucket.ShouldBe(minio.Options.Buckets.Quarantine);
        pending.Size.ShouldBe(content.Length);
        pending.Sha256.ShouldBe(SHA256.HashData(content));
        pending.Title.ShouldBe("Padia");
        pending.Key.ShouldMatch(@"^\d{4}/\d{2}/[0-9a-f-]{36}$"); // generated, never the file name
        (await Scalar($"SELECT original_file_name FROM documents WHERE storage_key = '{pending.Key}'")).ShouldBe("Padia e paditësit.pdf");
        (await Scalar($"SELECT first_data_entry_at IS NOT NULL FROM cases WHERE id = {s.Seed.ActiveCaseId}")).ShouldBe(1L);

        (await s.Job.RunOnceAsync(Ct)).ShouldBe(1);

        var clean = await DocumentAsync(id);
        clean.Status.ShouldBe("CLEAN");
        clean.Bucket.ShouldBe(minio.Options.Buckets.Documents);
        clean.Key.ShouldBe(pending.Key);
        (await minio.Storage.ExistsAsync(minio.Options.Buckets.Documents, clean.Key, Ct)).ShouldBeTrue();
        (await minio.Storage.ExistsAsync(minio.Options.Buckets.Quarantine, clean.Key, Ct)).ShouldBeFalse();
        (await Scalar("SELECT scanned_at IS NOT NULL FROM documents")).ShouldBe(1L);
    }

    [Fact]
    public async Task Upload_writes_history_without_the_storage_key_and_an_audit_entry()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);

        var id = (await Created(await Upload(client, s.Case, Form(new ByteArrayContent(Pdf(2048)), "akt.pdf",
            type: "TJETER", other: "Shkresë e institucionit"))))
            .GetProperty("documentPublicId").GetGuid();

        var key = (await DocumentAsync(id)).Key;
        var history = (string)(await Scalar(
            $"SELECT CONCAT(summary, ' ', COALESCE(details, '')) FROM case_history_events WHERE event_type = 'DOCUMENT_UPLOADED' AND case_id = {s.Seed.ActiveCaseId}"))!;
        history.ShouldContain("Dokumenti u ngarkua: akt (Tjetër: Shkresë e institucionit");
        history.ShouldNotContain(key);
        history.ShouldNotContain(key.Split('/')[^1]);
        (await Scalar("SELECT other_type_text FROM documents")).ShouldBe("Shkresë e institucionit");
    }

    [Fact]
    public async Task The_same_file_again_in_the_dossier_gives_a_warning()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);
        var content = Pdf(4096, seed: 7);
        await Created(await Upload(client, s.Case, Form(new ByteArrayContent(content), "vendim.pdf", title: "Vendimi i parë")));

        var second = await Created(await Upload(client, s.Case, Form(new ByteArrayContent(content), "kopje.pdf")));

        var warning = second.GetProperty("warnings")[0];
        warning.GetProperty("code").GetString().ShouldBe("document.duplicate");
        warning.GetProperty("message").GetString()!.ShouldMatch(
            @"^Ky dokument është ngarkuar më parë më \d{2}\.\d{2}\.\d{4} si ""Vendimi i parë""\.$");
        (await Scalar("SELECT COUNT(*) FROM documents")).ShouldBe(2L); // a warning, not a refusal
    }

    // ---------------------------------------------------------------- refusals

    [Fact]
    public async Task A_renamed_exe_is_refused_by_its_first_bytes()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);
        var exe = new byte[4096];
        "MZ"u8.CopyTo(exe);

        var text = await Problem(await Upload(client, s.Case, Form(new ByteArrayContent(exe), "vendim.pdf")), HttpStatusCode.BadRequest);

        text.ShouldContain("Lloji i skedarit nuk lejohet");
        (await Scalar("SELECT COUNT(*) FROM documents")).ShouldBe(0L);
    }

    [Fact]
    public async Task An_extension_that_is_not_allowed_is_refused()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);

        var text = await Problem(await Upload(client, s.Case, Form(new ByteArrayContent(Pdf(100)), "skript.exe")), HttpStatusCode.BadRequest);

        text.ShouldContain("Lloji i skedarit nuk lejohet");
    }

    [Fact]
    public async Task The_file_before_the_fields_is_refused()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);

        var text = await Problem(await Upload(client, s.Case, Form(new ByteArrayContent(Pdf(100)), "a.pdf", fileFirst: true)),
            HttpStatusCode.BadRequest);

        text.ShouldContain("Fushat duhet të dërgohen para skedarit.");
    }

    [Fact]
    public async Task Fields_are_validated_before_the_file()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);
        var before = await QuarantineKeysAsync();

        var text = await Problem(await Upload(client, s.Case,
            Form(new ByteArrayContent(Pdf(100)), "a.pdf", type: "TJETER", title: new string('t', 256))), HttpStatusCode.BadRequest);

        var errors = JsonDocument.Parse(text).RootElement.GetProperty("errors");
        errors.TryGetProperty("otherTypeText", out _).ShouldBeTrue(text);
        errors.TryGetProperty("title", out _).ShouldBeTrue(text);
        (await QuarantineKeysAsync()).ShouldBe(before); // nothing was stored
    }

    [Fact]
    public async Task An_oversized_file_gets_413_and_leaves_nothing_in_quarantine()
    {
        await Execute("UPDATE allowed_file_types SET max_size_bytes = 1048576 WHERE extension = 'pdf'");
        try
        {
            await using var s = await SetupAsync();
            using var client = s.As(Primary);
            var before = await QuarantineKeysAsync();

            var text = await Problem(await Upload(client, s.Case,
                Form(new StreamContent(new GeneratedStream(3L * 1024 * 1024, prefix: PdfMagic)), "i-madh.pdf")), HttpStatusCode.RequestEntityTooLarge);

            text.ShouldContain("Skedari kalon madhësinë maksimale prej 1 MB.");
            (await QuarantineKeysAsync()).ShouldBe(before);
            var uploads = await minio.Admin.ListMultipartUploadsAsync(minio.Options.Buckets.Quarantine, Ct);
            (uploads.MultipartUploads ?? []).ShouldBeEmpty();
            (await Scalar("SELECT COUNT(*) FROM documents")).ShouldBe(0L);
        }
        finally
        {
            await Execute("UPDATE allowed_file_types SET max_size_bytes = 52428800 WHERE extension = 'pdf'");
        }
    }

    [Fact]
    public async Task Monitoring_institution_may_upload_and_a_user_without_access_gets_404()
    {
        await using var s = await SetupAsync();
        using (var institution = s.As("institucion@ash.local"))
        {
            await Created(await Upload(institution, s.Case, Form(new ByteArrayContent(Pdf(1000)), "shkrese.pdf")));
        }

        var outsider = (string)(await Scalar($"SELECT email FROM users WHERE id = {s.Seed.User("drlawyer").Id}"))!;
        using var other = s.As(outsider);
        await Problem(await Upload(other, s.Case, Form(new ByteArrayContent(Pdf(1000)), "a.pdf")), HttpStatusCode.NotFound);
        await Problem(await Upload(other, Guid.NewGuid(), Form(new ByteArrayContent(Pdf(1000)), "a.pdf")), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_closed_case_refuses_uploads()
    {
        await using var s = await SetupAsync();
        await Execute($"UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = UTC_TIMESTAMP(3), closed_by = {s.Seed.User("kryetar").Id} " +
                      $"WHERE id = {s.Seed.ActiveCaseId}");
        using var client = s.As(Primary);

        var text = await Problem(await Upload(client, s.Case, Form(new ByteArrayContent(Pdf(1000)), "a.pdf")), HttpStatusCode.Conflict);

        text.ShouldContain("case.closed");
    }

    // ---------------------------------------------------------------- memory

    [Fact]
    public async Task A_40_MB_pdf_streams_through_the_api_without_holding_the_file()
    {
        await using var s = await SetupAsync();
        // The default test client follows redirects and therefore BUFFERS every request body (to resend it): it would
        // hold the 40 MB itself. A plain client, like a browser's upload, streams.
        using var client = s.Factory
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false })
            .AsUser(Primary);
        // warm-up: the host's first request builds caches that stay (EF models, the S3 client, routing, JIT) — not
        // what the upload holds
        await Created(await Upload(client, s.Case, Form(new ByteArrayContent(Pdf(64 * 1024)), "warm-up.pdf")));
        const long size = 40L * 1024 * 1024;
        var source = new GeneratedStream(size, prefix: PdfMagic, liveSampleEvery: 4L * 1024 * 1024);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var before = GC.GetTotalMemory(forceFullCollection: true);

        var body = await Created(await Upload(client, s.Case, Form(new StreamContent(source), "dosja-e-plote.pdf")));

        var heap = source.PeakManagedMemory - before;
        var live = source.PeakLiveMemory - before;
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"during the upload (client + API in one process): heap peak +{heap / 1024 / 1024} MB, live peak +{live / 1024 / 1024} MB");
        heap.ShouldBeLessThan(100L * 1024 * 1024); // everything allocated, garbage included
        live.ShouldBeLessThan(30L * 1024 * 1024); // what is held: one 8 MB part buffer and the pipes — never the 40 MB file
        var doc = await DocumentAsync(body.GetProperty("documentPublicId").GetGuid());
        doc.Size.ShouldBe(size);
        doc.Sha256.ShouldBe(source.Sha256);
    }

    // ---------------------------------------------------------------- the scan job

    [Fact]
    public async Task An_infected_file_is_rejected_with_history_and_an_event()
    {
        var events = new EventRecorder();
        await using var s = await SetupAsync(services =>
        {
            services.AddSingleton<IVirusScanner>(new FakeScanner(_ => ScanResult.Infected("Eicar-Test-Signature")));
            services.AddSingleton<IEventHandler<DocumentInfected>>(events);
        });
        using var client = s.As(Primary);
        var id = (await Created(await Upload(client, s.Case, Form(new ByteArrayContent(Pdf(1000)), "virus.pdf", title: "Prova"))))
            .GetProperty("documentPublicId").GetGuid();

        await s.Job.RunOnceAsync(Ct);

        var doc = await DocumentAsync(id);
        doc.Status.ShouldBe("INFECTED");
        doc.Bucket.ShouldBe(minio.Options.Buckets.Quarantine);
        (await minio.Storage.ExistsAsync(minio.Options.Buckets.Quarantine, doc.Key, Ct)).ShouldBeTrue(); // left to expire
        (await minio.Storage.ExistsAsync(minio.Options.Buckets.Documents, doc.Key, Ct)).ShouldBeFalse();
        var rejected = (string)(await Scalar("SELECT summary FROM case_history_events WHERE event_type = 'DOCUMENT_REJECTED'"))!;
        rejected.ShouldBe("Dokumenti \"Prova\" u refuzua: u gjet virus (Eicar-Test-Signature).");
        (await Scalar("SELECT actor_user_id FROM case_history_events WHERE event_type = 'DOCUMENT_REJECTED'")).ShouldBeNull(); // system
        var infected = events.Received.ShouldHaveSingleItem();
        infected.DocumentPublicId.ShouldBe(id);
        infected.UploadedByUserId.ShouldBe(s.Seed.User("avokat1").Id);
    }

    [Fact]
    public async Task A_jar_renamed_as_docx_is_rejected_as_an_invalid_format_and_a_real_docx_is_clean()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);
        var jar = Zip(("META-INF/MANIFEST.MF", "Manifest-Version: 1.0"), ("app/Main.class", "cafebabe"));
        var docx = Zip(("[Content_Types].xml", "<Types/>"), ("word/document.xml", "<w:document/>"));

        var jarId = (await Created(await Upload(client, s.Case, Form(new ByteArrayContent(jar), "kontrata.docx"))))
            .GetProperty("documentPublicId").GetGuid();
        var docxId = (await Created(await Upload(client, s.Case, Form(new ByteArrayContent(docx), "kontrata-e-vertete.docx"))))
            .GetProperty("documentPublicId").GetGuid();

        await s.Job.RunOnceAsync(Ct);

        (await DocumentAsync(jarId)).Status.ShouldBe("INFECTED");
        ((string)(await Scalar("SELECT summary FROM case_history_events WHERE event_type = 'DOCUMENT_REJECTED'"))!)
            .ShouldEndWith("u refuzua: format i pavlefshëm.");
        (await DocumentAsync(docxId)).Status.ShouldBe("CLEAN");
    }

    [Fact]
    public async Task Scanner_unavailable_keeps_the_document_pending_then_fails_it_after_five_attempts()
    {
        var events = new EventRecorder();
        await using var s = await SetupAsync(services =>
        {
            services.AddSingleton<IVirusScanner>(new FakeScanner(_ => throw new ScannerUnavailableException("ClamAV is down.")));
            services.AddSingleton<IEventHandler<DocumentScanFailed>>(events);
        });
        using var client = s.As(Primary);
        var id = (await Created(await Upload(client, s.Case, Form(new ByteArrayContent(Pdf(1000)), "a.pdf"))))
            .GetProperty("documentPublicId").GetGuid();

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await s.Job.RunOnceAsync(Ct);
            (await DocumentAsync(id)).Status.ShouldBe("PENDING", $"after attempt {attempt}");
        }

        await s.Job.RunOnceAsync(Ct);

        (await DocumentAsync(id)).Status.ShouldBe("FAILED");
        events.Failed.ShouldHaveSingleItem().DocumentPublicId.ShouldBe(id);
    }

    [Fact]
    public async Task Two_job_instances_in_parallel_never_scan_the_same_document_twice()
    {
        var scans = new ConcurrentDictionary<string, int>();
        await using var s = await SetupAsync(services => services.AddSingleton<IVirusScanner>(new FakeScanner(content =>
        {
            Thread.Sleep(150); // keep the row locked a while: the other instance must skip it, not wait for it
            scans.AddOrUpdate(Convert.ToHexString(SHA256.HashData(content)), 1, (_, n) => n + 1);
            return ScanResult.Clean;
        })));
        using var client = s.As(Primary);
        for (var i = 0; i < 6; i++)
        {
            await Created(await Upload(client, s.Case, Form(new ByteArrayContent(Pdf(2048, seed: 100 + i)), $"dok-{i}.pdf")));
        }

        var first = ActivatorUtilities.CreateInstance<ScanQueueJob>(s.Factory.Services);
        var second = ActivatorUtilities.CreateInstance<ScanQueueJob>(s.Factory.Services);
        var decided = await Task.WhenAll(Task.Run(() => first.RunOnceAsync(Ct), Ct), Task.Run(() => second.RunOnceAsync(Ct), Ct));

        decided.Sum().ShouldBe(6);
        decided.ShouldAllBe(n => n > 0); // both took part
        scans.Count.ShouldBe(6);
        scans.Values.ShouldAllBe(n => n == 1);
        (await Scalar("SELECT COUNT(*) FROM documents WHERE scan_status = 'CLEAN'")).ShouldBe(6L);
    }

    [Fact]
    public void NoOp_scanner_outside_Development_stops_the_startup()
    {
        using var factory = new AshApiFactory(Db, environment: "Production", scannerMode: "NoOp");

        var error = Should.Throw<InvalidOperationException>(() => factory.CreateClient());

        error.Message.ShouldContain("NoOp is allowed only in Development");
    }

    // ---------------------------------------------------------------- fakes

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    /// <summary>Reads the whole stream, then answers with <paramref name="verdict"/> (which may throw).</summary>
    private sealed class FakeScanner(Func<byte[], ScanResult> verdict) : IVirusScanner
    {
        public async Task<ScanResult> ScanAsync(Stream content, CancellationToken ct = default)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            return verdict(copy.ToArray());
        }
    }

    private sealed class EventRecorder : IEventHandler<DocumentInfected>, IEventHandler<DocumentScanFailed>
    {
        public ConcurrentQueue<DocumentInfected> Received { get; } = new();

        public ConcurrentQueue<DocumentScanFailed> Failed { get; } = new();

        public Task HandleAsync(DocumentInfected @event, CancellationToken cancellationToken = default)
        {
            Received.Enqueue(@event);
            return Task.CompletedTask;
        }

        public Task HandleAsync(DocumentScanFailed @event, CancellationToken cancellationToken = default)
        {
            Failed.Enqueue(@event);
            return Task.CompletedTask;
        }
    }
}
