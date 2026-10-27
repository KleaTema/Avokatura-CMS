using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Port of the 11 negative checks in docs/sql/tests/run_tests.sh: each statement MUST be rejected by the database.
/// The data is the life-cycle scenario (docs/sql/tests/test_scenario.sql) built by <see cref="Seed"/>.
/// </summary>
public class DatabaseConstraintTests(MySqlFixture db) : IntegrationTest(db)
{
    private Seed _seed = null!;
    private LifeCycle _s = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _seed = await Seed.CreateAsync(Db.AppConnectionString);
        _s = await _seed.LifeCycleAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await _seed.DisposeAsync();
        await base.DisposeAsync();
    }

    private async Task ShouldBeRejected(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await Should.ThrowAsync<MySqlException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public Task Second_current_primary_lawyer_on_a_case_is_rejected() =>
        ShouldBeRejected(
            "INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by) VALUES (@c, @u, 'PRIMARY_LAWYER', @a)",
            ("@c", _s.FirstInstanceCaseId), ("@u", _s.HeadUserId), ("@a", _s.ProtocolUserId));

    [Fact]
    public Task Same_user_twice_on_the_same_case_is_rejected() =>
        ShouldBeRejected(
            "INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by) VALUES (@c, @u, 'LEGAL_ASSISTANT', @a)",
            ("@c", _s.FirstInstanceCaseId), ("@u", _s.PrimaryLawyerId), ("@a", _s.ProtocolUserId));

    [Fact]
    public Task History_row_cannot_be_updated() =>
        ShouldBeRejected("UPDATE case_history_events SET summary = 'x'");

    [Fact]
    public Task History_row_cannot_be_deleted() =>
        ShouldBeRejected("DELETE FROM case_history_events");

    [Fact]
    public Task Subcategory_must_belong_to_its_category() =>
        ShouldBeRejected(
            "UPDATE cases SET category_id = (SELECT id FROM case_categories WHERE code = 'DELEGIM'), " +
            "subcategory_id = (SELECT id FROM case_subcategories WHERE code = 'KOD_A_AVSH') WHERE id = @c",
            ("@c", _s.AppealCaseId));

    [Fact]
    public Task Decision_cannot_be_final_and_appealed() =>
        ShouldBeRejected("UPDATE case_decisions SET is_final = 1 WHERE case_id = @c", ("@c", _s.FirstInstanceCaseId));

    [Fact]
    public Task Closed_case_must_have_closed_at() =>
        ShouldBeRejected("UPDATE cases SET lifecycle_status = 'CLOSED' WHERE id = @c", ("@c", _s.AppealCaseId));

    [Fact]
    public Task Institution_party_must_reference_an_institution() =>
        ShouldBeRejected(
            "INSERT INTO case_parties (case_id, party_role_id, party_kind, display_name, created_by) " +
            "SELECT @c, id, 'INSTITUTION', 'Ministria X', @u FROM party_roles ORDER BY id LIMIT 1",
            ("@c", _s.AppealCaseId), ("@u", _s.ProtocolUserId));

    [Fact]
    public Task Duplicate_level_in_the_same_dossier_is_rejected() =>
        ShouldBeRejected(
            "INSERT INTO cases (dossier_id, level_seq, case_code, office_id, summons_number, act_number, received_date, " +
            "court_level_id, court_id, object_text, registered_by) " +
            "SELECT dossier_id, 2, 'dup', office_id, 'a', 'b', '2027-01-01', court_level_id, court_id, 'o', registered_by FROM cases WHERE id = @c",
            ("@c", _s.FirstInstanceCaseId));

    [Fact]
    public Task Second_case_chat_for_the_same_case_is_rejected() =>
        ShouldBeRejected(
            "INSERT INTO ash_chat.conversations (conversation_type, case_id, created_by) VALUES ('CASE', @c, @u)",
            ("@c", _s.AppealCaseId), ("@u", _s.ProtocolUserId));

    [Fact]
    public Task Invalid_priority_value_is_rejected() =>
        ShouldBeRejected("UPDATE cases SET priority = 'URGENT' WHERE id = @c", ("@c", _s.AppealCaseId));
}
