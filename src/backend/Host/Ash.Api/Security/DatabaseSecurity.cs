using MySql.Data.MySqlClient;

namespace Ash.Api.Security;

/// <summary>
/// Task 051, item 10: outside Development every database connection is encrypted — each ConnectionStrings:* entry must
/// say SslMode=Required (or VerifyCA / VerifyFull); otherwise the app refuses to start. (The accounts themselves are
/// created REQUIRE SSL by script 001; this catches a configuration that would fail at the first query instead.)
/// </summary>
public static class DatabaseSecurity
{
    private static readonly MySqlSslMode[] Encrypted = [MySqlSslMode.Required, MySqlSslMode.VerifyCA, MySqlSslMode.VerifyFull];

    public static void AssertSsl(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        var plain = new List<string>();
        foreach (var entry in configuration.GetSection("ConnectionStrings").GetChildren())
        {
            if (string.IsNullOrWhiteSpace(entry.Value))
            {
                continue;
            }

            MySqlSslMode mode;
            try
            {
                mode = new MySqlConnectionStringBuilder(entry.Value).SslMode;
            }
            catch (ArgumentException)
            {
                plain.Add($"{entry.Key} (unreadable)");
                continue;
            }

            if (!Encrypted.Contains(mode))
            {
                plain.Add(entry.Key);
            }
        }

        if (plain.Count > 0)
        {
            // the names only — never the connection strings themselves
            throw new InvalidOperationException(
                $"Database connections must use SslMode=Required (or VerifyCA / VerifyFull) outside Development: {string.Join(", ", plain)}.");
        }
    }
}
