using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Modules.Reports.Application;
using Ash.Modules.Reports.Infrastructure;
using Ash.SharedKernel.Ids;
using Ash.SharedKernel.Time;
using Ash.Tests.Integration.Infrastructure;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>Tasks 042/043 — the active and closed cases reports, JSON and .xlsx export.</summary>
public class ReportTests(MySqlFixture db) : IntegrationTest(db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Active = "/api/reports/active-cases";
    private const string Closed = "/api/reports/closed-cases";
    private static readonly DateTime Now = new(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------- helpers

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(Ct);
        return value is DBNull ? null : value;
    }

    private async Task Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(Ct);
    }

    private async Task<List<Dictionary<string, object?>>> Rows(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(Enumerable.Range(0, reader.FieldCount)
                .ToDictionary(reader.GetName, i => reader.IsDBNull(i) ? null : reader.GetValue(i)));
        }

        return rows;
    }

    private async Task<Guid> PublicIdOf(string table, long id) =>
        PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM {table} WHERE id = {id}"))!);

    /// <summary>Every row of a report (all pages, 100 at a time).</summary>
    private static async Task<List<JsonElement>> AllRows(HttpClient client, string path, string query = "")
    {
        var rows = new List<JsonElement>();
        string? after = null;
        do
        {
            var response = await client.GetAsync($"{path}?limit=100{query}{(after is null ? "" : $"&after={Uri.EscapeDataString(after)}")}", Ct);
            var text = await response.Content.ReadAsStringAsync(Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
            var page = JsonDocument.Parse(text).RootElement;
            rows.AddRange(page.GetProperty("items").EnumerateArray().Select(e => e.Clone()));
            after = page.GetProperty("nextCursor").GetString();
        }
        while (after is not null);

        return rows;
    }

    private static List<string> Codes(IEnumerable<JsonElement> rows) =>
        rows.Select(r => r.GetProperty("caseCode").GetString()!).ToList();

    /// <summary>The user's case list over all their tabs, open statuses (REGISTERED + ACTIVE), with extra filters.</summary>
    private static async Task<HashSet<string>> ListCodes(HttpClient client, string query = "")
    {
        var tabs = await client.GetFromJsonAsync<JsonElement>("/api/cases/tabs", Ct);
        var codes = new HashSet<string>();
        foreach (var tab in tabs.GetProperty("tabs").EnumerateArray().Select(t => t.GetProperty("tab").GetString()))
        {
            string? after = null;
            do
            {
                var page = await client.GetFromJsonAsync<JsonElement>(
                    $"/api/cases?tab={tab}&status=OPEN&limit=100{query}{(after is null ? "" : $"&after={Uri.EscapeDataString(after)}")}", Ct);
                foreach (var item in page.GetProperty("items").EnumerateArray())
                {
                    codes.Add(item.GetProperty("caseCode").GetString()!);
                }

                after = page.GetProperty("nextCursor").GetString();
            }
            while (after is not null);
        }

        return codes;
    }

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private AshApiFactory Factory(Action<IServiceCollection>? more = null) =>
        new(Db, configureServices: services =>
        {
            services.AddSingleton<IClock>(new FixedClock(Now));
            more?.Invoke(services);
        });

    private static async Task<(SpreadsheetDocument Doc, HttpResponseMessage Response)> Export(HttpClient client, string path, string query = "")
    {
        var response = await client.GetAsync($"{path}/export?x=1{query}", Ct);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new ShouldAssertException($"{response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        return (SpreadsheetDocument.Open(new MemoryStream(bytes), false), response);
    }

    private static Worksheet Sheet(SpreadsheetDocument doc, string name)
    {
        var sheet = doc.WorkbookPart!.Workbook.Descendants<Sheet>().Single(s => s.Name == name);
        return ((WorksheetPart)doc.WorkbookPart.GetPartById(sheet.Id!)).Worksheet;
    }

    private static List<List<Cell>> SheetRows(Worksheet sheet) =>
        sheet.Descendants<Row>().Select(r => r.Elements<Cell>().ToList()).ToList();

    private static string Text(Cell cell) =>
        cell.DataType?.Value == CellValues.InlineString ? cell.InlineString!.Text!.Text : cell.CellValue?.Text ?? string.Empty;

    private static int Col(IReadOnlyList<ReportColumn> columns, string key) => columns.ToList().FindIndex(c => c.Key == key);

    // ---------------------------------------------------------------- active: the view, the scope

    [Fact]
    public async Task Active_report_rows_equal_the_view_rows_for_the_same_cases()
    {
        await DashboardSeed.CreateAsync(Db.AppConnectionString, 90, Now);
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await EnrichAsync(s);
        await using var factory = Factory();
        using var admin = factory.CreateClient().AsUser("admin@ash.local");

        var api = await AllRows(admin, Active);
        var view = (await Rows("""
            SELECT c.public_id AS pid, v.* FROM v_report_active_cases v JOIN cases c ON c.id = v.case_id
            """)).ToDictionary(r => PublicId.FromBytes((byte[])r["pid"]!));

        api.Count.ShouldBe(view.Count);
        api.Count.ShouldBeGreaterThan(60);
        foreach (var row in api)
        {
            var v = view[row.GetProperty("casePublicId").GetGuid()];
            string? S(string p) => row.GetProperty(p).ValueKind == JsonValueKind.Null ? null : row.GetProperty(p).GetString();
            decimal? D(string p) => row.GetProperty(p).ValueKind == JsonValueKind.Null ? null : row.GetProperty(p).GetDecimal();
            S("caseCode").ShouldBe(v["kodi_unik"]);
            S("actNumber").ShouldBe(v["numri_aktit"]);
            S("plaintiffs").ShouldBe(v["paditesi"]);
            S("defendants").ShouldBe(v["i_padituri_person_i_trete"]);
            S("objectText").ShouldBe(v["objekti"]);
            S("court").ShouldBe(v["gjykata"]);
            S("judge").ShouldBe(v["gjyqtari"]);
            (row.GetProperty("nextHearingAt").ValueKind == JsonValueKind.Null ? (DateTime?)null : row.GetProperty("nextHearingAt").GetDateTime().ToUniversalTime())
                .ShouldBe(v["seanca_e_radhes"] is DateTime h ? DateTime.SpecifyKind(h, DateTimeKind.Utc) : null);
            S("lawyers").ShouldBe(v["avokati"]);
            S("lastHearingDescription").ShouldBe(v["pershkrimi_seances_se_fundit"]);
            row.GetProperty("hasSecurityMeasure").GetBoolean().ShouldBe(Convert.ToBoolean(v["masa_sigurimit"]));
            D("claimAmount").ShouldBe(v["claim_amount"]);
            S("claimCurrency").ShouldBe(v["claim_currency"]);
            D("claimAreaM2").ShouldBe(v["claim_area_m2"]);
            S("category").ShouldBe(v["kategoria"]);
            S("subcategory").ShouldBe(v["lloji"]);
            S("priority").ShouldBe(v["priority"]);
            row.GetProperty("isSuspended").GetBoolean().ShouldBe(Convert.ToBoolean(v["is_suspended"]));
        }

        // the enriched case is really compared (not only empty columns)
        var activeCode = await Code(s.ActiveCaseId);
        var enriched = api.Single(r => r.GetProperty("caseCode").GetString() == activeCode);
        enriched.GetProperty("plaintiffs").GetString().ShouldBe("=HYPERLINK(\"http://x\",\"klik\"); Petrit Hoxha");
        enriched.GetProperty("defendants").GetString()!.ShouldContain("(");
        enriched.GetProperty("lawyers").GetString().ShouldBe("Avokat1 Dev / Avokat2 Dev");
    }

    /// <summary>Parties (one formula-like), claim, a judge, a last hearing with a description, a category on the seed's ACTIVE case.</summary>
    private async Task EnrichAsync(CaseSeed s)
    {
        var protokoll = s.User("protokoll").Id;
        await Execute($"""
            INSERT INTO case_parties (case_id, party_role_id, party_kind, display_name, created_by)
            SELECT {s.ActiveCaseId}, id, 'NATURAL', '=HYPERLINK("http://x","klik")', {protokoll} FROM party_roles WHERE code = 'PADITES';
            INSERT INTO case_parties (case_id, party_role_id, party_kind, display_name, created_by)
            SELECT {s.ActiveCaseId}, id, 'NATURAL', 'Petrit Hoxha', {protokoll} FROM party_roles WHERE code = 'PADITES';
            INSERT INTO case_parties (case_id, party_role_id, party_kind, display_name, created_by)
            SELECT {s.ActiveCaseId}, id, 'LEGAL', 'Shoqëria ABC sh.p.k.', {protokoll} FROM party_roles WHERE code = 'I_PADITUR';
            INSERT INTO case_hearings (case_id, hearing_type_id, scheduled_at, status, description, created_by)
            VALUES ({s.ActiveCaseId}, (SELECT MIN(id) FROM hearing_types), '2026-10-01 08:00:00', 'HELD', 'U dëgjuan palët.', {protokoll});
            UPDATE cases SET last_hearing_id = LAST_INSERT_ID(), next_hearing_at = '2026-11-20 08:30:00', claim_amount = 1500.50,
                   claim_currency = 'ALL', claim_area_m2 = 250.75, has_security_measure = 1,
                   category_id = (SELECT MIN(id) FROM case_categories),
                   judge_id = (SELECT MIN(id) FROM judges)
             WHERE id = {s.ActiveCaseId};
            UPDATE cases SET claim_amount = 1234567890123456.78, claim_currency = 'EUR' WHERE id = {s.RegisteredCaseId};
            """);
    }

    [Fact]
    public async Task The_report_case_set_equals_each_users_case_list_and_protokoll_is_403()
    {
        var ids = await DashboardSeed.CreateAsync(Db.AppConnectionString, 150, Now);
        await using var factory = Factory();

        foreach (var email in new[]
                 {
                     "avokat1@ash.local", "avokat2@ash.local", "jurist@ash.local", "institucion@ash.local",
                     "kryetar@ash.local", "monitor@ash.local", "admin@ash.local", ids.DrKryetarEmail,
                 })
        {
            using var client = factory.CreateClient().AsUser(email);
            var report = Codes(await AllRows(client, Active)).ToHashSet();
            var list = await ListCodes(client);
            report.ShouldBe(list, ignoreOrder: true, $"{email}: report vs case list");
        }

        // with the same filter: the Kryetar's office narrowed to one lawyer
        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");
        var avokat1 = await PublicIdOf("users", Convert.ToInt64(await Scalar("SELECT id FROM users WHERE email = 'avokat1@ash.local'")));
        var filtered = Codes(await AllRows(kryetar, Active, $"&lawyerId={avokat1}")).ToHashSet();
        filtered.Count.ShouldBeGreaterThan(0);
        filtered.ShouldBe(await ListCodes(kryetar, $"&lawyerId={avokat1}"), ignoreOrder: true);

        using var protokoll = factory.CreateClient().AsUser("protokoll@ash.local");
        (await protokoll.GetAsync(Active, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await protokoll.GetAsync(Closed, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await protokoll.GetAsync($"{Active}/export", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await protokoll.GetAsync($"{Closed}/export", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- closed

    [Fact]
    public async Task A_dossier_closed_at_1_and_2_gives_two_rows_and_a_later_transfer_keeps_the_lawyer_at_closing()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var kryetar = s.User("kryetar").Id;
        var closed1 = new DateTime(2026, 6, 10, 9, 0, 0, DateTimeKind.Utc);
        var closed2 = new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
        var level2 = await InsertLevel2Async(s, closed2);
        await Execute($"""
            UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = @c1, closed_by = {kryetar} WHERE id = {s.ActiveCaseId};
            INSERT INTO case_decisions (case_id, result_id, decision_number, decision_date, currency, lost_amount, court_costs_amount,
                                        is_appealed, decision_received, decision_received_at, recorded_by)
            SELECT {s.ActiveCaseId}, id, '100/1', '2026-06-09', 'ALL', 2500000.00, 15000.00, 1, 1, @c1, {kryetar} FROM decision_results WHERE code = 'HUMBUR';
            INSERT INTO case_decisions (case_id, result_id, decision_number, decision_date, currency, won_amount, won_area_m2,
                                        is_final, decision_received, recorded_by)
            SELECT {level2}, id, '200/2', '2026-10-04', 'EUR', 10000.00, 120.50, 1, 0, {kryetar} FROM decision_results WHERE code = 'FITUAR';
            -- the seed assigns "now": the team was there before the June closing
            UPDATE case_assignments SET assigned_at = '2026-01-15 09:00:00' WHERE case_id = {s.ActiveCaseId};
            -- after closing level 1, its primary changes (avokat1 → another lawyer): the report keeps avokat1
            UPDATE case_assignments SET ended_at = @c1 + INTERVAL 1 HOUR, end_reason = 'TRANSFERRED'
             WHERE case_id = {s.ActiveCaseId} AND case_role = 'PRIMARY_LAWYER' AND ended_at IS NULL;
            INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by, assigned_at)
            VALUES ({s.ActiveCaseId}, {s.User("drlawyer").Id}, 'PRIMARY_LAWYER', {kryetar}, @c1 + INTERVAL 1 HOUR);
            """, ("@c1", closed1));
        await using var factory = Factory();
        using var admin = factory.CreateClient().AsUser("admin@ash.local");

        var rows = await AllRows(admin, Closed);
        rows.Count.ShouldBe(2);
        rows.Select(r => r.GetProperty("caseCode").GetString()).ShouldBe([$"{await Code(s.ActiveCaseId)}", await Code(level2)]);
        rows[0].GetProperty("result").GetString().ShouldBe(await Scalar("SELECT name FROM decision_results WHERE code = 'HUMBUR'"));
        rows[1].GetProperty("result").GetString().ShouldBe(await Scalar("SELECT name FROM decision_results WHERE code = 'FITUAR'"));
        rows[0].GetProperty("lawyer").GetString().ShouldBe("Avokat1 Dev");
        rows[1].GetProperty("lawyer").GetString().ShouldBe("Avokat2 Dev");
        rows[0].GetProperty("lostAmount").GetDecimal().ShouldBe(2500000.00m);
        rows[0].GetProperty("currency").GetString().ShouldBe("ALL");
        rows[1].GetProperty("currency").GetString().ShouldBe("EUR");
        rows[1].GetProperty("wonAreaM2").GetDecimal().ShouldBe(120.50m);
        rows[0].GetProperty("isAppealed").GetBoolean().ShouldBeTrue();
        rows[1].GetProperty("isFinal").GetBoolean().ShouldBeTrue();
        rows[1].GetProperty("decisionReceived").GetBoolean().ShouldBeFalse();
        rows[0].GetProperty("dossierPublicId").GetGuid().ShouldBe(rows[1].GetProperty("dossierPublicId").GetGuid());

        // the lawyer filter follows the same rule; the dates are Tirane days, both ends included
        var avokat1 = await PublicIdOf("users", s.User("avokat1").Id);
        Codes(await AllRows(admin, Closed, $"&lawyerId={avokat1}")).ShouldBe([await Code(s.ActiveCaseId)]);
        Codes(await AllRows(admin, Closed, "&closedFrom=2026-10-05&closedTo=2026-10-05")).ShouldBe([await Code(level2)]);
        Codes(await AllRows(admin, Closed, "&resultCode=HUMBUR")).ShouldBe([await Code(s.ActiveCaseId)]);
        (await admin.GetAsync($"{Closed}?closedFrom=2026-10-06&closedTo=2026-10-05", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // the other columns equal the view (its "avokati" is the LATEST primary — see client question report-closed-lawyer)
        var view = await Rows($"SELECT * FROM v_report_closed_cases WHERE dossier_id = {s.ActiveDossierId} ORDER BY case_id");
        view.Count.ShouldBe(2);
        view[0]["rezultati"].ShouldBe(rows[0].GetProperty("result").GetString());
        view[0]["decision_number"].ShouldBe("100/1");
        view[1]["won_area_m2"].ShouldBe(120.50m);
        view[0]["avokati"].ShouldBe("Dr Lawyer");
    }

    private async Task<string> Code(long caseId) => (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {caseId}"))!;

    private async Task<long> InsertLevel2Async(CaseSeed s, DateTime closedAt)
    {
        var kryetar = s.User("kryetar").Id;
        await Execute($"""
            INSERT INTO cases (dossier_id, level_seq, case_code, office_id, summons_number, act_number, received_date,
                               court_level_id, court_id, object_text, lifecycle_status, registered_by, closed_at, closed_by)
            SELECT dossier_id, 2, CONCAT(SUBSTRING_INDEX(case_code, '/', 1), '/2'), office_id, 'FL-2', 'AKT-2', CURDATE(),
                   (SELECT id FROM court_levels WHERE code = 'APPEAL'), court_id, 'Apel', 'CLOSED', registered_by, @c2, {kryetar}
              FROM cases WHERE id = {s.ActiveCaseId};
            INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by, assigned_at)
            SELECT id, {s.User("avokat2").Id}, 'PRIMARY_LAWYER', {kryetar}, @c2 - INTERVAL 30 DAY FROM cases WHERE dossier_id = {s.ActiveDossierId} AND level_seq = 2;
            """, ("@c2", closedAt));
        return Convert.ToInt64(await Scalar($"SELECT id FROM cases WHERE dossier_id = {s.ActiveDossierId} AND level_seq = 2"));
    }

    // ---------------------------------------------------------------- export

    [Fact]
    public async Task The_export_has_typed_cells_a_Filtrat_sheet_the_same_rows_as_the_JSON_and_an_audit_row()
    {
        await DashboardSeed.CreateAsync(Db.AppConnectionString, 60, Now);
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await EnrichAsync(s);
        // the lawyer of the Registered case too, so its 18-digit claim is in the lawyer's export
        await Execute($"INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by) VALUES ({s.RegisteredCaseId}, {s.User("avokat1").Id}, 'SECONDARY_LAWYER', {s.User("kryetar").Id})");
        await using var factory = Factory();
        using var avokat1 = factory.CreateClient().AsUser("avokat1@ash.local");

        var json = await AllRows(avokat1, Active);
        var (doc, response) = await Export(avokat1, Active);
        using (doc)
        {
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            var disposition = response.Content.Headers.ContentDisposition!;
            disposition.FileName!.Trim('"').ShouldBe("raporti-ceshtjet-aktive-2026-11-10.xlsx");
            disposition.FileNameStar.ShouldBe("raporti-ceshtjet-aktive-2026-11-10.xlsx");

            var columns = ReportColumns.Active;
            var rows = SheetRows(Sheet(doc, "Çështjet aktive"));
            rows[0].Select(Text).ShouldBe(columns.Select(c => c.Header));
            var data = rows.Skip(1).ToList();

            // the same rows, in the same order, as the JSON — and only the lawyer's cases (the scope)
            data.Select(r => Text(r[Col(columns, "caseCode")])).ShouldBe(Codes(json));
            var mine = Convert.ToInt64(await Scalar($"""
                SELECT COUNT(*) FROM cases c WHERE c.lifecycle_status <> 'CLOSED' AND EXISTS (SELECT 1 FROM case_assignments a
                 WHERE a.case_id = c.id AND a.user_id = {s.User("avokat1").Id} AND a.ended_at IS NULL
                   AND a.case_role IN ('PRIMARY_LAWYER','SECONDARY_LAWYER','LEGAL_ASSISTANT'))
                """));
            data.Count.ShouldBe((int)mine);
            data.Count.ShouldBeLessThan(Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM cases WHERE lifecycle_status <> 'CLOSED'")));

            var activeCode = await Code(s.ActiveCaseId);
            var row = data.Single(r => Text(r[Col(columns, "caseCode")]) == activeCode);
            // a formula-like party name is a STRING cell
            var plaintiffs = row[Col(columns, "plaintiffs")];
            plaintiffs.DataType!.Value.ShouldBe(CellValues.InlineString);
            plaintiffs.CellFormula.ShouldBeNull();
            Text(plaintiffs).ShouldStartWith("=HYPERLINK(");
            // money: a number with 2 decimals; m² too; the currency in its own column
            var amount = row[Col(columns, "claimAmount")];
            amount.DataType!.Value.ShouldBe(CellValues.Number);
            decimal.Parse(amount.CellValue!.Text, CultureInfo.InvariantCulture).ShouldBe(1500.50m);
            Text(row[Col(columns, "claimCurrency")]).ShouldBe("ALL");
            decimal.Parse(row[Col(columns, "claimAreaM2")].CellValue!.Text, CultureInfo.InvariantCulture).ShouldBe(250.75m);
            // a date is a numeric cell with a date style: 20.11.2026 09:30 in Tirane
            var hearing = row[Col(columns, "nextHearingAt")];
            hearing.DataType!.Value.ShouldBe(CellValues.Number);
            DateTime.FromOADate(double.Parse(hearing.CellValue!.Text, CultureInfo.InvariantCulture))
                .ShouldBe(new DateTime(2026, 11, 20, 9, 30, 0));
            Text(row[Col(columns, "hasSecurityMeasure")]).ShouldBe("Po");

            // 18 significant digits do not fit a double: written as text, exact
            var registeredCode = await Code(s.RegisteredCaseId);
            var big = data.Single(r => Text(r[Col(columns, "caseCode")]) == registeredCode)[Col(columns, "claimAmount")];
            big.DataType!.Value.ShouldBe(CellValues.InlineString);
            Text(big).ShouldBe("1234567890123456.78");

            // the sheet: frozen header, autofilter over the rows
            var sheet = Sheet(doc, "Çështjet aktive");
            sheet.Descendants<Pane>().Single().State!.Value.ShouldBe(PaneStateValues.Frozen);
            sheet.Descendants<AutoFilter>().Single().Reference!.Value.ShouldBe($"A1:R{data.Count + 1}");

            // "Filtrat": the report, when, who, the row count
            var filtrat = SheetRows(Sheet(doc, "Filtrat")).Where(r => r.Count > 0).ToDictionary(r => Text(r[0]), r => r.Count > 1 ? r[1] : null);
            Text(filtrat["Raporti"]!).ShouldBe(ReportColumns.ActiveReportName);
            Text(filtrat["Gjeneruar nga"]!).ShouldBe("Avokat1 Dev");
            DateTime.FromOADate(double.Parse(filtrat["Gjeneruar më"]!.CellValue!.Text, CultureInfo.InvariantCulture))
                .ShouldBe(new DateTime(2026, 11, 10, 11, 0, 0));
            Text(filtrat["Numri i rreshtave"]!).ShouldBe(data.Count.ToString(CultureInfo.InvariantCulture));
            filtrat.ShouldContainKey("Pa filtra");
        }

        // a filtered export: the filters in Albanian, the audit row with report, filters and row count
        using var admin = factory.CreateClient().AsUser("admin@ash.local");
        var (filtered, _) = await Export(admin, Active, "&priority=NONE&suspended=false");
        using (filtered)
        {
            var filtrat = SheetRows(Sheet(filtered, "Filtrat")).Where(r => r.Count > 1).ToDictionary(r => Text(r[0]), r => Text(r[1]));
            filtrat["Prioriteti"].ShouldBe("NONE");
            filtrat["Pezulluar"].ShouldBe("Jo");
        }

        var adminId = Convert.ToInt64(await Scalar("SELECT id FROM users WHERE email = 'admin@ash.local'"));
        var deadline = DateTime.UtcNow.AddSeconds(15);
        string? details = null;
        while (details is null && DateTime.UtcNow < deadline)
        {
            await using var connection = new MySqlConnection(Db.AuditConnectionString);
            await connection.OpenAsync(Ct);
            await using var command = new MySqlCommand(
                $"SELECT details FROM ash_audit.audit_log WHERE action = 'REPORT_EXPORT' AND user_id = {adminId} ORDER BY id DESC LIMIT 1", connection);
            details = await command.ExecuteScalarAsync(Ct) as string;
            if (details is null)
            {
                await Task.Delay(100, Ct);
            }
        }

        details.ShouldNotBeNull();
        using var audit = JsonDocument.Parse(details);
        audit.RootElement.GetProperty("report").GetString().ShouldBe(ReportColumns.ActiveReportName);
        audit.RootElement.GetProperty("filters").GetProperty("Pezulluar").GetString().ShouldBe("Jo");
        audit.RootElement.GetProperty("rows").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task The_closed_export_writes_dates_flags_and_its_own_file_name()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var kryetar = s.User("kryetar").Id;
        await Execute($"""
            UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = '2026-06-10 09:00:00', closed_by = {kryetar} WHERE id = {s.ActiveCaseId};
            INSERT INTO case_decisions (case_id, result_id, decision_date, currency, lost_amount, is_recursed, recorded_by)
            SELECT {s.ActiveCaseId}, id, '2026-06-09', 'ALL', 99.99, 1, {kryetar} FROM decision_results WHERE code = 'HUMBUR';
            """);
        await using var factory = Factory();
        using var monitor = factory.CreateClient().AsUser("monitor@ash.local");

        var (doc, response) = await Export(monitor, Closed, "&closedFrom=2026-06-01&closedTo=2026-06-30");
        using (doc)
        {
            response.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("raporti-ceshtjet-mbyllura-2026-11-10.xlsx");
            var columns = ReportColumns.Closed;
            var row = SheetRows(Sheet(doc, "Çështjet e mbyllura")).Skip(1).Single();
            DateTime.FromOADate(double.Parse(row[Col(columns, "decisionDate")].CellValue!.Text, CultureInfo.InvariantCulture))
                .ShouldBe(new DateTime(2026, 6, 9));
            DateTime.FromOADate(double.Parse(row[Col(columns, "closedAt")].CellValue!.Text, CultureInfo.InvariantCulture))
                .ShouldBe(new DateTime(2026, 6, 10, 11, 0, 0)); // 09:00Z = 11:00 in Tirane (summer)
            Text(row[Col(columns, "isRecursed")]).ShouldBe("Po");
            Text(row[Col(columns, "isAppealed")]).ShouldBe("Jo");
            decimal.Parse(row[Col(columns, "lostAmount")].CellValue!.Text, CultureInfo.InvariantCulture).ShouldBe(99.99m);
            var filtrat = SheetRows(Sheet(doc, "Filtrat")).Where(r => r.Count > 1).ToDictionary(r => Text(r[0]), r => Text(r[1]));
            filtrat["Mbyllur nga"].ShouldBe("01.06.2026");
            filtrat["Mbyllur deri"].ShouldBe("30.06.2026");
        }
    }

    [Fact]
    public async Task More_rows_than_the_export_limit_is_400()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = Factory(services => services.AddSingleton(sp =>
            new ReportingOptions(Db.AppConnectionString, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60), MaxExportRows: 1)));
        using var admin = factory.CreateClient().AsUser("admin@ash.local");

        var response = await admin.GetAsync($"{Active}/export", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString()!.ShouldEndWith("Ngushtoni filtrat.");
    }

    [Fact]
    public async Task An_export_of_10000_rows_streams_in_under_10_seconds_with_flat_memory()
    {
        // 15,000 cases: a third CLOSED, so 10,000 open ones in the active report
        await DashboardSeed.CreateAsync(Db.AppConnectionString, 15_000, Now);
        await using var factory = Factory();
        using var admin = factory.CreateClient().AsUser("admin@ash.local");
        var total = (await admin.GetFromJsonAsync<JsonElement>($"{Active}?limit=1", Ct)).GetProperty("total").GetInt32();
        total.ShouldBe(10_000);

        // warm up (JIT, connection pool), then measure
        (await admin.GetAsync($"{Active}/export?priority=HIGH", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var baseline = process.PrivateMemorySize64;
        var peak = baseline;
        using var sampling = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            while (!sampling.IsCancellationRequested)
            {
                process.Refresh();
                peak = Math.Max(peak, process.PrivateMemorySize64);
                await Task.Delay(20, CancellationToken.None);
            }
        }, CancellationToken.None);

        var watch = Stopwatch.StartNew();
        using var response = await admin.GetAsync($"{Active}/export", HttpCompletionOption.ResponseHeadersRead, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var body = await response.Content.ReadAsStreamAsync(Ct);
        var file = new MemoryStream();
        await body.CopyToAsync(file, Ct);
        watch.Stop();
        await sampling.CancelAsync();
        await sampler;

        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        TestContext.Current.SendDiagnosticMessage(
            $"export of {total} rows: {watch.ElapsedMilliseconds} ms, {file.Length / 1024} KB, memory +{(peak - baseline) / (1024 * 1024)} MB");
        (peak - baseline).ShouldBeLessThan(100L * 1024 * 1024);
        file.Position = 0;
        using var doc = SpreadsheetDocument.Open(file, false);
        Sheet(doc, "Çështjet aktive").Descendants<Row>().Count().ShouldBe(total + 1);
    }
}
