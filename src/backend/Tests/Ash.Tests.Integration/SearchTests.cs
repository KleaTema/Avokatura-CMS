using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Serilog.Core;
using Serilog.Events;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 047 — the header search GET /api/search. Uses <see cref="CaseSeed"/> (office TR with kryetar, office DR) plus
/// a few cases written here with known codes, objects, parties and a judge.
/// </summary>
public class SearchTests(MySqlFixture db) : IntegrationTest(db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<object?> Scalar(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var result = await command.ExecuteScalarAsync(Ct);
        return result is DBNull ? null : result;
    }

    /// <summary>A case (level 1 of a new dossier) with this code, act number and object; returns its id.</summary>
    private async Task<long> CaseAsync(CaseSeed s, long officeId, string code, string act, string objectText,
        string status = "ACTIVE", bool archived = false, string? judge = null)
    {
        var user = s.User("kryetar").Id;
        var dossierCode = code[..code.IndexOf('/')];
        var dossier = Convert.ToInt64(await Scalar($"""
            INSERT INTO dossiers (unique_code, code_format_id, office_id, opened_by{(archived ? ", archived_at, archived_by" : "")})
            VALUES (@c, (SELECT id FROM dossier_code_formats WHERE is_active = 1), @o, @u{(archived ? ", UTC_TIMESTAMP(3), @u" : "")});
            SELECT LAST_INSERT_ID();
            """, ("@c", dossierCode), ("@o", officeId), ("@u", user)));
        var closed = status == "CLOSED";
        return Convert.ToInt64(await Scalar($"""
            INSERT INTO cases (dossier_id, level_seq, case_code, office_id, summons_number, act_number, received_date, court_level_id,
                               court_id, judge_id, object_text, lifecycle_status, registered_by{(closed ? ", closed_at, closed_by" : "")})
            VALUES (@d, 1, @code, @o, @summons, @act, CURDATE(), (SELECT id FROM court_levels WHERE code = 'FIRST_INSTANCE'),
                    (SELECT id FROM courts WHERE name = 'Gjykata e Shkallës së Parë Tiranë'),
                    (SELECT id FROM judges WHERE CONCAT(first_name, ' ', last_name) = @judge), @obj, @status, @u{(closed ? ", UTC_TIMESTAMP(3), @u" : "")});
            SELECT LAST_INSERT_ID();
            """, ("@d", dossier), ("@code", code), ("@o", officeId), ("@summons", $"FL-{act}"), ("@act", act), ("@judge", judge),
            ("@obj", objectText), ("@status", status), ("@u", user)));
    }

    private Task PartyAsync(CaseSeed s, long caseId, string name) =>
        Scalar("""
            INSERT INTO case_parties (case_id, party_role_id, party_kind, display_name, created_by)
            VALUES (@c, (SELECT id FROM party_roles WHERE code = 'PADITES'), 'NATURAL', @n, @u)
            """, ("@c", caseId), ("@n", name), ("@u", s.User("protokoll").Id));

    private static async Task<JsonElement> Search(HttpClient client, string q, int limit = 20)
    {
        var response = await client.GetAsync($"/api/search?q={Uri.EscapeDataString(q)}&limit={limit}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, q);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static List<JsonElement> Items(JsonElement page) => page.GetProperty("items").EnumerateArray().ToList();

    private static string Code(JsonElement item) => item.GetProperty("caseCode").GetString()!;

    [Fact]
    public async Task Hoxha_finds_the_case_where_Petrit_Hoxha_is_a_party_only_within_the_users_scope()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var mine = await CaseAsync(s, s.OfficeTrId, "2026-101010/1", "AKT-H1", "Kontratë qiraje");
        var other = await CaseAsync(s, s.OfficeDrId, "2026-202020/1", "AKT-H2", "Kontratë shitjeje");
        await PartyAsync(s, mine, "Petrit Hoxha");
        await PartyAsync(s, other, "Petrit Hoxha");
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");

        var page = await Search(kryetar, "Hoxha");

        var item = Items(page).ShouldHaveSingleItem(); // the Durrës case is another office's
        Code(item).ShouldBe("2026-101010/1");
        item.GetProperty("matchedOn").GetString().ShouldBe("PARTY");
        item.GetProperty("snippet").GetString().ShouldBe("Pala: Petrit Hoxha");
        page.GetProperty("totalApprox").GetInt32().ShouldBe(1);
        JsonAssert.NoNumericIds(page);
    }

    [Fact]
    public async Task An_exact_code_ranks_above_an_object_that_contains_the_same_digits()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        // the object match is newer: only the rank puts the exact code first
        await CaseAsync(s, s.OfficeTrId, "2026-555123/1", "AKT-E1", "Padi");
        await CaseAsync(s, s.OfficeTrId, "2026-777000/1", "AKT-E2", "Vazhdim i çështjes 2026-555123/1 në apel");
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");

        var items = Items(await Search(kryetar, "2026-555123/1"));

        items.Select(Code).ShouldBe(["2026-555123/1", "2026-777000/1"]);
        items[0].GetProperty("matchedOn").GetString().ShouldBe("CODE");
        items[1].GetProperty("matchedOn").GetString().ShouldBe("OBJECT");
        items[1].GetProperty("snippet").GetString()!.ShouldContain("2026-555123/1");
    }

    [Fact]
    public async Task Part_of_a_code_accents_a_judge_and_closed_or_archived_cases_are_found()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await CaseAsync(s, s.OfficeTrId, "2026-686640/1", "AKT-P1", "Padi");
        await CaseAsync(s, s.OfficeTrId, "2026-300001/1", "AKT-P2",
            "Kërkesë për pushimin e çështjes civile të ngritur nga paditësi kundër shoqërisë, me objekt dëmshpërblimin e shkaktuar", status: "CLOSED");
        await CaseAsync(s, s.OfficeTrId, "2026-300002/1", "AKT-P3", "Padi tjetër", judge: "Ilir Gjyqtari", status: "CLOSED", archived: true);
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");

        var byPart = Items(await Search(kryetar, "640")).ShouldHaveSingleItem();
        Code(byPart).ShouldBe("2026-686640/1");
        byPart.GetProperty("matchedOn").GetString().ShouldBe("CODE");

        var byAccents = Items(await Search(kryetar, "ceshtjes")).ShouldHaveSingleItem();
        Code(byAccents).ShouldBe("2026-300001/1");
        byAccents.GetProperty("status").GetString().ShouldBe("CLOSED");
        var snippet = byAccents.GetProperty("snippet").GetString()!;
        snippet.ShouldContain("çështjes");
        snippet.Length.ShouldBeLessThanOrEqualTo(82); // 80 characters and the ellipses
        snippet.ShouldEndWith("…");

        var byJudge = Items(await Search(kryetar, "Gjyqtari")).Single(i => Code(i) == "2026-300002/1");
        byJudge.GetProperty("matchedOn").GetString().ShouldBe("JUDGE");
        byJudge.GetProperty("snippet").GetString().ShouldBe("Gjyqtari: Ilir Gjyqtari");
        byJudge.GetProperty("isArchived").GetBoolean().ShouldBeTrue();
    }

    [Theory]
    [InlineData("Hoxha")]
    [InlineData("640")]
    [InlineData("ceshtje")]
    [InlineData("Gjyqtari")]
    [InlineData("AKT-S")]
    public async Task The_header_search_and_the_case_list_return_the_same_cases(string q)
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var a = await CaseAsync(s, s.OfficeTrId, "2026-686640/1", "AKT-S1", "Çështje pronësie", judge: "Ilir Gjyqtari");
        var b = await CaseAsync(s, s.OfficeDrId, "2026-640999/1", "AKT-S2", "Padi për çështje pune", status: "CLOSED");
        await PartyAsync(s, a, "Petrit Hoxha");
        await PartyAsync(s, b, "Arben Hoxha");
        await using var factory = new AshApiFactory(Db);
        using var monitor = factory.CreateClient().AsUser("monitor@ash.local");

        var search = await Search(monitor, q);
        var list = await monitor.GetFromJsonAsync<JsonElement>($"/api/cases?tab=all&status=ALL&q={Uri.EscapeDataString(q)}&limit=100", Ct);

        var listCodes = list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("caseCode").GetString()).Order().ToList();
        listCodes.ShouldNotBeEmpty(q);
        Items(search).Select(Code).Order().ToList().ShouldBe(listCodes!, $"query {q}");
        search.GetProperty("totalApprox").GetInt32().ShouldBe(list.GetProperty("total").GetInt32(), q);
    }

    [Fact]
    public async Task Too_short_or_too_long_text_is_400()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("monitor@ash.local");

        (await client.GetAsync("/api/search?q=%20a%20", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync($"/api/search?q={new string('x', 101)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/search", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }

    [Fact]
    public async Task The_search_text_never_reaches_the_logs()
    {
        const string secret = "Sekretisimo";
        var sink = new CapturingSink();
        await using var factory = new AshApiFactory(Db, configureServices: services => services.AddSingleton<ILogEventSink>(sink));
        using var client = factory.CreateClient().AsUser("monitor@ash.local");

        (await client.GetAsync($"/api/search?q={secret}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"/api/cases?tab=all&q={secret}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"/api/cases/registration-search?q={secret}", Ct)).StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.Forbidden);
        // whatever logs a full URL is masked too
        Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
            factory.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SearchTests>>(),
            "Calling {Url}", $"http://localhost/api/search?q={secret}&limit=8");

        var events = sink.Events.ToList();
        events.ShouldContain(e => e.Properties.ContainsKey("RequestPath") && e.Properties["RequestPath"].ToString().Contains("/api/search"));
        foreach (var e in events)
        {
            (e.RenderMessage() + " " + string.Join(" ", e.Properties.Values.Select(v => v.ToString())) + " " + e.Exception)
                .ShouldNotContain(secret);
        }

        events.ShouldContain(e => e.RenderMessage().Contains("q=***&limit=8"));
    }
}
