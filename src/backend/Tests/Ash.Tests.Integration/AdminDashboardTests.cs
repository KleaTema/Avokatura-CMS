using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 053n — the administrator's dashboard: administration widgets only (no case or office numbers, no office
/// needed); "Ndryshimet e fundit administrative" from ash_audit.v_admin_changes with links to the changed user /
/// office / institution and plain text for the rest; "Importet me gabime" from the import history. Each count = the
/// filtered list it opens (DashboardTests checks every card).
/// </summary>
public class AdminDashboardTests(MySqlFixture db) : IntegrationTest(db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<Dictionary<string, JsonElement>> Widgets(HttpClient client) =>
        (await Json(await client.GetAsync("/api/dashboard", Ct))).GetProperty("widgets").EnumerateArray()
            .ToDictionary(w => w.GetProperty("code").GetString()!);

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
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

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private async Task WaitForChanges(string where, int count)
    {
        var deadline = DateTime.UtcNow + WaitLimit;
        while (true)
        {
            await using var connection = new MySqlConnection(Db.AppConnectionString);
            await connection.OpenAsync(Ct);
            await using var command = new MySqlCommand($"SELECT COUNT(*) FROM ash_audit.v_admin_changes WHERE {where}", connection);
            if (Convert.ToInt64(await command.ExecuteScalarAsync(Ct)) >= count)
            {
                return;
            }

            DateTime.UtcNow.ShouldBeLessThan(deadline, $"no {count} admin changes where {where}");
            await Task.Delay(100, Ct);
        }
    }

    [Fact]
    public async Task An_administrator_without_an_office_gets_only_administration_widgets()
    {
        // the users table is not reset between tests: the admin's office is put back afterwards
        var office = await Scalar("SELECT office_id FROM users WHERE email = 'admin@ash.local'");
        await Execute("UPDATE users SET office_id = NULL WHERE email = 'admin@ash.local'");
        try
        {
            await using var factory = new AshApiFactory(Db);
            using var admin = factory.CreateClient().AsUser("admin@ash.local");

            var widgets = await Widgets(admin);

            widgets.Values.ShouldAllBe(w => Text(w, "error") == null);
            widgets.Keys.ShouldAllBe(code => code.StartsWith("admin", StringComparison.Ordinal));
            widgets.Keys.ShouldNotContain("allByOffice");
            widgets.Keys.ShouldNotContain("adminJobs");
            widgets.Keys.ShouldNotContain("adminPartitions");
            // every link opens an admin page, never the case list
            widgets.Values.Select(w => Text(w, "linkPath")).Where(l => l != null).ShouldAllBe(l => l!.StartsWith("/admin/", StringComparison.Ordinal));
        }
        finally
        {
            await Execute($"UPDATE users SET office_id = {(office is null ? "NULL" : Convert.ToString(office))} WHERE email = 'admin@ash.local'");
        }
    }

    [Fact]
    public async Task Recent_changes_link_a_user_to_its_filtered_page_and_show_a_court_as_plain_text()
    {
        await using var factory = new AshApiFactory(Db);
        using var admin = factory.CreateClient().AsUser("admin@ash.local");
        var avokat2 = (await Json(await admin.GetAsync("/api/identity/admin/users?q=avokat2&limit=1", Ct))).GetProperty("items")[0];
        var publicId = avokat2.GetProperty("publicId").GetGuid();

        // the users and reference tables are not reset between tests: both are activated again afterwards
        try
        {
            await AssertRecentChanges(admin, publicId);
        }
        finally
        {
            await admin.PostAsync($"/api/identity/admin/users/{publicId}/activate", null, Ct);
            await admin.PostAsync("/api/reference/admin/courts/1/activate", null, Ct);
        }
    }

    private async Task AssertRecentChanges(HttpClient admin, Guid publicId)
    {
        var before = Convert.ToInt64(await Scalar("SELECT COALESCE(MAX(id), 0) FROM ash_audit.v_admin_changes"));
        (await admin.PostAsync($"/api/identity/admin/users/{publicId}/deactivate", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PostAsync("/api/reference/admin/courts/1/deactivate", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        // the audit writer inserts in batches (every 250 ms): wait, with a limit, until both new rows are in the view
        await WaitForChanges($"id > {before} AND entity_type IN ('USER', 'COURT')", 2);

        var changes = (await Widgets(admin))["adminRecentChanges"].GetProperty("items").EnumerateArray().ToList();
        changes.Count.ShouldBeGreaterThanOrEqualTo(2);
        changes.Count.ShouldBeLessThanOrEqualTo(10);

        // newest first: the court, then the user
        var court = changes[0];
        Text(court, "label")!.ShouldStartWith("Gjykata ");
        Text(court, "label")!.ShouldEndWith("u çaktivizua");
        Text(court, "linkPath").ShouldBeNull();
        Text(court, "detail").ShouldBe("Admin Dev");

        var user = changes[1];
        Text(user, "label").ShouldBe("Përdoruesi Avokat2 Dev u çaktivizua");
        Text(user, "linkPath").ShouldBe($"/admin/users?userId={publicId}");

        // the link opens exactly that user — now inactive — whatever the list's other filters
        var opened = await Json(await admin.GetAsync($"/api/identity/admin/users?userId={publicId}", Ct));
        opened.GetProperty("total").GetInt32().ShouldBe(1);
        opened.GetProperty("items")[0].GetProperty("isActive").GetBoolean().ShouldBeFalse();

        // no internal id reaches the browser
        JsonAssert.NoNumericIds(await Json(await admin.GetAsync("/api/dashboard", Ct)));
    }

    [Fact]
    public async Task Imports_with_errors_are_counted_and_each_opens_its_history()
    {
        await using var factory = new AshApiFactory(Db);
        using var admin = factory.CreateClient().AsUser("admin@ash.local");
        const string csv = """
            email;emri;mbiemri;zyra_kodi;institucioni;rolet;pozicioni;aktiv;perditeso
            jo-email;Agim;Hoxha;TR;;STATE_LAWYER;;;
            """;
        var upload = await ImportFiles.Upload(admin, "/api/identity/admin/imports/users", "gabime.csv", Encoding.UTF8.GetBytes(csv), Ct);
        upload.StatusCode.ShouldBe(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync(Ct));

        var widget = (await Widgets(admin))["adminImportErrors"];
        var items = widget.GetProperty("items").EnumerateArray().ToList();
        // newest first: this one on top
        var row = items[0];
        Text(row, "label").ShouldBe("gabime.csv");
        Text(row, "detail").ShouldBe("Përdoruesit");
        row.GetProperty("value").GetInt64().ShouldBe(1);
        Text(row, "linkPath").ShouldBe("/admin/users?import=users&impErrors=true");

        // the count = the imports with errors in the histories the rows open (each page's "Me gabime")
        async Task<int> WithErrors(string kind) =>
            (await Json(await admin.GetAsync($"/api/identity/admin/imports/{kind}", Ct))).EnumerateArray()
                .Count(i => i.GetProperty("summary").GetProperty("errors").GetInt32() > 0);
        widget.GetProperty("value").GetInt64().ShouldBe(await WithErrors("users") + await WithErrors("organika"));
        items.Count.ShouldBeLessThanOrEqualTo(8);
    }
}
