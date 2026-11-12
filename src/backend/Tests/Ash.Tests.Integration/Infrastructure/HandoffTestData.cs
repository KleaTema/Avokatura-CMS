using MySql.Data.MySqlClient;

namespace Ash.Tests.Integration.Infrastructure;

/// <summary>
/// Task 053c: closing a level that is not final needs the handoff destination. Tests that close such a level map
/// "Gjykata e Apelit Tiranë" to the TR office (office_courts, emptied between tests) and send level + court.
/// </summary>
public static class HandoffTestData
{
    public const string AppealCourt = "Gjykata e Apelit Tiranë";
    public const string AppealLevel = "APPEAL";

    /// <summary>Maps the appeal court to the office with <paramref name="officeCode"/> (default TR); returns the court id.</summary>
    public static async Task<int> MapAppealCourtAsync(string connectionString, string officeCode = "TR")
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand("""
            INSERT INTO office_courts (office_id, court_id, is_active, updated_by)
            SELECT o.id, c.id, 1, (SELECT MIN(id) FROM users)
              FROM offices o JOIN courts c ON c.name = @court
             WHERE o.code = @office
            ON DUPLICATE KEY UPDATE is_active = 1;
            SELECT id FROM courts WHERE name = @court;
            """, connection);
        command.Parameters.AddWithValue("@court", AppealCourt);
        command.Parameters.AddWithValue("@office", officeCode);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    /// <summary>The handoff fields of a non-final closing, to the mapped appeal court.</summary>
    public static (string Key, object? Value)[] Fields(int appealCourtId) =>
        [("nextCourtLevelCode", AppealLevel), ("nextCourtId", appealCourtId)];
}
