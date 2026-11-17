using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.SharedKernel.Ids;
using Ash.SharedKernel.Time;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 053j — the Protokoll is a central role: "Vazhdo çështje ekzistuese" is a table of every dossier of the country
/// (default "Mund të vazhdohen"), with the 053b filters (shkalla → gjykata → gjyqtari, kategoria → lloji) and "+ Shto
/// filtër" from the report builder's registry; a registration goes to the office that covers the summons court and
/// to ITS Kryetar; the handoff (053c) is gone.
/// </summary>
public class ProtokollCentralTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Protokoll = "protokoll@ash.local";
    private const string Search = "/api/cases/registration-search";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- helpers

    private async Task<object?> Scalar(string sql, string? connectionString = null)
    {
        await using var connection = new MySqlConnection(connectionString ?? Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private async Task<Guid> PublicIdOf(string table, long id) =>
        PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM {table} WHERE id = {id}"))!);

    /// <summary>Closes a seeded case with a decision (final or not), <paramref name="minutesAgo"/> before now.</summary>
    private async Task CloseAsync(long caseId, bool final, int minutesAgo)
    {
        await Scalar($"""
            UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = UTC_TIMESTAMP(3) - INTERVAL {minutesAgo} MINUTE,
                   closed_by = (SELECT id FROM users WHERE email = 'kryetar@ash.local')
             WHERE id = {caseId};
            INSERT INTO case_decisions (case_id, result_id, is_final, decision_date, recorded_by)
            SELECT {caseId}, (SELECT MIN(id) FROM decision_results), {(final ? 1 : 0)}, CURDATE(), id
              FROM users WHERE email = 'kryetar@ash.local';
            """);
    }

    private async Task AddPartyAsync(long caseId, string roleCode, string name, string kind = "NATURAL") =>
        await Scalar($"""
            INSERT INTO case_parties (case_id, party_role_id, party_kind, display_name, created_by)
            SELECT {caseId}, r.id, '{kind}', '{name}', (SELECT id FROM users WHERE email = 'protokoll@ash.local')
              FROM party_roles r WHERE r.code = '{roleCode}'
            """);

    private static async Task<List<JsonElement>> Rows(HttpClient client, string query)
    {
        var response = await client.GetAsync($"{Search}{query}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await Json(response)).GetProperty("items").EnumerateArray().ToList();
    }

    private static List<string> Codes(IEnumerable<JsonElement> rows) => rows.Select(r => r.GetProperty("caseCode").GetString()!).ToList();

    /// <summary>The DR office gets an active Kryetar (CaseSeed's DR has none); returns the head's e-mail.</summary>
    private async Task<string> GiveDrAHeadAsync(CaseSeed seed)
    {
        var email = await seed.AddOfficeUserAsync("DrHead", seed.OfficeDrId, "OFFICE_HEAD");
        await Scalar($"UPDATE offices SET head_user_id = (SELECT id FROM users WHERE email = '{email}') WHERE id = {seed.OfficeDrId}");
        return email;
    }

    private async Task<Dictionary<string, object?>> RegistrationBody(int courtId, string level = "FIRST_INSTANCE")
    {
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var today = TiraneTime.Today(new SystemClock());
        return new Dictionary<string, object?>
        {
            ["summonsNumber"] = $"FL-J-{Guid.NewGuid():N}"[..18],
            ["actNumber"] = $"AKT-J-{Guid.NewGuid():N}"[..18],
            ["actDate"] = today.AddDays(-5).ToString("yyyy-MM-dd"),
            ["receivedDate"] = today.ToString("yyyy-MM-dd"),
            ["courtLevelCode"] = level,
            ["courtId"] = courtId,
            ["judgeId"] = await seed.JudgeAsync("Ilir", "Gjyqtari"),
            ["objectText"] = "Kërkesë për shpërblim dëmi",
            ["firstHearing"] = new { at = DateTime.UtcNow.AddDays(14).ToString("yyyy-MM-ddTHH:mm:ssZ") },
            ["parties"] = new object[] { new { partyRoleCode = "PADITES", kind = "NATURAL", displayName = "Agim Hoxha" } },
        };
    }

    private async Task<long> AuditCountAsync(string action, string contains)
    {
        var sql = $"SELECT COUNT(*) FROM ash_audit.audit_log WHERE action = '{action}' AND details LIKE '%{contains}%'";
        var deadline = DateTime.UtcNow.AddSeconds(15);
        long count;
        while ((count = Convert.ToInt64(await Scalar(sql, Db.AuditConnectionString))) == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, Ct);
        }

        return count;
    }

    /// <summary>Closed cases of both offices, a final one and an archived one (the TR seed cases stay open).</summary>
    private sealed record Lookup(CaseSeed Seed, (long Id, string Code) Dr, (long Id, string Code) Tr, (long Id, string Code) Final,
        (long Id, string Code) Archived);

    private async Task<Lookup> LookupDataAsync()
    {
        var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var dr = await seed.AddOfficeCaseAsync(seed.OfficeDrId, "dr");
        var tr = await seed.AddOfficeCaseAsync(seed.OfficeTrId, "tr");
        var final = await seed.AddOfficeCaseAsync(seed.OfficeTrId, "fin");
        var archived = await seed.AddOfficeCaseAsync(seed.OfficeDrId, "arc");
        await CloseAsync(dr.Id, final: false, minutesAgo: 1);
        await CloseAsync(tr.Id, final: false, minutesAgo: 2);
        await CloseAsync(final.Id, final: true, minutesAgo: 3);
        await CloseAsync(archived.Id, final: false, minutesAgo: 4);
        await Scalar($"""
            UPDATE dossiers SET archived_at = UTC_TIMESTAMP(3), archived_by = (SELECT id FROM users WHERE email = 'kryetar@ash.local')
             WHERE id = (SELECT dossier_id FROM cases WHERE id = {archived.Id})
            """);
        await Scalar($"UPDATE cases SET object_text = 'Shpërblim dëmi nga pronat', act_number = 'AKT-DR-4471' WHERE id = {dr.Id}");
        await AddPartyAsync(dr.Id, "PADITES", "Agim Lookupi");
        await AddPartyAsync(dr.Id, "I_PADITUR", "Bashkia Durrës");
        return new Lookup(seed, dr, tr, final, archived);
    }

    // ---------------------------------------------------------------- the table

    [Fact]
    public async Task The_Protokoll_sees_every_office_and_the_default_is_the_cases_that_can_continue()
    {
        var data = await LookupDataAsync();
        await using var _ = data.Seed;
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);

        // default "Mund të vazhdohen": closed, not final, dossier not archived — of BOTH offices, newest closing first
        var page = await Json(await client.GetAsync(Search, Ct));
        Codes(page.GetProperty("items").EnumerateArray()).ShouldBe([data.Dr.Code, data.Tr.Code]);
        page.GetProperty("total").GetInt32().ShouldBe(2);
        var dr = page.GetProperty("items")[0];
        dr.GetProperty("officeName").GetString()!.ShouldStartWith("Zyra Durrës test");
        dr.GetProperty("plaintiffs").EnumerateArray().Select(p => p.GetString()).ShouldBe(["Agim Lookupi"]);
        dr.GetProperty("defendants").EnumerateArray().Select(p => p.GetString()).ShouldBe(["Bashkia Durrës"]);
        dr.GetProperty("canContinue").GetBoolean().ShouldBeTrue();
        dr.GetProperty("courtLevelName").GetString().ShouldNotBeNullOrEmpty();

        // every status: the open, the final and the archived ones too; those that cannot continue say why
        var all = await Rows(client, "?status=ALL&limit=50");
        Codes(all).ShouldContain(data.Final.Code);
        var archived = all.Single(r => r.GetProperty("caseCode").GetString() == data.Archived.Code);
        archived.GetProperty("canContinue").GetBoolean().ShouldBeFalse();
        archived.GetProperty("reason").GetString().ShouldBe("Dosja është arkivuar");
        var open = all.First(r => r.GetProperty("status").GetString() == "ACTIVE");
        open.GetProperty("canContinue").GetBoolean().ShouldBeFalse();
        open.GetProperty("reason").GetString().ShouldBe("E pambyllur në sistem");
        all.Single(r => r.GetProperty("caseCode").GetString() == data.Final.Code).GetProperty("isFinal").GetBoolean().ShouldBeTrue();

        // keyset: one row per page, Pas › twice, the same total
        var first = await Json(await client.GetAsync($"{Search}?limit=1", Ct));
        var next = first.GetProperty("nextCursor").GetString()!;
        var second = await Json(await client.GetAsync($"{Search}?limit=1&after={Uri.EscapeDataString(next)}", Ct));
        Codes(second.GetProperty("items").EnumerateArray()).ShouldBe([data.Tr.Code]);
        second.GetProperty("total").GetInt32().ShouldBe(2);
        second.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Each_filter_narrows_the_table_including_the_dependent_ones_and_the_added_ones()
    {
        var data = await LookupDataAsync();
        await using var _ = data.Seed;
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var tirana = Convert.ToInt32(await Scalar($"SELECT id FROM courts WHERE name = '{OfficeCourtsTestData.FirstInstanceTirana}'"));
        var drPublic = await PublicIdOf("offices", data.Seed.OfficeDrId);

        // shkalla → gjykata
        Codes(await Rows(client, $"?courtLevel=FIRST_INSTANCE&courtId={tirana}")).ShouldBe([data.Dr.Code, data.Tr.Code]);
        (await Rows(client, "?courtLevel=APPEAL")).ShouldBeEmpty();
        // code and act number: a substring
        Codes(await Rows(client, $"?code={Uri.EscapeDataString(data.Dr.Code[2..9])}")).ShouldBe([data.Dr.Code]);
        Codes(await Rows(client, "?actNumber=DR-447")).ShouldBe([data.Dr.Code]);
        // parties and object (accent-insensitive)
        Codes(await Rows(client, "?plaintiff=lookupi")).ShouldBe([data.Dr.Code]);
        Codes(await Rows(client, "?defendant=bashkia")).ShouldBe([data.Dr.Code]);
        Codes(await Rows(client, "?object=shperblim")).ShouldBe([data.Dr.Code]);

        // "+ Shto filtër": the report builder's filters on the shown level
        string More(string json) => $"more={Uri.EscapeDataString(json)}";
        Codes(await Rows(client, $"?{More($$"""{"office":"{{drPublic}}"}""")}")).ShouldBe([data.Dr.Code]);
        Codes(await Rows(client, $"?status=ALL&{More("""{"isFinal":true}""")}")).ShouldBe([data.Final.Code]);
        Codes(await Rows(client, $"?{More("""{"registeredAt":{"from":"2000-01-01"}}""")}")).ShouldBe([data.Dr.Code, data.Tr.Code]);
        (await Rows(client, $"?{More("""{"registeredAt":{"to":"2000-01-01"}}""")}")).ShouldBeEmpty();
        (await client.GetAsync($"{Search}?{More("""{"nope":1}""")}", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // the metadata: every remaining column, never one the table already filters on; options of the country
        var moreFilters = await Json(await client.GetAsync($"{Search}/more-filters", Ct));
        var keys = moreFilters.EnumerateArray().Select(f => f.GetProperty("key").GetString()!).ToList();
        keys.ShouldContain("office");
        keys.ShouldContain("lawyer");
        keys.ShouldContain("registeredAt");
        keys.ShouldContain("decisionDate");
        keys.ShouldContain("isFinal");
        keys.ShouldContain("isAppealed");
        foreach (var own in new[] { "court", "courtLevel", "judge", "caseCode", "actNumber", "plaintiffs", "category", "result" })
        {
            keys.ShouldNotContain(own);
        }

        moreFilters.EnumerateArray().Single(f => f.GetProperty("key").GetString() == "office")
            .GetProperty("options").EnumerateArray().Select(o => o.GetProperty("value").GetString())
            .ShouldContain(drPublic.ToString());

        // the 053b form: shkalla before gjykata, gjykata depends on it
        var filters = await Json(await client.GetAsync($"{Search}/filters", Ct));
        filters.EnumerateArray().Single(f => f.GetProperty("name").GetString() == "courtId").GetProperty("dependsOn").GetString()
            .ShouldBe("courtLevel");

        // audited with the filters used
        (await AuditCountAsync("REGISTRATION_LOOKUP", "DR-447")).ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// The whole-country query, EXPLAIN-checked as in task 019: the default page walks ix_cases_status_closed (no full
    /// scan of cases), "is the latest level" is answered by uq_cases_dossier_level, the decision by its primary key.
    /// </summary>
    [Fact]
    public async Task Explain_the_default_page_uses_the_indexes()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        // 400 dossiers: a third closed (half of them final), the rest open
        await Scalar($"""
            INSERT INTO dossiers (unique_code, code_format_id, office_id, opened_by)
            WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 400)
            SELECT CONCAT('X-', LPAD(i, 6, '0')), (SELECT id FROM dossier_code_formats WHERE is_active = 1), {seed.OfficeTrId},
                   (SELECT id FROM users WHERE email = 'protokoll@ash.local')
              FROM n;
            INSERT INTO cases (dossier_id, level_seq, case_code, office_id, summons_number, act_number, received_date,
                               court_level_id, court_id, object_text, lifecycle_status, registered_by, closed_at, closed_by)
            SELECT d.id, 1, CONCAT(d.unique_code, '/1'), d.office_id, 'FL-X', CONCAT('AKT-X-', d.id), CURDATE(),
                   (SELECT id FROM court_levels WHERE code = 'FIRST_INSTANCE'),
                   (SELECT id FROM courts WHERE name = '{OfficeCourtsTestData.FirstInstanceTirana}'), 'Test',
                   IF(d.id % 3 = 0, 'CLOSED', 'ACTIVE'), d.opened_by,
                   IF(d.id % 3 = 0, UTC_TIMESTAMP(3) - INTERVAL d.id MINUTE, NULL), IF(d.id % 3 = 0, d.opened_by, NULL)
              FROM dossiers d WHERE d.unique_code LIKE 'X-%';
            INSERT INTO case_decisions (case_id, result_id, is_final, recorded_by)
            SELECT c.id, (SELECT MIN(id) FROM decision_results), c.id % 2, c.registered_by
              FROM cases c WHERE c.case_code LIKE 'X-%' AND c.lifecycle_status = 'CLOSED';
            ANALYZE TABLE cases, case_decisions, dossiers;
            """);

        var sql = Ash.Modules.Cases.Application.RegistrationSearch.RegistrationSearchSql.Page(
            Ash.Modules.Cases.Application.RegistrationSearch.RegistrationSearchSql.Matching([], "CONTINUABLE", string.Empty), "CONTINUABLE");
        var plan = new List<(string Table, string Type, string? Key)>();
        await using (var connection = new MySqlConnection(Db.AppConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var set = new MySqlCommand("SET @after_key = NULL, @after_id = NULL, @take = 11;", connection);
            await set.ExecuteNonQueryAsync(Ct);
            await using var explain = new MySqlCommand("EXPLAIN " + sql, connection);
            await using var reader = await explain.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                plan.Add((Convert.ToString(reader["table"])!, Convert.ToString(reader["type"])!,
                    reader["key"] is DBNull ? null : Convert.ToString(reader["key"])));
            }
        }

        var text = sql + "\n" + string.Join('\n', plan.Select(r => $"{r.Table,-6} type={r.Type,-8} key={r.Key ?? "NULL"}"));
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        plan.Single(r => r.Table == "c").Type.ShouldNotBe("ALL", text);
        plan.Single(r => r.Table == "c").Key.ShouldBe("ix_cases_status_closed", text);
        plan.Single(r => r.Table == "nx").Key.ShouldBe("uq_cases_dossier_level", text);
        plan.Single(r => r.Table == "d").Key.ShouldBe("PRIMARY", text);
    }

    [Fact]
    public async Task The_Protokoll_gets_404_on_the_full_page_of_another_offices_case_but_its_preview_works()
    {
        var data = await LookupDataAsync();
        await using var _ = data.Seed;
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var dr = await PublicIdOf("cases", data.Dr.Id);

        (await client.GetAsync($"/api/cases/{dr}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/cases/{dr}/documents", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/cases/{dr}/next-level/preview", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_handoff_endpoints_are_gone()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var id = await PublicIdOf("cases", seed.ActiveCaseId);

        (await client.GetAsync("/api/cases/handoffs/incoming", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync($"/api/cases/{id}/handoff", new { nextCourtId = 1 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Scalar("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'ash_core' AND table_name = 'case_handoffs'"))
            .ShouldBe(0L);
    }

    // ---------------------------------------------------------------- the office of the court

    [Fact]
    public async Task A_registration_goes_to_the_office_of_its_court_and_reaches_that_Kryetar()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var head = await GiveDrAHeadAsync(seed);
        var court = await OfficeCourtsTestData.MapAsync(Db.AppConnectionString, OfficeCourtsTestData.FirstInstanceTirana,
            (string)(await Scalar($"SELECT code FROM offices WHERE id = {seed.OfficeDrId}"))!);
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);

        // the form is pre-filled with the court's office
        var offices = await Json(await client.GetAsync($"/api/cases/registration/offices?courtId={court}", Ct));
        offices.GetProperty("courtOfficePublicId").GetGuid().ShouldBe(await PublicIdOf("offices", seed.OfficeDrId));
        offices.GetProperty("offices").GetArrayLength().ShouldBeGreaterThan(1);

        var response = await client.PostAsJsonAsync("/api/cases/register", await RegistrationBody(court), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var code = (await Json(response)).GetProperty("caseCode").GetString()!;
        var caseId = Convert.ToInt64(await Scalar($"SELECT id FROM cases WHERE case_code = '{code}'"));

        (await Scalar($"SELECT office_id FROM cases WHERE id = {caseId}")).ShouldBe((ushort)seed.OfficeDrId);
        (await Scalar($"""
            SELECT COUNT(*) FROM notifications n JOIN users u ON u.id = n.user_id
             WHERE u.email = '{head}' AND n.notification_type = 'CASE_REGISTERED'
            """)).ShouldBe(1L);
        (await Scalar("SELECT COUNT(*) FROM notifications n JOIN users u ON u.id = n.user_id WHERE u.email = 'kryetar@ash.local'"))
            .ShouldBe(0L);
        ((string)(await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.office')) FROM case_history_events WHERE case_id = {caseId} AND event_type = 'SENT_TO_HEAD'"))!)
            .ShouldStartWith("Zyra Durrës test");

        // "Të regjistruarat nga unë" works by registered_by, whatever the office
        var registered = await Json(await client.GetAsync("/api/cases?tab=registered", Ct));
        registered.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("caseCode").GetString()).ShouldContain(code);
    }

    [Fact]
    public async Task Choosing_another_office_is_audited_and_a_court_without_an_office_requires_one()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await GiveDrAHeadAsync(seed);
        await using var s = await Seed.CreateAsync(Db.AppConnectionString);
        var court = Convert.ToInt32(await s.CourtAsync(OfficeCourtsTestData.FirstInstanceTirana));
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);

        // no office covers the court: the office is required
        var unmapped = await client.PostAsJsonAsync("/api/cases/register", await RegistrationBody(court), Ct);
        unmapped.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(unmapped)).GetProperty("errors").GetProperty("officePublicId")[0].GetString()
            .ShouldBe("Kjo gjykatë nuk i është caktuar asnjë zyre: zgjidhni zyrën që e ndjek.");
        (await Json(await client.GetAsync($"/api/cases/registration/offices?courtId={court}", Ct)))
            .GetProperty("courtOfficePublicId").ValueKind.ShouldBe(JsonValueKind.Null);

        // the court is TR's, the Protokoll sends it to DR: it goes to DR, and the override is audited
        await OfficeCourtsTestData.MapAsync(Db.AppConnectionString, OfficeCourtsTestData.FirstInstanceTirana);
        var body = await RegistrationBody(court);
        body["officePublicId"] = await PublicIdOf("offices", seed.OfficeDrId);
        var response = await client.PostAsJsonAsync("/api/cases/register", body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var code = (await Json(response)).GetProperty("caseCode").GetString()!;
        (await Scalar($"SELECT office_id FROM cases WHERE case_code = '{code}'")).ShouldBe((ushort)seed.OfficeDrId);
        var caseId = Convert.ToInt64(await Scalar($"SELECT id FROM cases WHERE case_code = '{code}'"));
        (await AuditCountAsync("CASE_OFFICE_OVERRIDE", "Zyra Durrës test")).ShouldBe(1);
        (await Scalar($"SELECT COUNT(*) FROM ash_audit.audit_log WHERE action = 'CASE_OFFICE_OVERRIDE' AND case_id = {caseId}",
            Db.AuditConnectionString)).ShouldBe(1L);

        // an office without a Kryetar: the existing refusal
        await Scalar($"UPDATE offices SET head_user_id = NULL WHERE id = {seed.OfficeDrId}");
        var noHead = await client.PostAsJsonAsync("/api/cases/register", body.With("actNumber", $"AKT-N-{Guid.NewGuid():N}"[..18]), Ct);
        noHead.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(noHead)).GetProperty("code").GetString().ShouldBe("case.office_without_head");
    }

    [Fact]
    public async Task Continuing_copies_object_plaintiff_and_defendant_only_adds_the_new_parties_and_goes_to_the_courts_office()
    {
        var data = await LookupDataAsync();
        await using var _ = data.Seed;
        var head = await GiveDrAHeadAsync(data.Seed);
        await AddPartyAsync(data.Tr.Id, "PADITES", "Agim Hoxha");
        await AddPartyAsync(data.Tr.Id, "I_PADITUR", "Bashkia Tiranë");
        await AddPartyAsync(data.Tr.Id, "PERSON_I_TRETE", "Person i Tretë");
        var appeal = await OfficeCourtsTestData.MapAsync(Db.AppConnectionString, OfficeCourtsTestData.AppealTirana,
            (string)(await Scalar($"SELECT code FROM offices WHERE id = {data.Seed.OfficeDrId}"))!);
        await using var s = await Seed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var tr = await PublicIdOf("cases", data.Tr.Id);

        // the preview shows only what is copied
        var preview = await Json(await client.GetAsync($"/api/cases/{tr}/next-level/preview", Ct));
        preview.GetProperty("parties").EnumerateArray().Select(p => p.GetProperty("displayName").GetString())
            .ShouldBe(["Agim Hoxha", "Bashkia Tiranë"]);

        var response = await client.PostAsJsonAsync($"/api/cases/{tr}/next-level", new Dictionary<string, object?>
        {
            ["summonsNumber"] = $"FL-N-{Guid.NewGuid():N}"[..18],
            ["actNumber"] = $"AKT-N-{Guid.NewGuid():N}"[..18],
            ["receivedDate"] = TiraneTime.Today(new SystemClock()).ToString("yyyy-MM-dd"),
            ["courtLevelCode"] = "APPEAL",
            ["courtId"] = appeal,
            ["judgeId"] = await s.JudgeAsync("Genc", "Muçaj"),
            ["firstHearing"] = new { at = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-ddTHH:mm:ssZ") },
            ["parties"] = new object[] { new { partyRoleCode = "PERSON_I_TRETE", kind = "NATURAL", displayName = "Ndërhyrës i Ri" } },
        }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var code = (await Json(response)).GetProperty("caseCode").GetString()!;
        var nextId = Convert.ToInt64(await Scalar($"SELECT id FROM cases WHERE case_code = '{code}'"));

        (await Scalar($"SELECT GROUP_CONCAT(display_name ORDER BY id) FROM case_parties WHERE case_id = {nextId} AND deleted_at IS NULL"))
            .ShouldBe("Agim Hoxha,Bashkia Tiranë,Ndërhyrës i Ri");
        (await Scalar($"SELECT object_text FROM cases WHERE id = {nextId}")).ShouldBe("Test");
        (await Scalar($"SELECT office_id FROM cases WHERE id = {nextId}")).ShouldBe((ushort)data.Seed.OfficeDrId);
        (await Scalar($"""
            SELECT COUNT(*) FROM notifications n JOIN users u ON u.id = n.user_id
             WHERE u.email = '{head}' AND n.notification_type = 'CASE_REGISTERED'
            """)).ShouldBe(1L);
    }

    [Fact]
    public async Task A_party_role_is_copied_to_the_next_level_as_the_administrator_sets_it()
    {
        await using var factory = new AshApiFactory(Db);
        using var admin = factory.CreateClient().AsUser("admin@ash.local");

        var roles = await Json(await admin.GetAsync("/api/reference/admin/party-roles", Ct));
        roles.EnumerateArray().ToDictionary(r => r.GetProperty("code").GetString()!, r => r.GetProperty("copyToNextLevel").GetBoolean())
            .ShouldContainKeyAndValue("PADITES", true);
        roles.EnumerateArray().Single(r => r.GetProperty("code").GetString() == "PERSON_I_TRETE")
            .GetProperty("copyToNextLevel").GetBoolean().ShouldBeFalse();

        try
        {
            var updated = await admin.PutAsJsonAsync("/api/reference/admin/party-roles/PERSON_I_TRETE",
                new { name = "Person i tretë", sortOrder = 3, copyToNextLevel = true }, Ct);
            updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync(Ct));
            (await Json(updated)).GetProperty("copyToNextLevel").GetBoolean().ShouldBeTrue();
            Convert.ToInt32(await Scalar("SELECT copy_to_next_level FROM party_roles WHERE code = 'PERSON_I_TRETE'")).ShouldBe(1);
        }
        finally
        {
            await Scalar("UPDATE party_roles SET copy_to_next_level = 0 WHERE code = 'PERSON_I_TRETE'");
        }
    }
}

internal static class DictionaryExtensions
{
    public static Dictionary<string, object?> With(this Dictionary<string, object?> body, string key, object? value) =>
        new(body) { [key] = value };
}
