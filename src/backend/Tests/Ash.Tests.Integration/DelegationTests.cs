using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Modules.Cases.Contracts.Events;
using Ash.SharedKernel.Events;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 027 — the institution assigns its jurist; the primary lawyer delegates (and undelegates) the case. Uses
/// <see cref="CaseSeed"/>'s ACTIVE first-instance case: avokat1 primary, the ministry linked as MONITORING with its
/// jurist assigned ("jurist"), a second jurist of the ministry not assigned ("jurist2"), the ministry's
/// INSTITUTION_USER ("institucion").
/// </summary>
public class DelegationTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Primary = "avokat1@ash.local";
    private const string InstitutionUser = "institucion@ash.local";
    private const string Jurist = "jurist@ash.local";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class DelegatedRecorder
    {
        public ConcurrentQueue<CaseDelegated> Events { get; } = new();
    }

    private sealed class DelegatedHandler(DelegatedRecorder recorder) : IEventHandler<CaseDelegated>
    {
        public Task HandleAsync(CaseDelegated @event, CancellationToken cancellationToken = default)
        {
            recorder.Events.Enqueue(@event);
            return Task.CompletedTask;
        }
    }

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

    private static async Task<List<string>> Permissions(HttpClient client, Guid caseId)
    {
        var response = await client.GetAsync($"/api/cases/{caseId}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await Json(response)).GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToList();
    }

    private Task<object?> Links(long caseId) => Scalar($"""
        SELECT GROUP_CONCAT(CONCAT(link_mode, ':', IF(ended_at IS NULL, 'active', 'ended')) ORDER BY id)
        FROM case_institutions WHERE case_id = {caseId}
        """);

    private Task<object?> CategoryCode(long caseId) =>
        Scalar($"SELECT c.code FROM cases x LEFT JOIN case_categories c ON c.id = x.category_id WHERE x.id = {caseId}");

    private async Task<HttpResponseMessage> Delegate(HttpClient client, Guid caseId, long caseRowId, Guid institution,
        string mode = "DELEGIM", uint? rowVersion = null) =>
        await client.PostAsJsonAsync($"/api/cases/{caseId}/delegate",
            new { institutionPublicId = institution, mode, rowVersion = rowVersion ?? await RowVersion(caseRowId) }, Ct);

    // ---------------------------------------------------------------- delegation

    [Fact]
    public async Task Delegation_moves_the_rights_to_the_institution_and_undelegation_gives_them_back()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var caseId = seed.ActiveCaseId;
        var id = await PublicIdOf("cases", caseId);
        var ministry = await PublicIdOf("institutions", seed.User("institucion").InstitutionId!.Value);
        // the case was in Ndjekje: delegating replaces that category
        await Scalar($"""
            UPDATE cases SET category_id = (SELECT id FROM case_categories WHERE code = 'NDJEKJE'),
                             subcategory_id = (SELECT id FROM case_subcategories WHERE code = 'KOD_A_AVSH') WHERE id = {caseId}
            """);
        var recorder = new DelegatedRecorder();
        await using var factory = new AshApiFactory(Db, configureServices: services =>
        {
            services.AddSingleton(recorder);
            services.AddScoped<IEventHandler<CaseDelegated>, DelegatedHandler>();
        });
        using var primary = factory.CreateClient().AsUser(Primary);
        using var jurist = factory.CreateClient().AsUser(Jurist);
        using var institution = factory.CreateClient().AsUser(InstitutionUser);

        // monitoring: the jurist and the institution user only follow the case
        var before = await Permissions(jurist, id);
        before.ShouldNotContain("decision.record");
        before.ShouldNotContain("case.change_judge");

        var delegated = await Delegate(primary, id, caseId, ministry);

        delegated.StatusCode.ShouldBe(HttpStatusCode.OK, await delegated.Content.ReadAsStringAsync(Ct));
        var body = await Json(delegated);
        JsonAssert.NoNumericIds(body);
        body.GetProperty("linkMode").GetString().ShouldBe("DELEGATED");
        body.GetProperty("category").GetProperty("code").GetString().ShouldBe("DELEGIM");
        (await Links(caseId)).ShouldBe("MONITORING:ended,DELEGATED:active");
        (await CategoryCode(caseId)).ShouldBe("DELEGIM");
        (await Scalar($"SELECT subcategory_id FROM cases WHERE id = {caseId}")).ShouldBeNull();
        var history = $"FROM case_history_events WHERE case_id = {caseId} AND event_type = 'DELEGATED'";
        (await Scalar($"SELECT summary {history}")).ShouldBe("Çështja iu delegua institucionit Ministria e Financave dhe Ekonomisë.");
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.institution')) {history}")).ShouldBe("Ministria e Financave dhe Ekonomisë");
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.mode')) {history}")).ShouldBe("DELEGATED");
        var announced = recorder.Events.ShouldHaveSingleItem();
        announced.InstitutionPublicId.ShouldBe(ministry);
        announced.Mode.ShouldBe("DELEGIM");

        // the delegated profile: the jurist records the decision, changes the judge
        var after = await Permissions(jurist, id);
        after.ShouldContain("decision.record");
        after.ShouldContain("case.change_judge");
        (await Permissions(institution, id)).ShouldContain("decision.record");

        // the category belongs to the delegation now (task 025), and a second delegation is refused
        var category = await primary.PatchAsJsonAsync($"/api/cases/{id}/category",
            new { categoryCode = "NDJEKJE", subcategoryCode = "PA_KOD", rowVersion = await RowVersion(caseId) }, Ct);
        category.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(category)).GetProperty("code").GetString().ShouldBe("case.delegated");
        var again = await Delegate(primary, id, caseId, ministry, "RELATIM");
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(again)).GetProperty("code").GetString().ShouldBe("case.already_delegated");

        // undelegate: monitoring again, no category, the jurist stays (with the monitoring rights)
        var undelegated = await primary.PostAsJsonAsync($"/api/cases/{id}/undelegate",
            new { reason = " Institucioni kërkoi kthimin ", rowVersion = await RowVersion(caseId) }, Ct);

        undelegated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(undelegated)).GetProperty("category").ValueKind.ShouldBe(JsonValueKind.Null);
        (await Links(caseId)).ShouldBe("MONITORING:ended,DELEGATED:ended,MONITORING:active");
        (await CategoryCode(caseId)).ShouldBeNull();
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.reason')) FROM case_history_events WHERE case_id = {caseId} AND event_type = 'UNDELEGATED'"))
            .ShouldBe("Institucioni kërkoi kthimin");
        (await Scalar($"SELECT COUNT(*) FROM case_assignments WHERE case_id = {caseId} AND case_role = 'INSTITUTION_JURIST' AND ended_at IS NULL"))
            .ShouldBe(1L);
        var monitoringAgain = await Permissions(jurist, id);
        monitoringAgain.ShouldNotContain("decision.record");
        monitoringAgain.ShouldContain("case.view");

        // the lawyer sets the category again
        (await primary.PatchAsJsonAsync($"/api/cases/{id}/category",
            new { categoryCode = "NDJEKJE", subcategoryCode = "PA_KOD", rowVersion = await RowVersion(caseId) }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delegation_is_refused_where_it_is_not_allowed()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var other = await Seed.CreateAsync(Db.AppConnectionString);
        var unlinked = await PublicIdOf("institutions", await other.InstitutionAsync($"Institucion pa lidhje {other.Suffix}", "IPL"));
        var caseId = seed.ActiveCaseId;
        var id = await PublicIdOf("cases", caseId);
        var ministry = await PublicIdOf("institutions", seed.User("institucion").InstitutionId!.Value);
        await using var factory = new AshApiFactory(Db);
        using var primary = factory.CreateClient().AsUser(Primary);

        // an institution that is not linked to the case (not a party)
        var notLinked = await Delegate(primary, id, caseId, unlinked);
        notLinked.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(notLinked)).GetProperty("errors").TryGetProperty("institutionPublicId", out _).ShouldBeTrue();

        // a mode that is not a delegation category
        var mode = await Delegate(primary, id, caseId, ministry, "NDJEKJE");
        (await Json(mode)).GetProperty("errors").TryGetProperty("mode", out _).ShouldBeTrue();

        // stale row version
        var stale = await Delegate(primary, id, caseId, ministry, rowVersion: await RowVersion(caseId) + 1);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(stale)).GetProperty("code").GetString().ShouldBe("case.changed");

        // only the primary lawyer (case.delegate)
        using var secondary = factory.CreateClient().AsUser("avokat2@ash.local");
        (await Delegate(secondary, id, caseId, ministry)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // nothing to undelegate
        var undelegate = await primary.PostAsJsonAsync($"/api/cases/{id}/undelegate", new { rowVersion = await RowVersion(caseId) }, Ct);
        undelegate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(undelegate)).GetProperty("code").GetString().ShouldBe("case.not_delegated");

        // Gjykata e Lartë: no delegation at that level
        await Scalar($"UPDATE cases SET court_level_id = (SELECT id FROM court_levels WHERE code = 'HIGH_COURT') WHERE id = {caseId}");
        var highCourt = await Delegate(primary, id, caseId, ministry);
        highCourt.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(highCourt)).GetProperty("title").GetString().ShouldBe("Delegimi lejohet vetëm në shkallën e parë dhe të dytë.");

        (await Links(caseId)).ShouldBe("MONITORING:active");
        (await Scalar($"SELECT COUNT(*) FROM case_history_events WHERE case_id = {caseId}")).ShouldBe(0L);
    }

    // ---------------------------------------------------------------- the institution's jurist

    [Fact]
    public async Task Institution_user_replaces_its_jurist_and_the_old_one_loses_access()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var caseId = seed.ActiveCaseId;
        var id = await PublicIdOf("cases", caseId);
        var jurist2 = await PublicIdOf("users", seed.User("jurist2").Id);
        var jurist2Email = (string)(await Scalar($"SELECT email FROM users WHERE id = {seed.User("jurist2").Id}"))!;
        await using var factory = new AshApiFactory(Db);
        using var institution = factory.CreateClient().AsUser(InstitutionUser);
        using var oldJurist = factory.CreateClient().AsUser(Jurist);
        using var newJurist = factory.CreateClient().AsUser(jurist2Email);

        (await Permissions(institution, id)).ShouldContain("institution.assign_jurist");
        var picker = (await Json(await institution.GetAsync($"/api/cases/{id}/assignable-jurists", Ct))).EnumerateArray()
            .ToDictionary(x => x.GetProperty("fullName").GetString()!, x => x.GetProperty("isCurrent").GetBoolean());
        picker["Jurist Dev"].ShouldBeTrue();
        picker["Jurist Two"].ShouldBeFalse();
        (await newJurist.GetAsync($"/api/cases/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var replaced = await institution.PostAsJsonAsync($"/api/cases/{id}/jurist",
            new { userPublicId = jurist2, rowVersion = await RowVersion(caseId) }, Ct);

        replaced.StatusCode.ShouldBe(HttpStatusCode.OK, await replaced.Content.ReadAsStringAsync(Ct));
        var body = await Json(replaced);
        body.GetProperty("jurist").GetProperty("name").GetString().ShouldBe("Jurist Two");
        body.GetProperty("previous").GetProperty("name").GetString().ShouldBe("Jurist Dev");
        (await Scalar($"""
            SELECT GROUP_CONCAT(CONCAT(u.first_name, ' ', u.last_name, ':', COALESCE(a.end_reason, 'current')) ORDER BY a.id)
            FROM case_assignments a JOIN users u ON u.id = a.user_id WHERE a.case_id = {caseId} AND a.case_role = 'INSTITUTION_JURIST'
            """)).ShouldBe("Jurist Dev:REMOVED,Jurist Two:current");
        (await oldJurist.GetAsync($"/api/cases/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await newJurist.GetAsync($"/api/cases/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var history = $"FROM case_history_events WHERE case_id = {caseId} AND event_type = 'JURIST_ASSIGNED'";
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.old.name')) {history}")).ShouldBe("Jurist Dev");
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.userPublicId')) {history}")).ShouldBe(jurist2.ToString());

        // the same jurist again; a stale row version; someone who is not a jurist of the institution
        (await institution.PostAsJsonAsync($"/api/cases/{id}/jurist", new { userPublicId = jurist2, rowVersion = await RowVersion(caseId) }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await institution.PostAsJsonAsync($"/api/cases/{id}/jurist", new { userPublicId = jurist2, rowVersion = await RowVersion(caseId) + 1 }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var lawyer = await PublicIdOf("users", seed.User("avokat2").Id);
        (await institution.PostAsJsonAsync($"/api/cases/{id}/jurist", new { userPublicId = lawyer, rowVersion = await RowVersion(caseId) }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Only_a_linked_institution_assigns_its_jurist()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var other = await Seed.CreateAsync(Db.AppConnectionString);
        var otherInstitution = await other.InstitutionAsync($"Institucion tjetër {other.Suffix}", "IT");
        await other.UserAsync("otherinst", "Përdorues", "Tjetër", null, otherInstitution, "INSTITUTION_USER");
        var caseId = seed.ActiveCaseId;
        var id = await PublicIdOf("cases", caseId);
        var body = new { userPublicId = await PublicIdOf("users", seed.User("jurist2").Id), rowVersion = await RowVersion(caseId) };
        await using var factory = new AshApiFactory(Db);

        // an institution user of another institution: the case does not exist for them
        using var outsider = factory.CreateClient().AsUser($"otherinst-{other.Suffix}@ash.test");
        (await outsider.GetAsync($"/api/cases/{id}/assignable-jurists", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.PostAsJsonAsync($"/api/cases/{id}/jurist", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // a lawyer has no institution.assign_jurist
        using var lawyer = factory.CreateClient().AsUser(Primary);
        (await lawyer.GetAsync($"/api/cases/{id}/assignable-jurists", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await lawyer.PostAsJsonAsync($"/api/cases/{id}/jurist", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Permissions(lawyer, id)).ShouldNotContain("institution.assign_jurist");

        (await Scalar($"SELECT COUNT(*) FROM case_assignments WHERE case_id = {caseId} AND case_role = 'INSTITUTION_JURIST'")).ShouldBe(1L);
    }
}
