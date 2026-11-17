using System.Net;
using System.Text;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 053l — "Ngarko përdorues nga Excel" on the shared import pipeline: new users, "ekziston" without perditeso,
/// updates with perditeso = PO, every row error (cells, unknown office / institution / role, the user type, the guards of
/// 015 / 053i), headers with accents and case, ; and , CSV, the preview writes nothing, "Apliko" is atomic, a re-import
/// changes nothing.
/// </summary>
public class UsersImportTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Admin = "admin@ash.local";
    private const string Base = "/api/identity/admin/imports/users";
    private const string Ministry = "Ministria e Financave dhe Ekonomisë";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<object?> Scalar(string sql, bool root = false)
    {
        await using var connection = new MySqlConnection(root ? Db.RootConnectionString : Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(Ct);
        return value is DBNull ? null : value;
    }

    private static async Task<JsonElement> UploadCsv(HttpClient admin, string csv)
    {
        var response = await ImportFiles.Upload(admin, Base, "perdoruesit.csv", Encoding.UTF8.GetBytes(csv), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await ImportFiles.Json(response, Ct);
    }

    private static List<JsonElement> Rows(JsonElement view) => view.GetProperty("rows").EnumerateArray().ToList();

    private static string Errors(JsonElement row) => string.Join(" | ",
        row.GetProperty("errors").EnumerateArray().Select(e => $"{e.GetProperty("column").GetString()}: {e.GetProperty("message").GetString()}"));

    /// <summary>Headers in other case / with accents ("Përditëso", "Zyra kodi"); ; as separator.</summary>
    private static readonly string Valid = $"""
        EMAIL;Emri;Mbiemri;Zyra kodi;Institucioni;Rolet;Pozicioni;Aktiv;Përditëso
        agim.import@ash.test;Agim;Hoxha;tr;;"STATE_LAWYER; Avokat sekondar ";Avokat i Shtetit;;
        drita.import@ash.test;Drita;Kola;;{Ministry};Juristi i institucionit;;PO;
        avokat1@ash.local;Avokat1;Dev;TR;;STATE_LAWYER;;;
        avokat2@ash.local;Avokat2;Dev;TR;;"STATE_LAWYER;SECONDARY_LAWYER";Avokat i ri;;PO
        """;

    /// <summary>The state after applying <see cref="Valid"/>, every row asking for an update.</summary>
    private static readonly string Same = $"""
        email;emri;mbiemri;zyra_kodi;institucioni;rolet;pozicioni;aktiv;perditeso
        agim.import@ash.test;Agim;Hoxha;TR;;"STATE_LAWYER;SECONDARY_LAWYER";Avokat i Shtetit;PO;PO
        drita.import@ash.test;Drita;Kola;;{Ministry};INSTITUTION_JURIST;;PO;PO
        avokat2@ash.local;Avokat2;Dev;TR;;"STATE_LAWYER;SECONDARY_LAWYER";Avokat i ri;PO;PO
        """;

    [Fact]
    public async Task A_valid_file_previews_without_writing_applies_and_a_reimport_changes_nothing()
    {
        await using var factory = new AshApiFactory(Db);
        using var admin = factory.CreateClient().AsUser(Admin);

        var preview = await UploadCsv(admin, Valid);
        Rows(preview).Select(r => $"{r.GetProperty("key").GetString()}:{r.GetProperty("status").GetString()}").ShouldBe([
            "agim.import@ash.test:NEW", "drita.import@ash.test:NEW", "avokat1@ash.local:EXISTS", "avokat2@ash.local:CHANGED",
        ], string.Join("\n", Rows(preview).Select(Errors)));
        Rows(preview)[2].GetProperty("message").GetString()!.ShouldContain("perditeso = PO");
        Rows(preview)[3].GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("column").GetString()).ShouldBe(["pozicioni"]);

        // the preview writes nothing
        (await Scalar("SELECT COUNT(*) FROM users WHERE email LIKE '%.import@ash.test'")).ShouldBe(0L);
        (await Scalar("SELECT position FROM users WHERE email = 'avokat2@ash.local'")).ShouldBeNull();

        var id = preview.GetProperty("publicId").GetGuid();
        var applied = await ImportFiles.Json(await admin.PostAsync($"{Base}/{id}/apply", null, Ct), Ct);
        applied.GetProperty("appliedCount").GetInt32().ShouldBe(3);

        (await Scalar("""
            SELECT CONCAT(u.first_name, '|', o.code, '|', u.position, '|', GROUP_CONCAT(r.code ORDER BY r.code), '|', u.is_active)
              FROM users u JOIN offices o ON o.id = u.office_id JOIN user_roles ur ON ur.user_id = u.id JOIN roles r ON r.id = ur.role_id
             WHERE u.email = 'agim.import@ash.test' GROUP BY u.id
            """)).ShouldBe("Agim|TR|Avokat i Shtetit|SECONDARY_LAWYER,STATE_LAWYER|1");
        (await Scalar("""
            SELECT CONCAT(i.name, '|', r.code) FROM users u JOIN institutions i ON i.id = u.institution_id
              JOIN user_roles ur ON ur.user_id = u.id JOIN roles r ON r.id = ur.role_id WHERE u.email = 'drita.import@ash.test'
            """)).ShouldBe($"{Ministry}|INSTITUTION_JURIST");
        (await Scalar("SELECT external_subject IS NULL FROM users WHERE email = 'drita.import@ash.test'")).ShouldBe(1L); // linked at the first login
        (await Scalar("SELECT position FROM users WHERE email = 'avokat2@ash.local'")).ShouldBe("Avokat i ri");
        (await Scalar("SELECT kind FROM org_imports")).ShouldBe("USERS");

        // re-importing, now asking for updates everywhere: nothing to change
        var again = await UploadCsv(admin, Same);
        Rows(again).Select(r => r.GetProperty("status").GetString()).ShouldBe(["UNCHANGED", "UNCHANGED", "UNCHANGED"],
            string.Join("\n", Rows(again).Select(r => $"{Errors(r)} {r.GetProperty("changes")}")));
    }

    [Fact]
    public async Task Every_error_is_reported_on_its_row_and_cell_and_the_valid_rows_still_import()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString); // avokat1 primary on an ACTIVE case
        await using var factory = new AshApiFactory(Db);
        using var admin = factory.CreateClient().AsUser(Admin);

        var preview = await UploadCsv(admin, $"""
            email,emri,mbiemri,zyra_kodi,institucioni,rolet,pozicioni,aktiv,perditeso
            ,Pa,Email,TR,,STATE_LAWYER,,,
            jo-email,Gabim,Email,TR,,STATE_LAWYER,,,
            dy@ash.test,Dy,Here,TR,,STATE_LAWYER,,,
            dy@ash.test,Dy,Here,TR,,STATE_LAWYER,,,
            paemer@ash.test,,Mbiemri,TR,,STATE_LAWYER,,,
            tedyja@ash.test,Te,Dyja,TR,{Ministry},STATE_LAWYER,,,
            asnjera@ash.test,As,Njera,,,STATE_LAWYER,,,
            zyra@ash.test,Zyra,Panjohur,XX,,STATE_LAWYER,,,
            inst@ash.test,Inst,Panjohur,,Ministria e Panjohur,INSTITUTION_USER,,,
            rol@ash.test,Rol,Panjohur,TR,,SHEFI,,,
            tipi@ash.test,Tipi,Gabim,TR,,INSTITUTION_USER,,,
            aktiv@ash.test,Aktiv,Gabim,TR,,STATE_LAWYER,,ndoshta,
            kryetar@ash.local,Kryetar,Dev,TR,,OFFICE_HEAD,,JO,PO
            avokat1@ash.local,Avokat1,Dev,TR,,LEGAL_ASSISTANT,,,PO
            institucion@ash.local,Institucion,Dev,TR,,STATE_LAWYER,,,PO
            mire@ash.test,Mirë,Rresht,TR,,STATE_LAWYER,,,
            """);

        var rows = Rows(preview);
        string Error(int i) => Errors(rows[i]);
        rows.Take(15).ShouldAllBe(r => r.GetProperty("status").GetString() == "ERROR");
        Error(0).ShouldContain("email: Mungon e-maili.");
        Error(1).ShouldContain("email: E-maili nuk është i vlefshëm.");
        Error(2).ShouldContain("më shumë se një herë");
        Error(3).ShouldContain("më shumë se një herë");
        Error(4).ShouldContain("emri: Emri është i detyrueshëm");
        Error(5).ShouldContain("zyra_kodi: Plotësoni saktësisht njërën");
        Error(6).ShouldContain("zyra_kodi: Plotësoni saktësisht njërën");
        Error(7).ShouldContain("Nuk ka zyrë aktive me kodin „XX\"");
        Error(8).ShouldContain("institucioni: Nuk ka institucion aktiv");
        Error(9).ShouldContain("Roli „SHEFI\" nuk ekziston.");
        Error(10).ShouldContain("nuk lejohet për përdorues të zyrës");
        Error(11).ShouldContain("aktiv: Shkruani PO ose JO.");
        Error(12).ShouldContain("Kryetar i zyrës");                         // 015: the Kryetar stays
        Error(13).ShouldContain("avokat primar në 1 çështje të hapura");     // 053i: the lawyer role is in use
        Error(14).ShouldContain("nuk kthehet në përdorues institucioni");   // the user type is fixed
        rows[15].GetProperty("status").GetString().ShouldBe("NEW");

        var id = preview.GetProperty("publicId").GetGuid();
        (await ImportFiles.Json(await admin.PostAsync($"{Base}/{id}/apply", null, Ct), Ct)).GetProperty("appliedCount").GetInt32().ShouldBe(1);
        (await Scalar("SELECT COUNT(*) FROM users WHERE email = 'mire@ash.test'")).ShouldBe(1L);
        (await Scalar("""
            SELECT COUNT(*) FROM users WHERE email IN ('dy@ash.test', 'paemer@ash.test', 'tedyja@ash.test', 'asnjera@ash.test',
                   'zyra@ash.test', 'inst@ash.test', 'rol@ash.test', 'tipi@ash.test', 'aktiv@ash.test')
            """)).ShouldBe(0L);
        (await Scalar("SELECT is_active FROM users WHERE email = 'kryetar@ash.local'")).ShouldBe(true);
    }

    [Fact]
    public async Task Apliko_is_atomic_one_failing_row_writes_nothing()
    {
        await using var factory = new AshApiFactory(Db);
        using var admin = factory.CreateClient().AsUser(Admin);
        var preview = await UploadCsv(admin, """
            email;emri;mbiemri;zyra_kodi;institucioni;rolet
            para@ash.test;Para;Rreshti;TR;;STATE_LAWYER
            boom@ash.test;Boom;Rreshti;TR;;STATE_LAWYER
            """);
        Rows(preview).ShouldAllBe(r => r.GetProperty("status").GetString() == "NEW");

        // the second insert fails inside the database: the first one must be rolled back with it
        await Scalar("""
            CREATE TRIGGER ash_core.trg_test_import_boom BEFORE INSERT ON ash_core.users FOR EACH ROW
            BEGIN IF NEW.email = 'boom@ash.test' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'boom'; END IF; END
            """, root: true);
        try
        {
            var applied = await admin.PostAsync($"{Base}/{preview.GetProperty("publicId").GetGuid()}/apply", null, Ct);
            applied.IsSuccessStatusCode.ShouldBeFalse();
        }
        finally
        {
            await Scalar("DROP TRIGGER IF EXISTS ash_core.trg_test_import_boom", root: true);
        }

        (await Scalar("SELECT COUNT(*) FROM users WHERE email IN ('para@ash.test', 'boom@ash.test')")).ShouldBe(0L);
        (await Scalar("SELECT applied_at FROM org_imports")).ShouldBeNull();
    }

    [Fact]
    public async Task The_users_template_has_its_columns_and_instructions_and_a_comma_csv_reads_the_same()
    {
        await using var factory = new AshApiFactory(Db);
        using var admin = factory.CreateClient().AsUser(Admin);

        var definition = await ImportFiles.Json(await admin.GetAsync($"{Base}/definition", Ct), Ct);
        definition.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("name").GetString())
            .ShouldBe(["email", "emri", "mbiemri", "zyra_kodi", "institucioni", "rolet", "pozicioni", "aktiv", "perditeso"]);
        var template = await (await admin.GetAsync($"{Base}/template", Ct)).Content.ReadAsByteArrayAsync(Ct);
        ImportFiles.SheetNames(template).ShouldBe(["Perdoruesit", "Udhëzime"]);
        ImportFiles.ReadSheet(template)[0].Length.ShouldBe(9);

        var comma = await UploadCsv(admin, "email,emri,mbiemri,zyra_kodi,institucioni,rolet\nkoma@ash.test,Koma,Csv,TR,,\"STATE_LAWYER;SECONDARY_LAWYER\"\n");
        Rows(comma).Single().GetProperty("status").GetString().ShouldBe("NEW");

        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");
        (await kryetar.GetAsync($"{Base}/definition", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
