namespace Ash.Modules.Cases.Application.ChangeJudge;

/// <summary>PATCH /api/cases/{id}/judge — judges keep their numeric id (reference data).</summary>
public sealed record ChangeJudgeRequest(long? JudgeId, uint? RowVersion);
