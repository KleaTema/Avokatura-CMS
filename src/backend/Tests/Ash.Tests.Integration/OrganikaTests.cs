using System.Net;
using System.Text;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Tasks 053g / 053l — "Organika" on the shared import pipeline: the definition and the template (with the "Udhëzime"
/// sheet), the upload → preview WITHOUT writing, "Apliko" in one transaction with a history row per user, the result
/// report, every import kept.
/// </summary>
public class OrganikaTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Admin = "admin@ash.local";
    private const string Base = "/api/identity/admin/imports/organika";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(Ct);
        return value is DBNull ? null : value;
    }

    private static Task<HttpResponseMessage> Upload(HttpClient client, string fileName, byte[] content) =>
        ImportFiles.Upload(client, Base, fileName, content, Ct);

    private static Task<JsonElement> Json(HttpResponseMessage response) => ImportFiles.Json(response, Ct);

    /// <summary>
    /// avokat1 changes; avokat2 is already as written (unchanged); an unknown e-mail; ndihmes twice (duplicate); an
    /// unknown manager (protokoll); a row without position. Headers with accents, case and a "/zyra" suffix.
    /// </summary>
    private const string Csv = """
        Email;Pozicioni;Njësia/zyra;Eprori email
        AVOKAT1@ash.local;Avokat i Shtetit, specialist;Sektori i Gjyqësorit;kryetar@ash.local
        avokat2@ash.local;Avokat;Sektori A;
        nuk.ekziston@ash.local;Avokat;Sektori A;
        ndihmes@ash.local;Ndihmës ligjor;Sektori A;
        ndihmes@ash.local;Ndihmës ligjor;Sektori B;
        protokoll@ash.local;Specialist protokolli;Protokolli;askush@ash.local
        monitor@ash.local;;Sektori C;
        """;

    private static string ErrorOf(JsonElement row, string column) =>
        row.GetProperty("errors").EnumerateArray().Single(e => e.GetProperty("column").GetString() == column).GetProperty("message").GetString()!;

    [Fact]
    public async Task The_upload_shows_the_preview_and_writes_nothing_then_Apliko_writes_it_atomically_with_history()
    {
        await Scalar("UPDATE users SET position = 'Avokat', org_unit = 'Sektori A' WHERE email = 'avokat2@ash.local'");
        await using var factory = new AshApiFactory(Db);
        using var admin = factory.CreateClient().AsUser(Admin);

        var uploaded = await Upload(admin, "organika.csv", Encoding.UTF8.GetBytes(Csv));
        uploaded.StatusCode.ShouldBe(HttpStatusCode.Created, await uploaded.Content.ReadAsStringAsync(Ct));
        var preview = await Json(uploaded);
        var summary = preview.GetProperty("summary");
        (summary.GetProperty("total").GetInt32(), summary.GetProperty("changed").GetInt32(), summary.GetProperty("unchanged").GetInt32(),
                summary.GetProperty("errors").GetInt32())
            .ShouldBe((7, 1, 1, 5));
        var rows = preview.GetProperty("rows").EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("status").GetString())
            .ShouldBe(["CHANGED", "UNCHANGED", "ERROR", "ERROR", "ERROR", "ERROR", "ERROR"]);
        rows[0].GetProperty("line").GetInt32().ShouldBe(2);
        rows[0].GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("column").GetString() == "eprori_email")
            .GetProperty("new").GetString().ShouldBe("Kryetar Dev");
        ErrorOf(rows[2], "email").ShouldBe("Nuk ka përdorues me këtë e-mail.");
        ErrorOf(rows[3], "email").ShouldBe("Ky e-mail është më shumë se një herë në skedar.");
        ErrorOf(rows[5], "eprori_email").ShouldStartWith("Eprori nuk është përdorues");
        ErrorOf(rows[6], "pozicioni").ShouldBe("Mungon pozicioni.");

        // nothing written yet but the import itself (kind, file)
        (await Scalar("SELECT position FROM users WHERE email = 'avokat1@ash.local'")).ShouldBeNull();
        (await Scalar("SELECT CONCAT(kind, '|', file_name, '|', LENGTH(file_content) > 0, '|', applied_at IS NULL) FROM org_imports"))
            .ShouldBe("ORGANIKA|organika.csv|1|1");

        var id = preview.GetProperty("publicId").GetGuid();
        var applied = await Json(await admin.PostAsync($"{Base}/{id}/apply", null, Ct));
        applied.GetProperty("appliedCount").GetInt32().ShouldBe(1);

        (await Scalar("SELECT CONCAT(u.position, '|', u.org_unit, '|', m.email) FROM users u JOIN users m ON m.id = u.manager_user_id WHERE u.email = 'avokat1@ash.local'"))
            .ShouldBe("Avokat i Shtetit, specialist|Sektori i Gjyqësorit|kryetar@ash.local");
        (await Scalar("SELECT COUNT(*) FROM user_org_history")).ShouldBe(1L); // only the changed row
        (await Scalar("SELECT position FROM users WHERE email = 'ndihmes@ash.local'")).ShouldBeNull(); // duplicates never

        // the result report: every row with its status and message
        var report = await admin.GetAsync($"{Base}/{id}/report", Ct);
        report.Content.Headers.ContentType!.MediaType.ShouldBe(ImportFiles.XlsxType);
        var table = ImportFiles.ReadSheet(await report.Content.ReadAsByteArrayAsync(Ct));
        table.Count.ShouldBe(8);
        table[1][3].ShouldBe("Ndryshon");
        table[1][5].ShouldBe("U përditësua.");
        table[3][3].ShouldBe("Gabim");

        // once only; kept in the list; re-importing the same file changes nothing
        (await admin.PostAsync($"{Base}/{id}/apply", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(await admin.GetAsync(Base, Ct))).EnumerateArray().Single().GetProperty("appliedCount").GetInt32().ShouldBe(1);
        var again = await Json(await Upload(admin, "organika.csv", Encoding.UTF8.GetBytes(Csv)));
        again.GetProperty("summary").GetProperty("changed").GetInt32().ShouldBe(0);
        again.GetProperty("summary").GetProperty("unchanged").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task The_definition_and_the_template_are_one_and_an_xlsx_and_a_comma_csv_read_alike()
    {
        await using var factory = new AshApiFactory(Db);
        using var admin = factory.CreateClient().AsUser(Admin);

        var definition = await Json(await admin.GetAsync($"{Base}/definition", Ct));
        var columns = definition.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();
        columns.ShouldBe(["email", "pozicioni", "njesia", "eprori_email"]);
        definition.GetProperty("maxRows").GetInt32().ShouldBe(5000);
        definition.GetProperty("maxFileMb").GetInt32().ShouldBe(5);

        var template = await admin.GetAsync($"{Base}/template", Ct);
        template.Content.Headers.ContentType!.MediaType.ShouldBe(ImportFiles.XlsxType);
        var bytes = await template.Content.ReadAsByteArrayAsync(Ct);
        ImportFiles.SheetNames(bytes).ShouldBe(["Organika", "Udhëzime"]);
        var sheet = ImportFiles.ReadSheet(bytes);
        sheet[0].ShouldBe(columns.ToArray()); // the headers of the definition
        sheet.Count.ShouldBe(2);               // and the example row
        // the styled template writes every cell of a row (bordered): the empty ones are not part of the text
        var instructions = ImportFiles.ReadSheet(bytes, 1).Select(r => string.Join(" ", r.Where(c => c.Length > 0))).ToList();
        instructions.ShouldContain(definition.GetProperty("purpose").GetString()!);
        foreach (var rule in definition.GetProperty("rules").EnumerateArray())
        {
            instructions.ShouldContain(rule.GetString()!);
        }

        var xlsx = ImportFiles.Xlsx(["EMAIL", "Pozicioni", "Njësia", "Eprori_email"], ["avokat1@ash.local", "Avokat", "Sektori X", ""]);
        (await Json(await Upload(admin, "organika.xlsx", xlsx))).GetProperty("rows").EnumerateArray().Single().GetProperty("status").GetString()
            .ShouldBe("CHANGED");
        var commaCsv = "email,pozicioni,njesia,eprori_email\navokat1@ash.local,\"Avokat, specialist\",Sektori X,\n";
        var commaRow = (await Json(await Upload(admin, "o.csv", Encoding.UTF8.GetBytes(commaCsv)))).GetProperty("rows").EnumerateArray().Single();
        commaRow.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("column").GetString() == "pozicioni")
            .GetProperty("new").GetString().ShouldBe("Avokat, specialist");

        // a wrong format or headers → 400 with the reason
        (await Upload(admin, "organika.pdf", [1, 2, 3])).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var wrong = await Upload(admin, "x.csv", Encoding.UTF8.GetBytes("emri,mbiemri\nA,B"));
        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await wrong.Content.ReadAsStringAsync(Ct)).ShouldContain("email, pozicioni");
    }

    [Fact]
    public async Task Only_the_administrator_imports()
    {
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");
        (await kryetar.GetAsync(Base, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Upload(kryetar, "organika.csv", Encoding.UTF8.GetBytes(Csv))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
