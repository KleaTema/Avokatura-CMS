using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Modules.Cases.Contracts.Events;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Ids;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 038 — archive the dossier once its latest level has a final decision; an archived dossier is read-only on every
/// level. <see cref="CaseSeed"/>'s ACTIVE first-instance case: avokat1 primary, avokat2 secondary.
/// (Documents of an archived dossier: DocumentTimelineTests.)
/// </summary>
public class ArchiveTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Primary = "avokat1@ash.local";
    private const string Archived = "Dosja është e arkivuar.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class ArchivedRecorder
    {
        public ConcurrentQueue<DossierArchived> Events { get; } = new();
    }

    private sealed class ArchivedHandler(ArchivedRecorder recorder) : IEventHandler<DossierArchived>
    {
        public Task HandleAsync(DossierArchived @event, CancellationToken cancellationToken = default)
        {
            recorder.Events.Enqueue(@event);
            return Task.CompletedTask;
        }
    }

    private sealed record Ctx(CaseSeed Seed, AshApiFactory Factory, Guid Case, ArchivedRecorder Recorder) : IAsyncDisposable
    {
        public HttpClient As(string email) => Factory.CreateClient().AsUser(email);
        public long Id => Seed.ActiveCaseId;

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            await Seed.DisposeAsync();
        }
    }

    private async Task<Ctx> SetupAsync()
    {
        var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var recorder = new ArchivedRecorder();
        var factory = new AshApiFactory(Db, configureServices: services =>
        {
            services.AddSingleton(recorder);
            services.AddScoped<IEventHandler<DossierArchived>, ArchivedHandler>();
        });
        return new Ctx(seed, factory, await PublicIdOf(seed.ActiveCaseId), recorder);
    }

    private async Task<Guid> PublicIdOf(long caseId) =>
        PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM cases WHERE id = {caseId}"))!);

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static async Task<JsonElement> CaseOf(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/cases/{id}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await Json(response);
    }

    private static async Task<(Guid Dossier, uint RowVersion)> HeaderOf(HttpClient client, Guid id)
    {
        var header = (await CaseOf(client, id)).GetProperty("header");
        return (header.GetProperty("dossierPublicId").GetGuid(), header.GetProperty("rowVersion").GetUInt32());
    }

    private static async Task<List<string>> PermissionsOf(HttpClient client, Guid id) =>
        (await CaseOf(client, id)).GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToList();

    /// <summary>Closes the case through the API (HUMBUR), final or not.</summary>
    private static async Task CloseAsync(HttpClient client, Guid id, bool final)
    {
        var (_, rowVersion) = await HeaderOf(client, id);
        var response = await client.PostAsJsonAsync($"/api/cases/{id}/decision",
            new { resultCode = "HUMBUR", decisionReceived = false, isFinal = final, rowVersion }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<HttpResponseMessage> Archive(HttpClient client, Guid dossier, uint rowVersion) =>
        await client.PostAsJsonAsync($"/api/dossiers/{dossier}/archive", new { rowVersion }, Ct);

    private static async Task<string> Problem(HttpResponseMessage response, HttpStatusCode status)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(status, text);
        return text;
    }

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    // ---------------------------------------------------------------- archiving

    [Fact]
    public async Task After_a_final_decision_the_dossier_is_archived()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);
        await CloseAsync(client, s.Case, final: true);
        (await PermissionsOf(client, s.Case)).ShouldContain("decision.close_final");
        var (dossier, rowVersion) = await HeaderOf(client, s.Case);

        var response = await Archive(client, dossier, rowVersion);

        var result = await Json(response);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, result.ToString());
        result.GetProperty("caseRowVersion").GetUInt32().ShouldBe(rowVersion + 1);
        (await Scalar($"SELECT archived_at IS NOT NULL FROM dossiers WHERE id = {s.Seed.ActiveDossierId}")).ShouldBe(1L);
        (await Scalar($"SELECT archived_by FROM dossiers WHERE id = {s.Seed.ActiveDossierId}")).ShouldBe((uint)s.Seed.User("avokat1").Id);
        (await Scalar($"SELECT COUNT(*) FROM case_history_events WHERE case_id = {s.Id} AND event_type = 'DOSSIER_ARCHIVED'")).ShouldBe(1L);
        var archived = s.Recorder.Events.ShouldHaveSingleItem();
        archived.DossierPublicId.ShouldBe(dossier);
        archived.CasePublicIds.ShouldBe([s.Case]);

        var page = await CaseOf(client, s.Case);
        page.GetProperty("header").GetProperty("isArchived").GetBoolean().ShouldBeTrue();
        page.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ShouldBe(["case.view"]);

        // twice: already archived
        (await Problem(await Archive(client, dossier, rowVersion + 1), HttpStatusCode.Conflict)).ShouldContain(Archived);
    }

    [Fact]
    public async Task Not_final_or_latest_not_closed_is_409_with_the_reason_and_close_final_is_not_offered()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);
        var (dossier, rowVersion) = await HeaderOf(client, s.Case);
        (await PermissionsOf(client, s.Case)).ShouldNotContain("decision.close_final");

        (await Problem(await Archive(client, dossier, rowVersion), HttpStatusCode.Conflict)).ShouldContain("dossier.latest_not_closed");

        await CloseAsync(client, s.Case, final: false);
        (await PermissionsOf(client, s.Case)).ShouldNotContain("decision.close_final");
        (await Problem(await Archive(client, dossier, rowVersion + 1), HttpStatusCode.Conflict)).ShouldContain("dossier.not_final");
        (await Scalar($"SELECT archived_at FROM dossiers WHERE id = {s.Seed.ActiveDossierId}")).ShouldBe(DBNull.Value);
    }

    [Fact]
    public async Task The_secondary_lawyer_gets_403_a_stale_row_version_409_an_outsider_404()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);
        await CloseAsync(client, s.Case, final: true);
        var (dossier, rowVersion) = await HeaderOf(client, s.Case);

        using (var secondary = s.As("avokat2@ash.local"))
        {
            await Problem(await Archive(secondary, dossier, rowVersion), HttpStatusCode.Forbidden);
        }

        var outsider = (string)(await Scalar($"SELECT email FROM users WHERE id = {s.Seed.User("drlawyer").Id}"))!;
        using (var other = s.As(outsider))
        {
            await Problem(await Archive(other, dossier, rowVersion), HttpStatusCode.NotFound);
        }

        await Problem(await Archive(client, Guid.NewGuid(), rowVersion), HttpStatusCode.NotFound);
        (await Problem(await Archive(client, dossier, rowVersion - 1), HttpStatusCode.Conflict)).ShouldContain("case.changed");
        (await Scalar($"SELECT archived_at FROM dossiers WHERE id = {s.Seed.ActiveDossierId}")).ShouldBe(DBNull.Value);
    }

    // ---------------------------------------------------------------- read-only afterwards

    [Fact]
    public async Task An_archived_dossier_refuses_every_write_with_409_and_keeps_only_view_on_every_level()
    {
        await using var s = await SetupAsync();
        using var client = s.As(Primary);

        // X/1 closed (not final) and continued at X/2, where avokat1 is primary again; X/2 closes final and is archived
        await CloseAsync(client, s.Case, final: false);
        var code = (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {s.Id}"))!;
        var code2 = code[..^2] + "/2";
        await Scalar(
            "INSERT INTO cases (dossier_id, level_seq, case_code, previous_case_id, office_id, summons_number, act_number, received_date, " +
            "court_level_id, court_id, object_text, lifecycle_status, registered_by) " +
            $"SELECT dossier_id, 2, '{code2}', id, office_id, 'FL-2', 'AKT-2', CURDATE(), (SELECT id FROM court_levels WHERE code = 'APPEAL'), " +
            $"court_id, object_text, 'ACTIVE', registered_by FROM cases WHERE id = {s.Id}");
        var level2 = Convert.ToInt64(await Scalar($"SELECT id FROM cases WHERE case_code = '{code2}'"));
        await Scalar($"INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by) VALUES ({level2}, {s.Seed.User("avokat1").Id}, 'PRIMARY_LAWYER', {s.Seed.User("kryetar").Id})");
        var level2Public = await PublicIdOf(level2);
        try
        {
            await CloseAsync(client, level2Public, final: true);
            // only the latest level offers archiving
            (await PermissionsOf(client, s.Case)).ShouldNotContain("decision.close_final");
            var (dossier, rowVersion) = await HeaderOf(client, level2Public);
            (await Archive(client, dossier, rowVersion)).StatusCode.ShouldBe(HttpStatusCode.OK);

            foreach (var level in new[] { s.Case, level2Public })
            {
                (await PermissionsOf(client, level)).ShouldBe(["case.view"]);
            }

            var decision = (await CaseOf(client, level2Public)).GetProperty("decision");
            var writes = new (string What, Func<Task<HttpResponseMessage>> Send)[]
            {
                ("add hearing", () => client.PostAsJsonAsync($"/api/cases/{level2Public}/hearings",
                    new { hearingTypeCode = "PERGATITORE", at = DateTime.UtcNow.AddDays(5).ToString("yyyy-MM-ddTHH:mm:ssZ") }, Ct)),
                ("add party", () => client.PostAsJsonAsync($"/api/cases/{level2Public}/parties",
                    new { partyRoleCode = "PERSON_I_TRETE", kind = "NATURAL", displayName = "Arben Kola" }, Ct)),
                ("update decision", () => client.PutAsJsonAsync($"/api/cases/{level2Public}/decision",
                    new { resultCode = "HUMBUR", decisionNumber = "V-9", isFinal = true, rowVersion = decision.GetProperty("rowVersion").GetUInt32() }, Ct)),
                ("claim", () => client.PatchAsJsonAsync($"/api/cases/{s.Case}/claim",
                    new { claimAmount = "100", rowVersion = 0 }, Ct)),
                ("notify not closed", () => s.As("protokoll@ash.local").PostAsJsonAsync($"/api/cases/{level2Public}/notify-not-closed",
                    new { summonsNumber = "FL-9", courtId = 1, hearingAt = DateTimeOffset.UtcNow.AddDays(9).ToString("yyyy-MM-ddTHH:mm:sszzz") }, Ct)),
            };
            foreach (var (what, send) in writes)
            {
                var text = await Problem(await send(), HttpStatusCode.Conflict);
                text.ShouldContain(Archived, customMessage: what);
                text.ShouldContain("dossier.archived", customMessage: what);
            }
            // the next level from an archived dossier stays refused: NextLevelTests (task 018), unchanged
        }
        finally
        {
            await Scalar($"DELETE FROM case_assignments WHERE case_id = {level2}");
        }
    }
}
