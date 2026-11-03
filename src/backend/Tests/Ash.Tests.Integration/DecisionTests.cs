using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Modules.Cases.Contracts.Events;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Ids;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Tasks 036/037 — record the decision and close the level (POST /decision), complete it later (PUT /decision).
/// <see cref="CaseSeed"/>'s ACTIVE first-instance case (appeal kind ANKIM): avokat1 primary, avokat2 secondary,
/// jurist of the ministry, the ministry linked as MONITORING.
/// </summary>
public class DecisionTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Primary = "avokat1@ash.local";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class ClosedRecorder
    {
        public ConcurrentQueue<CaseClosed> Events { get; } = new();
    }

    private sealed class ClosedHandler(ClosedRecorder recorder) : IEventHandler<CaseClosed>
    {
        public Task HandleAsync(CaseClosed @event, CancellationToken cancellationToken = default)
        {
            recorder.Events.Enqueue(@event);
            return Task.CompletedTask;
        }
    }

    private sealed record Ctx(CaseSeed Seed, AshApiFactory Factory, Guid Case, ClosedRecorder Recorder) : IAsyncDisposable
    {
        public HttpClient As(string email) => Factory.CreateClient().AsUser(email);
        public long Id => Seed.ActiveCaseId;

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            await Seed.DisposeAsync();
        }
    }

    private async Task<Ctx> SetupAsync()
    {
        var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var recorder = new ClosedRecorder();
        var factory = new AshApiFactory(Db, configureServices: services =>
        {
            services.AddSingleton(recorder);
            services.AddScoped<IEventHandler<CaseClosed>, ClosedHandler>();
        });
        var id = PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM cases WHERE id = {seed.ActiveCaseId}"))!);
        return new Ctx(seed, factory, id, recorder);
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static async Task<JsonElement> CaseOf(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/cases/{id}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await Json(response);
    }

    private static async Task<uint> CaseRowVersion(HttpClient client, Guid id) =>
        (await CaseOf(client, id)).GetProperty("header").GetProperty("rowVersion").GetUInt32();

    /// <summary>A decision body: HUMBUR, not received, nothing else — with the given fields replaced.</summary>
    private static Dictionary<string, object?> Body(uint rowVersion, params (string Key, object? Value)[] overrides)
    {
        var body = new Dictionary<string, object?>
        {
            ["resultCode"] = "HUMBUR",
            ["decisionNumber"] = "V-123",
            ["decisionDate"] = DateTime.UtcNow.AddDays(-3).ToString("yyyy-MM-dd"),
            ["decisionReceived"] = false,
            ["rowVersion"] = rowVersion,
        };
        foreach (var (key, value) in overrides)
        {
            body[key] = value;
        }

        return body;
    }

    private static async Task<HttpResponseMessage> Record(HttpClient client, Guid id, Dictionary<string, object?> body) =>
        await client.PostAsJsonAsync($"/api/cases/{id}/decision", body, Ct);

    private static async Task<HttpResponseMessage> Update(HttpClient client, Guid id, Dictionary<string, object?> body) =>
        await client.PutAsJsonAsync($"/api/cases/{id}/decision", body, Ct);

    private static async Task<string> Problem(HttpResponseMessage response, HttpStatusCode status)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(status, text);
        return text;
    }

    /// <summary>Closes the case through the API (HUMBUR, not received); returns the decision's rowVersion.</summary>
    private static async Task<uint> CloseAsync(HttpClient client, Guid id, params (string Key, object? Value)[] overrides)
    {
        var response = await Record(client, id, Body(await CaseRowVersion(client, id), overrides));
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.GetProperty("decision").GetProperty("rowVersion").GetUInt32();
    }

    /// <summary>A documents row of the case's dossier (the file itself is not needed to link it).</summary>
    private async Task<Guid> DocumentAsync(long caseId, string type = "VENDIM", string status = "CLEAN", string title = "Vendimi i arsyetuar")
    {
        var publicId = Guid.NewGuid();
        await Execute(
            "INSERT INTO documents (public_id, dossier_id, case_id, document_type_id, title, original_file_name, content_type, size_bytes, " +
            "sha256, storage_bucket, storage_key, scan_status, uploaded_by, uploaded_at) " +
            "SELECT @p, c.dossier_id, c.id, t.id, @title, 'vendim.pdf', 'application/pdf', 1000, UNHEX(SHA2('x', 256)), 'b', @key, @status, " +
            "(SELECT id FROM users WHERE email = 'avokat1@ash.local'), UTC_TIMESTAMP(3) FROM cases c JOIN document_types t ON t.code = @type WHERE c.id = @case",
            ("@p", PublicId.ToBytes(publicId)), ("@title", title), ("@key", $"k/{publicId:N}"), ("@status", status), ("@type", type), ("@case", caseId));
        return publicId;
    }

    private async Task<long> HearingAsync(long caseId, DateTime atUtc)
    {
        await Execute(
            "INSERT INTO case_hearings (case_id, hearing_type_id, scheduled_at, status, created_by) " +
            "SELECT @c, id, @at, 'SCHEDULED', (SELECT id FROM users WHERE email = 'avokat1@ash.local') FROM hearing_types WHERE code = 'PERGATITORE'",
            ("@c", caseId), ("@at", atUtc));
        var id = Convert.ToInt64(await Scalar($"SELECT MAX(id) FROM case_hearings WHERE case_id = {caseId}"));
        await Execute($"UPDATE cases SET next_hearing_at = (SELECT MIN(scheduled_at) FROM case_hearings WHERE case_id = {caseId} AND status = 'SCHEDULED') WHERE id = {caseId}");
        return id;
    }

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    private async Task Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(Ct);
    }

    // ---------------------------------------------------------------- 036: record + close

    [Fact]
    public async Task Closing_without_the_reasoned_decision_closes_the_case_and_lists_it_as_pending()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);
        var rowVersion = await CaseRowVersion(client, s.Case);
        var teamBefore = await Scalar($"SELECT COUNT(*) FROM case_assignments WHERE case_id = {s.Id} AND ended_at IS NULL");

        var response = await Record(client, s.Case, Body(rowVersion));

        var result = await Json(response);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, result.ToString());
        result.GetProperty("caseRowVersion").GetUInt32().ShouldBe(rowVersion + 1);
        result.GetProperty("decision").GetProperty("decisionReceived").GetBoolean().ShouldBeFalse();
        result.GetProperty("suggestions").GetArrayLength().ShouldBe(0);
        (await Scalar($"SELECT lifecycle_status FROM cases WHERE id = {s.Id}")).ShouldBe("CLOSED");
        (await Scalar($"SELECT closed_at IS NOT NULL AND next_hearing_at IS NULL FROM cases WHERE id = {s.Id}")).ShouldBe(1L);
        (await Scalar($"SELECT closed_by FROM cases WHERE id = {s.Id}")).ShouldBe((uint)s.Seed.User("avokat1").Id);
        (await Scalar($"SELECT COUNT(*) FROM case_decisions WHERE case_id = {s.Id} AND decision_received = 0 AND decision_received_at IS NULL"))
            .ShouldBe(1L);
        // "mbartur në pritje të vendimit të arsyetuar": the statistics read ix_cd_pending
        (await Scalar($"SELECT COUNT(*) FROM case_decisions FORCE INDEX (ix_cd_pending) WHERE decision_received = 0 AND case_id = {s.Id}"))
            .ShouldBe(1L);
        // the team stays: the lawyers still see the closed case
        (await Scalar($"SELECT COUNT(*) FROM case_assignments WHERE case_id = {s.Id} AND ended_at IS NULL")).ShouldBe(teamBefore);
        (await Scalar($"SELECT GROUP_CONCAT(event_type ORDER BY id) FROM case_history_events WHERE case_id = {s.Id}"))
            .ShouldBe("DECISION_RECORDED,CASE_CLOSED");
        var closed = s.Recorder.Events.ShouldHaveSingleItem();
        closed.CasePublicId.ShouldBe(s.Case);
        closed.ResultCode.ShouldBe("HUMBUR");
        closed.DecisionReceived.ShouldBeFalse();

        // a second POST: the decision exists, it is completed with PUT
        (await Problem(await Record(client, s.Case, Body(rowVersion + 1)), HttpStatusCode.Conflict)).ShouldContain("decision.exists");
    }

    [Fact]
    public async Task The_lawyer_still_finds_the_closed_case_in_my_cases_with_closed_or_all()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);
        await CloseAsync(client, s.Case);

        async Task<List<Guid>> Ids(string query) => (await Json(await client.GetAsync($"/api/cases?tab=mine&{query}&limit=100", Ct)))
            .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("publicId").GetGuid()).ToList();

        (await Ids("status=CLOSED")).ShouldContain(s.Case);
        (await Ids("status=ALL")).ShouldContain(s.Case);
        (await Ids("status=OPEN")).ShouldNotContain(s.Case);
        var page = await CaseOf(client, s.Case);
        page.GetProperty("header").GetProperty("status").GetString().ShouldBe("CLOSED");
    }

    [Fact]
    public async Task Final_and_appealed_together_is_400_with_the_albanian_message_and_nothing_changes()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);

        var text = await Problem(await Record(client, s.Case,
            Body(await CaseRowVersion(client, s.Case), ("isFinal", true), ("isAppealed", true))), HttpStatusCode.BadRequest);

        text.ShouldContain("Një vendim në formë të prerë nuk mund të jetë i ankimuar/rekursuar.");
        text.ShouldContain("\"isFinal\"");
        (await Scalar($"SELECT lifecycle_status FROM cases WHERE id = {s.Id}")).ShouldBe("ACTIVE");
        (await Scalar($"SELECT COUNT(*) FROM case_decisions WHERE case_id = {s.Id}")).ShouldBe(0L);
    }

    [Theory]
    [InlineData("isRecursed", "true", "isRecursed")] // a first-instance level has "Ankimuar", not "Rekursuar"
    [InlineData("resultCode", "TJETER", "resultOtherText")] // "Tjetër" without its text
    [InlineData("resultCode", "NUK_KA", "resultCode")]
    [InlineData("decisionReceived", "true", "decisionDocumentPublicId")] // received without the document
    [InlineData("wonAmount", "-1", "wonAmount")]
    [InlineData("wonAmount", "1,5", "wonAmount")]
    [InlineData("lostAreaM2", "1234567890123.00", "lostAreaM2")]
    [InlineData("courtCostsAmount", "10.001", "courtCostsAmount")]
    [InlineData("currency", "euro", "currency")]
    [InlineData("decisionDate", "future", "decisionDate")]
    public async Task Invalid_fields_are_400_with_the_field(string field, string value, string errorField)
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);
        object? parsed = value switch
        {
            "true" => true,
            "future" => DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"),
            _ => value
        };

        var text = await Problem(await Record(client, s.Case, Body(await CaseRowVersion(client, s.Case), (field, parsed))),
            HttpStatusCode.BadRequest);

        text.ShouldContain($"\"{errorField}\"");
        (await Scalar($"SELECT lifecycle_status FROM cases WHERE id = {s.Id}")).ShouldBe("ACTIVE");
    }

    [Fact]
    public async Task The_decision_date_cannot_be_before_the_act_date()
    {
        await using var s = await SetupAsync();
        await Execute($"UPDATE cases SET act_date = '2026-03-10' WHERE id = {s.Id}");
        using var client = s.As(Primary);

        (await Problem(await Record(client, s.Case, Body(await CaseRowVersion(client, s.Case), ("decisionDate", "2026-03-09"))),
            HttpStatusCode.BadRequest)).ShouldContain("para datës së aktit");
    }

    [Fact]
    public async Task Received_needs_a_clean_vendim_document_of_this_dossier_and_links_it()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);
        var rowVersion = await CaseRowVersion(client, s.Case);
        var pending = await DocumentAsync(s.Id, status: "PENDING");
        var otherType = await DocumentAsync(s.Id, type: "PADI_PROVA");
        var otherDossier = await DocumentAsync(s.Seed.RegisteredCaseId);
        var good = await DocumentAsync(s.Id);

        foreach (var (document, message) in new[]
                 {
                     (pending, "nuk është kontrolluar ende"),
                     (otherType, "i llojit Vendim"),
                     (otherDossier, "nuk u gjet në këtë dosje"),
                     (Guid.NewGuid(), "nuk u gjet në këtë dosje"),
                 })
        {
            var text = await Problem(await Record(client, s.Case,
                Body(rowVersion, ("decisionReceived", true), ("decisionDocumentPublicId", document))), HttpStatusCode.BadRequest);
            text.ShouldContain("decisionDocumentPublicId");
            text.ShouldContain(message);
        }

        var response = await Record(client, s.Case, Body(rowVersion, ("decisionReceived", true), ("decisionDocumentPublicId", good)));
        var result = await Json(response);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, result.ToString());
        var decision = result.GetProperty("decision");
        decision.GetProperty("decisionReceived").GetBoolean().ShouldBeTrue();
        decision.GetProperty("document").GetProperty("publicId").GetGuid().ShouldBe(good);
        decision.GetProperty("document").GetProperty("title").GetString().ShouldBe("Vendimi i arsyetuar");
        (await Scalar($"SELECT decision_received = 1 AND decision_received_at IS NOT NULL FROM case_decisions WHERE case_id = {s.Id}"))
            .ShouldBe(1L);
        var linked = (byte[])(await Scalar($"SELECT d.public_id FROM case_decisions cd JOIN documents d ON d.id = cd.decision_document_id WHERE cd.case_id = {s.Id}"))!;
        PublicId.FromBytes(linked).ShouldBe(good);
    }

    [Fact]
    public async Task Open_hearings_are_listed_then_cancelled_with_the_closing()
    {
        await using var s = await SetupAsync();
        await HearingAsync(s.Id, DateTime.UtcNow.AddDays(5));
        await HearingAsync(s.Id, DateTime.UtcNow.AddDays(9));
        using var client = s.As(Primary);
        var rowVersion = await CaseRowVersion(client, s.Case);

        var refused = await Record(client, s.Case, Body(rowVersion));
        var problem = await Json(refused);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        problem.GetProperty("code").GetString().ShouldBe("case.open_hearings");
        var listed = problem.GetProperty("hearings").EnumerateArray().ToList();
        listed.Count.ShouldBe(2);
        listed[0].GetProperty("hearingType").GetString().ShouldBe("Seancë përgatitore");
        (await Scalar($"SELECT lifecycle_status FROM cases WHERE id = {s.Id}")).ShouldBe("ACTIVE");

        var response = await Record(client, s.Case, Body(rowVersion, ("cancelOpenHearings", true)));
        var result = await Json(response);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, result.ToString());
        result.GetProperty("cancelledHearings").GetInt32().ShouldBe(2);
        (await Scalar($"SELECT COUNT(*) FROM case_hearings WHERE case_id = {s.Id} AND status = 'CANCELLED' AND description = 'Anuluar në mbyllje të çështjes'"))
            .ShouldBe(2L);
        (await Scalar($"SELECT next_hearing_at FROM cases WHERE id = {s.Id}")).ShouldBe(DBNull.Value);
        (await Scalar($"SELECT COUNT(*) FROM case_history_events WHERE case_id = {s.Id} AND event_type = 'HEARING_COMPLETED'")).ShouldBe(2L);
    }

    [Fact]
    public async Task Money_and_m2_round_trip_exactly_as_strings()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);
        await CloseAsync(client, s.Case,
            ("currency", "EUR"),
            ("wonAmount", "9999999999999999.99"),
            ("wonAreaM2", "999999999999.99"),
            ("lostAmount", "0"),
            ("preventedDamageAmount", "1250000.5"),
            ("causedDamageAreaM2", "0.01"),
            ("courtCostsAmount", "12.30"));

        var decision = (await CaseOf(client, s.Case)).GetProperty("decision");
        decision.GetProperty("currency").GetString().ShouldBe("EUR");
        decision.GetProperty("wonAmount").GetString().ShouldBe("9999999999999999.99");
        decision.GetProperty("wonAreaM2").GetString().ShouldBe("999999999999.99");
        decision.GetProperty("lostAmount").GetString().ShouldBe("0.00");
        decision.GetProperty("preventedDamageAmount").GetString().ShouldBe("1250000.50");
        decision.GetProperty("causedDamageAreaM2").GetString().ShouldBe("0.01");
        decision.GetProperty("courtCostsAmount").GetString().ShouldBe("12.30");
        decision.GetProperty("lostAreaM2").ValueKind.ShouldBe(JsonValueKind.Null);
        (await Scalar($"SELECT CAST(won_amount AS CHAR) FROM case_decisions WHERE case_id = {s.Id}")).ShouldBe("9999999999999999.99");
    }

    [Fact]
    public async Task The_case_page_shows_the_appeal_kind_and_the_whole_decision()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);
        (await CaseOf(client, s.Case)).GetProperty("data").GetProperty("appealKind").GetString().ShouldBe("ANKIM");

        await CloseAsync(client, s.Case, ("isAppealed", true), ("dispositive", "Rrëzimi i padisë."), ("resultCode", "TJETER"),
            ("resultOtherText", "Pajtim"));

        var decision = (await CaseOf(client, s.Case)).GetProperty("decision");
        decision.GetProperty("result").GetProperty("code").GetString().ShouldBe("TJETER");
        decision.GetProperty("resultOtherText").GetString().ShouldBe("Pajtim");
        decision.GetProperty("dispositive").GetString().ShouldBe("Rrëzimi i padisë.");
        decision.GetProperty("isAppealed").GetBoolean().ShouldBeTrue();
        decision.GetProperty("decisionNumber").GetString().ShouldBe("V-123");
        decision.GetProperty("rowVersion").GetUInt32().ShouldBe(0u);
    }

    [Fact]
    public async Task A_retrial_result_suggests_continuing_as_retrial_and_a_final_one_archiving()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);

        var response = await Record(client, s.Case, Body(await CaseRowVersion(client, s.Case), ("resultCode", "KTHIM_RIGJYKIM"), ("isFinal", true)));

        var result = await Json(response);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, result.ToString());
        result.GetProperty("suggestions").EnumerateArray().Select(x => x.GetString()).ShouldBe(["RETRIAL_EXPECTED", "ARCHIVE_SUGGESTED"]);
        result.GetProperty("decision").GetProperty("sendsBackForRetrial").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Access_secondary_lawyer_403_registered_case_409_outsider_404_stale_case_row_version_409()
    {
        await using var s = await SetupAsync();
        using var primary = s.As(Primary);
        var rowVersion = await CaseRowVersion(primary, s.Case);

        using (var secondary = s.As("avokat2@ash.local"))
        {
            await Problem(await Record(secondary, s.Case, Body(rowVersion)), HttpStatusCode.Forbidden);
        }

        var registered = PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM cases WHERE id = {s.Seed.RegisteredCaseId}"))!);
        using (var kryetar = s.As("kryetar@ash.local"))
        {
            (await Problem(await Record(kryetar, registered, Body(0)), HttpStatusCode.Conflict)).ShouldContain("case.not_active");
        }

        var outsider = (string)(await Scalar($"SELECT email FROM users WHERE id = {s.Seed.User("drlawyer").Id}"))!;
        using (var other = s.As(outsider))
        {
            await Problem(await Record(other, s.Case, Body(rowVersion)), HttpStatusCode.NotFound);
        }

        (await Problem(await Record(primary, s.Case, Body(rowVersion + 7)), HttpStatusCode.Conflict)).ShouldContain("case.changed");
        (await Scalar($"SELECT lifecycle_status FROM cases WHERE id = {s.Id}")).ShouldBe("ACTIVE");
    }

    [Fact]
    public async Task A_delegated_institution_records_the_decision()
    {
        await using var s = await SetupAsync();
        await Execute($"UPDATE case_institutions SET link_mode = 'DELEGATED' WHERE case_id = {s.Id}");
        using var jurist = s.As("jurist@ash.local");
        (await CaseOf(jurist, s.Case)).GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .ShouldContain("decision.record");

        await CloseAsync(jurist, s.Case);

        (await Scalar($"SELECT closed_by FROM cases WHERE id = {s.Id}")).ShouldBe((uint)s.Seed.User("jurist").Id);
    }

    // ---------------------------------------------------------------- 037: complete later

    [Fact]
    public async Task Received_later_with_the_pdf_sets_received_and_its_time_and_records_only_the_changed_fields()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);
        var decisionVersion = await CloseAsync(client, s.Case);
        var document = await DocumentAsync(s.Id);

        var response = await Update(client, s.Case, Body(decisionVersion, ("decisionReceived", true), ("decisionDocumentPublicId", document)));

        var result = await Json(response);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, result.ToString());
        result.GetProperty("decision").GetProperty("rowVersion").GetUInt32().ShouldBe(decisionVersion + 1);
        (await Scalar($"SELECT decision_received = 1 AND decision_received_at IS NOT NULL AND decision_document_id IS NOT NULL FROM case_decisions WHERE case_id = {s.Id}"))
            .ShouldBe(1L);
        (await Scalar($"SELECT COUNT(*) FROM case_decisions FORCE INDEX (ix_cd_pending) WHERE decision_received = 0 AND case_id = {s.Id}")).ShouldBe(0L);
        (await Scalar($"SELECT lifecycle_status FROM cases WHERE id = {s.Id}")).ShouldBe("CLOSED");
        var details = (string)(await Scalar($"SELECT details FROM case_history_events WHERE case_id = {s.Id} AND event_type = 'DECISION_UPDATED'"))!;
        var keys = JsonDocument.Parse(details).RootElement.GetProperty("new").EnumerateObject().Select(p => p.Name).ToList();
        keys.ShouldBe(["decisionReceived", "decisionDocument"], ignoreOrder: true);

        // the same again: nothing to change
        (await Problem(await Update(client, s.Case, Body(decisionVersion + 1, ("decisionReceived", true), ("decisionDocumentPublicId", document))),
            HttpStatusCode.BadRequest)).ShouldContain("case.no_change");
        // received cannot be undone
        (await Problem(await Update(client, s.Case, Body(decisionVersion + 1)), HttpStatusCode.BadRequest)).ShouldContain("\"decisionReceived\"");
    }

    [Fact]
    public async Task Final_later_is_allowed_with_an_archive_suggestion_but_not_once_a_newer_level_exists()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);
        var decisionVersion = await CloseAsync(client, s.Case, ("isAppealed", true));

        // the dossier continued at X/2: the appeal stands
        var code = (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {s.Id}"))!;
        var code2 = code[..^2] + "/2";
        await Execute(
            "INSERT INTO cases (dossier_id, level_seq, case_code, previous_case_id, office_id, summons_number, act_number, received_date, " +
            "court_level_id, court_id, object_text, lifecycle_status, registered_by) " +
            $"SELECT dossier_id, 2, '{code2}', id, office_id, 'FL-2', 'AKT-2', CURDATE(), court_level_id, court_id, object_text, 'REGISTERED', registered_by " +
            $"FROM cases WHERE id = {s.Id}");
        try
        {
            (await Problem(await Update(client, s.Case, Body(decisionVersion, ("isFinal", true))), HttpStatusCode.Conflict))
                .ShouldContain($"Kjo çështje ka vazhduar në {code2}");
            (await Problem(await Update(client, s.Case, Body(decisionVersion, ("isAppealed", false))), HttpStatusCode.Conflict))
                .ShouldContain($"Kjo çështje ka vazhduar në {code2}");
        }
        finally
        {
            await Execute($"DELETE FROM cases WHERE case_code = '{code2}'");
        }

        // without the newer level: the appeal deadline passed — final, and the dossier may be archived
        var response = await Update(client, s.Case, Body(decisionVersion, ("isFinal", true)));
        var result = await Json(response);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, result.ToString());
        result.GetProperty("suggestions").EnumerateArray().Select(x => x.GetString()).ShouldBe(["ARCHIVE_SUGGESTED"]);
        (await Scalar($"SELECT CONCAT(is_final, is_appealed) FROM case_decisions WHERE case_id = {s.Id}")).ShouldBe("10");
    }

    [Fact]
    public async Task Update_needs_a_closed_case_the_permission_and_the_current_decision_row_version()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);
        (await Problem(await Update(client, s.Case, Body(0)), HttpStatusCode.Conflict)).ShouldContain("decision.not_recorded");

        var decisionVersion = await CloseAsync(client, s.Case);
        using (var secondary = s.As("avokat2@ash.local"))
        {
            await Problem(await Update(secondary, s.Case, Body(decisionVersion, ("decisionNumber", "V-9"))), HttpStatusCode.Forbidden);
        }

        (await Problem(await Update(client, s.Case, Body(decisionVersion + 3, ("decisionNumber", "V-9"))), HttpStatusCode.Conflict))
            .ShouldContain("case.changed");
        var ok = await Update(client, s.Case, Body(decisionVersion, ("decisionNumber", "V-9"), ("isAppealed", true)));
        ok.StatusCode.ShouldBe(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync(Ct));
        // the case row is not touched by PUT
        (await Scalar($"SELECT CONCAT(decision_number, is_appealed) FROM case_decisions WHERE case_id = {s.Id}")).ShouldBe("V-91");
    }
}
