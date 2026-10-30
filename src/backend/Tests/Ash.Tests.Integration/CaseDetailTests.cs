using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ash.SharedKernel.Ids;
using Ash.SharedKernel.Time;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 020 — the case page and the dossier history. Scenario (SQL): dossier 2026-777001 with
/// /1 CLOSED (primary avokat2, a decision) and /2 ACTIVE (primary avokat1 after a transfer from "Ilir Mëparshmi",
/// secondary avokat2, assistant ndihmes, ministry MONITORING link with jurist assigned, one party, one hearing,
/// one live + one deleted document); dossier 2026-777002 with a REGISTERED /1 registered by protokoll.
/// </summary>
public class CaseDetailTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Ministry = "Ministria e Financave dhe Ekonomisë";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Scenario(
        long Level1Id, Guid Level1, long Level2Id, Guid Level2, long RegisteredId, Guid Registered,
        string PreviousLawyerEmail, Guid PreviousLawyer, Guid Avokat1);

    private async Task<Scenario> SeedAsync()
    {
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var previous = await seed.UserAsync("prev", "Ilir", "Mëparshmi", await Id("SELECT id FROM offices WHERE code = 'TR'"), null, "STATE_LAWYER");
        var court1 = await seed.CourtAsync("Gjykata e Shkallës së Parë Tiranë");
        var court2 = await seed.CourtAsync("Gjykata e Apelit Tiranë");
        var judge1 = await seed.JudgeAsync("Ilir", "Gjyqtari");

        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        async Task<long> Insert(string sql)
        {
            await using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@prev", previous);
            cmd.Parameters.AddWithValue("@court1", court1);
            cmd.Parameters.AddWithValue("@court2", court2);
            cmd.Parameters.AddWithValue("@judge1", judge1);
            await cmd.ExecuteNonQueryAsync(Ct);
            return cmd.LastInsertedId;
        }

        const string vars = """
            SET @tr = (SELECT id FROM offices WHERE code = 'TR');
            SET @prot = (SELECT id FROM users WHERE email = 'protokoll@ash.local');
            SET @kry = (SELECT id FROM users WHERE email = 'kryetar@ash.local');
            SET @av1 = (SELECT id FROM users WHERE email = 'avokat1@ash.local');
            SET @av2 = (SELECT id FROM users WHERE email = 'avokat2@ash.local');
            SET @nd = (SELECT id FROM users WHERE email = 'ndihmes@ash.local');
            SET @jur = (SELECT id FROM users WHERE email = 'jurist@ash.local');
            SET @min = (SELECT id FROM institutions WHERE name = 'Ministria e Financave dhe Ekonomisë');
            SET @fmt = (SELECT id FROM dossier_code_formats WHERE is_active = 1);
            """;
        await Insert(vars);
        var dossier = await Insert("INSERT INTO dossiers (unique_code, code_format_id, office_id, opened_by) VALUES ('2026-777001', @fmt, @tr, @prot)");
        const string columns = "INSERT INTO cases (dossier_id, level_seq, case_code, previous_case_id, office_id, summons_number, act_number, " +
                               "received_date, court_level_id, court_id, judge_id, object_text, lifecycle_status, registered_by, closed_at, closed_by) ";
        var level1 = await Insert(columns +
            $"SELECT {dossier}, 1, '2026-777001/1', NULL, @tr, 'FL-1', 'AKT-777-1', '2026-01-10', id, @court1, @judge1, " +
            "'Shpërblim dëmi', 'CLOSED', @prot, UTC_TIMESTAMP(3), @av2 FROM court_levels WHERE code = 'FIRST_INSTANCE'");
        var level2 = await Insert(columns +
            $"SELECT {dossier}, 2, '2026-777001/2', {level1}, @tr, 'FL-2', 'AKT-777-2', '2026-06-10', id, @court2, NULL, " +
            "'Shpërblim dëmi', 'ACTIVE', @prot, NULL, NULL FROM court_levels WHERE code = 'APPEAL'");
        await Insert($"UPDATE cases SET claim_amount = 2500000, claim_currency = 'ALL', priority = 'HIGH', category_id = " +
                     "(SELECT id FROM case_categories WHERE code = 'NDJEKJE'), subcategory_id = (SELECT id FROM case_subcategories WHERE code = 'KOD_A_AVSH') " +
                     $"WHERE id = {level2}");
        var dossier2 = await Insert("INSERT INTO dossiers (unique_code, code_format_id, office_id, opened_by) VALUES ('2026-777002', @fmt, @tr, @prot)");
        var registered = await Insert(columns +
            $"SELECT {dossier2}, 1, '2026-777002/1', NULL, @tr, 'FL-3', 'AKT-777-3', '2026-10-01', id, @court1, @judge1, " +
            "'Kërkesë', 'REGISTERED', @prot, NULL, NULL FROM court_levels WHERE code = 'FIRST_INSTANCE'");

        await Insert($"""
            INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by, assigned_at, ended_at, ended_by, end_reason) VALUES
              ({level1}, @av2, 'PRIMARY_LAWYER', @kry, '2026-01-11', NULL, NULL, NULL),
              ({level2}, @prev, 'PRIMARY_LAWYER', @kry, '2026-06-11', '2026-07-01', @kry, 'TRANSFERRED');
            INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by, assigned_at) VALUES
              ({level2}, @av1, 'PRIMARY_LAWYER', @kry, '2026-07-01'),
              ({level2}, @av2, 'SECONDARY_LAWYER', @kry, '2026-06-11'),
              ({level2}, @nd, 'LEGAL_ASSISTANT', @av1, '2026-07-02'),
              ({level2}, @jur, 'INSTITUTION_JURIST', @kry, '2026-07-03');
            INSERT INTO case_institutions (case_id, institution_id, link_mode, started_by) VALUES ({level2}, @min, 'MONITORING', @prot);
            INSERT INTO case_parties (case_id, party_role_id, party_kind, display_name, created_by)
              SELECT {level2}, id, 'NATURAL', 'Petrit Hoxha', @prot FROM party_roles WHERE code = 'PADITES';
            INSERT INTO case_hearings (case_id, hearing_type_id, scheduled_at, created_by)
              SELECT {level2}, id, '2026-11-12 08:30:00', @av1 FROM hearing_types WHERE code = 'PERGATITORE';
            INSERT INTO documents (dossier_id, case_id, document_type_id, title, original_file_name, content_type, size_bytes,
                                   sha256, storage_bucket, storage_key, scan_status, uploaded_by, deleted_at, deleted_by)
              SELECT {dossier}, {level2}, id, CONCAT('Dok ', n.n), 'a.pdf', 'application/pdf', 10, UNHEX(SHA2(n.n, 256)), 'b',
                     UUID(), 'CLEAN', @av1, IF(n.n = 2, UTC_TIMESTAMP(3), NULL), IF(n.n = 2, @av1, NULL)
                FROM document_types JOIN (SELECT 1 AS n UNION ALL SELECT 2) n WHERE code = 'PADI_PROVA';
            INSERT INTO case_decisions (case_id, result_id, decision_number, decision_date, recorded_by)
              SELECT {level1}, id, '1450', '2026-05-02', @av2 FROM decision_results WHERE code = 'HUMBUR';
            INSERT INTO case_history_events (dossier_id, case_id, event_type, actor_user_id, occurred_at, summary, details) VALUES
              ({dossier}, NULL, 'DOSSIER_CREATED', @prot, '2026-01-10 08:00:00', 'Dosja 2026-777001 u krijua.',
               JSON_OBJECT('old', NULL, 'new', JSON_OBJECT('uniqueCode', '2026-777001'))),
              -- legacy rows written before task 020: internal ids inside the details
              ({dossier}, {level1}, 'CASE_REGISTERED', @prot, '2026-01-10 08:00:01', 'Çështja 2026-777001/1 u regjistrua.',
               JSON_OBJECT('old', NULL, 'new', JSON_OBJECT('caseCode', '2026-777001/1', 'courtId', 3, 'judgeId', 7, 'possibleDuplicateConfirmed', false))),
              ({dossier}, {level1}, 'SENT_TO_HEAD', @prot, '2026-01-10 08:00:02', 'Çështja iu dërgua Kryetarit.',
               JSON_OBJECT('old', NULL, 'new', JSON_OBJECT('headUserId', 3))),
              ({dossier}, {level2}, 'CASE_CREATED_FROM_PREVIOUS', @prot, '2026-06-10 08:00:00', 'Çështja 2026-777001/2 u krijua nga 2026-777001/1.',
               JSON_OBJECT('old', NULL, 'new', JSON_OBJECT('caseCode', '2026-777001/2', 'courtId', 4,
                 'nested', JSON_OBJECT('id', 9, 'items', JSON_ARRAY(JSON_OBJECT('userId', 1, 'name', 'x')))))),
              ({dossier}, {level2}, 'LAWYER_TRANSFERRED', @kry, '2026-07-01 09:00:00', 'Avokati primar u ndryshua: Ilir Mëparshmi → Avokat1 Dev.',
               JSON_OBJECT('old', JSON_OBJECT('name', 'Ilir Mëparshmi', 'userPublicId', BIN_TO_UUID((SELECT public_id FROM users WHERE id = @prev), 1)),
                           'new', JSON_OBJECT('name', 'Avokat1 Dev', 'userPublicId', BIN_TO_UUID((SELECT public_id FROM users WHERE id = @av1), 1),
                                              'reason', 'Ngarkesë pune')));
            """);

        return new Scenario(
            level1, await PublicIdOf("cases", level1), level2, await PublicIdOf("cases", level2),
            registered, await PublicIdOf("cases", registered),
            $"prev-{seed.Suffix}@ash.test", await PublicIdOf("users", previous),
            await PublicIdOf("users", await Id("SELECT id FROM users WHERE email = 'avokat1@ash.local'")));
    }

    // ---------------------------------------------------------------- permissions

    [Fact]
    public async Task Secondary_lawyer_has_no_decision_record_primary_has_it()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);

        var primary = await Permissions(factory, "avokat1@ash.local", s.Level2);
        primary.ShouldContain("decision.record");
        primary.ShouldContain("case.view");
        var secondary = await Permissions(factory, "avokat2@ash.local", s.Level2);
        secondary.ShouldNotContain("decision.record");
        secondary.ShouldContain("party.add");
        secondary.ShouldContain("case.view");
    }

    [Fact]
    public async Task Kryetar_gets_office_actions_and_Protokoll_registration_actions_while_registered()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);

        var kryetar = await Permissions(factory, "kryetar@ash.local", s.Registered);
        kryetar.ShouldBe(["case.assign_lawyer", "case.set_priority_any", "case.transfer", "case.view"]);
        var protokoll = await Permissions(factory, "protokoll@ash.local", s.Registered);
        protokoll.ShouldBe(["case.edit_registration", "case.view", "party.add", "party.delete", "party.edit"]);

        await Execute($"UPDATE cases SET lifecycle_status = 'ACTIVE' WHERE id = {s.RegisteredId}");
        using var client = factory.CreateClient().AsUser("protokoll@ash.local");
        (await client.GetAsync($"/api/cases/{s.Registered}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Permissions(factory, "kryetar@ash.local", s.Registered)).ShouldContain("case.assign_lawyer");
    }

    [Theory]
    [InlineData("avokat2@ash.local", new[] { "case.view", "chat.case" })] // primary lawyer of /1: may still discuss the appeal
    [InlineData("kryetar@ash.local", new[] { "case.view" })] // office actions are off once closed
    [InlineData("admin@ash.local", new[] { "case.view" })]
    public async Task Closed_case_keeps_only_view_and_chat(string user, string[] expected)
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        (await Permissions(factory, user, s.Level1)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("avokat1@ash.local", "level2")]
    [InlineData("avokat2@ash.local", "level2")]
    [InlineData("ndihmes@ash.local", "level2")]
    [InlineData("jurist@ash.local", "level2")]
    [InlineData("institucion@ash.local", "level2")]
    [InlineData("kryetar@ash.local", "level2")]
    [InlineData("monitor@ash.local", "level2")]
    [InlineData("avokat2@ash.local", "level1")]
    [InlineData("protokoll@ash.local", "registered")]
    [InlineData("kryetar@ash.local", "registered")]
    public async Task Party_endpoint_allows_exactly_what_permissions_list(string user, string target)
    {
        var s = await SeedAsync();
        var caseId = target switch { "level1" => s.Level1, "level2" => s.Level2, _ => s.Registered };
        await using var factory = new AshApiFactory(Db);
        var permissions = await Permissions(factory, user, caseId);

        using var client = factory.CreateClient().AsUser(user);
        var response = await client.PostAsJsonAsync($"/api/cases/{caseId}/parties",
            new { partyRoleCode = "PERSON_I_TRETE", kind = "NATURAL", displayName = "Arben Kola" }, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        if (permissions.Contains("party.add"))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Created, text);
        }
        else
        {
            // 403, or 409 for a CLOSED level — never a success
            response.StatusCode.ShouldBeOneOf([HttpStatusCode.Forbidden, HttpStatusCode.Conflict], text);
        }
    }

    // ---------------------------------------------------------------- levels + history access

    [Fact]
    public async Task Lawyer_of_level_2_only_sees_level_1_as_closed_door()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");

        var page = await Get(client, $"/api/cases/{s.Level2}");
        var levels = page.GetProperty("levels").EnumerateArray().ToList();
        levels.Select(l => l.GetProperty("caseCode").GetString()).ShouldBe(["2026-777001/1", "2026-777001/2"]);
        levels[0].GetProperty("canOpen").GetBoolean().ShouldBeFalse();
        levels[0].GetProperty("publicId").ValueKind.ShouldBe(JsonValueKind.Null);
        levels[0].GetProperty("status").GetString().ShouldBe("CLOSED");
        levels[0].GetProperty("courtLevelName").GetString().ShouldBe("Shkalla e parë");
        levels[1].GetProperty("canOpen").GetBoolean().ShouldBeTrue();
        levels[1].GetProperty("publicId").GetGuid().ShouldBe(s.Level2);

        (await client.GetAsync($"/api/cases/{s.Level1}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/cases/{s.Level1}/history", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var history = await AllHistory(client, s.Level2);
        history.Select(h => h.GetProperty("level").GetString()).Distinct().Order()
            .ShouldBe(["2026-777001", "2026-777001/2"]); // dossier-wide rows + /2, nothing of /1

        // avokat2 works on both levels and sees the history of both
        using var both = factory.CreateClient().AsUser("avokat2@ash.local");
        (await AllHistory(both, s.Level2)).Select(h => h.GetProperty("level").GetString()).Distinct().Order()
            .ShouldBe(["2026-777001", "2026-777001/1", "2026-777001/2"]);
    }

    [Fact]
    public async Task History_shows_the_previous_primary_lawyer_after_a_transfer()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");

        var transfer = (await AllHistory(client, s.Level2)).Single(h => h.GetProperty("eventType").GetString() == "LAWYER_TRANSFERRED");
        transfer.GetProperty("actorName").GetString().ShouldBe("Kryetar Dev");
        transfer.GetProperty("old").GetProperty("name").GetString().ShouldBe("Ilir Mëparshmi");
        transfer.GetProperty("old").GetProperty("userPublicId").GetGuid().ShouldBe(s.PreviousLawyer);
        transfer.GetProperty("new").GetProperty("name").GetString().ShouldBe("Avokat1 Dev");
        transfer.GetProperty("new").GetProperty("userPublicId").GetGuid().ShouldBe(s.Avokat1);
        transfer.GetProperty("new").GetProperty("reason").GetString().ShouldBe("Ngarkesë pune");

        // the team shows only the current primary; the previous one lost access
        var team = (await Get(client, $"/api/cases/{s.Level2}")).GetProperty("team").EnumerateArray().ToList();
        team.Where(t => t.GetProperty("role").GetString() == "PRIMARY_LAWYER").Select(t => t.GetProperty("name").GetString())
            .ShouldBe(["Avokat1 Dev"]);
        using var previous = factory.CreateClient().AsUser(s.PreviousLawyerEmail);
        (await previous.GetAsync($"/api/cases/{s.Level2}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task History_pages_newest_first_and_rejects_a_bad_cursor()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat2@ash.local");

        var all = await AllHistory(client, s.Level2, limit: 2);
        all.Count.ShouldBe(5);
        var times = all.Select(h => h.GetProperty("occurredAt").GetDateTime()).ToList();
        times.ShouldBe(times.OrderByDescending(t => t).ToList());
        all[0].GetProperty("eventType").GetString().ShouldBe("LAWYER_TRANSFERRED");

        var bad = await client.GetAsync($"/api/cases/{s.Level2}/history?after=nonsense", Ct);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await bad.Content.ReadAsStringAsync(Ct)).ShouldContain("\"after\"");
    }

    // ---------------------------------------------------------------- content, ids, audit

    [Fact]
    public async Task Case_page_returns_data_team_institutions_parties_counts_and_decision()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");

        var page = await Get(client, $"/api/cases/{s.Level2}");
        var header = page.GetProperty("header");
        header.GetProperty("publicId").GetGuid().ShouldBe(s.Level2);
        header.GetProperty("caseCode").GetString().ShouldBe("2026-777001/2");
        header.GetProperty("levelSeq").GetInt32().ShouldBe(2);
        header.GetProperty("status").GetString().ShouldBe("ACTIVE");
        header.GetProperty("priority").GetString().ShouldBe("HIGH");
        header.GetProperty("rowVersion").GetInt32().ShouldBe(0);

        var data = page.GetProperty("data");
        data.GetProperty("courtLevel").GetProperty("code").GetString().ShouldBe("APPEAL");
        data.GetProperty("courtName").GetString().ShouldBe("Gjykata e Apelit Tiranë");
        data.GetProperty("judgeName").ValueKind.ShouldBe(JsonValueKind.Null);
        data.GetProperty("category").GetProperty("code").GetString().ShouldBe("NDJEKJE");
        data.GetProperty("subcategory").GetProperty("code").GetString().ShouldBe("KOD_A_AVSH");
        data.GetProperty("claim").GetProperty("amount").GetDecimal().ShouldBe(2500000m);
        data.GetProperty("receivedDate").GetString().ShouldBe("2026-06-10");
        data.GetProperty("registeredBy").GetProperty("name").GetString().ShouldBe("Protokoll Dev");

        page.GetProperty("team").EnumerateArray()
            .Select(t => $"{t.GetProperty("role").GetString()}:{t.GetProperty("name").GetString()}").Order()
            .ShouldBe(["INSTITUTION_JURIST:Jurist Dev", "LEGAL_ASSISTANT:Ndihmës Dev", "PRIMARY_LAWYER:Avokat1 Dev", "SECONDARY_LAWYER:Avokat2 Dev"]);
        var institution = page.GetProperty("institutions").EnumerateArray().Single();
        institution.GetProperty("name").GetString().ShouldBe(Ministry);
        institution.GetProperty("mode").GetString().ShouldBe("MONITORING");
        institution.GetProperty("juristNames").EnumerateArray().Select(j => j.GetString()).ShouldBe(["Jurist Dev"]);
        page.GetProperty("parties").EnumerateArray().Single().GetProperty("displayName").GetString().ShouldBe("Petrit Hoxha");
        page.GetProperty("hearingsCount").GetInt32().ShouldBe(1);
        page.GetProperty("documentsCount").GetInt32().ShouldBe(1); // the deleted one is not counted
        page.GetProperty("decision").ValueKind.ShouldBe(JsonValueKind.Null);

        using var avokat2 = factory.CreateClient().AsUser("avokat2@ash.local");
        var decision = (await Get(avokat2, $"/api/cases/{s.Level1}")).GetProperty("decision");
        decision.GetProperty("result").GetProperty("code").GetString().ShouldBe("HUMBUR");
        decision.GetProperty("decisionNumber").GetString().ShouldBe("1450");
        decision.GetProperty("decisionDate").GetString().ShouldBe("2026-05-02");
    }

    [Fact]
    public async Task No_numeric_internal_id_in_the_case_page_or_the_history()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat2@ash.local");

        foreach (var caseId in new[] { s.Level1, s.Level2 })
        {
            JsonAssert.NoNumericIds(await Get(client, $"/api/cases/{caseId}"));
        }

        var history = await AllHistory(client, s.Level2);
        foreach (var item in history)
        {
            JsonAssert.NoNumericIds(item);
        }

        // stored legacy ids are stripped, the rest of the details stays
        var registered = history.Single(h => h.GetProperty("eventType").GetString() == "CASE_REGISTERED").GetProperty("new");
        registered.GetProperty("caseCode").GetString().ShouldBe("2026-777001/1");
        registered.TryGetProperty("courtId", out _).ShouldBeFalse();
        var nested = history.Single(h => h.GetProperty("eventType").GetString() == "CASE_CREATED_FROM_PREVIOUS")
            .GetProperty("new").GetProperty("nested");
        nested.TryGetProperty("id", out _).ShouldBeFalse();
        nested.GetProperty("items")[0].GetProperty("name").GetString().ShouldBe("x");
    }

    [Fact]
    public async Task Registration_writers_store_names_not_internal_ids()
    {
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var ministry = PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM institutions WHERE name = '{Ministry}'"))!);
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("protokoll@ash.local");
        var today = TiraneTime.Today(new SystemClock());
        var response = await client.PostAsJsonAsync("/api/cases/register", new Dictionary<string, object?>
        {
            ["summonsNumber"] = "FL-020",
            ["actNumber"] = "AKT-020",
            ["actDate"] = today.AddDays(-5).ToString("yyyy-MM-dd"),
            ["receivedDate"] = today.ToString("yyyy-MM-dd"),
            ["courtLevelCode"] = "FIRST_INSTANCE",
            ["courtId"] = await seed.CourtAsync("Gjykata e Shkallës së Parë Tiranë"),
            ["judgeId"] = await seed.JudgeAsync("Ilir", "Gjyqtari"),
            ["objectText"] = "Kërkesë për shpërblim",
            ["firstHearing"] = new { at = DateTime.UtcNow.AddDays(14).ToString("yyyy-MM-ddTHH:mm:ssZ") },
            ["parties"] = new object[] { new { partyRoleCode = "I_PADITUR", kind = "INSTITUTION", institutionPublicId = ministry } }
        }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));

        var details = await Strings("SELECT details FROM case_history_events WHERE details IS NOT NULL");
        details.ShouldNotBeEmpty();
        details.ShouldAllBe(d => !Regex.IsMatch(d, "\"(id|\\w+Id)\"\\s*:\\s*\\d"));
        (await Scalar("SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.court')) FROM case_history_events WHERE event_type = 'CASE_REGISTERED'"))
            .ShouldBe("Gjykata e Shkallës së Parë Tiranë");
        (await Scalar("SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.name')) FROM case_history_events WHERE event_type = 'SENT_TO_HEAD'"))
            .ShouldBe("Kryetar Dev");
    }

    [Fact]
    public async Task Case_view_is_audited_once_per_request()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");
        await Get(client, $"/api/cases/{s.Level2}");

        var sql = $"SELECT COUNT(*) FROM ash_audit.audit_log WHERE action = 'CASE_VIEW' AND case_id = {s.Level2Id}";
        var deadline = DateTime.UtcNow.AddSeconds(15);
        long count;
        while ((count = Convert.ToInt64(await Scalar(sql, Db.AuditConnectionString))) < 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, Ct);
        }

        count.ShouldBe(1);
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<List<string>> Permissions(AshApiFactory factory, string user, Guid caseId)
    {
        using var client = factory.CreateClient().AsUser(user);
        return (await Get(client, $"/api/cases/{caseId}")).GetProperty("permissions").EnumerateArray()
            .Select(p => p.GetString()!).ToList();
    }

    private static async Task<List<JsonElement>> AllHistory(HttpClient client, Guid caseId, int limit = 100)
    {
        var all = new List<JsonElement>();
        string? cursor = null;
        for (var pages = 0; pages < 50; pages++)
        {
            var page = await Get(client, $"/api/cases/{caseId}/history?limit={limit}"
                                         + (cursor is null ? "" : $"&after={Uri.EscapeDataString(cursor)}"));
            all.AddRange(page.GetProperty("items").EnumerateArray());
            cursor = page.GetProperty("nextCursor") is { ValueKind: JsonValueKind.String } next ? next.GetString() : null;
            if (cursor is null)
            {
                return all;
            }
        }

        throw new InvalidOperationException("History paging did not end.");
    }

    private static async Task<JsonElement> Get(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<Guid> PublicIdOf(string table, long id) =>
        PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM {table} WHERE id = {id}"))!);

    private async Task<long> Id(string sql) => Convert.ToInt64(await Scalar(sql));

    private async Task<object?> Scalar(string sql, string? connectionString = null)
    {
        await using var connection = new MySqlConnection(connectionString ?? Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    private async Task<List<string>> Strings(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var result = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private async Task Execute(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }
}
