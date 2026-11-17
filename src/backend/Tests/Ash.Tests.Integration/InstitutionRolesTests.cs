using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.SharedKernel.Ids;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 053k — the institution roles beyond the permission matrix (PermissionsMatrixTests): the names and scopes the
/// administrator grants, "Institucioni" assigns jurists on MONITORING cases only, "Institucioni me delegim" lists the
/// institution's cases.
/// </summary>
public class InstitutionRolesTests(MySqlFixture db) : IntegrationTest(db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    [Fact]
    public async Task The_administrator_grants_the_office_and_the_institution_roles_by_their_names()
    {
        await using var factory = new AshApiFactory(Db);
        using var admin = factory.CreateClient().AsUser("admin@ash.local");

        var roles = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/admin/roles", Ct)).EnumerateArray()
            .ToDictionary(r => r.GetProperty("code").GetString()!, r => $"{r.GetProperty("scope").GetString()}|{r.GetProperty("name").GetString()}");

        roles["STATE_LAWYER"].ShouldBe("OFFICE|Avokat Shteti (primar)");
        roles["SECONDARY_LAWYER"].ShouldBe("OFFICE|Avokat sekondar");
        roles["INSTITUTION_USER"].ShouldBe("INSTITUTION|Institucioni");
        roles["INSTITUTION_JURIST"].ShouldBe("INSTITUTION|Juristi i institucionit");
        roles["INSTITUTION_DELEGATE"].ShouldBe("INSTITUTION|Institucioni me delegim");
    }

    [Fact]
    public async Task Institucioni_assigns_a_jurist_on_a_monitoring_case_but_not_on_a_delegated_one()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var (delegated, _) = await seed.AddOfficeCaseAsync(seed.OfficeTrId, "jd", seed.User("avokat1").Id);
        await Scalar($"""
            INSERT INTO case_institutions (case_id, institution_id, link_mode, started_by)
            VALUES ({delegated}, {seed.User("institucion").InstitutionId}, 'DELEGATED', {seed.User("kryetar").Id})
            """);
        await using var factory = new AshApiFactory(Db);
        using var institution = factory.CreateClient().AsUser("institucion@ash.local");
        var monitoredId = PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM cases WHERE id = {seed.ActiveCaseId}"))!);
        var delegatedId = PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM cases WHERE id = {delegated}"))!);

        (await institution.GetAsync($"/api/cases/{monitoredId}/assignable-jurists", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        // the delegated case is visible (view only) but has no jurist to assign: the action is not the institution's
        (await institution.GetAsync($"/api/cases/{delegatedId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await institution.GetAsync($"/api/cases/{delegatedId}/assignable-jurists", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var rowVersion = Convert.ToUInt32(await Scalar($"SELECT row_version FROM cases WHERE id = {delegated}"));
        var jurist = PublicId.FromBytes((byte[])(await Scalar("SELECT public_id FROM users WHERE email = 'jurist@ash.local'"))!);
        (await institution.PostAsJsonAsync($"/api/cases/{delegatedId}/jurist", new { userPublicId = jurist, rowVersion }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Institucioni_me_delegim_lists_the_institutions_cases_under_its_tab()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);
        using var delegate_ = factory.CreateClient().AsUser("delegim@ash.local");

        var tabs = (await delegate_.GetFromJsonAsync<JsonElement>("/api/cases/tabs", Ct)).GetProperty("tabs").EnumerateArray()
            .Select(t => t.GetProperty("tab").GetString()).ToList();
        tabs.ShouldContain("institution");
        var codes = (await delegate_.GetFromJsonAsync<JsonElement>("/api/cases?tab=institution&status=ALL", Ct)).GetProperty("items")
            .EnumerateArray().Select(i => i.GetProperty("caseCode").GetString()).ToList();
        codes.ShouldContain((string)(await Scalar($"SELECT case_code FROM cases WHERE id = {seed.ActiveCaseId}"))!);
    }
}
