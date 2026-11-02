namespace Ash.Modules.Cases.Application.SetSecurityMeasure;

/// <summary>
/// PUT /api/cases/{id}/security-measure — active: decisionNumber and decisionDate required, dispositive optional;
/// { active: false } lifts the measure (the row is kept).
/// </summary>
public sealed record SetSecurityMeasureRequest(
    bool? Active,
    string? DecisionNumber,
    DateOnly? DecisionDate,
    string? Dispositive,
    uint? RowVersion);
