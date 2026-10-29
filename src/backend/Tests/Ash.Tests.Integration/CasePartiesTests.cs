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
/// Task 017 — parties of a case. CaseSeed gives an ACTIVE case (office TR, avokat1 primary, avokat2 secondary,
/// MONITORING link to the seeded ministry) and a REGISTERED case registered by protokoll.
/// </summary>
public class CasePartiesTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string SeededMinistry = "Ministria e Financave dhe Ekonomisë";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class EventRecorder
    {
        public ConcurrentQueue<InstitutionLinked> Linked { get; } = new();
        public ConcurrentQueue<InstitutionUnlinked> Unlinked { get; } = new();
    }

    private sealed class RecordingHandler(EventRecorder recorder) : IEventHandler<InstitutionLinked>, IEventHandler<InstitutionUnlinked>
    {
        public Task HandleAsync(InstitutionLinked @event, CancellationToken cancellationToken = default)
        {
            recorder.Linked.Enqueue(@event);
            return Task.CompletedTask;
        }

        public Task HandleAsync(InstitutionUnlinked @event, CancellationToken cancellationToken = default)
        {
            recorder.Unlinked.Enqueue(@event);
            return Task.CompletedTask;
        }
    }

    private AshApiFactory Factory(EventRecorder? recorder = null) =>
        new(Db, configureServices: services =>
        {
            if (recorder is not null)
            {
                services.AddSingleton(recorder);
                services.AddScoped<IEventHandler<InstitutionLinked>, RecordingHandler>();
                services.AddScoped<IEventHandler<InstitutionUnlinked>, RecordingHandler>();
            }
        });

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    private async Task<Guid> PublicIdOf(string table, long id) =>
        PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM {table} WHERE id = {id}"))!);

    private async Task<Guid> InstitutionPublicId(string name) =>
        PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM institutions WHERE name = '{name}'"))!);

    /// <summary>
    /// Party and link rows this test added to the seed's cases (so an institution the test created can be deleted).
    /// History is append-only (trigger): it is cleared by the fixture's TRUNCATE before the next test.
    /// </summary>
    private async Task ClearCaseRows(CaseSeed seed)
    {
        var ids = $"{seed.ActiveCaseId}, {seed.RegisteredCaseId}";
        await Scalar($"DELETE FROM case_parties WHERE case_id IN ({ids})");
        await Scalar($"DELETE FROM case_institutions WHERE case_id IN ({ids})");
    }

    private static Dictionary<string, object?> Natural(string name, string role = "PADITES", string kind = "NATURAL") => new()
    {
        ["partyRoleCode"] = role, ["kind"] = kind, ["institutionPublicId"] = null, ["displayName"] = name, ["identifier"] = null
    };

    private static Dictionary<string, object?> Institution(Guid institution, string role = "I_PADITUR") => new()
    {
        ["partyRoleCode"] = role, ["kind"] = "INSTITUTION", ["institutionPublicId"] = institution, ["displayName"] = null, ["identifier"] = null
    };

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        AssertNoInternalIds(json);
        return json;
    }

    /// <summary>Internal BIGINT ids never leave the server: no numeric "id"/"…Id" anywhere in a response.</summary>
    private static void AssertNoInternalIds(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if ((property.Name == "id" || property.Name.EndsWith("Id", StringComparison.Ordinal))
                        && property.Value.ValueKind == JsonValueKind.Number)
                    {
                        throw new ShouldAssertException($"numeric internal id in response: {property.Name}");
                    }

                    AssertNoInternalIds(property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AssertNoInternalIds(item);
                }

                break;
        }
    }

    private static string PartiesPath(Guid caseId) => $"/api/cases/{caseId}/parties";

    [Fact]
    public async Task Protokoll_adds_edits_and_removes_parties_while_the_case_is_registered()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        try
        {
            var caseId = await PublicIdOf("cases", seed.RegisteredCaseId);
            await using var factory = Factory();
            using var client = factory.CreateClient().AsUser("protokoll@ash.local");
            var path = PartiesPath(caseId);

            var added = await client.PostAsJsonAsync(path, Natural("Agim Hoxha"), Ct);
            added.StatusCode.ShouldBe(HttpStatusCode.Created);
            var party = await Json(added);
            var partyId = party.GetProperty("publicId").GetGuid();
            party.GetProperty("role").GetProperty("code").GetString().ShouldBe("PADITES");

            var legal = Natural("Shoqëria Test", "I_PADITUR", "LEGAL");
            legal["identifier"] = "k12345678a";
            (await client.PostAsJsonAsync(path, legal, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

            var renamed = Natural("Agim Hoxha Jr.", "PERSON_I_TRETE");
            var updated = await client.PutAsJsonAsync($"{path}/{partyId}", renamed, Ct);
            updated.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await Json(updated)).GetProperty("displayName").GetString().ShouldBe("Agim Hoxha Jr.");

            var list = await Json(await client.GetAsync(path, Ct));
            // ordered by role sort order: I_PADITUR (2) before PERSON_I_TRETE (3)
            list.EnumerateArray().Select(p => p.GetProperty("role").GetProperty("code").GetString())
                .ShouldBe(["I_PADITUR", "PERSON_I_TRETE"]);
            list.EnumerateArray().First().GetProperty("identifier").GetString().ShouldBe("K12345678A");

            var removed = await client.DeleteAsync($"{path}/{partyId}", Ct);
            removed.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await Json(removed)).GetProperty("lastInstitutionParty").GetBoolean().ShouldBeFalse();
            (await Json(await client.GetAsync(path, Ct))).GetArrayLength().ShouldBe(1);

            (await Scalar($"SELECT deleted_by IS NOT NULL AND deleted_at IS NOT NULL FROM case_parties WHERE display_name = 'Agim Hoxha Jr.'"))
                .ShouldBe(1L);
            var updatedEvent = $"FROM case_history_events WHERE case_id = {seed.RegisteredCaseId} AND event_type = 'PARTY_UPDATED'";
            (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.old.displayName')) {updatedEvent}")).ShouldBe("Agim Hoxha");
            (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.displayName')) {updatedEvent}")).ShouldBe("Agim Hoxha Jr.");
            (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.role')) {updatedEvent}")).ShouldBe("PERSON_I_TRETE");
            // unchanged fields are not stored
            (await Scalar($"SELECT JSON_CONTAINS_PATH(details, 'one', '$.new.identifier') {updatedEvent}")).ShouldBe(0L);
            (await Scalar($"SELECT COUNT(*) FROM case_history_events WHERE case_id = {seed.RegisteredCaseId} AND event_type = 'PARTY_REMOVED'"))
                .ShouldBe(1L);
            (await Scalar($"SELECT row_version FROM cases WHERE id = {seed.RegisteredCaseId}")).ShouldBe(0u);
        }
        finally
        {
            await ClearCaseRows(seed);
        }
    }

    [Fact]
    public async Task Protokoll_cannot_change_parties_once_the_case_is_active()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var caseId = await PublicIdOf("cases", seed.ActiveCaseId);
        await using var factory = Factory();
        using var client = factory.CreateClient().AsUser("protokoll@ash.local");

        // Protokoll's view of a case ends with REGISTERED, so the case is not found
        (await client.PostAsJsonAsync(PartiesPath(caseId), Natural("Agim Hoxha"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Secondary_lawyer_removes_a_party_and_monitor_may_read_but_not_change()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        try
        {
            var caseId = await PublicIdOf("cases", seed.ActiveCaseId);
            var path = PartiesPath(caseId);
            await using var factory = Factory();
            using var primary = factory.CreateClient().AsUser("avokat1@ash.local");
            var added = await Json(await primary.PostAsJsonAsync(path, Natural("Besa Kola"), Ct));
            var partyId = added.GetProperty("publicId").GetGuid();

            using var monitor = factory.CreateClient().AsUser("monitor@ash.local");
            (await monitor.GetAsync(path, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await monitor.PostAsJsonAsync(path, Natural("Monitor Party"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await monitor.DeleteAsync($"{path}/{partyId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            using var secondary = factory.CreateClient().AsUser("avokat2@ash.local");
            (await secondary.DeleteAsync($"{path}/{partyId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

            // the registering Protokoll no longer sees the case once it is ACTIVE
            using var protokoll = factory.CreateClient().AsUser("protokoll@ash.local");
            (await protokoll.GetAsync(path, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        finally
        {
            await ClearCaseRows(seed);
        }
    }

    [Fact]
    public async Task Closed_case_refuses_party_changes_with_409()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        try
        {
            var caseId = await PublicIdOf("cases", seed.ActiveCaseId);
            await Scalar($"UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = UTC_TIMESTAMP(3), closed_by = {seed.User("kryetar").Id} WHERE id = {seed.ActiveCaseId}");
            await using var factory = Factory();
            using var secondary = factory.CreateClient().AsUser("avokat2@ash.local");

            var response = await secondary.PostAsJsonAsync(PartiesPath(caseId), Natural("Agim Hoxha"), Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await Json(response)).GetProperty("title").GetString().ShouldBe("Çështja është mbyllur; palët ndryshohen në shkallën e radhës.");
        }
        finally
        {
            await ClearCaseRows(seed);
        }
    }

    [Fact]
    public async Task Kind_and_institution_cannot_be_changed()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        try
        {
            var caseId = await PublicIdOf("cases", seed.RegisteredCaseId);
            await using var factory = Factory();
            using var client = factory.CreateClient().AsUser("protokoll@ash.local");
            var path = PartiesPath(caseId);
            var partyId = (await Json(await client.PostAsJsonAsync(path, Natural("Agim Hoxha"), Ct))).GetProperty("publicId").GetGuid();

            var response = await client.PutAsJsonAsync($"{path}/{partyId}", Natural("Agim Hoxha", kind: "LEGAL"), Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var problem = await Json(response);
            problem.GetProperty("errors").GetProperty("kind")[0].GetString()
                .ShouldBe("Për të ndryshuar llojin ose institucionin, fshini palën dhe shtoni një të re.");
        }
        finally
        {
            await ClearCaseRows(seed);
        }
    }

    [Fact]
    public async Task Adding_an_institution_creates_a_monitoring_link_once()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var orgSeed = await Seed.CreateAsync(Db.AppConnectionString);
        var ministryId = await orgSeed.InstitutionAsync("Ministria e Financave", $"MF{orgSeed.Suffix}");
        try
        {
            var ministry = await PublicIdOf("institutions", ministryId);
            var caseId = await PublicIdOf("cases", seed.RegisteredCaseId);
            var recorder = new EventRecorder();
            await using var factory = Factory(recorder);
            using var client = factory.CreateClient().AsUser("protokoll@ash.local");

            var created = await client.PostAsJsonAsync(PartiesPath(caseId), Institution(ministry), Ct);

            created.StatusCode.ShouldBe(HttpStatusCode.Created);
            var party = await Json(created);
            party.GetProperty("role").GetProperty("name").GetString().ShouldBe("I paditur");
            party.GetProperty("institution").GetProperty("publicId").GetGuid().ShouldBe(ministry);
            party.GetProperty("displayName").GetString().ShouldBe("Ministria e Financave");
            (await Scalar($"SELECT COUNT(*) FROM case_institutions WHERE case_id = {seed.RegisteredCaseId} AND institution_id = {ministryId} AND link_mode = 'MONITORING' AND ended_at IS NULL"))
                .ShouldBe(1L);
            var linked = recorder.Linked.ShouldHaveSingleItem();
            linked.CasePublicId.ShouldBe(caseId);
            linked.InstitutionPublicId.ShouldBe(ministry);
            linked.Mode.ShouldBe("MONITORING");
            // TODO(task-040): assert the INSTITUTION_USER notification once Notifications exist.

            // the ACTIVE case already has a MONITORING link to the seeded ministry: adding it as a party adds no second link
            using var primary = factory.CreateClient().AsUser("avokat1@ash.local");
            var seeded = await InstitutionPublicId(SeededMinistry);
            (await primary.PostAsJsonAsync(PartiesPath(await PublicIdOf("cases", seed.ActiveCaseId)), Institution(seeded), Ct))
                .StatusCode.ShouldBe(HttpStatusCode.Created);
            (await Scalar($"SELECT COUNT(*) FROM case_institutions WHERE case_id = {seed.ActiveCaseId}")).ShouldBe(1L);
            recorder.Linked.Count.ShouldBe(1);

            // the same institution twice in a case → 400
            var again = await client.PostAsJsonAsync(PartiesPath(caseId), Institution(ministry, "PERSON_I_TRETE"), Ct);
            again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        finally
        {
            await ClearCaseRows(seed);
            await Scalar($"DELETE FROM institutions WHERE id = {ministryId}");
        }
    }

    [Fact]
    public async Task Removing_the_last_institution_party_ends_a_monitoring_link_only_when_asked()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        try
        {
            var caseId = await PublicIdOf("cases", seed.RegisteredCaseId);
            var ministry = await InstitutionPublicId(SeededMinistry);
            var recorder = new EventRecorder();
            await using var factory = Factory(recorder);
            using var client = factory.CreateClient().AsUser("protokoll@ash.local");
            var path = PartiesPath(caseId);

            // without the flag: the link is kept and the UI is told it was the last party
            var first = (await Json(await client.PostAsJsonAsync(path, Institution(ministry), Ct))).GetProperty("publicId").GetGuid();
            var kept = await Json(await client.DeleteAsync($"{path}/{first}", Ct));
            kept.GetProperty("lastInstitutionParty").GetBoolean().ShouldBeTrue();
            kept.GetProperty("institutionLinkEnded").GetBoolean().ShouldBeFalse();
            (await Scalar($"SELECT COUNT(*) FROM case_institutions WHERE case_id = {seed.RegisteredCaseId} AND ended_at IS NULL")).ShouldBe(1L);

            // with endInstitutionLink=true: the MONITORING link ends
            var second = (await Json(await client.PostAsJsonAsync(path, Institution(ministry), Ct))).GetProperty("publicId").GetGuid();
            var ended = await Json(await client.DeleteAsync($"{path}/{second}?endInstitutionLink=true", Ct));
            ended.GetProperty("institutionLinkEnded").GetBoolean().ShouldBeTrue();
            (await Scalar($"SELECT COUNT(*) FROM case_institutions WHERE case_id = {seed.RegisteredCaseId} AND ended_at IS NULL")).ShouldBe(0L);
            (await Scalar($"SELECT ended_by IS NOT NULL FROM case_institutions WHERE case_id = {seed.RegisteredCaseId}")).ShouldBe(1L);
            (await Scalar($"SELECT COUNT(*) FROM case_history_events WHERE case_id = {seed.RegisteredCaseId} AND event_type = 'INSTITUTION_UNLINKED'"))
                .ShouldBe(1L);
            var unlinked = recorder.Unlinked.ShouldHaveSingleItem();
            unlinked.InstitutionPublicId.ShouldBe(ministry);
            unlinked.Mode.ShouldBe("MONITORING");
        }
        finally
        {
            await ClearCaseRows(seed);
        }
    }

    [Fact]
    public async Task A_delegated_link_is_never_ended_by_removing_a_party()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        try
        {
            await Scalar($"UPDATE case_institutions SET link_mode = 'DELEGATED' WHERE case_id = {seed.ActiveCaseId}");
            var caseId = await PublicIdOf("cases", seed.ActiveCaseId);
            var path = PartiesPath(caseId);
            await using var factory = Factory();
            using var primary = factory.CreateClient().AsUser("avokat1@ash.local");
            var partyId = (await Json(await primary.PostAsJsonAsync(path, Institution(await InstitutionPublicId(SeededMinistry)), Ct)))
                .GetProperty("publicId").GetGuid();

            using var secondary = factory.CreateClient().AsUser("avokat2@ash.local");
            var response = await secondary.DeleteAsync($"{path}/{partyId}?endInstitutionLink=true", Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var result = await Json(response);
            result.GetProperty("lastInstitutionParty").GetBoolean().ShouldBeTrue();
            result.GetProperty("institutionLinkEnded").GetBoolean().ShouldBeFalse();
            result.GetProperty("warnings")[0].GetProperty("message").GetString()
                .ShouldBe("Institucioni ka delegim në këtë çështje; delegimi nuk ndryshon.");
            (await Scalar($"SELECT COUNT(*) FROM case_institutions WHERE case_id = {seed.ActiveCaseId} AND link_mode = 'DELEGATED' AND ended_at IS NULL"))
                .ShouldBe(1L);
        }
        finally
        {
            await ClearCaseRows(seed);
        }
    }
}
