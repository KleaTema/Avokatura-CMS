using MySql.Data.MySqlClient;

namespace Ash.Tests.Integration.Infrastructure;

/// <summary>
/// Reusable scenario data for case access tests (raw SQL, unique codes, removed again on dispose).
/// Needs the test container migrated with local=true (users admin/protokoll/kryetar/avokat1/avokat2/ndihmes/monitor/institucion/jurist).
/// Creates: dossier + ACTIVE case (office TR) with a full team and a MONITORING institution link, a REGISTERED case
/// registered by protokoll, office DR with its own lawyer, and a second jurist of the same ministry who is NOT assigned.
/// </summary>
public sealed class CaseSeed : IAsyncDisposable
{
    private static readonly string[] SeededUserKeys =
        ["admin", "protokoll", "kryetar", "avokat1", "avokat2", "ndihmes", "monitor", "institucion", "jurist"];

    private readonly MySqlConnection _connection;
    private readonly Dictionary<string, SeedUser> _users = new();
    private readonly List<(string Table, long Id)> _created = [];
    private readonly List<(string Sql, long Id)> _cleanup = [];

    private CaseSeed(MySqlConnection connection) => _connection = connection;

    public long OfficeTrId { get; private set; }
    public long OfficeDrId { get; private set; }
    public long ActiveCaseId { get; private set; }
    public long ActiveDossierId { get; private set; }
    public long RegisteredCaseId { get; private set; }

    /// <summary>Keys: admin, protokoll, kryetar, avokat1, avokat2, ndihmes, monitor, institucion, jurist, jurist2, drlawyer.</summary>
    public SeedUser User(string key) => _users[key];

    public static async Task<CaseSeed> CreateAsync(string connectionString)
    {
        var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        var seed = new CaseSeed(connection);
        try
        {
            await seed.InsertAsync();
        }
        catch
        {
            await seed.DisposeAsync();
            throw;
        }

        return seed;
    }

