using MySql.Data.MySqlClient;

namespace Ash.Tests.Integration.Infrastructure;

/// <summary>Ids of the life-cycle scenario built by <see cref="Seed.LifeCycleAsync"/>.</summary>
public sealed record LifeCycle(
    long OfficeId,
    long InstitutionId,
    long DossierId,
    string DossierCode,
    long FirstInstanceCaseId,
    long AppealCaseId,
    long ProtocolUserId,
    long HeadUserId,
    long PrimaryLawyerId,
    long SecondaryLawyerId,
    long AssistantId,
    long JuristId,
    long InstitutionUserId);

/// <summary>
/// Builder for test data (raw SQL, unique codes per instance). Port of docs/sql/tests/test_scenario.sql.
/// Business tables are emptied by <see cref="MySqlFixture.ResetAsync"/>; offices, users and courts created here are harmless leftovers.
/// </summary>
public sealed class Seed : IAsyncDisposable
{
    private readonly MySqlConnection _connection;

    private Seed(MySqlConnection connection) => _connection = connection;

    public string Suffix { get; } = Guid.NewGuid().ToString("N")[..8];

    public static async Task<Seed> CreateAsync(string connectionString)
    {
        var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        return new Seed(connection);
    }

    public Task<long> OfficeAsync(string name, string type = "CENTRAL") =>
        InsertAsync("INSERT INTO offices (code, name, office_type) VALUES (@c, @n, @t)",
            ("@c", $"T{Suffix}"), ("@n", name), ("@t", type));

    public Task<long> InstitutionAsync(string name, string shortName, string type = "MINISTRY") =>
        InsertAsync("INSERT INTO institutions (name, short_name, institution_type) VALUES (@n, @s, @t)",
            ("@n", name), ("@s", shortName), ("@t", type));

    public async Task<long> UserAsync(
        string key, string first, string last, long? officeId, long? institutionId, params string[] roles)
    {
        var id = await InsertAsync(
            "INSERT INTO users (external_subject, email, first_name, last_name, office_id, institution_id) VALUES (@s, @e, @f, @l, @o, @i)",
            ("@s", $"sub-{key}-{Suffix}"), ("@e", $"{key}-{Suffix}@ash.test"), ("@f", first), ("@l", last),
            ("@o", officeId), ("@i", institutionId));
        foreach (var role in roles)
        {
            await InsertAsync("INSERT INTO user_roles (user_id, role_id) SELECT @u, id FROM roles WHERE code = @r",
                ("@u", id), ("@r", role));
        }

        return id;
    }

    public Task<long> CourtAsync(string name, string levelCode) =>
        InsertAsync("INSERT INTO courts (name, court_level_id, city) SELECT @n, id, 'Tiranë' FROM court_levels WHERE code = @l",
            ("@n", name), ("@l", levelCode));

    public Task<long> JudgeAsync(string first, string last, long courtId) =>
        InsertAsync("INSERT INTO judges (first_name, last_name, court_id) VALUES (@f, @l, @c)",
            ("@f", first), ("@l", last), ("@c", courtId));

    /// <summary>Calls create_dossier (as the API does inside the registration transaction).</summary>
    public async Task<(long Id, string Code)> DossierAsync(long officeId, long userId)
    {
        await ExecuteAsync("CALL create_dossier(@o, @u, @d, @c)", ("@o", officeId), ("@u", userId));
        await using var cmd = Command("SELECT @d, @c");
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (Convert.ToInt64(reader.GetValue(0)), reader.GetString(1));
    }

