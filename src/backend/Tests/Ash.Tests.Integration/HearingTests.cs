using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 030 — hearings. Uses <see cref="CaseSeed"/>'s ACTIVE case (avokat1 primary; the ministry MONITORING through its
/// institution user), which starts without hearings and without next_hearing_at.
/// </summary>
public class HearingTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Primary = "avokat1@ash.local";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(Ct);
        return value is DBNull ? null : value;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private async Task<Guid> PublicIdOf(string table, long id) =>
        Ash.SharedKernel.Ids.PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM {table} WHERE id = {id}"))!);

    /// <summary>A fixed time in whole seconds, with the offset as the UI sends it.</summary>
    private static string Iso(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToOffset(TimeSpan.FromHours(1)).ToString("yyyy-MM-ddTHH:mm:sszzz");

    private static DateTime Future(int days) => DateTime.UtcNow.Date.AddDays(days).AddHours(8);

    private sealed record Ctx(CaseSeed Seed, Guid Case, long CaseId, AshApiFactory Factory, HttpClient Primary);

    private async Task<Ctx> SetupAsync()
    {
        var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var factory = new AshApiFactory(Db);
        return new Ctx(seed, await PublicIdOf("cases", seed.ActiveCaseId), seed.ActiveCaseId, factory,
            factory.CreateClient().AsUser(Primary));
    }

    private static async Task<JsonElement> Add(HttpClient client, Guid caseId, DateTime atUtc, string type = "PERGATITORE",
        string? other = null, string? description = null, HttpStatusCode expected = HttpStatusCode.Created)
    {
        var response = await client.PostAsJsonAsync($"/api/cases/{caseId}/hearings",
            new { at = Iso(atUtc), hearingTypeCode = type, otherTypeText = other, description }, Ct);
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync(Ct));
        return await Json(response);
    }

    private static Task<HttpResponseMessage> Complete(HttpClient client, Guid caseId, JsonElement hearing, object body) =>
        client.PostAsJsonAsync($"/api/cases/{caseId}/hearings/{hearing.GetProperty("publicId").GetGuid()}/complete", body, Ct);

    private static uint RowVersion(JsonElement hearing) => hearing.GetProperty("rowVersion").GetUInt32();

    private async Task<DateTime?> NextHearingAt(long caseId) => (DateTime?)await Scalar($"SELECT next_hearing_at FROM cases WHERE id = {caseId}");

    private Task<object?> CaseRowVersion(long caseId) => Scalar($"SELECT row_version FROM cases WHERE id = {caseId}");

    [Fact]
    public async Task Adding_sets_the_next_hearing_and_leaves_the_case_row_version_alone()
    {
        var s = await SetupAsync();
        await using var seedOwner = s.Seed;
        await using var factoryOwner = s.Factory;
        var caseRowVersion = await CaseRowVersion(s.CaseId);
        var at = Future(10);

        var body = await Add(s.Primary, s.Case, at, description: "  Përgatitja e provave  ");

        JsonAssert.NoNumericIds(body);
        var hearing = body.GetProperty("hearing");
        hearing.GetProperty("status").GetString().ShouldBe("SCHEDULED");
        hearing.GetProperty("type").GetProperty("code").GetString().ShouldBe("PERGATITORE");
        hearing.GetProperty("description").GetString().ShouldBe("Përgatitja e provave");
        hearing.GetProperty("createdBy").GetString().ShouldBe("Avokat1 Dev");
        hearing.GetProperty("isOverdue").GetBoolean().ShouldBeFalse();
        hearing.GetProperty("scheduledAt").GetDateTime().ToUniversalTime().ShouldBe(at);
        body.GetProperty("warnings").GetArrayLength().ShouldBe(0);
        (await NextHearingAt(s.CaseId)).ShouldBe(at);
        (await CaseRowVersion(s.CaseId)).ShouldBe(caseRowVersion); // derived data: the case's version stays
        (await Scalar($"SELECT GROUP_CONCAT(event_type) FROM case_history_events WHERE case_id = {s.CaseId}")).ShouldBe("HEARING_ADDED");
        (await Scalar($"SELECT first_data_entry_at FROM cases WHERE id = {s.CaseId}")).ShouldNotBeNull(); // the team's entry

        // a second one within 30 minutes: saved, with a warning; the list is newest first, without internal ids
        var close = await Add(s.Primary, s.Case, at.AddMinutes(20), "GJYQESORE");
        close.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()).ShouldBe(["hearing.close_to_another"]);
        var list = await Json(await s.Primary.GetAsync($"/api/cases/{s.Case}/hearings", Ct));
        JsonAssert.NoNumericIds(list);
        list.EnumerateArray().Select(h => h.GetProperty("type").GetProperty("code").GetString()).ShouldBe(["GJYQESORE", "PERGATITORE"]);
        (await NextHearingAt(s.CaseId)).ShouldBe(at); // still the earliest
    }

    [Fact]
    public async Task An_overdue_scheduled_hearing_stays_the_next_one_and_is_marked()
    {
        var s = await SetupAsync();
        await using var seedOwner = s.Seed;
        await using var factoryOwner = s.Factory;
        var past = DateTime.UtcNow.Date.AddDays(-3).AddHours(8);

        var body = await Add(s.Primary, s.Case, past);

        body.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()).ShouldBe(["hearing.in_past"]);
        body.GetProperty("hearing").GetProperty("isOverdue").GetBoolean().ShouldBeTrue();
        (await NextHearingAt(s.CaseId)).ShouldBe(past); // not NULL: the stored value does not depend on "now"
        var data = (await Json(await s.Primary.GetAsync($"/api/cases/{s.Case}", Ct))).GetProperty("data");
        data.GetProperty("nextHearingIsOverdue").GetBoolean().ShouldBeTrue();
        var row = (await Json(await s.Primary.GetAsync("/api/cases?tab=mine&limit=100", Ct))).GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("publicId").GetGuid() == s.Case);
        row.GetProperty("nextHearingIsOverdue").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Completing_as_held_with_the_next_hearing_updates_last_and_next_in_one_step()
    {
        var s = await SetupAsync();
        await using var seedOwner = s.Seed;
        await using var factoryOwner = s.Factory;
        var caseRowVersion = await CaseRowVersion(s.CaseId);
        var first = (await Add(s.Primary, s.Case, Future(5))).GetProperty("hearing");
        var nextAt = Future(40);

        // HELD needs its description
        var missing = await Complete(s.Primary, s.Case, first, new { outcome = "HELD", rowVersion = RowVersion(first) });
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(missing)).GetProperty("errors").TryGetProperty("description", out _).ShouldBeTrue();

        var completed = await Complete(s.Primary, s.Case, first, new
        {
            outcome = "HELD",
            description = "U dëgjuan dëshmitarët; gjykata kërkoi akte shtesë.",
            next = new { at = Iso(nextAt), hearingTypeCode = "GJYQESORE" },
            rowVersion = RowVersion(first)
        });

        completed.StatusCode.ShouldBe(HttpStatusCode.Created, await completed.Content.ReadAsStringAsync(Ct));
        var body = await Json(completed);
        JsonAssert.NoNumericIds(body);
        body.GetProperty("hearing").GetProperty("status").GetString().ShouldBe("HELD");
        body.GetProperty("hearing").GetProperty("rowVersion").GetUInt32().ShouldBe(RowVersion(first) + 1);
        body.GetProperty("next").GetProperty("status").GetString().ShouldBe("SCHEDULED");
        var heldId = await Scalar($"SELECT id FROM case_hearings WHERE public_id = UUID_TO_BIN('{first.GetProperty("publicId").GetGuid()}', 1)");
        (await Scalar($"SELECT last_hearing_id FROM cases WHERE id = {s.CaseId}")).ShouldBe(heldId);
        (await NextHearingAt(s.CaseId)).ShouldBe(nextAt);
        (await CaseRowVersion(s.CaseId)).ShouldBe(caseRowVersion);
        var history = $"FROM case_history_events WHERE case_id = {s.CaseId} AND event_type = 'HEARING_COMPLETED'";
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.status')) {history}")).ShouldBe("HELD");
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.nextHearingType')) {history}")).ShouldBe("Seancë gjyqësore");

        // the new hearing form starts from the previous description
        var defaults = await Json(await s.Primary.GetAsync($"/api/cases/{s.Case}/hearings/new-defaults", Ct));
        defaults.GetProperty("description").GetString().ShouldBe("U dëgjuan dëshmitarët; gjykata kërkoi akte shtesë.");
        defaults.GetProperty("type").GetProperty("code").GetString().ShouldBe("PERGATITORE");

        // HELD → back to SCHEDULED (or any other outcome) is refused; its time cannot change, its description can
        var held = body.GetProperty("hearing");
        var again = await Complete(s.Primary, s.Case, held, new { outcome = "CANCELLED", rowVersion = RowVersion(held) });
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(again)).GetProperty("code").GetString().ShouldBe("hearing.already_completed");
        var move = await s.Primary.PutAsJsonAsync($"/api/cases/{s.Case}/hearings/{held.GetProperty("publicId").GetGuid()}",
            new { at = Iso(Future(6)), rowVersion = RowVersion(held) }, Ct);
        move.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var fix = await s.Primary.PutAsJsonAsync($"/api/cases/{s.Case}/hearings/{held.GetProperty("publicId").GetGuid()}",
            new { description = "U dëgjuan dëshmitarët.", rowVersion = RowVersion(held) }, Ct);
        fix.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(fix)).GetProperty("hearing").GetProperty("status").GetString().ShouldBe("HELD");
    }

    [Fact]
    public async Task Reschedule_and_cancel_keep_the_next_hearing_right()
    {
        var s = await SetupAsync();
        await using var seedOwner = s.Seed;
        await using var factoryOwner = s.Factory;
        var hearing = (await Add(s.Primary, s.Case, Future(10))).GetProperty("hearing");
        var id = hearing.GetProperty("publicId").GetGuid();

        var moved = await s.Primary.PutAsJsonAsync($"/api/cases/{s.Case}/hearings/{id}",
            new { at = Iso(Future(12)), rowVersion = RowVersion(hearing) }, Ct);

        moved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var movedHearing = (await Json(moved)).GetProperty("hearing");
        RowVersion(movedHearing).ShouldBe(RowVersion(hearing) + 1);
        (await NextHearingAt(s.CaseId)).ShouldBe(Future(12));
        (await Scalar($"SELECT summary FROM case_history_events WHERE case_id = {s.CaseId} AND event_type = 'HEARING_UPDATED'"))
            .ShouldNotBeNull();

        // the old row version → 409, nothing changed
        var stale = await s.Primary.PutAsJsonAsync($"/api/cases/{s.Case}/hearings/{id}",
            new { at = Iso(Future(14)), rowVersion = RowVersion(hearing) }, Ct);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(stale)).GetProperty("code").GetString().ShouldBe("case.changed");
        (await NextHearingAt(s.CaseId)).ShouldBe(Future(12));

        // cancelled: no longer the next hearing
        (await Complete(s.Primary, s.Case, movedHearing, new { outcome = "CANCELLED", rowVersion = RowVersion(movedHearing) }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await NextHearingAt(s.CaseId)).ShouldBeNull();
        (await Scalar($"SELECT last_hearing_id FROM cases WHERE id = {s.CaseId}")).ShouldBeNull(); // not HELD
    }

    [Fact]
    public async Task Postponed_without_a_new_date_is_saved_with_a_warning()
    {
        var s = await SetupAsync();
        await using var seedOwner = s.Seed;
        await using var factoryOwner = s.Factory;
        var hearing = (await Add(s.Primary, s.Case, Future(3))).GetProperty("hearing");

        var postponed = await Complete(s.Primary, s.Case, hearing,
            new { outcome = "POSTPONED", description = "Mungoi pala paditëse.", rowVersion = RowVersion(hearing) });

        postponed.StatusCode.ShouldBe(HttpStatusCode.Created);
        var warning = (await Json(postponed)).GetProperty("warnings").EnumerateArray().Single();
        warning.GetProperty("code").GetString().ShouldBe("hearing.postponed_without_date");
        warning.GetProperty("message").GetString().ShouldBe("Shtyrja zakonisht ka datë të re.");
    }

    [Fact]
    public async Task Invalid_types_and_texts_are_400()
    {
        var s = await SetupAsync();
        await using var seedOwner = s.Seed;
        await using var factoryOwner = s.Factory;

        var other = await Add(s.Primary, s.Case, Future(5), "TJETER", expected: HttpStatusCode.BadRequest);
        other.GetProperty("errors").GetProperty("otherTypeText")[0].GetString().ShouldBe("Plotësoni llojin e seancës.");
        (await Add(s.Primary, s.Case, Future(5), "TJETER", other: "Seancë pajtimi")).GetProperty("hearing")
            .GetProperty("otherTypeText").GetString().ShouldBe("Seancë pajtimi");
        (await Add(s.Primary, s.Case, Future(5), "NUK_EKZISTON", expected: HttpStatusCode.BadRequest))
            .GetProperty("errors").TryGetProperty("hearingTypeCode", out _).ShouldBeTrue();
        (await Add(s.Primary, s.Case, Future(5), description: new string('x', 4001), expected: HttpStatusCode.BadRequest))
            .GetProperty("errors").TryGetProperty("description", out _).ShouldBeTrue();

        // a time without an offset is ambiguous
        var bare = await s.Primary.PostAsJsonAsync($"/api/cases/{s.Case}/hearings",
            new { at = "2030-03-15T09:30:00", hearingTypeCode = "PERGATITORE" }, Ct);
        bare.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(bare)).GetProperty("errors").TryGetProperty("at", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Monitoring_institution_adds_but_does_not_edit_and_a_closed_case_is_refused()
    {
        var s = await SetupAsync();
        await using var seedOwner = s.Seed;
        await using var factoryOwner = s.Factory;
        using var institution = s.Factory.CreateClient().AsUser("institucion@ash.local");

        var added = (await Add(institution, s.Case, Future(8))).GetProperty("hearing");

        (await institution.PutAsJsonAsync($"/api/cases/{s.Case}/hearings/{added.GetProperty("publicId").GetGuid()}",
            new { description = "Shënim", rowVersion = RowVersion(added) }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Complete(institution, s.Case, added, new { outcome = "CANCELLED", rowVersion = RowVersion(added) }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        // the monitoring institution is not the team: no first data entry
        (await Scalar($"SELECT first_data_entry_at FROM cases WHERE id = {s.CaseId}")).ShouldBeNull();

        await Scalar($"UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = UTC_TIMESTAMP(3), closed_by = {s.Seed.User("avokat1").Id} WHERE id = {s.CaseId}");
        var closed = await s.Primary.PostAsJsonAsync($"/api/cases/{s.Case}/hearings",
            new { at = Iso(Future(9)), hearingTypeCode = "PERGATITORE" }, Ct);
        closed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(closed)).GetProperty("code").GetString().ShouldBe("case.closed");
        (await Complete(s.Primary, s.Case, added, new { outcome = "CANCELLED", rowVersion = RowVersion(added) }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Unknown_or_invisible_hearings_are_404()
    {
        var s = await SetupAsync();
        await using var seedOwner = s.Seed;
        await using var factoryOwner = s.Factory;
        var hearing = (await Add(s.Primary, s.Case, Future(8))).GetProperty("hearing");

        (await s.Primary.PutAsJsonAsync($"/api/cases/{s.Case}/hearings/{Guid.NewGuid()}",
            new { description = "x", rowVersion = 0 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // the hearing of another case is not found through this case
        (await s.Primary.PutAsJsonAsync($"/api/cases/{await PublicIdOf("cases", s.Seed.RegisteredCaseId)}/hearings/{hearing.GetProperty("publicId").GetGuid()}",
            new { description = "x", rowVersion = RowVersion(hearing) }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var other = s.Factory.CreateClient().AsUser(
            (string)(await Scalar($"SELECT email FROM users WHERE id = {s.Seed.User("drlawyer").Id}"))!);
        (await other.GetAsync($"/api/cases/{s.Case}/hearings", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