    private async Task InsertAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];

        foreach (var key in SeededUserKeys)
        {
            var id = await ScalarAsync("SELECT id FROM users WHERE email = @e", ("@e", $"{key}@ash.local"));
            _users[key] = await LoadUserAsync(id);
        }

        var trId = _users["kryetar"].OfficeId!.Value;
        var ministryId = _users["institucion"].InstitutionId!.Value;
        OfficeTrId = trId;

        OfficeDrId = await InsertAsync("offices", "INSERT INTO offices (code, name, office_type) VALUES (@c, @n, 'LOCAL')",
            ("@c", $"DR-{suffix}"), ("@n", $"Zyra Durrës test {suffix}"));

        var drLawyerId = await InsertUserAsync($"dr-lawyer-{suffix}", "Dr", "Lawyer", OfficeDrId, null, "STATE_LAWYER");
        var jurist2Id = await InsertUserAsync($"jurist2-{suffix}", "Jurist", "Two", null, ministryId, "INSTITUTION_JURIST");
        _users["drlawyer"] = await LoadUserAsync(drLawyerId);
        _users["jurist2"] = await LoadUserAsync(jurist2Id);

        var courtId = await ScalarAsync("SELECT id FROM courts WHERE name = @n", ("@n", "Gjykata e Shkallës së Parë Tiranë"));
        var levelId = await ScalarAsync("SELECT id FROM court_levels WHERE code = 'FIRST_INSTANCE'");
        var formatId = await ScalarAsync("SELECT id FROM dossier_code_formats WHERE is_active = 1");

        var protokollId = _users["protokoll"].Id;
        var kryetarId = _users["kryetar"].Id;

        var dossierA = await InsertDossierAsync($"T-{suffix}-A", formatId, trId, protokollId);
        var dossierB = await InsertDossierAsync($"T-{suffix}-B", formatId, trId, protokollId);

        ActiveCaseId = await InsertCaseAsync(dossierA, $"T-{suffix}-A/1", trId, levelId, courtId, protokollId, "ACTIVE");
        ActiveDossierId = dossierA;
        RegisteredCaseId = await InsertCaseAsync(dossierB, $"T-{suffix}-B/1", trId, levelId, courtId, protokollId, "REGISTERED");

        await AssignAsync("avokat1", "PRIMARY_LAWYER", kryetarId);
        await AssignAsync("avokat2", "SECONDARY_LAWYER", kryetarId);
        await AssignAsync("ndihmes", "LEGAL_ASSISTANT", kryetarId);
        await AssignAsync("jurist", "INSTITUTION_JURIST", kryetarId);

        await InsertAsync("case_institutions",
            "INSERT INTO case_institutions (case_id, institution_id, link_mode, started_by) VALUES (@c, @i, 'MONITORING', @u)",
            ("@c", ActiveCaseId), ("@i", ministryId), ("@u", protokollId));
    }

    private Task<long> AssignAsync(string key, string role, long assignedBy) =>
        InsertAsync("case_assignments",
            "INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by) VALUES (@c, @u, @r, @a)",
            ("@c", ActiveCaseId), ("@u", _users[key].Id), ("@r", role), ("@a", assignedBy));

    private Task<long> InsertDossierAsync(string code, long formatId, long officeId, long openedBy) =>
        InsertAsync("dossiers",
            "INSERT INTO dossiers (unique_code, code_format_id, office_id, opened_by) VALUES (@c, @f, @o, @u)",
            ("@c", code), ("@f", formatId), ("@o", officeId), ("@u", openedBy));

    private Task<long> InsertCaseAsync(long dossierId, string code, long officeId, long levelId, long courtId, long registeredBy, string status) =>
        InsertAsync("cases",
            "INSERT INTO cases (dossier_id, level_seq, case_code, office_id, summons_number, act_number, received_date, " +
            "court_level_id, court_id, object_text, lifecycle_status, registered_by) " +
            "VALUES (@d, 1, @c, @o, 'FL-TEST', 'AKT-TEST', CURDATE(), @l, @ct, 'Test', @s, @u)",
            ("@d", dossierId), ("@c", code), ("@o", officeId), ("@l", levelId), ("@ct", courtId), ("@s", status), ("@u", registeredBy));

    private async Task<long> InsertUserAsync(string subject, string first, string last, long? officeId, long? institutionId, string role)
    {
        var id = await InsertAsync("users",
            "INSERT INTO users (external_subject, email, first_name, last_name, office_id, institution_id) VALUES (@s, @e, @f, @l, @o, @i)",
            ("@s", subject), ("@e", $"{subject}@ash.test"), ("@f", first), ("@l", last), ("@o", officeId), ("@i", institutionId));
        await ExecuteAsync(
            "INSERT INTO user_roles (user_id, role_id) SELECT @u, id FROM roles WHERE code = @r", ("@u", id), ("@r", role));
        _cleanup.Add(("DELETE FROM user_roles WHERE user_id = @id", id));
        return id;
    }

    private async Task<SeedUser> LoadUserAsync(long id)
    {
        long? officeId = null, institutionId = null;
        await using (var cmd = Command("SELECT office_id, institution_id FROM users WHERE id = @id", ("@id", id)))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            await reader.ReadAsync();
            if (!reader.IsDBNull(0)) officeId = reader.GetInt64(0);
            if (!reader.IsDBNull(1)) institutionId = reader.GetInt64(1);
        }

        var roles = await StringsAsync(
            "SELECT r.code FROM user_roles ur JOIN roles r ON r.id = ur.role_id WHERE ur.user_id = @id", id);
        var permissions = await StringsAsync(
            "SELECT DISTINCT p.code FROM user_roles ur JOIN role_permissions rp ON rp.role_id = ur.role_id " +
            "JOIN permissions p ON p.id = rp.permission_id WHERE ur.user_id = @id", id);
        return new SeedUser(id, officeId, institutionId, roles, permissions);
    }

    private async Task<string[]> StringsAsync(string sql, long id)
    {
        var result = new List<string>();
        await using var cmd = Command(sql, ("@id", id));
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return [.. result];
    }

    private async Task<long> InsertAsync(string table, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var cmd = Command(sql, parameters);
        await cmd.ExecuteNonQueryAsync();
        var id = cmd.LastInsertedId;
        _created.Add((table, id));
        return id;
    }

    private async Task ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var cmd = Command(sql, parameters);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var cmd = Command(sql, parameters);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private MySqlCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return cmd;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (sql, id) in _cleanup)
        {
            await TryAsync(sql, id);
        }

        // Reverse creation order respects the foreign keys.
        for (var i = _created.Count - 1; i >= 0; i--)
        {
            await TryAsync($"DELETE FROM {_created[i].Table} WHERE id = @id", _created[i].Id);
        }

        await _connection.DisposeAsync();
    }

    private async Task TryAsync(string sql, long id)
    {
        try
        {
            await ExecuteAsync(sql, ("@id", id));
        }
        catch (MySqlException)
        {
            // Best-effort cleanup: keep deleting the rest.
        }
    }
}

public sealed record SeedUser(long Id, long? OfficeId, long? InstitutionId, string[] Roles, string[] Permissions);
