using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Ash.Infrastructure.Storage;
using Ash.SharedKernel.Ids;
using Ash.SharedKernel.Time;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 034 — the dossier timeline, download / preview and soft delete. One dossier, two levels: X/1 = CaseSeed's
/// ACTIVE case (avokat1 primary, avokat2 secondary, the ministry monitoring); X/2 = a new level whose only lawyer is
/// DR's lawyer, who cannot view X/1 itself. Documents are inserted directly (rows + objects in MinIO).
/// </summary>
public class DocumentTimelineTests(MySqlFixture db, MinioFixture minio) : IntegrationTest(db), IClassFixture<MinioFixture>
{
    private const string Primary = "avokat1@ash.local";
    private const string Secondary = "avokat2@ash.local";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Ctx(CaseSeed Seed, AshApiFactory Factory, long Level1, Guid Level1Public, long Level2, Guid Level2Public,
        string Level2LawyerEmail, string Level1Code, string Level2Code) : IAsyncDisposable
    {
        public HttpClient As(string email) => Factory.CreateClient().AsUser(email);

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            await Seed.DisposeAsync();
        }
    }

    private async Task<Ctx> SetupAsync()
    {
        var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var level1 = seed.ActiveCaseId;
        var code1 = (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {level1}"))!;
        var code2 = code1[..^2] + "/2";
        await Execute(
            "INSERT INTO cases (dossier_id, level_seq, case_code, previous_case_id, office_id, summons_number, act_number, received_date, " +
            "court_level_id, court_id, object_text, lifecycle_status, registered_by) " +
            $"SELECT dossier_id, 2, '{code2}', id, office_id, 'FL-2', 'AKT-2', CURDATE(), court_level_id, court_id, object_text, 'ACTIVE', registered_by " +
            $"FROM cases WHERE id = {level1}");
        var level2 = Convert.ToInt64(await Scalar($"SELECT id FROM cases WHERE case_code = '{code2}'"));
        var drLawyer = seed.User("drlawyer").Id;
        await Execute($"INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by) VALUES ({level2}, {drLawyer}, 'PRIMARY_LAWYER', {seed.User("kryetar").Id})");
        var email = (string)(await Scalar($"SELECT email FROM users WHERE id = {drLawyer}"))!;
        var factory = new AshApiFactory(Db, storage: minio.Options);
        return new Ctx(seed, factory, level1, await PublicIdOf("cases", level1), level2, await PublicIdOf("cases", level2), email, code1, code2);
    }

    /// <summary>A documents row (and, with content, its object in the row's bucket).</summary>
    private async Task<(Guid Id, string Key, byte[] Content)> DocAsync(long caseId, string title, DateTime uploadedAt,
        string type = "PADI_PROVA", string status = "CLEAN", string contentType = "application/pdf", string fileName = "dok.pdf", int size = 2048)
    {
        var content = new byte[size];
        new Random(title.GetHashCode()).NextBytes(content);
        var key = StorageKeys.New(new SystemClock());
        var bucket = status == "CLEAN" ? minio.Options.Buckets.Documents : minio.Options.Buckets.Quarantine;
        await minio.Storage.PutAsync(bucket, key, new MemoryStream(content), contentType, Ct);
        var publicId = Guid.NewGuid();
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(
            "INSERT INTO documents (public_id, dossier_id, case_id, document_type_id, title, original_file_name, content_type, size_bytes, " +
            "sha256, storage_bucket, storage_key, scan_status, uploaded_by, uploaded_at) " +
            "SELECT @p, c.dossier_id, c.id, t.id, @title, @file, @ct, @size, @sha, @bucket, @key, @status, " +
            "(SELECT id FROM users WHERE email = 'avokat1@ash.local'), @at FROM cases c JOIN document_types t ON t.code = @type WHERE c.id = @case",
            connection);
        command.Parameters.AddWithValue("@p", PublicId.ToBytes(publicId));
        command.Parameters.AddWithValue("@title", title);
        command.Parameters.AddWithValue("@file", fileName);
        command.Parameters.AddWithValue("@ct", contentType);
        command.Parameters.AddWithValue("@size", size);
        command.Parameters.AddWithValue("@sha", SHA256.HashData(content));
        command.Parameters.AddWithValue("@bucket", bucket);
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@at", uploadedAt);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@case", caseId);
        await command.ExecuteNonQueryAsync(Ct);
        return (publicId, key, content);
    }

    private static readonly DateTime T0 = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------- timeline

    [Fact]
    public async Task The_X2_lawyer_sees_the_documents_of_both_levels_newest_first()
    {
        await using var s = await SetupAsync();
        var first = await DocAsync(s.Level1, "Padia", T0);
        var second = await DocAsync(s.Level2, "Ankimi", T0.AddDays(10), type: "ANKIM");
        using var lawyer2 = s.As(s.Level2LawyerEmail);

        var page = await GetJson(lawyer2, $"/api/cases/{s.Level2Public}/documents");

        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("publicId").GetGuid()).ShouldBe([second.Id, first.Id]);
        items[0].GetProperty("level").GetString().ShouldBe(s.Level2Code);
        items[1].GetProperty("level").GetString().ShouldBe(s.Level1Code);
        items[1].GetProperty("documentType").GetProperty("name").GetString().ShouldBe("Padi / Prova");
        items[1].GetProperty("uploadedBy").GetString().ShouldBe("Avokat1 Dev");
        items[1].GetProperty("canPreview").GetBoolean().ShouldBeTrue();
        items[1].GetProperty("sizeBytes").GetInt64().ShouldBe(2048);
        // the dossier rule: the first-instance document downloads for the appeal lawyer…
        (await lawyer2.GetAsync($"/api/documents/{first.Id}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        // …although the X/1 case itself stays closed to them
        (await lawyer2.GetAsync($"/api/cases/{s.Level1Public}/documents", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Filters_by_type_and_level()
    {
        await using var s = await SetupAsync();
        var padi = await DocAsync(s.Level1, "Padia", T0);
        var akt = await DocAsync(s.Level1, "Akti", T0.AddHours(1), type: "AKT_PROCEDURAL");
        var ankim = await DocAsync(s.Level2, "Ankimi", T0.AddHours(2), type: "ANKIM");
        using var client = s.As(Primary);

        (await Ids(client, $"/api/cases/{s.Level1Public}/documents?type=AKT_PROCEDURAL")).ShouldBe([akt.Id]);
        (await Ids(client, $"/api/cases/{s.Level1Public}/documents?level=1")).ShouldBe([akt.Id, padi.Id]);
        (await Ids(client, $"/api/cases/{s.Level1Public}/documents?level=2")).ShouldBe([ankim.Id]);
        (await Ids(client, $"/api/cases/{s.Level1Public}/documents?level=2&type=PADI_PROVA")).ShouldBeEmpty();
        (await client.GetAsync($"/api/cases/{s.Level1Public}/documents?level=7", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync($"/api/cases/{s.Level1Public}/documents?type=NUK_KA", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Filters_by_status_rejected_meaning_infected_or_failed_and_carries_the_total()
    {
        await using var s = await SetupAsync();
        var clean = await DocAsync(s.Level1, "Gati", T0);
        var pending = await DocAsync(s.Level1, "Në kontroll", T0.AddHours(1), status: "PENDING");
        var infected = await DocAsync(s.Level1, "Me virus", T0.AddHours(2), status: "INFECTED");
        var failed = await DocAsync(s.Level2, "Pa kontroll", T0.AddHours(3), status: "FAILED");
        using var client = s.As(Primary);

        (await Ids(client, $"/api/cases/{s.Level1Public}/documents?status=CLEAN")).ShouldBe([clean.Id]);
        (await Ids(client, $"/api/cases/{s.Level1Public}/documents?status=pending")).ShouldBe([pending.Id]);
        (await Ids(client, $"/api/cases/{s.Level1Public}/documents?status=REJECTED")).ShouldBe([failed.Id, infected.Id]);
        (await Ids(client, $"/api/cases/{s.Level1Public}/documents?status=REJECTED&level=1")).ShouldBe([infected.Id]);
        (await client.GetAsync($"/api/cases/{s.Level1Public}/documents?status=INFECTED", Ct)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);

        // the total of the filtered timeline, the same on every page
        var first = await GetJson(client, $"/api/cases/{s.Level1Public}/documents?limit=1");
        first.GetProperty("total").GetInt32().ShouldBe(4);
        var cursor = Uri.EscapeDataString(first.GetProperty("nextCursor").GetString()!);
        (await GetJson(client, $"/api/cases/{s.Level1Public}/documents?limit=1&after={cursor}")).GetProperty("total").GetInt32().ShouldBe(4);
        (await GetJson(client, $"/api/cases/{s.Level1Public}/documents?status=REJECTED")).GetProperty("total").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Pages_through_the_timeline_without_duplicates_also_on_equal_times()
    {
        await using var s = await SetupAsync();
        var expected = new List<Guid>();
        for (var i = 0; i < 7; i++)
        {
            // pairs share the same uploaded_at: the id breaks the tie
            expected.Add((await DocAsync(i % 2 == 0 ? s.Level1 : s.Level2, $"Dok {i}", T0.AddMinutes(i / 2))).Id);
        }

        using var client = s.As(Primary);
        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await GetJson(client, $"/api/cases/{s.Level1Public}/documents?limit=3" + (cursor is null ? "" : $"&after={Uri.EscapeDataString(cursor)}"));
            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("publicId").GetGuid()));
            cursor = page.GetProperty("nextCursor").ValueKind == JsonValueKind.String ? page.GetProperty("nextCursor").GetString() : null;
            pages++;
        }
        while (cursor is not null && pages < 10);

        pages.ShouldBe(3);
        seen.Count.ShouldBe(7);
        seen.Distinct().Count().ShouldBe(7);
        seen.ToHashSet().SetEquals(expected).ShouldBeTrue();
    }

    [Fact]
    public async Task Responses_carry_no_internal_ids_and_no_storage_keys()
    {
        await using var s = await SetupAsync();
        var doc = await DocAsync(s.Level1, "Padia", T0);
        using var client = s.As(Primary);

        var text = await (await client.GetAsync($"/api/cases/{s.Level1Public}/documents", Ct)).Content.ReadAsStringAsync(Ct);

        JsonAssert.NoNumericIds(JsonDocument.Parse(text).RootElement);
        text.ShouldNotContain(doc.Key);
        text.ShouldNotContain(minio.Options.Buckets.Documents);
    }

    // ---------------------------------------------------------------- download and preview

    [Fact]
    public async Task Only_clean_documents_are_served()
    {
        await using var s = await SetupAsync();
        var pending = await DocAsync(s.Level1, "Në kontroll", T0, status: "PENDING");
        var infected = await DocAsync(s.Level1, "Me virus", T0, status: "INFECTED");
        var failed = await DocAsync(s.Level1, "Pa kontroll", T0, status: "FAILED");
        using var client = s.As(Primary);

        (await Problem(await client.GetAsync($"/api/documents/{pending.Id}/download", Ct), HttpStatusCode.Conflict))
            .ShouldContain("Dokumenti po kontrollohet ende");
        (await Problem(await client.GetAsync($"/api/documents/{infected.Id}/preview", Ct), HttpStatusCode.Conflict))
            .ShouldContain("Dokumenti u refuzua nga kontrolli antivirus");
        (await Problem(await client.GetAsync($"/api/documents/{failed.Id}/download", Ct), HttpStatusCode.Conflict))
            .ShouldContain("Dokumenti u refuzua nga kontrolli antivirus");
        var list = await GetJson(client, $"/api/cases/{s.Level1Public}/documents");
        list.GetProperty("items").EnumerateArray().ShouldAllBe(i => !i.GetProperty("canPreview").GetBoolean());
    }

    [Fact]
    public async Task Download_streams_the_file_as_an_attachment()
    {
        await using var s = await SetupAsync();
        var doc = await DocAsync(s.Level1, "Vendimi", T0, fileName: "Vendim_gjykatë_çështja.pdf", size: 50_000);
        using var client = s.As(Primary);

        using var response = await client.GetAsync($"/api/documents/{doc.Id}/download", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(doc.Content);
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileNameStar.ShouldBe("Vendim_gjykatë_çështja.pdf");
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        response.Headers.AcceptRanges.ShouldContain("bytes");
    }

    [Fact]
    public async Task Preview_is_inline_in_a_sandbox_and_only_for_pdf_png_jpg()
    {
        await using var s = await SetupAsync();
        var pdf = await DocAsync(s.Level1, "Padia", T0);
        var docx = await DocAsync(s.Level1, "Kontrata", T0,
            contentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document", fileName: "kontrata.docx");
        using var client = s.As(Primary);

        using var response = await client.GetAsync($"/api/documents/{pdf.Id}/preview", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("inline");
        response.Headers.GetValues("Content-Security-Policy").ShouldBe(
            ["sandbox; default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'"]);
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(pdf.Content);
        (await Problem(await client.GetAsync($"/api/documents/{docx.Id}/preview", Ct), HttpStatusCode.BadRequest))
            .ShouldContain("document.preview_unsupported");
    }

    [Fact]
    public async Task A_range_request_gets_206_with_exactly_those_bytes()
    {
        await using var s = await SetupAsync();
        var doc = await DocAsync(s.Level1, "E madhe", T0, size: 100_000);
        using var client = s.As(Primary);

        using var middle = await Ranged(client, $"/api/documents/{doc.Id}/preview", "bytes=1000-1999");
        middle.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        middle.Content.Headers.ContentRange!.ToString().ShouldBe("bytes 1000-1999/100000");
        (await middle.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(doc.Content[1000..2000]);

        using var tail = await Ranged(client, $"/api/documents/{doc.Id}/download", "bytes=-10");
        tail.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        (await tail.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(doc.Content[^10..]);

        using var open = await Ranged(client, $"/api/documents/{doc.Id}/download", "bytes=99990-");
        (await open.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(doc.Content[99990..]);

        using var outside = await Ranged(client, $"/api/documents/{doc.Id}/download", "bytes=100000-100010");
        outside.StatusCode.ShouldBe(HttpStatusCode.RequestedRangeNotSatisfiable);
        outside.Content.Headers.ContentRange!.ToString().ShouldBe("bytes */100000");
    }

    [Fact]
    public async Task Download_and_preview_each_write_one_audit_row()
    {
        await using var s = await SetupAsync();
        var doc = await DocAsync(s.Level1, "Padia", T0);
        var documentId = Convert.ToInt64(await Scalar($"SELECT id FROM documents WHERE storage_key = '{doc.Key}'"));
        using var client = s.As(Primary);

        (await client.GetAsync($"/api/documents/{doc.Id}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"/api/documents/{doc.Id}/preview", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var sql = "SELECT GROUP_CONCAT(JSON_UNQUOTE(JSON_EXTRACT(details, '$.Mode')) ORDER BY id) FROM ash_audit.audit_log " +
                  $"WHERE action = 'DOCUMENT_DOWNLOAD' AND entity_type = 'DOCUMENT' AND entity_id = {documentId} AND case_id = {s.Level1}";
        var modes = await PollAsync(sql, value => value == "download,preview");
        modes.ShouldBe("download,preview");
    }

    // ---------------------------------------------------------------- delete

    [Fact]
    public async Task Delete_hides_the_document_keeps_history_and_the_object()
    {
        await using var s = await SetupAsync();
        var doc = await DocAsync(s.Level1, "Gabim", T0);
        var other = await DocAsync(s.Level1, "E mirë", T0.AddHours(1));
        using var client = s.As(Primary);

        var response = await Delete(client, doc.Id, "  U ngarkua në çështjen e gabuar.  ");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await Ids(client, $"/api/cases/{s.Level1Public}/documents")).ShouldBe([other.Id]);
        (await client.GetAsync($"/api/documents/{doc.Id}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Scalar($"SELECT delete_reason FROM documents WHERE storage_key = '{doc.Key}'")).ShouldBe("U ngarkua në çështjen e gabuar.");
        (await Scalar($"SELECT deleted_by IS NOT NULL FROM documents WHERE storage_key = '{doc.Key}'")).ShouldBe(1L);
        (await Scalar("SELECT summary FROM case_history_events WHERE event_type = 'DOCUMENT_REMOVED'"))
            .ShouldBe("Dokumenti u hoq: Gabim. Arsyeja: U ngarkua në çështjen e gabuar.");
        (await minio.Storage.ExistsAsync(minio.Options.Buckets.Documents, doc.Key, Ct)).ShouldBeTrue(); // the record stays
        (await Delete(client, doc.Id, "sërish")).StatusCode.ShouldBe(HttpStatusCode.NotFound); // already gone
    }

    [Fact]
    public async Task Delete_needs_a_reason_of_at_most_300_characters()
    {
        await using var s = await SetupAsync();
        var doc = await DocAsync(s.Level1, "Gabim", T0);
        using var client = s.As(Primary);

        (await Problem(await Delete(client, doc.Id, " "), HttpStatusCode.BadRequest)).ShouldContain("reason");
        (await Problem(await Delete(client, doc.Id, new string('a', 301)), HttpStatusCode.BadRequest)).ShouldContain("reason");
    }

    [Fact]
    public async Task A_decision_or_security_measure_document_cannot_be_deleted()
    {
        await using var s = await SetupAsync();
        var decision = await DocAsync(s.Level1, "Vendimi", T0);
        var measure = await DocAsync(s.Level2, "Masa", T0);
        var kryetar = s.Seed.User("kryetar").Id;
        await Execute("INSERT INTO case_decisions (case_id, result_id, recorded_by, decision_document_id) " +
                      $"SELECT {s.Level1}, (SELECT id FROM decision_results WHERE code = 'FITUAR'), {kryetar}, id FROM documents WHERE storage_key = '{decision.Key}'");
        await Execute("INSERT INTO case_security_measures (case_id, decision_number, decision_date, recorded_by, document_id) " +
                      $"SELECT {s.Level2}, '12/2026', '2026-10-01', {kryetar}, id FROM documents WHERE storage_key = '{measure.Key}'");

        using (var lawyer1 = s.As(Primary))
        {
            (await Problem(await Delete(lawyer1, decision.Id, "gabim"), HttpStatusCode.Conflict))
                .ShouldContain("Dokumenti është i lidhur me vendimin dhe nuk mund të hiqet.");
        }

        using var lawyer2 = s.As(s.Level2LawyerEmail);
        (await Problem(await Delete(lawyer2, measure.Id, "gabim"), HttpStatusCode.Conflict))
            .ShouldContain("Dokumenti është i lidhur me masën e sigurimit dhe nuk mund të hiqet.");
    }

    [Fact]
    public async Task Secondary_lawyer_gets_403_closed_level_409_and_a_user_without_access_404()
    {
        await using var s = await SetupAsync();
        var doc = await DocAsync(s.Level1, "Padia", T0);

        using (var secondary = s.As(Secondary))
        {
            (await Problem(await Delete(secondary, doc.Id, "gabim"), HttpStatusCode.Forbidden)).ShouldContain("document.delete_forbidden");
        }

        // the X/2 lawyer sees the X/1 document but has no rights on X/1
        using (var lawyer2 = s.As(s.Level2LawyerEmail))
        {
            (await Delete(lawyer2, doc.Id, "gabim")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var outsider = s.As("protokoll@ash.local"))
        {
            (await outsider.GetAsync($"/api/cases/{s.Level1Public}/documents", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await outsider.GetAsync($"/api/documents/{doc.Id}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await Delete(outsider, doc.Id, "gabim")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        await Execute($"UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = UTC_TIMESTAMP(3), closed_by = {s.Seed.User("kryetar").Id} WHERE id = {s.Level1}");
        using var primary = s.As(Primary);
        (await Problem(await Delete(primary, doc.Id, "gabim"), HttpStatusCode.Conflict)).ShouldContain("case.closed");
        (await primary.GetAsync($"/api/documents/{doc.Id}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK); // still readable
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<JsonElement> GetJson(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<List<Guid>> Ids(HttpClient client, string url) =>
        (await GetJson(client, url)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("publicId").GetGuid()).ToList();

    private static async Task<string> Problem(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, text);
        return text;
    }

    private static Task<HttpResponseMessage> Delete(HttpClient client, Guid id, string reason) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/documents/{id}") { Content = JsonContent.Create(new { reason }) }, Ct);

    private static Task<HttpResponseMessage> Ranged(HttpClient client, string url, string range)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Range", range);
        return client.SendAsync(request, Ct);
    }

    private async Task<string?> PollAsync(string sql, Func<string?, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        string? value;
        while (!done(value = (string?)await Scalar(sql, Db.AuditConnectionString)) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, Ct);
        }

        return value;
    }

    private async Task<object?> Scalar(string sql, string? connectionString = null)
    {
        await using var connection = new MySqlConnection(connectionString ?? Db.AppConnectionString);
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
}
