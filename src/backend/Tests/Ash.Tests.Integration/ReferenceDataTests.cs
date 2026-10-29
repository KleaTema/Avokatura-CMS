using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Modules.Reference.Contracts;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

public class ReferenceDataTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string User = "avokat1@ash.local";

    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _count);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(result);
        }
    }

    private AshApiFactory CreateFactory(CommandCounter? counter = null) =>
        new(Db, configureServices: services =>
        {
            if (counter is not null)
            {
                services.AddSingleton<IInterceptor>(counter);
            }
        });

    private static async Task<JsonElement> GetJson(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private static string[] Strings(JsonElement list, string property) =>
        list.EnumerateArray().Select(x => x.GetProperty(property).GetString()!).ToArray();

    private async Task Execute(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<object?> ScalarAsync(string sql, string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Endpoints_require_authentication()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/reference/courts", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Every_list_returns_seeded_values()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().AsUser(User);

        Strings(await GetJson(client, "/api/reference/court-levels"), "code").ShouldContain("FIRST_INSTANCE");
        var courts = await GetJson(client, "/api/reference/courts");
        Strings(courts, "name").ShouldContain("Gjykata e Lartë");
        var levelByCourt = courts.EnumerateArray().ToDictionary(
            x => x.GetProperty("name").GetString()!, x => x.GetProperty("courtLevelCode").GetString());
        levelByCourt["Gjykata e Shkallës së Parë Tiranë"].ShouldBe("FIRST_INSTANCE");
        levelByCourt["Gjykata e Shkallës së Parë Durrës"].ShouldBe("FIRST_INSTANCE");
        levelByCourt["Gjykata e Apelit Tiranë"].ShouldBe("APPEAL");
        levelByCourt["Gjykata e Apelit Shkodër"].ShouldBe("APPEAL");
        levelByCourt["Gjykata e Lartë"].ShouldBe("HIGH_COURT");
        Strings(await GetJson(client, "/api/reference/party-roles"), "code").ShouldContain("PADITES");
        (await GetJson(client, "/api/reference/summons-types")).ValueKind.ShouldBe(JsonValueKind.Array);
        (await GetJson(client, "/api/reference/hearing-types")).GetArrayLength().ShouldBeGreaterThan(0);
        (await GetJson(client, "/api/reference/document-types")).GetArrayLength().ShouldBeGreaterThan(0);
        Strings(await GetJson(client, "/api/reference/decision-results"), "code").ShouldContain("KTHIM_RIGJYKIM");
        Strings(await GetJson(client, "/api/reference/case-categories"), "code").ShouldNotBeEmpty();
        Strings(await GetJson(client, "/api/reference/case-subcategories"), "code").ShouldNotBeEmpty();
        (await GetJson(client, "/api/reference/allowed-file-types")).GetArrayLength().ShouldBeGreaterThan(0);
        (await GetJson(client, "/api/reference/hearing-types")).EnumerateArray().First().TryGetProperty("isOther", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Inactive_rows_are_hidden_from_lists_but_returned_by_Get()
    {
        await Execute("INSERT INTO courts (name, court_level_id, is_active) " +
                      "SELECT 'Gjykata e Mbyllur Test', id, 0 FROM court_levels WHERE code = 'FIRST_INSTANCE'");
        await Execute("INSERT INTO hearing_types (code, name, is_active) VALUES ('TEST_INACTIVE', 'Seancë e pasaktë', 0)");
        try
        {
            await using var factory = CreateFactory();
            using var client = factory.CreateClient().AsUser(User);

            Strings(await GetJson(client, "/api/reference/courts"), "name").ShouldNotContain("Gjykata e Mbyllur Test");
            Strings(await GetJson(client, "/api/reference/hearing-types"), "code").ShouldNotContain("TEST_INACTIVE");

            await using var scope = factory.Services.CreateAsyncScope();
            var data = scope.ServiceProvider.GetRequiredService<IReferenceData>();
            var hearingType = await data.GetHearingType("TEST_INACTIVE", TestContext.Current.CancellationToken);
            hearingType.ShouldNotBeNull();
            hearingType.IsActive.ShouldBeFalse();
            var court = (await data.ListCourtsAsync(TestContext.Current.CancellationToken)).Single(x => x.Name == "Gjykata e Mbyllur Test");
            (await data.GetCourt(court.Id, TestContext.Current.CancellationToken)).ShouldNotBeNull();
            (await data.GetResultByCode("KTHIM_RIGJYKIM", TestContext.Current.CancellationToken)).ShouldNotBeNull();
            (await data.GetCourtLevelByCode("FIRST_INSTANCE", TestContext.Current.CancellationToken))!.AllowsDelegation.ShouldBeTrue();
        }
        finally
        {
            await Execute("DELETE FROM courts WHERE name = 'Gjykata e Mbyllur Test'");
            await Execute("DELETE FROM hearing_types WHERE code = 'TEST_INACTIVE'");
        }
    }

    [Theory]
    [InlineData("cela", "Çela")]
    [InlineData("Çela", "Çela")]
    [InlineData("deshira", "Dëshira")]
    [InlineData("Dëshira", "Dëshira")]
    [InlineData("klodiana", "Çela")]
    [InlineData("arber", "Dëshira")]
    public async Task Judge_search_works_with_and_without_diacritics(string q, string expectedLastName)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().AsUser(User);

        var result = await GetJson(client, $"/api/reference/judges?q={Uri.EscapeDataString(q)}");

        Strings(result, "lastName").ShouldContain(expectedLastName);
    }

    [Fact]
    public async Task Judge_search_filters_by_court_and_limits_results()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().AsUser(User);

        var courts = await GetJson(client, "/api/reference/courts");
        var tirana = courts.EnumerateArray().Single(x => x.GetProperty("name").GetString() == "Gjykata e Shkallës së Parë Tiranë")
            .GetProperty("id").GetInt32();

        var result = await GetJson(client, $"/api/reference/judges?courtId={tirana}");

        var names = Strings(result, "lastName");
        names.ShouldContain("Gjyqtari");
        names.ShouldContain("Çela");
        names.ShouldContain("Dëshira");
        result.EnumerateArray().All(x => x.GetProperty("courtId").GetInt32() == tirana).ShouldBeTrue();
        (await GetJson(client, "/api/reference/judges")).GetArrayLength().ShouldBeLessThanOrEqualTo(50);
    }

    [Fact]
    public async Task Second_call_within_cache_lifetime_runs_no_sql()
    {
        var counter = new CommandCounter();
        await using var factory = CreateFactory(counter);
        using var client = factory.CreateClient().AsUser(User);

        await GetJson(client, "/api/reference/courts");
        var afterFirst = counter.Count;
        afterFirst.ShouldBeGreaterThan(0);

        await GetJson(client, "/api/reference/courts");
        await GetJson(client, "/api/reference/judges?q=cela");
        await GetJson(client, "/api/reference/judges?q=cela");

        // courts needs court_levels + courts on first call; the judges list loads once, nothing else may hit the database
        var afterJudgesLoad = counter.Count;
        await GetJson(client, "/api/reference/courts");
        await GetJson(client, "/api/reference/judges?q=deshira");
        counter.Count.ShouldBe(afterJudgesLoad);
    }

    [Fact]
    public async Task Admin_can_create_and_rename_hearing_type()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().AsUser("admin@ash.local");
        var code = "ADMIN_HEARING_TEST";

        try
        {
            var create = await client.PostAsJsonAsync("/api/reference/admin/hearing-types", new
            {
                code,
                name = "Seanca e parë",
                sortOrder = 99,
                isOther = false
            }, TestContext.Current.CancellationToken);
            create.StatusCode.ShouldBe(HttpStatusCode.Created);

            await create.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            var adminList = await GetJson(client, "/api/reference/admin/hearing-types");
            adminList.EnumerateArray().ShouldContain(x => x.GetProperty("code").GetString() == code);

            var rename = await client.PutAsJsonAsync($"/api/reference/admin/hearing-types/{code}", new
            {
                name = "Seanca e riemërtuar",
                sortOrder = 100,
                isOther = true
            }, TestContext.Current.CancellationToken);
            rename.StatusCode.ShouldBe(HttpStatusCode.OK);

            await rename.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            var renamed = await GetJson(client, "/api/reference/admin/hearing-types");
            renamed.EnumerateArray().Single(x => x.GetProperty("code").GetString() == code)
                .GetProperty("name").GetString().ShouldBe("Seanca e riemërtuar");
        }
        finally
        {
            await Execute($"DELETE FROM hearing_types WHERE code = '{code}'");
        }
    }

    [Fact]
    public async Task Duplicate_hearing_type_code_is_rejected_with_400()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().AsUser("admin@ash.local");
        var code = "ADMIN_HEARING_DUP";

        try
        {
            await Execute($"INSERT INTO hearing_types (code, name, is_other, sort_order, is_active) VALUES ('{code}', 'Origjinal', 0, 1, 1)");
            var response = await client.PostAsJsonAsync("/api/reference/admin/hearing-types", new
            {
                code,
                name = "Duble",
                sortOrder = 2,
                isOther = false
            }, TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        finally
        {
            await Execute($"DELETE FROM hearing_types WHERE code = '{code}'");
        }
    }

    [Fact]
    public async Task Admin_deactivated_hearing_type_disappears_from_public_reference_and_still_resolves_by_code()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().AsUser("admin@ash.local");
        var code = "ADMIN_HEARING_INACTIVE";

        try
        {
            var create = await client.PostAsJsonAsync("/api/reference/admin/hearing-types", new
            {
                code,
                name = "Seanca e çaktivizuar",
                sortOrder = 11,
                isOther = false
            }, TestContext.Current.CancellationToken);
            create.StatusCode.ShouldBe(HttpStatusCode.Created);

            var deactivate = await client.PostAsync($"/api/reference/admin/hearing-types/{code}/deactivate", null, TestContext.Current.CancellationToken);
            deactivate.StatusCode.ShouldBe(HttpStatusCode.OK);

            await deactivate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            var publicList = await GetJson(client, "/api/reference/hearing-types");
            publicList.EnumerateArray().Select(x => x.GetProperty("code").GetString()).ShouldNotContain(code);

            await using var scope = factory.Services.CreateAsyncScope();
            var data = scope.ServiceProvider.GetRequiredService<IReferenceData>();
            var resolved = await data.GetHearingType(code, TestContext.Current.CancellationToken);
            resolved.ShouldNotBeNull();
            resolved.IsActive.ShouldBeFalse();
        }
        finally
        {
            await Execute($"DELETE FROM hearing_types WHERE code = '{code}'");
        }
    }

    [Fact]
    public async Task Non_admin_cannot_manage_hearing_types()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().AsUser(User);

        var response = await client.GetAsync("/api/reference/admin/hearing-types", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_can_manage_file_types()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().AsUser("admin@ash.local");

        try
        {
            var blocked = await client.PostAsJsonAsync("/api/reference/admin/file-types", new
            {
                extension = "exe",
                contentType = "application/x-msdownload",
                magicHex = "4D5A",
                maxSizeBytes = 12345,
                isActive = true,
                appliesTo = "DOCUMENT"
            }, TestContext.Current.CancellationToken);
            blocked.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var blockedBody = await blocked.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            blockedBody.GetProperty("title").GetString()!.ShouldContain("nuk lejohet");

            var created = await client.PostAsJsonAsync("/api/reference/admin/file-types", new
            {
                extension = "custompdf",
                contentType = "application/pdf",
                magicHex = "25504446",
                maxSizeBytes = 1048576,
                isActive = true,
                appliesTo = "DOCUMENT"
            }, TestContext.Current.CancellationToken);
            created.StatusCode.ShouldBe(HttpStatusCode.Created);
            var file = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            var fileId = file.GetProperty("id").GetByte();
            fileId.ShouldBeGreaterThan((byte)0);

            var update = await client.PutAsJsonAsync($"/api/reference/admin/file-types/{fileId}", new
            {
                extension = "custompdf",
                contentType = "application/octet-stream",
                magicHex = "FF",
                maxSizeBytes = 2048,
                isActive = false,
                appliesTo = "DOCUMENT"
            }, TestContext.Current.CancellationToken);
            update.StatusCode.ShouldBe(HttpStatusCode.OK);

            var immutable = await client.PutAsJsonAsync($"/api/reference/admin/file-types/{fileId}", new
            {
                extension = "otherpdf",
                contentType = "application/pdf",
                magicHex = "25504446",
                maxSizeBytes = 4096,
                isActive = true,
                appliesTo = "DOCUMENT"
            }, TestContext.Current.CancellationToken);
            immutable.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        finally
        {
            await Execute("DELETE FROM allowed_file_types WHERE extension = 'custompdf'");
        }
    }

    [Fact]
    public async Task Admin_can_create_and_activate_dossier_code_format()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().AsUser("admin@ash.local");
        var originalId = Convert.ToByte(await ScalarAsync(
            "SELECT id FROM dossier_code_formats WHERE is_active = 1", Db.AppConnectionString));

        try
        {
            var tempBad = await client.PostAsJsonAsync("/api/reference/admin/dossier-code-formats", new
            {
                template = "{YYYY}-{NOTHING}-{RAND}",
                randomDigits = 6,
                note = "bad"
            }, TestContext.Current.CancellationToken);
            tempBad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

            var formatCreate = await client.PostAsJsonAsync("/api/reference/admin/dossier-code-formats", new
            {
                template = "{YYYY}-{OFFICE}-{RAND}",
                randomDigits = 6,
                note = "test"
            }, TestContext.Current.CancellationToken);
            formatCreate.StatusCode.ShouldBe(HttpStatusCode.Created);
            var format = await formatCreate.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            var formatId = format.GetProperty("id").GetByte();
            formatId.ShouldBeGreaterThan((byte)0);

            var activate = await client.PostAsync($"/api/reference/admin/dossier-code-formats/{formatId}/activate", null, TestContext.Current.CancellationToken);
            activate.StatusCode.ShouldBe(HttpStatusCode.OK);

            var activeCount = await ScalarAsync("SELECT COUNT(*) FROM dossier_code_formats WHERE is_active = 1", Db.AppConnectionString);
            activeCount.ShouldBe(1L);

            var officeId = await ScalarAsync("SELECT id FROM offices WHERE code = 'TR' LIMIT 1", Db.AppConnectionString);
            var userId = await ScalarAsync("SELECT id FROM users WHERE email = 'admin@ash.local' LIMIT 1", Db.AppConnectionString);
            await using var connection = new MySqlConnection(Db.AppConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new MySqlCommand("CALL create_dossier(@office_id, @user_id, @dossier_id, @code)", connection);
            var officeParam = new MySqlParameter("@office_id", MySqlDbType.UInt16) { Value = officeId };
            var userParam = new MySqlParameter("@user_id", MySqlDbType.UInt32) { Value = userId };
            var idParam = new MySqlParameter("@dossier_id", MySqlDbType.UInt64) { Direction = ParameterDirection.Output };
            var codeParam = new MySqlParameter("@code", MySqlDbType.VarChar, 30) { Direction = ParameterDirection.Output };
            command.Parameters.AddRange(new MySqlParameter[] { officeParam, userParam, idParam, codeParam });
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            var newCode = (string?)codeParam.Value;
            newCode.ShouldNotBeNullOrWhiteSpace();
            newCode.ShouldMatch(@"^\d{4}-[A-Z0-9_]+-\d{6}$");
        }
        finally
        {
            var restore = await client.PostAsync($"/api/reference/admin/dossier-code-formats/{originalId}/activate", null, TestContext.Current.CancellationToken);
            restore.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var activeIds = await ScalarAsync(
            "SELECT GROUP_CONCAT(id) FROM dossier_code_formats WHERE is_active = 1", Db.AppConnectionString);
        activeIds.ShouldBe(originalId.ToString());
    }

    [Fact]
    public async Task Dossier_code_format_that_would_exceed_30_chars_is_rejected_with_400()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().AsUser("admin@ash.local");
        var longestOffice = Convert.ToInt32(await ScalarAsync(
            "SELECT MAX(CHAR_LENGTH(code)) FROM offices", Db.AppConnectionString));

        // {YYYY}-{OFFICE}-{RAND} with 12 digits expands to 18 + longest office code; pad to exactly 31
        var prefix = new string('A', Math.Max(0, 13 - longestOffice));
        var template = prefix + "{YYYY}-{OFFICE}-{RAND}";
        template.Length.ShouldBeLessThanOrEqualTo(40);

        var response = await client.PostAsJsonAsync("/api/reference/admin/dossier-code-formats", new
        {
            template,
            randomDigits = 12,
            note = "too long"
        }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("title").GetString().ShouldBe("Kodi do të ishte më i gjatë se 30 karaktere.");
    }

    [Fact]
    public async Task Non_admin_cannot_manage_file_types_and_dossier_code_formats()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().AsUser(User);

        (await client.GetAsync("/api/reference/admin/file-types", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/reference/admin/dossier-code-formats", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invalidate_forces_a_reload()
    {
        var counter = new CommandCounter();
        await using var factory = CreateFactory(counter);
        using var client = factory.CreateClient().AsUser(User);

        await GetJson(client, "/api/reference/courts");
        var before = counter.Count;

        factory.Services.GetRequiredService<IReferenceCache>().Invalidate();
        await GetJson(client, "/api/reference/courts");

        counter.Count.ShouldBeGreaterThan(before);
    }
}