    public Task<long> RegisteredCaseAsync(
        long dossierId, int levelSeq, string caseCode, long officeId, string courtLevelCode, long courtId, long? judgeId,
        string summonsNumber, string actNumber, DateTime receivedDate, string objectText, long registeredBy) =>
        InsertAsync(
            "INSERT INTO cases (dossier_id, level_seq, case_code, office_id, summons_number, act_number, received_date, " +
            "court_level_id, court_id, judge_id, object_text, registered_by) " +
            "SELECT @d, @ls, @cc, @o, @sn, @an, @rd, id, @ct, @j, @ot, @u FROM court_levels WHERE code = @l",
            ("@d", dossierId), ("@ls", levelSeq), ("@cc", caseCode), ("@o", officeId), ("@sn", summonsNumber),
            ("@an", actNumber), ("@rd", receivedDate), ("@ct", courtId), ("@j", judgeId), ("@ot", objectText),
            ("@u", registeredBy), ("@l", courtLevelCode));

    public Task<long> AssignAsync(long caseId, long userId, string caseRole, long assignedBy) =>
        InsertAsync("INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by) VALUES (@c, @u, @r, @a)",
            ("@c", caseId), ("@u", userId), ("@r", caseRole), ("@a", assignedBy));

    public Task<long> HistoryAsync(long dossierId, long caseId, string eventType, long actorId, string summary) =>
        InsertAsync(
            "INSERT INTO case_history_events (dossier_id, case_id, event_type, actor_user_id, summary) VALUES (@d, @c, @e, @a, @s)",
            ("@d", dossierId), ("@c", caseId), ("@e", eventType), ("@a", actorId), ("@s", summary));

