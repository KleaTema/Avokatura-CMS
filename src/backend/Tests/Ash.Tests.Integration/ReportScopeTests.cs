using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 053h (decisions F, P6/P9) — the report builder: who sees which cases (ReportAudience, one place for the
/// reports and the statistics), the dependent filters and their scoped options, the three export formats (audited) and
/// "Raportet e mia". Uses <see cref="CaseSeed"/>: office TR's ACTIVE case (avokat1 primary, avokat2 secondary, ndihmes,
/// the ministry's jurist; the ministry linked MONITORING) and office DR with its lawyer.
/// </summary>
public class ReportScopeTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Builder = "/api/reports/builder";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static int Year => DateTime.UtcNow.Year;

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(Ct);
        return value is DBNull ? null : value;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private async Task<Guid> PublicIdOf(string table, long id) =>
        Ash.SharedKernel.Ids.PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM {table} WHERE id = {id}"))!);

    private static string F(object filters) => Uri.EscapeDataString(JsonSerializer.Serialize(filters));

    private static async Task<List<(string Code, bool CanOpen)>> ActiveRows(HttpClient client, object? filters = null)
    {
        var page = await Json(await client.GetAsync($"{Builder}/active?limit=100&filters={F(filters ?? new { })}", Ct));
        return page.GetProperty("page").GetProperty("items").EnumerateArray()
            .Select(i => (i.GetProperty("caseCode").GetString()!, i.GetProperty("canOpen").GetBoolean())).ToList();
    }

    private static async Task<List<string>> Options(HttpClient client, int table, string key, object? filters = null)
    {
        var meta = await Json(await client.GetAsync($"{Builder}/{table}/filters?filters={F(filters ?? new { })}", Ct));
        var filter = meta.GetProperty("filters").EnumerateArray().Single(f => f.GetProperty("key").GetString() == key);
        return filter.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("value").GetString()!).ToList();
    }

    /// <summary>The numbers of a statistics answer (the table and "Në dorë"), without the texts that name the reader.</summary>
    private static async Task<string> Numbers(HttpClient client, int table, object filters)
    {
        var s = await Json(await client.GetAsync($"{Builder}/statistics/{table}?filters={F(filters)}", Ct));
        return s.GetProperty("courts").GetRawText() + "|" + s.GetProperty("inHand").GetRawText();
    }

    /// <summary>A case of office DR where the ministry (or one of its agencies) is only a PARTY — not linked.</summary>
    private async Task<string> PartyOnlyCaseAsync(CaseSeed s, string label, long institutionId)
    {
        var (id, code) = await s.AddOfficeCaseAsync(s.OfficeDrId, label);
        await Scalar($"""
            INSERT INTO case_parties (case_id, party_role_id, party_kind, institution_id, display_name, created_by)
            SELECT {id}, pr.id, 'INSTITUTION', {institutionId}, 'Institucioni', {s.User("protokoll").Id} FROM party_roles pr WHERE pr.code = 'I_PADITUR'
            """);
        return code;
    }

    // ---------------------------------------------------------------- scope per role

    [Fact]
    public async Task Institution_user_and_jurist_see_every_case_of_the_institution_with_its_agencies_and_identical_numbers()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var ministry = s.User("institucion").InstitutionId!.Value;
        var agency = Convert.ToInt64(await Scalar($"""
            INSERT INTO institutions (name, institution_type, parent_institution_id) VALUES ('Agjencia test {Guid.NewGuid():N}', 'AGENCY', {ministry});
            SELECT LAST_INSERT_ID();
            """));
        try
        {
            var activeCode = (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {s.ActiveCaseId}"))!;
            var partyCode = await PartyOnlyCaseAsync(s, "PARTY", ministry);
            var agencyCode = await PartyOnlyCaseAsync(s, "AGENCY", agency);
            var (_, unrelated) = await s.AddOfficeCaseAsync(s.OfficeDrId, "NONE");
            await using var factory = new AshApiFactory(Db);
            using var institution = factory.CreateClient().AsUser("institucion@ash.local");
            using var jurist = factory.CreateClient().AsUser("jurist@ash.local");

            foreach (var client in new[] { institution, jurist })
            {
                var rows = await ActiveRows(client);
                var codes = rows.Select(r => r.Code).ToList();
                codes.ShouldContain(activeCode);
                codes.ShouldContain(partyCode); // a party, not linked: still the institution's case
                codes.ShouldContain(agencyCode); // a ministry: its direct agencies too
                codes.ShouldNotContain(unrelated);
                // the case page's own rule decides the link: the party-only cases are not opened from here
                rows.Single(r => r.Code == partyCode).CanOpen.ShouldBeFalse();
                rows.Single(r => r.Code == activeCode).CanOpen.ShouldBeTrue();
            }

            // Tabela 5 and 6 for both, identical numbers; the institution filter is fixed to the ministry
            foreach (var table in new[] { 5, 6 })
            {
                (await Numbers(institution, table, new { year = $"{Year}" }))
                    .ShouldBe(await Numbers(jurist, table, new { year = $"{Year}" }), $"Tabela {table}");
            }

            var meta = await Json(await institution.GetAsync($"{Builder}/6/filters", Ct));
            var locked = meta.GetProperty("filters").EnumerateArray().Single(f => f.GetProperty("key").GetString() == "ministry");
            locked.GetProperty("locked").GetBoolean().ShouldBeTrue();
            meta.GetProperty("fixed").GetProperty("ministry").GetString()
                .ShouldBe((await PublicIdOf("institutions", ministry)).ToString());

            // another institution → 403
            (await institution.GetAsync($"{Builder}/statistics/6?filters={F(new { year = $"{Year}", institution = await PublicIdOf("institutions", agency) })}", Ct))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        finally
        {
            await Scalar($"DELETE FROM case_parties WHERE institution_id = {agency}");
            await Scalar($"DELETE FROM institutions WHERE id = {agency}");
        }
    }

    [Fact]
    public async Task Assistant_statistics_for_a_linked_lawyer_equal_the_lawyers_own_and_unlinking_removes_him()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var link = Convert.ToInt64(await Scalar($"""
            INSERT INTO assistant_lawyers (assistant_user_id, lawyer_user_id, office_id, created_by)
            VALUES ({s.User("ndihmes").Id}, {s.User("avokat1").Id}, {s.OfficeTrId}, {s.User("kryetar").Id});
            SELECT LAST_INSERT_ID();
            """));
        try
        {
            await using var factory = new AshApiFactory(Db);
            using var assistant = factory.CreateClient().AsUser("ndihmes@ash.local");
            using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");
            var avokat1 = (await PublicIdOf("users", s.User("avokat1").Id)).ToString();
            var avokat2 = (await PublicIdOf("users", s.User("avokat2").Id)).ToString();

            (await Options(assistant, 5, "lawyer")).ShouldBe([avokat1]);
            var filters = new { year = $"{Year}", lawyer = avokat1 };
            var mine = await Numbers(assistant, 5, filters);
            mine.ShouldBe(await Numbers(lawyer, 5, filters));
            // without a filter: the sum of his lawyers (one here) — the same numbers
            (await Numbers(assistant, 5, new { year = $"{Year}" })).ShouldBe(mine);
            // a lawyer he is not linked to → 403
            (await assistant.GetAsync($"{Builder}/statistics/5?filters={F(new { year = $"{Year}", lawyer = avokat2 })}", Ct))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            await Scalar($"UPDATE assistant_lawyers SET ended_at = UTC_TIMESTAMP(3), ended_by = {s.User("kryetar").Id} WHERE id = {link}");
            (await Options(assistant, 5, "lawyer")).ShouldBeEmpty();
            (await assistant.GetAsync($"{Builder}/statistics/5?filters={F(filters)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        finally
        {
            await Scalar($"DELETE FROM assistant_lawyers WHERE id = {link}");
        }
    }

    [Fact]
    public async Task Secondary_only_lawyer_gets_403_on_every_statistics_endpoint_and_reports_with_my_role()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await Scalar($"""
            UPDATE case_assignments SET user_id = {s.User("sekondar").Id}
            WHERE case_id = {s.ActiveCaseId} AND case_role = 'SECONDARY_LAWYER' AND ended_at IS NULL
            """);
        await using var factory = new AshApiFactory(Db);
        using var sekondar = factory.CreateClient().AsUser("sekondar@ash.local");

        (await sekondar.GetAsync($"{Builder}/statistics/5?filters={F(new { year = $"{Year}" })}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await sekondar.GetAsync($"{Builder}/5/filters", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await sekondar.GetAsync($"{Builder}/5/export?filters={F(new { year = $"{Year}" })}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var page = await Json(await sekondar.GetAsync($"{Builder}/active?limit=100", Ct));
        var item = page.GetProperty("page").GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        item.GetProperty("myRole").GetString().ShouldBe("Avokat sekondar");
    }

    // ---------------------------------------------------------------- dependent filters

    [Fact]
    public async Task Dependent_filters_offer_only_values_under_the_parent_and_inside_the_scope()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await s.AddOfficeCaseAsync(s.OfficeDrId, "DR");
        await using var factory = new AshApiFactory(Db);
        using var monitor = factory.CreateClient().AsUser("monitor@ash.local");
        using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");
        var tr = (await PublicIdOf("offices", s.OfficeTrId)).ToString();
        var dr = (await PublicIdOf("offices", s.OfficeDrId)).ToString();
        var firstInstance = Convert.ToString(await Scalar("SELECT id FROM courts WHERE name = 'Gjykata e Shkallës së Parë Tiranë'"));

        // the scope: the Monitor sees both offices, the lawyer only the offices of his own cases
        (await Options(monitor, 1, "office")).ShouldContain(dr);
        (await Options(lawyer, 1, "office")).ShouldBe([tr]);

        // zyra → avokati: under DR only DR's lawyers (nobody holds a DR case here), under TR avokat1 and avokat2
        var avokat1 = (await PublicIdOf("users", s.User("avokat1").Id)).ToString();
        (await Options(monitor, 1, "lawyer", new { office = tr })).ShouldContain(avokat1);
        (await Options(monitor, 1, "lawyer", new { office = dr })).ShouldNotContain(avokat1);

        // shkalla → gjykata: under the appeal level, no first-instance court
        (await Options(monitor, 1, "court", new { courtLevel = "FIRST_INSTANCE" })).ShouldContain(firstInstance);
        (await Options(monitor, 1, "court", new { courtLevel = "APPEAL" })).ShouldNotContain(firstInstance);

        // a filter that does not exist, or a value that cannot be read → 400
        (await monitor.GetAsync($"{Builder}/1/filters?filters={F(new { nope = "x" })}", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await monitor.GetAsync($"{Builder}/active?filters={F(new { nextHearingAt = new { from = "31.12.2026" } })}", Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await monitor.GetAsync($"{Builder}/9/filters", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Filters_narrow_the_rows_and_the_totals_row_counts_every_row()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await Scalar($"UPDATE cases SET claim_amount = 1000, claim_currency = 'ALL' WHERE id = {s.ActiveCaseId}");
        var code = (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {s.ActiveCaseId}"))!;
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");

        var page = await Json(await kryetar.GetAsync($"{Builder}/active?limit=100&filters={F(new { caseCode = code, claimAmount = new { min = 500 } })}", Ct));
        page.GetProperty("page").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("caseCode").GetString()).ShouldBe([code]);
        var totals = page.GetProperty("totals");
        totals.GetProperty("rows").GetInt32().ShouldBe(1);
        var claim = totals.GetProperty("sums").EnumerateArray().Single(t => t.GetProperty("key").GetString() == "claimAmount");
        claim.GetProperty("currency").GetString().ShouldBe("ALL");
        claim.GetProperty("value").GetDecimal().ShouldBe(1000m);

        (await ActiveRows(kryetar, new { caseCode = code, claimAmount = new { min = 5000 } })).ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- exports

    [Fact]
    public async Task Each_format_exports_the_chosen_columns_and_is_audited()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");
        var columns = "caseCode,court,myRole";

        var csv = await kryetar.GetAsync($"{Builder}/1/export?format=csv&columns={columns}", Ct);
        csv.StatusCode.ShouldBe(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        var bytes = await csv.Content.ReadAsByteArrayAsync(Ct);
        bytes.Take(3).ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF }); // the BOM, so Excel reads ë / ç
        Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n")[0].ShouldBe("Kodi unik;Gjykata;Roli im");

        var pdf = await kryetar.GetAsync($"{Builder}/1/export?format=pdf&columns={columns}", Ct);
        pdf.StatusCode.ShouldBe(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync(Ct)).Take(5).ToArray()).ShouldBe("%PDF-");

        var xlsx = await kryetar.GetAsync($"{Builder}/3/export?format=xlsx", Ct);
        xlsx.StatusCode.ShouldBe(HttpStatusCode.OK);
        xlsx.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var stats = await kryetar.GetAsync($"{Builder}/5/export?format=csv&filters={F(new { year = $"{Year}" })}&columns=court,total", Ct);
        stats.StatusCode.ShouldBe(HttpStatusCode.OK);
        var statsText = Encoding.UTF8.GetString((await stats.Content.ReadAsByteArrayAsync(Ct))[3..]);
        // task 053o: the column's name with the client's groups above it (CSV is flat)
        statsText.Split("\r\n")[0].ShouldBe("Gjykata;Gjithsej çështje ardhur në Avokaturën e Shtetit — TOTAL");

        (await kryetar.GetAsync($"{Builder}/1/export?format=docx", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await kryetar.GetAsync($"{Builder}/1/export?format=csv&columns=nope", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // each export is audited with its format (the audit is written in the background: wait, with a limit)
        var kryetarId = s.User("kryetar").Id;
        var formats = new List<string>();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (formats.Count < 4 && DateTime.UtcNow < deadline)
        {
            await using var connection = new MySqlConnection(Db.AuditConnectionString);
            await connection.OpenAsync(Ct);
            await using var command = new MySqlCommand($"""
                SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.format')) FROM ash_audit.audit_log
                 WHERE action = 'REPORT_EXPORT' AND user_id = {kryetarId} AND occurred_at > UTC_TIMESTAMP(3) - INTERVAL 5 MINUTE
                """, connection);
            formats.Clear();
            await using var reader = await command.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                formats.Add(reader.IsDBNull(0) ? "" : reader.GetString(0));
            }

            if (formats.Count < 4)
            {
                await Task.Delay(100, Ct);
            }
        }

        formats.ShouldBe(["csv", "pdf", "xlsx", "csv"], ignoreOrder: true);
    }

    // ---------------------------------------------------------------- saved reports

    [Fact]
    public async Task Saved_reports_are_private_named_once_and_keep_table_filters_and_columns()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");
        using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");
        var tr = (await PublicIdOf("offices", s.OfficeTrId)).ToString();
        var body = new { name = "  Zyra ime  ", table = 1, filters = new { office = tr }, columns = new[] { "caseCode", "court" } };

        var created = await kryetar.PostAsJsonAsync("/api/reports/saved", body, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var report = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        report.GetProperty("name").GetString().ShouldBe("Zyra ime");
        report.GetProperty("filters").GetProperty("office").GetString().ShouldBe(tr);
        report.GetProperty("columns").EnumerateArray().Select(c => c.GetString()).ShouldBe(["caseCode", "court"]);
        var id = report.GetProperty("publicId").GetGuid();

        // the same name again → 409; bad filters / columns / table → 400
        (await kryetar.PostAsJsonAsync("/api/reports/saved", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await kryetar.PostAsJsonAsync("/api/reports/saved", new { name = "x", table = 1, filters = new { nope = 1 }, columns = Array.Empty<string>() }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await kryetar.PostAsJsonAsync("/api/reports/saved", new { name = "y", table = 1, filters = new { }, columns = new[] { "nope" } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await kryetar.PostAsJsonAsync("/api/reports/saved", new { name = "z", table = 2, filters = new { }, columns = Array.Empty<string>() }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // private: another user neither lists, changes nor deletes it
        (await Json(await lawyer.GetAsync("/api/reports/saved", Ct))).EnumerateArray().ShouldBeEmpty();
        (await lawyer.PutAsJsonAsync($"/api/reports/saved/{id}", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await lawyer.DeleteAsync($"/api/reports/saved/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // overwrite with Tabela 5, then delete
        var updated = await Json(await kryetar.PutAsJsonAsync($"/api/reports/saved/{id}",
            new { name = "Statistika", table = 5, filters = new { year = $"{Year}" }, columns = new[] { "court", "total" } }, Ct));
        updated.GetProperty("table").GetInt32().ShouldBe(5);
        (await Json(await kryetar.GetAsync("/api/reports/saved", Ct))).EnumerateArray()
            .Select(r => r.GetProperty("name").GetString()).ShouldBe(["Statistika"]);
        (await kryetar.DeleteAsync($"/api/reports/saved/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Json(await kryetar.GetAsync("/api/reports/saved", Ct))).EnumerateArray().ShouldBeEmpty();

        using var protokoll = factory.CreateClient().AsUser("protokoll@ash.local");
        (await protokoll.GetAsync("/api/reports/saved", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
