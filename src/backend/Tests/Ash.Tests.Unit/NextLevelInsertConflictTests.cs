using Ash.Modules.Cases.Application.RegisterNextLevel;
using Ash.SharedKernel.Results;
using Shouldly;

namespace Ash.Tests.Unit;

/// <summary>
/// The insert-race mapping of next-level, without relying on timing: a parallel request that took the level first
/// must give the "already continued" 409, never a 500.
/// </summary>
public class NextLevelInsertConflictTests
{
    private const string Code = "2026-483915/2";

    [Theory]
    [InlineData("Duplicate entry '17-2' for key 'cases.uq_cases_dossier_level'")]
    [InlineData("Duplicate entry '2026-483915/2' for key 'cases.uq_cases_code'")]
    public void Duplicate_level_or_code_maps_to_already_continued(string message)
    {
        var error = NextLevelInsertConflict.Map(NextLevelInsertConflict.DuplicateKey, message, Code);

        error.ShouldNotBeNull();
        error.Type.ShouldBe(ErrorType.Conflict);
        error.Code.ShouldBe("case.already_continued");
        error.Message.ShouldBe("Kjo çështje ka vazhduar tashmë në 2026-483915/2");
    }

    [Fact]
    public void Deadlock_maps_to_already_continued()
    {
        var error = NextLevelInsertConflict.Map(NextLevelInsertConflict.Deadlock,
            "Deadlock found when trying to get lock; try restarting transaction", Code);

        error.ShouldNotBeNull();
        error.Code.ShouldBe("case.already_continued");
    }

    [Theory]
    [InlineData(1062, "Duplicate entry 'x' for key 'cases.uq_cases_public'")] // another unique key: a real error
    [InlineData(1452, "Cannot add or update a child row: a foreign key constraint fails")]
    [InlineData(1205, "Lock wait timeout exceeded")]
    public void Other_database_errors_are_not_mapped(int number, string message) =>
        NextLevelInsertConflict.Map(number, message, Code).ShouldBeNull();
}
