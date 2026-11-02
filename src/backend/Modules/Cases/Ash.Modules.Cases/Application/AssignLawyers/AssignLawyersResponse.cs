namespace Ash.Modules.Cases.Application.AssignLawyers;

/// <summary>The case after the assignment; <see cref="RowVersion"/> is the new value for the next change.</summary>
public sealed record AssignLawyersResponse(Guid CasePublicId, string Status, string Priority, DateTime AssignedAt, uint RowVersion);
