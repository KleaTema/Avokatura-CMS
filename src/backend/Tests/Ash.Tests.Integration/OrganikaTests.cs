using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ash.Modules.Identity.Application.Organika;
using Ash.Tests.Integration.Infrastructure;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 053g — "Organika" (client decision E): the template, the upload → preview WITHOUT applying, "Apliko" in one
/// transaction with a history row per user, every import kept.
/// </summary>
public class OrganikaTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Admin = "admin@ash.local";
    private const string Base = "/api/identity/admin/organika";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(Ct);
        return value is DBNull ? null : value;
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient client, string fileName, byte[] content)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(fileName.EndsWith(".csv") ? "text/csv" : OrgSheet.XlsxContentType);
        form.Add(file, "file", fileName);
        return await client.PostAsync($"{Base}/imports", form, Ct);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue(text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>
    /// avokat1 changes; avokat2 is already as written (unchanged); an unknown e-mail; ndihmes twice (duplicate); an
    /// unknown manager (protokoll); a row without position; kryetar as avokat1's manager.
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

    [Fact]
    public async Task The_upload_shows_the_preview_and_applies_nothing_then_Apliko_writes_it_with_history()
    {
        await Scalar("UPDATE users SET position = 'Avokat', org_unit = 'Sektori A' WHERE email = 'avokat2@ash.local'");
        await using var factory = new AshApiFactory(Db);
        using var admin = factory.CreateClient().AsUser(Admin);

        var uploaded = await Upload(admin, "organika.csv", Encoding.UTF8.GetBytes(Csv));
        uploaded.StatusCode.ShouldBe(HttpStatusCode.Created, await uploaded.Content.ReadAsStringAsync(Ct));
        var preview = await Json(uploaded);
        var summary = preview.GetProperty("summary");
        (summary.GetProperty("total").GetInt32(), summary.GetProperty("matched").GetInt32(), summary.GetProperty("changed").GetInt32(),
                summary.GetProperty("unchanged").GetInt32(), summary.GetProperty("emailNotFound").GetInt32(),
                summary.GetProperty("duplicate").GetInt32(), summary.GetProperty("managerNotFound").GetInt32(),
                summary.GetProperty("invalid").GetInt32())
            .ShouldBe((7, 2, 1, 1, 1, 2, 1, 1));
        var rows = preview.GetProperty("rows").EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("status").GetString())
            .ShouldBe(["CHANGED", "UNCHANGED", "EMAIL_NOT_FOUND", "DUPLICATE", "DUPLICATE", "MANAGER_NOT_FOUND", "INVALID"]);
        rows[0].GetProperty("newManager").GetString().ShouldBe("Kryetar Dev");
        rows[0].GetProperty("line").GetInt32().ShouldBe(2);

        // nothing applied yet; the import is kept with its file
        (await Scalar("SELECT position FROM users WHERE email = 'avokat1@ash.local'")).ShouldBeNull();
        (await Scalar("SELECT CONCAT(file_name, '|', LENGTH(file_content) > 0, '|', applied_at IS NULL) FROM org_imports"))
            .ShouldBe("organika.csv|1|1");

        var id = preview.GetProperty("publicId").GetGuid();
        var applied = await Json(await admin.PostAsync($"{Base}/imports/{id}/apply", null, Ct));
        applied.GetProperty("appliedCount").GetInt32().ShouldBe(1);
        applied.GetProperty("appliedBy").GetString().ShouldNotBeNullOrEmpty();

        (await Scalar("SELECT CONCAT(u.position, '|', u.org_unit, '|', m.email) FROM users u JOIN users m ON m.id = u.manager_user_id WHERE u.email = 'avokat1@ash.local'"))
            .ShouldBe("Avokat i Shtetit, specialist|Sektori i Gjyqësorit|kryetar@ash.local");
        (await Scalar("SELECT CONCAT(IFNULL(old_position, '-'), '→', new_position) FROM user_org_history h JOIN users u ON u.id = h.user_id WHERE u.email = 'avokat1@ash.local'"))
            .ShouldBe("-→Avokat i Shtetit, specialist");
        (await Scalar("SELECT COUNT(*) FROM user_org_history")).ShouldBe(1L); // only the changed row
        (await Scalar("SELECT position FROM users WHERE email = 'ndihmes@ash.local'")).ShouldBeNull(); // duplicates never

        // once only; the list keeps it; the import re-read shows nothing left to change
        (await admin.PostAsync($"{Base}/imports/{id}/apply", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var list = await Json(await admin.GetAsync($"{Base}/imports", Ct));
        list.EnumerateArray().Single().GetProperty("appliedCount").GetInt32().ShouldBe(1);
        (await Json(await admin.GetAsync($"{Base}/imports/{id}", Ct))).GetProperty("summary").GetProperty("changed").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task The_template_downloads_and_an_xlsx_sheet_is_read_like_the_csv()
    {
        await using var factory = new AshApiFactory(Db);
        using var admin = factory.CreateClient().AsUser(Admin);

        var template = await admin.GetAsync($"{Base}/template", Ct);
        template.StatusCode.ShouldBe(HttpStatusCode.OK);
        template.Content.Headers.ContentType!.MediaType.ShouldBe(OrgSheet.XlsxContentType);
        var (headerOnly, error) = OrgSheet.Read("t.xlsx", await template.Content.ReadAsByteArrayAsync(Ct));
        error.ShouldBeNull();
        headerOnly!.ShouldBeEmpty(); // the template: headers, no rows

        var xlsx = Xlsx(["email", "pozicioni", "njesia", "eprori_email"], ["avokat1@ash.local", "Avokat", "Sektori X", ""]);
        var preview = await Json(await Upload(admin, "organika.xlsx", xlsx));
        preview.GetProperty("rows").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("CHANGED");

        // a wrong format or headers → 400 with the reason
        (await Upload(admin, "organika.pdf", [1, 2, 3])).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Upload(admin, "x.csv", Encoding.UTF8.GetBytes("emri,mbiemri\nA,B"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Only_the_administrator_imports()
    {
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");
        (await kryetar.GetAsync($"{Base}/imports", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Upload(kryetar, "organika.csv", Encoding.UTF8.GetBytes(Csv))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>A one-sheet workbook with shared strings, as Excel writes it.</summary>
    private static byte[] Xlsx(params string[][] rows)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var strings = workbook.AddNewPart<SharedStringTablePart>();
            strings.SharedStringTable = new SharedStringTable();
            var sheetPart = workbook.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            var index = 0;
            for (var r = 0; r < rows.Length; r++)
            {
                var row = new Row { RowIndex = (uint)(r + 1) };
                for (var c = 0; c < rows[r].Length; c++)
                {
                    if (rows[r][c].Length == 0)
                    {
                        continue;
                    }

                    strings.SharedStringTable.Append(new SharedStringItem(new Text(rows[r][c])));
                    row.Append(new Cell
                    {
                        CellReference = $"{(char)('A' + c)}{r + 1}", DataType = CellValues.SharedString,
                        CellValue = new CellValue((index++).ToString()),
                    });
                }

                data.Append(row);
            }

            sheetPart.Worksheet = new Worksheet(data);
            workbook.Workbook.AppendChild(new Sheets()).Append(new Sheet { Id = workbook.GetIdOfPart(sheetPart), SheetId = 1, Name = "S" });
            workbook.Workbook.Save();
        }

        return stream.ToArray();
    }
}
