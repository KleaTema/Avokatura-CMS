using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.SharedKernel.Events;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Time;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>Task 016 — Protokoll registers a summons (case X/1). Business tables are emptied before each test.</summary>
public class RegisterCaseTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Path = "/api/cases/register";
    private const string Protokoll = "protokoll@ash.local";
    private const string TiraneFirstInstance = "Gjykata e Shkallës së Parë Tiranë";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Dictionary<string, object?>> ValidBody(string? judgeLast = "Gjyqtari", string? judgeFirst = "Ilir")
    {
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var court = await seed.CourtAsync(TiraneFirstInstance);
        var judge = await seed.JudgeAsync(judgeFirst!, judgeLast!);
        var today = TiraneTime.Today(new SystemClock());
        return new Dictionary<string, object?>
        {
            ["summonsNumber"] = $"FL-{Guid.NewGuid():N}"[..20],
            ["summonsTypeCode"] = null,
            ["actNumber"] = $"AKT-{Guid.NewGuid():N}"[..20],
            ["actDate"] = today.AddDays(-10).ToString("yyyy-MM-dd"),
            ["receivedDate"] = today.ToString("yyyy-MM-dd"),
            ["courtLevelCode"] = "FIRST_INSTANCE",
            ["courtId"] = court,
            ["judgeId"] = judge,
            ["objectText"] = "Kërkesë për shpërblim dëmi",
            ["firstHearing"] = new Dictionary<string, object?>
            {
                ["at"] = DateTime.UtcNow.AddDays(14).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["hearingTypeCode"] = null,
                ["otherTypeText"] = null
            }
        };
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    private async Task<long> Count(string table) => Convert.ToInt64(await Scalar($"SELECT COUNT(*) FROM {table}"));

    [Fact]
    public async Task Protokoll_registers_a_case_with_its_first_hearing_and_history()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var body = await ValidBody();

        var response = await client.PostAsJsonAsync(Path, body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await Json(response);
        var caseCode = created.GetProperty("caseCode").GetString()!;
        caseCode.ShouldMatch(@"^\d{4}-\d{6}/1$");
        created.GetProperty("warnings").GetArrayLength().ShouldBe(0);

        var caseId = Convert.ToInt64(await Scalar($"SELECT id FROM cases WHERE case_code = '{caseCode}'"));
        (await Scalar($"SELECT lifecycle_status FROM cases WHERE id = {caseId}")).ShouldBe("REGISTERED");
        (await Scalar($"SELECT level_seq FROM cases WHERE id = {caseId}")).ShouldBe((ushort)1);
        (await Scalar($"SELECT o.code FROM cases c JOIN offices o ON o.id = c.office_id WHERE c.id = {caseId}")).ShouldBe("TR");
        (await Scalar($"SELECT u.email FROM cases c JOIN users u ON u.id = c.registered_by WHERE c.id = {caseId}")).ShouldBe(Protokoll);
        (await Scalar($"SELECT DATE_FORMAT(received_date, '%Y-%m-%d') FROM cases WHERE id = {caseId}")).ShouldBe(body["receivedDate"]);
        (await Scalar($"SELECT CONCAT(d.unique_code, '/1') = c.case_code FROM cases c JOIN dossiers d ON d.id = c.dossier_id WHERE c.id = {caseId}"))
            .ShouldBe(1L);

        (await Scalar($"SELECT COUNT(*) FROM case_hearings WHERE case_id = {caseId} AND status = 'SCHEDULED'")).ShouldBe(1L);
        (await Scalar($"SELECT ht.code FROM case_hearings h JOIN hearing_types ht ON ht.id = h.hearing_type_id WHERE h.case_id = {caseId}"))
            .ShouldBe("PERGATITORE");
        (await Scalar($"SELECT c.next_hearing_at = h.scheduled_at FROM cases c JOIN case_hearings h ON h.case_id = c.id WHERE c.id = {caseId}"))
            .ShouldBe(1L);

        var events = await Scalar($"""
            SELECT GROUP_CONCAT(event_type ORDER BY id) FROM case_history_events
            WHERE dossier_id = (SELECT dossier_id FROM cases WHERE id = {caseId})
            """);
        events.ShouldBe("DOSSIER_CREATED,CASE_REGISTERED,SENT_TO_HEAD");
        ((string)(await Scalar($"SELECT summary FROM case_history_events WHERE case_id = {caseId} AND event_type = 'SENT_TO_HEAD'"))!)
            .ShouldContain("Kryetar");
    }

    [Theory]
    [InlineData("summonsNumber")]
    [InlineData("actNumber")]
    [InlineData("receivedDate")]
    [InlineData("courtLevelCode")]
    [InlineData("courtId")]
    [InlineData("judgeId")]
    [InlineData("objectText")]
    [InlineData("firstHearing")]
    [InlineData("firstHearing.at")]
    public async Task Missing_required_field_returns_400_with_that_field(string field)
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var body = await ValidBody();
        if (field == "firstHearing.at")
        {
            ((Dictionary<string, object?>)body["firstHearing"]!)["at"] = null;
        }
        else
        {
            body[field] = null;
        }

        var response = await client.PostAsJsonAsync(Path, body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await Json(response)).GetProperty("errors");
        errors.TryGetProperty(field, out var messages).ShouldBeTrue(field);
        messages[0].GetString().ShouldNotBeNullOrWhiteSpace();
        (await Count("dossiers")).ShouldBe(0);
    }

    [Fact]
    public async Task Missing_judge_message_tells_to_ask_the_administrator()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var body = await ValidBody();
        body["judgeId"] = 999999;

        var response = await client.PostAsJsonAsync(Path, body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(response)).GetProperty("errors").GetProperty("judgeId")[0].GetString()!
            .ShouldContain("kërkoni administratorit ta shtojë");
    }

    [Fact]
    public async Task Received_date_tomorrow_in_Tirane_and_hearing_before_the_act_are_rejected()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var body = await ValidBody();
        var today = TiraneTime.Today(new SystemClock());
        body["receivedDate"] = today.AddDays(1).ToString("yyyy-MM-dd");
        body["actDate"] = today.ToString("yyyy-MM-dd");
        ((Dictionary<string, object?>)body["firstHearing"]!)["at"] = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ssZ");

        var response = await client.PostAsJsonAsync(Path, body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await Json(response)).GetProperty("errors");
        errors.TryGetProperty("receivedDate", out _).ShouldBeTrue();
        errors.TryGetProperty("firstHearing.at", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Hearing_without_time_zone_is_rejected_and_past_hearing_is_only_a_warning()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);

        var noZone = await ValidBody();
        ((Dictionary<string, object?>)noZone["firstHearing"]!)["at"] = "2030-01-15T09:30:00";
        var rejected = await client.PostAsJsonAsync(Path, noZone, Ct);
        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(rejected)).GetProperty("errors").TryGetProperty("firstHearing.at", out _).ShouldBeTrue();

        var late = await ValidBody();
        ((Dictionary<string, object?>)late["firstHearing"]!)["at"] = DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var accepted = await client.PostAsJsonAsync(Path, late, Ct);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Json(accepted)).GetProperty("warnings").EnumerateArray()
            .ShouldContain(w => w.GetProperty("code").GetString() == "hearing.in_past");
    }

    [Fact]
    public async Task Court_of_another_level_is_rejected_with_400()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var body = await ValidBody();
        body["courtId"] = await seed.CourtAsync("Gjykata e Apelit Tiranë");

        var response = await client.PostAsJsonAsync(Path, body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(response)).GetProperty("errors").TryGetProperty("courtId", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Judge_of_another_court_is_registered_with_a_warning()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var body = await ValidBody(judgeLast: "Hoxha", judgeFirst: "Besnik"); // listed at Durrës

        var response = await client.PostAsJsonAsync(Path, body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Json(response)).GetProperty("warnings").EnumerateArray()
            .ShouldContain(w => w.GetProperty("code").GetString() == "judge.court_mismatch");
    }

    [Fact]
    public async Task Office_without_head_is_refused_before_any_dossier_is_created()
    {
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var office = await seed.OfficeAsync("Zyra pa Kryetar", "LOCAL");
        await seed.UserAsync("protnohead", "Protokoll", "PaKryetar", office, null, "PROTOCOL");
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser($"protnohead-{seed.Suffix}@ash.test");

        var response = await client.PostAsJsonAsync(Path, await ValidBody(), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await Json(response);
        problem.GetProperty("code").GetString().ShouldBe("case.office_without_head");
        problem.GetProperty("title").GetString().ShouldBe("Zyra nuk ka Kryetar. Kontaktoni administratorin.");
        (await Count("dossiers")).ShouldBe(0);
        (await Count("cases")).ShouldBe(0);
    }

    private sealed class FailingCaseRegisteredHandler : IEventHandler<CaseRegistered>
    {
        public Task HandleAsync(CaseRegistered @event, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("forced failure after create_dossier");
    }

    [Fact]
    public async Task Failure_after_create_dossier_rolls_everything_back()
    {
        // the event is published last, after create_dossier, the case, the hearing and the history rows
        await using var factory = new AshApiFactory(Db, configureServices: services =>
            services.AddScoped<IEventHandler<CaseRegistered>, FailingCaseRegisteredHandler>());
        using var client = factory.CreateClient().AsUser(Protokoll);

        var response = await client.PostAsJsonAsync(Path, await ValidBody(), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await Count("dossiers")).ShouldBe(0);
        (await Count("cases")).ShouldBe(0);
        (await Count("case_hearings")).ShouldBe(0);
        (await Count("case_history_events")).ShouldBe(0);
    }

    [Fact]
    public async Task Same_summons_and_court_is_a_possible_duplicate_until_confirmed()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var body = await ValidBody();
        var first = await client.PostAsJsonAsync(Path, body, Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var firstCode = (await Json(first)).GetProperty("caseCode").GetString();
        body["actNumber"] = $"AKT-{Guid.NewGuid():N}"[..20]; // only summons + court match

        var duplicate = await client.PostAsJsonAsync(Path, body, Ct);

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await Json(duplicate);
        problem.GetProperty("code").GetString().ShouldBe("case.possible_duplicate");
        var match = problem.GetProperty("matches").EnumerateArray().ShouldHaveSingleItem();
        match.GetProperty("caseCode").GetString().ShouldBe(firstCode);
        match.GetProperty("status").GetString().ShouldBe("REGISTERED");
        match.GetProperty("courtName").GetString().ShouldBe(TiraneFirstInstance);
        problem.GetProperty("hiddenCount").GetInt32().ShouldBe(0);
        (await Count("dossiers")).ShouldBe(1);

        body["confirmPossibleDuplicate"] = true;
        var confirmed = await client.PostAsJsonAsync(Path, body, Ct);

        confirmed.StatusCode.ShouldBe(HttpStatusCode.Created);
        var code = (await Json(confirmed)).GetProperty("caseCode").GetString();
        var details = (string)(await Scalar($"""
            SELECT JSON_EXTRACT(h.details, '$.new.possibleDuplicateConfirmed') FROM case_history_events h
            JOIN cases c ON c.id = h.case_id WHERE c.case_code = '{code}' AND h.event_type = 'CASE_REGISTERED'
            """))!;
        details.ShouldBe("true");
    }

    [Fact]
    public async Task User_without_case_create_gets_403()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");

        var response = await client.PostAsJsonAsync(Path, await ValidBody(), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Count("dossiers")).ShouldBe(0);
    }

    private sealed class FixedCurrentUser(long userId, long officeId) : ICurrentUser
    {
        public long UserId => userId;
        public Guid PublicId => Guid.Empty;
        public long? OfficeId => officeId;
        public long? InstitutionId => null;
        public IReadOnlyCollection<string> Roles => ["PROTOCOL"];
        public bool HasPermission(string permission) => false;
    }

    [Fact]
    public async Task Protokoll_of_another_office_cannot_see_the_registered_case()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var response = await client.PostAsJsonAsync(Path, await ValidBody(), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var caseCode = (await Json(response)).GetProperty("caseCode").GetString();
        var caseId = Convert.ToInt64(await Scalar($"SELECT id FROM cases WHERE case_code = '{caseCode}'"));

        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var otherOffice = await seed.OfficeAsync("Zyra tjetër", "LOCAL");
        var otherProtokoll = await seed.UserAsync("protother", "Protokoll", "Tjetër", otherOffice, null, "PROTOCOL");
        var ownProtokoll = Convert.ToInt64(await Scalar($"SELECT id FROM users WHERE email = '{Protokoll}'"));
        var ownOffice = Convert.ToInt64(await Scalar("SELECT id FROM offices WHERE code = 'TR'"));

        async Task<bool> CanView(long userId, long officeId)
        {
            var services = new ServiceCollection();
            Ash.Infrastructure.InfrastructureServiceCollectionExtensions.AddAshInfrastructure(services, Db.AppConnectionString);
            Ash.Modules.Cases.CasesModule.AddCasesModule(services, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
            services.AddScoped<ICurrentUser>(_ => new FixedCurrentUser(userId, officeId));
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ICaseAccessService>().CanViewAsync(caseId, Ct);
        }

        (await CanView(otherProtokoll, otherOffice)).ShouldBeFalse();
        (await CanView(ownProtokoll, ownOffice)).ShouldBeTrue();
    }
}