    /// <summary>
    /// One dossier through its whole life: X/1 first instance (registered, assigned, hearing, document, lost + appealed, closed),
    /// X/2 appeal with a lawyer transfer, a case chat and an audit row.
    /// </summary>
    public async Task<LifeCycle> LifeCycleAsync()
    {
        var office = await OfficeAsync($"Zyra Qendrore Tiranë {Suffix}");
        var inst = await InstitutionAsync($"Ministria e Financave dhe Ekonomisë {Suffix}", "MFE");

        var prot = await UserAsync("prot", "Arta", "Protokolli", office, null, "PROTOCOL");
        var head = await UserAsync("head", "Besnik", "Kryetari", office, null, "OFFICE_HEAD", "STATE_LAWYER");
        var av1 = await UserAsync("av1", "Drita", "Avokatja", office, null, "STATE_LAWYER");
        var av2 = await UserAsync("av2", "Ermal", "Avokati", office, null, "STATE_LAWYER");
        var nd = await UserAsync("nd", "Fiona", "Ndihmësja", office, null, "LEGAL_ASSISTANT");
        var instu = await UserAsync("instu", "Hana", "Institucioni", null, inst, "INSTITUTION_USER");
        var jur = await UserAsync("jur", "Gent", "Juristi", null, inst, "INSTITUTION_JURIST");
        await ExecuteAsync("UPDATE offices SET head_user_id = @h WHERE id = @o", ("@h", head), ("@o", office));

        var court1 = await CourtAsync($"Gjykata e Shkallës së Parë Tiranë (test) {Suffix}", "FIRST_INSTANCE");
        var court2 = await CourtAsync($"Gjykata e Apelit Tiranë (test) {Suffix}", "APPEAL");
        var judge1 = await JudgeAsync("Ilir", "Gjyqtari", court1);
        var judge2 = await JudgeAsync("Klodiana", "Gjyqtarja", court2);

        // 1. Fletëthirrja arrives: new dossier + X/1
        var (dossier, code) = await DossierAsync(office, prot);
        var case1 = await RegisteredCaseAsync(dossier, 1, $"{code}/1", office, "FIRST_INSTANCE", court1, judge1,
            "FL-778", "3312-2026", new DateTime(2026, 10, 20), "Detyrim pagimi i shpërblimit për pronën", prot);
        await ExecuteAsync(
            "INSERT INTO case_parties (case_id, party_role_id, party_kind, display_name, created_by) " +
            "SELECT @c, id, 'NATURAL', 'Petrit Hoxha', @u FROM party_roles WHERE code = 'PADITES'",
            ("@c", case1), ("@u", prot));
        await ExecuteAsync(
            "INSERT INTO case_parties (case_id, party_role_id, party_kind, institution_id, display_name, created_by) " +
            "SELECT @c, id, 'INSTITUTION', @i, 'Ministria e Financave dhe Ekonomisë', @u FROM party_roles WHERE code = 'I_PADITUR'",
            ("@c", case1), ("@i", inst), ("@u", prot));
        await ExecuteAsync(
            "INSERT INTO case_institutions (case_id, institution_id, link_mode, started_by) VALUES (@c, @i, 'MONITORING', @u)",
            ("@c", case1), ("@i", inst), ("@u", prot));
        await HistoryAsync(dossier, case1, "CASE_REGISTERED", prot, "Çështja u regjistrua dhe kaloi te Kryetari i Zyrës");

        // 2. Kryetari assigns primary + secondary, lawyer adds assistant, institution user adds the jurist
        await AssignAsync(case1, av1, "PRIMARY_LAWYER", head);
        await AssignAsync(case1, av2, "SECONDARY_LAWYER", head);
        await AssignAsync(case1, nd, "LEGAL_ASSISTANT", av1);
        await AssignAsync(case1, jur, "INSTITUTION_JURIST", instu);
        await ExecuteAsync(
            "UPDATE cases SET lifecycle_status = 'ACTIVE', assigned_at = NOW(3), priority = 'HIGH', " +
            "category_id = (SELECT id FROM case_categories WHERE code = 'NDJEKJE'), " +
            "subcategory_id = (SELECT id FROM case_subcategories WHERE code = 'KOD_A_AVSH'), " +
            "claim_amount = 2500000, claim_currency = 'ALL', row_version = row_version + 1 WHERE id = @c",
            ("@c", case1));

        // 3. Hearing + document
        var hearing = await InsertAsync(
            "INSERT INTO case_hearings (case_id, hearing_type_id, scheduled_at, status, description, created_by) " +
            "SELECT @c, id, '2026-11-12 08:30:00', 'SCHEDULED', 'Seanca e parë përgatitore', @u FROM hearing_types WHERE code = 'PERGATITORE'",
            ("@c", case1), ("@u", av1));
        await ExecuteAsync(
            "UPDATE cases SET next_hearing_at = '2026-11-12 08:30:00', last_hearing_id = @h, first_data_entry_at = NOW(3) WHERE id = @c",
            ("@h", hearing), ("@c", case1));
        await ExecuteAsync(
            "INSERT INTO documents (dossier_id, case_id, document_type_id, title, original_file_name, content_type, size_bytes, " +
            "sha256, storage_bucket, storage_key, scan_status, uploaded_by) " +
            "SELECT @d, @c, id, 'Padia', 'padia.pdf', 'application/pdf', 482113, UNHEX(SHA2('padia-content', 256)), " +
            "'ash-documents', CONCAT('2026/10/', UUID()), 'CLEAN', @u FROM document_types WHERE code = 'PADI_PROVA'",
            ("@d", dossier), ("@c", case1), ("@u", av1));

        // 4. Decision at first instance: lost, appealed
        await ExecuteAsync(
            "INSERT INTO case_decisions (case_id, result_id, decision_number, decision_date, dispositive, lost_amount, " +
            "caused_damage_amount, court_costs_amount, is_appealed, recorded_by) " +
            "SELECT @c, id, '1450', '2027-03-02', 'Pranimi i padisë', 2500000, 2500000, 30000, 1, @u FROM decision_results WHERE code = 'HUMBUR'",
            ("@c", case1), ("@u", av1));
        await ExecuteAsync(
            "UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = NOW(3), closed_by = @u, next_hearing_at = NULL WHERE id = @c",
            ("@u", av1), ("@c", case1));

        // 5. New summons from the Court of Appeal: X/2 continues the dossier
        var case2 = await InsertAsync(
            "INSERT INTO cases (dossier_id, level_seq, case_code, previous_case_id, office_id, summons_number, act_number, " +
            "received_date, court_level_id, court_id, judge_id, object_text, registered_by) " +
            "SELECT c.dossier_id, c.level_seq + 1, CONCAT(d.unique_code, '/', c.level_seq + 1), c.id, c.office_id, " +
            "'FL-1021', '88-2027', '2027-05-10', cl.id, @ct, @j, c.object_text, @u " +
            "FROM cases c JOIN dossiers d ON d.id = c.dossier_id JOIN court_levels cl ON cl.code = 'APPEAL' WHERE c.id = @c",
            ("@ct", court2), ("@j", judge2), ("@u", prot), ("@c", case1));
        await ExecuteAsync(
            "INSERT INTO case_parties (case_id, party_role_id, party_kind, institution_id, display_name, identifier, created_by) " +
            "SELECT @n, party_role_id, party_kind, institution_id, display_name, identifier, @u FROM case_parties " +
            "WHERE case_id = @c AND deleted_at IS NULL",
            ("@n", case2), ("@u", prot), ("@c", case1));
        await ExecuteAsync(
            "INSERT INTO case_institutions (case_id, institution_id, link_mode, started_by) " +
            "SELECT @n, institution_id, link_mode, @u FROM case_institutions WHERE case_id = @c AND ended_at IS NULL",
            ("@n", case2), ("@u", prot), ("@c", case1));

        // 6. Transfer: Kryetari replaces the primary lawyer on X/2
        await AssignAsync(case2, av1, "PRIMARY_LAWYER", head);
        await ExecuteAsync(
            "UPDATE case_assignments SET ended_at = NOW(3), ended_by = @h, end_reason = 'TRANSFERRED' " +
            "WHERE case_id = @c AND case_role = 'PRIMARY_LAWYER' AND ended_at IS NULL",
            ("@h", head), ("@c", case2));
        await AssignAsync(case2, av2, "PRIMARY_LAWYER", head);
        await ExecuteAsync("UPDATE cases SET lifecycle_status = 'ACTIVE', assigned_at = NOW(3) WHERE id = @c", ("@c", case2));

        // 7. Chat + audit sanity
        var conversation = await InsertAsync(
            "INSERT INTO ash_chat.conversations (conversation_type, case_id, title, created_by) VALUES ('CASE', @c, @t, @u)",
            ("@c", case2), ("@t", $"{code}/2"), ("@u", av2));
        await ExecuteAsync(
            "INSERT INTO ash_chat.conversation_participants (conversation_id, user_id) VALUES (@v, @a), (@v, @j)",
            ("@v", conversation), ("@a", av2), ("@j", jur));
        await ExecuteAsync(
            "INSERT INTO ash_chat.messages (conversation_id, sender_user_id, body, created_at) VALUES " +
            "(@v, @a, 'Ju lutem dërgoni vendimin e shkallës së parë.', '2026-11-15 09:00:00'), " +
            "(@v, @j, 'Dërguar sot.', '2026-12-02 10:00:00')",
            ("@v", conversation), ("@a", av2), ("@j", jur));
        await ExecuteAsync(
            "INSERT INTO ash_audit.audit_log (user_id, user_display, action, entity_type, entity_id, case_id, ip_address) " +
            "VALUES (@a, 'Ermal Avokati', 'CASE_VIEW', 'CASE', @c, @c, INET6_ATON('10.20.1.15'))",
            ("@a", av2), ("@c", case2));

        return new LifeCycle(office, inst, dossier, code, case1, case2, prot, head, av1, av2, nd, jur, instu);
    }

    private async Task<long> InsertAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var cmd = Command(sql, parameters);
        await cmd.ExecuteNonQueryAsync();
        return cmd.LastInsertedId;
    }

    private async Task ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var cmd = Command(sql, parameters);
        await cmd.ExecuteNonQueryAsync();
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

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
