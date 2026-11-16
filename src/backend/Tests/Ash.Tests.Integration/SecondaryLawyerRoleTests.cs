using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 053i — "Avokat sekondar" (SECONDARY_LAWYER) is a system role of its own, next to "Avokat Shteti (primar)"
/// (STATE_LAWYER). The pickers and the position checks are in <see cref="AssignLawyersTests"/> and
/// <see cref="TransferLawyerTests"/>; here: what a secondary-only user sees, and the role-removal guard.
/// Uses <see cref="CaseSeed"/> (sekondar@ash.local: SECONDARY_LAWYER only; avokat2: both).
/// </summary>
public class SecondaryLawyerRoleTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Admin = "admin@ash.local";
    private const string Sekondar = "sekondar@ash.local";

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

    /// <summary>The seeded ACTIVE case gets sekondar as its current secondary instead of avokat2.</summary>
    private Task MakeSekondarTheSecondaryAsync(CaseSeed s) => Scalar($"""
        UPDATE case_assignments SET user_id = {s.User("sekondar").Id}
        WHERE case_id = {s.ActiveCaseId} AND case_role = 'SECONDARY_LAWYER' AND ended_at IS NULL
        """);

    private Task RestoreSecondaryRoleAsync(long userId) => Scalar($"""
        INSERT IGNORE INTO user_roles (user_id, role_id) SELECT {userId}, id FROM roles WHERE code = 'SECONDARY_LAWYER'
        """);

    [Fact]
    public async Task Secondary_only_user_gets_403_on_statistics_and_200_on_reports_with_their_case()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await MakeSekondarTheSecondaryAsync(s);
        var caseCode = (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {s.ActiveCaseId}"))!;
        await using var factory = new AshApiFactory(Db);
        using var sekondar = factory.CreateClient().AsUser(Sekondar);
        using var both = factory.CreateClient().AsUser("avokat2@ash.local");

        (await sekondar.GetAsync("/api/reports/statistics", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await sekondar.GetAsync("/api/reports/statistics?table=5", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        // holding STATE_LAWYER too: statistics allowed (counted as primary only)
        (await both.GetAsync("/api/reports/statistics", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var report = await sekondar.GetAsync("/api/reports/active-cases?limit=100", Ct);
        report.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(report)).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("caseCode").GetString()).ShouldContain(caseCode);
    }

    [Fact]
    public async Task Secondary_only_user_has_my_cases_and_the_secondary_widget_without_statistics()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await MakeSekondarTheSecondaryAsync(s);
        var caseCode = (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {s.ActiveCaseId}"))!;
        await using var factory = new AshApiFactory(Db);
        using var sekondar = factory.CreateClient().AsUser(Sekondar);

        var mine = await sekondar.GetAsync("/api/cases?tab=mine&limit=100", Ct);
        mine.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(mine)).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("caseCode").GetString()).ShouldContain(caseCode);

        var widgets = (await Json(await sekondar.GetAsync("/api/dashboard", Ct))).GetProperty("widgets").EnumerateArray()
            .ToDictionary(w => w.GetProperty("code").GetString()!, w => w.Clone());
        widgets["mineSecondary"].GetProperty("value").GetInt64().ShouldBe(1);
        widgets.ShouldContainKey("mineHearingsThisWeek");
        widgets.ShouldContainKey("mineHearingOverdue");
        widgets.Keys.ShouldNotContain(k => k.StartsWith("stat", StringComparison.OrdinalIgnoreCase));

        // a STATE_LAWYER-only lawyer does not get the secondary widget
        using var avokat1 = factory.CreateClient().AsUser("avokat1@ash.local");
        (await Json(await avokat1.GetAsync("/api/dashboard", Ct))).GetProperty("widgets").EnumerateArray()
            .Select(w => w.GetProperty("code").GetString()).ShouldNotContain("mineSecondary");
    }

    [Fact]
    public async Task Lawyer_role_cannot_be_removed_while_its_position_is_held_on_open_cases()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await MakeSekondarTheSecondaryAsync(s);
        await using var factory = new AshApiFactory(Db);
        using var admin = factory.CreateClient().AsUser(Admin);
        var sekondar = await PublicIdOf("users", s.User("sekondar").Id);
        var avokat1 = await PublicIdOf("users", s.User("avokat1").Id);

        async Task<string> Refused(Guid user, string role)
        {
            var response = await admin.DeleteAsync($"/api/identity/admin/users/{user}/roles/{role}", Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var body = await Json(response);
            body.GetProperty("code").GetString().ShouldBe("user.role_in_use");
            return body.GetProperty("title").GetString()!;
        }

        (await Refused(sekondar, "SECONDARY_LAWYER")).ShouldContain("avokat sekondar në 1 çështje");
        (await Refused(avokat1, "STATE_LAWYER")).ShouldContain("avokat primar në 1 çështje");

        // avokat2 is no longer secondary anywhere: their SECONDARY_LAWYER may go (and is given back for the next tests)
        var avokat2 = await PublicIdOf("users", s.User("avokat2").Id);
        try
        {
            (await admin.DeleteAsync($"/api/identity/admin/users/{avokat2}/roles/SECONDARY_LAWYER", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

            // once the case is closed, the position no longer blocks
            await Scalar($"UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = UTC_TIMESTAMP(3), closed_by = {s.User("kryetar").Id} WHERE id = {s.ActiveCaseId}");
            (await admin.DeleteAsync($"/api/identity/admin/users/{sekondar}/roles/SECONDARY_LAWYER", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            // the local seed (LocalScripts/907) roles are shared by every test: give them back
            await RestoreSecondaryRoleAsync(s.User("avokat2").Id);
            await RestoreSecondaryRoleAsync(s.User("sekondar").Id);
        }

        (await Scalar($"SELECT COUNT(*) FROM user_roles ur JOIN roles r ON r.id = ur.role_id WHERE ur.user_id = {s.User("sekondar").Id} AND r.code = 'SECONDARY_LAWYER'"))
            .ShouldBe(1L);
    }
}
