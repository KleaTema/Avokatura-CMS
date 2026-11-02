using Ash.Modules.Cases.Application.AssignLawyers;
using Shouldly;

namespace Ash.Tests.Unit;

/// <summary>
/// The insert-race mapping of the lawyer assignment, without relying on timing: a parallel assignment that took the
/// slot first must give the "already assigned" 409, never a 500.
/// </summary>
public class AssignmentInsertConflictTests
{
    [Theory]
    [InlineData("Duplicate entry '42:PRIMARY_LAWYER' for key 'case_assignments.uq_ca_lawyer_slot'")]
    [InlineData("Duplicate entry '42:7' for key 'case_assignments.uq_ca_user_slot'")]
    public void Taken_lawyer_or_user_slot_is_a_conflict(string message) =>
        AssignmentInsertConflict.IsSlotTaken(AssignmentInsertConflict.DuplicateKey, message).ShouldBeTrue();

    [Fact]
    public void Deadlock_is_a_conflict() =>
        AssignmentInsertConflict.IsSlotTaken(AssignmentInsertConflict.Deadlock,
            "Deadlock found when trying to get lock; try restarting transaction").ShouldBeTrue();

    [Theory]
    [InlineData(1062, "Duplicate entry 'x' for key 'case_assignments.PRIMARY'")] // another unique key: a real error
    [InlineData(1452, "Cannot add or update a child row: a foreign key constraint fails")]
    [InlineData(1205, "Lock wait timeout exceeded")]
    public void Other_database_errors_are_not_mapped(int number, string message) =>
        AssignmentInsertConflict.IsSlotTaken(number, message).ShouldBeFalse();
}
