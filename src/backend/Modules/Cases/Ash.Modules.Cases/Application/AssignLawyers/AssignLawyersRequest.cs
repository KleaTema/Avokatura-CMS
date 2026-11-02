namespace Ash.Modules.Cases.Application.AssignLawyers;

/// <summary>
/// POST /api/cases/{id}/assignment. <see cref="Priority"/> (NONE | LOW | MEDIUM | HIGH) is optional and needs
/// case.set_priority_any; <see cref="RowVersion"/> is the case's row_version as the client read it.
/// </summary>
public sealed record AssignLawyersRequest(
    Guid? PrimaryUserPublicId,
    Guid? SecondaryUserPublicId,
    string? Priority,
    uint? RowVersion);
