using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 025 — the case's working data: claim, category, suspension, security measure, priority. Uses
/// <see cref="CaseSeed"/>'s ACTIVE case (avokat1 primary, avokat2 secondary, no category, no first data entry yet).
/// </summary>
public class CaseDataTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Primary = "avokat1@ash.local";
    private const string Secondary = "avokat2@ash.local";
    private const string Kryetar = "kryetar@ash.local";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(Ct);
        return value is DBNull ? null : value;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private async Task<Guid> PublicIdOf(string table, long id) =>
        Ash.SharedKernel.Ids.PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM {table} WHERE id = {id}"))!);

    private async Task<uint> RowVersion(long caseId) => Convert.ToUInt32(await Scalar($"SELECT row_version FROM cases WHERE id = {caseId}"));

    private static Task<HttpResponseMessage> Patch(HttpClient client, Guid caseId, string action, object body) =>
        client.PatchAsJsonAsync($"/api/cases/{caseId}/{action}", body, Ct);

    private static Task<HttpResponseMessage> PutMeasure(HttpClient client, Guid caseId, object body) =>
        client.PutAsJsonAsync($"/api/cases/{caseId}/security-measure", body, Ct);

    private static async Task<uint> NewRowVersion(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await Json(response)).GetProperty("rowVersion").GetUInt32();
    }

    private Task<object?> Events(long caseId) =>
        Scalar($"SELECT GROUP_CONCAT(event_type ORDER BY id) FROM case_history_events WHERE case_id = {caseId}");

    private async Task<(CaseSeed Seed, Guid Case, AshApiFactory Factory)> SetupAsync()
    {
        var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        return (seed, await PublicIdOf("cases", seed.ActiveCaseId), new AshApiFactory(Db));
    }

    // ---------------------------------------------------------------- concurrency

    [Fact]
    public async Task Second_editor_with_the_old_row_version_gets_409_and_chained_saves_succeed()
    {
        var (seed, id, factory) = await SetupAsync();
        await using var seedOwner = seed;
        await using var factoryOwner = factory;
        using var primary = factory.CreateClient().AsUser(Primary);
        using var secondary = factory.CreateClient().AsUser(Secondary);
        var read = await RowVersion(seed.ActiveCaseId); // both opened the case

        var first = await Patch(primary, id, "claim", new { claimAmount = "1000", rowVersion = read });
        var second = await Patch(secondary, id, "claim", new { claimAmount = "2000", rowVersion = read });

        (await NewRowVersion(first)).ShouldBe(read + 1);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await Json(second);
        problem.GetProperty("code").GetString().ShouldBe("case.changed");
        problem.GetProperty("title").GetString().ShouldBe("Të dhënat u ndryshuan nga një përdorues tjetër.");
        (await Scalar($"SELECT claim_amount FROM cases WHERE id = {seed.ActiveCaseId}")).ShouldBe(1000m);

        // the primary chains every action with the row version each one returns
        var rv = read + 1;
        rv = await NewRowVersion(await Patch(primary, id, "category", new { categoryCode = "NDJEKJE", subcategoryCode = "PA_KOD", rowVersion = rv }));
        rv = await NewRowVersion(await Patch(primary, id, "suspension", new { suspended = true, rowVersion = rv }));
        rv = await NewRowVersion(await Patch(primary, id, "priority", new { priority = "HIGH", rowVersion = rv }));
        rv = await NewRowVersion(await PutMeasure(primary, id, new { active = true, decisionNumber = "55/2026", decisionDate = "2026-09-01", rowVersion = rv }));
        rv = await NewRowVersion(await Patch(primary, id, "claim", new { claimAreaM2 = "120.5", rowVersion = rv }));

        rv.ShouldBe(read + 6);
        (await RowVersion(seed.ActiveCaseId)).ShouldBe(rv);
        (await Events(seed.ActiveCaseId)).ShouldBe(
            "CLAIM_CHANGED,CATEGORY_CHANGED,SUSPENSION_CHANGED,PRIORITY_CHANGED,SECURITY_MEASURE_SET,CLAIM_CHANGED");

        // nothing to change → 400, row version untouched
        var same = await Patch(primary, id, "priority", new { priority = "HIGH", rowVersion = rv });
        same.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(same)).GetProperty("title").GetString().ShouldBe("Nuk ka ndryshime.");
        (await RowVersion(seed.ActiveCaseId)).ShouldBe(rv);
    }

    // ---------------------------------------------------------------- claim

    [Fact]
    public async Task Claim_amounts_are_exact_decimal_strings()
    {
        var (seed, id, factory) = await SetupAsync();
        await using var seedOwner = seed;
        await using var factoryOwner = factory;
        using var primary = factory.CreateClient().AsUser(Primary);

        // too many decimals, a sign, too many digits, a comma, a JSON-style exponent
        foreach (var bad in new[] { "12.345", "-1", "10000000000000000", "12,5", "1e5" })
        {
            var refused = await Patch(primary, id, "claim", new { claimAmount = bad, rowVersion = await RowVersion(seed.ActiveCaseId) });
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, bad);
            (await Json(refused)).GetProperty("errors").TryGetProperty("claimAmount", out _).ShouldBeTrue(bad);
        }

        var area = await Patch(primary, id, "claim", new { claimAreaM2 = "1000000000000", rowVersion = await RowVersion(seed.ActiveCaseId) });
        area.StatusCode.ShouldBe(HttpStatusCode.BadRequest); // 13 digits: DECIMAL(14,2) holds 12
        var currency = await Patch(primary, id, "claim", new { claimAmount = "5", claimCurrency = "euro", rowVersion = await RowVersion(seed.ActiveCaseId) });
        (await Json(currency)).GetProperty("errors").TryGetProperty("claimCurrency", out _).ShouldBeTrue();

        // the largest DECIMAL(18,2) round-trips exactly; no currency → ALL
        var saved = await Patch(primary, id, "claim", new { claimAmount = "9999999999999999.99", claimAreaM2 = "250.5", rowVersion = await RowVersion(seed.ActiveCaseId) });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await Json(saved);
        body.GetProperty("claimAmount").GetString().ShouldBe("9999999999999999.99");
        body.GetProperty("claimCurrency").GetString().ShouldBe("ALL");
        body.GetProperty("claimAreaM2").GetString().ShouldBe("250.50");
        (await Scalar($"SELECT CONCAT(claim_amount, ' ', claim_currency, ' ', claim_area_m2) FROM cases WHERE id = {seed.ActiveCaseId}"))
            .ShouldBe("9999999999999999.99 ALL 250.50");

        var claim = (await Json(await primary.GetAsync($"/api/cases/{id}", Ct))).GetProperty("data").GetProperty("claim");
        claim.GetProperty("amount").ValueKind.ShouldBe(JsonValueKind.String);
        claim.GetProperty("amount").GetString().ShouldBe("9999999999999999.99");
        claim.GetProperty("areaM2").GetString().ShouldBe("250.50");

        var history = $"FROM case_history_events WHERE case_id = {seed.ActiveCaseId} AND event_type = 'CLAIM_CHANGED'";
        (await Scalar($"SELECT JSON_TYPE(JSON_EXTRACT(details, '$.new.claimAmount')) {history}")).ShouldBe("STRING");
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.claimAmount')) {history}")).ShouldBe("9999999999999999.99");
        (await Scalar($"SELECT JSON_TYPE(JSON_EXTRACT(details, '$.old.claimAmount')) {history}")).ShouldBe("NULL");

        // clearing the amount clears its currency too (CHECK ck_cases_claim)
        var cleared = await Json(await Patch(primary, id, "claim", new { claimAmount = (string?)null, claimAreaM2 = "250.50", rowVersion = await RowVersion(seed.ActiveCaseId) }));
        cleared.GetProperty("claimAmount").ValueKind.ShouldBe(JsonValueKind.Null);
        cleared.GetProperty("claimCurrency").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // ---------------------------------------------------------------- category

    [Fact]
    public async Task Category_is_ndjekje_with_its_subcategory_and_never_a_delegation()
    {
        var (seed, id, factory) = await SetupAsync();
        await using var seedOwner = seed;
        await using var factoryOwner = factory;
        using var primary = factory.CreateClient().AsUser(Primary);

        async Task<JsonElement> BadRequest(object body)
        {
            var response = await Patch(primary, id, "category", body);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            return (await Json(response)).GetProperty("errors");
        }

        var rv = await RowVersion(seed.ActiveCaseId);
        (await BadRequest(new { categoryCode = "DELEGIM", subcategoryCode = "KOD_A_AVSH", rowVersion = rv }))
            .GetProperty("categoryCode")[0].GetString().ShouldBe("Delegimi/relatimi caktohet nga veprimi i delegimit.");
        (await BadRequest(new { categoryCode = "RELATIM", rowVersion = rv })).TryGetProperty("categoryCode", out _).ShouldBeTrue();
        (await BadRequest(new { categoryCode = "NDJEKJE", rowVersion = rv })).TryGetProperty("subcategoryCode", out _).ShouldBeTrue();
        (await BadRequest(new { categoryCode = "NDJEKJE", subcategoryCode = "NUK_EKZISTON", rowVersion = rv }))
            .GetProperty("subcategoryCode")[0].GetString().ShouldBe("Nënkategoria nuk i përket kësaj kategorie.");
        (await BadRequest(new { categoryCode = "NUK_EKZISTON", rowVersion = rv })).TryGetProperty("categoryCode", out _).ShouldBeTrue();

        var set = await Patch(primary, id, "category", new { categoryCode = "NDJEKJE", subcategoryCode = "KOD_A_KM", rowVersion = rv });
        set.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(set)).GetProperty("subcategory").GetProperty("code").GetString().ShouldBe("KOD_A_KM");
        var history = $"FROM case_history_events WHERE case_id = {seed.ActiveCaseId} AND event_type = 'CATEGORY_CHANGED'";
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.subcategory')) {history}")).ShouldBe("KOD A KM");

        // a delegated case: the category belongs to the delegation (task 027)
        await Scalar($"UPDATE cases SET category_id = (SELECT id FROM case_categories WHERE code = 'DELEGIM'), subcategory_id = NULL WHERE id = {seed.ActiveCaseId}");
        var delegated = await Patch(primary, id, "category", new { categoryCode = "NDJEKJE", subcategoryCode = "PA_KOD", rowVersion = await RowVersion(seed.ActiveCaseId) });
        delegated.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await Json(delegated);
        problem.GetProperty("code").GetString().ShouldBe("case.delegated");
        problem.GetProperty("title").GetString().ShouldBe("Çështja është e deleguar; ndryshimi bëhet te delegimi.");
    }

    // ---------------------------------------------------------------- suspension and security measure

    [Fact]
    public async Task Suspend_and_resume_set_and_clear_suspended_at()
    {
        var (seed, id, factory) = await SetupAsync();
        await using var seedOwner = seed;
        await using var factoryOwner = factory;
        using var primary = factory.CreateClient().AsUser(Primary);
        var row = $"FROM cases WHERE id = {seed.ActiveCaseId}";

        var suspended = await Json(await Patch(primary, id, "suspension", new { suspended = true, reason = " Pret vendimin e Kushtetueses ", rowVersion = await RowVersion(seed.ActiveCaseId) }));
        suspended.GetProperty("isSuspended").GetBoolean().ShouldBeTrue();
        ((DateTime)(await Scalar($"SELECT suspended_at {row}"))!).ShouldBe(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        (await Scalar($"SELECT is_suspended = 1 {row}")).ShouldBe(1L);
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.reason')) FROM case_history_events WHERE case_id = {seed.ActiveCaseId}"))
            .ShouldBe("Pret vendimin e Kushtetueses");

        (await Patch(primary, id, "suspension", new { suspended = true, rowVersion = await RowVersion(seed.ActiveCaseId) }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest); // already suspended

        var resumed = await Json(await Patch(primary, id, "suspension", new { suspended = false, rowVersion = await RowVersion(seed.ActiveCaseId) }));
        resumed.GetProperty("suspendedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        (await Scalar($"SELECT suspended_at {row}")).ShouldBeNull();
        (await Events(seed.ActiveCaseId)).ShouldBe("SUSPENSION_CHANGED,SUSPENSION_CHANGED");
    }

    [Fact]
    public async Task Lifting_keeps_the_measure_row_and_setting_again_updates_it()
    {
        var (seed, id, factory) = await SetupAsync();
        await using var seedOwner = seed;
        await using var factoryOwner = factory;
        using var primary = factory.CreateClient().AsUser(Primary);
        var measure = $"FROM case_security_measures WHERE case_id = {seed.ActiveCaseId}";

        (await PutMeasure(primary, id, new { active = true, rowVersion = await RowVersion(seed.ActiveCaseId) }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest); // number and date required
        (await PutMeasure(primary, id, new { active = false, rowVersion = await RowVersion(seed.ActiveCaseId) }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest); // nothing to lift

        var set = await Json(await PutMeasure(primary, id, new
        {
            active = true, decisionNumber = " 12/2026 ", decisionDate = "2026-09-01", dispositive = "Sekuestro konservative",
            rowVersion = await RowVersion(seed.ActiveCaseId)
        }));
        set.GetProperty("measure").GetProperty("decisionNumber").GetString().ShouldBe("12/2026");
        set.GetProperty("measure").GetProperty("decisionDate").GetString().ShouldBe("2026-09-01");

        var lifted = await Json(await PutMeasure(primary, id, new { active = false, rowVersion = await RowVersion(seed.ActiveCaseId) }));
        lifted.GetProperty("hasSecurityMeasure").GetBoolean().ShouldBeFalse();
        lifted.GetProperty("measure").ValueKind.ShouldBe(JsonValueKind.Null);
        (await Scalar($"SELECT has_security_measure = 0 FROM cases WHERE id = {seed.ActiveCaseId}")).ShouldBe(1L);
        (await Scalar($"SELECT decision_number {measure}")).ShouldBe("12/2026"); // the row stays

        (await NewRowVersion(await PutMeasure(primary, id, new
        {
            active = true, decisionNumber = "40/2026", decisionDate = "2026-10-15", rowVersion = await RowVersion(seed.ActiveCaseId)
        }))).ShouldBeGreaterThan(0u);
        (await Scalar($"SELECT COUNT(*) {measure}")).ShouldBe(1L);
        (await Scalar($"SELECT CONCAT(decision_number, ' ', decision_date, ' ', COALESCE(dispositive, '-')) {measure}"))
            .ShouldBe("40/2026 2026-10-15 -");

        (await Events(seed.ActiveCaseId)).ShouldBe("SECURITY_MEASURE_SET,SECURITY_MEASURE_LIFTED,SECURITY_MEASURE_SET");
        var history = $"FROM case_history_events WHERE case_id = {seed.ActiveCaseId}";
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.old.decisionNumber')) {history} AND event_type = 'SECURITY_MEASURE_LIFTED'"))
            .ShouldBe("12/2026");
        // setting again: the replaced values are in the history
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.old.decisionNumber')) {history} AND event_type = 'SECURITY_MEASURE_SET' ORDER BY id DESC LIMIT 1"))
            .ShouldBe("12/2026");
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.decisionDate')) {history} AND event_type = 'SECURITY_MEASURE_SET' ORDER BY id DESC LIMIT 1"))
            .ShouldBe("2026-10-15");
    }

    // ---------------------------------------------------------------- permissions and first data entry

    [Fact]
    public async Task Each_action_needs_its_own_permission()
    {
        var (seed, id, factory) = await SetupAsync();
        await using var seedOwner = seed;
        await using var factoryOwner = factory;
        using var secondary = factory.CreateClient().AsUser(Secondary);
        using var kryetar = factory.CreateClient().AsUser(Kryetar);

        // the secondary edits data, not the category, the status or the priority
        (await NewRowVersion(await Patch(secondary, id, "claim", new { claimAmount = "100", rowVersion = await RowVersion(seed.ActiveCaseId) })))
            .ShouldBeGreaterThan(0u);
        (await Patch(secondary, id, "category", new { categoryCode = "NDJEKJE", subcategoryCode = "PA_KOD", rowVersion = await RowVersion(seed.ActiveCaseId) }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Patch(secondary, id, "suspension", new { suspended = true, rowVersion = await RowVersion(seed.ActiveCaseId) }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Patch(secondary, id, "priority", new { priority = "LOW", rowVersion = await RowVersion(seed.ActiveCaseId) }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // the Kryetar sets the priority (set_priority_any), nothing else of the data
        (await NewRowVersion(await Patch(kryetar, id, "priority", new { priority = "MEDIUM", rowVersion = await RowVersion(seed.ActiveCaseId) })))
            .ShouldBeGreaterThan(0u);
        (await Patch(kryetar, id, "claim", new { claimAmount = "5", rowVersion = await RowVersion(seed.ActiveCaseId) }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // no access at all → 404
        using var drLawyer = factory.CreateClient().AsUser($"{(await Scalar($"SELECT email FROM users WHERE id = {seed.User("drlawyer").Id}"))}");
        (await Patch(drLawyer, id, "claim", new { claimAmount = "5", rowVersion = await RowVersion(seed.ActiveCaseId) }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task First_data_entry_is_set_by_the_team_not_by_the_kryetar()
    {
        var (seed, id, factory) = await SetupAsync();
        await using var seedOwner = seed;
        await using var factoryOwner = factory;
        using var kryetar = factory.CreateClient().AsUser(Kryetar);
        using var secondary = factory.CreateClient().AsUser(Secondary);
        var firstEntry = $"SELECT first_data_entry_at FROM cases WHERE id = {seed.ActiveCaseId}";

        (await NewRowVersion(await Patch(kryetar, id, "priority", new { priority = "HIGH", rowVersion = await RowVersion(seed.ActiveCaseId) })))
            .ShouldBeGreaterThan(0u);
        (await Scalar(firstEntry)).ShouldBeNull();

        (await NewRowVersion(await Patch(secondary, id, "claim", new { claimAmount = "100", rowVersion = await RowVersion(seed.ActiveCaseId) })))
            .ShouldBeGreaterThan(0u);
        var stamped = (DateTime)(await Scalar(firstEntry))!;
        stamped.ShouldBe(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        // set once: a later edit does not move it
        await Task.Delay(50, Ct);
        await Patch(secondary, id, "claim", new { claimAmount = "200", rowVersion = await RowVersion(seed.ActiveCaseId) });
        (await Scalar(firstEntry)).ShouldBe(stamped);
    }

    [Fact]
    public async Task A_party_added_by_the_team_is_a_first_data_entry()
    {
        var (seed, id, factory) = await SetupAsync();
        await using var seedOwner = seed;
        await using var factoryOwner = factory;
        using var secondary = factory.CreateClient().AsUser(Secondary);

        var added = await secondary.PostAsJsonAsync($"/api/cases/{id}/parties",
            new { partyRoleCode = "PADITES", kind = "NATURAL", displayName = "Arben Kola" }, Ct);

        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync(Ct));
        (await Scalar($"SELECT first_data_entry_at FROM cases WHERE id = {seed.ActiveCaseId}")).ShouldNotBeNull();
        // a party change does not touch the case's row version (task 017)
    }

    [Fact]
    public async Task Closed_case_is_refused()
    {
        var (seed, id, factory) = await SetupAsync();
        await using var seedOwner = seed;
        await using var factoryOwner = factory;
        await Scalar($"UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = UTC_TIMESTAMP(3), closed_by = {seed.User("avokat1").Id} WHERE id = {seed.ActiveCaseId}");
        using var primary = factory.CreateClient().AsUser(Primary);
        var rv = await RowVersion(seed.ActiveCaseId);

        foreach (var response in new[]
                 {
                     await Patch(primary, id, "claim", new { claimAmount = "1", rowVersion = rv }),
                     await Patch(primary, id, "category", new { categoryCode = "NDJEKJE", subcategoryCode = "PA_KOD", rowVersion = rv }),
                     await Patch(primary, id, "suspension", new { suspended = true, rowVersion = rv }),
                     await Patch(primary, id, "priority", new { priority = "LOW", rowVersion = rv }),
                     await PutMeasure(primary, id, new { active = true, decisionNumber = "1", decisionDate = "2026-01-01", rowVersion = rv })
                 })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await Json(response)).GetProperty("code").GetString().ShouldBe("case.closed");
        }

        (await Events(seed.ActiveCaseId)).ShouldBeNull();
        (await RowVersion(seed.ActiveCaseId)).ShouldBe(rv);
    }
}
