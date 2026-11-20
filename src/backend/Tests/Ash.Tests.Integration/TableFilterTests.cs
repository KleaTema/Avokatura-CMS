using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 053m — GET /api/filters/{table}: ONE metadata source per table for the shared FilterBar. Every table: 2–3
/// defaults, unique keys and URL parameters, a parent that exists; an admin table only with its permission; the case
/// list's office / lawyer filters by the user's scope; a child's options name their parent's value.
/// </summary>
public class TableFilterTests(MySqlFixture db) : IntegrationTest(db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Option(string Value, string Label, string? Parent);

    private sealed record Filter(string Key, string Label, string Kind, string? Parent, bool IsDefault, List<string> Params, List<Option> Options);

    private static async Task<List<Filter>> Get(HttpClient client, string table, string query = "")
    {
        var response = await client.GetAsync($"/api/filters/{table}{query}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{table}: {await response.Content.ReadAsStringAsync(Ct)}");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return json.EnumerateArray().Select(f => new Filter(
            f.GetProperty("key").GetString()!,
            f.GetProperty("label").GetString()!,
            f.GetProperty("kind").GetString()!,
            f.TryGetProperty("parent", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null,
            f.GetProperty("isDefault").GetBoolean(),
            f.GetProperty("params").EnumerateArray().Select(x => x.GetString()!).ToList(),
            f.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array
                ? o.EnumerateArray().Select(x => new Option(x.GetProperty("value").GetString()!, x.GetProperty("label").GetString()!,
                    x.TryGetProperty("parent", out var op) && op.ValueKind == JsonValueKind.String ? op.GetString() : null)).ToList()
                : [])).ToList();
    }

    [Theory]
    [InlineData("kryetar@ash.local", "office", "REGISTERED:Regjistruar,ACTIVE:Aktive")]
    [InlineData("monitor@ash.local", "all", "REGISTERED:Regjistruar,ACTIVE:Aktive")]
    [InlineData("avokat1@ash.local", "mine", "REGISTERED:Regjistruar,ACTIVE:Aktive")]
    [InlineData("kryetar@ash.local", "unassigned", "")] // REGISTERED only: no status filter
    [InlineData("protokoll@ash.local", "registered", "REGISTERED:Regjistruar,ACTIVE:Aktive")] // task 054d
    public async Task The_case_status_filter_offers_the_statuses_of_the_tab_with_the_chip_names_and_no_duplicates(
        string user, string tab, string expected)
    {
        // task 054c: the empty choice is "Të gjitha"; no "ALL" / "OPEN" option beside it
        await using var factory = new AshApiFactory(Db);
        var status = (await Get(factory.CreateClient().AsUser(user), "cases", $"?tab={tab}")).SingleOrDefault(f => f.Key == "status");
        var options = status is null ? "" : string.Join(',', status.Options.Select(o => $"{o.Value}:{o.Label}"));
        options.ShouldBe(expected, tab);
    }

    [Fact]
    public async Task The_supervision_status_filter_has_no_second_Te_gjitha()
    {
        await using var factory = new AshApiFactory(Db);
        var status = (await Get(factory.CreateClient().AsUser("monitor@ash.local"), "supervisions")).Single(f => f.Key == "status");
        status.Options.Select(o => o.Label).ShouldBe(["Aktive", "Të përfunduara"]);
    }

    [Theory]
    [InlineData("cases", "monitor@ash.local")]
    [InlineData("users", "admin@ash.local")]
    [InlineData("offices", "admin@ash.local")]
    [InlineData("institutions", "admin@ash.local")]
    [InlineData("imports", "admin@ash.local")]
    [InlineData("courts", "admin@ash.local")]
    [InlineData("judges", "admin@ash.local")]
    [InlineData("lists", "admin@ash.local")]
    [InlineData("file-types", "admin@ash.local")]
    [InlineData("code-formats", "admin@ash.local")]
    [InlineData("documents", "avokat1@ash.local")]
    [InlineData("notifications", "avokat1@ash.local")]
    [InlineData("saved-reports", "monitor@ash.local")]
    [InlineData("supervisions", "monitor@ash.local")]
    public async Task Every_table_has_two_or_three_defaults_and_unique_keys_and_params(string table, string user)
    {
        await using var factory = new AshApiFactory(Db);
        var filters = await Get(factory.CreateClient().AsUser(user), table);

        var defaults = filters.Count(f => f.IsDefault);
        defaults.ShouldBeInRange(2, 3, table);
        filters.Select(f => f.Key).ShouldBeUnique(table);
        filters.SelectMany(f => f.Params).ShouldBeUnique(table);
        filters.ShouldAllBe(f => f.Params.Count == 1 || f.Params.Count == 2, table);
        filters.ShouldAllBe(f => f.Parent == null || filters.Any(p => p.Key == f.Parent), table);
        filters.ShouldAllBe(f => f.Label.Length > 0 && new[] { "select", "text", "dateRange", "numberRange", "yesNo" }.Contains(f.Kind), table);
    }

    [Fact]
    public async Task An_unknown_table_is_404_and_an_admin_table_is_403_without_its_permission()
    {
        await using var factory = new AshApiFactory(Db);
        var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");

        (await lawyer.GetAsync("/api/filters/nothing", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await lawyer.GetAsync("/api/filters/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await lawyer.GetAsync("/api/filters/courts", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.CreateClient().GetAsync("/api/filters/cases", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_case_list_offers_office_and_lawyer_by_scope_and_children_name_their_parent()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);

        var monitor = await Get(factory.CreateClient().AsUser("monitor@ash.local"), "cases", "?tab=all");
        var kryetar = await Get(factory.CreateClient().AsUser("kryetar@ash.local"), "cases", "?tab=office");
        var lawyer = await Get(factory.CreateClient().AsUser("avokat1@ash.local"), "cases", "?tab=mine");

        monitor.Select(f => f.Key).ShouldContain("office");
        monitor.Select(f => f.Key).ShouldContain("lawyer");
        kryetar.Select(f => f.Key).ShouldNotContain("office");
        kryetar.Select(f => f.Key).ShouldContain("lawyer");
        lawyer.Select(f => f.Key).ShouldNotContain("office");
        lawyer.Select(f => f.Key).ShouldNotContain("lawyer");

        // shkalla → gjykata: every court names a level the level filter offers
        var levels = monitor.Single(f => f.Key == "level").Options.Select(o => o.Value).ToHashSet();
        var court = monitor.Single(f => f.Key == "court");
        court.Parent.ShouldBe("level");
        court.Options.ShouldNotBeEmpty();
        court.Options.ShouldAllBe(o => o.Parent != null && levels.Contains(o.Parent));
        // zyra → avokati
        var offices = monitor.Single(f => f.Key == "office").Options.Select(o => o.Value).ToHashSet();
        monitor.Single(f => f.Key == "lawyer").Options.ShouldAllBe(o => o.Parent != null && offices.Contains(o.Parent));
        // the hearing range writes the page's from / to
        monitor.Single(f => f.Key == "hearing").Params.ShouldBe(["from", "to"]);
    }

    [Fact]
    public async Task The_users_list_filters_by_position_and_last_login()
    {
        await using var factory = new AshApiFactory(Db);
        var admin = factory.CreateClient().AsUser("admin@ash.local");

        var none = await admin.GetFromJsonAsync<JsonElement>("/api/identity/admin/users?position=zzz-asnje-pozicion", Ct);
        none.GetProperty("items").GetArrayLength().ShouldBe(0);

        var all = await admin.GetFromJsonAsync<JsonElement>("/api/identity/admin/users?limit=200", Ct);
        all.GetProperty("items").GetArrayLength().ShouldBeGreaterThan(0);

        // relative to the latest stored login, not to today: tests that run the app on a fixed clock (ChatTests' partition
        // test, months ahead) sign the dev users in "in the future", and the database is shared by the whole run
        var latest = all.GetProperty("items").EnumerateArray()
            .Select(u => u.GetProperty("lastLoginAt"))
            .Where(v => v.ValueKind == JsonValueKind.String)
            .Select(v => v.GetDateTime().ToUniversalTime())
            .Max();
        string Day(DateTime utc) => DateOnly.FromDateTime(utc).ToString("yyyy-MM-dd");

        var after = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/admin/users?lastLoginFrom={Day(latest.AddDays(2))}", Ct);
        after.GetProperty("items").GetArrayLength().ShouldBe(0);
        var around = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/admin/users?lastLoginFrom={Day(latest.AddDays(-1))}", Ct);
        around.GetProperty("items").GetArrayLength().ShouldBeGreaterThan(0);
    }
}
