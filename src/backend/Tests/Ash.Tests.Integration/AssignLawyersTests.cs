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
/// Task 023 — the Kryetar assigns the primary and secondary lawyer. Uses <see cref="CaseSeed"/>: office TR with the
/// seeded team (an ACTIVE case: avokat1 primary, avokat2 secondary) and a REGISTERED case, office DR with its lawyer.
/// </summary>
public class AssignLawyersTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Kryetar = "kryetar@ash.local";
    private const string Avokat1 = "avokat1@ash.local";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class AssignedRecorder
    {
        public ConcurrentQueue<LawyerAssigned> Events { get; } = new();
    }

    private sealed class AssignedHandler(AssignedRecorder recorder) : IEventHandler<LawyerAssigned>
    {
        public Task HandleAsync(LawyerAssigned @event, CancellationToken cancellationToken = default)
        {
            recorder.Events.Enqueue(@event);
            return Task.CompletedTask;
        }
    }

    private AshApiFactory FactoryRecording(AssignedRecorder recorder) =>
        new(Db, configureServices: services =>
        {
            services.AddSingleton(recorder);
            services.AddScoped<IEventHandler<LawyerAssigned>, AssignedHandler>();
        });

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

    private static string AssignPath(Guid casePublicId) => $"/api/cases/{casePublicId}/assignment";

    private static string AssignablePath(Guid casePublicId) => $"/api/cases/{casePublicId}/assignable-lawyers";

    private static async Task<List<string>> TabCodes(HttpClient client, string tab)
    {
        var response = await client.GetAsync($"/api/cases?tab={tab}&limit=100", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, tab);
        return (await Json(response)).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("caseCode").GetString()!).ToList();
    }

    private static async Task<string> ProblemCode(HttpResponseMessage response) =>
        (await Json(response)).GetProperty("code").GetString()!;

    /// <summary>One more case of office TR (create_dossier + level 1) in the given status.</summary>
    private async Task<long> CaseAsync(CaseSeed s, string status)
    {
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var protokoll = s.User("protokoll").Id;
        var (dossier, code) = await seed.DossierAsync(s.OfficeTrId, protokoll);
        var id = await seed.RegisteredCaseAsync(dossier, 1, $"{code}/1", s.OfficeTrId, "FIRST_INSTANCE",
            await seed.CourtAsync("Gjykata e Shkallës së Parë Tiranë"), null, "FL-W", "AKT-W", DateTime.Today, "Test", protokoll);
        var closed = status == "CLOSED" ? $"UTC_TIMESTAMP(3), closed_by = {s.User("kryetar").Id}" : "NULL";
        await Scalar($"UPDATE cases SET lifecycle_status = '{status}', closed_at = {closed} WHERE id = {id}");
        return id;
    }

    private async Task AssignSqlAsync(CaseSeed s, long caseId, string userKey, string role, bool ended = false)
    {
        var kryetar = s.User("kryetar").Id;
        var end = ended ? $"UTC_TIMESTAMP(3), {kryetar}, 'TRANSFERRED'" : "NULL, NULL, NULL";
        await Scalar($"""
            INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by, ended_at, ended_by, end_reason)
            VALUES ({caseId}, {s.User(userKey).Id}, '{role}', {kryetar}, {end})
            """);
    }

    // ---------------------------------------------------------------- assignment

    [Fact]
    public async Task Kryetar_assigns_primary_and_secondary_and_the_case_moves_to_the_lawyers_list()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var ministry = s.User("institucion").InstitutionId!.Value;
        await Scalar($"""
            INSERT INTO case_institutions (case_id, institution_id, link_mode, started_by)
            VALUES ({s.RegisteredCaseId}, {ministry}, 'MONITORING', {s.User("protokoll").Id})
            """);
        var casePublicId = await PublicIdOf("cases", s.RegisteredCaseId);
        var caseCode = (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {s.RegisteredCaseId}"))!;
        var avokat1 = await PublicIdOf("users", s.User("avokat1").Id);
        var avokat2 = await PublicIdOf("users", s.User("avokat2").Id);
        var recorder = new AssignedRecorder();
        await using var factory = FactoryRecording(recorder);
        using var kryetar = factory.CreateClient().AsUser(Kryetar);
        using var lawyer = factory.CreateClient().AsUser(Avokat1);
        (await TabCodes(kryetar, "unassigned")).ShouldContain(caseCode);
        (await TabCodes(lawyer, "mine")).ShouldNotContain(caseCode);
        var rowVersion = await RowVersion(s.RegisteredCaseId);

        var response = await kryetar.PostAsJsonAsync(AssignPath(casePublicId), new
        {
            primaryUserPublicId = avokat1,
            secondaryUserPublicId = avokat2,
            priority = "HIGH",
            rowVersion
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await Json(response);
        result.GetProperty("status").GetString().ShouldBe("ACTIVE");
        result.GetProperty("priority").GetString().ShouldBe("HIGH");
        result.GetProperty("rowVersion").GetUInt32().ShouldBe(rowVersion + 1);
        JsonAssert.NoNumericIds(result);

        var row = $"FROM cases WHERE id = {s.RegisteredCaseId}";
        (await Scalar($"SELECT lifecycle_status {row}")).ShouldBe("ACTIVE");
        (await Scalar($"SELECT priority {row}")).ShouldBe("HIGH");
        (await Scalar($"SELECT row_version {row}")).ShouldBe(rowVersion + 1);
        var assignedAt = (DateTime)(await Scalar($"SELECT assigned_at {row}"))!;
        assignedAt.ShouldBe(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        (await Scalar($"""
            SELECT GROUP_CONCAT(CONCAT(u.email, ':', a.case_role, ':', a.assigned_by = {s.User("kryetar").Id}) ORDER BY a.case_role)
            FROM case_assignments a JOIN users u ON u.id = a.user_id WHERE a.case_id = {s.RegisteredCaseId} AND a.ended_at IS NULL
            """)).ShouldBe("avokat1@ash.local:PRIMARY_LAWYER:1,avokat2@ash.local:SECONDARY_LAWYER:1");

        var history = $"FROM case_history_events WHERE case_id = {s.RegisteredCaseId}";
        (await Scalar($"SELECT GROUP_CONCAT(event_type ORDER BY id) {history}"))
            .ShouldBe("LAWYER_ASSIGNED,SECONDARY_ASSIGNED,PRIORITY_CHANGED");
        (await Scalar($"SELECT summary {history} AND event_type = 'LAWYER_ASSIGNED'")).ShouldBe("Avokati primar u caktua: Avokat1 Dev.");
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.name')) {history} AND event_type = 'LAWYER_ASSIGNED'"))
            .ShouldBe("Avokat1 Dev");
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.userPublicId')) {history} AND event_type = 'LAWYER_ASSIGNED'"))
            .ShouldBe(avokat1.ToString());
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.userPublicId')) {history} AND event_type = 'SECONDARY_ASSIGNED'"))
            .ShouldBe(avokat2.ToString());
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.old.priority')) {history} AND event_type = 'PRIORITY_CHANGED'"))
            .ShouldBe("NONE");
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.priority')) {history} AND event_type = 'PRIORITY_CHANGED'"))
            .ShouldBe("HIGH");
        (await Scalar($"SELECT COUNT(*) {history} AND actor_user_id <> {s.User("kryetar").Id}")).ShouldBe(0L);

        var announced = recorder.Events.ShouldHaveSingleItem();
        announced.CasePublicId.ShouldBe(casePublicId);
        announced.PrimaryUserPublicId.ShouldBe(avokat1);
        announced.SecondaryUserPublicId.ShouldBe(avokat2);
        announced.LinkedInstitutionPublicIds.ShouldBe([await PublicIdOf("institutions", ministry)]);

        // 'Pa avokat' → the lawyer's 'Çështjet e mia'
        (await TabCodes(kryetar, "unassigned")).ShouldNotContain(caseCode);
        (await TabCodes(lawyer, "mine")).ShouldContain(caseCode);

        // a second assignment, even with the fresh row version: the case already has its lawyer
        var again = await kryetar.PostAsJsonAsync(AssignPath(casePublicId),
            new { primaryUserPublicId = avokat2, rowVersion = rowVersion + 1 }, Ct);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await Json(again);
        problem.GetProperty("code").GetString().ShouldBe("case.already_assigned");
        problem.GetProperty("title").GetString().ShouldBe("Çështja ka tashmë avokat. Për ndryshim përdorni transferimin.");
        (await Scalar($"SELECT COUNT(*) FROM case_assignments WHERE case_id = {s.RegisteredCaseId}")).ShouldBe(2L);
        recorder.Events.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Without_secondary_and_priority_only_the_primary_is_written()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser(Kryetar);

        var response = await kryetar.PostAsJsonAsync(AssignPath(await PublicIdOf("cases", s.RegisteredCaseId)), new
        {
            primaryUserPublicId = await PublicIdOf("users", s.User("avokat2").Id),
            rowVersion = await RowVersion(s.RegisteredCaseId)
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Scalar($"SELECT GROUP_CONCAT(event_type) FROM case_history_events WHERE case_id = {s.RegisteredCaseId}"))
            .ShouldBe("LAWYER_ASSIGNED");
        (await Scalar($"SELECT priority FROM cases WHERE id = {s.RegisteredCaseId}")).ShouldBe("NONE");
        (await Scalar($"SELECT COUNT(*) FROM case_assignments WHERE case_id = {s.RegisteredCaseId}")).ShouldBe(1L);
    }

    [Fact]
    public async Task Stale_row_version_is_409_and_changes_nothing()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var recorder = new AssignedRecorder();
        await using var factory = FactoryRecording(recorder);
        using var kryetar = factory.CreateClient().AsUser(Kryetar);
        var rowVersion = await RowVersion(s.RegisteredCaseId);
        await Scalar($"UPDATE cases SET row_version = row_version + 1 WHERE id = {s.RegisteredCaseId}"); // someone else saved

        var response = await kryetar.PostAsJsonAsync(AssignPath(await PublicIdOf("cases", s.RegisteredCaseId)), new
        {
            primaryUserPublicId = await PublicIdOf("users", s.User("avokat1").Id),
            rowVersion
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await Json(response);
        problem.GetProperty("code").GetString().ShouldBe("case.changed");
        problem.GetProperty("title").GetString().ShouldBe("Të dhënat u ndryshuan nga një përdorues tjetër.");
        (await Scalar($"SELECT lifecycle_status FROM cases WHERE id = {s.RegisteredCaseId}")).ShouldBe("REGISTERED");
        (await Scalar($"SELECT COUNT(*) FROM case_assignments WHERE case_id = {s.RegisteredCaseId}")).ShouldBe(0L);
        (await Scalar($"SELECT COUNT(*) FROM case_history_events WHERE case_id = {s.RegisteredCaseId}")).ShouldBe(0L);
        recorder.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Invalid_choices_are_400_before_anything_is_written()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        // an active OFFICE_HEAD of TR who is not a STATE_LAWYER: not offered either
        var otherHead = await seed.UserAsync("trhead", "Tjetër", "Kryetar", s.OfficeTrId, null, "OFFICE_HEAD");
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser(Kryetar);
        var path = AssignPath(await PublicIdOf("cases", s.RegisteredCaseId));
        var rowVersion = await RowVersion(s.RegisteredCaseId);
        var avokat1 = await PublicIdOf("users", s.User("avokat1").Id);

        async Task<JsonElement> Errors(object body)
        {
            var response = await kryetar.PostAsJsonAsync(path, body, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            return (await Json(response)).GetProperty("errors");
        }

        // primary = secondary
        (await Errors(new { primaryUserPublicId = avokat1, secondaryUserPublicId = avokat1, rowVersion }))
            .GetProperty("secondaryUserPublicId")[0].GetString()
            .ShouldBe("Avokati sekondar duhet të jetë i ndryshëm nga avokati primar.");

        // a lawyer of another office, as primary and as secondary
        var drLawyer = await PublicIdOf("users", s.User("drlawyer").Id);
        (await Errors(new { primaryUserPublicId = drLawyer, rowVersion }))
            .GetProperty("primaryUserPublicId")[0].GetString().ShouldBe("Ky avokat nuk mund të caktohet në këtë çështje.");
        (await Errors(new { primaryUserPublicId = avokat1, secondaryUserPublicId = drLawyer, rowVersion }))
            .TryGetProperty("secondaryUserPublicId", out _).ShouldBeTrue();

        // not a lawyer; an unknown user; a deactivated lawyer
        (await Errors(new { primaryUserPublicId = await PublicIdOf("users", otherHead), rowVersion }))
            .TryGetProperty("primaryUserPublicId", out _).ShouldBeTrue();
        (await Errors(new { primaryUserPublicId = Guid.NewGuid(), rowVersion }))
            .TryGetProperty("primaryUserPublicId", out _).ShouldBeTrue();
        var inactive = await seed.UserAsync("inactive", "Joaktiv", "Avokat", s.OfficeTrId, null, "STATE_LAWYER");
        await Scalar($"UPDATE users SET is_active = 0 WHERE id = {inactive}");
        (await Errors(new { primaryUserPublicId = await PublicIdOf("users", inactive), rowVersion }))
            .TryGetProperty("primaryUserPublicId", out _).ShouldBeTrue();

        // missing fields and an unknown priority
        var missing = await Errors(new { priority = "URGENT" });
        missing.TryGetProperty("primaryUserPublicId", out _).ShouldBeTrue();
        missing.TryGetProperty("rowVersion", out _).ShouldBeTrue();
        missing.TryGetProperty("priority", out _).ShouldBeTrue();

        (await Scalar($"SELECT COUNT(*) FROM case_assignments WHERE case_id = {s.RegisteredCaseId}")).ShouldBe(0L);
        (await Scalar($"SELECT lifecycle_status FROM cases WHERE id = {s.RegisteredCaseId}")).ShouldBe("REGISTERED");
    }

    [Fact]
    public async Task Kryetar_assigns_himself()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser(Kryetar);

        var response = await kryetar.PostAsJsonAsync(AssignPath(await PublicIdOf("cases", s.RegisteredCaseId)), new
        {
            primaryUserPublicId = await PublicIdOf("users", s.User("kryetar").Id),
            rowVersion = await RowVersion(s.RegisteredCaseId)
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Scalar($"""
            SELECT u.email FROM case_assignments a JOIN users u ON u.id = a.user_id
            WHERE a.case_id = {s.RegisteredCaseId} AND a.case_role = 'PRIMARY_LAWYER' AND a.ended_at IS NULL
            """)).ShouldBe(Kryetar);
        (await Scalar($"SELECT summary FROM case_history_events WHERE case_id = {s.RegisteredCaseId}"))
            .ShouldBe("Avokati primar u caktua: Kryetar Dev.");
    }

    // ---------------------------------------------------------------- assignable lawyers

    [Fact]
    public async Task Assignable_lawyers_have_workload_counts_and_include_the_kryetar_as_self()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        // seeded ACTIVE case: avokat1 primary, avokat2 secondary
        var second = await CaseAsync(s, "ACTIVE");
        await AssignSqlAsync(s, second, "avokat2", "PRIMARY_LAWYER");
        await AssignSqlAsync(s, second, "avokat1", "SECONDARY_LAWYER");
        var third = await CaseAsync(s, "ACTIVE");
        await AssignSqlAsync(s, third, "avokat1", "PRIMARY_LAWYER");
        var closed = await CaseAsync(s, "CLOSED"); // not counted
        await AssignSqlAsync(s, closed, "avokat2", "PRIMARY_LAWYER");
        var transferred = await CaseAsync(s, "ACTIVE"); // the ended row is not counted, the Kryetar's current one is
        await AssignSqlAsync(s, transferred, "avokat2", "PRIMARY_LAWYER", ended: true);
        await AssignSqlAsync(s, transferred, "kryetar", "PRIMARY_LAWYER");
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser(Kryetar);

        var response = await kryetar.GetAsync(AssignablePath(await PublicIdOf("cases", s.RegisteredCaseId)), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await Json(response);
        JsonAssert.NoNumericIds(body);
        var list = body.EnumerateArray().ToList();
        string Line(JsonElement x) =>
            $"{x.GetProperty("fullName").GetString()}|{x.GetProperty("isSelf").GetBoolean()}|" +
            $"{x.GetProperty("workload").GetProperty("activePrimary").GetInt32()}|{x.GetProperty("workload").GetProperty("activeSecondary").GetInt32()}";
        Guid Id(JsonElement x) => x.GetProperty("publicId").GetGuid();
        var byId = list.ToDictionary(Id, Line);

        byId[await PublicIdOf("users", s.User("avokat1").Id)].ShouldBe("Avokat1 Dev|False|2|1");
        byId[await PublicIdOf("users", s.User("avokat2").Id)].ShouldBe("Avokat2 Dev|False|1|1");
        byId[await PublicIdOf("users", s.User("kryetar").Id)].ShouldBe("Kryetar Dev|True|1|0");
        list.Count(x => x.GetProperty("isSelf").GetBoolean()).ShouldBe(1);
        byId.ShouldNotContainKey(await PublicIdOf("users", s.User("drlawyer").Id)); // another office
        byId.ShouldNotContainKey(await PublicIdOf("users", s.User("ndihmes").Id)); // not a STATE_LAWYER

        // least loaded first, then by name
        var order = list.Select(x => (Primary: x.GetProperty("workload").GetProperty("activePrimary").GetInt32(),
            Name: x.GetProperty("fullName").GetString()!)).ToList();
        order.ShouldBe(order.OrderBy(x => x.Primary).ThenBy(x => x.Name, StringComparer.InvariantCultureIgnoreCase).ToList());
        var names = order.Select(x => x.Name).ToList();
        names.IndexOf("Avokat2 Dev").ShouldBeLessThan(names.IndexOf("Kryetar Dev"));
        names.IndexOf("Kryetar Dev").ShouldBeLessThan(names.IndexOf("Avokat1 Dev"));
    }

    [Fact]
    public async Task Kryetar_filter_lawyers_are_the_same_people_as_the_picker_without_workload()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser(Kryetar);
        var assignable = await Json(await kryetar.GetAsync(AssignablePath(await PublicIdOf("cases", s.RegisteredCaseId)), Ct));

        var response = await kryetar.GetAsync("/api/cases/filter-options", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var office = (await Json(response)).GetProperty("lawyers").EnumerateArray().ToList();
        office.Select(x => x.GetProperty("publicId").GetGuid()).Order()
            .ShouldBe(assignable.EnumerateArray().Select(x => x.GetProperty("publicId").GetGuid()).Order());
        office.Where(x => x.TryGetProperty("workload", out _)).ShouldBeEmpty();
        var names = office.Select(x => x.GetProperty("fullName").GetString()!).ToList();
        names.ShouldBe(names.Order(StringComparer.InvariantCultureIgnoreCase).ToList());
        names.ShouldContain("Kryetar Dev"); // the Kryetar may assign himself, so he is a lawyer of the office too
    }

    // ---------------------------------------------------------------- access

    [Fact]
    public async Task Lawyer_gets_403_and_kryetar_of_another_office_gets_404()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        await seed.UserAsync("drkryetar", "Kryetar", "Durrës", s.OfficeDrId, null, "OFFICE_HEAD");
        await using var factory = new AshApiFactory(Db);
        var casePublicId = await PublicIdOf("cases", s.RegisteredCaseId);
        var body = new
        {
            primaryUserPublicId = await PublicIdOf("users", s.User("avokat1").Id),
            rowVersion = await RowVersion(s.RegisteredCaseId)
        };

        using var lawyer = factory.CreateClient().AsUser(Avokat1);
        (await lawyer.GetAsync(AssignablePath(casePublicId), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await lawyer.PostAsJsonAsync(AssignPath(casePublicId), body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Json(await lawyer.GetAsync("/api/cases/filter-options", Ct))).GetProperty("lawyers").GetArrayLength().ShouldBe(0);
        // also on the case the lawyer works on: assigning is the Kryetar's
        var active = await PublicIdOf("cases", s.ActiveCaseId);
        (await lawyer.GetAsync(AssignablePath(active), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var drKryetar = factory.CreateClient().AsUser($"drkryetar-{seed.Suffix}@ash.test");
        var notFound = await drKryetar.GetAsync(AssignablePath(casePublicId), Ct);
        notFound.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemCode(notFound)).ShouldBe("case.not_found");
        (await drKryetar.PostAsJsonAsync(AssignPath(casePublicId), body, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await drKryetar.PostAsJsonAsync(AssignPath(Guid.NewGuid()), body, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // the DR Kryetar's office list has the DR lawyer, not TR's
        var drOffice = (await Json(await drKryetar.GetAsync("/api/cases/filter-options", Ct))).GetProperty("lawyers").EnumerateArray()
            .Select(x => x.GetProperty("publicId").GetGuid()).ToList();
        drOffice.ShouldContain(await PublicIdOf("users", s.User("drlawyer").Id));
        drOffice.ShouldNotContain(await PublicIdOf("users", s.User("avokat1").Id));

        (await Scalar($"SELECT COUNT(*) FROM case_assignments WHERE case_id = {s.RegisteredCaseId}")).ShouldBe(0L);
    }

    [Fact]
    public async Task Closed_case_cannot_be_assigned()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var closed = await CaseAsync(s, "CLOSED");
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser(Kryetar);

        var response = await kryetar.PostAsJsonAsync(AssignPath(await PublicIdOf("cases", closed)), new
        {
            primaryUserPublicId = await PublicIdOf("users", s.User("avokat1").Id),
            rowVersion = await RowVersion(closed)
        }, Ct);

        // not REGISTERED (the state rule is checked before the permission, task 029): the specific 409
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemCode(response)).ShouldBe("case.already_assigned");
        (await Scalar($"SELECT COUNT(*) FROM case_assignments WHERE case_id = {closed}")).ShouldBe(0L);
    }

    [Fact]
    public async Task Two_parallel_assignments_give_one_200_and_one_409()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var recorder = new AssignedRecorder();
        await using var factory = FactoryRecording(recorder);
        var path = AssignPath(await PublicIdOf("cases", s.RegisteredCaseId));
        var rowVersion = await RowVersion(s.RegisteredCaseId);
        var bodies = new[]
        {
            new { primaryUserPublicId = await PublicIdOf("users", s.User("avokat1").Id), rowVersion },
            new { primaryUserPublicId = await PublicIdOf("users", s.User("avokat2").Id), rowVersion }
        };
        using var a = factory.CreateClient().AsUser(Kryetar);
        using var b = factory.CreateClient().AsUser(Kryetar);

        var responses = await Task.WhenAll(a.PostAsJsonAsync(path, bodies[0], Ct), b.PostAsJsonAsync(path, bodies[1], Ct));

        responses.Select(r => r.StatusCode).OrderBy(x => x).ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        (await ProblemCode(responses.Single(r => r.StatusCode == HttpStatusCode.Conflict))).ShouldBe("case.already_assigned");
        (await Scalar($"SELECT COUNT(*) FROM case_assignments WHERE case_id = {s.RegisteredCaseId}")).ShouldBe(1L);
        (await Scalar($"SELECT COUNT(*) FROM case_history_events WHERE case_id = {s.RegisteredCaseId}")).ShouldBe(1L);
        (await Scalar($"SELECT row_version FROM cases WHERE id = {s.RegisteredCaseId}")).ShouldBe(rowVersion + 1);
        recorder.Events.Count.ShouldBe(1);
    }
}
