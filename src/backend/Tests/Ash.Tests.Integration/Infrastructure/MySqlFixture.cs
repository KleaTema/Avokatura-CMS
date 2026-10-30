using Ash.Database;
using MySql.Data.MySqlClient;
using Testcontainers.MySql;

namespace Ash.Tests.Integration.Infrastructure;

/// <summary>
/// One MySQL container per test run, schema applied once by the real migrator (Scripts + LocalScripts).
/// Shared by every integration test class through <see cref="IntegrationCollection"/>.
/// </summary>
public sealed class MySqlFixture : IAsyncLifetime
{
    private const string RootPassword = "ash_test_root";

    // Business tables, emptied between tests (children before parents). Lookup and seed tables (users, roles, offices, courts, lists...) are kept.
    private static readonly string[] BusinessTables =
    [
        "ash_core.case_reminders",
        "ash_core.notifications",
        "ash_core.case_history_events",
        "ash_core.documents",
        "ash_core.case_decisions",
        "ash_core.case_security_measures",
        "ash_core.case_hearings",
        "ash_core.case_parties",
        "ash_core.case_institutions",
        "ash_core.case_assignments",
        "ash_core.cases",
        "ash_core.dossiers",
        "ash_chat.message_attachments",
        "ash_chat.messages",
        "ash_chat.conversation_participants",
        "ash_chat.conversations",
        "ash_audit.audit_daily_digests",
        "ash_audit.audit_log",
    ];

    // Append-only tables whose triggers block DELETE.
    private static readonly HashSet<string> TruncateOnly = ["ash_core.case_history_events"];

    private readonly MySqlContainer _container = new MySqlBuilder("mysql:8.0")
        .WithUsername("root")
        .WithPassword(RootPassword)
        .WithCommand(
            "--default-time-zone=+00:00",
            "--event-scheduler=ON",
            "--character-set-server=utf8mb4",
            "--collation-server=utf8mb4_0900_ai_ci")
        .Build();

    /// <summary>Application connection (ash_dev, ash_core).</summary>
    public string AppConnectionString { get; private set; } = string.Empty;

    /// <summary>Insert/select-only audit account (ash_audit_local).</summary>
    public string AuditConnectionString { get; private set; } = string.Empty;

    private string RootConnectionString { get; set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await Timing.MeasureAsync("container start", async () =>
        {
            await _container.StartAsync();
            return 0;
        });

        var host = _container.Hostname;
        var port = _container.GetMappedPublicPort(3306);
        RootConnectionString = $"Server={host};Port={port};User=root;Password={RootPassword};AllowUserVariables=true;";
        AppConnectionString = $"Server={host};Port={port};User=ash_dev;Password=ash_dev_local;Database=ash_core;AllowUserVariables=true;";
        AuditConnectionString = $"Server={host};Port={port};User=ash_audit_local;Password=ash_audit_local_pw;";

        var result = Timing.Measure("migration", () => DatabaseUpgrader.Upgrade(RootConnectionString, local: true, quiet: true));
        if (!result.Successful)
        {
            throw new InvalidOperationException(result.Error);
        }
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Empties the business tables; users, roles, offices, institutions, courts and lists stay.</summary>
    public async Task ResetAsync()
    {
        await Timing.MeasureAsync("database reset", async () =>
        {
            await using var connection = new MySqlConnection(RootConnectionString);
            await connection.OpenAsync();

            // Only touch tables that actually have rows. DELETE, not TRUNCATE: TRUNCATE is DDL (drops and recreates the
            // tablespace, ~200 ms per table, more with FULLTEXT) and was two thirds of the suite's time; a test leaves
            // few rows, so DELETE takes milliseconds. Every table here has a BIGINT AUTO_INCREMENT key or none, so not
            // resetting the counters is harmless (no test may depend on id values). case_history_events keeps
            // TRUNCATE: its append-only trigger blocks DELETE (TRUNCATE fires no triggers).
            var dirty = new List<string>();
            await using (var probe = new MySqlCommand(ProbeSql, connection))
            await using (var reader = await probe.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    dirty.Add(reader.GetString(0));
                }
            }

            if (dirty.Count > 0)
            {
                var sql = "SET FOREIGN_KEY_CHECKS=0; "
                    + string.Join(" ", dirty.Select(t => TruncateOnly.Contains(t) ? $"TRUNCATE TABLE {t};" : $"DELETE FROM {t};"))
                    + " SET FOREIGN_KEY_CHECKS=1;";
                await ExecuteAsync(connection, sql);
            }

            return 0;
        });
    }

    private static readonly string ProbeSql = string.Join(
        " UNION ALL ",
        BusinessTables.Select(t => $"SELECT '{t}' AS table_name WHERE EXISTS (SELECT 1 FROM {t})"));

    private static async Task ExecuteAsync(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<MySqlFixture>
{
    public const string Name = "Integration";
}

/// <summary>Base for every integration test class: resets the business tables before each test.</summary>
[Collection(IntegrationCollection.Name)]
public abstract class IntegrationTest(MySqlFixture db) : IAsyncLifetime
{
    protected MySqlFixture Db { get; } = db;

    public virtual async ValueTask InitializeAsync() => await Db.ResetAsync();

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
