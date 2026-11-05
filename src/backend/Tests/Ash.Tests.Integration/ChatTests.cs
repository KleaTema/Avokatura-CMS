using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Infrastructure.Jobs;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Chat.Application;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Ids;
using Ash.SharedKernel.Time;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 049 — the case chat (policy CaseOnly). CaseSeed's ACTIVE case: avokat1 primary, avokat2 secondary, ndihmes
/// assistant, jurist (institution jurist) and the ministry linked MONITORING (institucion = its INSTITUTION_USER).
/// kryetar heads the office but is not on the team.
/// </summary>
public class ChatTests(MySqlFixture db) : IntegrationTest(db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Every wait gives up after this long: a missing push fails the test instead of hanging it.</summary>
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private static readonly string[] Team =
        ["avokat1@ash.local", "avokat2@ash.local", "institucion@ash.local", "jurist@ash.local", "ndihmes@ash.local"];

    // ---------------------------------------------------------------- helpers

    private async Task<object?> Scalar(string sql, string? connectionString = null)
    {
        await using var connection = new MySqlConnection(connectionString ?? Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(Ct);
        return value is DBNull ? null : value;
    }

    private async Task Execute(string sql, string? connectionString = null)
    {
        await using var connection = new MySqlConnection(connectionString ?? Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private async Task<List<string>> Strings(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var values = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private async Task<Guid> PublicIdOf(string table, long id) =>
        PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM {table} WHERE id = {id}"))!);

    /// <summary>E-mails of the active participants of the case chat, sorted.</summary>
    private Task<List<string>> Participants(long caseId) => Strings($"""
        SELECT u.email FROM ash_chat.conversation_participants p
          JOIN ash_chat.conversations c ON c.id = p.conversation_id
          JOIN ash_core.users u ON u.id = p.user_id
         WHERE c.case_id = {caseId} AND p.left_at IS NULL ORDER BY u.email
        """);

    private Task<List<string>> SystemMessages(long caseId) => Strings($"""
        SELECT m.body FROM ash_chat.messages m JOIN ash_chat.conversations c ON c.id = m.conversation_id
         WHERE c.case_id = {caseId} AND m.message_type = 'SYSTEM' ORDER BY m.id
        """);

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static async Task<Guid> Open(HttpClient client, Guid casePublicId)
    {
        var response = await client.PostAsync($"/api/chat/cases/{casePublicId}/conversation", null, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, Guid conversation, string body) =>
        await client.PostAsJsonAsync($"/api/chat/conversations/{conversation}/messages", new { body }, Ct);

    private static async Task<JsonElement> SendOk(HttpClient client, Guid conversation, string body)
    {
        var response = await Send(client, conversation, body);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, text);
        return JsonDocument.Parse(text).RootElement;
    }

    private static async Task<JsonElement> Page(HttpClient client, Guid conversation, string? before = null, int limit = 50)
    {
        var response = await client.GetAsync(
            $"/api/chat/conversations/{conversation}/messages?limit={limit}{(before is null ? "" : $"&before={Uri.EscapeDataString(before)}")}", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement;
    }

    private static async Task<int> UnreadTotal(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/chat/unread-total", Ct)).GetProperty("unread").GetInt32();

    /// <summary>A real SignalR client on /hubs/chat (long polling through the test server), recording every event.</summary>
    private sealed class HubClient : IAsyncDisposable
    {
        private readonly HubConnection _connection;
        public ConcurrentQueue<(string Method, JsonElement Payload)> Events { get; } = new();

        public HubClient(AshApiFactory factory, string email)
        {
            _connection = new HubConnectionBuilder()
                .WithUrl(new Uri(factory.Server.BaseAddress, $"hubs/chat?dev_user={Uri.EscapeDataString(email)}"), o =>
                {
                    o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    o.Transports = HttpTransportType.LongPolling;
                })
                .Build();
            foreach (var method in new[] { "message.new", "message.edited", "message.deleted", "conversation.read", "participants.changed" })
            {
                _connection.On<JsonElement>(method, payload => Events.Enqueue((method, payload)));
            }
        }

        public Task StartAsync() => _connection.StartAsync(Ct).WaitAsync(WaitLimit, Ct);

        public async Task<JsonElement> WaitForAsync(string method, Func<JsonElement, bool> match)
        {
            var deadline = DateTime.UtcNow + WaitLimit;
            while (DateTime.UtcNow < deadline)
            {
                var hit = Events.Where(e => e.Method == method && match(e.Payload)).Select(e => (JsonElement?)e.Payload).FirstOrDefault();
                if (hit is { } found)
                {
                    return found;
                }

                await Task.Delay(50, Ct);
            }

            throw new TimeoutException($"No '{method}' event within {WaitLimit.TotalSeconds} s.");
        }

        public bool Got(string method, Func<JsonElement, bool> match) => Events.Any(e => e.Method == method && match(e.Payload));

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }

    private static bool HasBody(JsonElement pushed, string body) =>
        pushed.GetProperty("message").GetProperty("body").GetString() == body;

    /// <summary>No property whose name ends in "id" holds a number: the API shows public ids (GUIDs) only.</summary>
    private static void NoInternalIds(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase))
                    {
                        property.Value.ValueKind.ShouldNotBe(JsonValueKind.Number, $"'{property.Name}' is a number");
                    }

                    NoInternalIds(property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    NoInternalIds(item);
                }

                break;
        }
    }

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    // ---------------------------------------------------------------- tests

    [Fact]
    public async Task Two_participants_exchange_messages_live_and_the_unread_count_survives_a_reload()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var casePublicId = await PublicIdOf("cases", s.ActiveCaseId);
        await using var factory = new AshApiFactory(Db);
        using var avokat1 = factory.CreateClient().AsUser("avokat1@ash.local");
        using var avokat2 = factory.CreateClient().AsUser("avokat2@ash.local");

        var conversation = await Open(avokat1, casePublicId);
        (await Open(avokat2, casePublicId)).ShouldBe(conversation); // idempotent: one chat per case
        (await Participants(s.ActiveCaseId)).ShouldBe(Team);       // team + the monitoring institution's user
        (await SystemMessages(s.ActiveCaseId)).ShouldBeEmpty();     // the first opening adds everyone quietly

        await using var hub1 = new HubClient(factory, "avokat1@ash.local");
        await using var hub2 = new HubClient(factory, "avokat2@ash.local");
        await hub1.StartAsync();
        await hub2.StartAsync();

        var sent = await SendOk(avokat1, conversation, "  Përshëndetje, seanca u shty.  ");
        sent.GetProperty("body").GetString().ShouldBe("Përshëndetje, seanca u shty."); // trimmed
        NoInternalIds(sent);
        await hub2.WaitForAsync("message.new", p => HasBody(p, "Përshëndetje, seanca u shty."));

        // a new client (a reload): the counts come from the read pointers
        using var reloaded = factory.CreateClient().AsUser("avokat2@ash.local");
        (await UnreadTotal(reloaded)).ShouldBe(1);
        (await UnreadTotal(avokat1)).ShouldBe(0);               // one's own message is read
        var list = await reloaded.GetFromJsonAsync<JsonElement>("/api/chat/conversations", Ct);
        NoInternalIds(list);
        var item = list.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("unread").GetInt32().ShouldBe(1);
        item.GetProperty("lastMessagePreview").GetString().ShouldBe("Përshëndetje, seanca u shty.");
        item.GetProperty("title").GetString()!.ShouldContain("Gjykata e Shkallës së Parë Tiranë");

        var reply = await SendOk(avokat2, conversation, "Faleminderit.");
        await hub1.WaitForAsync("message.new", p => HasBody(p, "Faleminderit."));
        (await UnreadTotal(reloaded)).ShouldBe(0);              // replying moves one's own pointer past it
        (await UnreadTotal(avokat1)).ShouldBe(1);

        var read = await avokat1.PostAsJsonAsync($"/api/chat/conversations/{conversation}/read",
            new { lastMessageId = reply.GetProperty("id").GetGuid() }, Ct);
        read.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await hub2.WaitForAsync("conversation.read", _ => true);
        (await UnreadTotal(avokat1)).ShouldBe(0);

        // an older id never moves the pointer back
        (await avokat1.PostAsJsonAsync($"/api/chat/conversations/{conversation}/read",
            new { lastMessageId = sent.GetProperty("id").GetGuid() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await UnreadTotal(avokat1)).ShouldBe(0);

        var page = await Page(avokat2, conversation);
        NoInternalIds(page);
        page.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("body").GetString())
            .ShouldBe(["Faleminderit.", "Përshëndetje, seanca u shty."]);
    }

    [Fact]
    public async Task Kryetar_not_on_the_team_is_403_the_monitoring_institution_user_takes_part_direct_is_403()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var casePublicId = await PublicIdOf("cases", s.ActiveCaseId);
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");
        using var institucion = factory.CreateClient().AsUser("institucion@ash.local");
        using var avokat1 = factory.CreateClient().AsUser("avokat1@ash.local");

        var refused = await kryetar.PostAsync($"/api/chat/cases/{casePublicId}/conversation", null, Ct);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Json(refused)).GetProperty("code").GetString().ShouldBe("chat.not_allowed");

        var conversation = await Open(institucion, casePublicId);
        await SendOk(institucion, conversation, "Institucioni e ndjek çështjen.");
        (await kryetar.GetAsync($"/api/chat/conversations/{conversation}/messages", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // a user who cannot view the case: 404 (never which)
        using var other = factory.CreateClient().AsUser("protokoll@ash.local");
        (await other.PostAsync($"/api/chat/cases/{casePublicId}/conversation", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var direct = await avokat1.PostAsJsonAsync("/api/chat/direct",
            new { userId = await PublicIdOf("users", s.User("avokat2").Id) }, Ct);
        direct.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Json(direct)).GetProperty("title").GetString().ShouldBe("Biseda lejohet vetëm brenda çështjes.");
    }

    [Fact]
    public async Task After_a_transfer_the_old_lawyer_leaves_the_chat_and_its_group()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var thirdId = await seed.UserAsync("av3", "Avokat", "Tre", s.OfficeTrId, null, "STATE_LAWYER");
        var casePublicId = await PublicIdOf("cases", s.ActiveCaseId);
        await using var factory = new AshApiFactory(Db);
        using var avokat1 = factory.CreateClient().AsUser("avokat1@ash.local");
        using var avokat2 = factory.CreateClient().AsUser("avokat2@ash.local");
        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");
        var conversation = await Open(avokat1, casePublicId);
        await using var hub1 = new HubClient(factory, "avokat1@ash.local");
        await using var hub2 = new HubClient(factory, "avokat2@ash.local");
        await hub1.StartAsync();
        await hub2.StartAsync();

        var rowVersion = Convert.ToUInt32(await Scalar($"SELECT row_version FROM cases WHERE id = {s.ActiveCaseId}"));
        var transfer = await kryetar.PostAsJsonAsync($"/api/cases/{casePublicId}/transfer",
            new { newPrimaryUserPublicId = await PublicIdOf("users", thirdId), reason = "Ngarkesë pune", rowVersion }, Ct);
        transfer.StatusCode.ShouldBe(HttpStatusCode.OK, await transfer.Content.ReadAsStringAsync(Ct));

        // after the core commit: the old primary left, the new one joined — with system messages
        (await Participants(s.ActiveCaseId)).ShouldContain($"av3-{seed.Suffix}@ash.test");
        (await Participants(s.ActiveCaseId)).ShouldNotContain("avokat1@ash.local");
        var system = await SystemMessages(s.ActiveCaseId);
        system.ShouldContain("Avokat Tre u shtua në bisedë");
        system.ShouldContain(m => m.EndsWith("doli nga biseda"));
        await hub2.WaitForAsync("participants.changed", _ => true);

        (await avokat1.GetAsync($"/api/chat/conversations/{conversation}/messages", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Send(avokat1, conversation, "A më dëgjon dikush?")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await avokat1.PostAsync($"/api/chat/cases/{casePublicId}/conversation", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // out of the group at once: avokat2's next message reaches avokat2's own hub, not avokat1's
        await SendOk(avokat2, conversation, "Mirë se erdhe, kolegu i ri.");
        await hub2.WaitForAsync("message.new", p => HasBody(p, "Mirë se erdhe, kolegu i ri."));
        await Task.Delay(500, Ct);
        hub1.Got("message.new", p => HasBody(p, "Mirë se erdhe, kolegu i ri.")).ShouldBeFalse();

        // audited (participants, not messages)
        await WaitForAudit($"action = 'CHAT_PARTICIPANT_REMOVED' AND case_id = {s.ActiveCaseId}");
    }

    private async Task WaitForAudit(string where)
    {
        var deadline = DateTime.UtcNow + WaitLimit;
        while (Convert.ToInt64(await Scalar($"SELECT COUNT(*) FROM ash_audit.audit_log WHERE {where}", Db.AuditConnectionString)) == 0)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, $"no audit row where {where}");
            await Task.Delay(100, Ct);
        }
    }

    [Fact]
    public async Task An_added_assistant_joins_with_a_system_message_and_reconcile_repairs_a_missing_participant()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var assistantId = await seed.UserAsync("nd2", "Ndihmës", "Dy", s.OfficeTrId, null, "LEGAL_ASSISTANT");
        var casePublicId = await PublicIdOf("cases", s.ActiveCaseId);
        await using var factory = new AshApiFactory(Db);
        using var avokat1 = factory.CreateClient().AsUser("avokat1@ash.local");
        await Open(avokat1, casePublicId);

        var rowVersion = Convert.ToUInt32(await Scalar($"SELECT row_version FROM cases WHERE id = {s.ActiveCaseId}"));
        var added = await avokat1.PostAsJsonAsync($"/api/cases/{casePublicId}/assistants",
            new { userPublicId = await PublicIdOf("users", assistantId), rowVersion }, Ct);
        added.StatusCode.ShouldBe(HttpStatusCode.OK, await added.Content.ReadAsStringAsync(Ct));
        (await Participants(s.ActiveCaseId)).ShouldContain($"nd2-{seed.Suffix}@ash.test");
        (await SystemMessages(s.ActiveCaseId)).ShouldBe(["Ndihmës Dy u shtua në bisedë"]);

        // something went missing (e.g. a reconcile that failed after a commit): the nightly job puts it back, once
        await Execute($"""
            UPDATE conversation_participants SET left_at = NOW(3)
             WHERE user_id = {s.User("avokat2").Id}
               AND conversation_id = (SELECT id FROM conversations WHERE case_id = {s.ActiveCaseId})
            """, Db.ChatConnectionString);
        (await Participants(s.ActiveCaseId)).ShouldNotContain("avokat2@ash.local");

        await factory.Services.GetRequiredService<JobRunner>().RunAsync("chat-reconcile", DateTime.UtcNow, Ct);
        (await Participants(s.ActiveCaseId)).ShouldContain("avokat2@ash.local");
        await factory.Services.GetRequiredService<JobRunner>().RunAsync("chat-reconcile", DateTime.UtcNow, Ct);
        (await SystemMessages(s.ActiveCaseId)).Count(m => m.Contains("u shtua")).ShouldBe(2); // idempotent: no new one
    }

    private sealed class FailingAssistantHandler : IEventHandler<AssistantAssigned>
    {
        public Task HandleAsync(AssistantAssigned e, CancellationToken ct = default) =>
            throw new InvalidOperationException("the assignment fails after its event was published");
    }

    [Fact]
    public async Task A_rolled_back_assignment_changes_nothing_in_the_chat()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var assistantId = await seed.UserAsync("nd3", "Ndihmës", "Tre", s.OfficeTrId, null, "LEGAL_ASSISTANT");
        var casePublicId = await PublicIdOf("cases", s.ActiveCaseId);
        await using var factory = new AshApiFactory(Db,
            configureServices: services => services.AddScoped<IEventHandler<AssistantAssigned>, FailingAssistantHandler>());
        using var avokat1 = factory.CreateClient().AsUser("avokat1@ash.local");
        await Open(avokat1, casePublicId);
        var before = await Participants(s.ActiveCaseId);

        var rowVersion = Convert.ToUInt32(await Scalar($"SELECT row_version FROM cases WHERE id = {s.ActiveCaseId}"));
        var failed = await avokat1.PostAsJsonAsync($"/api/cases/{casePublicId}/assistants",
            new { userPublicId = await PublicIdOf("users", assistantId), rowVersion }, Ct);

        failed.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await Scalar($"SELECT COUNT(*) FROM case_assignments WHERE case_id = {s.ActiveCaseId} AND user_id = {assistantId}")).ShouldBe(0L);
        (await Participants(s.ActiveCaseId)).ShouldBe(before);
        (await SystemMessages(s.ActiveCaseId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_archived_dossier_makes_the_chat_read_only_and_keeps_its_history()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var casePublicId = await PublicIdOf("cases", s.ActiveCaseId);
        await using var factory = new AshApiFactory(Db);
        using var avokat1 = factory.CreateClient().AsUser("avokat1@ash.local");
        var conversation = await Open(avokat1, casePublicId);
        await SendOk(avokat1, conversation, "Para mbylljes.");

        // closed (final): the chat stays writable (task 020) …
        var header = (await avokat1.GetFromJsonAsync<JsonElement>($"/api/cases/{casePublicId}", Ct)).GetProperty("header");
        var close = await avokat1.PostAsJsonAsync($"/api/cases/{casePublicId}/decision",
            new { resultCode = "HUMBUR", decisionReceived = false, isFinal = true, rowVersion = header.GetProperty("rowVersion").GetUInt32() }, Ct);
        close.StatusCode.ShouldBe(HttpStatusCode.OK, await close.Content.ReadAsStringAsync(Ct));
        await SendOk(avokat1, conversation, "Pas mbylljes.");

        // … archived: read-only
        header = (await avokat1.GetFromJsonAsync<JsonElement>($"/api/cases/{casePublicId}", Ct)).GetProperty("header");
        var archive = await avokat1.PostAsJsonAsync($"/api/dossiers/{header.GetProperty("dossierPublicId").GetGuid()}/archive",
            new { rowVersion = header.GetProperty("rowVersion").GetUInt32() }, Ct);
        archive.StatusCode.ShouldBe(HttpStatusCode.OK, await archive.Content.ReadAsStringAsync(Ct));

        var refused = await Send(avokat1, conversation, "Pas arkivimit.");
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(refused)).GetProperty("title").GetString().ShouldBe("Biseda është vetëm për lexim.");
        (await Page(avokat1, conversation)).GetProperty("items").GetArrayLength().ShouldBe(2);
        (await Open(avokat1, casePublicId)).ShouldBe(conversation); // still opens, read-only
    }

    [Fact]
    public async Task Edit_within_15_minutes_only_delete_shows_a_placeholder_to_others_and_is_audited()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var casePublicId = await PublicIdOf("cases", s.ActiveCaseId);
        await using var factory = new AshApiFactory(Db);
        using var avokat1 = factory.CreateClient().AsUser("avokat1@ash.local");
        using var avokat2 = factory.CreateClient().AsUser("avokat2@ash.local");
        var conversation = await Open(avokat1, casePublicId);
        var message = (await SendOk(avokat1, conversation, "Gabim shtypi")).GetProperty("id").GetGuid();

        var edited = await avokat1.PutAsJsonAsync($"/api/chat/messages/{message}", new { body = "Gabim shtypi, u rregullua" }, Ct);
        edited.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(edited)).GetProperty("editedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        (await avokat2.PutAsJsonAsync($"/api/chat/messages/{message}", new { body = "jo i imi" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // 16 minutes later
        await Execute($"UPDATE messages SET created_at = created_at - INTERVAL 16 MINUTE WHERE public_id = UUID_TO_BIN('{message}', 1)",
            Db.ChatConnectionString);
        var late = await avokat1.PutAsJsonAsync($"/api/chat/messages/{message}", new { body = "shumë vonë" }, Ct);
        late.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(late)).GetProperty("code").GetString().ShouldBe("chat.edit_window");

        (await avokat1.DeleteAsync($"/api/chat/messages/{message}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var seen = (await Page(avokat2, conversation)).GetProperty("items").EnumerateArray().Single();
        seen.GetProperty("body").GetString().ShouldBe("Mesazh i fshirë");
        seen.GetProperty("isDeleted").GetBoolean().ShouldBeTrue();
        (await Scalar($"SELECT body FROM messages WHERE public_id = UUID_TO_BIN('{message}', 1)", Db.ChatConnectionString))
            .ShouldBe("Gabim shtypi, u rregullua"); // kept for the record
        await WaitForAudit($"action = 'CHAT_MESSAGE_DELETED' AND case_id = {s.ActiveCaseId}");
    }

    [Fact]
    public async Task The_31st_message_within_a_minute_is_429()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var casePublicId = await PublicIdOf("cases", s.ActiveCaseId);
        await using var factory = new AshApiFactory(Db);
        using var avokat1 = factory.CreateClient().AsUser("avokat1@ash.local");
        var conversation = await Open(avokat1, casePublicId);

        for (var i = 1; i <= ChatRateLimiter.PerMinute; i++)
        {
            await SendOk(avokat1, conversation, $"Mesazhi {i}");
        }

        var limited = await Send(avokat1, conversation, "Një më shumë");
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await Json(limited)).GetProperty("code").GetString().ShouldBe("chat.rate_limited");
        (await Send(avokat1, conversation, "")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Send(avokat1, conversation, new string('a', 4001))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Ten_thousand_messages_over_three_months_page_without_duplicates_and_EXPLAIN_prunes_partitions()
    {
        // three whole months that have their own partitions (the rotation keeps this month + 3 ahead), never p_max;
        // the API's clock stands just after them, so every message is in its past
        var now = DateTime.UtcNow;
        var m0 = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = m0.AddMonths(3);
        var partitions = await Strings($"""
            SELECT partition_name FROM information_schema.partitions
             WHERE table_schema = 'ash_chat' AND table_name = 'messages' ORDER BY partition_ordinal_position
            """);
        for (var m = 0; m <= 3; m++)
        {
            partitions.ShouldContain($"p{m0.AddMonths(m):yyyyMM}");
        }

        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var casePublicId = await PublicIdOf("cases", s.ActiveCaseId);
        await using var factory = new AshApiFactory(Db,
            configureServices: services => services.AddSingleton<IClock>(new FixedClock(end.AddDays(1))));
        using var avokat1 = factory.CreateClient().AsUser("avokat1@ash.local");
        var conversation = await Open(avokat1, casePublicId);
        var conversationId = Convert.ToUInt64(await Scalar(
            $"SELECT id FROM conversations WHERE public_id = UUID_TO_BIN('{conversation}', 1)", Db.ChatConnectionString));

        const int count = 10_000;
        var seconds = (long)(end - m0).TotalSeconds;
        await Execute($"""
            SET SESSION cte_max_recursion_depth = {count + 10};
            INSERT INTO messages (created_at, conversation_id, sender_user_id, message_type, body)
            WITH RECURSIVE n (i) AS (SELECT 0 UNION ALL SELECT i + 1 FROM n WHERE i < {count - 1})
            SELECT '{m0:yyyy-MM-dd HH:mm:ss}' + INTERVAL (i * {seconds} DIV {count}) SECOND,
                   {conversationId}, {s.User("avokat2").Id}, 'TEXT', CONCAT('Mesazhi ', i)
              FROM n;
            ANALYZE TABLE messages;
            """, Db.ChatConnectionString);
        (await Scalar($"SELECT COUNT(*) FROM messages PARTITION (p_max)", Db.ChatConnectionString)).ShouldBe(0L);

        // EXPLAIN the API's own statement with a cursor in the middle month: later months and p_max are pruned
        var cursorAt = m0.AddMonths(1).AddDays(14);
        await using (var connection = new MySqlConnection(Db.ChatConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var explain = new MySqlCommand("EXPLAIN " + ChatService.PageSql, connection);
            explain.Parameters.AddWithValue("@conversation", conversationId);
            explain.Parameters.AddWithValue("@before_at", cursorAt);
            explain.Parameters.AddWithValue("@before_id", ulong.MaxValue);
            explain.Parameters.AddWithValue("@take", 51);
            await using var reader = await explain.ExecuteReaderAsync(Ct);
            (await reader.ReadAsync(Ct)).ShouldBeTrue();
            var used = reader.GetString(reader.GetOrdinal("partitions")).Split(',');
            used.ShouldContain($"p{m0.AddMonths(1):yyyyMM}");
            used.ShouldNotContain($"p{m0.AddMonths(2):yyyyMM}");
            used.ShouldNotContain($"p{m0.AddMonths(3):yyyyMM}");
            used.ShouldNotContain("p_max");
            reader.GetString(reader.GetOrdinal("key")).ShouldBe("ix_msg_conv_time");
        }

        // every message once, newest first, across both month boundaries (100 a page)
        var seen = new List<(Guid Id, DateTime At)>();
        string? before = null;
        var started = DateTime.UtcNow;
        do
        {
            var page = await Page(avokat1, conversation, before, 100);
            seen.AddRange(page.GetProperty("items").EnumerateArray()
                .Select(m => (m.GetProperty("id").GetGuid(), m.GetProperty("createdAt").GetDateTime())));
            before = page.GetProperty("nextCursor").GetString();
        }
        while (before is not null);

        seen.Count.ShouldBe(count);
        seen.Select(x => x.Id).Distinct().Count().ShouldBe(count);
        seen.Select(x => x.At).ShouldBeInOrder(SortDirection.Descending);
        seen.Select(x => x.At.Month).Distinct().Count().ShouldBe(3);
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(60)); // 100 pages
    }
}
