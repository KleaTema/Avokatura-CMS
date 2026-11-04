using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Api.Logging;
using Ash.Infrastructure.Persistence;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Documents.Contracts.Events;
using Ash.Modules.Notifications.Contracts;
using Ash.Modules.Notifications.Realtime;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Identity;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Serilog.Core;
using Serilog.Events;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 040 — notifications: rows stored in the action's transaction by the event handlers, one per recipient, never
/// for the actor or an inactive user; pushed over SignalR only after the commit; the API shows each user only their
/// own; hub credentials in the query string never reach a log. Uses <see cref="CaseSeed"/> (office TR team on the
/// ACTIVE case: avokat1 primary, avokat2 secondary, ndihmes assistant, jurist; the ministry MONITORING it).
/// </summary>
public class NotificationTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Court = "Gjykata e Shkallës së Parë Tiranë";
    private const string SeededMinistry = "Ministria e Financave dhe Ekonomisë";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------- helpers

    private sealed record Row(string Email, string Type, string Title, string? Body, string? LinkPath, long? CaseId);

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(Ct);
        return value is DBNull ? null : value;
    }

    private async Task<List<Row>> Rows(string where = "1 = 1")
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand($"""
            SELECT u.email, n.notification_type, n.title, n.body, n.link_path, n.case_id
            FROM notifications n JOIN users u ON u.id = n.user_id WHERE {where} ORDER BY u.email, n.id
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<Row>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(new Row(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt64(5)));
        }

        return rows;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private async Task<Guid> PublicIdOf(string table, long id) =>
        Ash.SharedKernel.Ids.PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM {table} WHERE id = {id}"))!);

    private async Task<string> CaseCode(long caseId) => (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {caseId}"))!;

    private async Task<uint> RowVersion(long caseId) => Convert.ToUInt32(await Scalar($"SELECT row_version FROM cases WHERE id = {caseId}"));

    private async Task<Guid> UserPublicId(CaseSeed s, string key) => await PublicIdOf("users", s.User(key).Id);

    private async Task<long> InstitutionId(string name) => Convert.ToInt64(await Scalar($"SELECT id FROM institutions WHERE name = '{name}'"));

    /// <summary>The active users of an institution with a role (the expected recipients, from the data itself).</summary>
    private async Task<List<string>> ActiveEmails(string role, long? institutionId = null)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        var institution = institutionId is { } i ? $"AND u.institution_id = {i}" : string.Empty;
        await using var command = new MySqlCommand($"""
            SELECT u.email FROM users u JOIN user_roles ur ON ur.user_id = u.id JOIN roles r ON r.id = ur.role_id
            WHERE r.code = '{role}' AND u.is_active = 1 {institution} ORDER BY u.email
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var emails = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            emails.Add(reader.GetString(0));
        }

        return emails;
    }

    /// <summary>Records every push; for a new notification, whether its row was already committed when it was sent.</summary>
    private sealed class HubSpy : IHubContext<NotificationsHub>
    {
        private readonly string _connectionString;

        public HubSpy(string connectionString)
        {
            _connectionString = connectionString;
            Clients = new SpyClients(this);
        }

        public ConcurrentQueue<(string User, string Method, object? Payload, bool Committed)> Sent { get; } = new();

        public IHubClients Clients { get; }
        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class SpyClients(HubSpy spy) : IHubClients
        {
            public IClientProxy All => throw new NotSupportedException();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Client(string connectionId) => throw new NotSupportedException();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
            public IClientProxy Group(string groupName) => throw new NotSupportedException();
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
            public IClientProxy User(string userId) => new Proxy(spy, userId);
            public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
        }

        public async Task WaitForAsync(int count, Func<(string User, string Method, object? Payload, bool Committed), bool>? filter = null)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (Sent.Count(x => filter?.Invoke(x) ?? true) < count && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50, Ct);
            }

            Sent.Count(x => filter?.Invoke(x) ?? true).ShouldBeGreaterThanOrEqualTo(count, "pushes expected");
        }

        private sealed class Proxy(HubSpy spy, string user) : IClientProxy
        {
            public async Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                var payload = args.SingleOrDefault();
                var committed = false;
                if (payload is NotificationPushed n)
                {
                    // a separate connection sees only committed rows
                    await using var connection = new MySqlConnection(spy._connectionString);
                    await connection.OpenAsync(cancellationToken);
                    await using var command = new MySqlCommand("SELECT COUNT(*) FROM notifications WHERE public_id = @p", connection);
                    command.Parameters.AddWithValue("@p", Ash.SharedKernel.Ids.PublicId.ToBytes(n.PublicId));
                    committed = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
                }

                spy.Sent.Enqueue((user, method, payload, committed));
            }
        }
    }

    /// <summary>Fails the request after the notification handlers ran (registered after them).</summary>
    private sealed class FailAfterAssigned : IEventHandler<LawyerAssigned>
    {
        public Task HandleAsync(LawyerAssigned @event, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("a later step of the action failed");
    }

    private sealed class Actor(long id) : ICurrentUser
    {
        public long UserId => id;
        public Guid PublicId => Guid.Empty;
        public long? OfficeId => null;
        public long? InstitutionId => null;
        public IReadOnlyCollection<string> Roles => [];
        public bool HasPermission(string permission) => false;
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }

    private AshApiFactory Factory(HubSpy? spy = null, Action<IServiceCollection>? more = null) =>
        new(Db, configureServices: services =>
        {
            if (spy is not null)
            {
                services.AddSingleton<IHubContext<NotificationsHub>>(spy);
            }

            more?.Invoke(services);
        });

    /// <summary>Publishes an event the way an action does: in a transaction of its own scope, committed; actor optional.</summary>
    private static async Task PublishAsync<TEvent>(AshApiFactory factory, TEvent @event, long? actorId = null) where TEvent : IEvent
    {
        await using var scope = factory.Services.CreateAsyncScope();
        if (actorId is { } id)
        {
            scope.ServiceProvider.GetRequiredService<InheritedCurrentUser>().User = new Actor(id);
        }

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.BeginAsync(Ct);
        await scope.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(@event, Ct);
        await unitOfWork.CommitAsync(Ct);
    }

    private static string Link(Guid casePublicId, string tab) => $"/cases/{casePublicId}/{tab}";

    // ---------------------------------------------------------------- producers through the API

    [Fact]
    public async Task Assigning_lawyers_notifies_primary_secondary_and_the_linked_institution_but_not_the_kryetar()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var ministry = s.User("institucion").InstitutionId!.Value;
        await Scalar($"""
            INSERT INTO case_institutions (case_id, institution_id, link_mode, started_by)
            VALUES ({s.RegisteredCaseId}, {ministry}, 'MONITORING', {s.User("protokoll").Id})
            """);
        // the object is case content: it never appears in a notification
        await Scalar($"UPDATE cases SET object_text = 'OBJEKT-SEKRET-040' WHERE id = {s.RegisteredCaseId}");
        var casePublicId = await PublicIdOf("cases", s.RegisteredCaseId);
        var code = await CaseCode(s.RegisteredCaseId);
        await using var factory = Factory();
        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");

        var response = await kryetar.PostAsJsonAsync($"/api/cases/{casePublicId}/assignment", new
        {
            primaryUserPublicId = await UserPublicId(s, "avokat1"),
            secondaryUserPublicId = await UserPublicId(s, "avokat2"),
            rowVersion = await RowVersion(s.RegisteredCaseId)
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = await Rows();
        rows.Where(r => r.Type == NotificationTypes.LawyerAssigned).Select(r => $"{r.Email}|{r.Body}").ShouldBe([
            $"avokat1@ash.local|U caktuat avokat kryesor në çështjen {code} ({Court}).",
            $"avokat2@ash.local|U caktuat avokat dytësor në çështjen {code} ({Court}).",
        ]);
        rows.Where(r => r.Type == NotificationTypes.InstitutionCaseAssigned).Select(r => r.Email)
            .ShouldBe(await ActiveEmails("INSTITUTION_USER", ministry));
        rows.ShouldAllBe(r => r.Email != "kryetar@ash.local");
        rows.ShouldAllBe(r => r.LinkPath == Link(casePublicId, "te-dhenat") && r.CaseId == s.RegisteredCaseId);
        rows.ShouldAllBe(r => !r.Body!.Contains("OBJEKT-SEKRET-040") && !r.Title.Contains("OBJEKT-SEKRET-040"));
        rows.Single(r => r.Type == NotificationTypes.InstitutionCaseAssigned).Body
            .ShouldBe($"Çështja {code} ({Court}) iu caktua avokatit Avokat1 Dev.");
    }

    [Fact]
    public async Task The_kryetar_assigning_himself_gets_no_notification()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = Factory();
        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");

        var response = await kryetar.PostAsJsonAsync($"/api/cases/{await PublicIdOf("cases", s.RegisteredCaseId)}/assignment", new
        {
            primaryUserPublicId = await UserPublicId(s, "kryetar"),
            secondaryUserPublicId = await UserPublicId(s, "avokat2"),
            rowVersion = await RowVersion(s.RegisteredCaseId)
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Rows()).Select(r => r.Email).ShouldBe(["avokat2@ash.local"]);
    }

    [Fact]
    public async Task A_failing_action_after_the_event_leaves_no_notification_and_pushes_nothing()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var spy = new HubSpy(Db.AppConnectionString);
        await using var factory = Factory(spy, services => services.AddScoped<IEventHandler<LawyerAssigned>, FailAfterAssigned>());
        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");

        var response = await kryetar.PostAsJsonAsync($"/api/cases/{await PublicIdOf("cases", s.RegisteredCaseId)}/assignment", new
        {
            primaryUserPublicId = await UserPublicId(s, "avokat1"),
            rowVersion = await RowVersion(s.RegisteredCaseId)
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await Rows()).ShouldBeEmpty();
        (await Scalar($"SELECT COUNT(*) FROM case_assignments WHERE case_id = {s.RegisteredCaseId}")).ShouldBe(0L);
        await Task.Delay(500, Ct);
        spy.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Pushes_go_out_only_after_the_commit_to_each_recipient_with_the_new_count()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var spy = new HubSpy(Db.AppConnectionString);
        await using var factory = Factory(spy);
        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");
        var avokat1 = await UserPublicId(s, "avokat1");
        var avokat2 = await UserPublicId(s, "avokat2");

        var response = await kryetar.PostAsJsonAsync($"/api/cases/{await PublicIdOf("cases", s.RegisteredCaseId)}/assignment", new
        {
            primaryUserPublicId = avokat1,
            secondaryUserPublicId = avokat2,
            rowVersion = await RowVersion(s.RegisteredCaseId)
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await spy.WaitForAsync(4);
        var created = spy.Sent.Where(x => x.Method == NotificationsHub.NewMethod).ToList();
        created.Select(x => x.User).OrderBy(x => x).ShouldBe(new[] { avokat1.ToString(), avokat2.ToString() }.OrderBy(x => x));
        created.ShouldAllBe(x => x.Committed);
        created.Select(x => ((NotificationPushed)x.Payload!).Title).ShouldAllBe(t => t == "Ju u caktua një çështje");
        var counts = spy.Sent.Where(x => x.Method == NotificationsHub.CountMethod).ToList();
        counts.Select(x => x.User).OrderBy(x => x).ShouldBe(new[] { avokat1.ToString(), avokat2.ToString() }.OrderBy(x => x));
        counts.ShouldAllBe(x => ((UnreadCountPushed)x.Payload!).Unread == 1);
        var kryetarId = (await UserPublicId(s, "kryetar")).ToString();
        spy.Sent.ShouldAllBe(x => x.User != kryetarId);

        // the pushed notification is the stored one
        using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");
        var item = (await Json(await lawyer.GetAsync("/api/notifications", Ct))).GetProperty("items")[0];
        var pushed = (NotificationPushed)created.Single(x => x.User == avokat1.ToString()).Payload!;
        item.GetProperty("publicId").GetGuid().ShouldBe(pushed.PublicId);
        item.GetProperty("linkPath").GetString().ShouldBe(pushed.LinkPath);
        item.GetProperty("createdAt").GetDateTime().ToUniversalTime().ShouldBe(pushed.CreatedAt);
    }

    // ---------------------------------------------------------------- producers, one per event (step 0)

    [Fact]
    public async Task Case_registered_tells_the_office_head()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = Factory();
        var casePublicId = await PublicIdOf("cases", s.RegisteredCaseId);

        await PublishAsync(factory, new CaseRegistered(casePublicId, s.OfficeTrId, await UserPublicId(s, "kryetar")), s.User("protokoll").Id);

        var row = (await Rows()).ShouldHaveSingleItem();
        row.ShouldBe(new Row("kryetar@ash.local", NotificationTypes.CaseRegistered, "Çështje e re për caktim",
            $"Çështja {await CaseCode(s.RegisteredCaseId)} ({Court}) u regjistrua dhe pret caktimin e avokatit.",
            Link(casePublicId, "te-dhenat"), s.RegisteredCaseId));
    }

    [Fact]
    public async Task Transfer_tells_the_new_and_the_old_lawyer_of_each_changed_role()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = Factory();
        var casePublicId = await PublicIdOf("cases", s.ActiveCaseId);
        var code = await CaseCode(s.ActiveCaseId);
        var (avokat1, avokat2, kryetar) = (await UserPublicId(s, "avokat1"), await UserPublicId(s, "avokat2"), await UserPublicId(s, "kryetar"));

        // the primary moves to the secondary; the secondary role ends
        await PublishAsync(factory, new LawyerTransferred(casePublicId, avokat1, avokat2, avokat2, null), s.User("kryetar").Id);

        (await Rows()).Select(r => $"{r.Email}|{r.Type}|{r.Body}").ShouldBe([
            $"avokat1@ash.local|{NotificationTypes.LawyerRemoved}|Nuk jeni më avokat kryesor në çështjen {code} ({Court}); u caktua Avokat2 Dev.",
            $"avokat2@ash.local|{NotificationTypes.LawyerAssigned}|U caktuat avokat kryesor në çështjen {code} ({Court}).",
        ]);
        await Scalar("DELETE FROM notifications");

        // only the secondary changes: the primary hears nothing
        await PublishAsync(factory, new LawyerTransferred(casePublicId, avokat1, avokat1, avokat2, kryetar), s.User("admin").Id);

        (await Rows()).Select(r => $"{r.Email}|{r.Type}|{r.Body}").ShouldBe([
            $"avokat2@ash.local|{NotificationTypes.LawyerRemoved}|Nuk jeni më avokat dytësor në çështjen {code} ({Court}); u caktua Kryetar Dev.",
            $"kryetar@ash.local|{NotificationTypes.LawyerAssigned}|U caktuat avokat dytësor në çështjen {code} ({Court}).",
        ]);
    }

    [Fact]
    public async Task Assistant_added_and_removed_tells_the_assistant()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = Factory();
        var casePublicId = await PublicIdOf("cases", s.ActiveCaseId);
        var assistant = await UserPublicId(s, "ndihmes");

        await PublishAsync(factory, new AssistantAssigned(casePublicId, assistant), s.User("avokat1").Id);
        await PublishAsync(factory, new AssistantRemoved(casePublicId, assistant), s.User("avokat1").Id);

        (await Rows()).Select(r => $"{r.Email}|{r.Type}|{r.Title}").ShouldBe([
            $"ndihmes@ash.local|{NotificationTypes.AssistantAssigned}|U shtuat si ndihmës ligjor",
            $"ndihmes@ash.local|{NotificationTypes.AssistantRemoved}|U hoqët si ndihmës ligjor",
        ]);
    }

    [Fact]
    public async Task Jurist_assigned_tells_the_new_jurist_and_the_one_replaced()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = Factory();
        var casePublicId = await PublicIdOf("cases", s.ActiveCaseId);
        var code = await CaseCode(s.ActiveCaseId);
        var ministry = await PublicIdOf("institutions", s.User("institucion").InstitutionId!.Value);
        var jurist2Email = (string)(await Scalar($"SELECT email FROM users WHERE id = {s.User("jurist2").Id}"))!;

        await PublishAsync(factory, new JuristAssigned(casePublicId, ministry, await UserPublicId(s, "jurist2"), await UserPublicId(s, "jurist")),
            s.User("institucion").Id);

        (await Rows()).Select(r => $"{r.Email}|{r.Type}|{r.Body}").OrderBy(x => x).ShouldBe(new[]
        {
            $"jurist@ash.local|{NotificationTypes.JuristReplaced}|{SeededMinistry} caktoi juristin Jurist Two në vendin tuaj në çështjen {code} ({Court}).",
            $"{jurist2Email}|{NotificationTypes.JuristAssigned}|{SeededMinistry} ju caktoi jurist në çështjen {code} ({Court}).",
        }.OrderBy(x => x));
    }

    [Fact]
    public async Task Institution_linked_tells_its_active_institution_users()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = Factory();
        var casePublicId = await PublicIdOf("cases", s.RegisteredCaseId);
        var ministryId = s.User("institucion").InstitutionId!.Value;

        await PublishAsync(factory, new InstitutionLinked(casePublicId, await PublicIdOf("institutions", ministryId), "DELEGATED"), s.User("protokoll").Id);

        var code = await CaseCode(s.RegisteredCaseId);
        var rows = await Rows();
        rows.Select(r => r.Email).ShouldBe(await ActiveEmails("INSTITUTION_USER", ministryId));
        rows.ShouldAllBe(r => r.Type == NotificationTypes.InstitutionLinked
            && r.Body == $"{SeededMinistry} u lidh me çështjen {code} ({Court}) me delegim.");
    }

    [Fact]
    public async Task Delegation_and_its_end_tell_the_institution_users_and_its_jurist_on_the_case()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = Factory();
        var casePublicId = await PublicIdOf("cases", s.ActiveCaseId);
        var code = await CaseCode(s.ActiveCaseId);
        var ministryId = s.User("institucion").InstitutionId!.Value;
        var ministry = await PublicIdOf("institutions", ministryId);
        var expected = (await ActiveEmails("INSTITUTION_USER", ministryId)).Append("jurist@ash.local").OrderBy(x => x).ToList();

        await PublishAsync(factory, new CaseDelegated(casePublicId, ministry, "RELATIM"), s.User("avokat1").Id);

        var rows = await Rows();
        rows.Select(r => r.Email).OrderBy(x => x).ShouldBe(expected); // jurist2 is of the ministry but not on the case
        rows.ShouldAllBe(r => r.Type == NotificationTypes.CaseDelegated && r.Title == "Çështja u relatua"
            && r.Body == $"Çështja {code} ({Court}) iu relatua institucionit {SeededMinistry}.");
        await Scalar("DELETE FROM notifications");

        await PublishAsync(factory, new CaseUndelegated(casePublicId, ministry), s.User("avokat1").Id);

        rows = await Rows();
        rows.Select(r => r.Email).OrderBy(x => x).ShouldBe(expected);
        rows.ShouldAllBe(r => r.Type == NotificationTypes.CaseUndelegated);
    }

    [Fact]
    public async Task Previous_level_not_closed_tells_the_primary_and_the_head_with_the_new_hearing_date()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = Factory();
        var casePublicId = await PublicIdOf("cases", s.ActiveCaseId);
        var code = await CaseCode(s.ActiveCaseId);

        // 09:00 UTC on 20 November = 10:00 in Tirane (CET)
        await PublishAsync(factory, new PreviousLevelNotClosed(casePublicId, "FL-2/26", new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc),
            await UserPublicId(s, "avokat1"), await UserPublicId(s, "kryetar")), s.User("protokoll").Id);

        var rows = await Rows();
        rows.Select(r => r.Email).ShouldBe(["avokat1@ash.local", "kryetar@ash.local"]);
        rows.ShouldAllBe(r => r.Type == NotificationTypes.PreviousLevelNotClosed && r.LinkPath == Link(casePublicId, "vendimi")
            && r.Body == $"Erdhi thirrja FL-2/26 për shkallën tjetër, me seancë më 20.11.2026 10:00, por çështja {code} ({Court}) nuk është mbyllur në sistem. Regjistroni vendimin.");

        // no primary lawyer: the head only
        await Scalar("DELETE FROM notifications");
        await PublishAsync(factory, new PreviousLevelNotClosed(casePublicId, "FL-3/26", DateTime.UtcNow.AddDays(9), null, await UserPublicId(s, "kryetar")));
        (await Rows()).Select(r => r.Email).ShouldBe(["kryetar@ash.local"]);
    }

    [Fact]
    public async Task Case_closed_tells_the_team_except_the_one_who_closed_it()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = Factory();
        var casePublicId = await PublicIdOf("cases", s.ActiveCaseId);
        var code = await CaseCode(s.ActiveCaseId);

        await PublishAsync(factory, new CaseClosed(casePublicId, code, "PRANUAR", true, false, DateTime.UtcNow), s.User("avokat1").Id);

        var rows = await Rows();
        rows.Select(r => r.Email).ShouldBe(["avokat2@ash.local", "jurist@ash.local", "ndihmes@ash.local"]);
        rows.ShouldAllBe(r => r.Type == NotificationTypes.CaseClosed && r.LinkPath == Link(casePublicId, "vendimi")
            && r.Body == $"Avokat1 Dev mbylli çështjen {code} ({Court}) me regjistrimin e vendimit.");
    }

    [Fact]
    public async Task Rejected_and_unscannable_documents_tell_the_uploader_and_the_admins_and_every_verdict_is_pushed()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var spy = new HubSpy(Db.AppConnectionString);
        await using var factory = Factory(spy);
        var casePublicId = await PublicIdOf("cases", s.ActiveCaseId);
        var code = await CaseCode(s.ActiveCaseId);
        var uploader = s.User("avokat2").Id;
        var uploaderPublicId = (await UserPublicId(s, "avokat2")).ToString();
        var expected = (await ActiveEmails("ADMIN")).Append("avokat2@ash.local").OrderBy(x => x).ToList();
        var (infected, failed, clean) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        // the scan job has no current user: nobody is skipped as the actor
        await PublishAsync(factory, new DocumentInfected(infected, s.ActiveCaseId, uploader, "Titull sekret", "u gjet virus (EICAR)"));

        var rows = await Rows();
        rows.Select(r => r.Email).OrderBy(x => x).ShouldBe(expected);
        rows.ShouldAllBe(r => r.Type == NotificationTypes.DocumentInfected && r.LinkPath == Link(casePublicId, "dokumentet")
            && r.Body == $"Një dokument i ngarkuar në çështjen {code} ({Court}) u refuzua nga kontrolli i sigurisë.");
        await Scalar("DELETE FROM notifications");

        await PublishAsync(factory, new DocumentScanFailed(failed, s.ActiveCaseId, uploader, "Titull sekret"));
        rows = await Rows();
        rows.Select(r => r.Email).OrderBy(x => x).ShouldBe(expected);
        rows.ShouldAllBe(r => r.Type == NotificationTypes.DocumentScanFailed && !r.Body!.Contains("Titull sekret"));
        await Scalar("DELETE FROM notifications");

        // CLEAN: pushed, never stored
        await PublishAsync(factory, new DocumentScanned(clean, uploader, "CLEAN"));
        (await Rows()).ShouldBeEmpty();

        await spy.WaitForAsync(3, x => x.Method == NotificationsHub.DocumentStatusMethod);
        spy.Sent.Where(x => x.Method == NotificationsHub.DocumentStatusMethod)
            .Select(x => (x.User, (DocumentStatusPushed)x.Payload!)).OrderBy(x => x.Item2.ScanStatus)
            .ShouldBe([
                (uploaderPublicId, new DocumentStatusPushed(clean, "CLEAN")),
                (uploaderPublicId, new DocumentStatusPushed(failed, "FAILED")),
                (uploaderPublicId, new DocumentStatusPushed(infected, "INFECTED")),
            ]);
    }

    [Fact]
    public async Task A_deactivated_user_gets_nothing()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = Factory();
        var casePublicId = await PublicIdOf("cases", s.ActiveCaseId);
        await Scalar($"UPDATE users SET is_active = 0 WHERE id = {s.User("drlawyer").Id}");

        await PublishAsync(factory, new AssistantAssigned(casePublicId, await UserPublicId(s, "drlawyer")), s.User("avokat1").Id);
        await PublishAsync(factory, new PreviousLevelNotClosed(casePublicId, "FL-9", DateTime.UtcNow.AddDays(5),
            await UserPublicId(s, "drlawyer"), await UserPublicId(s, "kryetar")));

        (await Rows()).Select(r => r.Email).ShouldBe(["kryetar@ash.local"]);
    }

    // ---------------------------------------------------------------- the API: own rows only

    private async Task<long> InsertAsync(long userId, long? caseId, DateTime createdAt, bool read = false, string title = "T")
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand("""
            INSERT INTO notifications (user_id, notification_type, case_id, title, body, link_path, created_at, read_at)
            VALUES (@u, 'CASE_CLOSED', @c, @t, NULL, NULL, @at, @r)
            """, connection);
        command.Parameters.AddWithValue("@u", userId);
        command.Parameters.AddWithValue("@c", (object?)caseId ?? DBNull.Value);
        command.Parameters.AddWithValue("@t", title);
        command.Parameters.AddWithValue("@at", createdAt);
        command.Parameters.AddWithValue("@r", read ? createdAt : DBNull.Value);
        await command.ExecuteNonQueryAsync(Ct);
        return command.LastInsertedId;
    }

    [Fact]
    public async Task List_pages_by_keyset_newest_first_with_the_total_and_only_the_users_own_rows()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var me = s.User("avokat1").Id;
        var start = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 25; i++)
        {
            // pairs share created_at: the id breaks the tie
            await InsertAsync(me, s.ActiveCaseId, start.AddMinutes(i / 2), read: i % 3 == 0, title: $"N{i:00}");
        }

        await InsertAsync(s.User("avokat2").Id, s.ActiveCaseId, start.AddDays(1), title: "SOMEONE-ELSE");
        await using var factory = Factory();
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");

        var titles = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await Json(await client.GetAsync($"/api/notifications{(cursor is null ? string.Empty : $"?after={cursor}")}", Ct));
            page.GetProperty("total").GetInt32().ShouldBe(25);
            var items = page.GetProperty("items").EnumerateArray().ToList();
            items.Count.ShouldBeLessThanOrEqualTo(10); // the default limit
            titles.AddRange(items.Select(x => x.GetProperty("title").GetString()!));
            JsonAssert.NoNumericIds(page);
            cursor = page.GetProperty("nextCursor").ValueKind == JsonValueKind.Null ? null : page.GetProperty("nextCursor").GetString();
            pages++;
        }
        while (cursor is not null && pages < 10); // bounded: a cursor that never ends fails below instead of looping

        pages.ShouldBe(3);
        titles.ShouldBe(Enumerable.Range(0, 25).Reverse().Select(i => $"N{i:00}"));

        var unread = await Json(await client.GetAsync("/api/notifications?unreadOnly=true&limit=50", Ct));
        unread.GetProperty("total").GetInt32().ShouldBe(16);
        unread.GetProperty("items").EnumerateArray().ShouldAllBe(x => x.GetProperty("readAt").ValueKind == JsonValueKind.Null);

        (await Json(await client.GetAsync("/api/notifications?limit=500", Ct))).GetProperty("items").GetArrayLength().ShouldBe(25); // max 50
        (await client.GetAsync("/api/notifications?after=not-a-cursor", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(await client.GetAsync("/api/notifications/unread-count", Ct))).GetProperty("unread").GetInt32().ShouldBe(16);
    }

    [Fact]
    public async Task Read_and_read_all_change_only_the_users_own_rows_and_another_users_notification_is_404()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var me = s.User("avokat1").Id;
        var other = s.User("avokat2").Id;
        var now = DateTime.UtcNow;
        var mine = await InsertAsync(me, s.ActiveCaseId, now.AddMinutes(-3));
        await InsertAsync(me, s.ActiveCaseId, now.AddMinutes(-2));
        await InsertAsync(me, null, now.AddMinutes(-1));
        var theirs = await InsertAsync(other, s.ActiveCaseId, now);
        var spy = new HubSpy(Db.AppConnectionString);
        await using var factory = Factory(spy);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");
        using var otherClient = factory.CreateClient().AsUser("avokat2@ash.local");

        var theirsId = await PublicIdOf("notifications", theirs);
        (await client.PostAsync($"/api/notifications/{theirsId}/read", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsync($"/api/notifications/{Guid.NewGuid()}/read", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Scalar($"SELECT read_at FROM notifications WHERE id = {theirs}")).ShouldBeNull();

        var read = await client.PostAsync($"/api/notifications/{await PublicIdOf("notifications", mine)}/read", null, Ct);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(read)).GetProperty("unread").GetInt32().ShouldBe(2);
        var readAt = await Scalar($"SELECT read_at FROM notifications WHERE id = {mine}");
        readAt.ShouldNotBeNull();
        // again: idempotent, the first read time stays
        (await client.PostAsync($"/api/notifications/{await PublicIdOf("notifications", mine)}/read", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Scalar($"SELECT read_at FROM notifications WHERE id = {mine}")).ShouldBe(readAt);
        (await Json(await client.GetAsync("/api/notifications/unread-count", Ct))).GetProperty("unread").GetInt32().ShouldBe(2);
        (await Json(await otherClient.GetAsync("/api/notifications/unread-count", Ct))).GetProperty("unread").GetInt32().ShouldBe(1);

        var all = await Json(await client.PostAsync("/api/notifications/read-all", null, Ct));
        all.GetProperty("marked").GetInt32().ShouldBe(2);
        all.GetProperty("unread").GetInt32().ShouldBe(0);
        (await Scalar($"SELECT COUNT(*) FROM notifications WHERE user_id = {me} AND read_at IS NULL")).ShouldBe(0L);
        (await Scalar($"SELECT read_at FROM notifications WHERE id = {theirs}")).ShouldBeNull();
        (await Json(await otherClient.GetAsync("/api/notifications", Ct))).GetProperty("total").GetInt32().ShouldBe(1);

        // every open tab of the reader hears the new count, after the commit
        var me1 = (await PublicIdOf("users", me)).ToString();
        await spy.WaitForAsync(2, x => x.Method == NotificationsHub.CountMethod);
        spy.Sent.Where(x => x.Method == NotificationsHub.CountMethod).ShouldAllBe(x => x.User == me1);
        spy.Sent.Where(x => x.Method == NotificationsHub.CountMethod).Select(x => ((UnreadCountPushed)x.Payload!).Unread)
            .ShouldBe([2, 0], ignoreOrder: true);
    }

    [Fact]
    public async Task Notifications_need_an_authenticated_user()
    {
        await using var factory = Factory();
        using var anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/api/notifications", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/notifications/unread-count", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync("/api/notifications/read-all", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------- the hub and the logs

    private const string Negotiate = "/hubs/notifications/negotiate?negotiateVersion=1";

    [Fact]
    public async Task The_hub_rejects_a_connection_without_a_valid_user_and_takes_the_dev_user_from_the_query_only_there()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = Factory();
        using var client = factory.CreateClient();

        (await client.PostAsync(Negotiate, null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsync($"{Negotiate}&dev_user=nobody%40ash.local", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await Scalar($"UPDATE users SET is_active = 0 WHERE id = {s.User("drlawyer").Id}");
        var inactive = (string)(await Scalar($"SELECT email FROM users WHERE id = {s.User("drlawyer").Id}"))!;
        (await client.PostAsync($"{Negotiate}&dev_user={Uri.EscapeDataString(inactive)}", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var ok = await client.PostAsync($"{Negotiate}&dev_user=avokat1%40ash.local", null, Ct);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(ok)).GetProperty("connectionToken").GetString().ShouldNotBeNullOrEmpty();
        // the header works too (non-browser clients)
        using var header = factory.CreateClient().AsUser("avokat1@ash.local");
        (await header.PostAsync(Negotiate, null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // anywhere else the query-string user is ignored
        (await client.GetAsync("/api/notifications/unread-count?dev_user=avokat1%40ash.local", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Hub_credentials_in_the_query_string_never_reach_the_logs()
    {
        const string token = "SECRET-TOKEN-040-xyz";
        var sink = new CapturingSink();
        await using var factory = Factory(more: services => services.AddSingleton<ILogEventSink>(sink));
        using var client = factory.CreateClient();

        (await client.PostAsync($"{Negotiate}&dev_user=avokat1%40ash.local&access_token={token}", null, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsync($"{Negotiate}&dev_user=nobody%40ash.local&access_token={token}", null, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var header = factory.CreateClient().AsUser("avokat1@ash.local");
        (await header.GetAsync($"/api/notifications?access_token={token}&dev_user=x%40y", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        // whatever else logs a full URL is masked too
        factory.Services.GetRequiredService<ILogger<NotificationTests>>()
            .LogWarning("Calling {Url}", $"http://localhost/hubs/notifications?id=1&access_token={token}&dev_user=avokat1%40ash.local");

        var events = sink.Events.ToList();
        events.ShouldContain(e => e.Properties.ContainsKey("RequestPath")
            && e.Properties["RequestPath"].ToString().Contains("/hubs/notifications/negotiate"), "the request log was captured");
        foreach (var e in events)
        {
            var text = e.RenderMessage() + " " + string.Join(" ", e.Properties.Values.Select(v => v.ToString())) + " " + e.Exception;
            text.ShouldNotContain(token);
            text.ShouldNotContain("dev_user=avokat1");
            text.ShouldNotContain("dev_user=nobody");
        }

        events.ShouldContain(e => e.RenderMessage().Contains($"access_token={LogRedaction.Mask}&dev_user={LogRedaction.Mask}"));
        LogRedaction.Redact("/hubs/x?access_token=abc&x=1&dev_user=a@b").ShouldBe("/hubs/x?access_token=***&x=1&dev_user=***");
    }
}
