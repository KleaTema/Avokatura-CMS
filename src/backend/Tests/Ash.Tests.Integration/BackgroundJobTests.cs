using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Infrastructure.Jobs;
using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Application.Digest;
using Ash.Modules.Notifications.Contracts;
using Ash.SharedKernel.Time;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 041 — the background jobs, run explicitly with a chosen "now" (Jobs:Enabled is false in AshApiFactory, so the
/// scheduler never runs on its own). Uses <see cref="CaseSeed"/>: the ACTIVE case of office TR (head: kryetar) with
/// avokat1 primary, avokat2 secondary, ndihmes assistant and jurist.
/// </summary>
public class BackgroundJobTests(MySqlFixture db) : IntegrationTest(db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Every wait in this class gives up after this long, so a failure fails fast instead of hanging.</summary>
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private static readonly DateTime Assigned = new(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);

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

    /// <summary>"email|TYPE" of every notification, sorted.</summary>
    private Task<List<string>> Notifications(string? type = null) => Strings($"""
        SELECT CONCAT(u.email, '|', n.notification_type) FROM notifications n JOIN users u ON u.id = n.user_id
        {(type is null ? string.Empty : $"WHERE n.notification_type = '{type}'")} ORDER BY 1
        """);

    private Task<List<string>> ActiveEmails(string role) => Strings($"""
        SELECT u.email FROM users u JOIN user_roles ur ON ur.user_id = u.id JOIN roles r ON r.id = ur.role_id
        WHERE r.code = '{role}' AND u.is_active = 1 ORDER BY u.email
        """);

    private static Task<JobOutcome> Run(AshApiFactory factory, string job, DateTime nowUtc) =>
        factory.Services.GetRequiredService<JobRunner>().RunAsync(job, nowUtc, Ct);

    private async Task<long> PrimaryAssignmentId(CaseSeed s) => Convert.ToInt64(await Scalar(
        $"SELECT id FROM case_assignments WHERE case_id = {s.ActiveCaseId} AND case_role = 'PRIMARY_LAWYER' AND ended_at IS NULL"));

    /// <summary>The ACTIVE case's current primary assignment was made at <paramref name="at"/>.</summary>
    private async Task AssignedAt(CaseSeed s, DateTime at) =>
        await Execute($"UPDATE case_assignments SET assigned_at = @at WHERE id = {await PrimaryAssignmentId(s)}", ("@at", at));

    private static DateTime Tirane(int y, int m, int d, int h, int min = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(y, m, d, h, min, 0, DateTimeKind.Unspecified), TiraneTime.Zone);

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    /// <summary>A job that holds its lock until the test opens the gate.</summary>
    private sealed class Gate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Runs;
    }

    private sealed class GateJob(Gate gate) : IScheduledJob
    {
        public string Name => "test-gate";
        public JobSchedule Schedule => JobSchedule.Hourly;

        public async Task RunAsync(DateTime nowUtc, CancellationToken ct)
        {
            Interlocked.Increment(ref gate.Runs);
            gate.Entered.TrySetResult();
            await gate.Release.Task.WaitAsync(WaitLimit, ct);
        }
    }

    private sealed class FailingJob : IScheduledJob
    {
        public string Name => "test-failing";
        public JobSchedule Schedule => JobSchedule.Hourly;
        public Task RunAsync(DateTime nowUtc, CancellationToken ct) => throw new InvalidOperationException("boom");
    }

    // ---------------------------------------------------------------- data reminders

    [Fact]
    public async Task Data_reminder_at_30_days_goes_once_to_the_primary_and_the_head_not_at_29()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await AssignedAt(s, Assigned);
        await using var factory = new AshApiFactory(Db);

        (await Run(factory, "data-reminders", Assigned.AddDays(30).AddMinutes(-1))).ShouldBe(JobOutcome.Succeeded);
        (await Notifications()).ShouldBeEmpty();

        (await Run(factory, "data-reminders", Assigned.AddDays(30))).ShouldBe(JobOutcome.Succeeded);
        (await Run(factory, "data-reminders", Assigned.AddDays(30).AddHours(1))).ShouldBe(JobOutcome.Succeeded);

        (await Notifications()).ShouldBe(["avokat1@ash.local|DATA_MISSING_30D", "kryetar@ash.local|DATA_MISSING_30D"]);
        (await Strings($"SELECT CONCAT(reminder_type, ':', assignment_id = {await PrimaryAssignmentId(s)}) FROM case_reminders"))
            .ShouldBe(["DATA_MISSING_30D:1"]);
        (await Scalar("SELECT body FROM notifications LIMIT 1")).ShouldBe(
            $"Kanë kaluar 30 ditë nga caktimi i avokatit në çështjen {await Scalar($"SELECT case_code FROM cases WHERE id = {s.ActiveCaseId}")} (Gjykata e Shkallës së Parë Tiranë) dhe nuk janë hedhur ende të dhëna.");
    }

    [Fact]
    public async Task After_6_months_the_monitors_are_told_too_and_a_job_down_past_both_sends_both_once()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await AssignedAt(s, Assigned);
        await using var factory = new AshApiFactory(Db);
        var now = Assigned.AddMonths(6);

        await Run(factory, "data-reminders", now);
        await Run(factory, "data-reminders", now.AddHours(1));

        (await Notifications(NotificationTypes.DataMissing30D)).ShouldBe(["avokat1@ash.local|DATA_MISSING_30D", "kryetar@ash.local|DATA_MISSING_30D"]);
        var sixMonths = new[] { "avokat1@ash.local", "kryetar@ash.local" }.Concat(await ActiveEmails("MONITOR"))
            .Select(e => $"{e}|DATA_MISSING_6M").OrderBy(x => x, StringComparer.Ordinal).ToList();
        (await Notifications(NotificationTypes.DataMissing6M)).OrderBy(x => x, StringComparer.Ordinal).ShouldBe(sixMonths);
        sixMonths.ShouldContain("monitor@ash.local|DATA_MISSING_6M");
        (await Scalar("SELECT COUNT(*) FROM case_reminders")).ShouldBe(2L);
    }

    [Fact]
    public async Task No_data_reminder_after_data_entry_or_for_closed_or_archived_cases()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await AssignedAt(s, Assigned);
        await using var factory = new AshApiFactory(Db);
        var now = Assigned.AddDays(31);

        await Execute($"UPDATE cases SET first_data_entry_at = @at WHERE id = {s.ActiveCaseId}", ("@at", Assigned.AddDays(29)));
        await Run(factory, "data-reminders", now);
        (await Notifications()).ShouldBeEmpty();

        await Execute($"UPDATE cases SET first_data_entry_at = NULL WHERE id = {s.ActiveCaseId}");
        await Execute($"UPDATE dossiers SET archived_at = @at, archived_by = {s.User("avokat1").Id} WHERE id = {s.ActiveDossierId}", ("@at", now));
        await Run(factory, "data-reminders", now);
        (await Notifications()).ShouldBeEmpty();

        await Execute($"UPDATE dossiers SET archived_at = NULL, archived_by = NULL WHERE id = {s.ActiveDossierId}");
        await Execute($"UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = @at, closed_by = {s.User("avokat1").Id} WHERE id = {s.ActiveCaseId}", ("@at", now));
        await Run(factory, "data-reminders", now);
        (await Notifications()).ShouldBeEmpty();
        (await Scalar("SELECT COUNT(*) FROM case_reminders")).ShouldBe(0L);
    }

    [Fact]
    public async Task A_transfer_on_day_20_restarts_the_30_days_from_the_new_assignment()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await AssignedAt(s, Assigned);
        var transfer = Assigned.AddDays(20);
        var kryetar = s.User("kryetar").Id;
        await Execute($"""
            UPDATE case_assignments SET ended_at = @at, ended_by = {kryetar}, end_reason = 'TRANSFERRED'
            WHERE case_id = {s.ActiveCaseId} AND case_role = 'PRIMARY_LAWYER' AND ended_at IS NULL
            """, ("@at", transfer));
        await Execute($"""
            INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by, assigned_at)
            VALUES ({s.ActiveCaseId}, {kryetar}, 'PRIMARY_LAWYER', {kryetar}, @at)
            """, ("@at", transfer));
        await using var factory = new AshApiFactory(Db);

        await Run(factory, "data-reminders", Assigned.AddDays(31)); // 30 days after the FIRST assignment
        (await Notifications()).ShouldBeEmpty();

        await Run(factory, "data-reminders", transfer.AddDays(30));
        // the new primary is also the office head: one notification
        (await Notifications()).ShouldBe(["kryetar@ash.local|DATA_MISSING_30D"]);
    }

    [Fact]
    public async Task Two_instances_at_once_send_one_set_and_the_named_lock_lets_only_one_run()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await AssignedAt(s, Assigned);
        var gate = new Gate();
        await using var factory = new AshApiFactory(Db, configureServices: services =>
        {
            services.AddSingleton(gate);
            services.AddScheduledJob<GateJob>();
        });
        // two "instances": two runners with their own DI scopes and connections
        var runner = factory.Services.GetRequiredService<JobRunner>();
        var outcomes = await Task.WhenAll(
            runner.RunAsync("data-reminders", Assigned.AddDays(30), Ct),
            runner.RunAsync("data-reminders", Assigned.AddDays(30), Ct)).WaitAsync(WaitLimit, Ct);
        outcomes.ShouldAllBe(o => o == JobOutcome.Succeeded || o == JobOutcome.Skipped);
        (await Notifications()).ShouldBe(["avokat1@ash.local|DATA_MISSING_30D", "kryetar@ash.local|DATA_MISSING_30D"]);

        // while one run holds job:test-gate, the other one skips
        var first = runner.RunAsync("test-gate", Assigned, Ct);
        await gate.Entered.Task.WaitAsync(WaitLimit, Ct);
        (await runner.RunAsync("test-gate", Assigned, Ct).WaitAsync(WaitLimit, Ct)).ShouldBe(JobOutcome.Skipped);
        gate.Release.SetResult();
        (await first.WaitAsync(WaitLimit, Ct)).ShouldBe(JobOutcome.Succeeded);
        gate.Runs.ShouldBe(1);
        // released: the next run takes the lock again (the gate stays open)
        (await runner.RunAsync("test-gate", Assigned, Ct).WaitAsync(WaitLimit, Ct)).ShouldBe(JobOutcome.Succeeded);
        gate.Runs.ShouldBe(2);
    }

    // ---------------------------------------------------------------- hearing reminders

    private async Task<long> HearingAsync(CaseSeed s, DateTime atUtc) =>
        await InsertAsync($"""
            INSERT INTO case_hearings (case_id, hearing_type_id, scheduled_at, status, created_by)
            VALUES ({s.ActiveCaseId}, (SELECT MIN(id) FROM hearing_types), @at, 'SCHEDULED', {s.User("avokat1").Id})
            """, ("@at", atUtc));

    private async Task<long> InsertAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(Ct);
        return command.LastInsertedId;
    }

    [Fact]
    public async Task Hearing_tomorrow_goes_once_to_the_team_from_08_00_and_a_reschedule_is_a_new_reminder()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var hearing = await HearingAsync(s, Tirane(2026, 11, 11, 10));
        await using var factory = new AshApiFactory(Db);
        var team = new[] { "avokat1@ash.local", "avokat2@ash.local", "jurist@ash.local", "ndihmes@ash.local" }
            .Select(e => $"{e}|HEARING_TOMORROW").ToList();

        await Run(factory, "hearing-reminders", Tirane(2026, 11, 10, 7, 59)); // before 08:00
        (await Notifications()).ShouldBeEmpty();

        await Run(factory, "hearing-reminders", Tirane(2026, 11, 10, 8, 0));
        await Run(factory, "hearing-reminders", Tirane(2026, 11, 10, 9, 0));
        (await Notifications()).ShouldBe(team);
        (await Scalar("SELECT body FROM notifications LIMIT 1")).ShouldBe(
            $"Çështja {await Scalar($"SELECT case_code FROM cases WHERE id = {s.ActiveCaseId}")} (Gjykata e Shkallës së Parë Tiranë) ka seancë nesër, më 11.11.2026 10:00.");

        // moved to the 13th: the day before, a new reminder (new date, new key)
        await Execute($"UPDATE case_hearings SET scheduled_at = @at WHERE id = {hearing}", ("@at", Tirane(2026, 11, 13, 9, 30)));
        await Execute("DELETE FROM notifications");
        await Run(factory, "hearing-reminders", Tirane(2026, 11, 12, 8, 5));
        await Run(factory, "hearing-reminders", Tirane(2026, 11, 12, 10, 5));
        (await Notifications()).ShouldBe(team);
        (await Strings("SELECT dedupe_key FROM sent_reminders ORDER BY id")).ShouldBe(
            [$"hearing:{hearing}:2026-11-11:tomorrow", $"hearing:{hearing}:2026-11-13:tomorrow"]);

        // cancelled: nothing
        await Execute($"UPDATE case_hearings SET status = 'CANCELLED', scheduled_at = @at WHERE id = {hearing}", ("@at", Tirane(2026, 11, 20, 9)));
        await Execute("DELETE FROM notifications");
        await Run(factory, "hearing-reminders", Tirane(2026, 11, 19, 8, 5));
        (await Notifications()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_pending_next_level_summons_is_reminded_once_3_days_before_its_hearing_while_the_level_is_open()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var hearingAt = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
        var details = JsonSerializer.Serialize(new { @new = new { summonsNumber = "FL-77/26", hearingAt } },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var notice = await InsertAsync($"""
            INSERT INTO case_history_events (dossier_id, case_id, event_type, actor_user_id, occurred_at, summary, details)
            VALUES ({s.ActiveDossierId}, {s.ActiveCaseId}, 'NEXT_LEVEL_SUMMONS_PENDING', {s.User("protokoll").Id}, @occurred, 'Erdhi fletëthirrja', @details)
            """, ("@occurred", hearingAt.AddDays(-10)), ("@details", details));
        await using var factory = new AshApiFactory(Db);

        await Run(factory, "hearing-reminders", hearingAt.AddDays(-3).AddMinutes(-1));
        (await Notifications()).ShouldBeEmpty();

        await Run(factory, "hearing-reminders", hearingAt.AddDays(-3));
        await Run(factory, "hearing-reminders", hearingAt.AddDays(-2));
        (await Notifications()).ShouldBe(["avokat1@ash.local|NEXT_LEVEL_PENDING_3D", "kryetar@ash.local|NEXT_LEVEL_PENDING_3D"]);
        (await Strings("SELECT dedupe_key FROM sent_reminders")).ShouldBe([$"pending:{notice}:3d"]);
        (await Scalar("SELECT link_path FROM notifications LIMIT 1")).ShouldBe(
            $"/cases/{Ash.SharedKernel.Ids.PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM cases WHERE id = {s.ActiveCaseId}"))!)}/vendimi");

        // closed in the meantime: no reminder for another notice
        await Execute("DELETE FROM sent_reminders");
        await Execute("DELETE FROM notifications");
        await Execute($"UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = @at, closed_by = {s.User("avokat1").Id} WHERE id = {s.ActiveCaseId}", ("@at", hearingAt.AddDays(-5)));
        await Run(factory, "hearing-reminders", hearingAt.AddDays(-2));
        (await Notifications()).ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- cleanup

    [Fact]
    public async Task Cleanup_deletes_only_read_notifications_older_than_12_months_in_batches()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var now = new DateTime(2026, 11, 4, 1, 0, 0, DateTimeKind.Utc);
        var user = s.User("avokat1").Id;
        // 5,003 read and old (more than one batch of 5,000), one unread and old, one read and recent
        await Execute($"""
            SET SESSION cte_max_recursion_depth = 10000;
            INSERT INTO notifications (user_id, notification_type, title, created_at, read_at)
            WITH RECURSIVE seq (n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 5003)
            SELECT {user}, 'CASE_CLOSED', CONCAT('old ', n), @old, @old FROM seq
            """, ("@old", now.AddMonths(-13)));
        await Execute($"INSERT INTO notifications (user_id, notification_type, title, created_at) VALUES ({user}, 'CASE_CLOSED', 'unread old', @at)",
            ("@at", now.AddMonths(-30)));
        await Execute($"INSERT INTO notifications (user_id, notification_type, title, created_at, read_at) VALUES ({user}, 'CASE_CLOSED', 'read recent', @at, @at)",
            ("@at", now.AddMonths(-11)));
        await using var factory = new AshApiFactory(Db);

        (await Run(factory, "notification-cleanup", now)).ShouldBe(JobOutcome.Succeeded);

        (await Strings("SELECT title FROM notifications ORDER BY title")).ShouldBe(["read recent", "unread old"]);
    }

    // ---------------------------------------------------------------- audit digest

    private async Task AuditRowAsync(DateTime occurredUtc, string action) =>
        await Execute("""
            INSERT INTO ash_audit.audit_log (occurred_at, user_id, user_display, action, outcome, entity_type, entity_id, ip_address, correlation_id, details)
            VALUES (@at, 7, 'Përdorues\tme tab', @action, 'SUCCESS', 'CASE', 42, INET6_ATON('10.0.0.5'), UUID_TO_BIN(UUID()), JSON_OBJECT('q', 'ë'))
            """, ("@at", occurredUtc), ("@action", action));

    private async Task<List<string>> DigestDays() =>
        await Strings("SELECT CONCAT(DATE_FORMAT(audit_day, '%Y-%m-%d'), ':', row_count) FROM ash_audit.audit_daily_digests ORDER BY audit_day");

    [Fact]
    public async Task Digest_chains_every_missed_day_recomputes_equal_and_a_tampered_row_breaks_its_day()
    {
        // three Tirane days; 23:30 UTC on the 1st is already the 2nd in Tirane
        await AuditRowAsync(Tirane(2026, 10, 1, 9), "LOGIN");
        await AuditRowAsync(Tirane(2026, 10, 1, 18), "CASE_VIEW");
        await AuditRowAsync(new DateTime(2026, 10, 1, 23, 30, 0, DateTimeKind.Utc), "CASE_UPDATE");
        await AuditRowAsync(Tirane(2026, 10, 3, 12), "DOCUMENT_DOWNLOAD");
        await using var factory = new AshApiFactory(Db);

        (await Run(factory, "audit-digest", Tirane(2026, 10, 2, 2))).ShouldBe(JobOutcome.Succeeded);
        (await DigestDays()).ShouldBe(["2026-10-01:2"]);

        // down for two days: both missed days, in order, and a day without rows too
        (await Run(factory, "audit-digest", Tirane(2026, 10, 5, 2))).ShouldBe(JobOutcome.Succeeded);
        (await DigestDays()).ShouldBe(["2026-10-01:2", "2026-10-02:1", "2026-10-03:1", "2026-10-04:0"]);
        (await Run(factory, "audit-digest", Tirane(2026, 10, 5, 9))).ShouldBe(JobOutcome.Succeeded);
        (await DigestDays()).Count.ShouldBe(4);

        // chained: the 2nd's digest is SHA-256(1st's digest ‖ its rows), recomputed the same
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var verify = scope.ServiceProvider.GetRequiredService<VerifyAuditChain>();
            var ok = (await verify.VerifyAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 4), Ct)).Value;
            ok.Ok.ShouldBeTrue();
            ok.CheckedDays.ShouldBe(4);

            await Execute("UPDATE ash_audit.audit_log SET action = 'TAMPERED' WHERE action = 'CASE_UPDATE'");
            var broken = (await verify.VerifyAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 4), Ct)).Value;
            broken.Ok.ShouldBeFalse();
            broken.FirstBrokenDay.ShouldBe(new DateOnly(2026, 10, 2));
            // the days after it still check against their stored predecessor
            (await verify.VerifyAsync(new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 4), Ct)).Value.Ok.ShouldBeTrue();
        }

        // the endpoint: administrators only
        using var admin = factory.CreateClient().AsUser("admin@ash.local");
        var response = await admin.GetAsync("/api/admin/audit/verify?from=2026-10-01&to=2026-10-04", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("ok").GetBoolean().ShouldBeFalse();
        body.GetProperty("firstBrokenDay").GetString().ShouldBe("2026-10-02");
        (await admin.GetAsync("/api/admin/audit/verify?from=2026-10-04&to=2026-10-01", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.GetAsync("/api/admin/audit/verify?from=x&to=2026-10-01", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");
        (await lawyer.GetAsync("/api/admin/audit/verify?from=2026-10-01&to=2026-10-04", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- partition check

    [Fact]
    public async Task Partition_check_alerts_the_admins_once_a_day_when_p_max_has_rows_or_next_month_is_missing()
    {
        await using var factory = new AshApiFactory(Db);
        var admins = (await ActiveEmails("ADMIN")).Select(e => $"{e}|PARTITION_ALERT").ToList();
        admins.ShouldNotBeEmpty();
        // the container created this month's partitions and the next three at install (script 006)
        var now = DateTime.UtcNow;

        (await Run(factory, "partition-check", now)).ShouldBe(JobOutcome.Succeeded);
        (await Notifications()).ShouldBeEmpty();

        await AuditRowAsync(new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc), "LOGIN"); // lands in p_max
        await Run(factory, "partition-check", now);
        await Run(factory, "partition-check", now.AddMinutes(30));
        (await Notifications()).ShouldBe(admins);
        ((string)(await Scalar("SELECT body FROM notifications LIMIT 1"))!).ShouldContain("ash_audit.audit_log: particioni p_max ka rreshta");

        // far ahead: next month has no partition; a new day, a new alert
        await Execute("DELETE FROM notifications");
        await Execute("DELETE FROM ash_audit.audit_log");
        await Run(factory, "partition-check", now.AddMonths(8));
        (await Notifications()).ShouldBe(admins);
        ((string)(await Scalar("SELECT body FROM notifications LIMIT 1"))!).ShouldContain("mungon particioni i muajit të ardhshëm");
    }

    // ---------------------------------------------------------------- runner, health, endpoint

    [Fact]
    public async Task The_runner_records_success_and_failure_and_the_health_check_turns_unhealthy_when_stale()
    {
        await using var factory = new AshApiFactory(Db, configureServices: services => services.AddScheduledJob<FailingJob>());
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

        (await Run(factory, "partition-check", now)).ShouldBe(JobOutcome.Succeeded);
        (await Run(factory, "test-failing", now)).ShouldBe(JobOutcome.Failed);
        (await Strings("SELECT CONCAT(job_name, '|', last_succeeded_at IS NOT NULL, '|', IFNULL(last_error, '-')) FROM ash_ops.job_runs ORDER BY job_name"))
            .ShouldBe(["partition-check|1|-", "test-failing|0|InvalidOperationException: boom"]);

        // every job succeeded just now, except the failing one: healthy only without it
        var persistence = factory.Services.GetRequiredService<PersistenceOptions>();
        var scopes = factory.Services.GetRequiredService<IServiceScopeFactory>();
        string[] jobs;
        await using (var scope = scopes.CreateAsyncScope())
        {
            jobs = scope.ServiceProvider.GetServices<IScheduledJob>().Select(j => j.Name).ToArray();
        }

        foreach (var job in jobs)
        {
            await Execute("""
                INSERT INTO ash_ops.job_runs (job_name, last_started_at, last_succeeded_at) VALUES (@n, @at, @at)
                ON DUPLICATE KEY UPDATE last_succeeded_at = @at, last_error = NULL
                """, ("@n", job), ("@at", now));
        }

        StaleCheck Check(DateTime at) => new(scopes, persistence, at);
        (await Check(now.AddHours(2)).Run()).Status.ShouldBe(HealthStatus.Healthy);
        // an hourly job: 2 × 1 h + 15 min
        var stale = await Check(now.AddHours(2).AddMinutes(16)).Run();
        stale.Status.ShouldBe(HealthStatus.Unhealthy);
        stale.Description!.ShouldContain("data-reminders");
        stale.Description!.ShouldNotContain("audit-digest"); // daily: 48 h 15 min
        (await Check(now.AddHours(49)).Run()).Description!.ShouldContain("audit-digest");

        // disabled jobs: nothing is expected to run
        var disabled = new JobsHealthCheck(scopes, new JobOptions { Enabled = false }, new JobSchedulerState(), persistence, new FixedClock(now.AddDays(9)));
        (await disabled.CheckHealthAsync(new HealthCheckContext(), Ct)).Status.ShouldBe(HealthStatus.Healthy);
    }

    /// <summary>The "jobs" health check as if the scheduler were on, at <paramref name="now"/>.</summary>
    private sealed class StaleCheck(IServiceScopeFactory scopes, PersistenceOptions persistence, DateTime now)
    {
        public Task<HealthCheckResult> Run() =>
            new JobsHealthCheck(scopes, new JobOptions { Enabled = true }, new JobSchedulerState { StartedAtUtc = now }, persistence, new FixedClock(now))
                .CheckHealthAsync(new HealthCheckContext(), Ct);
    }

    [Fact]
    public async Task Development_endpoint_runs_a_job_for_administrators_only()
    {
        await using var factory = new AshApiFactory(Db);
        using var admin = factory.CreateClient().AsUser("admin@ash.local");
        using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");

        var response = await admin.PostAsync("/api/admin/jobs/partition-check/run", null, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("outcome").GetString().ShouldBe("Succeeded");
        (await admin.PostAsync("/api/admin/jobs/nope/run", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await lawyer.PostAsync("/api/admin/jobs/partition-check/run", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await using var production = new AshApiFactory(Db, environment: "Production");
        using var anyone = production.CreateClient();
        (await anyone.PostAsync("/api/admin/jobs/partition-check/run", null, Ct)).StatusCode
            .ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized);
    }
}
