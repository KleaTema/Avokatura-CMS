using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 051, item 10 — least privilege of the production database accounts, read with SHOW GRANTS from the test
/// MySQL (the migrator runs the same scripts there), and the SSL rule for connection strings.
/// </summary>
public class DatabasePrivilegeTests(MySqlFixture db) : IntegrationTest(db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<List<string>> GrantsAsync(string account)
    {
        await using var connection = new MySqlConnection(Db.RootConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand($"SHOW GRANTS FOR {account}", connection);
        var grants = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            grants.Add(reader.GetString(0));
        }

        return grants;
    }

    /// <summary>The privileges of one GRANT line ("GRANT SELECT, INSERT ON `x`.* TO …" → SELECT, INSERT).</summary>
    private static IEnumerable<string> Privileges(string grant) =>
        grant["GRANT ".Length..grant.IndexOf(" ON ", StringComparison.Ordinal)]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('(')[0].Trim());

    private static readonly string[] Dangerous =
        ["ALL PRIVILEGES", "DROP", "ALTER", "CREATE", "GRANT OPTION", "SUPER", "FILE", "PROCESS", "SHUTDOWN", "CREATE USER", "RELOAD", "INDEX", "REFERENCES", "TRIGGER", "EVENT"];

    [Theory]
    [InlineData("'ash_app'@'10.%'")]
    [InlineData("'ash_chat_app'@'10.%'")]
    [InlineData("'ash_reporting'@'10.%'")]
    [InlineData("'ash_audit_writer'@'10.%'")]
    [InlineData("'ash_audit_digest'@'10.%'")]
    public async Task An_application_account_cannot_change_the_schema_or_hand_out_rights(string account)
    {
        var grants = await GrantsAsync(account);

        grants.ShouldNotBeEmpty();
        foreach (var grant in grants)
        {
            grant.ShouldNotContain("WITH GRANT OPTION", Case.Insensitive, grant);
            // USAGE ON *.* = "may log in, no rights": every account has it
            if (grant.StartsWith("GRANT USAGE ON *.*", StringComparison.Ordinal))
            {
                continue;
            }

            grant.ShouldNotContain(" ON *.*", Case.Insensitive, grant); // nothing server-wide
            Privileges(grant).Intersect(Dangerous).ShouldBeEmpty(grant);
        }
    }

    [Fact]
    public async Task The_reporting_account_only_reads()
    {
        foreach (var grant in (await GrantsAsync("'ash_reporting'@'10.%'")).Where(g => !g.StartsWith("GRANT USAGE", StringComparison.Ordinal)))
        {
            Privileges(grant).ToList().ShouldBe(["SELECT"], customMessage: grant);
        }
    }

    [Fact]
    public async Task The_audit_accounts_can_never_update_or_delete_the_audit_log()
    {
        foreach (var account in new[] { "'ash_audit_writer'@'10.%'", "'ash_audit_digest'@'10.%'", "'ash_app'@'10.%'", "'ash_chat_app'@'10.%'", "'ash_reporting'@'10.%'" })
        {
            foreach (var grant in (await GrantsAsync(account)).Where(g => g.Contains(" ON `ash_audit`", StringComparison.Ordinal)))
            {
                Privileges(grant).ShouldNotContain("UPDATE", grant);
                Privileges(grant).ShouldNotContain("DELETE", grant);
            }
        }

        // the writer: insert and read (the digest chain reads it back), nothing else
        foreach (var grant in (await GrantsAsync("'ash_audit_writer'@'10.%'")).Where(g => g.Contains(" ON `ash_audit`", StringComparison.Ordinal)))
        {
            Privileges(grant).Order().ToList().ShouldBe(["INSERT", "SELECT"], customMessage: grant);
        }
    }

    [Fact]
    public async Task The_migrator_has_its_four_schemas_only_and_cannot_grant()
    {
        var grants = await GrantsAsync("'ash_migrator'@'10.%'");
        foreach (var grant in grants)
        {
            grant.ShouldNotContain("WITH GRANT OPTION", Case.Insensitive, grant);
            if (!grant.StartsWith("GRANT USAGE ON *.*", StringComparison.Ordinal))
            {
                grant.ShouldMatch(@" ON `ash_(core|chat|audit|ops)`\.\* TO ", grant);
            }
        }
    }

    [Fact]
    public async Task Every_production_account_requires_ssl()
    {
        await using var connection = new MySqlConnection(Db.RootConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(
            "SELECT CONCAT(user, '@', host), ssl_type FROM mysql.user WHERE user IN ('ash_app','ash_chat_app','ash_reporting','ash_audit_writer','ash_audit_digest','ash_migrator')",
            connection);
        var rows = new List<(string Account, string Ssl)>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        rows.Count.ShouldBe(6);
        rows.ShouldAllBe(r => r.Ssl != string.Empty);
    }

    [Fact]
    public void Outside_Development_a_connection_string_without_SslMode_Required_stops_the_startup()
    {
        try
        {
            using var factory = new AshApiFactory(Db, environment: "Production", environmentVariables: new Dictionary<string, string?>
            {
                ["ConnectionStrings__Reporting"] = new MySqlConnectionStringBuilder(Db.AppConnectionString) { SslMode = MySqlSslMode.Disabled }.ConnectionString,
            });

            var error = Should.Throw<InvalidOperationException>(() => factory.CreateClient());
            error.Message.ShouldContain("Reporting");
            error.Message.ShouldNotContain("Password"); // the names, never the strings
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__Reporting", null);
        }
    }
}
