using MySql.Data.MySqlClient;

namespace Ash.Tests.Integration.Infrastructure;

/// <summary>
/// Task 053j: a registration goes to the office that covers the summons court (office_courts, emptied between tests).
/// Tests that register through the API map their court to the registrar's office first (default TR).
/// </summary>
public static class OfficeCourtsTestData
{
    public const string FirstInstanceTirana = "Gjykata e Shkallës së Parë Tiranë";
    public const string AppealTirana = "Gjykata e Apelit Tiranë";

    /// <summary>Maps <paramref name="court"/> to the office with <paramref name="officeCode"/>; returns the court id.</summary>
    public static async Task<int> MapAsync(string connectionString, string court, string officeCode = "TR")
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand("""
            UPDATE office_courts SET is_active = 0 WHERE court_id = (SELECT id FROM courts WHERE name = @court);
            INSERT INTO office_courts (office_id, court_id, is_active, updated_by)
            SELECT o.id, c.id, 1, (SELECT MIN(id) FROM users)
              FROM offices o JOIN courts c ON c.name = @court
             WHERE o.code = @office
            ON DUPLICATE KEY UPDATE is_active = 1;
            SELECT id FROM courts WHERE name = @court;
            """, connection);
        command.Parameters.AddWithValue("@court", court);
        command.Parameters.AddWithValue("@office", officeCode);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    /// <summary>The usual test courts (first instance and appeal, Tirana) to TR.</summary>
    public static async Task MapTiranaAsync(string connectionString)
    {
        await MapAsync(connectionString, FirstInstanceTirana);
        await MapAsync(connectionString, AppealTirana);
    }
}
